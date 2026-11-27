using System.Text.Json;
using Googols.Application.Abstractions;
using Googols.Application.Ai;
using Googols.Domain.Ai;
using Googols.Domain.Common;
using Googols.Infrastructure.Persistence;
using Googols.WebApi.Plumbing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Googols.WebApi.Features;

/// <summary>Admin view of AI quality: usage and cost, student thumbs up/down, and the golden-set evals.</summary>
public static class AiQualityEndpoints
{
    public sealed record UsageTotals(int Calls, long InputTokens, long OutputTokens, long SpeechCharacters, decimal CostUsd, decimal CostInr);
    public sealed record FeatureUsage(string Feature, int Calls, long Tokens, decimal CostUsd);
    public sealed record DayUsage(DateOnly Day, int Calls, decimal CostUsd);
    public sealed record FeedbackItem(Guid MessageId, string Question, string Answer, string? Comment, DateTime At, string Language);
    public sealed record UsageDto(int Days, UsageTotals Totals, IReadOnlyList<FeatureUsage> ByFeature, IReadOnlyList<DayUsage> Daily,
        int Helpful, int NotHelpful, IReadOnlyList<FeedbackItem> RecentNotHelpful, decimal UsdToInr);

    public sealed record EvalCaseRequest(Guid SubjectId, Guid? ChapterId, string Question, string? Language, string? Mode,
        string? ExpectedKeywords, string? ForbiddenPhrases, bool ExpectGrounded);
    public sealed record EvalCaseDto(Guid Id, Guid SubjectId, string SubjectName, Guid? ChapterId, string? ChapterTitle, string Question, string Language,
        string Mode, string? ExpectedKeywords, string? ForbiddenPhrases, bool ExpectGrounded);
    public sealed record EvalResult(Guid CaseId, string Question, string Language, bool Passed, double Score, string Answer, bool Grounded,
        IReadOnlyList<EvalScoring.Check> Checks, string? Error);
    public sealed record EvalRunSummary(Guid Id, DateTime CreatedAtUtc, string PromptVersion, string? Model, int Total, int Passed, double AverageScore);
    public sealed record EvalRunDto(EvalRunSummary Run, IReadOnlyList<EvalResult> Results);

    public const int MaxCasesPerRun = 30;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapAiQualityEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/admin/ai").WithTags("AI quality").RequireAuthorization(Policies.Admin);

        g.MapGet("/usage", async (int? days, GoogolsDbContext db, IClock clock, IOptions<AiPricingOptions> pricing, CancellationToken ct) =>
        {
            var d = Math.Clamp(days ?? 14, 1, 90);
            var from = clock.UtcNow.Date.AddDays(-(d - 1));
            var logs = db.AiUsageLogs.AsNoTracking().Where(l => l.CreatedAtUtc >= from);
            var byFeature = await logs.GroupBy(l => l.Feature)
                .Select(x => new { Feature = x.Key, Calls = x.Count(), Tokens = x.Sum(l => (long)l.InputTokens + l.OutputTokens), Cost = x.Sum(l => l.CostUsd) })
                .ToListAsync(ct);
            var totals = await logs.GroupBy(_ => 1).Select(x => new
            {
                Calls = x.Count(), In = x.Sum(l => (long)l.InputTokens), Out = x.Sum(l => (long)l.OutputTokens),
                Chars = x.Sum(l => (long)l.Characters), Cost = x.Sum(l => l.CostUsd),
            }).FirstOrDefaultAsync(ct);
            var daily = await logs.GroupBy(l => l.CreatedAtUtc.Date).Select(x => new { x.Key, Calls = x.Count(), Cost = x.Sum(l => l.CostUsd) }).ToListAsync(ct);
            var dayMap = daily.ToDictionary(x => x.Key);
            var series = Enumerable.Range(0, d).Select(i => from.AddDays(i))
                .Select(day => dayMap.TryGetValue(day, out var v) ? new DayUsage(DateOnly.FromDateTime(day), v.Calls, v.Cost) : new DayUsage(DateOnly.FromDateTime(day), 0, 0))
                .ToList();

            var rated = db.AiMessages.AsNoTracking().Where(m => m.FeedbackAtUtc >= from);
            var helpful = await rated.CountAsync(m => m.FeedbackHelpful == true, ct);
            var notHelpful = await rated.CountAsync(m => m.FeedbackHelpful == false, ct);
            var bad = await rated.Where(m => m.FeedbackHelpful == false).OrderByDescending(m => m.FeedbackAtUtc).Take(20)
                .Select(m => new
                {
                    m.Id, Answer = m.Text, m.FeedbackComment, At = m.FeedbackAtUtc!.Value, m.Conversation!.Language,
                    Question = db.AiMessages.Where(u => u.ConversationId == m.ConversationId && u.Role == "user" && u.CreatedAtUtc < m.CreatedAtUtc)
                        .OrderByDescending(u => u.CreatedAtUtc).Select(u => u.Text).FirstOrDefault(),
                }).ToListAsync(ct);

            var rate = pricing.Value.UsdToInr;
            var t = totals is null ? new UsageTotals(0, 0, 0, 0, 0, 0)
                : new UsageTotals(totals.Calls, totals.In, totals.Out, totals.Chars, Math.Round(totals.Cost, 4), Math.Round(totals.Cost * rate, 2));
            return new UsageDto(d, t,
                byFeature.OrderByDescending(x => x.Cost).Select(x => new FeatureUsage(x.Feature, x.Calls, x.Tokens, Math.Round(x.Cost, 4))).ToList(),
                series, helpful, notHelpful,
                bad.Select(b => new FeedbackItem(b.Id, b.Question ?? "", b.Answer, b.FeedbackComment, b.At, b.Language)).ToList(), rate);
        });

        // ---- Golden set
        g.MapGet("/evals/cases", async (GoogolsDbContext db, CancellationToken ct) =>
            await db.AiEvalCases.AsNoTracking().OrderBy(c => c.CreatedAtUtc)
                .Select(c => new EvalCaseDto(c.Id, c.SubjectId, db.Subjects.Where(s => s.Id == c.SubjectId).Select(s => s.Name).First(),
                    c.ChapterId, db.Chapters.Where(x => x.Id == c.ChapterId).Select(x => x.Title).FirstOrDefault(),
                    c.Question, c.Language, c.Mode, c.ExpectedKeywords, c.ForbiddenPhrases, c.ExpectGrounded)).ToListAsync(ct));

        g.MapPost("/evals/cases", async (EvalCaseRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await Validate(req, db, ct) is { } problem)
            {
                return problem;
            }
            var c = new AiEvalCase { SubjectId = req.SubjectId, Question = "" };
            Apply(c, req);
            db.AiEvalCases.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/ai/evals/cases/{c.Id}", new { c.Id });
        });

        g.MapPut("/evals/cases/{id:guid}", async (Guid id, EvalCaseRequest req, GoogolsDbContext db, CancellationToken ct) =>
        {
            if (await Validate(req, db, ct) is { } problem)
            {
                return problem;
            }
            var c = await db.AiEvalCases.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null)
            {
                return Results.NotFound();
            }
            Apply(c, req);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("/evals/cases/{id:guid}", async (Guid id, GoogolsDbContext db, IClock clock, CancellationToken ct) =>
        {
            var n = await db.AiEvalCases.Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivedAtUtc, clock.UtcNow), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        g.MapGet("/evals/runs", async (GoogolsDbContext db, CancellationToken ct) =>
            await db.AiEvalRuns.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).Take(10)
                .Select(r => new EvalRunSummary(r.Id, r.CreatedAtUtc, r.PromptVersion, r.Model, r.Total, r.Passed, r.AverageScore)).ToListAsync(ct));

        g.MapGet("/evals/runs/{id:guid}", async (Guid id, GoogolsDbContext db, CancellationToken ct) =>
        {
            var r = await db.AiEvalRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return r is null ? Results.NotFound() : Results.Ok(new EvalRunDto(
                new EvalRunSummary(r.Id, r.CreatedAtUtc, r.PromptVersion, r.Model, r.Total, r.Passed, r.AverageScore),
                JsonSerializer.Deserialize<List<EvalResult>>(r.ResultsJson, Json) ?? []));
        });

        // Runs every case through the same retrieval + prompt as the live AI teacher and scores the answers.
        g.MapPost("/evals/run", async (GoogolsDbContext db, ICurrentUser me, ICurriculumRetriever retriever, IAiModelProvider model,
            AiUsageRecorder usage, ILogger<EvalCaseRequest> log, CancellationToken ct) =>
        {
            var cases = await db.AiEvalCases.AsNoTracking().OrderBy(c => c.CreatedAtUtc).Take(MaxCasesPerRun).ToListAsync(ct);
            if (cases.Count == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["cases"] = ["Add at least one test question first."] });
            }

            // Retrieval uses the database, so it runs one case at a time; the model calls then run four at a time.
            var prepared = new List<(AiEvalCase Case, AiCompletionRequest? Request, bool Grounded, string? Error)>();
            foreach (var c in cases)
            {
                var info = await db.Subjects.AsNoTracking().Where(s => s.Id == c.SubjectId)
                    .Select(s => new { s.Class!.GradeNumber, s.Class.Curriculum!.AcademicYear, Board = s.Class.Curriculum.Board!.Code }).FirstOrDefaultAsync(ct);
                if (info is null)
                {
                    prepared.Add((c, null, false, "The subject no longer exists."));
                    continue;
                }
                var mode = Enum.TryParse<TeacherMode>(c.Mode, out var m) ? m : TeacherMode.PersonalTeacher;
                var ctx = new StudentLearningContext(Guid.Empty, info.Board, info.AcademicYear, info.GradeNumber, c.SubjectId, c.ChapterId, null, c.Language);
                var passages = await retriever.SearchAsync(ctx, c.Question, top: 5, ct);
                prepared.Add((c, new AiCompletionRequest(TeacherPromptBuilder.Build(ctx, mode, passages), [], c.Question, 900), passages.Count > 0, null));
            }

            using var gate = new SemaphoreSlim(4);
            var answers = await Task.WhenAll(prepared.Select(async p =>
            {
                if (p.Request is null)
                {
                    return (p, (AiCompletionResult?)null, (string?)p.Error);
                }
                await gate.WaitAsync(ct);
                try
                {
                    return (p, (AiCompletionResult?)await model.CompleteAsync(p.Request, ct), (string?)null);
                }
                catch (AiContentFilteredException)
                {
                    return (p, (AiCompletionResult?)null, (string?)"The provider's content filter blocked this answer.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Eval case {Case} failed", p.Case.Id);
                    return (p, (AiCompletionResult?)null, (string?)("The AI call failed: " + ex.GetType().Name));
                }
                finally
                {
                    gate.Release();
                }
            }));

            var results = new List<EvalResult>();
            string? modelName = null;
            foreach (var (p, completion, error) in answers)
            {
                if (completion is not null)
                {
                    modelName ??= completion.Model;
                    await usage.RecordAsync("eval", completion, null, me.UserId, ct);
                }
                var reply = completion?.Text ?? "";
                var outcome = error is null
                    ? EvalScoring.Score(reply, p.Case.Language, EvalScoring.Lines(p.Case.ExpectedKeywords), EvalScoring.Lines(p.Case.ForbiddenPhrases), p.Case.ExpectGrounded, p.Grounded)
                    : new EvalScoring.Outcome(false, 0, [new EvalScoring.Check("Answer given", false, error)]);
                results.Add(new EvalResult(p.Case.Id, p.Case.Question, p.Case.Language, outcome.Passed, outcome.Score, reply, p.Grounded, outcome.Checks, error));
            }

            var run = new AiEvalRun
            {
                PromptVersion = TeacherPromptBuilder.Version,
                Model = modelName,
                Total = results.Count,
                Passed = results.Count(r => r.Passed),
                AverageScore = Math.Round(results.Average(r => r.Score), 3),
                ResultsJson = JsonSerializer.Serialize(results, Json),
            };
            db.AiEvalRuns.Add(run);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new EvalRunDto(new EvalRunSummary(run.Id, run.CreatedAtUtc, run.PromptVersion, run.Model, run.Total, run.Passed, run.AverageScore), results));
        });
    }

    private static async Task<IResult?> Validate(EvalCaseRequest req, GoogolsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Question) || req.Question.Trim().Length > 1000)
        {
            errors["question"] = ["Write the test question (up to 1000 characters)."];
        }
        if (!await db.Subjects.AnyAsync(s => s.Id == req.SubjectId, ct))
        {
            errors["subjectId"] = ["Choose a subject."];
        }
        if (req.ChapterId is { } chapterId && !await db.Chapters.AnyAsync(c => c.Id == chapterId && c.SubjectId == req.SubjectId, ct))
        {
            errors["chapterId"] = ["That chapter isn't in the chosen subject."];
        }
        if (req.Language is not null && !LanguageCodes.IsSupported(req.Language))
        {
            errors["language"] = ["Use en or ml."];
        }
        if (req.Mode is not null && !Enum.TryParse<TeacherMode>(req.Mode, out _))
        {
            errors["mode"] = ["Unknown teaching mode."];
        }
        if ((req.ExpectedKeywords?.Length ?? 0) > 2000 || (req.ForbiddenPhrases?.Length ?? 0) > 1000)
        {
            errors["expectedKeywords"] = ["Keep the expected points shorter."];
        }
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static void Apply(AiEvalCase c, EvalCaseRequest req)
    {
        c.SubjectId = req.SubjectId;
        c.ChapterId = req.ChapterId;
        c.Question = req.Question.Trim();
        c.Language = (req.Language ?? LanguageCodes.English).ToLowerInvariant();
        c.Mode = req.Mode ?? nameof(TeacherMode.PersonalTeacher);
        c.ExpectedKeywords = string.IsNullOrWhiteSpace(req.ExpectedKeywords) ? null : req.ExpectedKeywords.Trim();
        c.ForbiddenPhrases = string.IsNullOrWhiteSpace(req.ForbiddenPhrases) ? null : req.ForbiddenPhrases.Trim();
        c.ExpectGrounded = req.ExpectGrounded;
    }
}
