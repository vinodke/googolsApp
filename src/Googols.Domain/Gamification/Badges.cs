using Googols.Domain.Common;

namespace Googols.Domain.Gamification;

/// <summary>A badge a student has earned. One row per (student, badge).</summary>
public class StudentBadge : Entity
{
    public Guid StudentId { get; set; }
    public required string BadgeCode { get; set; }
    public DateTime AwardedAtUtc { get; set; }
}

public sealed record BadgeDefinition(string Code, string Name, string Description, string Icon);

/// <summary>Badge catalogue. Rules are evaluated on the server from verified progress, never claimed by the client.</summary>
public static class BadgeCatalog
{
    public sealed record Stats(int LessonsCompleted, int ChaptersCompleted, int QuizzesTaken, bool HasPerfectQuiz, int StreakDays, int Level);

    public static readonly IReadOnlyList<BadgeDefinition> All =
    [
        new("first-lesson", "First step", "Finish your first lesson", "book"),
        new("lessons-10", "Bookworm", "Finish 10 lessons", "book"),
        new("lessons-50", "Scholar", "Finish 50 lessons", "grad"),
        new("first-chapter", "Chapter champ", "Finish every lesson in a chapter", "trophy"),
        new("first-quiz", "Quiz taker", "Complete your first quiz", "bolt"),
        new("perfect-quiz", "Full marks", "Score 100% in a quiz", "star"),
        new("streak-3", "On a roll", "Learn 3 days in a row", "flame"),
        new("streak-7", "Week warrior", "Learn 7 days in a row", "flame"),
        new("streak-30", "Unstoppable", "Learn 30 days in a row", "flame"),
        new("level-5", "Rising star", "Reach level 5", "trophy"),
        new("level-10", "Googols legend", "Reach level 10", "trophy"),
    ];

    public static IEnumerable<string> Earned(Stats s)
    {
        if (s.LessonsCompleted >= 1) yield return "first-lesson";
        if (s.LessonsCompleted >= 10) yield return "lessons-10";
        if (s.LessonsCompleted >= 50) yield return "lessons-50";
        if (s.ChaptersCompleted >= 1) yield return "first-chapter";
        if (s.QuizzesTaken >= 1) yield return "first-quiz";
        if (s.HasPerfectQuiz) yield return "perfect-quiz";
        if (s.StreakDays >= 3) yield return "streak-3";
        if (s.StreakDays >= 7) yield return "streak-7";
        if (s.StreakDays >= 30) yield return "streak-30";
        if (s.Level >= 5) yield return "level-5";
        if (s.Level >= 10) yield return "level-10";
    }
}
