namespace Googols.Application.Gamification;

/// <summary>
/// Daily learning streak in the student's local calendar (Asia/Kolkata). Activity on consecutive days
/// grows the streak; a missed day restarts it at 1. Only real learning (lesson or quiz completion) counts.
/// </summary>
public static class StreakPolicy
{
    public sealed record Result(int StreakDays, bool IsNewDay);

    public static Result Next(int currentStreak, DateOnly? lastActive, DateOnly today)
    {
        if (lastActive == today)
        {
            return new Result(Math.Max(currentStreak, 1), false);
        }
        if (lastActive == today.AddDays(-1))
        {
            return new Result(currentStreak + 1, true);
        }
        return new Result(1, true);
    }

    /// <summary>The streak shown today: it lapses to 0 once a full day has been missed.</summary>
    public static int Current(int storedStreak, DateOnly? lastActive, DateOnly today) =>
        lastActive is { } d && d >= today.AddDays(-1) ? storedStreak : 0;
}
