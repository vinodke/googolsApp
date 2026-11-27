using Googols.Application.Assessment;

namespace Googols.UnitTests;

public class QuizTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    [Fact]
    public void Grading_counts_correct_answers_and_treats_blanks_as_wrong()
    {
        var key = new[] { new QuizGrading.Key(A, 1), new QuizGrading.Key(B, 0), new QuizGrading.Key(C, 3) };
        var r = QuizGrading.Grade(key, [1, null, 3]);
        Assert.Equal(2, r.Score);
        Assert.Equal(3, r.Total);
        Assert.Equal(66.7, r.Percent);
        Assert.Equal([true, false, true], r.PerQuestion);
    }

    [Fact]
    public void Pick_never_repeats_and_respects_the_count()
    {
        var bank = Enumerable.Range(0, 30).Select(i => (Guid.NewGuid(), i % 3 + 1)).ToList();
        var picked = QuizGrading.Pick(bank, 10, seed: 42);
        Assert.Equal(10, picked.Count);
        Assert.Equal(10, picked.Distinct().Count());
        Assert.Equal(picked, QuizGrading.Pick(bank, 10, seed: 42)); // deterministic for a seed
        Assert.Equal(5, QuizGrading.Pick(bank.Take(5).ToList(), 10, 1).Count);
    }

    [Fact]
    public void Model_reply_is_parsed_and_bad_items_dropped()
    {
        var reply = """
            Here you go:
            [
              {"prompt":"What do leaves need to make food?","options":["Sunlight","Sand","Salt","Plastic"],"correctIndex":0,"explanation":"Photosynthesis uses light.","difficulty":1},
              {"prompt":"Broken","options":["Only one"],"correctIndex":0},
              {"prompt":"Out of range","options":["a","b"],"correctIndex":5},
              {"prompt":"Duplicate options","options":["x","X"],"correctIndex":0}
            ]
            """;
        var parsed = QuestionDraftParser.Parse(reply, "en");
        var q = Assert.Single(parsed);
        Assert.Equal("Sunlight", q.Options[q.CorrectIndex]);
        Assert.Equal(1, q.Difficulty);
        Assert.Empty(QuestionDraftParser.Parse("sorry, no JSON", "en"));
    }

    [Fact]
    public void Validator_requires_a_marked_answer()
    {
        var v = new QuestionUpsertValidator();
        Assert.False(v.Validate(new QuestionUpsertRequest("Q?", ["a", "b"], -1, null, 2, null)).IsValid);
        Assert.True(v.Validate(new QuestionUpsertRequest("Q?", ["a", "b"], 1, null, 2, "ml")).IsValid);
    }
}
