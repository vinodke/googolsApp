using System.Text.Json;
using Googols.Application.Abstractions;
using Googols.Application.Assessment;
using Googols.Application.Curriculum;
using Googols.Domain.Assessment;
using Googols.Domain.Common;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

public sealed record WrittenQuestionRequest(string Prompt, string? ModelAnswer, IReadOnlyList<RubricCriterion> Rubric, string? Language);
public sealed record AdminWrittenQuestionDto(Guid Id, string Prompt, string? ModelAnswer, IReadOnlyList<RubricCriterion> Rubric, int MaxMarks, string Language, string Status);
public sealed record ExamRequest(Guid ClassId, Guid? SubjectId, string Title, IReadOnlyList<Guid> ChapterIds, int McqCount, int WrittenCount, int DurationMinutes,
    DateTime? OpensAtUtc, DateTime? ClosesAtUtc);
public sealed record ExamAdminDto(Guid Id, string Title, string Kind, Guid ClassId, Guid? SubjectId, IReadOnlyList<Guid> ChapterIds, int McqCount, int WrittenCount,
    int DurationMinutes, DateTime? OpensAtUtc, DateTime? ClosesAtUtc, string Status, int Attempts, int Submitted, double? Average);
public sealed record MarkingItemDto(Guid AnswerId, Guid AttemptId, string Student, string ExamTitle, DateTime SubmittedAtUtc, string Question, string? ModelAnswer,
    IReadOnlyList<RubricCriterion> Rubric, string Answer, int MaxMarks, double? AiScore, IReadOnlyList<CriterionMark> AiCriteria, string? AiFeedback,
    double? TeacherScore, string? TeacherComment, string Status);
public sealed record ConfirmMarkRequest(double Score, string? Comment);

/// <summary>Written questions and mock exams (content team), class tests and marking (teachers).</summary>
public static class AssessmentAdminEndpoints
{
    private static readonly JsonSerializerOptions Json = ExamService.Json;

    public static void MapAssessmentAdminEndpoints(this RouteGroupBuilder v1)
    {
        var cm = v1.MapGroup("/curriculum/admin").WithTags("Written questions and mock exams").RequireAuthorization(Policies.ContentManager);

        cm.MapGet("/chapters/{chapterId:guid}/written-questions", async (Guid chapterId, GoogolsDbContext db, CancellationToken ct) =>
        {
            var rows = await db.WrittenQuestions.AsNoTracking().Where(q => q.ChapterId == chapterId).OrderBy(q => q.CreatedAtUtc).ToListAsync(ct);
            return rows.Select(q => new AdminWrittenQuestionDto(q.Id, q.Prompt, q.ModelAnswer, RubricMarking.ParseRubric(q.RubricJson), q.MaxMarks, q.Language, q.Status.ToString()));
        });

        cm.MapPost("/chapters/{chapterId:guid}/written-questions", async (Guid chapterId, WrittenQuestionRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (ValidateWritten(req) is { } problem)
            {
                return problem;
            }
            var medium = await db.Chapters.Where(c => c.Id == chapterId).Select(c => c.Subject!.MediumLanguage).FirstOrDefaultAsync(ct);
            if (medium is null)
            {
                return Results.NotFound();
            }
            var q = new WrittenQuestion { ChapterId = chapterId, Prompt = "", RubricJson = "[]" };
            ApplyWritten(q, req, medium);
            db.WrittenQuestions.Add(q);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/curriculum/admin/written-questions/{q.Id}", new { q.Id });
        });

        cm.MapPut("/written-questions/{id:guid}", async (Guid id, WrittenQuestionRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (ValidateWritten(req) is { } problem)
            {
                return problem;
            }
            var q = await db.WrittenQuestions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (q is null)
            {
                return Results.NotFound();
            }
            ApplyWritten(q, req, q.Language);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        cm.MapPut("/written-questions/{id:guid}/status", async (Guid id, SetStatusRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!PublicationStatusParser.TryParseAdminStatus(req.Status, out var status))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Choose Draft, Published or Archived."] });
            }
            var n = await db.WrittenQuestions.Where(q => q.Id == id).ExecuteUpdateAsync(u => u.SetProperty(q => q.Status, status), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        cm.MapDelete("/written-questions/{id:guid}", async (Guid id, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var n = await db.WrittenQuestions.Where(q => q.Id == id).ExecuteUpdateAsync(u => u
                .SetProperty(q => q.ArchivedAtUtc, clock.UtcNow).SetProperty(q => q.Status, PublicationStatus.Archived), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Mock exams: one per class, published to every student enrolled in it.
        cm.MapGet("/exams", async (Guid? classId, GoogolsDbContext db, CancellationToken ct) =>
            await ListAsync(db.Exams.Where(e => e.Kind == ExamKind.Mock && (classId == null || e.ClassId == classId)), db, ct));

        cm.MapPost("/exams", async (ExamRequest req, GoogolsDbContext db, ExamService exams, CancellationToken ct) =>
        {
            var (exam, problem) = await BuildExamAsync(req, ExamKind.Mock, null, db, exams, ct);
            if (problem is not null)
            {
                return problem;
            }
            db.Exams.Add(exam!);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/curriculum/admin/exams/{exam!.Id}", new { exam.Id });
        });

        cm.MapPut("/exams/{id:guid}/status", async (Guid id, SetStatusRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!PublicationStatusParser.TryParseAdminStatus(req.Status, out var status))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Choose Draft, Published or Archived."] });
            }
            var n = await db.Exams.Where(e => e.Id == id && e.Kind == ExamKind.Mock).ExecuteUpdateAsync(u => u.SetProperty(e => e.Status, status), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        cm.MapDelete("/exams/{id:guid}", async (Guid id, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var n = await db.Exams.Where(e => e.Id == id && e.Kind == ExamKind.Mock).ExecuteUpdateAsync(u => u.SetProperty(e => e.ArchivedAtUtc, clock.UtcNow), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        // ---- Teachers: class tests and marking written answers.
        var t = v1.MapGroup("/teacher").WithTags("Class tests and marking").RequireAuthorization(Policies.Teacher);

        t.MapGet("/classrooms/{id:guid}/exams", async (Guid id, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
            await db.Classrooms.AnyAsync(c => c.Id == id && c.TeacherUserId == me.UserId, ct)
                ? Results.Ok(await ListAsync(db.Exams.Where(e => e.Kind == ExamKind.ClassTest && e.ClassroomId == id), db, ct))
                : Results.NotFound());

        t.MapPost("/classrooms/{id:guid}/exams", async (Guid id, ExamRequest req, ICurrentUser me, GoogolsDbContext db, ExamService exams, CancellationToken ct) =>
        {
            var room = await db.Classrooms.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.TeacherUserId == me.UserId, ct);
            if (room is null)
            {
                return Results.NotFound();
            }
            var (exam, problem) = await BuildExamAsync(req with { ClassId = room.ClassId }, ExamKind.ClassTest, room.Id, db, exams, ct);
            if (problem is not null)
            {
                return problem;
            }
            exam!.Status = PublicationStatus.Published; // a teacher's test goes live for the class straight away
            db.Exams.Add(exam);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/teacher/exams/{exam.Id}", new { exam.Id });
        });

        t.MapDelete("/exams/{id:guid}", async (Guid id, ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var mine = db.Classrooms.Where(c => c.TeacherUserId == me.UserId).Select(c => (Guid?)c.Id);
            var n = await db.Exams.Where(e => e.Id == id && e.Kind == ExamKind.ClassTest && mine.Contains(e.ClassroomId))
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.ArchivedAtUtc, clock.UtcNow), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Written answers from this classroom's students that the AI marked (provisional) or couldn't mark.
        t.MapGet("/classrooms/{id:guid}/marking", async (Guid id, bool? all, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Classrooms.AnyAsync(c => c.Id == id && c.TeacherUserId == me.UserId, ct))
            {
                return Results.NotFound();
            }
            var members = db.ClassroomMembers.Where(m => m.ClassroomId == id).Select(m => m.StudentId);
            var rows = await (from w in db.WrittenAnswers
                              join a in db.ExamAttempts on w.AttemptId equals a.Id
                              join e in db.Exams.IgnoreQueryFilters() on a.ExamId equals e.Id
                              join q in db.WrittenQuestions.IgnoreQueryFilters() on w.QuestionId equals q.Id
                              join s in db.StudentProfiles on w.StudentId equals s.Id
                              where members.Contains(w.StudentId) && a.SubmittedAtUtc != null && (all == true || w.Status != MarkingStatus.Confirmed)
                              orderby a.SubmittedAtUtc descending
                              select new { w, a.SubmittedAtUtc, e.Title, q.Prompt, q.ModelAnswer, q.RubricJson, s.DisplayName }).Take(100).ToListAsync(ct);
            return Results.Ok(rows.Select(r =>
            {
                var ai = r.w.AiFeedbackJson is { } j ? JsonSerializer.Deserialize<RubricResult>(j, Json) : null;
                return new MarkingItemDto(r.w.Id, r.w.AttemptId, r.DisplayName, r.Title, r.SubmittedAtUtc!.Value, r.Prompt, r.ModelAnswer,
                    RubricMarking.ParseRubric(r.RubricJson), r.w.AnswerText, r.w.MaxMarks, r.w.AiScore, ai?.Criteria ?? [], ai?.Feedback,
                    r.w.TeacherScore, r.w.TeacherComment, r.w.Status.ToString());
            }));
        });

        // Confirm or change a mark. The teacher must teach a classroom the student belongs to.
        t.MapPut("/written-answers/{id:guid}", async (Guid id, ConfirmMarkRequest req, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var w = await db.WrittenAnswers.FirstOrDefaultAsync(x => x.Id == id, ct);
            var teaches = w is not null && await (from m in db.ClassroomMembers join c in db.Classrooms on m.ClassroomId equals c.Id
                                                  where m.StudentId == w.StudentId && c.TeacherUserId == me.UserId select m).AnyAsync(ct);
            if (w is null || !teaches)
            {
                return Results.NotFound();
            }
            if (req.Score < 0 || req.Score > w.MaxMarks || Math.Abs(req.Score * 2 - Math.Round(req.Score * 2)) > 1e-9)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["score"] = [$"Give 0 to {w.MaxMarks} marks, in steps of 0.5."] });
            }
            var comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
            if (comment?.Length > 1000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["comment"] = ["Keep the comment under 1000 characters."] });
            }
            w.TeacherScore = req.Score;
            w.TeacherComment = comment;
            w.MarkedByUserId = me.UserId;
            w.Status = MarkingStatus.Confirmed;
            await db.SaveChangesAsync(ct);

            var attempt = await db.ExamAttempts.FirstAsync(a => a.Id == w.AttemptId, ct);
            var marks = await db.WrittenAnswers.Where(x => x.AttemptId == attempt.Id).Select(x => x.TeacherScore ?? x.AiScore ?? 0).ToListAsync(ct);
            attempt.WrittenScore = marks.Sum();
            attempt.Percent = ExamService.Percent(attempt);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static IResult? ValidateWritten(WrittenQuestionRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Prompt) || req.Prompt.Length > 1000)
        {
            errors["prompt"] = ["Write the question (up to 1000 characters)."];
        }
        if ((req.ModelAnswer?.Length ?? 0) > 3000)
        {
            errors["modelAnswer"] = ["Keep the model answer under 3000 characters."];
        }
        if (RubricMarking.Validate(req.Rubric) is { } rubricError)
        {
            errors["rubric"] = [rubricError];
        }
        if (req.Language is not null && !LanguageCodes.IsSupported(req.Language))
        {
            errors["language"] = ["Use en or ml."];
        }
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static void ApplyWritten(WrittenQuestion q, WrittenQuestionRequest req, string defaultLanguage)
    {
        var rubric = req.Rubric.Select(c => new RubricCriterion(c.Criterion.Trim(), c.Marks)).ToList();
        q.Prompt = req.Prompt.Trim();
        q.ModelAnswer = string.IsNullOrWhiteSpace(req.ModelAnswer) ? null : req.ModelAnswer.Trim();
        q.RubricJson = JsonSerializer.Serialize(rubric, Json);
        q.MaxMarks = rubric.Sum(c => c.Marks);
        q.Language = (req.Language ?? defaultLanguage).ToLowerInvariant();
    }

    private static async Task<(Exam? Exam, IResult? Problem)> BuildExamAsync(ExamRequest req, ExamKind kind, Guid? classroomId, GoogolsDbContext db, ExamService exams, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 200)
        {
            errors["title"] = ["Give the test a title (up to 200 characters)."];
        }
        if (req.McqCount is < 0 or > Exam.MaxMcq || req.WrittenCount is < 0 or > Exam.MaxWritten || req.McqCount + req.WrittenCount == 0)
        {
            errors["mcqCount"] = [$"Use 0–{Exam.MaxMcq} multiple-choice and 0–{Exam.MaxWritten} written questions (at least one question)."];
        }
        if (req.DurationMinutes is < 5 or > Exam.MaxMinutes)
        {
            errors["durationMinutes"] = [$"Choose 5 to {Exam.MaxMinutes} minutes."];
        }
        if (req.OpensAtUtc is { } o && req.ClosesAtUtc is { } c && c <= o)
        {
            errors["closesAtUtc"] = ["The closing time must be after the opening time."];
        }
        if (!await db.AcademicClasses.AnyAsync(k => k.Id == req.ClassId, ct))
        {
            errors["classId"] = ["Choose a class."];
        }
        var chapters = await exams.ValidChaptersAsync(req.ClassId, req.SubjectId, req.ChapterIds ?? [], ct);
        if (chapters.Count == 0)
        {
            errors["chapterIds"] = ["Choose at least one published chapter of this class."];
        }
        if (errors.Count > 0)
        {
            return (null, Results.ValidationProblem(errors));
        }
        return (new Exam
        {
            ClassId = req.ClassId,
            SubjectId = req.SubjectId,
            Title = req.Title.Trim(),
            Kind = kind,
            ChapterIdsJson = JsonSerializer.Serialize(chapters, Json),
            McqCount = req.McqCount,
            WrittenCount = req.WrittenCount,
            DurationMinutes = req.DurationMinutes,
            OpensAtUtc = req.OpensAtUtc,
            ClosesAtUtc = req.ClosesAtUtc,
            ClassroomId = classroomId,
        }, null);
    }

    private static async Task<List<ExamAdminDto>> ListAsync(IQueryable<Exam> q, GoogolsDbContext db, CancellationToken ct)
    {
        var rows = await q.AsNoTracking().OrderByDescending(e => e.CreatedAtUtc).Take(100)
            .Select(e => new
            {
                e,
                Attempts = db.ExamAttempts.Count(a => a.ExamId == e.Id),
                Submitted = db.ExamAttempts.Count(a => a.ExamId == e.Id && a.SubmittedAtUtc != null),
                Average = db.ExamAttempts.Where(a => a.ExamId == e.Id && a.SubmittedAtUtc != null).Average(a => a.Percent),
            }).ToListAsync(ct);
        return rows.Select(r => new ExamAdminDto(r.e.Id, r.e.Title, r.e.Kind.ToString(), r.e.ClassId, r.e.SubjectId, ExamService.Ids(r.e.ChapterIdsJson),
            r.e.McqCount, r.e.WrittenCount, r.e.DurationMinutes, r.e.OpensAtUtc, r.e.ClosesAtUtc, r.e.Status.ToString(), r.Attempts, r.Submitted,
            r.Average is { } avg ? Math.Round(avg, 1) : null)).ToList();
    }
}
