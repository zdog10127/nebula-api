namespace DiscordClone.Application.Auth;

public record RegisterRequest(string Username, string Email, string Password, string? DisplayName);

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record AuthResult(
    Guid UserId,
    string Username,
    string Email,
    string DisplayName,
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken);

public record UserProfile(
    Guid UserId,
    string Username,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    string? BannerUrl,
    string? BannerColor,
    string? Bio,
    string? Pronouns,
    string? CustomStatusText,
    string? CustomStatusEmoji,
    bool TotpEnabled,
    bool ShareActivityStatus,
    bool SteamLinked);

public record PublicProfileDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    string? BannerUrl,
    string? BannerColor,
    string? Bio,
    string? Pronouns,
    string? CustomStatusText,
    string? CustomStatusEmoji,
    DateTime CreatedAt,
    string? CurrentActivity);

public record UpdateProfileRequest(
    string? DisplayName,
    string? Bio,
    string? Pronouns,
    string? BannerColor,
    string? CustomStatusText,
    string? CustomStatusEmoji,
    bool? ShareActivityStatus);

// Two-factor authentication (opt-in TOTP).

/// <summary>
/// Returned by LoginAsync. When RequiresTwoFactor is true, Result is null and the
/// client must call POST /api/auth/2fa/verify with LoginToken plus the code from the
/// user's authenticator app (or a recovery code) to actually receive tokens.
/// </summary>
public record LoginOutcome(bool RequiresTwoFactor, string? LoginToken, AuthResult? Result);

public record VerifyTwoFactorRequest(string LoginToken, string Code);

/// <summary>Returned by SetupTwoFactorAsync so the client can render a QR code (from OtpAuthUri) and a manual-entry fallback (SecretBase32).</summary>
public record TwoFactorSetupResult(string SecretBase32, string OtpAuthUri);

public record EnableTwoFactorRequest(string Code);

/// <summary>RecoveryCodes are shown to the user exactly once — only their hashes are stored.</summary>
public record EnableTwoFactorResult(IReadOnlyList<string> RecoveryCodes);

public record DisableTwoFactorRequest(string Password);

// Steam account link (opt-in, independent of ShareActivityStatus — see User.SteamId64).

/// <summary>Returned by StartSteamLinkAsync — the client should open RedirectUrl (system browser / new tab) to let the user log into Steam.</summary>
public record SteamLinkStartResult(string RedirectUrl);

/// <summary>Returned by CompleteSteamLinkAsync so AuthController can render a plain HTML confirmation/error page for the browser Steam redirected back to.</summary>
public record SteamLinkCallbackResult(bool Success, string Message);
