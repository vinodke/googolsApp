using System.Text.Json;
using Googols.Application.Abstractions;
using Googols.Application.Ai;
using Googols.Application.Assessment;
using Googols.Application.Gamification;
using Googols.Application.Learning;
using Googols.Domain.Assessment;
using Googols.Domain.Common;
using Googols.Domain.Students;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

public sealed record ExamSummaryDto(Guid Id, string Title, string Kind, string? Subject, int McqCount, int WrittenCount, int DurationMinutes,
    DateTime? OpensAtUtc, DateTime? ClosesAtUtc, string? Classroom, Guid? MyAttemptId, bool Submitted, double? Percent, bool CanStart);
public sealed record ExamHistoryDto(Guid AttemptId, Guid ExamId, string Title, string Kind, DateTime StartedAtUtc, DateTime? SubmittedAtUtc,
    double? Percent, bool Provisional);
public sealed record PracticeTestRequest(Guid SubjectId, IReadOnlyList<Guid> ChapterIds, int QuestionCount, int Minutes, bool IncludeWritten);
public sealed record WrittenPromptDto(Guid Id, string Prompt, int MaxMarks);
public sealed record WrittenDraftDto(Guid QuestionId, string Text);
public sealed record SaveAnswersRequest(IReadOnlyList<int?>? Answers, IReadOnlyList<WrittenDraftDto>? Written);
public sealed record WrittenResultDto(Guid AnswerId, Guid QuestionId, string Prompt, string Answer, double? Score, int Max, string Status,
    IReadOnlyList<CriterionMark> Criteria, string? Feedback, string? TeacherComment, string? ModelAnswer);
public sealed record ExamResultDto(int McqScore, int McqTotal, double? WrittenScore, int WrittenMax, double? Percent, bool Provisional, int XpAwarded,
    IReadOnlyList<QuizReviewItem> Review, IReadOnlyList<WrittenResultDto> Written, IReadOnlyList<string> NewBadges, bool Late);
public sealed record ExamAttemptDto(Guid AttemptId, Guid ExamId, string Title, string Kind, DateTime StartedAtUtc, DateTime DeadlineUtc, DateTime ServerNowUtc,
    bool Submitted, IReadOnlyList<QuizQuestionDto> Questions, IReadOnlyList<WrittenPromptDto> Written, IReadOnlyList<int?> Answers,
    IReadOnlyList<WrittenDraftDto> WrittenAnswers, ExamResultDto? Result);

/// <summary>Updates chapter mastery from graded work. Called by quiz and test submission only.</summary>
public sealed class MasteryService(GoogolsDbContext db, IClock clock)
{
    public sealed record ChapterResult(Guid ChapterId, double Correct, double Answered);

    public async Task RecordAsync(Guid studentId, IEnumerable<ChapterResult> results, CancellationToken ct)
    {
        foreach (var g in results.Where(r => r.Answered > 0).GroupBy(r => r.ChapterId))
        {
            var correct = g.Sum(r => r.Correct);
            var answered = g.Sum(r => r.Answered);
            var percent = Math.Round(100.0 * correct / answered, 1);
            var row = await db.ChapterMastery.FirstOrDefaultAsync(m => m.StudentId == studentId && m.ChapterId == g.Key, ct);
            if (row is null)
            {
                var subjectId = await db.Chapters.Where(c => c.Id == g.Key).Select(c => c.SubjectId).FirstAsync(ct);
                row = new ChapterMastery { StudentId = studentId, ChapterId = g.Key, SubjectId = subjectId, PracticeScore = percent };
                db.ChapterMastery.Add(row);
            }
            else
            {
                row.PracticeScore = MasteryPolicy.Next(row.PracticeScore, percent);
            }
            row.Attempts++;
            row.Correct += (int)Math.Round(correct);
            row.Answered += (int)Math.Round(answered);
            row.LastPracticedAtUtc = clock.UtcNow;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear(); // two submissions at once created the same row; the other one counted
        }
    }
}

/// <summary>Timed tests: build, start, autosave, submit, grade (MCQ on the server, written answers by AI against a rubric).</summary>
public sealed class ExamService(
    GoogolsDbContext db, IClock clock, ICurrentUser me, IAiModelProvider model, AiUsageRecorder usage, MasteryService mastery,
    XpLedgerService xp, LearningActivityService activity, BadgeService badges, ILogger<ExamService> log)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static List<Guid> Ids(string json) => JsonSerializer.Deserialize<List<Guid>>(json, Json) ?? [];
    private static IReadOnlyList<string> Options(string json) => JsonSerializer.Deserialize<List<string>>(json, Json) ?? [];

    /// <summary>Published chapters of the class (and subject, if given) among <paramref name="chapterIds"/>.</summary>
    public async Task<List<Guid>> ValidChaptersAsync(Guid classId, Guid? subjectId, IEnumerable<Guid> chapterIds, CancellationToken ct)
    {
        var ids = chapterIds.Distinct().Take(30).ToList();
        return await db.Chapters.Where(c => ids.Contains(c.Id) && c.Status == PublicationStatus.Published && c.Subject!.ClassId == classId
            && (subjectId == null || c.SubjectId == subjectId)).Select(c => c.Id).ToListAsync(ct);
    }

    public async Task<IResult> StartAsync(Exam exam, StudentAccess.Context ctx, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var open = await db.ExamAttempts.Where(a => a.ExamId == exam.Id && a.StudentId == ctx.Student.Id).OrderByDescending(a => a.StartedAtUtc).FirstOrDefaultAsync(ct);
        if (open is not null && open.SubmittedAtUtc is null && now <= open.DeadlineUtc + ExamAttempt.Grace)
        {
            return Results.Ok(await ViewAsync(open, ct)); // resume (page refresh, phone locked)
        }
        if (open is not null && exam.Kind != ExamKind.Practice)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You have already taken this test.");
        }

        var chapters = Ids(exam.ChapterIdsJson);
        var bank = await db.Questions.AsNoTracking().Where(q => chapters.Contains(q.ChapterId) && q.Status == PublicationStatus.Published)
            .Select(q => new { q.Id, q.Difficulty }).ToListAsync(ct);
        var written = await db.WrittenQuestions.AsNoTracking().Where(q => chapters.Contains(q.ChapterId) && q.Status == PublicationStatus.Published)
            .Select(q => q.Id).ToListAsync(ct);
        var seed = Random.Shared.Next();
        var mcqIds = QuizGrading.Pick(bank.Select(b => (b.Id, b.Difficulty)).ToList(), exam.McqCount, seed);
        var writtenIds = written.OrderBy(_ => Random.Shared.Next()).Take(exam.WrittenCount).ToList();
        if (mcqIds.Count == 0 && writtenIds.Count == 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "There are no published questions for these chapters yet.");
        }
        var attempt = new ExamAttempt
        {
            ExamId = exam.Id,
            StudentId = ctx.Student.Id,
            QuestionIdsJson = JsonSerializer.Serialize(mcqIds, Json),
            WrittenIdsJson = JsonSerializer.Serialize(writtenIds, Json),
            StartedAtUtc = now,
            DeadlineUtc = now.AddMinutes(exam.DurationMinutes),
            McqTotal = mcqIds.Count,
        };
        if (exam.ClosesAtUtc is { } closes && attempt.DeadlineUtc > closes)
        {
            attempt.DeadlineUtc = closes; // a test that closes at 5 pm ends at 5 pm for everyone
        }
        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ViewAsync(attempt, ct));
    }

    public async Task<ExamAttemptDto> ViewAsync(ExamAttempt a, CancellationToken ct)
    {
        var exam = await db.Exams.IgnoreQueryFilters().AsNoTracking().FirstAsync(e => e.Id == a.ExamId, ct);
        var mcqIds = Ids(a.QuestionIdsJson);
        var writtenIds = Ids(a.WrittenIdsJson);
        var questions = await db.Questions.IgnoreQueryFilters().AsNoTracking().Where(q => mcqIds.Contains(q.Id)).ToListAsync(ct);
        var prompts = await db.WrittenQuestions.IgnoreQueryFilters().AsNoTracking().Where(q => writtenIds.Contains(q.Id)).ToListAsync(ct);
        var drafts = await db.WrittenAnswers.AsNoTracking().Where(w => w.AttemptId == a.Id).ToListAsync(ct);
        var answers = string.IsNullOrEmpty(a.AnswersJson) ? mcqIds.Select(_ => (int?)null).ToList() : JsonSerializer.Deserialize<List<int?>>(a.AnswersJson, Json)!;

        ExamResultDto? result = null;
        if (a.SubmittedAtUtc is not null)
        {
            result = BuildResult(a, mcqIds.Select(id => questions.First(q => q.Id == id)).ToList(), answers,
                writtenIds.Select(id => prompts.First(q => q.Id == id)).ToList(), drafts, 0, [], false);
        }
        return new ExamAttemptDto(a.Id, a.ExamId, exam.Title, exam.Kind.ToString(), a.StartedAtUtc, a.DeadlineUtc, clock.UtcNow, a.SubmittedAtUtc is not null,
            mcqIds.Select(id => questions.First(q => q.Id == id)).Select(q => new QuizQuestionDto(q.Id, q.Prompt, Options(q.OptionsJson))).ToList(),
            writtenIds.Select(id => prompts.First(q => q.Id == id)).Select(q => new WrittenPromptDto(q.Id, q.Prompt, q.MaxMarks)).ToList(),
            answers, drafts.Select(d => new WrittenDraftDto(d.QuestionId, d.AnswerText)).ToList(), result);
    }

    private static ExamResultDto BuildResult(ExamAttempt a, IReadOnlyList<Question> questions, IReadOnlyList<int?> answers,
        IReadOnlyList<WrittenQuestion> prompts, IReadOnlyList<WrittenAnswer> written, int xpAwarded, IReadOnlyList<string> newBadges, bool late)
    {
        var review = questions.Select((q, i) =>
        {
            var chosen = i < answers.Count ? answers[i] : null;
            return new QuizReviewItem(q.Id, q.Prompt, Options(q.OptionsJson), chosen, q.CorrectIndex, chosen == q.CorrectIndex, q.Explanation);
        }).ToList();
        var writtenRows = prompts.Select(p =>
        {
            var w = written.FirstOrDefault(x => x.QuestionId == p.Id);
            var fb = w?.AiFeedbackJson is { } j ? JsonSerializer.Deserialize<RubricResult>(j, Json) : null;
            return new WrittenResultDto(w?.Id ?? Guid.Empty, p.Id, p.Prompt, w?.AnswerText ?? "", w?.FinalScore, p.MaxMarks, (w?.Status ?? MarkingStatus.NotMarked).ToString(),
                fb?.Criteria ?? [], fb?.Feedback, w?.TeacherComment, p.ModelAnswer);
        }).ToList();
        var provisional = written.Any(w => w.Status != MarkingStatus.Confirmed);
        return new ExamResultDto(a.McqScore, a.McqTotal, a.WrittenScore, a.WrittenMax, a.Percent, provisional && prompts.Count > 0, xpAwarded, review, writtenRows, newBadges, late);
    }

    public async Task<IResult> SaveAsync(ExamAttempt a, SaveAnswersRequest req, CancellationToken ct)
    {
        if (a.SubmittedAtUtc is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This test was already submitted.");
        }
        if (clock.UtcNow > a.DeadlineUtc + ExamAttempt.Grace)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Time is up. Submit to see your result.");
        }
        if (Validate(a, req) is { } problem)
        {
            return problem;
        }
        await ApplyAsync(a, req, ct);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static IResult? Validate(ExamAttempt a, SaveAnswersRequest req)
    {
        var mcq = Ids(a.QuestionIdsJson).Count;
        if (req.Answers is { } answers && answers.Count != mcq)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["answers"] = [$"Send one answer per question ({mcq})."] });
        }
        if (req.Written?.Any(w => (w.Text?.Length ?? 0) > RubricMarking.MaxAnswerLength) == true)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["written"] = [$"Written answers can be up to {RubricMarking.MaxAnswerLength} characters."] });
        }
        return null;
    }

    private async Task ApplyAsync(ExamAttempt a, SaveAnswersRequest req, CancellationToken ct)
    {
        if (req.Answers is { } answers)
        {
            a.AnswersJson = JsonSerializer.Serialize(answers, Json);
        }
        if (req.Written is { Count: > 0 } written)
        {
            var allowed = Ids(a.WrittenIdsJson).ToHashSet();
            var rows = await db.WrittenAnswers.Where(w => w.AttemptId == a.Id).ToListAsync(ct);
            var max = await db.WrittenQuestions.IgnoreQueryFilters().Where(q => allowed.Contains(q.Id)).ToDictionaryAsync(q => q.Id, q => q.MaxMarks, ct);
            foreach (var w in written.Where(w => allowed.Contains(w.QuestionId)))
            {
                var row = rows.FirstOrDefault(r => r.QuestionId == w.QuestionId);
                if (row is null)
                {
                    row = new WrittenAnswer { AttemptId = a.Id, QuestionId = w.QuestionId, StudentId = a.StudentId, AnswerText = "", MaxMarks = max[w.QuestionId], Status = MarkingStatus.NotMarked };
                    db.WrittenAnswers.Add(row);
                    rows.Add(row);
                }
                row.AnswerText = (w.Text ?? "").Trim();
            }
        }
    }

    public async Task<IResult> SubmitAsync(ExamAttempt a, SaveAnswersRequest req, StudentAccess.Context ctx, CancellationToken ct)
    {
        if (a.SubmittedAtUtc is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This test was already submitted.");
        }
        var late = clock.UtcNow > a.DeadlineUtc + ExamAttempt.Grace;
        if (!late)
        {
            if (Validate(a, req) is { } problem)
            {
                return problem;
            }
            await ApplyAsync(a, req, ct); // answers sent after time is up are ignored; the last autosave counts
        }

        var exam = await db.Exams.IgnoreQueryFilters().AsNoTracking().FirstAsync(e => e.Id == a.ExamId, ct);
        var mcqIds = Ids(a.QuestionIdsJson);
        var writtenIds = Ids(a.WrittenIdsJson);
        var questions = await db.Questions.IgnoreQueryFilters().AsNoTracking().Where(q => mcqIds.Contains(q.Id)).ToListAsync(ct);
        var ordered = mcqIds.Select(id => questions.First(q => q.Id == id)).ToList();
        var answers = string.IsNullOrEmpty(a.AnswersJson) ? mcqIds.Select(_ => (int?)null).ToList() : JsonSerializer.Deserialize<List<int?>>(a.AnswersJson, Json)!;
        var graded = QuizGrading.Grade(ordered.Select(q => new QuizGrading.Key(q.Id, q.CorrectIndex)).ToList(), answers);

        // Written answers: AI marks each against its rubric (provisional until a teacher confirms).
        var prompts = await db.WrittenQuestions.IgnoreQueryFilters().AsNoTracking().Where(q => writtenIds.Contains(q.Id)).ToListAsync(ct);
        var promptsOrdered = writtenIds.Select(id => prompts.First(q => q.Id == id)).ToList();
        var rows = await db.WrittenAnswers.Where(w => w.AttemptId == a.Id).ToListAsync(ct);
        foreach (var p in promptsOrdered.Where(p => rows.All(r => r.QuestionId != p.Id)))
        {
            var row = new WrittenAnswer { AttemptId = a.Id, QuestionId = p.Id, StudentId = a.StudentId, AnswerText = "", MaxMarks = p.MaxMarks, Status = MarkingStatus.NotMarked };
            db.WrittenAnswers.Add(row);
            rows.Add(row);
        }
        var grade = await db.AcademicClasses.Where(k => k.Id == exam.ClassId).Select(k => k.GradeNumber).FirstOrDefaultAsync(ct);
        await MarkAsync(promptsOrdered, rows, grade, ctx.Student.Id, ct);

        a.McqScore = graded.Score;
        a.McqTotal = graded.Total;
        a.WrittenMax = promptsOrdered.Sum(p => p.MaxMarks);
        a.WrittenScore = promptsOrdered.Count == 0 ? null : rows.Sum(r => r.FinalScore ?? 0);
        a.Percent = Percent(a);
        a.SubmittedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        var today = clock.TodayIn("Asia/Kolkata");
        var awarded = await xp.AwardAsync(new XpAwardPolicy.LearningEvent("exam-completed", ctx.Student.Id, exam.Id, a.Percent,
            Key: exam.Kind == ExamKind.Practice ? $"practice:{today:yyyy-MM-dd}" : null), ct);
        await activity.RecordAsync(ctx.Student, ct);

        // Mastery per chapter: multiple-choice marks plus written marks.
        var chapterOfWritten = promptsOrdered.ToDictionary(p => p.Id, p => p.ChapterId);
        var perChapter = ordered.Select((q, i) => new MasteryService.ChapterResult(q.ChapterId, graded.PerQuestion[i] ? 1 : 0, 1))
            .Concat(rows.Where(r => r.FinalScore is not null).Select(r => new MasteryService.ChapterResult(chapterOfWritten[r.QuestionId], r.FinalScore!.Value, r.MaxMarks)));
        await mastery.RecordAsync(ctx.Student.Id, perChapter, ct);
        var newBadges = await badges.EvaluateAsync(ctx.Student.Id, ct);

        return Results.Ok(BuildResult(a, ordered, answers, promptsOrdered, rows, awarded, newBadges, late));
    }

    public static double? Percent(ExamAttempt a)
    {
        var max = a.McqTotal + a.WrittenMax;
        return max == 0 ? null : Math.Round(100.0 * (a.McqScore + (a.WrittenScore ?? 0)) / max, 1);
    }

    private async Task MarkAsync(IReadOnlyList<WrittenQuestion> prompts, List<WrittenAnswer> rows, int grade, Guid studentId, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(3);
        var jobs = prompts.Select(async p =>
        {
            var row = rows.First(r => r.QuestionId == p.Id);
            var rubric = RubricMarking.ParseRubric(p.RubricJson);
            if (string.IsNullOrWhiteSpace(row.AnswerText))
            {
                return (row, (RubricResult?)new RubricResult(0, p.MaxMarks, rubric.Select(c => new CriterionMark(c.Criterion, 0, c.Marks, null)).ToList(), "No answer was written."), (AiCompletionResult?)null);
            }
            await gate.WaitAsync(ct);
            try
            {
                var reply = await model.CompleteAsync(new AiCompletionRequest(RubricMarking.SystemPrompt, [],
                    RubricMarking.BuildPrompt(p.Prompt, p.ModelAnswer, rubric, row.AnswerText, grade, p.Language), 800), ct);
                return (row, RubricMarking.Parse(reply.Text, rubric), (AiCompletionResult?)reply);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "AI marking failed for written answer {Answer}", row.Id);
                return (row, (RubricResult?)null, (AiCompletionResult?)null);
            }
            finally
            {
                gate.Release();
            }
        });
        foreach (var (row, result, reply) in await Task.WhenAll(jobs))
        {
            await usage.RecordAsync("marking", reply, studentId, me.UserId, ct);
            if (result is null)
            {
                row.Status = MarkingStatus.NotMarked; // a teacher marks it
                continue;
            }
            row.AiScore = result.Score;
            row.AiFeedbackJson = JsonSerializer.Serialize(result, Json);
            row.Status = MarkingStatus.Provisional;
        }
    }
}

public static class ExamEndpoints
{
    public static void MapExamEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/learning").WithTags("Tests").RequireAuthorization(Policies.Student);

        // Mock exams for my class, tests set by my teachers, and my own practice tests.
        g.MapGet("/exams", async (StudentAccess access, GoogolsDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var now = clock.UtcNow;
            var rooms = db.ClassroomMembers.Where(m => m.StudentId == ctx.Student.Id).Select(m => m.ClassroomId);
            var exams = await db.Exams.AsNoTracking()
                .Where(e => e.Status == PublicationStatus.Published && e.ClassId == ctx.ClassId
                    && ((e.Kind == ExamKind.Mock) || (e.Kind == ExamKind.ClassTest && rooms.Contains(e.ClassroomId!.Value))))
                .OrderByDescending(e => e.CreatedAtUtc).Take(50)
                .Select(e => new
                {
                    e.Id, e.Title, e.Kind, e.McqCount, e.WrittenCount, e.DurationMinutes, e.OpensAtUtc, e.ClosesAtUtc,
                    Subject = db.Subjects.Where(s => s.Id == e.SubjectId).Select(s => s.Name).FirstOrDefault(),
                    Classroom = db.Classrooms.Where(c => c.Id == e.ClassroomId).Select(c => c.Name).FirstOrDefault(),
                    Attempt = db.ExamAttempts.Where(a => a.ExamId == e.Id && a.StudentId == ctx.Student.Id).OrderByDescending(a => a.StartedAtUtc)
                        .Select(a => new { a.Id, a.SubmittedAtUtc, a.Percent }).FirstOrDefault(),
                }).ToListAsync(ct);
            return Results.Ok(exams.Select(e => new ExamSummaryDto(e.Id, e.Title, e.Kind.ToString(), e.Subject, e.McqCount, e.WrittenCount, e.DurationMinutes,
                e.OpensAtUtc, e.ClosesAtUtc, e.Classroom, e.Attempt?.Id, e.Attempt?.SubmittedAtUtc != null, e.Attempt?.Percent,
                (e.OpensAtUtc == null || e.OpensAtUtc <= now) && (e.ClosesAtUtc == null || e.ClosesAtUtc > now) && e.Attempt?.SubmittedAtUtc == null)));
        });

        g.MapGet("/exam-attempts", async (StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var rows = await (from a in db.ExamAttempts
                              join e in db.Exams.IgnoreQueryFilters() on a.ExamId equals e.Id
                              where a.StudentId == ctx.Student.Id
                              orderby a.StartedAtUtc descending
                              select new
                              {
                                  a.Id, a.ExamId, e.Title, e.Kind, a.StartedAtUtc, a.SubmittedAtUtc, a.Percent,
                                  Provisional = db.WrittenAnswers.Any(w => w.AttemptId == a.Id && w.Status != MarkingStatus.Confirmed),
                              }).Take(30).ToListAsync(ct);
            return Results.Ok(rows.Select(r => new ExamHistoryDto(r.Id, r.ExamId, r.Title, r.Kind.ToString(), r.StartedAtUtc, r.SubmittedAtUtc, r.Percent, r.Provisional)));
        });

        // A timed practice test the student builds from their own chapters.
        g.MapPost("/practice-tests", async (PracticeTestRequest req, StudentAccess access, ExamService exams, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var errors = new Dictionary<string, string[]>();
            if (req.QuestionCount is < 5 or > Exam.MaxMcq)
            {
                errors["questionCount"] = [$"Choose 5 to {Exam.MaxMcq} questions."];
            }
            if (req.Minutes is < 5 or > Exam.MaxMinutes)
            {
                errors["minutes"] = [$"Choose 5 to {Exam.MaxMinutes} minutes."];
            }
            var subject = await db.Subjects.Where(s => s.Id == req.SubjectId && s.ClassId == ctx.ClassId).Select(s => s.Name).FirstOrDefaultAsync(ct);
            if (subject is null)
            {
                errors["subjectId"] = ["Choose one of your subjects."];
            }
            var chapters = await exams.ValidChaptersAsync(ctx.ClassId, req.SubjectId, req.ChapterIds ?? [], ct);
            if (chapters.Count == 0)
            {
                errors["chapterIds"] = ["Choose at least one chapter."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }
            var exam = new Exam
            {
                ClassId = ctx.ClassId,
                SubjectId = req.SubjectId,
                Title = $"{subject} practice test",
                Kind = ExamKind.Practice,
                ChapterIdsJson = JsonSerializer.Serialize(chapters, ExamService.Json),
                McqCount = req.QuestionCount,
                WrittenCount = req.IncludeWritten ? 2 : 0,
                DurationMinutes = req.Minutes,
                Status = PublicationStatus.Published,
                CreatedForStudentId = ctx.Student.Id,
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync(ct);
            return await exams.StartAsync(exam, ctx, ct);
        });

        g.MapPost("/exams/{id:guid}/start", async (Guid id, StudentAccess access, ExamService exams, GoogolsDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var rooms = db.ClassroomMembers.Where(m => m.StudentId == ctx.Student.Id).Select(m => m.ClassroomId);
            var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == id && e.Status == PublicationStatus.Published && e.ClassId == ctx.ClassId
                && (e.Kind == ExamKind.Mock || (e.Kind == ExamKind.ClassTest && rooms.Contains(e.ClassroomId!.Value))
                    || (e.Kind == ExamKind.Practice && e.CreatedForStudentId == ctx.Student.Id)), ct);
            if (exam is null)
            {
                return Results.NotFound();
            }
            var now = clock.UtcNow;
            if (exam.OpensAtUtc > now)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: $"This test opens on {exam.OpensAtUtc:dd MMM, h:mm tt} UTC.");
            }
            if (exam.ClosesAtUtc <= now)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This test has closed.");
            }
            return await exams.StartAsync(exam, ctx, ct);
        });

        g.MapGet("/exam-attempts/{id:guid}", async (Guid id, StudentAccess access, ExamService exams, GoogolsDbContext db, CancellationToken ct) =>
            await access.GetAsync(ct) is { } ctx && await db.ExamAttempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.StudentId == ctx.Student.Id, ct) is { } a
                ? Results.Ok(await exams.ViewAsync(a, ct)) : Results.NotFound());

        g.MapPut("/exam-attempts/{id:guid}/answers", async (Guid id, SaveAnswersRequest req, StudentAccess access, ExamService exams, GoogolsDbContext db, CancellationToken ct) =>
            await access.GetAsync(ct) is { } ctx && await db.ExamAttempts.FirstOrDefaultAsync(a => a.Id == id && a.StudentId == ctx.Student.Id, ct) is { } a
                ? await exams.SaveAsync(a, req, ct) : Results.NotFound());

        g.MapPost("/exam-attempts/{id:guid}/submit", async (Guid id, SaveAnswersRequest req, StudentAccess access, ExamService exams, GoogolsDbContext db, CancellationToken ct) =>
            await access.GetAsync(ct) is { } ctx && await db.ExamAttempts.FirstOrDefaultAsync(a => a.Id == id && a.StudentId == ctx.Student.Id, ct) is { } a
                ? await exams.SubmitAsync(a, req, ctx, ct) : Results.NotFound());
    }
}
