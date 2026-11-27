using System.Globalization;

namespace Googols.Application.Ai;

/// <summary>
/// BM25 ranking over a small candidate set (one subject's published chunks), used when Azure AI Search
/// isn't configured. Works for English and Malayalam: tokens are Unicode letter/digit runs, and two
/// words match if they are equal or share their first 4 letters (a rough stem for inflected forms,
/// e.g. "photosynthesis" / "photosynthetic", "സസ്യങ്ങൾ" / "സസ്യം").
/// </summary>
public static class KeywordRanker
{
    private const double K1 = 1.2;
    private const double B = 0.75;
    private const int StemLength = 4;

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "is", "are", "was", "were", "of", "to", "in", "on", "and", "or", "for", "what", "why", "how",
        "do", "does", "did", "can", "you", "me", "my", "i", "it", "this", "that", "with", "be", "about", "explain", "tell",
        "please", "give", "example", "simply", "എന്താണ്", "എന്ത്", "എങ്ങനെ", "ഒരു", "ആണ്",
    };

    public static IReadOnlyList<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && IsWordChar(text[i]);
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                var t = text[start..i].ToLowerInvariant();
                if (t.Length > 1 && !Stop.Contains(t))
                {
                    tokens.Add(t);
                }
                start = -1;
            }
        }
        return tokens;
    }

    private static bool IsWordChar(char c)
    {
        var cat = char.GetUnicodeCategory(c);
        // Malayalam vowel signs and virama are combining marks; keep them inside words.
        return char.IsLetterOrDigit(c) || cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }

    public static bool Matches(string queryTerm, string docTerm)
    {
        if (queryTerm == docTerm)
        {
            return true;
        }
        if (queryTerm.Length < StemLength || docTerm.Length < StemLength)
        {
            return false;
        }
        return string.CompareOrdinal(queryTerm, 0, docTerm, 0, StemLength) == 0
            && (docTerm.StartsWith(queryTerm, StringComparison.Ordinal) || queryTerm.StartsWith(docTerm, StringComparison.Ordinal)
                || CommonPrefix(queryTerm, docTerm) >= Math.Min(queryTerm.Length, docTerm.Length) * 0.7);
    }

    private static int CommonPrefix(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i])
        {
            i++;
        }
        return i;
    }

    /// <summary>Returns (index, score) for documents with a positive score, best first.</summary>
    public static IReadOnlyList<(int Index, double Score)> Rank(string query, IReadOnlyList<string> documents, int top)
    {
        var q = Tokenize(query).Distinct().ToList();
        if (q.Count == 0 || documents.Count == 0)
        {
            return [];
        }
        var docs = documents.Select(Tokenize).ToList();
        var avgLen = docs.Average(d => (double)Math.Max(d.Count, 1));
        var n = docs.Count;

        var scores = new double[n];
        foreach (var term in q)
        {
            var tf = docs.Select(d => d.Count(t => Matches(term, t))).ToArray();
            var df = tf.Count(x => x > 0);
            if (df == 0)
            {
                continue;
            }
            var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
            for (var i = 0; i < n; i++)
            {
                if (tf[i] == 0)
                {
                    continue;
                }
                var len = Math.Max(docs[i].Count, 1);
                scores[i] += idf * tf[i] * (K1 + 1) / (tf[i] + K1 * (1 - B + B * len / avgLen));
            }
        }
        return scores.Select((s, i) => (Index: i, Score: s)).Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).Take(top).ToList();
    }
}
