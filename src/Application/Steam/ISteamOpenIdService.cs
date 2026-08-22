namespace DiscordClone.Application.Steam;

/// <summary>
/// Hand-rolled Steam OpenID 2.0 (steamcommunity.com/openid) — Steam never migrated
/// this flow to OAuth2. Plain dictionaries in/out rather than ASP.NET Core's
/// IQueryCollection so the Application layer stays framework-agnostic; the controller
/// is responsible for flattening Request.Query before calling in.
/// </summary>
public interface ISteamOpenIdService
{
    /// <summary>Builds the steamcommunity.com/openid/login URL to redirect the user's browser to.</summary>
    string BuildLoginRedirectUrl(string returnToUrl, string realm);

    /// <summary>
    /// Verifies a Steam OpenID callback by re-posting its parameters back to Steam
    /// (the "check_authentication" round-trip) and returns the SteamID64 extracted
    /// from openid.claimed_id if — and only if — Steam confirms the response is
    /// genuine and unmodified. Returns null on any failure to verify.
    /// </summary>
    Task<string?> VerifyAndExtractSteamId64Async(IReadOnlyDictionary<string, string> callbackQuery, CancellationToken ct);
}
