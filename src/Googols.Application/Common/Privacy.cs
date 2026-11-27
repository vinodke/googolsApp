namespace Googols.Application.Common;

public static class Privacy
{
    /// <summary>"anitha.k@gmail.com" → "an•••@gmail.com": enough to recognise, not enough to harvest.</summary>
    public static string MaskEmail(string? email)
    {
        var at = email?.IndexOf('@') ?? -1;
        if (email is null || at < 1)
        {
            return "•••";
        }
        return $"{email[..Math.Min(2, at)]}•••{email[at..]}";
    }

    /// <summary>"Anitha Krishnan" → "Anitha K." Classmates never see full names.</summary>
    public static string PublicName(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "Student",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }
}
