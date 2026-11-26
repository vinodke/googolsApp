using Googols.Application.Abstractions;
using Googols.Application.Gamification;
using Googols.Domain.Gamification;
using Googols.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

/// <summary>
/// Writes XP to the append-only ledger. Called by server-side learning flows (lesson completion,
/// graded quiz submission) — never by a client endpoint, so the browser can't mint XP.
/// </summary>
public sealed class XpLedgerService(GoogolsDbContext db, IRealtimeNotifier realtime)
{
    public async Task<int> AwardAsync(XpAwardPolicy.LearningEvent e, CancellationToken ct)
    {
        var rules = await db.XpRules.AsNoTracking().Where(r => r.IsActive).ToDictionaryAsync(r => r.ActivityCode, r => r.Points, ct);
        var awarded = 0;
        foreach (var award in XpAwardPolicy.Evaluate(e, rules))
        {
            if (await db.XpTransactions.AnyAsync(x => x.StudentId == e.StudentId && x.IdempotencyKey == award.IdempotencyKey, ct))
            {
                continue; // already paid
            }
            db.XpTransactions.Add(new XpTransaction
            {
                StudentId = e.StudentId,
                Points = award.Points,
                Reason = award.ActivityCode,
                IdempotencyKey = award.IdempotencyKey,
                RuleVersion = XpAwardPolicy.Version,
            });
            try
            {
                await db.SaveChangesAsync(ct);
                awarded += award.Points;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                db.ChangeTracker.Clear(); // a concurrent request paid it first; unique index held
            }
        }

        if (awarded > 0)
        {
            // Recompute the cached total from the ledger rather than incrementing, so it can't drift.
            var total = await db.XpTransactions.Where(x => x.StudentId == e.StudentId).SumAsync(x => x.Points, ct);
            await db.StudentProfiles.Where(s => s.Id == e.StudentId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.TotalXp, total), ct);
            await realtime.NotifyStudentAsync(e.StudentId, "xpAwarded", new { awarded, total, level = XpAwardPolicy.LevelFor(total) }, ct);
        }
        return awarded;
    }
}

public static class GamificationEndpoints
{
    public static void MapGamificationEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/gamification").WithTags("Gamification").RequireAuthorization(Plumbing.Policies.Student);

        g.MapGet("/xp", async (ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var studentId = await db.StudentProfiles.Where(s => s.UserId == me.UserId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (studentId is null)
            {
                return Results.NotFound();
            }
            var recent = await db.XpTransactions.AsNoTracking().Where(x => x.StudentId == studentId)
                .OrderByDescending(x => x.CreatedAtUtc).Take(20)
                .Select(x => new { x.Points, x.Reason, x.CreatedAtUtc }).ToListAsync(ct);
            var total = await db.XpTransactions.Where(x => x.StudentId == studentId).SumAsync(x => x.Points, ct);
            return Results.Ok(new { total, level = XpAwardPolicy.LevelFor(total), recent });
        });
    }
}
