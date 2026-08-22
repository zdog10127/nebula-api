using System.Security.Cryptography;
using System.Text;

namespace DiscordClone.Infrastructure.Auth;

/// <summary>
/// Minimal RFC 6238 TOTP (built on RFC 4226 HOTP) implementation using only BCL
/// primitives. Deliberately hand-rolled instead of pulling in a third-party TOTP
/// NuGet package: this environment has no way to compile-check a new dependency
/// before it reaches production, so sticking to HMACSHA1 + RandomNumberGenerator
/// (both already in the BCL) avoids shipping an unverifiable package version.
/// </summary>
public static class TotpService
{
    private const int SecretBytesLength = 20; // 160 bits, matches Google Authenticator's default
    private const int Digits = 6;
    private const int StepSeconds = 30;
    private const int WindowSteps = 1; // accept 1 step before/after to absorb clock drift

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretBytesLength);
        return Base32Encode(bytes);
    }

    public static string BuildOtpAuthUri(string secretBase32, string accountLabel, string issuer)
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedLabel = Uri.EscapeDataString($"{issuer}:{accountLabel}");
        return $"otpauth://totp/{encodedLabel}?secret={secretBase32}&issuer={encodedIssuer}&digits={Digits}&period={StepSeconds}&algorithm=SHA1";
    }

    public static bool Validate(string secretBase32, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        code = code.Trim();
        if (code.Length != Digits || !code.All(char.IsDigit))
            return false;

        var secretBytes = Base32Decode(secretBase32);
        var currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;
        var codeBytes = Encoding.ASCII.GetBytes(code);

        for (var offset = -WindowSteps; offset <= WindowSteps; offset++)
        {
            var candidate = ComputeCode(secretBytes, currentStep + offset);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), codeBytes))
                return true;
        }

        return false;
    }

    private static string ComputeCode(byte[] secretBytes, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        var truncated = binaryCode % (int)Math.Pow(10, Digits);
        return truncated.ToString().PadLeft(Digits, '0');
    }

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int bitBuffer = 0, bitsInBuffer = 0;

        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                sb.Append(Base32Alphabet[(bitBuffer >> bitsInBuffer) & 0x1F]);
            }
        }

        if (bitsInBuffer > 0)
            sb.Append(Base32Alphabet[(bitBuffer << (5 - bitsInBuffer)) & 0x1F]);

        return sb.ToString();
    }

    private static byte[] Base32Decode(string base32)
    {
        var cleaned = base32.Trim().TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(cleaned.Length * 5 / 8);
        int bitBuffer = 0, bitsInBuffer = 0;

        foreach (var c in cleaned)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0) continue; // skip stray formatting characters (spaces, dashes)

            bitBuffer = (bitBuffer << 5) | index;
            bitsInBuffer += 5;
            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                bytes.Add((byte)((bitBuffer >> bitsInBuffer) & 0xFF));
            }
        }

        return bytes.ToArray();
    }
}
