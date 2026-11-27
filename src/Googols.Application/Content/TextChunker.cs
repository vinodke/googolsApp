using System.Text;

namespace Googols.Application.Content;

/// <summary>
/// Splits lesson text into retrieval chunks on paragraph boundaries, up to <see cref="MaxChars"/> each.
/// Headings ("## ...") are carried into the following chunk so each chunk keeps its context.
/// </summary>
public static class TextChunker
{
    public const int MaxChars = 900;

    public static IReadOnlyList<string> Chunk(string text, int maxChars = MaxChars)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }
        var paragraphs = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new StringBuilder();
        string? heading = null;

        foreach (var para in paragraphs)
        {
            if (para.StartsWith('#'))
            {
                Flush();
                heading = para.TrimStart('#', ' ');
                continue;
            }
            foreach (var piece in SplitLong(para, maxChars))
            {
                if (current.Length > 0 && current.Length + piece.Length + 2 > maxChars)
                {
                    Flush();
                }
                if (current.Length == 0 && heading is not null)
                {
                    current.Append(heading).Append(": ");
                }
                if (current.Length > 0 && !current.ToString().EndsWith(": "))
                {
                    current.Append("\n\n");
                }
                current.Append(piece);
            }
        }
        Flush();
        return chunks;

        void Flush()
        {
            if (current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }
        }
    }

    /// <summary>Very long paragraphs are cut at sentence ends (". ", "? ", "! ", "। ") where possible.</summary>
    private static IEnumerable<string> SplitLong(string para, int maxChars)
    {
        while (para.Length > maxChars)
        {
            var cut = -1;
            foreach (var mark in new[] { ". ", "? ", "! ", "। " })
            {
                var i = para.LastIndexOf(mark, maxChars - 1, StringComparison.Ordinal);
                cut = Math.Max(cut, i < 0 ? -1 : i + mark.Length);
            }
            if (cut < maxChars / 3)
            {
                cut = para.LastIndexOf(' ', maxChars - 1);
            }
            if (cut <= 0)
            {
                cut = maxChars;
            }
            yield return para[..cut].Trim();
            para = para[cut..].Trim();
        }
        if (para.Length > 0)
        {
            yield return para;
        }
    }
}
