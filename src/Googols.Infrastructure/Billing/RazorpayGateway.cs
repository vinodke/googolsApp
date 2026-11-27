using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Googols.Application.Billing;
using Microsoft.Extensions.Options;

namespace Googols.Infrastructure.Billing;

public sealed class RazorpayOptions
{
    public const string Section = "Razorpay";
    public string? KeyId { get; set; }          // rzp_test_… / rzp_live_…
    public string? KeySecret { get; set; }      // app setting only, never in the repo
    public string? WebhookSecret { get; set; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);
}

/// <summary>Razorpay Orders API over HTTPS (no SDK needed). Card and UPI details never touch Googols servers.</summary>
public sealed class RazorpayGateway(IHttpClientFactory http, IOptions<RazorpayOptions> options) : IPaymentGateway
{
    private readonly RazorpayOptions _o = options.Value;

    public bool IsConfigured => _o.IsConfigured;
    public string? PublicKeyId => _o.KeyId;

    public async Task<string> CreateOrderAsync(int amountPaise, string currency, string receipt, IReadOnlyDictionary<string, string> notes, CancellationToken ct)
    {
        var client = http.CreateClient(nameof(RazorpayGateway));
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders")
        {
            Content = JsonContent.Create(new { amount = amountPaise, currency, receipt, notes }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_o.KeyId}:{_o.KeySecret}")));
        using var res = await client.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var order = await res.Content.ReadFromJsonAsync<OrderResponse>(ct);
        return order?.Id ?? throw new InvalidOperationException("Razorpay returned no order id.");
    }

    public bool VerifyPayment(string orderId, string paymentId, string signature) =>
        IsConfigured && RazorpaySignature.VerifyPayment(orderId, paymentId, signature, _o.KeySecret!);

    public bool VerifyWebhook(string rawBody, string signature) =>
        !string.IsNullOrWhiteSpace(_o.WebhookSecret) && RazorpaySignature.VerifyWebhook(rawBody, signature, _o.WebhookSecret);

    private sealed class OrderResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }
}
