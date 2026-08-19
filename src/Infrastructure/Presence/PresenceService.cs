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

    private static string ConnectionsKey(Guid userId) => $"presence:{userId}";
    private static string StatusKey(Guid userId) => $"user_status:{userId}";

    public async Task<bool> ConnectAsync(Guid userId, string connectionId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = ConnectionsKey(userId);
        var wasOffline = !await db.KeyExistsAsync(key);
        await db.SetAddAsync(key, connectionId);
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
}
