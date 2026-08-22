using System.Text.RegularExpressions;
using DiscordClone.Application.Steam;

namespace DiscordClone.Infrastructure.Steam;

/// <summary>
/// Hand-rolled instead of pulling in a third-party OpenID/OAuth NuGet package: same
/// rationale as TotpService — this environment has no way to compile-check a new
/// dependency before it reaches production, and the actual protocol surface needed
/// here (build one redirect URL, verify one check_authentication round-trip) is small
/// enough to implement directly against HttpClient and the BCL.
/// </summary>
public partial class SteamOpenIdService : ISteamOpenIdService
{
    private const string SteamLoginEndpoint = "https://steamcommunity.com/openid/login";

    [GeneratedRegex(@"^https://steamcommunity\.com/openid/id/(\d+)$")]
    private static partial Regex ClaimedIdPattern();

    private readonly HttpClient _http;

    public SteamOpenIdService(HttpClient http)
    {
        _http = http;
    }

    public string BuildLoginRedirectUrl(string returnToUrl, string realm)
    {
        var query = new (string Key, string Value)[]
        {
            ("openid.ns", "http://specs.openid.net/auth/2.0"),
            ("openid.mode", "checkid_setup"),
            ("openid.return_to", returnToUrl),
            ("openid.realm", realm),
            ("openid.identity", "http://specs.openid.net/auth/2.0/identifier_select"),
            ("openid.claimed_id", "http://specs.openid.net/auth/2.0/identifier_select"),
        };

        var queryString = string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return $"{SteamLoginEndpoint}?{queryString}";
    }

    public async Task<string?> VerifyAndExtractSteamId64Async(IReadOnlyDictionary<string, string> callbackQuery, CancellationToken ct)
    {
        if (!callbackQuery.TryGetValue("openid.mode", out var mode) || mode != "id_res")
            return null;

        if (!callbackQuery.TryGetValue("openid.claimed_id", out var claimedId))
            return null;

        var match = ClaimedIdPattern().Match(claimedId);
        if (!match.Success)
            return null;

        // Re-post every openid.* parameter Steam sent us, only flipping the mode to
        // check_authentication. Steam's response body contains "is_valid:true" if and
        // only if this callback really came from them and hasn't been tampered with
        // or replayed — that check is what makes trusting openid.claimed_id safe.
        var form = callbackQuery
            .Where(kv => kv.Key.StartsWith("openid.", StringComparison.Ordinal) && kv.Key != "openid.mode")
            .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value))
            .Append(new KeyValuePair<string, string>("openid.mode", "check_authentication"))
            .ToList();

        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(SteamLoginEndpoint, content, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        var isValid = body.Split('\n').Any(line => line.Trim() == "is_valid:true");

        return isValid ? match.Groups[1].Value : null;
    }
}
