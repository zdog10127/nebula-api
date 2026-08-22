namespace DiscordClone.Application.Auth;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<LoginOutcome> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResult> VerifyTwoFactorAsync(VerifyTwoFactorRequest request, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct);
    Task LogoutAsync(string refreshToken, CancellationToken ct);
    Task<UserProfile> GetProfileAsync(Guid userId, CancellationToken ct);
    Task<UserProfile> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct);
    Task<PublicProfileDto> GetPublicProfileAsync(Guid userId, CancellationToken ct);
    Task<TwoFactorSetupResult> SetupTwoFactorAsync(Guid userId, CancellationToken ct);
    Task<EnableTwoFactorResult> EnableTwoFactorAsync(Guid userId, EnableTwoFactorRequest request, CancellationToken ct);
    Task DisableTwoFactorAsync(Guid userId, DisableTwoFactorRequest request, CancellationToken ct);

    Task<SteamLinkStartResult> StartSteamLinkAsync(Guid userId, CancellationToken ct);
    Task<SteamLinkCallbackResult> CompleteSteamLinkAsync(IReadOnlyDictionary<string, string> callbackQuery, CancellationToken ct);

    /// <summary>Returns the user's effective activity right after unlinking (Steam cleared, may still fall back to a locally-detected one) so the caller can broadcast it.</summary>
    Task<string?> UnlinkSteamAsync(Guid userId, CancellationToken ct);
}
