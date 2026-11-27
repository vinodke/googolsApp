using System.Security.Cryptography;
using System.Text;

namespace Googols.Application.Billing;

public sealed record Plan(string Code, string Name, int AmountPaise, int DurationDays, int AiQuestionsPerDay, bool IsFree);

/// <summary>
/// Plans and prices come from app settings (Billing__Plans__N__Code, …) so they can change without a release.
/// The defaults below are placeholders until the business sets real prices.
/// </summary>
public sealed class BillingOptions
{
    public const string Section = "Billing";

    public int FreeAiQuestionsPerDay { get; set; } = 15;

    public List<Plan> Plans { get; set; } =
    [
        new("premium-monthly", "Premium · 1 month", 19900, 30, 60, false),
        new("premium-yearly", "Premium · 12 months", 149900, 365, 60, false),
    ];

    public Plan Free => new("free", "Free", 0, 0, FreeAiQuestionsPerDay, true);

    public Plan? Find(string code) => Plans.FirstOrDefault(p => p.Code == code);
}

public static class SubscriptionMath
{
    /// <summary>Renewing early adds the new days after the current end date instead of wasting the days left.</summary>
    public static DateTime NewEnd(DateTime nowUtc, DateTime? currentEndUtc, int days) =>
        (currentEndUtc is { } end && end > nowUtc ? end : nowUtc).AddDays(days);
}

/// <summary>Razorpay signatures: HMAC-SHA256, lowercase hex. Compared in constant time.</summary>
public static class RazorpaySignature
{
    public static string Compute(string payload, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    /// <summary>Checkout success: signature over "{order_id}|{payment_id}" with the key secret.</summary>
    public static bool VerifyPayment(string orderId, string paymentId, string signature, string keySecret) =>
        Matches(Compute($"{orderId}|{paymentId}", keySecret), signature);

    /// <summary>Webhook: signature over the raw request body with the webhook secret.</summary>
    public static bool VerifyWebhook(string rawBody, string signature, string webhookSecret) =>
        Matches(Compute(rawBody, webhookSecret), signature);

    private static bool Matches(string expected, string? actual) =>
        actual is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual.ToLowerInvariant()));
}

/// <summary>Payment gateway port (Razorpay today).</summary>
public interface IPaymentGateway
{
    bool IsConfigured { get; }
    string? PublicKeyId { get; }
    Task<string> CreateOrderAsync(int amountPaise, string currency, string receipt, IReadOnlyDictionary<string, string> notes, CancellationToken ct);
    bool VerifyPayment(string orderId, string paymentId, string signature);
    bool VerifyWebhook(string rawBody, string signature);
}
