using System.Security.Claims;
using Googols.Application.Abstractions;
using Googols.Domain.Common;
using Googols.Domain.Gamification;
using Googols.Infrastructure.Ai;
using Googols.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Plumbing;

public static class Policies
{
    public const string Student = "student";
    public const string Parent = "parent";
    public const string Teacher = "teacher";
    public const string ContentManager = "content-manager";
    public const string Admin = "admin";
}

public sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public bool IsInRole(string role) => http.HttpContext?.User.IsInRole(role) ?? false;
}

/// <summary>Maps exceptions to RFC 7807 problem details without leaking stack traces.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            AiNotConfiguredException => (StatusCodes.Status503ServiceUnavailable, "The AI teacher is not available right now."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "This record was changed by someone else. Reload and try again."),
            _ => (StatusCodes.Status500InternalServerError, "Something went wrong."),
        };
        if (status == StatusCodes.Status500InternalServerError)
        {
            log.LogError(ex, "Unhandled exception for {Path}", ctx.Request.Path);
        }
        ctx.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = new ProblemDetails { Status = status, Title = title },
        });
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), geolocation=(), microphone=(self)";
        // API responses need nothing; the React app needs its own scripts, styles, images and the SignalR socket.
        h["Content-Security-Policy"] = ctx.Request.Path.StartsWithSegments("/api")
            ? "default-src 'none'; frame-ancestors 'none'"
            // Razorpay Checkout runs its script and an iframe from its own domains; card/UPI details never reach Googols.
            : "default-src 'self'; script-src 'self' https://checkout.razorpay.com; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://*.razorpay.com; media-src 'self' blob:; font-src 'self'; " +
              "connect-src 'self' wss: https://*.razorpay.com; frame-src https://api.razorpay.com https://checkout.razorpay.com; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        await next();
    });
}

/// <summary>Each student joins a group named after their profile id, so events reach only them.</summary>
[Authorize]
public sealed class LearningHub(GoogolsDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.UserIdentifier, out var userId))
        {
            var studentId = await db.StudentProfiles.Where(s => s.UserId == userId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
            if (studentId is { } id)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"student:{id}");
            }
        }
        await base.OnConnectedAsync();
    }
}

public sealed class SignalRNotifier(IHubContext<LearningHub> hub) : IRealtimeNotifier
{
    public Task NotifyStudentAsync(Guid studentId, string eventName, object payload, CancellationToken ct) =>
        hub.Clients.Group($"student:{studentId}").SendAsync(eventName, payload, ct);
}

public static class StartupTasks
{
    public static async Task SeedRolesAndRulesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<GlobalExceptionHandler>>();
        var db = scope.ServiceProvider.GetRequiredService<GoogolsDbContext>();
        try
        {
            // Apply pending EF Core migrations on start (app setting Database__MigrateOnStartup=true).
            // Migrations are additive and idempotent; a failure stops here and is logged, the app still starts.
            if (scope.ServiceProvider.GetRequiredService<IConfiguration>().GetValue("Database:MigrateOnStartup", false))
            {
                await db.Database.MigrateAsync();
            }
            if (!await db.Database.CanConnectAsync())
            {
                log.LogWarning("Database not reachable; skipping role and XP-rule seeding. Run `dotnet ef database update` first.");
                return;
            }
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            foreach (var role in Roles.All)
            {
                if (!await roles.RoleExistsAsync(role))
                {
                    await roles.CreateAsync(new IdentityRole<Guid>(role));
                }
            }
            // Optional: make one known account a SuperAdmin (app setting Admin__BootstrapEmail). Runs on every start,
            // so registering that email and restarting the app is enough. Never removes roles.
            var bootstrapEmail = scope.ServiceProvider.GetRequiredService<IConfiguration>()["Admin:BootstrapEmail"];
            if (!string.IsNullOrWhiteSpace(bootstrapEmail))
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<global::Googols.Infrastructure.Identity.AppUser>>();
                var admin = await users.FindByEmailAsync(bootstrapEmail);
                if (admin is not null && !await users.IsInRoleAsync(admin, Roles.SuperAdmin))
                {
                    await users.AddToRoleAsync(admin, Roles.SuperAdmin);
                    log.LogWarning("Granted SuperAdmin to the bootstrap admin account.");
                }
            }

            var existing = await db.XpRules.Select(r => r.ActivityCode).ToListAsync();
            foreach (var (code, points) in XpRule.Defaults.Where(d => !existing.Contains(d.Key)))
            {
                db.XpRules.Add(new XpRule { ActivityCode = code, Points = points });
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Startup seeding skipped (schema may not be migrated yet).");
        }
    }
}
