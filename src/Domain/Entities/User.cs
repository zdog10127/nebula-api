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

    // Opt-out "what game am I playing" activity status (see TotpSecret comment above for
    // the general pattern: on by default like Discord, flip-off-able in account settings).
    // The activity text itself never lives here — it's short-lived, so it's kept in Redis
    // (see PresenceService) alongside online/away/DnD status, not in Mongo.
    public bool ShareActivityStatus { get; set; } = true;

    // Steam account link (opt-in, separate from ShareActivityStatus above — a user can
    // share locally-detected game activity without ever linking Steam, and vice versa).
    // Null until AuthService.CompleteSteamLinkAsync finishes the OpenID flow. Unique +
    // sparse indexed (see MongoIndexInitializer) so the same Steam account can't end up
    // linked to two Nébula accounts at once. SteamActivityPollingService uses this to
    // know which online users to poll via the Steam Web API.
    public string? SteamId64 { get; set; }
}
