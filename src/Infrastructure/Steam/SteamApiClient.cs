using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DiscordClone.Application.Steam;

namespace DiscordClone.Infrastructure.Steam;

/// <summary>
/// Thin wrapper over Steam's GetPlayerSummaries Web API. gameextrainfo (the current
/// game's display name) is only present when the player is actually in-game AND their
/// Steam privacy settings allow "game details" to be visible to this API key — an
/// account with a private profile simply never reports an activity here, same as it
/// wouldn't show one to a random visitor on their Steam profile page. That's a real
/// reliability caveat inherent to Steam's API, not a bug in this wrapper.
/// </summary>
public class SteamApiClient : ISteamApiClient
{
    private const int MaxIdsPerRequest = 100;
    private const string SummariesUrl = "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/";

    private readonly HttpClient _http;
    private readonly SteamOptions _options;

    public SteamApiClient(HttpClient http, SteamOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<IReadOnlyDictionary<string, SteamPlayerActivity>> GetPlayerActivitiesAsync(IReadOnlyList<string> steamIds64, CancellationToken ct)
    {
        var result = new Dictionary<string, SteamPlayerActivity>();
        if (string.IsNullOrEmpty(_options.ApiKey) || steamIds64.Count == 0)
            return result;

        foreach (var batch in steamIds64.Chunk(MaxIdsPerRequest))
        {
            var ids = string.Join(',', batch);
            var url = $"{SummariesUrl}?key={Uri.EscapeDataString(_options.ApiKey)}&steamids={ids}";

            SteamSummariesResponse? response;
            try
            {
                response = await _http.GetFromJsonAsync<SteamSummariesResponse>(url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                // Steam's API being unreachable, rate-limited, or returning something
                // unparseable shouldn't crash the whole polling tick — this batch's
                // activities just stay stale until the next one, same as any other
                // best-effort background refresh.
                continue;
            }

            foreach (var player in response?.Response?.Players ?? [])
                result[player.SteamId] = new SteamPlayerActivity(player.SteamId, player.GameExtraInfo);
        }

        return result;
    }

    private record SteamSummariesResponse([property: JsonPropertyName("response")] SteamSummariesInner? Response);
    private record SteamSummariesInner([property: JsonPropertyName("players")] List<SteamPlayer>? Players);
    private record SteamPlayer(
        [property: JsonPropertyName("steamid")] string SteamId,
        [property: JsonPropertyName("gameextrainfo")] string? GameExtraInfo);
}
