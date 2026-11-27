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
}
