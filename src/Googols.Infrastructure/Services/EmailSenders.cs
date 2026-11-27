using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Googols.Application.Abstractions;
using Googols.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Googols.Infrastructure.Services;

/// <summary>
/// SMTP settings (works with Azure Communication Services SMTP, SendGrid, Zoho, Gmail app passwords…).
/// App settings: Email__Smtp__Host, Email__Smtp__Port, Email__Smtp__User, Email__Smtp__Password, Email__From.
/// </summary>
public sealed class EmailOptions
{
    public const string Section = "Email";
    public string? From { get; set; }
    public string FromName { get; set; } = "Googols";
    public SmtpSettings Smtp { get; set; } = new();

    public sealed class SmtpSettings
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public string? User { get; set; }
        public string? Password { get; set; }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Smtp.Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>Sends through SMTP when configured; otherwise logs that an email was skipped (never logs the body).</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> log) : Application.Abstractions.IEmailSender
{
    private readonly EmailOptions _o = options.Value;

    public bool IsConfigured => _o.IsConfigured;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            log.LogWarning("Email not configured; skipped \"{Subject}\".", subject);
            return;
        }
        using var msg = new MailMessage
        {
            From = new MailAddress(_o.From!, _o.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        msg.To.Add(to);
        using var smtp = new SmtpClient(_o.Smtp.Host, _o.Smtp.Port) { EnableSsl = true };
        if (!string.IsNullOrWhiteSpace(_o.Smtp.User))
        {
            smtp.Credentials = new NetworkCredential(_o.Smtp.User, _o.Smtp.Password);
        }
        await smtp.SendMailAsync(msg, ct);
    }
}

/// <summary>Lets ASP.NET Core Identity (confirm email, reset password) send through the same sender.</summary>
public sealed class IdentityEmailSender(Application.Abstractions.IEmailSender email) : IEmailSender<AppUser>
{
    public Task SendConfirmationLinkAsync(AppUser user, string email1, string confirmationLink) =>
        email.SendAsync(email1, "Confirm your Googols account",
            EmailTemplates.Wrap("Confirm your email", $"<p>Tap the button to confirm your Googols account.</p>{EmailTemplates.Button(confirmationLink, "Confirm email")}"),
            CancellationToken.None);

    public Task SendPasswordResetLinkAsync(AppUser user, string email1, string resetLink) =>
        email.SendAsync(email1, "Reset your Googols password",
            EmailTemplates.Wrap("Reset your password", EmailTemplates.Button(resetLink, "Reset password")), CancellationToken.None);

    public Task SendPasswordResetCodeAsync(AppUser user, string email1, string resetCode) =>
        email.SendAsync(email1, "Your Googols password reset code",
            EmailTemplates.Wrap("Reset your password", $"<p>Your reset code is:</p><p style=\"font-size:22px;font-weight:700;letter-spacing:2px\">{HtmlEncoder.Default.Encode(resetCode)}</p>"),
            CancellationToken.None);
}

public static class EmailTemplates
{
    public static string Wrap(string heading, string inner) =>
        $"""
        <div style="font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:auto;padding:24px;color:#161a2e">
          <div style="font-size:20px;font-weight:800;color:#4f46e5;margin-bottom:16px">Googols</div>
          <h2 style="margin:0 0 12px">{HtmlEncoder.Default.Encode(heading)}</h2>
          {inner}
          <p style="color:#5f6580;font-size:12px;margin-top:24px">You received this because of activity on a Googols account. If this wasn't you, you can ignore this email.</p>
        </div>
        """;

    public static string Button(string url, string label) =>
        $"""<p><a href="{HtmlEncoder.Default.Encode(url)}" style="display:inline-block;background:#4f46e5;color:#fff;padding:10px 18px;border-radius:999px;text-decoration:none;font-weight:700">{HtmlEncoder.Default.Encode(label)}</a></p>""";
}
