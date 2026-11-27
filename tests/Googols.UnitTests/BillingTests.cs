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

public class CurriculumCsvTests
{
    [Fact]
    public void Parses_rows_with_quotes_and_reports_bad_lines()
    {
        var csv = "﻿grade,subject_code,subject_name,medium,chapter_no,chapter_title\r\n" +
                  "8,bio-8,Biology,ml,1,\"Life, cells and \"\"tissues\"\"\"\r\n" +
                  "8,BIO-8,Biology,ml,2,Photosynthesis\n" +
                  "13,BIO-8,Biology,ml,3,Bad grade\n" +
                  "\n" +
                  "8,BIO-8,Biology,ml,2,Duplicate number\n";
        var r = Googols.Application.Curriculum.CurriculumCsv.Parse(csv);
        Assert.Equal(3, r.Rows.Count);
        Assert.Equal("Life, cells and \"tissues\"", r.Rows[0].ChapterTitle);
        Assert.Equal("BIO-8", r.Rows[0].SubjectCode);
        Assert.Contains(r.Errors, e => e.StartsWith("Line 4:") && e.Contains("grade"));
        Assert.Contains(r.Errors, e => e.Contains("appears twice"));
    }

    [Fact]
    public void Missing_header_columns_are_named()
    {
        var r = Googols.Application.Curriculum.CurriculumCsv.Parse("grade,subject\n8,Bio");
        Assert.Empty(r.Rows);
        Assert.Contains("subject_code", Assert.Single(r.Errors));
    }
}
