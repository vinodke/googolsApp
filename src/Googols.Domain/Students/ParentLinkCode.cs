using System.Security.Cryptography;
using System.Text;
using Googols.Domain.Common;

namespace Googols.Domain.Students;

/// <summary>
/// One-time code a student shows to a parent so the parent can link their account.
/// Only a SHA-256 hash is stored; the plain code is shown once and never persisted.
/// </summary>
public class ParentLinkCode : Entity
{
    public const int Length = 8;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    // No 0/O, 1/I/L: easy to read out loud and type on a phone.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public Guid StudentId { get; set; }
    public required string CodeHash { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }

    public bool IsUsable(DateTime nowUtc) => UsedAtUtc is null && ExpiresAtUtc > nowUtc;

    public static string NewCode()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(chars);
    }

    /// <summary>"abcd-efgh" and "ABCDEFGH" are the same code.</summary>
    public static string Normalize(string code) =>
        new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));

    /// <summary>Display form, e.g. "ABCD-EFGH".</summary>
    public static string Format(string code) => code.Length == Length ? $"{code[..4]}-{code[4..]}" : code;
}

public static class ParentalConsent
{
    /// <summary>Bump when the consent text shown to parents changes; parents then confirm again.</summary>
    public const string CurrentVersion = "2026-11";
    public const int MaxParentsPerStudent = 4;
}
