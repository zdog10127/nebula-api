using DiscordClone.Application.Presence;
using DiscordClone.Domain.Enums;
using StackExchange.Redis;

namespace DiscordClone.Infrastructure.Presence;

public class PresenceService : IPresenceService
{
    private readonly IConnectionMultiplexer _redis;

    public PresenceService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    private const string OnlineUsersSetKey = "online_users";

    private static string ConnectionsKey(Guid userId) => $"presence:{userId}";
    private static string StatusKey(Guid userId) => $"user_status:{userId}";
    private static string ActivityKey(Guid userId) => $"user_activity:{userId}";
    private static string SteamActivityKey(Guid userId) => $"steam_activity:{userId}";

    public async Task<bool> ConnectAsync(Guid userId, string connectionId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = ConnectionsKey(userId);
        var wasOffline = !await db.KeyExistsAsync(key);
        await db.SetAddAsync(key, connectionId);
        // SADD is idempotent, so no need to gate this on wasOffline — keeping
        // online_users in sync with presence:{userId} is what lets
        // SteamActivityPollingService find "who's online" without an unsafe KEYS scan.
        await db.SetAddAsync(OnlineUsersSetKey, userId.ToString());
        return wasOffline;
    }

    public async Task<bool> DisconnectAsync(Guid userId, string connectionId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = ConnectionsKey(userId);
        await db.SetRemoveAsync(key, connectionId);
        var remaining = await db.SetLengthAsync(key);

        if (remaining > 0)
            return false;

        await db.KeyDeleteAsync(key);
        await db.SetRemoveAsync(OnlineUsersSetKey, userId.ToString());
        // Fully offline (no connections left on any device) — an activity from a game
        // that isn't running anymore would otherwise linger forever until overwritten.
        await db.KeyDeleteAsync(ActivityKey(userId));
        await db.KeyDeleteAsync(SteamActivityKey(userId));
        return true;
    }

    public async Task<bool> HasActiveConnectionAsync(Guid userId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync(ConnectionsKey(userId));
    }

    public async Task SetStatusAsync(Guid userId, PresenceStatus status, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(StatusKey(userId), status.ToString());
    }

    public async Task<PresenceStatus> GetEffectiveStatusAsync(Guid userId, CancellationToken ct)
    {
        var statuses = await GetEffectiveStatusesAsync([userId], ct);
        return statuses.GetValueOrDefault(userId, PresenceStatus.Offline);
    }

    public async Task<IReadOnlyDictionary<Guid, PresenceStatus>> GetEffectiveStatusesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var ids = userIds.Distinct().ToList();

        var onlineChecks = await Task.WhenAll(ids.Select(id => db.KeyExistsAsync(ConnectionsKey(id))));
        var preferenceValues = await Task.WhenAll(ids.Select(id => db.StringGetAsync(StatusKey(id))));

        var result = new Dictionary<Guid, PresenceStatus>();
        for (var i = 0; i < ids.Count; i++)
        {
            if (!onlineChecks[i])
            {
                result[ids[i]] = PresenceStatus.Offline;
                continue;
            }

            var preference = preferenceValues[i].IsNullOrEmpty || !Enum.TryParse<PresenceStatus>(preferenceValues[i].ToString(), out var parsed)
                ? PresenceStatus.Online
                : parsed;

            result[ids[i]] = preference == PresenceStatus.Invisible ? PresenceStatus.Offline : preference;
        }

        return result;
    }

    public async Task SetActivityAsync(Guid userId, string? activityName, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        if (string.IsNullOrWhiteSpace(activityName))
            await db.KeyDeleteAsync(ActivityKey(userId));
        else
            await db.StringSetAsync(ActivityKey(userId), activityName);
    }

    public async Task<string?> GetActivityAsync(Guid userId, CancellationToken ct)
    {
        var activities = await GetActivitiesAsync([userId], ct);
        return activities.GetValueOrDefault(userId);
    }

    public async Task<IReadOnlyDictionary<Guid, string?>> GetActivitiesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        var db = _redis.GetDatabase();

        var localValues = await Task.WhenAll(ids.Select(id => db.StringGetAsync(ActivityKey(id))));
        var steamValues = await Task.WhenAll(ids.Select(id => db.StringGetAsync(SteamActivityKey(id))));

        var result = new Dictionary<Guid, string?>();
        for (var i = 0; i < ids.Count; i++)
        {
            // Steam-reported activity wins when present — it doesn't depend on the
            // Electron app being open, so it's the more reliable source when both
            // happen to be available at once.
            result[ids[i]] = !steamValues[i].IsNullOrEmpty
                ? steamValues[i].ToString()
                : (localValues[i].IsNullOrEmpty ? null : localValues[i].ToString());
        }

        return result;
    }

    public async Task SetSteamActivityAsync(Guid userId, string? activityName, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        if (string.IsNullOrWhiteSpace(activityName))
            await db.KeyDeleteAsync(SteamActivityKey(userId));
        else
            await db.StringSetAsync(SteamActivityKey(userId), activityName);
    }

    public async Task<IReadOnlyDictionary<Guid, string?>> GetSteamActivitiesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var ids = userIds.Distinct().ToList();
        var values = await Task.WhenAll(ids.Select(id => db.StringGetAsync(SteamActivityKey(id))));

        var result = new Dictionary<Guid, string?>();
        for (var i = 0; i < ids.Count; i++)
            result[ids[i]] = values[i].IsNullOrEmpty ? null : values[i].ToString();

        return result;
    }

    public async Task<IReadOnlyList<Guid>> GetOnlineUserIdsAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var members = await db.SetMembersAsync(OnlineUsersSetKey);
        var result = new List<Guid>(members.Length);
        foreach (var member in members)
        {
            if (Guid.TryParse(member.ToString(), out var userId))
                result.Add(userId);
        }
        return result;
    }
}
