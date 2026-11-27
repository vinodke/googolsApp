using FluentValidation;
using Googols.Domain.Assessment;
using Googols.Domain.Common;

namespace Googols.Application.Assessment;

public sealed record QuestionUpsertRequest(string Prompt, IReadOnlyList<string> Options, int CorrectIndex, string? Explanation, int Difficulty, string? Language);

public sealed record AdminQuestionDto(Guid Id, string Prompt, IReadOnlyList<string> Options, int CorrectIndex, string? Explanation, int Difficulty,
    string Language, string Source, string Status);

public sealed record QuizQuestionDto(Guid Id, string Prompt, IReadOnlyList<string> Options);

public sealed record QuizStartDto(Guid AttemptId, Guid ChapterId, string ChapterTitle, IReadOnlyList<QuizQuestionDto> Questions);

public sealed record SubmitQuizRequest(IReadOnlyList<int?> Answers);

public sealed record QuizReviewItem(Guid QuestionId, string Prompt, IReadOnlyList<string> Options, int? Chosen, int Correct, bool IsCorrect, string? Explanation);

public sealed record QuizResultDto(Guid AttemptId, int Score, int Total, double Percent, int XpAwarded, int StreakDays, IReadOnlyList<QuizReviewItem> Review,
    IReadOnlyList<string> NewBadges);

public sealed record QuizHistoryDto(Guid AttemptId, DateTime SubmittedAtUtc, int Score, int Total, double Percent);

public sealed record GenerateQuestionsRequest(int Count);

public sealed class QuestionUpsertValidator : AbstractValidator<QuestionUpsertRequest>
{
    public QuestionUpsertValidator()
    {
        RuleFor(x => x.Prompt).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Options).NotNull()
            .Must(o => o.Count is >= Question.MinOptions and <= Question.MaxOptions).WithMessage("Give between 2 and 6 answer options.")
            .Must(o => o.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 300)).WithMessage("Options can't be empty or longer than 300 characters.")
            .Must(o => o.Select(s => s.Trim().ToLowerInvariant()).Distinct().Count() == o.Count).WithMessage("Each option must be different.");
        RuleFor(x => x.CorrectIndex).Must((r, i) => r.Options is not null && i >= 0 && i < r.Options.Count).WithMessage("Mark which option is correct.");
        RuleFor(x => x.Explanation).MaximumLength(1000);
        RuleFor(x => x.Difficulty).InclusiveBetween(1, 3);
        RuleFor(x => x.Language).Must(l => l is null || LanguageCodes.IsSupported(l)).WithMessage("Choose en or ml.");
    }
}

public static class QuizGrading
{
    public sealed record Key(Guid QuestionId, int Correct);

    /// <summary>Scores answers against the frozen key. Missing or out-of-range answers count as wrong.</summary>
    public static (int Score, int Total, double Percent, IReadOnlyList<bool> PerQuestion) Grade(IReadOnlyList<Key> key, IReadOnlyList<int?> answers)
    {
        var per = key.Select((k, i) => i < answers.Count && answers[i] is { } a && a == k.Correct).ToList();
        var score = per.Count(x => x);
        var total = key.Count;
        var percent = total == 0 ? 0 : Math.Round(100.0 * score / total, 1);
        return (score, total, percent, per);
    }

    /// <summary>Random selection weighted to cover difficulties; deterministic for a given seed (testable).</summary>
    public static IReadOnlyList<Guid> Pick(IReadOnlyList<(Guid Id, int Difficulty)> bank, int count, int seed)
    {
        var rng = new Random(seed);
        return bank.GroupBy(q => q.Difficulty)
            .SelectMany(g => g.OrderBy(_ => rng.Next()).Select((q, i) => (q.Id, Rank: i)))
            .OrderBy(x => x.Rank).ThenBy(_ => rng.Next())
            .Take(count).Select(x => x.Id).OrderBy(_ => rng.Next()).ToList();
    }
}
