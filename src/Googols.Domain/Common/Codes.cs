using System.Security.Cryptography;

namespace Googols.Domain.Common;

/// <summary>Short human-friendly codes (no 0/O, 1/I/L) for joining classrooms and similar.</summary>
public static class Codes
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string New(int length)
    {
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(chars);
    }

    public static string Normalize(string code) => new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
