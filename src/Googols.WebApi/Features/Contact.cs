using System.Net.Mail;
using System.Text.Encodings.Web;
using Googols.Application.Abstractions;
using Googols.Domain.Site;
using Googols.Infrastructure.Persistence;
using Googols.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

/// <summary>Public contact form, plus an admin inbox. Messages are stored; an email alert is sent when SMTP is configured.</summary>
public static class ContactEndpoints
{
    public sealed record ContactRequest(string Name, string Email, string? Phone, string Topic, string Message, string? Website);
    public sealed record ContactDto(Guid Id, string Name, string Email, string? Phone, string Topic, string Message, bool Handled, DateTime CreatedAtUtc);
    public sealed record HandledRequest(bool Handled);

    private static readonly string[] Topics = ["general", "school", "parent", "support", "partnership"];

    public static void MapContactEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapPost("/contact", async (ContactRequest req, GoogolsDbContext db, IEmailSender email, IConfiguration config, CancellationToken ct) =>
        {
            // Honeypot: real visitors never see or fill the "website" field; bots usually do.
            if (!string.IsNullOrEmpty(req.Website))
            {
                return Results.Accepted();
            }
            var errors = new Dictionary<string, string[]>();
            var name = req.Name?.Trim() ?? "";
            var mail = req.Email?.Trim() ?? "";
            var message = req.Message?.Trim() ?? "";
            var topic = (req.Topic ?? "general").Trim().ToLowerInvariant();
            if (name.Length is < 2 or > 100) errors["name"] = ["Please tell us your name."];
            if (mail.Length > 256 || !MailAddress.TryCreate(mail, out _)) errors["email"] = ["Please enter a valid email address."];
            if (req.Phone is { Length: > 20 }) errors["phone"] = ["Phone number is too long."];
            if (!Topics.Contains(topic)) errors["topic"] = ["Choose a topic."];
            if (message.Length is < 10 or > 2000) errors["message"] = ["Please write a message of 10 to 2,000 characters."];
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }
            var row = new ContactMessage { Name = name, Email = mail, Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(), Topic = topic, Message = message };
            db.ContactMessages.Add(row);
            await db.SaveChangesAsync(ct);

            if (config["Contact:NotifyEmail"] is { Length: > 0 } to)
            {
                var h = HtmlEncoder.Default;
                await email.SendAsync(to, $"Googols contact: {topic} from {name}",
                    EmailTemplates.Wrap("New contact message", $"<p><strong>{h.Encode(name)}</strong> &lt;{h.Encode(mail)}&gt; {h.Encode(row.Phone ?? "")}</p><p>{h.Encode(message).Replace("\n", "<br>")}</p>"), ct);
            }
            return Results.Created($"/api/v1/admin/messages/{row.Id}", new { row.Id });
        }).WithTags("Website").AllowAnonymous().RequireRateLimiting("auth");

        var admin = v1.MapGroup("/admin/messages").WithTags("Admin").RequireAuthorization(Plumbing.Policies.Admin);
        admin.MapGet("", async (bool? handled, GoogolsDbContext db, CancellationToken ct) =>
        {
            var q = db.ContactMessages.AsNoTracking();
            if (handled is { } h)
            {
                q = q.Where(m => m.Handled == h);
            }
            return await q.OrderByDescending(m => m.CreatedAtUtc).Take(200)
                .Select(m => new ContactDto(m.Id, m.Name, m.Email, m.Phone, m.Topic, m.Message, m.Handled, m.CreatedAtUtc)).ToListAsync(ct);
        });
        admin.MapPut("/{id:guid}", async (Guid id, HandledRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            var n = await db.ContactMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u => u.SetProperty(m => m.Handled, req.Handled), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
