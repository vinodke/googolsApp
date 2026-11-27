using Googols.Application.Billing;

namespace Googols.UnitTests;

public class BillingTests
{
    [Fact]
    public void Razorpay_payment_signature_matches_the_documented_hmac()
    {
        // HMAC-SHA256("order_ABC|pay_XYZ", "secret_123"), computed independently.
        const string expected = "36b48c1d9de5f3e289442c5766e0ecee2ea60f4f1d4b5400b0c9eec6979ae944";
        Assert.True(RazorpaySignature.VerifyPayment("order_ABC", "pay_XYZ", expected, "secret_123"));
        Assert.True(RazorpaySignature.VerifyPayment("order_ABC", "pay_XYZ", expected.ToUpperInvariant(), "secret_123"));
        Assert.False(RazorpaySignature.VerifyPayment("order_ABC", "pay_OTHER", expected, "secret_123"));
        Assert.False(RazorpaySignature.VerifyPayment("order_ABC", "pay_XYZ", "", "secret_123"));
    }

    [Fact]
    public void Webhook_signature_is_over_the_raw_body()
    {
        var body = """{"event":"payment.captured"}""";
        var sig = RazorpaySignature.Compute(body, "whsec");
        Assert.True(RazorpaySignature.VerifyWebhook(body, sig, "whsec"));
        Assert.False(RazorpaySignature.VerifyWebhook(body + " ", sig, "whsec"));
    }

    [Fact]
    public void Early_renewal_adds_to_the_remaining_days()
    {
        var now = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddDays(30), SubscriptionMath.NewEnd(now, null, 30));
        Assert.Equal(now.AddDays(40), SubscriptionMath.NewEnd(now, now.AddDays(10), 30));
        Assert.Equal(now.AddDays(30), SubscriptionMath.NewEnd(now, now.AddDays(-5), 30)); // lapsed: start from today
    }

    [Fact]
    public void Default_plans_have_a_free_tier_and_paid_tiers_with_more_ai()
    {
        var o = new BillingOptions();
        Assert.True(o.Free.IsFree);
        Assert.All(o.Plans, p => Assert.True(p.AiQuestionsPerDay > o.Free.AiQuestionsPerDay && p.AmountPaise > 0));
        Assert.Null(o.Find("nope"));
    }
}
