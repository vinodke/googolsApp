using System.Globalization;

namespace Googols.Application.Ai;

/// <summary>Model prices used for the usage and cost log. Defaults are Azure OpenAI gpt-4.1-mini and Azure Neural TTS list prices (USD).</summary>
public sealed class AiPricingOptions
{
    public const string Section = "AiPricing";
    public decimal InputPerMillionTokensUsd { get; set; } = 0.40m;
    public decimal OutputPerMillionTokensUsd { get; set; } = 1.60m;
    public decimal SpeechPerMillionCharactersUsd { get; set; } = 16m;
    public decimal UsdToInr { get; set; } = 85m;
}

public static class AiCost
{
    public static decimal Usd(int inputTokens, int outputTokens, int speechCharacters, AiPricingOptions p) =>
        Math.Round(inputTokens * p.InputPerMillionTokensUsd / 1_000_000m
            + outputTokens * p.OutputPerMillionTokensUsd / 1_000_000m
            + speechCharacters * p.SpeechPerMillionCharactersUsd / 1_000_000m, 6);

    /// <summary>Rough token count when the provider doesn't report usage (≈4 chars per token in English, fewer in Malayalam).</summary>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        var malayalam = text.Count(c => c is >= 'ഀ' and <= 'ൿ');
        return (int)Math.Ceiling((text.Length - malayalam) / 4.0 + malayalam / 1.5);
    }
}

/// <summary>
/// Scores one golden-set answer. Pure, so the same rules run in tests and in the admin "Run evals" button.
/// Expected keywords: one per line, alternatives separated by "|" (e.g. "chlorophyll|ഹരിതകം").
/// </summary>
public static class EvalScoring
{
    public sealed record Check(string Name, bool Passed, string? Detail);

    public sealed record Outcome(bool Passed, double Score, IReadOnlyList<Check> Checks);

    public const double KeywordPassShare = 0.8;

    public static IReadOnlyList<string> Lines(string? text) =>
        (text ?? "").Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0).Take(30).ToList();

    public static double MalayalamShare(string text)
    {
        var letters = text.Count(char.IsLetter);
        if (letters == 0)
        {
            return 0;
        }
        return (double)text.Count(c => c is >= 'ഀ' and <= 'ൿ' && char.IsLetter(c)) / letters;
    }

    public static Outcome Score(string reply, string language, IReadOnlyList<string> expected, IReadOnlyList<string> forbidden, bool expectGrounded, bool grounded)
    {
        var checks = new List<Check>();
        var text = reply ?? "";
        checks.Add(new Check("Answer given", text.Trim().Length >= 20, text.Trim().Length < 20 ? "The answer was empty or too short." : null));

        var ml = MalayalamShare(text);
        var langOk = language == "ml" ? ml >= 0.3 : ml < 0.1;
        checks.Add(new Check("Right language", langOk,
            langOk ? null : string.Create(CultureInfo.InvariantCulture, $"Malayalam letters were {ml:P0} of the answer.")));

        if (expected.Count > 0)
        {
            var missing = expected.Where(line => !line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(alt => text.Contains(alt, StringComparison.OrdinalIgnoreCase))).ToList();
            var share = 1 - (double)missing.Count / expected.Count;
            checks.Add(new Check("Key points covered", share >= KeywordPassShare,
                missing.Count == 0 ? null : "Missing: " + string.Join(", ", missing)));
        }

        if (forbidden.Count > 0)
        {
            var found = forbidden.Where(f => text.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
            checks.Add(new Check("Nothing it must not say", found.Count == 0, found.Count == 0 ? null : "Found: " + string.Join(", ", found)));
        }

        if (expectGrounded)
        {
            checks.Add(new Check("Used the published lessons", grounded, grounded ? null : "No lesson passage was found for this question."));
        }

        var passed = checks.Count(c => c.Passed);
        return new Outcome(passed == checks.Count, Math.Round((double)passed / checks.Count, 3), checks);
    }
}
