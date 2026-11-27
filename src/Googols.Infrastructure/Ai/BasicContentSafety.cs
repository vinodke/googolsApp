using System.Text.RegularExpressions;
using Googols.Application.Ai;

namespace Googols.Infrastructure.Ai;

/// <summary>
/// First-line screen run before and after every AI call: blocks messages carrying contact details
/// (phone numbers, email addresses) so children don't share them, and caps length.
/// Before launch, add Azure AI Content Safety (or equivalent) behind the same interface for
/// harmful-content categories; this class does not classify violence, self-harm or sexual content.
/// </summary>
public sealed partial class BasicContentSafety : IContentSafety
{
    private const int MaxLength = 4000;

    public Task<SafetyVerdict> CheckAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new SafetyVerdict(false, "empty"));
        }
        if (text.Length > MaxLength)
        {
            return Task.FromResult(new SafetyVerdict(false, "too-long"));
        }
        if (Email().IsMatch(text) || Phone().IsMatch(text))
        {
            return Task.FromResult(new SafetyVerdict(false, SafetyCategories.PersonalInfo));
        }
        return Task.FromResult(new SafetyVerdict(true, null));
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // Indian mobile numbers (optionally +91 / 0 prefixed), allowing spaces or hyphens.
    [GeneratedRegex(@"(?<!\d)(?:\+?91[\s-]?|0)?[6-9]\d{4}[\s-]?\d{5}(?!\d)")]
    private static partial Regex Phone();
}
