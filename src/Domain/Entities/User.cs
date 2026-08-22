namespace DiscordClone.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? BannerColor { get; set; }
    public string? Bio { get; set; }
    public string? Pronouns { get; set; }
    public string? CustomStatusText { get; set; }
    public string? CustomStatusEmoji { get; set; }
    public DateTime CreatedAt { get; set; }

    // Opt-in two-factor authentication (TOTP). TotpSecret is encrypted at rest via
    // TotpSecretProtector — never stored or logged in plaintext. Only ever set/read by
    // AuthService; nothing else should touch these fields directly.
    public string? TotpSecret { get; set; }
    public bool TotpEnabled { get; set; }
    public List<string> TotpRecoveryCodeHashes { get; set; } = new();
}
