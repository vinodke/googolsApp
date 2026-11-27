using System.Text.Json;
using Googols.Application.Abstractions;
using Googols.Application.Billing;
using Googols.Domain.Billing;
using Googols.Domain.Students;
using Googols.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Googols.WebApi.Features;

/// <summary>What a student's current plan allows.</summary>
public sealed class EntitlementService(GoogolsDbContext db, IClock clock, IOptions<BillingOptions> billing)
{
    public async Task<Plan> CurrentPlanAsync(Guid studentId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var sub = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.StudentId == studentId && s.StartsAtUtc <= now && s.EndsAtUtc > now, ct);
        return sub is null ? billing.Value.Free : billing.Value.Find(sub.PlanCode) ?? billing.Value.Free;
    }
}

public static class BillingEndpoints
{
    public sealed record PlanDto(string Code, string Name, int AmountPaise, int DurationDays, int AiQuestionsPerDay, bool IsFree);
    public sealed record SubscriptionDto(string PlanCode, string PlanName, int AiQuestionsPerDay, DateTime? EndsAtUtc, bool PaymentsEnabled);
    public sealed record CheckoutRequest(string PlanCode, Guid? StudentId);
    public sealed record CheckoutDto(string OrderId, string KeyId, int AmountPaise, string Currency, string PlanName, string StudentName, string? Email);
    public sealed record VerifyRequest(string OrderId, string PaymentId, string Signature);

    public static void MapBillingEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/billing").WithTags("Billing").RequireAuthorization();

        g.MapGet("/plans", (IOptions<BillingOptions> o) =>
            new[] { o.Value.Free }.Concat(o.Value.Plans).Select(p => new PlanDto(p.Code, p.Name, p.AmountPaise, p.DurationDays, p.AiQuestionsPerDay, p.IsFree)));

        // Subscription of the signed-in student, or of a linked child when ?studentId= is given by a verified parent.
        g.MapGet("/subscription", async (Guid? studentId, ICurrentUser me, IClock clock, GoogolsDbContext db, EntitlementService ent,
            IPaymentGateway gateway, CancellationToken ct) =>
        {
            if (await ResolveStudentAsync(studentId, me, db, ct) is not { } student)
            {
                return Results.NotFound();
            }
            var plan = await ent.CurrentPlanAsync(student.Id, ct);
            var now = clock.UtcNow;
            var end = await db.Subscriptions.Where(s => s.StudentId == student.Id && s.EndsAtUtc > now).Select(s => (DateTime?)s.EndsAtUtc).FirstOrDefaultAsync(ct);
            return Results.Ok(new SubscriptionDto(plan.Code, plan.Name, plan.AiQuestionsPerDay, end, gateway.IsConfigured));
        });

        g.MapPost("/checkout", async (CheckoutRequest req, ICurrentUser me, GoogolsDbContext db, IPaymentGateway gateway,
            IOptions<BillingOptions> billing, CancellationToken ct) =>
        {
            if (!gateway.IsConfigured)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Online payments are not set up yet.");
            }
            var plan = billing.Value.Find(req.PlanCode);
            if (plan is null || plan.IsFree)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["planCode"] = ["Choose a paid plan."] });
            }
            if (await ResolveStudentAsync(req.StudentId, me, db, ct) is not { } student)
            {
                return Results.NotFound();
            }
            var receipt = $"g-{Guid.NewGuid():N}"[..30];
            var orderId = await gateway.CreateOrderAsync(plan.AmountPaise, "INR", receipt,
                new Dictionary<string, string> { ["studentId"] = student.Id.ToString(), ["plan"] = plan.Code }, ct);
            db.Payments.Add(new Payment { StudentId = student.Id, PayerUserId = me.UserId!.Value, PlanCode = plan.Code, AmountPaise = plan.AmountPaise, ProviderOrderId = orderId });
            await db.SaveChangesAsync(ct);
            var email = await db.Users.Where(u => u.Id == me.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
            return Results.Ok(new CheckoutDto(orderId, gateway.PublicKeyId!, plan.AmountPaise, "INR", plan.Name, student.DisplayName, email));
        });

        g.MapPost("/verify", async (VerifyRequest req, ICurrentUser me, GoogolsDbContext db, IPaymentGateway gateway, IClock clock,
            IOptions<BillingOptions> billing, CancellationToken ct) =>
        {
            if (!gateway.VerifyPayment(req.OrderId, req.PaymentId, req.Signature))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "We couldn't confirm this payment. If money was taken, it will be confirmed automatically within a few minutes.");
            }
            var payment = await db.Payments.FirstOrDefaultAsync(p => p.ProviderOrderId == req.OrderId && p.PayerUserId == me.UserId, ct);
            if (payment is null)
            {
                return Results.NotFound();
            }
            var end = await ActivateAsync(db, payment, req.PaymentId, clock, billing.Value, ct);
            return Results.Ok(new { endsAtUtc = end });
        });

        // Razorpay → Googols, server to server. Anonymous, but every call must carry a valid HMAC signature.
        v1.MapPost("/billing/webhooks/razorpay", async (HttpRequest http, GoogolsDbContext db, IPaymentGateway gateway, IClock clock,
            IOptions<BillingOptions> billing, ILogger<VerifyRequest> log, CancellationToken ct) =>
        {
            using var reader = new StreamReader(http.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (!gateway.VerifyWebhook(body, http.Headers["X-Razorpay-Signature"].ToString()))
            {
                return Results.Unauthorized();
            }
            using var doc = JsonDocument.Parse(body);
            var evt = doc.RootElement.GetProperty("event").GetString();
            if (evt is not ("payment.captured" or "order.paid"))
            {
                return Results.Ok();
            }
            var entity = doc.RootElement.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var orderId = entity.GetProperty("order_id").GetString();
            var paymentId = entity.GetProperty("id").GetString();
            var payment = await db.Payments.FirstOrDefaultAsync(p => p.ProviderOrderId == orderId, ct);
            if (payment is null || paymentId is null)
            {
                log.LogWarning("Razorpay webhook for unknown order {Order}", orderId);
                return Results.Ok();
            }
            await ActivateAsync(db, payment, paymentId, clock, billing.Value, ct);
            return Results.Ok();
        }).WithTags("Billing").AllowAnonymous().DisableAntiforgery();
    }

    /// <summary>Marks the payment paid and extends the subscription. Safe to call twice (verify + webhook).</summary>
    private static async Task<DateTime> ActivateAsync(GoogolsDbContext db, Payment payment, string providerPaymentId, IClock clock, BillingOptions billing, CancellationToken ct)
    {
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.StudentId == payment.StudentId, ct);
        if (payment.Status == PaymentStatus.Paid)
        {
            return sub?.EndsAtUtc ?? clock.UtcNow;
        }
        var plan = billing.Find(payment.PlanCode) ?? throw new InvalidOperationException($"Unknown plan {payment.PlanCode}.");
        var now = clock.UtcNow;
        payment.Status = PaymentStatus.Paid;
        payment.ProviderPaymentId = providerPaymentId;
        payment.PaidAtUtc = now;
        if (sub is null)
        {
            sub = new Subscription { StudentId = payment.StudentId, PlanCode = plan.Code, StartsAtUtc = now, EndsAtUtc = SubscriptionMath.NewEnd(now, null, plan.DurationDays) };
            db.Subscriptions.Add(sub);
        }
        else
        {
            if (sub.EndsAtUtc <= now)
            {
                sub.StartsAtUtc = now;
            }
            sub.EndsAtUtc = SubscriptionMath.NewEnd(now, sub.EndsAtUtc, plan.DurationDays);
            sub.PlanCode = plan.Code;
        }
        sub.LastPaymentId = payment.Id;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The checkout callback and the webhook raced; the other one already activated this payment.
            db.ChangeTracker.Clear();
            return await db.Subscriptions.Where(s => s.StudentId == payment.StudentId).Select(s => s.EndsAtUtc).FirstAsync(ct);
        }
        return sub.EndsAtUtc;
    }

    /// <summary>The signed-in student, or a child the signed-in parent is verified for. Anything else: null.</summary>
    private static async Task<StudentProfile?> ResolveStudentAsync(Guid? studentId, ICurrentUser me, GoogolsDbContext db, CancellationToken ct)
    {
        if (studentId is null)
        {
            return await db.StudentProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == me.UserId, ct);
        }
        var linked = await db.ParentStudentLinks.AnyAsync(l => l.ParentUserId == me.UserId && l.StudentId == studentId && l.Status == ParentLinkStatus.Verified, ct);
        return linked || await db.StudentProfiles.AnyAsync(s => s.Id == studentId && s.UserId == me.UserId, ct)
            ? await db.StudentProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct)
            : null;
    }
}
