namespace DiscordClone.Application.Steam;

/// <summary>GameName is null when the player is offline, or in-game but their Steam privacy settings hide it.</summary>
public record SteamPlayerActivity(string SteamId64, string? GameName);

public interface ISteamApiClient
{
    /// <summary>
    /// Batches internally (Steam's GetPlayerSummaries accepts at most 100 IDs per
    /// call). Returns only the SteamIDs Steam actually responded with — a player with
    /// no entry in the result (private profile, invalid ID, Steam API hiccup) should
    /// be treated as "unknown", not "definitely not playing anything".
    /// </summary>
    Task<IReadOnlyDictionary<string, SteamPlayerActivity>> GetPlayerActivitiesAsync(IReadOnlyList<string> steamIds64, CancellationToken ct);
}
