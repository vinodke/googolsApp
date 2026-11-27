using Googols.Application.Ai;

namespace Googols.UnitTests;

public class AiQualityTests
{
    [Fact]
    public void Cost_uses_per_million_prices()
    {
        var p = new AiPricingOptions { InputPerMillionTokensUsd = 0.40m, OutputPerMillionTokensUsd = 1.60m, SpeechPerMillionCharactersUsd = 16m };
        Assert.Equal(0.0004m + 0.0016m, AiCost.Usd(1000, 1000, 0, p));
        Assert.Equal(0.016m, AiCost.Usd(0, 0, 1000, p));
    }

    [Fact]
    public void Token_estimate_counts_malayalam_more_densely()
    {
        Assert.Equal(0, AiCost.EstimateTokens(""));
        Assert.True(AiCost.EstimateTokens("പ്രകാശസംശ്ലേഷണം") > AiCost.EstimateTokens("photosynthesi"));
    }

    [Fact]
    public void Eval_passes_when_language_keywords_and_grounding_match()
    {
        var reply = "സസ്യങ്ങൾ സൂര്യപ്രകാശം ഉപയോഗിച്ച് ആഹാരം നിർമ്മിക്കുന്നു. ഇലകളിലെ chlorophyll ഇതിന് സഹായിക്കുന്നു. കാർബൺ ഡൈ ഓക്സൈഡ് ആവശ്യമാണ്.";
        var r = EvalScoring.Score(reply, "ml", EvalScoring.Lines("chlorophyll|ഹരിതകം\nസൂര്യപ്രകാശം|sunlight"), EvalScoring.Lines("page 42"), true, true);
        Assert.True(r.Passed);
        Assert.Equal(1.0, r.Score);
    }

    [Fact]
    public void Eval_fails_on_wrong_language_missing_points_and_forbidden_text()
    {
        var reply = "Plants make food using sunlight. See textbook page 42 for details about the process.";
        var r = EvalScoring.Score(reply, "ml", EvalScoring.Lines("chlorophyll\nsunlight"), EvalScoring.Lines("page 42"), true, false);
        Assert.False(r.Passed);
        Assert.Contains(r.Checks, c => c.Name == "Right language" && !c.Passed);
        Assert.Contains(r.Checks, c => c.Name == "Key points covered" && !c.Passed && c.Detail!.Contains("chlorophyll"));
        Assert.Contains(r.Checks, c => c.Name == "Nothing it must not say" && !c.Passed);
        Assert.Contains(r.Checks, c => c.Name == "Used the published lessons" && !c.Passed);
    }

    [Fact]
    public void Photo_prompt_rules_appear_only_with_a_photo()
    {
        var ctx = new StudentLearningContext(Guid.NewGuid(), "KL-SCERT", "2026-27", 8, Guid.NewGuid(), null, null, "ml");
        Assert.DoesNotContain("photo", TeacherPromptBuilder.Build(ctx, TeacherMode.DoubtSolver, []), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attached a photo", TeacherPromptBuilder.Build(ctx, TeacherMode.DoubtSolver, [], hasImage: true));
    }
}
