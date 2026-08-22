using System.Security.Cryptography;

namespace DiscordClone.Infrastructure.Auth;

/// <summary>
/// Generates one-time backup codes for accounts with 2FA enabled, for the case where
/// the user loses access to their authenticator app. Codes are shown to the user
/// exactly once at generation time; only their bcrypt hashes are ever persisted (see
/// AuthService.EnableTwoFactorAsync), the same treatment as the account password.
/// </summary>
public static class RecoveryCodeGenerator
{
    private const int CodeCount = 8;
    private const int GroupLength = 5;

    // No 0/O/1/I — characters that are easy to mix up when a person is transcribing a
    // code by hand from a phone screen to a login form.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static IReadOnlyList<string> Generate()
    {
        var codes = new List<string>(CodeCount);
        for (var i = 0; i < CodeCount; i++)
            codes.Add($"{RandomAlphabetString(GroupLength)}-{RandomAlphabetString(GroupLength)}");

        return codes;
    }

    private static string RandomAlphabetString(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        return new string(chars);
    }
}
