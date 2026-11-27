using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Googols.Application.Abstractions;
using Googols.Application.Ai;
using Googols.Application.Content;
using Googols.Domain.Ai;
using Googols.Domain.Common;
using Googols.Domain.Students;
using Googols.Infrastructure.Ai;
using Googols.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Googols.WebApi.Features;

/// <summary>
/// <paramref name="ConversationId"/> continues a saved chat (history is read from the server, never trusted from the client).
/// <paramref name="History"/> is accepted only for older clients that don't send a conversation id.
/// <paramref name="ImageBase64"/> is an optional PNG/JPEG photo (a page or a problem), at most 4 MB.
/// </summary>
public sealed record AskTeacherRequest(Guid SubjectId, Guid? ChapterId, Guid? TopicId, string? Message, TeacherMode Mode, string? Language,
    IReadOnlyList<ChatTurn>? History, Guid? ConversationId = null, string? ImageBase64 = null);

public sealed record AskTeacherResponse(string Reply, IReadOnlyList<SourceRef> Sources, bool GroundedInApprovedContent, int RemainingToday,
    Guid? ConversationId = null, string? SafetyNotice = null, Guid? MessageId = null);

public sealed record SourceRef(Guid ContentId, string Title, SourceKind Kind);

public sealed record ConversationSummaryDto(Guid Id, string Title, Guid SubjectId, Guid? ChapterId, string Language, DateTime LastMessageAtUtc, int MessageCount);

public sealed record ConversationMessageDto(string Role, string Text, IReadOnlyList<SourceRef> Sources, bool Grounded, DateTime CreatedAtUtc,
    Guid Id = default, bool HasImage = false, bool? Helpful = null);

public sealed record ConversationDto(Guid Id, string Title, Guid SubjectId, Guid? ChapterId, string Language, IReadOnlyList<ConversationMessageDto> Messages);

public sealed record FeedbackRequest(bool Helpful, string? Comment);

public sealed record SpeechRequest(string Text, string? Language);

/// <summary>Writes one row per billable AI call, priced from <see cref="AiPricingOptions"/>.</summary>
public sealed class AiUsageRecorder(GoogolsDbContext db, IOptions<AiPricingOptions> pricing)
{
    public async Task RecordAsync(string feature, AiCompletionResult? result, Guid? studentId, Guid? userId, CancellationToken ct, int speechCharacters = 0)
    {
        if (result is null && speechCharacters == 0)
        {
            return;
        }
        db.AiUsageLogs.Add(new AiUsageLog
        {
            Feature = feature,
            Model = result?.Model,
            InputTokens = result?.InputTokens ?? 0,
            OutputTokens = result?.OutputTokens ?? 0,
            Characters = speechCharacters,
            CostUsd = AiCost.Usd(result?.InputTokens ?? 0, result?.OutputTokens ?? 0, speechCharacters, pricing.Value),
            StudentId = studentId,
            UserId = userId,
        });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// AI Teacher orchestration: who is asking -> curriculum context -> safety screen -> retrieval of
/// Published content -> prompt -> model -> output screen -> save turn -> usage accounting.
/// The same preparation feeds both the one-shot endpoint and the streaming (word-by-word) endpoint.
/// </summary>
public sealed class AiTeacherService(
    GoogolsDbContext db, ICurrentUser me, IClock clock, ICache cache, IFileStorage files, AiUsageRecorder usage,
    ICurriculumRetriever retriever, IAiModelProvider model, IContentSafety safety, EntitlementService entitlements, ILogger<AiTeacherService> log)
{
    private const int HistoryTurns = 10;
    public const int MaxImageBytes = 4 * 1024 * 1024;
    public const string PhotoContainer = "ai-photos";
    public const string PhotoQuestion = "Please help me with the question in this photo.";

    /// <summary>Tele-MANAS is India's free 24×7 mental-health helpline (14416).</summary>
    public const string SelfHarmReply =
        "I'm really glad you told me. You matter, and you don't have to handle this alone. Please talk to a parent, a teacher or another adult you trust today. " +
        "You can also call Tele-MANAS on 14416 (free, any time, in Malayalam and English). If you are in danger right now, call 112.\n\n" +
        "നിങ്ങൾ ഇത് പറഞ്ഞതിൽ സന്തോഷം. നിങ്ങൾ ഒറ്റയ്ക്കല്ല. ഇന്ന് തന്നെ അച്ഛനോടോ അമ്മയോടോ അധ്യാപകരോടോ സംസാരിക്കൂ. Tele-MANAS 14416 എന്ന നമ്പറിൽ എപ്പോഴും സൗജന്യമായി വിളിക്കാം. അപകടത്തിലാണെങ്കിൽ 112 വിളിക്കൂ.";

    public const string BlockedReply = "I can't answer that one here. Let's get back to your lesson. Ask your teacher if you need more help.";
    public const string FilteredReply = "I can't help with that here. Let's get back to your lesson. Try asking about your chapter.";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private abstract record Prep;
    private sealed record Fail(int Status, string Title) : Prep;
    private sealed record Immediate(AskTeacherResponse Response) : Prep;
    private sealed record Ready(StudentProfile Student, AiConversation? Conversation, AskTeacherRequest Req, string Message, string Language,
        IReadOnlyList<RetrievedPassage> Passages, AiCompletionRequest Request, string UsageKey, int Used, int DailyLimit, AiImage? Image) : Prep;

    private async Task<Prep> PrepareAsync(AskTeacherRequest req, CancellationToken ct)
    {
        var student = await db.StudentProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == me.UserId, ct);
        if (student is null)
        {
            return new Fail(StatusCodes.Status404NotFound, "Complete onboarding first.");
        }

        var today = clock.TodayIn("Asia/Kolkata");
        if (student.RequiresParentalConsent(today) &&
            !await db.ParentStudentLinks.AnyAsync(l => l.StudentId == student.Id && l.Status == ParentLinkStatus.Verified && l.ConsentGrantedAtUtc != null && l.ConsentVersion == ParentalConsent.CurrentVersion, ct))
        {
            return new Fail(StatusCodes.Status403Forbidden, "A parent needs to give permission before you can use the AI teacher.");
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
            return new Fail(StatusCodes.Status403Forbidden, "That subject is not part of your class.");
        }

        AiImage? image = null;
        if (!string.IsNullOrWhiteSpace(req.ImageBase64))
        {
            image = DecodeImage(req.ImageBase64);
            if (image is null)
            {
                return new Fail(StatusCodes.Status400BadRequest, "Send the photo as a PNG or JPEG image of 4 MB or less.");
            }
        }
        var message = (req.Message ?? "").Trim();
        if (message.Length == 0)
        {
            if (image is null)
            {
                return new Fail(StatusCodes.Status400BadRequest, "Type a question or add a photo.");
            }
            message = PhotoQuestion;
        }

        AiConversation? conversation = null;
        if (req.ConversationId is { } cid)
        {
            conversation = await db.AiConversations.FirstOrDefaultAsync(c => c.Id == cid && c.StudentId == student.Id, ct);
            if (conversation is null)
            {
                return new Fail(StatusCodes.Status404NotFound, "That chat no longer exists. Start a new one.");
            }
        }

        var usageKey = $"ai-usage:{student.Id}:{today:yyyyMMdd}";
        var used = await cache.GetAsync<int>(usageKey, ct);
        var plan = await entitlements.CurrentPlanAsync(student.Id, ct);
        var dailyLimit = plan.AiQuestionsPerDay;
        if (used >= dailyLimit)
        {
            return new Fail(StatusCodes.Status429TooManyRequests,
                plan.IsFree ? "You've used today's free AI teacher questions. They reset tomorrow, or a parent can upgrade to Premium." : "You've reached today's AI teacher limit. It resets tomorrow.");
        }

        var language = LanguageCodes.IsSupported(req.Language) ? req.Language!.ToLowerInvariant() : student.PreferredLanguage;

        var input = await safety.CheckAsync(message, ct);
        if (!input.Allowed)
        {
            if (input.Category == SafetyCategories.SelfHarm)
            {
                // Never a refusal: the child gets a caring reply with a helpline. Not sent to the model.
                var saved = await SaveTurnAsync(conversation, student.Id, req, message, null, language, SelfHarmReply, [], false, input.Category, null, ct);
                log.LogWarning("AI teacher self-harm screen triggered for student {Student}", student.Id);
                return new Immediate(new AskTeacherResponse(SelfHarmReply, [], false, dailyLimit - used, saved.Conversation.Id, "wellbeing", saved.MessageId));
            }
            var msg = input.Category switch
            {
                SafetyCategories.PersonalInfo => "Please don't share phone numbers or email addresses here. Ask your question without them.",
                SafetyCategories.Harmful => "Let's keep our chat about your lessons. Please ask a question about your subject.",
                SafetyCategories.Unavailable => "The safety check is busy right now. Please try again in a minute.",
                _ => "Please ask a shorter question about your lesson.",
            };
            return new Fail(StatusCodes.Status422UnprocessableEntity, msg);
        }

        var learning = new StudentLearningContext(student.Id, ctx.Code, ctx.AcademicYear, ctx.GradeNumber, req.SubjectId, req.ChapterId ?? conversation?.ChapterId, req.TopicId, language);
        var passages = await retriever.SearchAsync(learning, message, top: 5, ct);
        var system = TeacherPromptBuilder.Build(learning, req.Mode, passages, hasImage: image is not null);

        IReadOnlyList<ChatTurn> history = conversation is null
            ? (req.History ?? []).TakeLast(HistoryTurns).ToList()
            : (await db.AiMessages.AsNoTracking().Where(m => m.ConversationId == conversation.Id && m.SafetyCategory == null)
                .OrderByDescending(m => m.CreatedAtUtc).Take(HistoryTurns).Select(m => new ChatTurn(m.Role, m.Text)).ToListAsync(ct))
                .AsEnumerable().Reverse().ToList();

        var request = new AiCompletionRequest(system, history, message, MaxOutputTokens: image is null ? 900 : 1200, image);
        return new Ready(student, conversation, req, message, language, passages, request, usageKey, used, dailyLimit, image);
    }

    public static AiImage? DecodeImage(string base64)
    {
        var raw = base64.Trim();
        var comma = raw.IndexOf(',');
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
        {
            raw = raw[(comma + 1)..];
        }
        if (raw.Length > MaxImageBytes / 3 * 4 + 8)
        {
            return null;
        }
        var bytes = new byte[raw.Length];
        if (!Convert.TryFromBase64String(raw, bytes, out var n) || n < 16)
        {
            return null;
        }
        var data = bytes.AsSpan(0, n).ToArray();
        var type = AttachmentRules.DetectContentType(data.AsSpan(0, 8));
        return type is "image/png" or "image/jpeg" ? new AiImage(data, type) : null;
    }

    private static IResult ToResult(Fail f) => Results.Problem(statusCode: f.Status, title: f.Title);

    public async Task<IResult> AskAsync(AskTeacherRequest req, CancellationToken ct)
    {
        var prep = await PrepareAsync(req, ct);
        switch (prep)
        {
            case Fail f:
                return ToResult(f);
            case Immediate i:
                return Results.Ok(i.Response);
        }
        var r = (Ready)prep;

        string reply;
        string? blocked = null;
        AiCompletionResult? completion = null;
        try
        {
            completion = await model.CompleteAsync(r.Request, ct);
            (reply, blocked) = await ScreenOutputAsync(completion.Text, ct);
        }
        catch (AiContentFilteredException)
        {
            reply = FilteredReply;
            blocked = SafetyCategories.Harmful;
        }
        return Results.Ok(await FinishAsync(r, reply, blocked, completion, ct));
    }

    /// <summary>Server-sent events: "delta" {text} as words arrive, then "done" with the saved turn, or "error" {title}.</summary>
    public async Task<IResult?> StreamAsync(AskTeacherRequest req, HttpContext http, CancellationToken ct)
    {
        var prep = await PrepareAsync(req, ct);
        if (prep is Fail f)
        {
            return ToResult(f); // nothing streamed yet, so a normal error response
        }

        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache, no-transform";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        async Task Send(string ev, object data)
        {
            await http.Response.WriteAsync($"event: {ev}\ndata: {JsonSerializer.Serialize(data, Json)}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }

        if (prep is Immediate i)
        {
            await Send("done", i.Response);
            return null;
        }
        var r = (Ready)prep;
        await Send("start", new { grounded = r.Passages.Count > 0 });

        var text = new StringBuilder();
        AiCompletionResult? completion = null;
        string? blocked = null;
        string reply;
        try
        {
            await foreach (var u in model.StreamAsync(r.Request, ct))
            {
                if (u.Delta is { } d)
                {
                    text.Append(d);
                    await Send("delta", new { text = d });
                }
                if (u.Final is { } final)
                {
                    completion = final;
                }
            }
            (reply, blocked) = await ScreenOutputAsync(text.ToString(), ct);
        }
        catch (AiContentFilteredException)
        {
            reply = FilteredReply;
            blocked = SafetyCategories.Harmful;
        }
        catch (AiNotConfiguredException)
        {
            await Send("error", new { title = "The AI teacher is not available right now." });
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null; // the student closed the page
        }
        catch (Exception ex)
        {
            log.LogError(ex, "AI teacher stream failed for student {Student}", r.Student.Id);
            await Send("error", new { title = "The AI teacher couldn't finish that answer. Please try again." });
            return null;
        }

        var response = await FinishAsync(r, reply, blocked, completion, ct);
        await Send("done", response); // the client replaces the streamed text with this final, screened reply
        return null;
    }

    private async Task<(string Reply, string? Blocked)> ScreenOutputAsync(string text, CancellationToken ct)
    {
        var output = await safety.CheckAsync(text, ct);
        return output.Allowed ? (text, null) : (BlockedReply, output.Category);
    }

    private async Task<AskTeacherResponse> FinishAsync(Ready r, string reply, string? blocked, AiCompletionResult? completion, CancellationToken ct)
    {
        string? imageBlob = null;
        if (r.Image is { } img)
        {
            using var stream = new MemoryStream(img.Data);
            imageBlob = await files.SaveAsync(PhotoContainer, img.MediaType == "image/png" ? "photo.png" : "photo.jpg", stream, img.MediaType, ct);
        }
        var sources = r.Passages.Select(p => new SourceRef(p.ContentId, p.Title, p.Kind)).ToList();
        var grounded = blocked is null && r.Passages.Count > 0;
        var saved = await SaveTurnAsync(r.Conversation, r.Student.Id, r.Req, r.Message, imageBlob, r.Language, reply, grounded ? sources : [], grounded, blocked, completion, ct);

        await cache.SetAsync(r.UsageKey, r.Used + 1, TimeSpan.FromHours(26), ct);
        await usage.RecordAsync(r.Image is null ? "teacher" : "teacher-photo", completion, r.Student.Id, me.UserId, ct);
        log.LogInformation("AI teacher call {Model} in={In} out={Out} student={Student} passages={Count} prompt={PromptVersion} blocked={Blocked} photo={Photo}",
            completion?.Model, completion?.InputTokens, completion?.OutputTokens, r.Student.Id, r.Passages.Count, TeacherPromptBuilder.Version, blocked, r.Image is not null);

        return new AskTeacherResponse(reply, grounded ? sources : [], grounded, r.DailyLimit - r.Used - 1, saved.Conversation.Id, null, saved.MessageId);
    }

    private async Task<(AiConversation Conversation, Guid MessageId)> SaveTurnAsync(AiConversation? conversation, Guid studentId, AskTeacherRequest req,
        string message, string? imageBlob, string language, string reply, IReadOnlyList<SourceRef> sources, bool grounded, string? safetyCategory,
        AiCompletionResult? completion, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (conversation is null)
        {
            conversation = new AiConversation
            {
                StudentId = studentId,
                SubjectId = req.SubjectId,
                ChapterId = req.ChapterId,
                Title = AiConversation.TitleFrom(imageBlob is not null && message == PhotoQuestion ? "Photo question" : message),
                Language = language,
            };
            db.AiConversations.Add(conversation);
        }
        conversation.LastMessageAtUtc = now;
        conversation.MessageCount += 2;
        conversation.Language = language;
        // A blocked or wellbeing exchange is kept for the record but marked, and never replayed to the model.
        db.AiMessages.Add(new AiMessage
        {
            ConversationId = conversation.Id, Role = "user", Text = message, Mode = req.Mode.ToString(), SafetyCategory = safetyCategory,
            ImageBlobName = imageBlob, CreatedAtUtc = now,
        });
        var assistant = new AiMessage
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
        };
        db.AiMessages.Add(assistant);
        await db.SaveChangesAsync(ct);
        return (conversation, assistant.Id);
    }

    public static IReadOnlyList<SourceRef> ParseSources(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<SourceRef>>(json, Json) ?? [];
}

public static partial class AiTeacherEndpoints
{
    public const int MaxSpeechCharacters = 3000;

    public static void MapAiTeacherEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/ai").WithTags("AI Teacher").RequireAuthorization(Plumbing.Policies.Student);

        g.MapPost("/messages", (AskTeacherRequest req, AiTeacherService svc, CancellationToken ct) => svc.AskAsync(req, ct))
            .RequireRateLimiting("ai");

        g.MapPost("/messages/stream", async (AskTeacherRequest req, AiTeacherService svc, HttpContext http, CancellationToken ct) =>
            await svc.StreamAsync(req, http, ct) ?? Results.Empty).RequireRateLimiting("ai");

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
                .Select(m => new { m.Id, m.Role, m.Text, m.SourcesJson, m.Grounded, m.CreatedAtUtc, HasImage = m.ImageBlobName != null, m.FeedbackHelpful })
                .ToListAsync(ct);
            return Results.Ok(new ConversationDto(c.Id, c.Title, c.SubjectId, c.ChapterId, c.Language,
                messages.Select(m => new ConversationMessageDto(m.Role, m.Text, AiTeacherService.ParseSources(m.SourcesJson), m.Grounded, m.CreatedAtUtc,
                    m.Id, m.HasImage, m.FeedbackHelpful)).ToList()));
        });

        g.MapDelete("/conversations/{id:guid}", async (Guid id, ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var studentId = await db.StudentProfiles.Where(s => s.UserId == me.UserId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            var n = await db.AiConversations.Where(c => c.Id == id && c.StudentId == studentId)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.ArchivedAtUtc, clock.UtcNow), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Thumbs up / down on one answer. Only the student who asked can rate it.
        g.MapPost("/messages/{id:guid}/feedback", async (Guid id, FeedbackRequest req, ICurrentUser me, IClock clock, GoogolsDbContext db, CancellationToken ct) =>
        {
            var comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
            if (comment?.Length > 500)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["comment"] = ["Keep the comment under 500 characters."] });
            }
            var studentId = await db.StudentProfiles.Where(s => s.UserId == me.UserId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            var n = await db.AiMessages.Where(m => m.Id == id && m.Role == "assistant" && m.Conversation!.StudentId == studentId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.FeedbackHelpful, req.Helpful)
                    .SetProperty(m => m.FeedbackComment, comment)
                    .SetProperty(m => m.FeedbackAtUtc, clock.UtcNow), ct);
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        // The photo a student sent with a question. Private: only that student can open it.
        g.MapGet("/messages/{id:guid}/image", async (Guid id, ICurrentUser me, GoogolsDbContext db, IFileStorage files, CancellationToken ct) =>
        {
            var studentId = await db.StudentProfiles.Where(s => s.UserId == me.UserId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            var blob = await db.AiMessages.Where(m => m.Id == id && m.ImageBlobName != null && m.Conversation!.StudentId == studentId)
                .Select(m => m.ImageBlobName).FirstOrDefaultAsync(ct);
            if (blob is null || await files.OpenReadAsync(AiTeacherService.PhotoContainer, blob, ct) is not { } stream)
            {
                return Results.NotFound();
            }
            return Results.File(stream, blob.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
        });

        // Read an answer aloud with a natural Malayalam / English voice (used when the device has no Malayalam voice).
        g.MapGet("/speech/status", (ISpeechSynthesizer speech) => Results.Ok(new { serverVoice = speech.IsConfigured }));

        g.MapPost("/speech", async (SpeechRequest req, ISpeechSynthesizer speech, ICurrentUser me, AiUsageRecorder usage, GoogolsDbContext db,
            ILogger<SpeechRequest> log, CancellationToken ct) =>
        {
            if (!speech.IsConfigured)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Read-aloud voices aren't set up on the server.");
            }
            var text = SpeakableText(req.Text ?? "");
            if (text.Length == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["Nothing to read aloud."] });
            }
            if (text.Length > MaxSpeechCharacters)
            {
                text = text[..MaxSpeechCharacters];
            }
            var language = req.Language == "ml" ? "ml" : "en";
            byte[] audio;
            try
            {
                audio = await speech.SynthesizeAsync(text, language, ct);
            }
            catch (HttpRequestException ex)
            {
                log.LogWarning(ex, "Speech synthesis failed");
                return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Couldn't read this aloud right now. Please try again.");
            }
            var studentId = await db.StudentProfiles.Where(s => s.UserId == me.UserId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            await usage.RecordAsync("speech", null, studentId, me.UserId, ct, speechCharacters: text.Length);
            return Results.File(audio, "audio/mpeg");
        }).RequireRateLimiting("speech");
    }

    /// <summary>Drops markdown symbols so the voice doesn't read "asterisk".</summary>
    public static string SpeakableText(string text) => Spaces().Replace(Markdown().Replace(text, " "), " ").Trim();

    [GeneratedRegex(@"[*#_`>|~]+")]
    private static partial Regex Markdown();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex Spaces();
}
