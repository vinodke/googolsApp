using Googols.Application.Abstractions;
using Googols.Application.Learning;
using Googols.Domain.Assessment;
using Googols.Domain.Common;
using Googols.Domain.Schools;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

public sealed record AssignmentRequest(string Title, string? Instructions, AssignmentKind Kind, Guid? LessonId, Guid? ChapterId, Guid? ExamId, DateTime? DueAtUtc);
public sealed record AssignmentStudentStatus(Guid StudentId, string Name, bool Done, DateTime? DoneAtUtc, string? Text);
public sealed record TeacherAssignmentDto(Guid Id, string Title, string? Instructions, string Kind, Guid? LessonId, Guid? ChapterId, Guid? ExamId, string? Target,
    DateTime? DueAtUtc, DateTime CreatedAtUtc, int Done, int Total, IReadOnlyList<AssignmentStudentStatus> Students);
public sealed record HomeworkDto(Guid Id, string Title, string? Instructions, string Kind, string Classroom, Guid? LessonId, Guid? ChapterId, Guid? ExamId,
    string? Target, DateTime? DueAtUtc, bool Done, DateTime? DoneAtUtc, bool Overdue, string? MyText);
public sealed record HomeworkDoneRequest(string? Text);
public sealed record HeatmapChapter(Guid ChapterId, string Title, string Subject, double? Average);
public sealed record HeatmapRow(Guid StudentId, string Name, IReadOnlyList<double?> Cells);
public sealed record HeatmapDto(IReadOnlyList<HeatmapChapter> Chapters, IReadOnlyList<HeatmapRow> Students);

/// <summary>Works out, from real activity, which students have finished which homework.</summary>
public static class HomeworkStatus
{
    public sealed record Done(Guid StudentId, DateTime At, string? Text);

    public static async Task<Dictionary<Guid, List<Done>>> ForAsync(GoogolsDbContext db, IReadOnlyList<Assignment> items, IReadOnlyCollection<Guid> students, CancellationToken ct)
    {
        var result = items.ToDictionary(a => a.Id, _ => new List<Done>());
        if (items.Count == 0 || students.Count == 0)
        {
            return result;
        }
        var ids = items.Select(a => a.Id).ToList();
        var subs = await db.AssignmentSubmissions.AsNoTracking().Where(s => ids.Contains(s.AssignmentId) && students.Contains(s.StudentId)).ToListAsync(ct);
        var lessonIds = items.Where(a => a.Kind == AssignmentKind.Lesson && a.LessonId != null).Select(a => a.LessonId!.Value).ToList();
        var lessons = await db.LessonProgress.AsNoTracking().Where(p => lessonIds.Contains(p.LessonId) && students.Contains(p.StudentId))
            .Select(p => new { p.LessonId, p.StudentId, p.CompletedAtUtc }).ToListAsync(ct);
        var chapterIds = items.Where(a => a.Kind == AssignmentKind.Quiz && a.ChapterId != null).Select(a => a.ChapterId!.Value).ToList();
        var quizzes = await db.QuizAttempts.AsNoTracking().Where(q => chapterIds.Contains(q.ChapterId) && students.Contains(q.StudentId) && q.SubmittedAtUtc != null)
            .Select(q => new { q.ChapterId, q.StudentId, At = q.SubmittedAtUtc!.Value }).ToListAsync(ct);
        var examIds = items.Where(a => a.Kind == AssignmentKind.Test && a.ExamId != null).Select(a => a.ExamId!.Value).ToList();
        var exams = await db.ExamAttempts.AsNoTracking().Where(x => examIds.Contains(x.ExamId) && students.Contains(x.StudentId) && x.SubmittedAtUtc != null)
            .Select(x => new { x.ExamId, x.StudentId, At = x.SubmittedAtUtc!.Value }).ToListAsync(ct);

        foreach (var a in items)
        {
            var list = result[a.Id];
            list.AddRange(subs.Where(s => s.AssignmentId == a.Id).Select(s => new Done(s.StudentId, s.SubmittedAtUtc, s.Text)));
            switch (a.Kind)
            {
                case AssignmentKind.Lesson:
                    // A lesson finished before the homework was set still counts: the work is done.
                    list.AddRange(lessons.Where(l => l.LessonId == a.LessonId).Select(l => new Done(l.StudentId, l.CompletedAtUtc, null)));
                    break;
                case AssignmentKind.Quiz:
                    list.AddRange(quizzes.Where(q => q.ChapterId == a.ChapterId && q.At >= a.CreatedAtUtc).GroupBy(q => q.StudentId)
                        .Select(g => new Done(g.Key, g.Min(x => x.At), null)));
                    break;
                case AssignmentKind.Test:
                    list.AddRange(exams.Where(x => x.ExamId == a.ExamId).GroupBy(x => x.StudentId).Select(g => new Done(g.Key, g.Min(x => x.At), null)));
                    break;
            }
            var dedup = list.GroupBy(d => d.StudentId).Select(g => g.OrderBy(d => d.At).First()).ToList();
            list.Clear();
            list.AddRange(dedup);
        }
        return result;
    }
}

public static class Heatmap
{
    /// <summary>Mastery per student per published chapter of the class; null cells mean "not tested yet".</summary>
    public static async Task<HeatmapDto> BuildAsync(GoogolsDbContext db, Guid classId, IQueryable<Guid> studentIds, Guid? subjectId, CancellationToken ct)
    {
        var chapters = await db.Chapters.AsNoTracking()
            .Where(c => c.Status == PublicationStatus.Published && c.Subject!.ClassId == classId && (subjectId == null || c.SubjectId == subjectId))
            .OrderBy(c => c.Subject!.Sequence).ThenBy(c => c.Sequence)
            .Select(c => new { c.Id, c.Title, Subject = c.Subject!.Name }).ToListAsync(ct);
        var chapterIds = chapters.Select(c => c.Id).ToList();
        var students = await db.StudentProfiles.AsNoTracking().Where(s => studentIds.Contains(s.Id)).OrderBy(s => s.DisplayName)
            .Select(s => new { s.Id, s.DisplayName }).ToListAsync(ct);
        var ids = students.Select(s => s.Id).ToList();
        var mastery = await db.ChapterMastery.AsNoTracking().Where(m => ids.Contains(m.StudentId) && chapterIds.Contains(m.ChapterId))
            .Select(m => new { m.StudentId, m.ChapterId, m.PracticeScore }).ToListAsync(ct);
        var lessonTotals = await db.Lessons.AsNoTracking()
            .Where(l => chapterIds.Contains(l.ChapterId) && l.Status == PublicationStatus.Published)
            .GroupBy(l => l.ChapterId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var lessonsDone = await db.LessonProgress.AsNoTracking().Where(p => ids.Contains(p.StudentId) && chapterIds.Contains(p.ChapterId))
            .GroupBy(p => new { p.StudentId, p.ChapterId }).Select(g => new { g.Key.StudentId, g.Key.ChapterId, N = g.Count() }).ToListAsync(ct);

        var rows = students.Select(s => new HeatmapRow(s.Id, s.DisplayName, chapters.Select(c =>
        {
            var m = mastery.FirstOrDefault(x => x.StudentId == s.Id && x.ChapterId == c.Id);
            var done = lessonsDone.FirstOrDefault(x => x.StudentId == s.Id && x.ChapterId == c.Id)?.N ?? 0;
            return MasteryPolicy.Score(m?.PracticeScore, done, lessonTotals.GetValueOrDefault(c.Id));
        }).ToList())).ToList();
        var cols = chapters.Select((c, i) =>
        {
            var vals = rows.Select(r => r.Cells[i]).Where(v => v is not null).Select(v => v!.Value).ToList();
            return new HeatmapChapter(c.Id, c.Title, c.Subject, vals.Count == 0 ? null : Math.Round(vals.Average(), 0));
        }).ToList();
        return new HeatmapDto(cols, rows);
    }
}

public static class HomeworkEndpoints
{
    public static void MapHomeworkEndpoints(this RouteGroupBuilder v1)
    {
        var t = v1.MapGroup("/teacher/classrooms/{id:guid}").WithTags("Homework").RequireAuthorization(Policies.Teacher);

        t.MapGet("/heatmap", async (Guid id, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var room = await db.Classrooms.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.TeacherUserId == me.UserId, ct);
            if (room is null)
            {
                return Results.NotFound();
            }
            var members = db.ClassroomMembers.Where(m => m.ClassroomId == id).Select(m => m.StudentId);
            return Results.Ok(await Heatmap.BuildAsync(db, room.ClassId, members, room.SubjectId, ct));
        });

        t.MapGet("/assignments", async (Guid id, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Classrooms.AnyAsync(c => c.Id == id && c.TeacherUserId == me.UserId, ct))
            {
                return Results.NotFound();
            }
            var items = await db.Assignments.AsNoTracking().Where(a => a.ClassroomId == id).OrderByDescending(a => a.CreatedAtUtc).Take(50).ToListAsync(ct);
            var students = await (from m in db.ClassroomMembers join s in db.StudentProfiles on m.StudentId equals s.Id
                                  where m.ClassroomId == id orderby s.DisplayName select new { s.Id, s.DisplayName }).ToListAsync(ct);
            var done = await HomeworkStatus.ForAsync(db, items, students.Select(s => s.Id).ToList(), ct);
            var targets = await TargetsAsync(db, items, ct);
            return Results.Ok(items.Select(a =>
            {
                var d = done[a.Id];
                return new TeacherAssignmentDto(a.Id, a.Title, a.Instructions, a.Kind.ToString(), a.LessonId, a.ChapterId, a.ExamId, targets.GetValueOrDefault(a.Id),
                    a.DueAtUtc, a.CreatedAtUtc, d.Count, students.Count,
                    students.Select(s => d.FirstOrDefault(x => x.StudentId == s.Id) is { } x
                        ? new AssignmentStudentStatus(s.Id, s.DisplayName, true, x.At, x.Text)
                        : new AssignmentStudentStatus(s.Id, s.DisplayName, false, null, null)).ToList());
            }));
        });

        t.MapPost("/assignments", async (Guid id, AssignmentRequest req, ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var room = await db.Classrooms.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.TeacherUserId == me.UserId, ct);
            if (room is null)
            {
                return Results.NotFound();
            }
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 200)
            {
                errors["title"] = ["Give the homework a title (up to 200 characters)."];
            }
            if ((req.Instructions?.Length ?? 0) > 2000)
            {
                errors["instructions"] = ["Keep the instructions under 2000 characters."];
            }
            if (req.DueAtUtc is { } due && due <= clock.UtcNow)
            {
                errors["dueAtUtc"] = ["The due date must be in the future."];
            }
            if (req.Kind == AssignmentKind.Lesson && (req.LessonId is null || !await db.Lessons.AnyAsync(l => l.Id == req.LessonId && l.Status == PublicationStatus.Published
                    && l.Chapter!.Status == PublicationStatus.Published && l.Chapter.Subject!.ClassId == room.ClassId, ct)))
            {
                errors["lessonId"] = ["Choose a published lesson of this class."];
            }
            else if (req.Kind == AssignmentKind.Quiz && (req.ChapterId is null || !await db.Chapters.AnyAsync(c => c.Id == req.ChapterId
                    && c.Status == PublicationStatus.Published && c.Subject!.ClassId == room.ClassId, ct)))
            {
                errors["chapterId"] = ["Choose a published chapter of this class."];
            }
            else if (req.Kind == AssignmentKind.Test && (req.ExamId is null || !await db.Exams.AnyAsync(e => e.Id == req.ExamId && e.ClassroomId == id && e.Kind == ExamKind.ClassTest, ct)))
            {
                errors["examId"] = ["Choose one of this classroom's tests."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }
            var a = new Assignment
            {
                ClassroomId = id,
                Title = req.Title.Trim(),
                Instructions = string.IsNullOrWhiteSpace(req.Instructions) ? null : req.Instructions.Trim(),
                Kind = req.Kind,
                LessonId = req.Kind == AssignmentKind.Lesson ? req.LessonId : null,
                ChapterId = req.Kind == AssignmentKind.Quiz ? req.ChapterId : null,
                ExamId = req.Kind == AssignmentKind.Test ? req.ExamId : null,
                DueAtUtc = req.DueAtUtc,
            };
            db.Assignments.Add(a);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/teacher/classrooms/{id}/assignments/{a.Id}", new { a.Id });
        });

        t.MapDelete("/assignments/{assignmentId:guid}", async (Guid id, Guid assignmentId, ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Classrooms.AnyAsync(c => c.Id == id && c.TeacherUserId == me.UserId, ct))
            {
                return Results.NotFound();
            }
            var n = await db.Assignments.Where(a => a.Id == assignmentId && a.ClassroomId == id).ExecuteUpdateAsync(u => u.SetProperty(a => a.ArchivedAtUtc, clock.UtcNow), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        // ---- Student side
        var s = v1.MapGroup("/learning/homework").WithTags("Homework").RequireAuthorization(Policies.Student);

        s.MapGet("", async (StudentAccess access, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var rows = await (from a in db.Assignments.AsNoTracking()
                              join m in db.ClassroomMembers on a.ClassroomId equals m.ClassroomId
                              join c in db.Classrooms on a.ClassroomId equals c.Id
                              where m.StudentId == ctx.Student.Id
                              orderby a.DueAtUtc ?? DateTime.MaxValue, a.CreatedAtUtc descending
                              select new { a, Room = c.Name }).Take(60).ToListAsync(ct);
            var items = rows.Select(r => r.a).ToList();
            var done = await HomeworkStatus.ForAsync(db, items, [ctx.Student.Id], ct);
            var targets = await TargetsAsync(db, items, ct);
            var now = clock.UtcNow;
            return Results.Ok(rows.Select(r =>
            {
                var d = done[r.a.Id].FirstOrDefault();
                return new HomeworkDto(r.a.Id, r.a.Title, r.a.Instructions, r.a.Kind.ToString(), r.Room, r.a.LessonId, r.a.ChapterId, r.a.ExamId,
                    targets.GetValueOrDefault(r.a.Id), r.a.DueAtUtc, d is not null, d?.At, d is null && r.a.DueAtUtc < now, d?.Text);
            }));
        });

        s.MapPost("/{id:guid}/done", async (Guid id, HomeworkDoneRequest req, StudentAccess access, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var a = await (from x in db.Assignments join m in db.ClassroomMembers on x.ClassroomId equals m.ClassroomId
                           where x.Id == id && m.StudentId == ctx.Student.Id select x).FirstOrDefaultAsync(ct);
            if (a is null)
            {
                return Results.NotFound();
            }
            if (a.Kind != AssignmentKind.Task)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This homework is marked done automatically when you finish it.");
            }
            var text = string.IsNullOrWhiteSpace(req.Text) ? null : req.Text.Trim();
            if (text?.Length > 2000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["Keep it under 2000 characters."] });
            }
            var sub = await db.AssignmentSubmissions.FirstOrDefaultAsync(x => x.AssignmentId == id && x.StudentId == ctx.Student.Id, ct);
            if (sub is null)
            {
                db.AssignmentSubmissions.Add(new AssignmentSubmission { AssignmentId = id, StudentId = ctx.Student.Id, Text = text, SubmittedAtUtc = clock.UtcNow });
            }
            else
            {
                sub.Text = text;
                sub.SubmittedAtUtc = clock.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>Readable name of what each homework points at (lesson title, chapter title or test title).</summary>
    private static async Task<Dictionary<Guid, string>> TargetsAsync(GoogolsDbContext db, IReadOnlyList<Assignment> items, CancellationToken ct)
    {
        var lessonIds = items.Where(a => a.LessonId != null).Select(a => a.LessonId!.Value).ToList();
        var chapterIds = items.Where(a => a.ChapterId != null).Select(a => a.ChapterId!.Value).ToList();
        var examIds = items.Where(a => a.ExamId != null).Select(a => a.ExamId!.Value).ToList();
        var lessons = await db.Lessons.IgnoreQueryFilters().Where(l => lessonIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Title, ct);
        var chapters = await db.Chapters.Where(c => chapterIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        var exams = await db.Exams.IgnoreQueryFilters().Where(e => examIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Title, ct);
        var map = new Dictionary<Guid, string>();
        foreach (var a in items)
        {
            var name = a.Kind switch
            {
                AssignmentKind.Lesson when a.LessonId is { } l => lessons.GetValueOrDefault(l),
                AssignmentKind.Quiz when a.ChapterId is { } c => chapters.GetValueOrDefault(c),
                AssignmentKind.Test when a.ExamId is { } e => exams.GetValueOrDefault(e),
                _ => null,
            };
            if (name is not null)
            {
                map[a.Id] = name;
            }
        }
        return map;
    }
}
