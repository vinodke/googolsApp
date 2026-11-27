using Googols.Application.Abstractions;
using Googols.Application.Ai;
using Googols.Domain.Common;
using Googols.Domain.Students;
using Googols.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

public sealed record AskTeacherRequest(Guid SubjectId, Guid? ChapterId, Guid? TopicId, string Message, TeacherMode Mode, string? Language,
    IReadOnlyList<ChatTurn>? History);

public sealed record AskTeacherResponse(string Reply, IReadOnlyList<SourceRef> Sources, bool GroundedInApprovedContent, int RemainingToday);

public sealed record SourceRef(Guid ContentId, string Title, SourceKind Kind);

/// <summary>
/// AI Teacher orchestration: who is asking -> curriculum context -> safety screen -> retrieval of
/// Published content -> prompt -> model -> output screen -> usage accounting.
/// Conversation persistence (AIConversations/AIMessages tables) arrives in Phase 7.
/// </summary>
public sealed class AiTeacherService(
    GoogolsDbContext db, ICurrentUser me, IClock clock, ICache cache,
    ICurriculumRetriever retriever, IAiModelProvider model, IContentSafety safety, ILogger<AiTeacherService> log)
{
    public const int DailyMessageLimit = 40; // fair-use cap; per-plan entitlements come with subscriptions

    public async Task<IResult> AskAsync(AskTeacherRequest req, CancellationToken ct)
    {
        var student = await db.StudentProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == me.UserId, ct);
        if (student is null)
        {
            return Results.NotFound(new { message = "Complete onboarding first." });
        }

        var today = clock.TodayIn("Asia/Kolkata");
        if (student.RequiresParentalConsent(today) &&
            !await db.ParentStudentLinks.AnyAsync(l => l.StudentId == student.Id && l.Status == ParentLinkStatus.Verified && l.ConsentGrantedAtUtc != null && l.ConsentVersion == ParentalConsent.CurrentVersion, ct))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "A parent needs to give permission before you can use the AI teacher.");
        }

        var ctx = await (
            from e in db.Enrollments.AsNoTracking()
            join c in db.Curricula on e.CurriculumId equals c.Id
            join b in db.CurriculumBoards on c.BoardId equals b.Id
            join k in db.AcademicClasses on e.ClassId equals k.Id
            join s in db.Subjects on k.Id equals s.ClassId
            where e.StudentId == student.Id && e.IsActive && s.Id == req.SubjectId
            select new { b.Code, c.AcademicYear, k.GradeNumber }).FirstOrDefaultAsync(ct);
        if (ctx is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "That subject is not part of your class.");
        }

        var usageKey = $"ai-usage:{student.Id}:{today:yyyyMMdd}";
        var used = await cache.GetAsync<int>(usageKey, ct);
        if (used >= DailyMessageLimit)
        {
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "You've reached today's AI teacher limit. It resets tomorrow.");
        }

        var input = await safety.CheckAsync(req.Message, ct);
        if (!input.Allowed)
        {
            var msg = input.Category == "personal-info"
                ? "Please don't share phone numbers or email addresses here. Ask your question without them."
                : "Please ask a shorter question about your lesson.";
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: msg);
        }

        var language = LanguageCodes.IsSupported(req.Language) ? req.Language!.ToLowerInvariant() : student.PreferredLanguage;
        var learning = new StudentLearningContext(student.Id, ctx.Code, ctx.AcademicYear, ctx.GradeNumber, req.SubjectId, req.ChapterId, req.TopicId, language);

        var passages = await retriever.SearchAsync(learning, req.Message, top: 5, ct);
        var system = TeacherPromptBuilder.Build(learning, req.Mode, passages);
        var history = (req.History ?? []).TakeLast(10).ToList();

        var completion = await model.CompleteAsync(new AiCompletionRequest(system, history, req.Message, MaxOutputTokens: 900), ct);

        var output = await safety.CheckAsync(completion.Text, ct);
        var reply = output.Allowed ? completion.Text : "I can't answer that one here. Let's get back to your lesson. Ask your teacher if you need more help.";

        await cache.SetAsync(usageKey, used + 1, TimeSpan.FromHours(26), ct);
        log.LogInformation("AI teacher call {Model} in={In} out={Out} student={Student} passages={Count} prompt={PromptVersion}",
            completion.Model, completion.InputTokens, completion.OutputTokens, student.Id, passages.Count, TeacherPromptBuilder.Version);

        var sources = passages.Select(p => new SourceRef(p.ContentId, p.Title, p.Kind)).ToList();
        return Results.Ok(new AskTeacherResponse(reply, sources, passages.Count > 0, DailyMessageLimit - used - 1));
    }
}

public static class AiTeacherEndpoints
{
    public static void MapAiTeacherEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapPost("/ai/messages", (AskTeacherRequest req, AiTeacherService svc, CancellationToken ct) => svc.AskAsync(req, ct))
            .WithTags("AI Teacher")
            .RequireAuthorization(Plumbing.Policies.Student)
            .RequireRateLimiting("ai");
    }
}
