using Microsoft.Extensions.Configuration;

namespace DiscordClone.Infrastructure.Steam;

/// <summary>
/// Both values are optional on purpose, unlike Mongo/JWT/Redis/LiveKit/S3: Steam
/// linking is an opt-in add-on feature, not core infrastructure the server can't run
/// without. A deploy that hasn't set these up yet just has Steam linking disabled —
/// AuthService.StartSteamLinkAsync throws a clean 503 instead of the whole app
/// failing to boot.
/// </summary>
public class SteamOptions
{
    public string ApiKey { get; init; } = string.Empty;

    // The backend's own public HTTPS base URL (e.g. https://xxxxxxxx.cloudfront.net,
    // no trailing slash), used to build openid.realm/openid.return_to. Deliberately a
    // fixed config value instead of derived from the incoming request's Host/
    // X-Forwarded-* headers: this API sits behind CloudFront (see docs/AWS_SETUP.md),
    // and trusting those headers without a properly configured
    // ForwardedHeadersMiddleware + known-proxies allowlist would let a spoofed Host
    // header redirect Steam's callback somewhere else. A fixed value sidesteps that
    // risk entirely.
    public string PublicApiUrl { get; init; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(PublicApiUrl);

    public static SteamOptions FromConfiguration(IConfiguration configuration) => new()
    {
        ApiKey = configuration["STEAM_API_KEY"] ?? string.Empty,
        PublicApiUrl = (configuration["PUBLIC_API_URL"] ?? string.Empty).TrimEnd('/'),
    };
}
