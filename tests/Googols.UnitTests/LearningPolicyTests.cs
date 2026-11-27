using Googols.Application.Assessment;
using Googols.Application.Gamification;
using Googols.Application.Learning;
using Googols.Domain.Gamification;

namespace Googols.UnitTests;

public class LearningPolicyTests
{
    [Fact]
    public void Mastery_moves_towards_recent_results_and_counts_lessons()
    {
        Assert.Equal(50, MasteryPolicy.Next(null, 50));
        Assert.Equal(82, MasteryPolicy.Next(70, 90)); // 0.6*90 + 0.4*70
        Assert.Null(MasteryPolicy.Score(null, 3, 3));
        Assert.Equal(92, MasteryPolicy.Score(90, 3, 3)); // 0.8*90 + 20
        Assert.Equal(82, MasteryPolicy.Score(90, 1, 2)); // 72 + 10
        Assert.Equal("Needs work", MasteryPolicy.Band(30));
        Assert.Equal("Strong", MasteryPolicy.Band(85));
        Assert.Equal("Not tested", MasteryPolicy.Band(null));
    }

    [Fact]
    public void Study_plan_puts_weak_chapters_first_and_spreads_two_tasks_a_day()
    {
        var s = Guid.NewGuid();
        var weak = new StudyPlanner.ChapterState(Guid.NewGuid(), "Cells", s, "Biology", 3, 3, Guid.NewGuid(), true, 35, DateTime.UtcNow);
        var newCh = new StudyPlanner.ChapterState(Guid.NewGuid(), "Plants", s, "Biology", 2, 0, Guid.NewGuid(), true, null, null);
        var halfway = new StudyPlanner.ChapterState(Guid.NewGuid(), "Food", s, "Biology", 4, 2, Guid.NewGuid(), false, null, null);
        var plan = StudyPlanner.Build([newCh, halfway, weak], new DateOnly(2026, 11, 27), DateTime.UtcNow);

        Assert.Equal(7, plan.Count);
        Assert.Equal("Revise Cells", plan[0].Tasks[0].Title);
        Assert.All(plan, d => Assert.True(d.Tasks.Count <= StudyPlanner.TasksPerDay));
        Assert.NotEqual(plan[0].Tasks[0].ChapterId, plan[0].Tasks[1].ChapterId); // different chapters on one day when possible
        var all = plan.SelectMany(d => d.Tasks).Select(t => t.Title).ToList();
        Assert.Contains("Continue Food", all);
        Assert.Contains("Start Plants", all);
    }

    [Fact]
    public void Missions_and_challenges_need_real_activity()
    {
        var none = MissionPolicy.Today(new MissionPolicy.Activity(0, 0, 0));
        Assert.All(none, m => Assert.False(m.Done));
        var all = MissionPolicy.Today(new MissionPolicy.Activity(1, 1, 85));
        Assert.All(all, m => Assert.True(m.Done));

        var monday = ChallengePolicy.WeekStart(new DateOnly(2026, 11, 27)); // Friday
        Assert.Equal(new DateOnly(2026, 11, 23), monday);
        var week = ChallengePolicy.ForWeek(monday, new ChallengePolicy.WeekActivity(5, 3, 5, 2));
        Assert.Equal(2, week.Count);
        Assert.All(week, c => Assert.True(c.Done));
        Assert.NotEqual(week[0].Code, week[1].Code);
    }

    [Fact]
    public void Practice_tests_pay_exam_xp_once_a_day_and_missions_once_a_day()
    {
        var rules = XpRule.Defaults;
        var st = Guid.NewGuid();
        var a = XpAwardPolicy.Evaluate(new XpAwardPolicy.LearningEvent("exam-completed", st, Guid.NewGuid(), 95, Key: "practice:2026-11-27"), rules);
        var b = XpAwardPolicy.Evaluate(new XpAwardPolicy.LearningEvent("exam-completed", st, Guid.NewGuid(), 95, Key: "practice:2026-11-27"), rules);
        Assert.Equal(a.Select(x => x.IdempotencyKey), b.Select(x => x.IdempotencyKey));
        Assert.Equal(2, a.Count); // completion + 90% bonus
        var m = XpAwardPolicy.Evaluate(new XpAwardPolicy.LearningEvent("missions-completed", st, st, Day: new DateOnly(2026, 11, 27)), rules);
        Assert.Equal("missions:2026-11-27", Assert.Single(m).IdempotencyKey);
    }

    [Fact]
    public void Rubric_marks_are_clamped_rounded_to_half_and_bad_replies_rejected()
    {
        var rubric = new[] { new RubricCriterion("Names chlorophyll", 1), new RubricCriterion("Explains light energy", 2) };
        var r = RubricMarking.Parse("""Sure! {"criteria":[{"point":1,"awarded":5,"comment":"ok"},{"point":2,"awarded":1.3}],"feedback":"Add the word glucose."}""", rubric);
        Assert.NotNull(r);
        Assert.Equal(1, r!.Criteria[0].Awarded);   // clamped to the criterion's maximum
        Assert.Equal(1.5, r.Criteria[1].Awarded);  // nearest half mark
        Assert.Equal(2.5, r.Score);
        Assert.Equal(3, r.Max);
        Assert.Null(RubricMarking.Parse("I can't mark this.", rubric));
        Assert.NotNull(RubricMarking.Validate([]));
        Assert.Null(RubricMarking.Validate(rubric));
        Assert.Contains("&lt;b&gt;", RubricMarking.BuildPrompt("Q", null, rubric, "<b>answer</b>", 8, "en"));
    }
}
