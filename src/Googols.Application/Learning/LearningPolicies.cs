using System.Globalization;

namespace Googols.Application.Learning;

/// <summary>Chapter mastery: a moving average of practice results, blended with how many lessons were finished.</summary>
public static class MasteryPolicy
{
    public const double NewResultWeight = 0.6;
    public const double WeakBelow = 60;

    public static double Next(double? previous, double resultPercent) =>
        Math.Round(previous is { } p ? NewResultWeight * resultPercent + (1 - NewResultWeight) * p : resultPercent, 1);

    /// <summary>Null until the student has practised the chapter at least once.</summary>
    public static double? Score(double? practice, int lessonsDone, int lessonsTotal)
    {
        if (practice is not { } p)
        {
            return null;
        }
        var lessonShare = lessonsTotal == 0 ? 1.0 : Math.Min(1.0, (double)lessonsDone / lessonsTotal);
        return Math.Round(0.8 * p + 20 * lessonShare, 0);
    }

    public static string Band(double? score) => score switch
    {
        null => "Not tested",
        < 40 => "Needs work",
        < 70 => "Getting there",
        _ => "Strong",
    };
}

/// <summary>Builds a 7-day plan from chapter progress: weak chapters first, then unfinished lessons, untested chapters and stale revision.</summary>
public static class StudyPlanner
{
    public sealed record ChapterState(Guid ChapterId, string Title, Guid SubjectId, string Subject, int LessonsTotal, int LessonsDone,
        Guid? NextLessonId, bool HasQuiz, double? Mastery, DateTime? LastPracticedUtc);

    public sealed record PlanTask(string Kind, string Title, string Reason, Guid ChapterId, Guid? LessonId, string Subject);

    public sealed record PlanDay(DateOnly Date, IReadOnlyList<PlanTask> Tasks);

    public const int TasksPerDay = 2;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(14);

    public static IReadOnlyList<PlanDay> Build(IReadOnlyList<ChapterState> chapters, DateOnly today, DateTime nowUtc)
    {
        var tasks = new List<PlanTask>();

        foreach (var c in chapters.Where(c => c.Mastery is < MasteryPolicy.WeakBelow).OrderBy(c => c.Mastery))
        {
            if (c.NextLessonId is { } next)
            {
                tasks.Add(new PlanTask("lesson", $"Revise {c.Title}", $"Mastery is {c.Mastery:0}%. Finish its lessons again.", c.ChapterId, next, c.Subject));
            }
            else
            {
                tasks.Add(new PlanTask("ask", $"Ask the AI teacher about {c.Title}", $"Mastery is {c.Mastery:0}%. Clear your doubts first.", c.ChapterId, null, c.Subject));
            }
            if (c.HasQuiz)
            {
                tasks.Add(new PlanTask("quiz", $"Retake the {c.Title} quiz", "Practise until you score 70% or more.", c.ChapterId, null, c.Subject));
            }
        }
        foreach (var c in chapters.Where(c => c.LessonsDone > 0 && c.LessonsDone < c.LessonsTotal && c.Mastery is null or >= MasteryPolicy.WeakBelow && c.NextLessonId is not null))
        {
            tasks.Add(new PlanTask("lesson", $"Continue {c.Title}", $"{c.LessonsDone} of {c.LessonsTotal} lessons done.", c.ChapterId, c.NextLessonId, c.Subject));
        }
        foreach (var c in chapters.Where(c => c.Mastery is null && c.HasQuiz && c.LessonsTotal > 0 && c.LessonsDone >= c.LessonsTotal))
        {
            tasks.Add(new PlanTask("quiz", $"Take the {c.Title} quiz", "You've finished the lessons. Check what you remember.", c.ChapterId, null, c.Subject));
        }
        foreach (var c in chapters.Where(c => c.LessonsDone == 0 && c.Mastery is null && c.NextLessonId is not null))
        {
            tasks.Add(new PlanTask("lesson", $"Start {c.Title}", "A new chapter for you.", c.ChapterId, c.NextLessonId, c.Subject));
        }
        foreach (var c in chapters.Where(c => c.Mastery >= 70 && c.LastPracticedUtc is { } at && nowUtc - at > StaleAfter && c.HasQuiz))
        {
            tasks.Add(new PlanTask("quiz", $"Quick revision: {c.Title}", "It's been a while. A short quiz keeps it fresh.", c.ChapterId, null, c.Subject));
        }

        // Spread across the week; never the same chapter twice on one day when there's a choice.
        var days = Enumerable.Range(0, 7).Select(i => new List<PlanTask>()).ToList();
        var queue = new List<PlanTask>(tasks.Take(7 * TasksPerDay));
        var d = 0;
        while (queue.Count > 0 && d < 7)
        {
            var pick = queue.FirstOrDefault(t => days[d].All(x => x.ChapterId != t.ChapterId)) ?? queue[0];
            days[d].Add(pick);
            queue.Remove(pick);
            if (days[d].Count >= TasksPerDay)
            {
                d++;
            }
        }
        return days.Select((list, i) => new PlanDay(today.AddDays(i), list)).ToList();
    }
}

/// <summary>Three small goals each day. Finishing all of them pays a once-a-day bonus.</summary>
public static class MissionPolicy
{
    public sealed record Activity(int Lessons, int QuizzesOrTests, double BestScore);

    public sealed record Mission(string Code, string Title, int Progress, int Target, bool Done);

    public static IReadOnlyList<Mission> Today(Activity a) =>
    [
        new("lesson", "Finish a lesson", Math.Min(a.Lessons, 1), 1, a.Lessons >= 1),
        new("practice", "Complete a quiz or test", Math.Min(a.QuizzesOrTests, 1), 1, a.QuizzesOrTests >= 1),
        new("score", "Score 80% or more in a quiz or test", a.BestScore >= 80 ? 1 : 0, 1, a.BestScore >= 80),
    ];
}

/// <summary>Two challenges a week (Monday to Sunday, India time), rotating by week number.</summary>
public static class ChallengePolicy
{
    public sealed record WeekActivity(int Lessons, int QuizzesOrTests, int ActiveDays, int HighScores);

    public sealed record Challenge(string Code, string Title, string Description, int Progress, int Target, bool Done);

    private static readonly (string Code, string Title, string Description, int Target, Func<WeekActivity, int> Measure)[] Catalog =
    [
        ("lessons-5", "Lesson marathon", "Finish 5 lessons this week", 5, a => a.Lessons),
        ("practice-3", "Practice makes perfect", "Complete 3 quizzes or tests this week", 3, a => a.QuizzesOrTests),
        ("days-5", "Five-day learner", "Learn on 5 different days this week", 5, a => a.ActiveDays),
        ("high-2", "High flyer", "Score 90% or more twice this week", 2, a => a.HighScores),
    ];

    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static IReadOnlyList<Challenge> ForWeek(DateOnly weekStart, WeekActivity a)
    {
        var week = ISOWeek.GetWeekOfYear(weekStart.ToDateTime(TimeOnly.MinValue));
        var first = week % Catalog.Length;
        var picks = new[] { Catalog[first], Catalog[(first + 1) % Catalog.Length] };
        return picks.Select(c =>
        {
            var p = c.Measure(a);
            return new Challenge(c.Code, c.Title, c.Description, Math.Min(p, c.Target), c.Target, p >= c.Target);
        }).ToList();
    }
}
