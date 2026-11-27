using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Googols.Application;
using Microsoft.AspNetCore.Authentication;
using Googols.Application.Abstractions;
using Googols.Domain.Common;
using Googols.Infrastructure;
using Googols.Infrastructure.Identity;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Features;
using Googols.WebApi.Plumbing;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);

// ---- Identity: bearer tokens + refresh tokens for the React SPA (no API keys in the browser).
builder.Services
    .AddIdentityApiEndpoints<AppUser>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedEmail = builder.Configuration.GetValue("Auth:RequireConfirmedEmail", false); // turn on once an email sender is configured (Phase 4)
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<GoogolsDbContext>();

// "Continue with Google" (app settings Authentication__Google__ClientId / __ClientSecret). Off when not configured.
var googleId = config["Authentication:Google:ClientId"];
var googleSecret = config["Authentication:Google:ClientSecret"];
var googleEnabled = !string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret);
if (googleEnabled)
{
    builder.Services.AddAuthentication().AddGoogle(ExternalLoginEndpoints.GoogleScheme, o =>
    {
        o.ClientId = googleId!;
        o.ClientSecret = googleSecret!;
        o.SignInScheme = IdentityConstants.ExternalScheme;
        o.ClaimActions.MapJsonKey("email_verified", "verified_email"); // userinfo v2 field
        o.ClaimActions.MapJsonKey("email_verified", "email_verified");
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
    });
}

// App Service terminates HTTPS in front of the app; trust its forwarded headers so redirect URLs use https.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Role changes made by an admin (or a lock) reach existing sessions within a minute.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

// SignalR over WebSockets can't send an Authorization header; accept the bearer token from the query string on /hubs only.
builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, o => o.Events = new BearerTokenEvents
{
    OnMessageReceived = ctx =>
    {
        var token = ctx.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
        {
            ctx.Token = token;
        }
        return Task.CompletedTask;
    },
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Student, p => p.RequireRole(Roles.Student))
    .AddPolicy(Policies.Parent, p => p.RequireRole(Roles.Parent))
    .AddPolicy(Policies.Teacher, p => p.RequireRole(Roles.Teacher, Roles.SchoolAdmin))
    .AddPolicy(Policies.ContentManager, p => p.RequireRole(Roles.ContentManager, Roles.Admin, Roles.SuperAdmin))
    .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin, Roles.SuperAdmin))
    .AddPolicy(Policies.SchoolAdmin, p => p.RequireRole(Roles.SchoolAdmin));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<AiTeacherService>();
builder.Services.AddScoped<AiUsageRecorder>();
builder.Services.AddScoped<MasteryService>();
builder.Services.AddScoped<ExamService>();
builder.Services.AddScoped<SchoolAccess>();
builder.Services.AddScoped<WeeklyReportBuilder>();
builder.Services.AddHostedService<WeeklyReportSender>();
builder.Services.AddScoped<XpLedgerService>();
builder.Services.AddScoped<StudentAccess>();
builder.Services.AddScoped<LessonIndexer>();
builder.Services.AddScoped<LearningActivityService>();
builder.Services.AddScoped<BadgeService>();
builder.Services.AddScoped<EntitlementService>();

// ---- Realtime
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();

// ---- API plumbing
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
// Enums travel as names ("PersonalTeacher", "ApprovedMaterial"), matching the React client's types.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<GoogolsDbContext>("sql");

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Sign-in, sign-up, password and code-entry endpoints: 60 a minute per IP (a whole school can share one IP;
    // each account also locks after 5 wrong passwords). Reading "who am I" on every page load is not limited.
    o.AddPolicy("auth", ctx => HttpMethods.IsGet(ctx.Request.Method) && ctx.Request.Path.StartsWithSegments("/api/v1/auth/manage/info")
        ? RateLimitPartition.GetNoLimiter("whoami")
        : RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("ai", ctx => RateLimitPartition.GetTokenBucketLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new TokenBucketRateLimiterOptions { TokenLimit = 6, TokensPerPeriod = 6, ReplenishmentPeriod = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("speech", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

var spaOrigins = config.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(spaOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// ---- Observability: OpenTelemetry, exported to Azure Monitor when a connection string is present, else OTLP.
var otel = builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
if (!string.IsNullOrWhiteSpace(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    otel.UseAzureMonitor();
}
else if (!string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    otel.WithTracing(t => t.AddOtlpExporter()).WithMetrics(m => m.AddOtlpExporter());
}

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
}

var v1 = app.MapGroup("/api/v1");
var auth = v1.MapGroup("/auth").WithTags("Auth");
auth.MapIdentityApi<AppUser>().RequireRateLimiting("auth");
auth.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
{
    await signIn.SignOutAsync(); // clears the Identity cookie used by the React app
    return Results.NoContent();
}).RequireAuthorization();
v1.MapCurriculumEndpoints();
v1.MapStudentEndpoints();
v1.MapAiTeacherEndpoints();
v1.MapGamificationEndpoints();
v1.MapAdminCurriculumEndpoints();
v1.MapAccountEndpoints();
v1.MapParentEndpoints();
v1.MapLearningEndpoints();
v1.MapQuizEndpoints();
v1.MapLeaderboardEndpoints();
v1.MapTeacherEndpoints();
v1.MapBillingEndpoints();
v1.MapAdminPlatformEndpoints();
v1.MapExternalLoginEndpoints(googleEnabled);
v1.MapContactEndpoints();
v1.MapAiQualityEndpoints();
v1.MapExamEndpoints();
v1.MapProgressEndpoints();
v1.MapAssessmentAdminEndpoints();
v1.MapHomeworkEndpoints();
v1.MapWeeklyReportEndpoints();
v1.MapSchoolEndpoints();

app.MapHub<LearningHub>("/hubs/learning");
app.MapHealthChecks("/health");

// The React build is copied into wwwroot at publish time, so the SPA and API share one origin.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await StartupTasks.SeedRolesAndRulesAsync(app.Services);

app.Run();

public partial class Program;
