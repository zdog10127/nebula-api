using DiscordClone.Application.Presence;
using StackExchange.Redis;

namespace DiscordClone.Infrastructure.Presence;

public class VoicePresenceService : IVoicePresenceService
{
    private readonly IConnectionMultiplexer _redis;

    public VoicePresenceService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    private static string ChannelKey(Guid channelId) => $"voice_channel:{channelId}";
    private static string ConnectionKey(string connectionId) => $"voice_conn:{connectionId}";

    private static string Encode(Guid userId, bool isMuted, bool isDeafened) =>
        $"{userId}|{(isMuted ? 1 : 0)}|{(isDeafened ? 1 : 0)}";

    private static VoicePresenceEntry? Decode(string raw)
    {
        var parts = raw.Split('|');
        if (parts.Length != 3 || !Guid.TryParse(parts[0], out var userId))
            return null;

        return new VoicePresenceEntry(userId, parts[1] == "1", parts[2] == "1");
    }

    public async Task<IReadOnlyList<VoicePresenceEntry>> JoinAsync(Guid channelId, string connectionId, Guid userId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        await db.HashSetAsync(ChannelKey(channelId), connectionId, Encode(userId, false, false));
        await db.StringSetAsync(ConnectionKey(connectionId), channelId.ToString());

        return await GetParticipantEntriesAsync(channelId, ct);
    }

    public async Task<(Guid ChannelId, IReadOnlyList<VoicePresenceEntry> Entries)?> LeaveAsync(string connectionId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        var channelIdValue = await db.StringGetAsync(ConnectionKey(connectionId));
        if (channelIdValue.IsNullOrEmpty || !Guid.TryParse(channelIdValue.ToString(), out var channelId))
            return null;

        await db.HashDeleteAsync(ChannelKey(channelId), connectionId);
        await db.KeyDeleteAsync(ConnectionKey(connectionId));

        var remaining = await GetParticipantEntriesAsync(channelId, ct);
        return (channelId, remaining);
    }

    public async Task<(Guid ChannelId, IReadOnlyList<VoicePresenceEntry> Entries)?> UpdateStateAsync(string connectionId, bool isMuted, bool isDeafened, CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        var channelIdValue = await db.StringGetAsync(ConnectionKey(connectionId));
        if (channelIdValue.IsNullOrEmpty || !Guid.TryParse(channelIdValue.ToString(), out var channelId))
            return null;

        var existing = await db.HashGetAsync(ChannelKey(channelId), connectionId);
        if (existing.IsNullOrEmpty)
            return null;

        var decoded = Decode(existing.ToString());
        if (decoded is null)
            return null;

        await db.HashSetAsync(ChannelKey(channelId), connectionId, Encode(decoded.UserId, isMuted, isDeafened));

        var entries = await GetParticipantEntriesAsync(channelId, ct);
        return (channelId, entries);
    }

    public async Task<IReadOnlyList<VoicePresenceEntry>> GetParticipantEntriesAsync(Guid channelId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var values = await db.HashValuesAsync(ChannelKey(channelId));

        return values
            .Select(v => Decode(v.ToString()))
            .Where(e => e is not null)
            .Select(e => e!)
            .GroupBy(e => e.UserId)
            .Select(g => g.Last())
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<VoicePresenceEntry>>> GetParticipantsForChannelsAsync(IEnumerable<Guid> channelIds, CancellationToken ct)
    {
        var ids = channelIds.Distinct().ToList();
        var results = await Task.WhenAll(ids.Select(id => GetParticipantEntriesAsync(id, ct)));

        var dict = new Dictionary<Guid, IReadOnlyList<VoicePresenceEntry>>();
        for (var i = 0; i < ids.Count; i++)
            dict[ids[i]] = results[i];

        return dict;
    }
}
