using Googols.Domain.Common;

namespace Googols.Domain.Gamification;

/// <summary>
/// Append-only XP ledger. Every award carries an idempotency key derived from the learning event
/// ("lesson-complete:{lessonId}") with a unique index on (StudentId, IdempotencyKey), so the same
/// event can never pay out twice, however many times the client retries.
/// </summary>
public class XpTransaction : Entity
{
    public Guid StudentId { get; set; }
    public int Points { get; set; }
    public required string Reason { get; set; }
    public required string IdempotencyKey { get; set; }
    public string? RuleVersion { get; set; }
}

/// <summary>Admin-configurable XP amounts. Defaults follow the blueprint table.</summary>
public class XpRule : Entity
{
    public required string ActivityCode { get; set; }
    public int Points { get; set; }
    public bool IsActive { get; set; } = true;

    public static class Codes
    {
        public const string LessonCompleted = "lesson-completed";
        public const string QuizCompleted = "quiz-completed";
        public const string QuizScore90 = "quiz-score-90";
        public const string DailyStreak = "daily-streak";
        public const string ChallengeCompleted = "challenge-completed";
        public const string ChapterCompleted = "chapter-completed";
        public const string ExamCompleted = "exam-completed";
        public const string DailyMissions = "daily-missions";
    }

    public static IReadOnlyDictionary<string, int> Defaults { get; } = new Dictionary<string, int>
    {
        [Codes.LessonCompleted] = 20,
        [Codes.QuizCompleted] = 15,
        [Codes.QuizScore90] = 30,
        [Codes.DailyStreak] = 10,
        [Codes.ChallengeCompleted] = 50,
        [Codes.ChapterCompleted] = 100,
        [Codes.ExamCompleted] = 25,
        [Codes.DailyMissions] = 20,
    };
}
