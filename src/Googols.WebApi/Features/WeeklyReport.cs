using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Googols.Application.Abstractions;
using Googols.Application.Gamification;
using Googols.Application.Learning;
using Googols.Domain.Students;
using Googols.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

public sealed record ReportChapter(string Title, string Subject, double Mastery);
public sealed record WeeklyReportDto(Guid StudentId, string Name, DateOnly WeekStart, DateOnly WeekEnd, int LessonsCompleted, IReadOnlyList<string> Lessons,
    int Quizzes, int Tests, double? AverageScore, double? BestScore, int XpGained, int ActiveDays, int StreakDays, int AiQuestions,
    IReadOnlyList<ReportChapter> Practised, IReadOnlyList<ReportChapter> WeakAreas, int HomeworkSet, int HomeworkDone, int HomeworkOverdue,
    int LessonsLastWeek, int XpLastWeek, bool EmailEnabled, bool EmailConfigured);
public sealed record ReportEmailRequest(bool Enabled);

/// <summary>A parent's weekly summary of one child: what was learned, scores, effort, weak areas and homework. No chat content.</summary>
public sealed class WeeklyReportBuilder(GoogolsDbContext db, IClock clock)
{
    public async Task<WeeklyReportDto?> BuildAsync(Guid studentId, DateOnly weekStart, bool emailEnabled, bool emailConfigured, CancellationToken ct)
    {
        var st = await db.StudentProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (st is null)
        {
            return null;
        }
        var (from, to) = ProgressEndpoints.IstRange(weekStart, 7);
        var (prevFrom, _) = ProgressEndpoints.IstRange(weekStart.AddDays(-7), 7);

        var lessons = await (from p in db.LessonProgress join l in db.Lessons.IgnoreQueryFilters() on p.LessonId equals l.Id
                             where p.StudentId == studentId && p.CompletedAtUtc >= from && p.CompletedAtUtc < to
                             orderby p.CompletedAtUtc select new { l.Title, p.CompletedAtUtc }).ToListAsync(ct);
        var lessonsLastWeek = await db.LessonProgress.CountAsync(p => p.StudentId == studentId && p.CompletedAtUtc >= prevFrom && p.CompletedAtUtc < from, ct);
        var quizzes = await db.QuizAttempts.AsNoTracking().Where(a => a.StudentId == studentId && a.SubmittedAtUtc >= from && a.SubmittedAtUtc < to)
            .Select(a => new { At = a.SubmittedAtUtc!.Value, Percent = (double?)a.Percent }).ToListAsync(ct);
        var tests = await db.ExamAttempts.AsNoTracking().Where(a => a.StudentId == studentId && a.SubmittedAtUtc >= from && a.SubmittedAtUtc < to)
            .Select(a => new { At = a.SubmittedAtUtc!.Value, a.Percent }).ToListAsync(ct);
        var scores = quizzes.Concat(tests).Where(x => x.Percent is not null).Select(x => x.Percent!.Value).ToList();
        var xp = await db.XpTransactions.Where(x => x.StudentId == studentId && x.CreatedAtUtc >= from && x.CreatedAtUtc < to).SumAsync(x => (int?)x.Points, ct) ?? 0;
        var xpLast = await db.XpTransactions.Where(x => x.StudentId == studentId && x.CreatedAtUtc >= prevFrom && x.CreatedAtUtc < from).SumAsync(x => (int?)x.Points, ct) ?? 0;
        var ai = await db.AiMessages.CountAsync(m => m.Role == "user" && m.Conversation!.StudentId == studentId && m.CreatedAtUtc >= from && m.CreatedAtUtc < to, ct);
        var days = lessons.Select(l => l.CompletedAtUtc).Concat(quizzes.Select(q => q.At)).Concat(tests.Select(t => t.At))
            .Select(ProgressEndpoints.IstDate).Distinct().Count();

        var mastery = await (from m in db.ChapterMastery join c in db.Chapters on m.ChapterId equals c.Id
                             where m.StudentId == studentId
                             select new { m.PracticeScore, m.LastPracticedAtUtc, c.Title, Subject = c.Subject!.Name }).ToListAsync(ct);
        var practised = mastery.Where(m => m.LastPracticedAtUtc >= from && m.LastPracticedAtUtc < to)
            .OrderByDescending(m => m.PracticeScore).Select(m => new ReportChapter(m.Title, m.Subject, Math.Round(m.PracticeScore))).Take(8).ToList();
        var weak = mastery.Where(m => m.PracticeScore < MasteryPolicy.WeakBelow).OrderBy(m => m.PracticeScore)
            .Select(m => new ReportChapter(m.Title, m.Subject, Math.Round(m.PracticeScore))).Take(5).ToList();

        // Homework due this week (or set this week without a due date).
        var homework = await (from a in db.Assignments.AsNoTracking() join cm in db.ClassroomMembers on a.ClassroomId equals cm.ClassroomId
                              where cm.StudentId == studentId && ((a.DueAtUtc >= from && a.DueAtUtc < to) || (a.DueAtUtc == null && a.CreatedAtUtc >= from && a.CreatedAtUtc < to))
                              select a).ToListAsync(ct);
        var done = await HomeworkStatus.ForAsync(db, homework, [studentId], ct);
        var doneCount = homework.Count(a => done[a.Id].Count > 0);
        var now = clock.UtcNow;
        var overdue = homework.Count(a => done[a.Id].Count == 0 && a.DueAtUtc < now);

        var streak = StreakPolicy.Current(st.CurrentStreakDays, st.LastActiveLocalDate, clock.TodayIn("Asia/Kolkata"));
        return new WeeklyReportDto(st.Id, st.DisplayName, weekStart, weekStart.AddDays(6), lessons.Count, lessons.Select(l => l.Title).Distinct().Take(10).ToList(),
            quizzes.Count, tests.Count, scores.Count == 0 ? null : Math.Round(scores.Average(), 0), scores.Count == 0 ? null : Math.Round(scores.Max(), 0),
            xp, days, streak, ai, practised, weak, homework.Count, doneCount, overdue, lessonsLastWeek, xpLast, emailEnabled, emailConfigured);
    }

    public static string RenderHtml(WeeklyReportDto r, string appUrl)
    {
        var h = HtmlEncoder.Default;
        var sb = new StringBuilder();
        var range = $"{r.WeekStart.ToString("d MMM", CultureInfo.InvariantCulture)} – {r.WeekEnd.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        sb.Append("<div style=\"font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#1f2937\">");
        sb.Append($"<h2 style=\"color:#4f46e5\">{h.Encode(r.Name)}'s week on Googols</h2><p style=\"color:#6b7280\">{range}</p>");
        sb.Append("<table style=\"width:100%;border-collapse:collapse\">");
        void Row(string label, string value) => sb.Append($"<tr><td style=\"padding:6px 0;color:#6b7280\">{label}</td><td style=\"padding:6px 0;text-align:right\"><strong>{value}</strong></td></tr>");
        Row("Lessons finished", $"{r.LessonsCompleted} (last week {r.LessonsLastWeek})");
        Row("Quizzes and tests", $"{r.Quizzes + r.Tests}");
        Row("Average score", r.AverageScore is { } avg ? $"{avg}%" : "—");
        Row("Days learning", $"{r.ActiveDays} of 7");
        Row("XP earned", $"{r.XpGained} (last week {r.XpLastWeek})");
        Row("Questions asked to the AI teacher", $"{r.AiQuestions}");
        if (r.HomeworkSet > 0)
        {
            Row("Homework done", $"{r.HomeworkDone} of {r.HomeworkSet}{(r.HomeworkOverdue > 0 ? $" ({r.HomeworkOverdue} overdue)" : "")}");
        }
        sb.Append("</table>");
        if (r.WeakAreas.Count > 0)
        {
            sb.Append("<h3>Chapters to practise</h3><ul>");
            foreach (var w in r.WeakAreas)
            {
                sb.Append($"<li>{h.Encode(w.Subject)}: {h.Encode(w.Title)} ({w.Mastery}%)</li>");
            }
            sb.Append("</ul>");
        }
        if (r.LessonsCompleted + r.Quizzes + r.Tests == 0)
        {
            sb.Append("<p>No learning was recorded this week. A short lesson a day helps a lot. You could sit with your child for 10 minutes and open the next lesson together.</p>");
        }
        sb.Append($"<p><a href=\"{h.Encode(appUrl)}/parent\" style=\"color:#4f46e5\">See the full report on Googols</a></p>");
        sb.Append("<p style=\"color:#9ca3af;font-size:12px\">You get this because you are linked to this child on Googols. Turn weekly emails off in the parent area at any time.</p></div>");
        return sb.ToString();
    }
}

/// <summary>Every Monday morning (India time), emails last week's report to linked parents who haven't turned it off.</summary>
public sealed class WeeklyReportSender(IServiceScopeFactory scopes, IConfiguration config, ILogger<WeeklyReportSender> log) : BackgroundService
{
    private const int MaxPerRun = 200;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stop); // let the app finish starting
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        do
        {
            try
            {
                await RunOnceAsync(stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Weekly report run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stop));
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        if (!email.IsConfigured || !config.GetValue("Reports:WeeklyEmail", true))
        {
            return 0;
        }
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var nowIst = clock.UtcNow.AddMinutes(330);
        var today = DateOnly.FromDateTime(nowIst);
        if (today.DayOfWeek == DayOfWeek.Monday && nowIst.Hour < 7)
        {
            return 0; // send from 7 am Monday
        }
        var lastWeek = ChallengePolicy.WeekStart(today).AddDays(-7);
        var db = scope.ServiceProvider.GetRequiredService<GoogolsDbContext>();
        var builder = scope.ServiceProvider.GetRequiredService<WeeklyReportBuilder>();
        var appUrl = (config["App:PublicUrl"] ?? "https://googols-app.azurewebsites.net").TrimEnd('/');

        var due = await (from l in db.ParentStudentLinks
                         join u in db.Users on l.ParentUserId equals u.Id
                         where l.Status == ParentLinkStatus.Verified && l.WeeklyReportEmail && u.Email != null
                            && !db.ParentReportLogs.Any(r => r.ParentUserId == l.ParentUserId && r.StudentId == l.StudentId && r.WeekStart == lastWeek)
                         select new { l.ParentUserId, l.StudentId, u.Email }).Take(MaxPerRun).ToListAsync(ct);
        var sent = 0;
        foreach (var d in due)
        {
            // Claim first (unique index), so two app instances never send the same report.
            var logRow = new ParentReportLog { ParentUserId = d.ParentUserId, StudentId = d.StudentId, WeekStart = lastWeek, SentAtUtc = clock.UtcNow };
            db.ParentReportLogs.Add(logRow);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                continue;
            }
            var report = await builder.BuildAsync(d.StudentId, lastWeek, true, true, ct);
            if (report is null)
            {
                continue;
            }
            try
            {
                await email.SendAsync(d.Email!, $"{report.Name}'s week on Googols", WeeklyReportBuilder.RenderHtml(report, appUrl), ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Weekly report email failed; will retry next run");
                db.ParentReportLogs.Remove(logRow);
                await db.SaveChangesAsync(ct);
            }
        }
        if (sent > 0)
        {
            log.LogInformation("Sent {Count} weekly parent reports for week {Week}", sent, lastWeek);
        }
        return sent;
    }
}

public static class WeeklyReportEndpoints
{
    public static void MapWeeklyReportEndpoints(this RouteGroupBuilder v1)
    {
        var p = v1.MapGroup("/parents/children/{studentId:guid}").WithTags("Parents").RequireAuthorization();

        p.MapGet("/weekly-report", async (Guid studentId, DateOnly? week, ICurrentUser me, IClock clock, GoogolsDbContext db,
            WeeklyReportBuilder builder, IEmailSender email, CancellationToken ct) =>
        {
            var link = await db.ParentStudentLinks.AsNoTracking()
                .FirstOrDefaultAsync(l => l.ParentUserId == me.UserId && l.StudentId == studentId && l.Status == ParentLinkStatus.Verified, ct);
            if (link is null)
            {
                return Results.NotFound();
            }
            var start = ChallengePolicy.WeekStart(week ?? clock.TodayIn("Asia/Kolkata"));
            var report = await builder.BuildAsync(studentId, start, link.WeeklyReportEmail, email.IsConfigured, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        p.MapPut("/report-email", async (Guid studentId, ReportEmailRequest req, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var n = await db.ParentStudentLinks.Where(l => l.ParentUserId == me.UserId && l.StudentId == studentId && l.Status == ParentLinkStatus.Verified)
                .ExecuteUpdateAsync(u => u.SetProperty(l => l.WeeklyReportEmail, req.Enabled), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
