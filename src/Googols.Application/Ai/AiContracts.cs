namespace Googols.Application.Ai;

/// <summary>Where the student is in the curriculum. Every AI call is scoped to this context.</summary>
public sealed record StudentLearningContext(
    Guid StudentId,
    string BoardCode,
    string AcademicYear,
    int Grade,
    Guid SubjectId,
    Guid? ChapterId,
    Guid? TopicId,
    string Language);

public enum TeacherMode
{
    PersonalTeacher,
    DoubtSolver,
    ExamCoach,
    RevisionAssistant,
    PracticeTeacher,
}

/// <summary>Source type is shown to the student so generated text is never passed off as a textbook extract.</summary>
public enum SourceKind
{
    OfficialSyllabus,
    ApprovedMaterial,
    AiGenerated,
}

public sealed record RetrievedPassage(Guid ContentId, string Title, string Text, SourceKind Kind, double Score);

public sealed record ChatTurn(string Role, string Text);

/// <summary>A photo sent with a question (a textbook page or a written problem). Only PNG and JPEG.</summary>
public sealed record AiImage(byte[] Data, string MediaType);

public sealed record AiCompletionRequest(
    string SystemPrompt,
    IReadOnlyList<ChatTurn> History,
    string UserMessage,
    int MaxOutputTokens,
    AiImage? Image = null);

public sealed record AiCompletionResult(string Text, int InputTokens, int OutputTokens, string Model);

/// <summary>One piece of a streamed answer: text as it arrives, then a final item with the full result and token counts.</summary>
public sealed record AiStreamUpdate(string? Delta, AiCompletionResult? Final);

/// <summary>Model provider port (Azure OpenAI / OpenAI today).</summary>
public interface IAiModelProvider
{
    Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct);

    /// <summary>Streams the answer word by word. The last item carries <see cref="AiStreamUpdate.Final"/>.</summary>
    IAsyncEnumerable<AiStreamUpdate> StreamAsync(AiCompletionRequest request, CancellationToken ct);
}

/// <summary>Text-to-speech for reading answers aloud (Azure AI Speech when configured).</summary>
public interface ISpeechSynthesizer
{
    bool IsConfigured { get; }

    /// <summary>MP3 audio of <paramref name="text"/> in "ml" or "en".</summary>
    Task<byte[]> SynthesizeAsync(string text, string language, CancellationToken ct);
}

/// <summary>
/// Retrieval port (Azure AI Search today). Implementations MUST filter on board, curriculum version,
/// class, subject, chapter, topic, language and Published status. Unpublished content is never returned.
/// </summary>
public interface ICurriculumRetriever
{
    Task<IReadOnlyList<RetrievedPassage>> SearchAsync(StudentLearningContext context, string query, int top, CancellationToken ct);
}

/// <summary>Input/output safety screen for a children's platform (age-appropriateness, PII, prompt injection).</summary>
public interface IContentSafety
{
    Task<SafetyVerdict> CheckAsync(string text, CancellationToken ct);
}

public sealed record SafetyVerdict(bool Allowed, string? Category);

public static class SafetyCategories
{
    public const string PersonalInfo = "personal-info";
    public const string SelfHarm = "self-harm";
    public const string Harmful = "harmful";
    public const string Unavailable = "unavailable";
}

/// <summary>The model provider's own safety filter refused the prompt or the answer.</summary>
public sealed class AiContentFilteredException() : Exception("The AI provider's content filter blocked this exchange.");
