using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace DiscordClone.Infrastructure.Auth;

/// <summary>
/// Encrypts/decrypts TOTP secrets at rest with AES-256-GCM, so a database dump alone
/// isn't enough to generate valid 2FA codes for every account — the key lives only in
/// the backend's environment configuration, never in Mongo alongside the ciphertext.
/// Mirrors the fail-fast pattern used by JwtOptions: a missing key throws at startup
/// rather than silently storing secrets in plaintext.
/// </summary>
public class TotpSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public TotpSecretProtector(IConfiguration configuration)
    {
        var base64Key = configuration["TOTP_ENCRYPTION_KEY"]
            ?? throw new InvalidOperationException("TOTP_ENCRYPTION_KEY is not configured.");

        try
        {
            _key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("TOTP_ENCRYPTION_KEY must be a valid base64 string.");
        }

        if (_key.Length != 32)
            throw new InvalidOperationException("TOTP_ENCRYPTION_KEY must decode to exactly 32 bytes (AES-256).");
    }

    public string Encrypt(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using (var aesGcm = new AesGcm(_key, TagSize))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        // Pack as nonce || tag || ciphertext into a single base64 string so only one
        // column/field is needed to store an encrypted secret.
        var packed = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, packed, nonce.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(packed);
    }

    public string Decrypt(string packedBase64)
    {
        var packed = Convert.FromBase64String(packedBase64);
        var nonce = packed[..NonceSize];
        var tag = packed[NonceSize..(NonceSize + TagSize)];
        var ciphertext = packed[(NonceSize + TagSize)..];
        var plaintextBytes = new byte[ciphertext.Length];

        using (var aesGcm = new AesGcm(_key, TagSize))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        }

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
