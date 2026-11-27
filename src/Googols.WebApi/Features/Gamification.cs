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

/// <summary>Checks progress after verified learning events and stores any newly earned badges.</summary>
public sealed class BadgeService(GoogolsDbContext db, IClock clock, IRealtimeNotifier realtime)
{
    public async Task<IReadOnlyList<string>> EvaluateAsync(Guid studentId, CancellationToken ct)
    {
        var student = await db.StudentProfiles.AsNoTracking().Where(s => s.Id == studentId)
            .Select(s => new { s.TotalXp, s.CurrentStreakDays }).FirstAsync(ct);
        var lessons = await db.LessonProgress.CountAsync(p => p.StudentId == studentId, ct);
        var chapters = await db.XpTransactions.CountAsync(x => x.StudentId == studentId && x.Reason == XpRule.Codes.ChapterCompleted, ct);
        var quizzes = await db.QuizAttempts.Where(a => a.StudentId == studentId && a.SubmittedAtUtc != null)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Perfect = g.Any(a => a.Percent >= 100) }).FirstOrDefaultAsync(ct);

        var stats = new BadgeCatalog.Stats(lessons, chapters, quizzes?.Count ?? 0, quizzes?.Perfect ?? false,
            student.CurrentStreakDays, XpAwardPolicy.LevelFor(student.TotalXp));
        var have = await db.StudentBadges.Where(b => b.StudentId == studentId).Select(b => b.BadgeCode).ToListAsync(ct);
        var fresh = BadgeCatalog.Earned(stats).Except(have).ToList();
        if (fresh.Count == 0)
        {
            return [];
        }
        foreach (var code in fresh)
        {
            db.StudentBadges.Add(new StudentBadge { StudentId = studentId, BadgeCode = code, AwardedAtUtc = clock.UtcNow });
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear(); // a parallel request stored the same badge
            return [];
        }
        await realtime.NotifyStudentAsync(studentId, "badgesAwarded", new { badges = fresh }, ct);
        return fresh;
    }
}

public static class LeaderboardEndpoints
{
    public sealed record BadgeDto(string Code, string Name, string Description, string Icon, bool Earned, DateTime? AwardedAtUtc);
    public sealed record LeaderboardRow(int Rank, string Name, int Xp, int Level, bool IsMe);
    public sealed record LeaderboardDto(string Period, string ClassName, IReadOnlyList<LeaderboardRow> Rows, LeaderboardRow? Me, bool ShowingMe);
    public sealed record VisibilityRequest(bool Visible);

    public static void MapLeaderboardEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/gamification").WithTags("Gamification").RequireAuthorization(Plumbing.Policies.Student);

        g.MapGet("/badges", async (StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var earned = await db.StudentBadges.AsNoTracking().Where(b => b.StudentId == ctx.Student.Id)
                .ToDictionaryAsync(b => b.BadgeCode, b => b.AwardedAtUtc, ct);
            return Results.Ok(BadgeCatalog.All.Select(b => new BadgeDto(b.Code, b.Name, b.Description, b.Icon,
                earned.ContainsKey(b.Code), earned.TryGetValue(b.Code, out var at) ? at : null)));
        });

        g.MapGet("/leaderboard", async (string? period, StudentAccess access, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var weekly = period != "all";
            var since = clock.UtcNow.AddDays(-7);
            var className = await db.AcademicClasses.Where(k => k.Id == ctx.ClassId).Select(k => k.GradeName).FirstAsync(ct);
            var classmates = db.Enrollments.Where(e => e.ClassId == ctx.ClassId && e.IsActive).Select(e => e.StudentId);
            var rows = await db.StudentProfiles.AsNoTracking()
                .Where(s => classmates.Contains(s.Id) && (s.ShowOnLeaderboard || s.Id == ctx.Student.Id))
                .Select(s => new
                {
                    s.Id,
                    s.DisplayName,
                    s.TotalXp,
                    s.ShowOnLeaderboard,
                    Xp = weekly ? db.XpTransactions.Where(x => x.StudentId == s.Id && x.CreatedAtUtc >= since).Sum(x => (int?)x.Points) ?? 0 : s.TotalXp,
                })
                .OrderByDescending(x => x.Xp).ThenBy(x => x.DisplayName).Take(200).ToListAsync(ct);

            var ranked = rows.Select((r, i) => new LeaderboardRow(i + 1, Application.Common.Privacy.PublicName(r.DisplayName), r.Xp, XpAwardPolicy.LevelFor(r.TotalXp), r.Id == ctx.Student.Id)).ToList();
            var me = ranked.FirstOrDefault(r => r.IsMe);
            // Students who hid themselves still see their own rank, but are left out of the list others see.
            var visible = ranked.Where(r => !r.IsMe || ctx.Student.ShowOnLeaderboard).Take(20).ToList();
            return Results.Ok(new LeaderboardDto(weekly ? "week" : "all", className, visible, me, ctx.Student.ShowOnLeaderboard));
        });

        g.MapPut("/leaderboard/visibility", async (VisibilityRequest req, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var n = await db.StudentProfiles.Where(s => s.UserId == me.UserId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.ShowOnLeaderboard, req.Visible), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
