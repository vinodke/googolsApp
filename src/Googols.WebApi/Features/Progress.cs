using Googols.Application.Abstractions;
using Googols.Application.Gamification;
using Googols.Application.Learning;
using Googols.Domain.Common;
using Googols.Domain.Gamification;
using Googols.Domain.Students;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

public sealed record ChapterMasteryDto(Guid ChapterId, string Title, int LessonsTotal, int LessonsDone, double? Mastery, string Band, int Attempts,
    DateTime? LastPracticedAtUtc, bool HasQuiz);
public sealed record SubjectMasteryDto(Guid SubjectId, string Name, double? Average, IReadOnlyList<ChapterMasteryDto> Chapters);
public sealed record WeakAreaDto(Guid ChapterId, string Title, string Subject, double Mastery);
public sealed record MasteryDto(IReadOnlyList<SubjectMasteryDto> Subjects, IReadOnlyList<WeakAreaDto> WeakAreas);
public sealed record MissionDto(string Code, string Title, int Progress, int Target, bool Done);
public sealed record ChallengeDto(string Code, string Title, string Description, int Progress, int Target, bool Done, bool Claimed);
public sealed record MissionsDto(DateOnly Date, IReadOnlyList<MissionDto> Missions, bool AllDone, bool Claimed, int BonusXp,
    DateOnly WeekStart, IReadOnlyList<ChallengeDto> Challenges, int ChallengeXp);
public sealed record BookmarkRequest(string Kind, Guid RefId, string? Note);
public sealed record BookmarkDto(Guid Id, string Kind, Guid RefId, string Title, string? Snippet, string? Note, DateTime CreatedAtUtc,
    Guid? ConversationId, Guid? SubjectId);

/// <summary>Mastery, weak areas, the 7-day study plan, daily missions, weekly challenges and bookmarks.</summary>
public static class ProgressEndpoints
{
    /// <summary>UTC range of <paramref name="days"/> India-time calendar days starting at <paramref name="start"/>.</summary>
    public static (DateTime From, DateTime To) IstRange(DateOnly start, int days)
    {
        var from = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue).AddMinutes(-330), DateTimeKind.Utc);
        return (from, from.AddDays(days));
    }

    public static DateOnly IstDate(DateTime utc) => DateOnly.FromDateTime(utc.AddMinutes(330));

    public static async Task<List<StudyPlanner.ChapterState>> ChapterStatesAsync(StudentAccess access, StudentAccess.Context ctx, GoogolsDbContext db, CancellationToken ct)
    {
        var chapters = await db.Chapters.AsNoTracking()
            .Where(c => c.Status == PublicationStatus.Published && c.Subject!.ClassId == ctx.ClassId)
            .OrderBy(c => c.Subject!.Sequence).ThenBy(c => c.Sequence)
            .Select(c => new { c.Id, c.Title, c.SubjectId, Subject = c.Subject!.Name }).ToListAsync(ct);
        var lessons = await access.VisibleLessons(ctx.ClassId).AsNoTracking().OrderBy(l => l.Sequence)
            .Select(l => new { l.Id, l.ChapterId }).ToListAsync(ct);
        var done = (await db.LessonProgress.Where(p => p.StudentId == ctx.Student.Id).Select(p => p.LessonId).ToListAsync(ct)).ToHashSet();
        var chapterIds = chapters.Select(c => c.Id).ToList();
        var quizChapters = (await db.Questions.Where(q => chapterIds.Contains(q.ChapterId) && q.Status == PublicationStatus.Published)
            .Select(q => q.ChapterId).Distinct().ToListAsync(ct)).ToHashSet();
        var mastery = await db.ChapterMastery.AsNoTracking().Where(m => m.StudentId == ctx.Student.Id).ToDictionaryAsync(m => m.ChapterId, ct);

        return chapters.Select(c =>
        {
            var mine = lessons.Where(l => l.ChapterId == c.Id).ToList();
            var doneCount = mine.Count(l => done.Contains(l.Id));
            var next = mine.FirstOrDefault(l => !done.Contains(l.Id))?.Id ?? mine.FirstOrDefault()?.Id;
            var m = mastery.GetValueOrDefault(c.Id);
            return new StudyPlanner.ChapterState(c.Id, c.Title, c.SubjectId, c.Subject, mine.Count, doneCount, next, quizChapters.Contains(c.Id),
                MasteryPolicy.Score(m?.PracticeScore, doneCount, mine.Count), m?.LastPracticedAtUtc);
        }).ToList();
    }

    public static void MapProgressEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/learning").WithTags("Progress").RequireAuthorization(Policies.Student);

        g.MapGet("/mastery", async (StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var states = await ChapterStatesAsync(access, ctx, db, ct);
            var attempts = await db.ChapterMastery.AsNoTracking().Where(m => m.StudentId == ctx.Student.Id).ToDictionaryAsync(m => m.ChapterId, m => m.Attempts, ct);
            var subjects = states.GroupBy(s => (s.SubjectId, s.Subject)).Select(grp =>
            {
                var tested = grp.Where(s => s.Mastery is not null).ToList();
                return new SubjectMasteryDto(grp.Key.SubjectId, grp.Key.Subject, tested.Count == 0 ? null : Math.Round(tested.Average(s => s.Mastery!.Value), 0),
                    grp.Select(s => new ChapterMasteryDto(s.ChapterId, s.Title, s.LessonsTotal, s.LessonsDone, s.Mastery, MasteryPolicy.Band(s.Mastery),
                        attempts.GetValueOrDefault(s.ChapterId), s.LastPracticedUtc, s.HasQuiz)).ToList());
            }).ToList();
            var weak = states.Where(s => s.Mastery is < MasteryPolicy.WeakBelow).OrderBy(s => s.Mastery)
                .Select(s => new WeakAreaDto(s.ChapterId, s.Title, s.Subject, s.Mastery!.Value)).ToList();
            return Results.Ok(new MasteryDto(subjects, weak));
        });

        g.MapGet("/study-plan", async (StudentAccess access, GoogolsDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var states = await ChapterStatesAsync(access, ctx, db, ct);
            return Results.Ok(StudyPlanner.Build(states, clock.TodayIn("Asia/Kolkata"), clock.UtcNow));
        });

        g.MapGet("/missions", async (StudentAccess access, GoogolsDbContext db, IClock clock, CancellationToken ct) =>
            await access.GetAsync(ct) is { } ctx ? Results.Ok(await MissionsAsync(ctx.Student.Id, db, clock, ct)) : Results.NotFound());

        g.MapPost("/missions/claim", async (StudentAccess access, GoogolsDbContext db, IClock clock, XpLedgerService xp, BadgeService badges, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var m = await MissionsAsync(ctx.Student.Id, db, clock, ct);
            if (!m.AllDone)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Finish all of today's missions first.");
            }
            var awarded = await xp.AwardAsync(new XpAwardPolicy.LearningEvent("missions-completed", ctx.Student.Id, ctx.Student.Id, Day: m.Date), ct);
            await badges.EvaluateAsync(ctx.Student.Id, ct);
            return Results.Ok(new { xpAwarded = awarded });
        });

        g.MapPost("/challenges/{code}/claim", async (string code, StudentAccess access, GoogolsDbContext db, IClock clock, XpLedgerService xp, BadgeService badges, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var m = await MissionsAsync(ctx.Student.Id, db, clock, ct);
            var c = m.Challenges.FirstOrDefault(x => x.Code == code);
            if (c is null)
            {
                return Results.NotFound();
            }
            if (!c.Done)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This challenge isn't finished yet.");
            }
            var awarded = await xp.AwardAsync(new XpAwardPolicy.LearningEvent("challenge-completed", ctx.Student.Id, ctx.Student.Id, Key: $"{m.WeekStart:yyyy-MM-dd}:{code}"), ct);
            await badges.EvaluateAsync(ctx.Student.Id, ct);
            return Results.Ok(new { xpAwarded = awarded });
        });

        // ---- Bookmarks
        g.MapGet("/bookmarks", async (StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var rows = await db.Bookmarks.AsNoTracking().Where(b => b.StudentId == ctx.Student.Id).OrderByDescending(b => b.CreatedAtUtc).Take(200)
                .Select(b => new
                {
                    b.Id, b.Kind, b.RefId, b.Title, b.Snippet, b.Note, b.CreatedAtUtc,
                    Conversation = db.AiMessages.Where(m => m.Id == b.RefId).Select(m => new { m.ConversationId, m.Conversation!.SubjectId }).FirstOrDefault(),
                }).ToListAsync(ct);
            return Results.Ok(rows.Select(b => new BookmarkDto(b.Id, b.Kind, b.RefId, b.Title, b.Snippet, b.Note, b.CreatedAtUtc,
                b.Conversation?.ConversationId, b.Conversation?.SubjectId)));
        });

        g.MapPost("/bookmarks", async (BookmarkRequest req, StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
            if (note?.Length > 500)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = ["Keep the note under 500 characters."] });
            }
            string? title = null, snippet = null;
            if (req.Kind == Bookmark.Lesson)
            {
                var l = await access.VisibleLessons(ctx.ClassId).Where(x => x.Id == req.RefId).Select(x => new { x.Title, Chapter = x.Chapter!.Title }).FirstOrDefaultAsync(ct);
                if (l is not null)
                {
                    title = l.Title;
                    snippet = l.Chapter;
                }
            }
            else if (req.Kind == Bookmark.Answer)
            {
                var m = await db.AiMessages.Where(x => x.Id == req.RefId && x.Role == "assistant" && x.Conversation!.StudentId == ctx.Student.Id)
                    .Select(x => new { x.Text, x.Conversation!.Title }).FirstOrDefaultAsync(ct);
                if (m is not null)
                {
                    title = m.Title;
                    snippet = m.Text.Length > 300 ? m.Text[..297] + "…" : m.Text;
                }
            }
            else
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Use lesson or answer."] });
            }
            if (title is null)
            {
                return Results.NotFound();
            }
            var existing = await db.Bookmarks.FirstOrDefaultAsync(b => b.StudentId == ctx.Student.Id && b.Kind == req.Kind && b.RefId == req.RefId, ct);
            if (existing is not null)
            {
                existing.Note = note ?? existing.Note;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { existing.Id });
            }
            var b = new Bookmark { StudentId = ctx.Student.Id, Kind = req.Kind, RefId = req.RefId, Title = title, Snippet = snippet, Note = note };
            db.Bookmarks.Add(b);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/learning/bookmarks/{b.Id}", new { b.Id });
        });

        g.MapDelete("/bookmarks/{id:guid}", async (Guid id, StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var n = await db.Bookmarks.Where(b => b.Id == id && b.StudentId == ctx.Student.Id).ExecuteDeleteAsync(ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    public static async Task<MissionsDto> MissionsAsync(Guid studentId, GoogolsDbContext db, IClock clock, CancellationToken ct)
    {
        var today = clock.TodayIn("Asia/Kolkata");
        var weekStart = ChallengePolicy.WeekStart(today);
        var (weekFrom, weekTo) = IstRange(weekStart, 7);
        var (dayFrom, dayTo) = IstRange(today, 1);

        var lessons = await db.LessonProgress.Where(p => p.StudentId == studentId && p.CompletedAtUtc >= weekFrom && p.CompletedAtUtc < weekTo)
            .Select(p => p.CompletedAtUtc).ToListAsync(ct);
        var quizzes = await db.QuizAttempts.Where(a => a.StudentId == studentId && a.SubmittedAtUtc >= weekFrom && a.SubmittedAtUtc < weekTo)
            .Select(a => new { At = a.SubmittedAtUtc!.Value, Percent = (double?)a.Percent }).ToListAsync(ct);
        var exams = await db.ExamAttempts.Where(a => a.StudentId == studentId && a.SubmittedAtUtc >= weekFrom && a.SubmittedAtUtc < weekTo)
            .Select(a => new { At = a.SubmittedAtUtc!.Value, a.Percent }).ToListAsync(ct);
        var practice = quizzes.Concat(exams).ToList();

        bool Today(DateTime at) => at >= dayFrom && at < dayTo;
        var missions = MissionPolicy.Today(new MissionPolicy.Activity(
            lessons.Count(Today), practice.Count(p => Today(p.At)), practice.Where(p => Today(p.At)).Select(p => p.Percent ?? 0).DefaultIfEmpty(0).Max()));
        var week = new ChallengePolicy.WeekActivity(lessons.Count, practice.Count,
            lessons.Concat(practice.Select(p => p.At)).Select(IstDate).Distinct().Count(), practice.Count(p => p.Percent >= 90));
        var challenges = ChallengePolicy.ForWeek(weekStart, week);

        var keys = challenges.Select(c => $"challenge:{weekStart:yyyy-MM-dd}:{c.Code}").Append($"missions:{today:yyyy-MM-dd}").ToList();
        var paid = (await db.XpTransactions.Where(x => x.StudentId == studentId && keys.Contains(x.IdempotencyKey)).Select(x => x.IdempotencyKey).ToListAsync(ct)).ToHashSet();
        var rules = await db.XpRules.AsNoTracking().Where(r => r.IsActive).ToDictionaryAsync(r => r.ActivityCode, r => r.Points, ct);

        return new MissionsDto(today, missions.Select(m => new MissionDto(m.Code, m.Title, m.Progress, m.Target, m.Done)).ToList(),
            missions.All(m => m.Done), paid.Contains($"missions:{today:yyyy-MM-dd}"), rules.GetValueOrDefault(XpRule.Codes.DailyMissions),
            weekStart, challenges.Select(c => new ChallengeDto(c.Code, c.Title, c.Description, c.Progress, c.Target, c.Done,
                paid.Contains($"challenge:{weekStart:yyyy-MM-dd}:{c.Code}"))).ToList(), rules.GetValueOrDefault(XpRule.Codes.ChallengeCompleted));
    }
}
