using DiscordClone.Domain.Enums;

namespace DiscordClone.Application.Presence;

public interface IPresenceService
{
    Task<bool> ConnectAsync(Guid userId, string connectionId, CancellationToken ct);
    Task<bool> DisconnectAsync(Guid userId, string connectionId, CancellationToken ct);
    Task<bool> HasActiveConnectionAsync(Guid userId, CancellationToken ct);

    Task SetStatusAsync(Guid userId, PresenceStatus status, CancellationToken ct);
    Task<PresenceStatus> GetEffectiveStatusAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, PresenceStatus>> GetEffectiveStatusesAsync(IEnumerable<Guid> userIds, CancellationToken ct);

    // "What game am I playing" activity text (e.g. "VALORANT"), detected client-side (see
    // the Electron app's gameActivity.cjs) and pushed here over the hub. Short-lived like
    // status — lives only in Redis, cleared automatically when the user goes offline.
    Task SetActivityAsync(Guid userId, string? activityName, CancellationToken ct);
    Task<string?> GetActivityAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string?>> GetActivitiesAsync(IEnumerable<Guid> userIds, CancellationToken ct);

    // Same idea, but sourced from SteamActivityPollingService instead of the Electron
    // app. Kept in a separate Redis key from SetActivityAsync above so the two sources
    // never overwrite each other; GetActivityAsync/GetActivitiesAsync check this one
    // first and fall back to the local one, since Steam's data is considered more
    // authoritative when both are available (it doesn't depend on the desktop app
    // being open, and updates even from a console or another PC).
    Task SetSteamActivityAsync(Guid userId, string? activityName, CancellationToken ct);

    // Raw (non-merged) Steam-only activity lookup — used only by
    // SteamActivityPollingService to diff against before writing, so it can skip
    // broadcasting ActivityChanged when nothing actually changed since the last poll.
    Task<IReadOnlyDictionary<Guid, string?>> GetSteamActivitiesAsync(IEnumerable<Guid> userIds, CancellationToken ct);

    // Backed by an explicit Redis SET (maintained in ConnectAsync/DisconnectAsync)
    // rather than a KEYS/SCAN over presence:* — safe to call periodically in
    // production. Used by SteamActivityPollingService to know which users are even
    // worth polling Steam for.
    Task<IReadOnlyList<Guid>> GetOnlineUserIdsAsync(CancellationToken ct);
}
