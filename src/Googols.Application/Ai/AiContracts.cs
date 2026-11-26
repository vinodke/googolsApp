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

public sealed record AiCompletionRequest(
    string SystemPrompt,
    IReadOnlyList<ChatTurn> History,
    string UserMessage,
    int MaxOutputTokens);

public sealed record AiCompletionResult(string Text, int InputTokens, int OutputTokens, string Model);

/// <summary>Model provider port (Azure OpenAI / OpenAI today).</summary>
public interface IAiModelProvider
{
    Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct);
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
