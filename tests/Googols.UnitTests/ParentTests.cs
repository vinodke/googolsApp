using Googols.Application.Common;
using Googols.Domain.Students;

namespace Googols.UnitTests;

public class ParentLinkCodeTests
{
    [Fact]
    public void New_codes_use_the_unambiguous_alphabet_and_length()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = ParentLinkCode.NewCode();
            Assert.Equal(ParentLinkCode.Length, code.Length);
            Assert.DoesNotContain(code, c => "01OIL".Contains(c));
        }
    }

    [Fact]
    public void Hash_ignores_case_spaces_and_dashes()
    {
        Assert.Equal(ParentLinkCode.Hash("ABCD-EFGH"), ParentLinkCode.Hash(" abcd efgh "));
        Assert.NotEqual(ParentLinkCode.Hash("ABCD-EFGH"), ParentLinkCode.Hash("ABCD-EFGJ"));
        Assert.Equal(64, ParentLinkCode.Hash("ABCDEFGH").Length);
    }

    [Fact]
    public void Codes_expire_and_are_single_use()
    {
        var now = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc);
        var code = new ParentLinkCode { CodeHash = "x", ExpiresAtUtc = now.AddDays(1) };
        Assert.True(code.IsUsable(now));
        Assert.False(code.IsUsable(now.AddDays(2)));
        code.UsedAtUtc = now;
        Assert.False(code.IsUsable(now));
    }

    [Fact]
    public void Format_adds_a_dash_in_the_middle() => Assert.Equal("ABCD-EFGH", ParentLinkCode.Format("ABCDEFGH"));

    [Theory]
    [InlineData("anitha.k@gmail.com", "an•••@gmail.com")]
    [InlineData("a@x.in", "a•••@x.in")]
    [InlineData("@x.in", "•••")]
    [InlineData(null, "•••")]
    public void Emails_are_masked(string? input, string expected) => Assert.Equal(expected, Privacy.MaskEmail(input));
}
