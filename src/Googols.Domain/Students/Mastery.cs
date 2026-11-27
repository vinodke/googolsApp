using Googols.Domain.Common;

namespace Googols.Domain.Students;

/// <summary>How well a student knows one chapter, from quiz, test and written-answer results (recent results weigh more).</summary>
public class ChapterMastery : Entity
{
    public Guid StudentId { get; set; }
    public Guid ChapterId { get; set; }
    public Guid SubjectId { get; set; }
    public double PracticeScore { get; set; }          // 0–100, moving average of results
    public int Attempts { get; set; }
    public int Correct { get; set; }
    public int Answered { get; set; }
    public DateTime LastPracticedAtUtc { get; set; }
}

/// <summary>Something a student saved to come back to: a lesson or an AI teacher answer.</summary>
public class Bookmark : Entity
{
    public const string Lesson = "lesson";
    public const string Answer = "answer";

    public Guid StudentId { get; set; }
    public required string Kind { get; set; }
    public Guid RefId { get; set; }
    public required string Title { get; set; }
    public string? Snippet { get; set; }
    public string? Note { get; set; }
}
