using System.Text.Json;
using Googols.Application.Abstractions;
using Googols.Application.Ai;
using Googols.Domain.Ai;
using Googols.Domain.Common;
using Googols.Domain.Students;
using Googols.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Googols.WebApi.Features;

/// <summary>
/// <paramref name="ConversationId"/> continues a saved chat (history is read from the server, never trusted from the client).
/// <paramref name="History"/> is accepted only for older clients that don't send a conversation id.
/// </summary>
public sealed record AskTeacherRequest(Guid SubjectId, Guid? ChapterId, Guid? TopicId, string Message, TeacherMode Mode, string? Language,
    IReadOnlyList<ChatTurn>? History, Guid? ConversationId = null);

public sealed record AskTeacherResponse(string Reply, IReadOnlyList<SourceRef> Sources, bool GroundedInApprovedContent, int RemainingToday,
    Guid? ConversationId = null, string? SafetyNotice = null);

public sealed record SourceRef(Guid ContentId, string Title, SourceKind Kind);

public sealed record ConversationSummaryDto(Guid Id, string Title, Guid SubjectId, Guid? ChapterId, string Language, DateTime LastMessageAtUtc, int MessageCount);

public sealed record ConversationMessageDto(string Role, string Text, IReadOnlyList<SourceRef> Sources, bool Grounded, DateTime CreatedAtUtc);

public sealed record ConversationDto(Guid Id, string Title, Guid SubjectId, Guid? ChapterId, string Language, IReadOnlyList<ConversationMessageDto> Messages);

/// <summary>
/// AI Teacher orchestration: who is asking -> curriculum context -> safety screen -> retrieval of
/// Published content -> prompt -> model -> output screen -> save turn -> usage accounting.
/// </summary>
public sealed class AiTeacherService(
    GoogolsDbContext db, ICurrentUser me, IClock clock, ICache cache,
    ICurriculumRetriever retriever, IAiModelProvider model, IContentSafety safety, EntitlementService entitlements, ILogger<AiTeacherService> log)
{
    private const int HistoryTurns = 10;

    /// <summary>Tele-MANAS is India's free 24×7 mental-health helpline (14416).</summary>
    public const string SelfHarmReply =
        "I'm really glad you told me. You matter, and you don't have to handle this alone. Please talk to a parent, a teacher or another adult you trust today. " +
        "You can also call Tele-MANAS on 14416 (free, any time, in Malayalam and English). If you are in danger right now, call 112.\n\n" +
        "നിങ്ങൾ ഇത് പറഞ്ഞതിൽ സന്തോഷം. നിങ്ങൾ ഒറ്റയ്ക്കല്ല. ഇന്ന് തന്നെ അച്ഛനോടോ അമ്മയോടോ അധ്യാപകരോടോ സംസാരിക്കൂ. Tele-MANAS 14416 എന്ന നമ്പറിൽ എപ്പോഴും സൗജന്യമായി വിളിക്കാം. അപകടത്തിലാണെങ്കിൽ 112 വിളിക്കൂ.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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

        AiConversation? conversation = null;
        if (req.ConversationId is { } cid)
        {
            conversation = await db.AiConversations.FirstOrDefaultAsync(c => c.Id == cid && c.StudentId == student.Id, ct);
            if (conversation is null)
            {
                return Results.NotFound(new { message = "That chat no longer exists. Start a new one." });
            }
        }

        var usageKey = $"ai-usage:{student.Id}:{today:yyyyMMdd}";
        var used = await cache.GetAsync<int>(usageKey, ct);
        var plan = await entitlements.CurrentPlanAsync(student.Id, ct);
        var dailyLimit = plan.AiQuestionsPerDay;
        if (used >= dailyLimit)
        {
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                title: plan.IsFree ? "You've used today's free AI teacher questions. They reset tomorrow, or a parent can upgrade to Premium." : "You've reached today's AI teacher limit. It resets tomorrow.");
        }

        var language = LanguageCodes.IsSupported(req.Language) ? req.Language!.ToLowerInvariant() : student.PreferredLanguage;

        var input = await safety.CheckAsync(req.Message, ct);
        if (!input.Allowed)
        {
            if (input.Category == SafetyCategories.SelfHarm)
            {
                // Never a refusal: the child gets a caring reply with a helpline. Not sent to the model.
                conversation = await SaveTurnAsync(conversation, student.Id, req, language, SelfHarmReply, [], false, input.Category, null, ct);
                log.LogWarning("AI teacher self-harm screen triggered for student {Student}", student.Id);
                return Results.Ok(new AskTeacherResponse(SelfHarmReply, [], false, dailyLimit - used, conversation.Id, "wellbeing"));
            }
            var msg = input.Category switch
            {
                SafetyCategories.PersonalInfo => "Please don't share phone numbers or email addresses here. Ask your question without them.",
                SafetyCategories.Harmful => "Let's keep our chat about your lessons. Please ask a question about your subject.",
                SafetyCategories.Unavailable => "The safety check is busy right now. Please try again in a minute.",
                _ => "Please ask a shorter question about your lesson.",
            };
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: msg);
        }

        var learning = new StudentLearningContext(student.Id, ctx.Code, ctx.AcademicYear, ctx.GradeNumber, req.SubjectId, req.ChapterId ?? conversation?.ChapterId, req.TopicId, language);
        var passages = await retriever.SearchAsync(learning, req.Message, top: 5, ct);
        var system = TeacherPromptBuilder.Build(learning, req.Mode, passages);

        IReadOnlyList<ChatTurn> history = conversation is null
            ? (req.History ?? []).TakeLast(HistoryTurns).ToList()
            : (await db.AiMessages.AsNoTracking().Where(m => m.ConversationId == conversation.Id && m.SafetyCategory == null)
                .OrderByDescending(m => m.CreatedAtUtc).Take(HistoryTurns).Select(m => new ChatTurn(m.Role, m.Text)).ToListAsync(ct))
                .AsEnumerable().Reverse().ToList();

        string reply;
        string? blocked = null;
        AiCompletionResult? completion = null;
        try
        {
            completion = await model.CompleteAsync(new AiCompletionRequest(system, history, req.Message, MaxOutputTokens: 900), ct);
            var output = await safety.CheckAsync(completion.Text, ct);
            reply = output.Allowed ? completion.Text : "I can't answer that one here. Let's get back to your lesson. Ask your teacher if you need more help.";
            blocked = output.Allowed ? null : output.Category;
        }
        catch (AiContentFilteredException)
        {
            reply = "I can't help with that here. Let's get back to your lesson. Try asking about your chapter.";
            blocked = SafetyCategories.Harmful;
        }

        var sources = passages.Select(p => new SourceRef(p.ContentId, p.Title, p.Kind)).ToList();
        var grounded = blocked is null && passages.Count > 0;
        conversation = await SaveTurnAsync(conversation, student.Id, req, language, reply, grounded ? sources : [], grounded, blocked, completion, ct);

        await cache.SetAsync(usageKey, used + 1, TimeSpan.FromHours(26), ct);
        log.LogInformation("AI teacher call {Model} in={In} out={Out} student={Student} passages={Count} prompt={PromptVersion} blocked={Blocked}",
            completion?.Model, completion?.InputTokens, completion?.OutputTokens, student.Id, passages.Count, TeacherPromptBuilder.Version, blocked);

        return Results.Ok(new AskTeacherResponse(reply, grounded ? sources : [], grounded, dailyLimit - used - 1, conversation.Id));
    }

    private async Task<AiConversation> SaveTurnAsync(AiConversation? conversation, Guid studentId, AskTeacherRequest req, string language,
        string reply, IReadOnlyList<SourceRef> sources, bool grounded, string? safetyCategory, AiCompletionResult? completion, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (conversation is null)
        {
            conversation = new AiConversation
            {
                StudentId = studentId,
                SubjectId = req.SubjectId,
                ChapterId = req.ChapterId,
                Title = AiConversation.TitleFrom(req.Message),
                Language = language,
            };
            db.AiConversations.Add(conversation);
        }
        conversation.LastMessageAtUtc = now;
        conversation.MessageCount += 2;
        conversation.Language = language;
        // A blocked or wellbeing exchange is kept for the record but marked, and never replayed to the model.
        db.AiMessages.Add(new AiMessage { ConversationId = conversation.Id, Role = "user", Text = req.Message, Mode = req.Mode.ToString(), SafetyCategory = safetyCategory, CreatedAtUtc = now });
        db.AiMessages.Add(new AiMessage
        {
            ConversationId = conversation.Id,
            Role = "assistant",
            Text = reply,
            Mode = req.Mode.ToString(),
            SourcesJson = sources.Count > 0 ? JsonSerializer.Serialize(sources, Json) : null,
            Grounded = grounded,
            SafetyCategory = safetyCategory,
            Model = completion?.Model,
            InputTokens = completion?.InputTokens ?? 0,
            OutputTokens = completion?.OutputTokens ?? 0,
            CreatedAtUtc = now.AddMilliseconds(1), // keeps user → assistant order stable
        });
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    public static IReadOnlyList<SourceRef> ParseSources(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<SourceRef>>(json, Json) ?? [];
}

public static class AiTeacherEndpoints
{
    public static void MapAiTeacherEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/ai").WithTags("AI Teacher").RequireAuthorization(Plumbing.Policies.Student);

        g.MapPost("/messages", (AskTeacherRequest req, AiTeacherService svc, CancellationToken ct) => svc.AskAsync(req, ct))
            .RequireRateLimiting("ai");

        g.MapGet("/conversations", async (Guid? subjectId, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var studentId = await db.StudentProfiles.Where(s => s.UserId == me.UserId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            var q = db.AiConversations.AsNoTracking().Where(c => c.StudentId == studentId);
            if (subjectId is { } sid)
            {
                q = q.Where(c => c.SubjectId == sid);
            }
            return await q.OrderByDescending(c => c.LastMessageAtUtc).Take(30)
                .Select(c => new ConversationSummaryDto(c.Id, c.Title, c.SubjectId, c.ChapterId, c.Language, c.LastMessageAtUtc, c.MessageCount)).ToListAsync(ct);
        });

        g.MapGet("/conversations/{id:guid}", async (Guid id, ICurrentUser me, GoogolsDbContext db, CancellationToken ct) =>
        {
            var studentId = await db.StudentProfiles.Where(s => s.UserId == me.UserId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            var c = await db.AiConversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.StudentId == studentId, ct);
            if (c is null)
            {
                return Results.NotFound();
            }
            var messages = await db.AiMessages.AsNoTracking().Where(m => m.ConversationId == id).OrderBy(m => m.CreatedAtUtc).Take(200)
                .Select(m => new { m.Role, m.Text, m.SourcesJson, m.Grounded, m.CreatedAtUtc }).ToListAsync(ct);
            return Results.Ok(new ConversationDto(c.Id, c.Title, c.SubjectId, c.ChapterId, c.Language,
                messages.Select(m => new ConversationMessageDto(m.Role, m.Text, AiTeacherService.ParseSources(m.SourcesJson), m.Grounded, m.CreatedAtUtc)).ToList()));
        });

        g.MapDelete("/conversations/{id:guid}", async (Guid id, ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var studentId = await db.StudentProfiles.Where(s => s.UserId == me.UserId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            var n = await db.AiConversations.Where(c => c.Id == id && c.StudentId == studentId)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivedAtUtc, clock.UtcNow), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
