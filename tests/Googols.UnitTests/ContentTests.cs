using Googols.Application.Ai;
using Googols.Application.Content;
using Googols.Application.Gamification;
using Googols.Domain.Gamification;

namespace Googols.UnitTests;

public class TextChunkerTests
{
    [Fact]
    public void Short_paragraphs_are_merged_and_headings_carried_into_the_chunk()
    {
        var chunks = TextChunker.Chunk("## Photosynthesis\n\nPlants make food.\n\nThey need sunlight.");
        Assert.Single(chunks);
        Assert.StartsWith("Photosynthesis: Plants make food.", chunks[0]);
        Assert.Contains("They need sunlight.", chunks[0]);
    }

    [Fact]
    public void Long_text_is_split_under_the_limit_at_sentence_ends()
    {
        var para = string.Join(" ", Enumerable.Repeat("Leaves contain chlorophyll which absorbs light.", 60));
        var chunks = TextChunker.Chunk(para);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= TextChunker.MaxChars));
        Assert.All(chunks, c => Assert.EndsWith(".", c));
    }

    [Fact]
    public void Empty_text_gives_no_chunks() => Assert.Empty(TextChunker.Chunk("  \n\n "));
}

public class KeywordRankerTests
{
    private static readonly string[] Docs =
    [
        "Photosynthesis is how green plants make food using sunlight, water and carbon dioxide.",
        "The heart pumps blood through arteries and veins.",
        "ഇലകൾ സൂര്യപ്രകാശം ഉപയോഗിച്ച് ആഹാരം നിർമ്മിക്കുന്ന പ്രക്രിയയാണ് പ്രകാശസംശ്ലേഷണം.",
    ];

    [Fact]
    public void Finds_the_relevant_english_passage() =>
        Assert.Equal(0, KeywordRanker.Rank("What is photosynthesis?", Docs, 3)[0].Index);

    [Fact]
    public void Inflected_forms_match_by_shared_stem() =>
        Assert.Equal(0, KeywordRanker.Rank("photosynthetic plant", Docs, 3)[0].Index);

    [Fact]
    public void Finds_the_malayalam_passage() =>
        Assert.Equal(2, KeywordRanker.Rank("പ്രകാശസംശ്ലേഷണം എന്താണ്?", Docs, 3)[0].Index);

    [Fact]
    public void Questions_made_only_of_stop_words_match_nothing() => Assert.Empty(KeywordRanker.Rank("what is this?", Docs, 3));

    [Fact]
    public void Malayalam_signs_stay_inside_words() =>
        Assert.Equal(["സൂര്യപ്രകാശം"], KeywordRanker.Tokenize("സൂര്യപ്രകാശം."));
}

public class StreakPolicyTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);

    [Fact]
    public void Consecutive_days_grow_the_streak() => Assert.Equal(new StreakPolicy.Result(4, true), StreakPolicy.Next(3, Today.AddDays(-1), Today));

    [Fact]
    public void Same_day_activity_does_not_count_twice() => Assert.Equal(new StreakPolicy.Result(3, false), StreakPolicy.Next(3, Today, Today));

    [Fact]
    public void A_missed_day_restarts_at_one() => Assert.Equal(new StreakPolicy.Result(1, true), StreakPolicy.Next(9, Today.AddDays(-2), Today));

    [Fact]
    public void Displayed_streak_lapses_after_a_missed_day()
    {
        Assert.Equal(5, StreakPolicy.Current(5, Today.AddDays(-1), Today));
        Assert.Equal(0, StreakPolicy.Current(5, Today.AddDays(-2), Today));
    }

    [Fact]
    public void Daily_activity_pays_streak_xp_once_per_day()
    {
        var student = Guid.NewGuid();
        var a = XpAwardPolicy.Evaluate(new("daily-active", student, student, Day: Today), XpRule.Defaults);
        Assert.Equal("streak:2026-11-27", Assert.Single(a).IdempotencyKey);
        Assert.Empty(XpAwardPolicy.Evaluate(new("daily-active", student, student), XpRule.Defaults)); // no day, no award
    }
}

public class AttachmentRulesTests
{
    [Fact]
    public void File_type_comes_from_the_bytes_not_the_name()
    {
        Assert.Equal("application/pdf", AttachmentRules.DetectContentType("%PDF-1.7"u8));
        Assert.Equal("image/png", AttachmentRules.DetectContentType(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }));
        Assert.Null(AttachmentRules.DetectContentType("MZ\x90\0"u8)); // an .exe renamed to .pdf is rejected
    }

    [Fact]
    public void File_names_are_cleaned() =>
        Assert.Equal("Chapter 1 notesscript.pdf", AttachmentRules.SafeFileName("../../Chapter 1 notes<script>.exe", "application/pdf"));
}
