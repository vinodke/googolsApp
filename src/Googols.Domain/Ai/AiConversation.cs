using Googols.Domain.Common;

namespace Googols.Domain.Ai;

/// <summary>A saved AI Teacher chat. Owned by one student; nobody else can read it through the API.</summary>
public class AiConversation : Entity, ISoftDeletable
{
    public Guid StudentId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? ChapterId { get; set; }
    public required string Title { get; set; }
    public string Language { get; set; } = LanguageCodes.English;
    public DateTime LastMessageAtUtc { get; set; }
    public int MessageCount { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public List<AiMessage> Messages { get; } = [];

    /// <summary>First question, trimmed to a readable title.</summary>
    public static string TitleFrom(string firstQuestion)
    {
        var t = string.Join(' ', firstQuestion.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return t.Length <= 80 ? t : t[..77].TrimEnd() + "…";
    }
}

public class AiMessage : Entity
{
    public Guid ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }
    public required string Role { get; set; }          // "user" | "assistant"
    public required string Text { get; set; }
    public string? Mode { get; set; }
    public string? SourcesJson { get; set; }           // [{contentId,title,kind}] for assistant turns
    public bool Grounded { get; set; }
    public string? SafetyCategory { get; set; }        // set when a screen replaced or blocked the text
    public string? Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? ImageBlobName { get; set; }         // photo the student sent with this question (private blob)
    public bool? FeedbackHelpful { get; set; }         // thumbs up / down from the student on an assistant turn
    public string? FeedbackComment { get; set; }
    public DateTime? FeedbackAtUtc { get; set; }
}

/// <summary>One billable AI call (chat, photo question, question drafting, marking, evals, speech). Used for the cost dashboard.</summary>
public class AiUsageLog : Entity
{
    public required string Feature { get; set; }
    public string? Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int Characters { get; set; }                // text-to-speech characters
    public decimal CostUsd { get; set; }
    public Guid? StudentId { get; set; }
    public Guid? UserId { get; set; }
}

/// <summary>A golden-set question with what a good answer must (and must not) contain. Run by admins after prompt or model changes.</summary>
public class AiEvalCase : Entity, ISoftDeletable
{
    public Guid SubjectId { get; set; }
    public Guid? ChapterId { get; set; }
    public required string Question { get; set; }
    public string Language { get; set; } = LanguageCodes.English;
    public string Mode { get; set; } = "PersonalTeacher";
    public string? ExpectedKeywords { get; set; }      // one per line, "a|b" = either
    public string? ForbiddenPhrases { get; set; }
    public bool ExpectGrounded { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
}

public class AiEvalRun : Entity
{
    public required string PromptVersion { get; set; }
    public string? Model { get; set; }
    public int Total { get; set; }
    public int Passed { get; set; }
    public double AverageScore { get; set; }
    public required string ResultsJson { get; set; }
}
