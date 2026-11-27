using Googols.Domain.Common;

namespace Googols.Domain.Billing;

public enum PaymentStatus
{
    Created = 0,
    Paid = 1,
    Failed = 2,
}

/// <summary>A payment attempt for a student's plan. Amounts are in paise (₹1 = 100 paise).</summary>
public class Payment : Entity
{
    public Guid StudentId { get; set; }
    public Guid PayerUserId { get; set; }              // the student or a verified parent
    public required string PlanCode { get; set; }
    public int AmountPaise { get; set; }
    public string Currency { get; set; } = "INR";
    public string Provider { get; set; } = "razorpay";
    public required string ProviderOrderId { get; set; }
    public string? ProviderPaymentId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    public DateTime? PaidAtUtc { get; set; }
}

/// <summary>Paid access for one student until <see cref="EndsAtUtc"/>. Renewals extend the end date.</summary>
public class Subscription : Entity
{
    public Guid StudentId { get; set; }
    public required string PlanCode { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public Guid LastPaymentId { get; set; }

    public bool IsActive(DateTime nowUtc) => StartsAtUtc <= nowUtc && EndsAtUtc > nowUtc;
}
