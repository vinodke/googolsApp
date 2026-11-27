using Googols.Domain.Gamification;

namespace Googols.Application.Gamification;

/// <summary>
/// Turns a verified learning event into XP awards. Pure function: the caller persists the result in
/// the XP ledger, whose unique (StudentId, IdempotencyKey) index makes replays harmless.
/// Only server-verified events are accepted; AI chat messages never earn XP.
/// </summary>
public static class XpAwardPolicy
{
    public const string Version = "xp-v1";

    public sealed record LearningEvent(string Kind, Guid StudentId, Guid SubjectRefId, double? ScorePercent = null, DateOnly? Day = null, string? Key = null);

    public sealed record Award(string ActivityCode, int Points, string IdempotencyKey);

    public static IReadOnlyList<Award> Evaluate(LearningEvent e, IReadOnlyDictionary<string, int> rules)
    {
        var awards = new List<Award>();
        switch (e.Kind)
        {
            case "lesson-completed":
                Add(XpRule.Codes.LessonCompleted, $"lesson:{e.SubjectRefId}");
                break;
            case "quiz-completed":
                Add(XpRule.Codes.QuizCompleted, $"quiz:{e.SubjectRefId}");
                if (e.ScorePercent is >= 90)
                {
                    Add(XpRule.Codes.QuizScore90, $"quiz90:{e.SubjectRefId}");
                }
                break;
            case "chapter-completed":
                Add(XpRule.Codes.ChapterCompleted, $"chapter:{e.SubjectRefId}");
                break;
            case "daily-active" when e.Day is { } day:
                // One streak bonus per local calendar day of real learning activity.
                Add(XpRule.Codes.DailyStreak, $"streak:{day:yyyy-MM-dd}");
                break;
            case "challenge-completed":
                Add(XpRule.Codes.ChallengeCompleted, e.Key is { } ck ? $"challenge:{ck}" : $"challenge:{e.SubjectRefId}");
                break;
            case "exam-completed":
                // Once per mock/class test, and once per day for self-made practice tests (Key = "practice:yyyy-MM-dd"),
                // so retaking or creating many tests can't farm points.
                var examKey = e.Key ?? e.SubjectRefId.ToString();
                Add(XpRule.Codes.ExamCompleted, $"exam:{examKey}");
                if (e.ScorePercent is >= 90)
                {
                    Add(XpRule.Codes.QuizScore90, $"exam90:{examKey}");
                }
                break;
            case "missions-completed" when e.Day is { } missionDay:
                Add(XpRule.Codes.DailyMissions, $"missions:{missionDay:yyyy-MM-dd}");
                break;
            default:
                // Unknown or unverified event kinds (including AI chat) earn nothing.
                break;
        }
        return awards;

        void Add(string code, string key)
        {
            if (rules.TryGetValue(code, out var points) && points > 0)
            {
                awards.Add(new Award(code, points, key));
            }
        }
    }

    /// <summary>Daily streak bonus: one award per local calendar day, keyed by date.</summary>
    public static Award? StreakBonus(DateOnly localDay, IReadOnlyDictionary<string, int> rules) =>
        rules.TryGetValue(XpRule.Codes.DailyStreak, out var p) && p > 0
            ? new Award(XpRule.Codes.DailyStreak, p, $"streak:{localDay:yyyy-MM-dd}")
            : null;

    /// <summary>Level from total XP: level n needs 100 * n * (n + 1) / 2 XP, so each level is a bit longer.</summary>
    public static int LevelFor(int totalXp)
    {
        var level = 1;
        while (100 * level * (level + 1) / 2 <= totalXp)
        {
            level++;
        }
        return level;
    }
}
