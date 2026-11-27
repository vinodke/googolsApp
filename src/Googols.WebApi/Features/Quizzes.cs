using System.Text.Json;
using FluentValidation;
using Googols.Application.Abstractions;
using Googols.Application.Ai;
using Googols.Application.Assessment;
using Googols.Application.Gamification;
using Googols.Domain.Assessment;
using Googols.Domain.Common;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

/// <summary>Chapter question banks (admin) and chapter quizzes (students). Grading happens only on the server.</summary>
public static class QuizEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapQuizEndpoints(this RouteGroupBuilder v1)
    {
        MapAdmin(v1.MapGroup("/curriculum/admin").WithTags("Question bank").RequireAuthorization(Policies.ContentManager));
        MapStudent(v1.MapGroup("/learning").WithTags("Quizzes").RequireAuthorization(Policies.Student));
    }

    private static IReadOnlyList<string> Options(string json) => JsonSerializer.Deserialize<List<string>>(json, Json) ?? [];

    private static void MapAdmin(RouteGroupBuilder g)
    {
        g.MapGet("/chapters/{chapterId:guid}/questions", async (Guid chapterId, GoogolsDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Questions.AsNoTracking().Where(q => q.ChapterId == chapterId).OrderBy(q => q.CreatedAtUtc).ToListAsync(ct);
            return rows.Select(q => new AdminQuestionDto(q.Id, q.Prompt, Options(q.OptionsJson), q.CorrectIndex, q.Explanation, q.Difficulty,
                q.Language, q.Source.ToString(), q.Status.ToString()));
        });

        g.MapPost("/chapters/{chapterId:guid}/questions", async (Guid chapterId, QuestionUpsertRequest req, IValidator<QuestionUpsertRequest> v,
            GoogolsDbContext db, CancellationToken ct) =>
        {
            var result = await v.ValidateAsync(req, ct);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
            var medium = await db.Chapters.Where(c => c.Id == chapterId).Select(c => c.Subject!.MediumLanguage).FirstOrDefaultAsync(ct);
            if (medium is null)
            {
                return Results.NotFound();
            }
            var q = ToEntity(chapterId, req, req.Language ?? medium, QuestionSource.Manual);
            db.Questions.Add(q);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/curriculum/admin/questions/{q.Id}", new { q.Id });
        });

        g.MapPut("/questions/{id:guid}", async (Guid id, QuestionUpsertRequest req, IValidator<QuestionUpsertRequest> v, GoogolsDbContext db, CancellationToken ct) =>
        {
            var result = await v.ValidateAsync(req, ct);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
            var q = await db.Questions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (q is null)
            {
                return Results.NotFound();
            }
            q.Prompt = req.Prompt.Trim();
            q.OptionsJson = JsonSerializer.Serialize(req.Options.Select(o => o.Trim()), Json);
            q.CorrectIndex = req.CorrectIndex;
            q.Explanation = string.IsNullOrWhiteSpace(req.Explanation) ? null : req.Explanation.Trim();
            q.Difficulty = req.Difficulty;
            q.Language = (req.Language ?? q.Language).ToLowerInvariant();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPut("/questions/{id:guid}/status", async (Guid id, Application.Curriculum.SetStatusRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (!Application.Curriculum.PublicationStatusParser.TryParseAdminStatus(req.Status, out var status))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Choose Draft, Published or Archived."] });
            }
            var n = await db.Questions.Where(q => q.Id == id).ExecuteUpdateAsync(u => u.SetProperty(q => q.Status, status), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        g.MapDelete("/questions/{id:guid}", async (Guid id, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var n = await db.Questions.Where(q => q.Id == id).ExecuteUpdateAsync(u => u
                .SetProperty(q => q.ArchivedAtUtc, clock.UtcNow).SetProperty(q => q.Status, PublicationStatus.Archived), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        // AI drafts from the chapter's lesson text. Always saved as Draft for a person to check and publish.
        g.MapPost("/chapters/{chapterId:guid}/questions/generate", async (Guid chapterId, GenerateQuestionsRequest req, GoogolsDbContext db,
            IAiModelProvider model, AiUsageRecorder usage, ICurrentUser me, ILogger<GenerateQuestionsRequest> log, CancellationToken ct) =>
        {
            var count = Math.Clamp(req.Count, 1, 15);
            var chapter = await db.Chapters.AsNoTracking().Where(c => c.Id == chapterId)
                .Select(c => new { c.Title, c.Subject!.MediumLanguage, c.Subject.Class!.GradeNumber }).FirstOrDefaultAsync(ct);
            if (chapter is null)
            {
                return Results.NotFound();
            }
            var text = string.Join("\n\n", await db.Lessons.AsNoTracking().Where(l => l.ChapterId == chapterId)
                .OrderBy(l => l.Sequence).Select(l => l.Title + "\n" + l.Body).ToListAsync(ct));
            if (text.Length < 200)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["chapter"] = ["Add lesson text to this chapter first. Questions are written from it."] });
            }
            var prompt = QuestionDraftParser.BuildPrompt(chapter.Title, chapter.GradeNumber, chapter.MediumLanguage, text, count);
            AiCompletionResult reply;
            try
            {
                reply = await model.CompleteAsync(new AiCompletionRequest("You write accurate school quiz questions and reply with JSON only.", [], prompt, 3000), ct);
            }
            catch (AiContentFilteredException)
            {
                return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The AI couldn't write questions for this text. Please write them by hand.");
            }
            await usage.RecordAsync("question-draft", reply, null, me.UserId, ct);
            var drafts = QuestionDraftParser.Parse(reply.Text, chapter.MediumLanguage);
            foreach (var d in drafts)
            {
                db.Questions.Add(ToEntity(chapterId, d, chapter.MediumLanguage, QuestionSource.AiDraft));
            }
            await db.SaveChangesAsync(ct);
            log.LogInformation("Generated {Count} draft questions for chapter {Chapter} with {Model} ({Prompt})", drafts.Count, chapterId, reply.Model, QuestionDraftParser.PromptVersion);
            return Results.Ok(new { created = drafts.Count });
        });
    }

    private static Question ToEntity(Guid chapterId, QuestionUpsertRequest r, string language, QuestionSource source) => new()
    {
        ChapterId = chapterId,
        Prompt = r.Prompt.Trim(),
        OptionsJson = JsonSerializer.Serialize(r.Options.Select(o => o.Trim()), Json),
        CorrectIndex = r.CorrectIndex,
        Explanation = string.IsNullOrWhiteSpace(r.Explanation) ? null : r.Explanation.Trim(),
        Difficulty = r.Difficulty,
        Language = language.ToLowerInvariant(),
        Source = source,
    };

    private static void MapStudent(RouteGroupBuilder g)
    {
        g.MapPost("/chapters/{chapterId:guid}/quiz", async (Guid chapterId, StudentAccess access, GoogolsDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var chapter = await db.Chapters.AsNoTracking()
                .Where(c => c.Id == chapterId && c.Status == PublicationStatus.Published && c.Subject!.ClassId == ctx.ClassId)
                .Select(c => new { c.Id, c.Title }).FirstOrDefaultAsync(ct);
            if (chapter is null)
            {
                return Results.NotFound();
            }
            var bank = await db.Questions.AsNoTracking().Where(q => q.ChapterId == chapterId && q.Status == PublicationStatus.Published)
                .Select(q => new { q.Id, q.Difficulty }).ToListAsync(ct);
            if (bank.Count == 0)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no quiz for this chapter yet.");
            }
            var ids = QuizGrading.Pick(bank.Select(b => (b.Id, b.Difficulty)).ToList(), QuizAttempt.QuestionsPerQuiz, Random.Shared.Next());
            var attempt = new QuizAttempt
            {
                StudentId = ctx.Student.Id,
                ChapterId = chapterId,
                QuestionIdsJson = JsonSerializer.Serialize(ids, Json),
                StartedAtUtc = clock.UtcNow,
                Total = ids.Count,
            };
            db.QuizAttempts.Add(attempt);
            await db.SaveChangesAsync(ct);

            var questions = await db.Questions.AsNoTracking().Where(q => ids.Contains(q.Id)).ToListAsync(ct);
            var ordered = ids.Select(id => questions.First(q => q.Id == id))
                .Select(q => new QuizQuestionDto(q.Id, q.Prompt, Options(q.OptionsJson))).ToList(); // no correct answers here
            return Results.Ok(new QuizStartDto(attempt.Id, chapter.Id, chapter.Title, ordered));
        });

        g.MapPost("/quiz-attempts/{attemptId:guid}/submit", async (Guid attemptId, SubmitQuizRequest req, StudentAccess access, GoogolsDbContext db,
            IClock clock, XpLedgerService xp, LearningActivityService activity, BadgeService badges, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var attempt = await db.QuizAttempts.FirstOrDefaultAsync(a => a.Id == attemptId && a.StudentId == ctx.Student.Id, ct);
            if (attempt is null)
            {
                return Results.NotFound();
            }
            if (attempt.SubmittedAtUtc is not null)
            {
                return Results.Conflict(new { message = "This quiz was already submitted." });
            }
            if (clock.UtcNow - attempt.StartedAtUtc > QuizAttempt.MaxDuration)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This quiz has expired. Start a new one.");
            }
            var ids = JsonSerializer.Deserialize<List<Guid>>(attempt.QuestionIdsJson, Json) ?? [];
            if (req.Answers is null || req.Answers.Count != ids.Count)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["answers"] = [$"Send one answer per question ({ids.Count})."] });
            }
            // IgnoreQueryFilters: a question archived after the quiz started is still graded with its frozen key.
            var questions = await db.Questions.IgnoreQueryFilters().AsNoTracking().Where(q => ids.Contains(q.Id)).ToListAsync(ct);
            var ordered = ids.Select(id => questions.First(q => q.Id == id)).ToList();
            var graded = QuizGrading.Grade(ordered.Select(q => new QuizGrading.Key(q.Id, q.CorrectIndex)).ToList(), req.Answers);

            attempt.AnswersJson = JsonSerializer.Serialize(req.Answers, Json);
            attempt.SubmittedAtUtc = clock.UtcNow;
            attempt.Score = graded.Score;
            attempt.Total = graded.Total;
            attempt.Percent = graded.Percent;
            await db.SaveChangesAsync(ct);

            // XP once per chapter (not per attempt), so retaking a quiz can't farm points.
            var awarded = await xp.AwardAsync(new XpAwardPolicy.LearningEvent("quiz-completed", ctx.Student.Id, attempt.ChapterId, graded.Percent), ct);
            var streak = await activity.RecordAsync(ctx.Student, ct);

            var review = ordered.Select((q, i) => new QuizReviewItem(q.Id, q.Prompt, Options(q.OptionsJson), req.Answers[i], q.CorrectIndex, graded.PerQuestion[i], q.Explanation)).ToList();
            var newBadges = await badges.EvaluateAsync(ctx.Student.Id, ct);
            return Results.Ok(new QuizResultDto(attempt.Id, graded.Score, graded.Total, graded.Percent, awarded, streak, review, newBadges));
        });

        g.MapGet("/chapters/{chapterId:guid}/quiz-history", async (Guid chapterId, StudentAccess access, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await access.GetAsync(ct) is not { } ctx)
            {
                return Results.NotFound();
            }
            var published = await db.Questions.CountAsync(q => q.ChapterId == chapterId && q.Status == PublicationStatus.Published, ct);
            var attempts = await db.QuizAttempts.AsNoTracking()
                .Where(a => a.StudentId == ctx.Student.Id && a.ChapterId == chapterId && a.SubmittedAtUtc != null)
                .OrderByDescending(a => a.SubmittedAtUtc).Take(10)
                .Select(a => new QuizHistoryDto(a.Id, a.SubmittedAtUtc!.Value, a.Score, a.Total, a.Percent)).ToListAsync(ct);
            return Results.Ok(new { available = published > 0, questionCount = Math.Min(published, QuizAttempt.QuestionsPerQuiz), attempts });
        });
    }
}
