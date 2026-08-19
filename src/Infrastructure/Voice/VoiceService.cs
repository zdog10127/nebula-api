using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using DiscordClone.Application.Common;
using DiscordClone.Application.Presence;
using DiscordClone.Application.Voice;
using DiscordClone.Domain.Enums;
using DiscordClone.Infrastructure.Persistence;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using StackExchange.Redis;

namespace DiscordClone.Infrastructure.Voice;

public class VoiceService : IVoiceService
{
    private readonly MongoContext _mongo;
    private readonly LiveKitOptions _options;
    private readonly IVoicePresenceService _voicePresence;
    private readonly IConnectionMultiplexer _redis;

    public VoiceService(MongoContext mongo, LiveKitOptions options, IVoicePresenceService voicePresence, IConnectionMultiplexer redis)
    {
        _mongo = mongo;
        _options = options;
        _voicePresence = voicePresence;
        _redis = redis;
    }

    public async Task<VoiceTokenResult> GetJoinTokenAsync(Guid userId, Guid channelId, CancellationToken ct)
    {
        var channel = await _mongo.Channels.Find(c => c.Id == channelId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Channel not found.", 404);

        if (channel.Type != ChannelType.Voice)
            throw new AppException("This channel is not a voice channel.", 400);

        var isMember = await _mongo.ServerMembers
            .Find(m => m.ServerId == channel.ServerId && m.UserId == userId)
            .AnyAsync(ct);

        if (!isMember)
            throw new AppException("You are not a member of this server.", 403);

        var user = await _mongo.Users.Find(u => u.Id == userId).SingleAsync(ct);
        var roomName = channelId.ToString();
        var identity = userId.ToString();

        var metadata = JsonSerializer.Serialize(new { avatarUrl = user.AvatarUrl, deafened = false });
        var token = GenerateAccessToken(identity, user.DisplayName, roomName, metadata);

        return new VoiceTokenResult(_options.Url, token, roomName, identity);
    }

    public async Task<IReadOnlyList<VoiceParticipantDto>> ResolveParticipantsAsync(IEnumerable<VoicePresenceEntry> entries, CancellationToken ct)
    {
        var deduped = entries.GroupBy(e => e.UserId).Select(g => g.Last()).ToList();
        var userIds = deduped.Select(e => e.UserId).ToList();
        if (userIds.Count == 0)
            return [];

        var users = await _mongo.Users.Find(u => userIds.Contains(u.Id)).ToListAsync(ct);
        var usersById = users.ToDictionary(u => u.Id);

        return deduped
            .Where(e => usersById.ContainsKey(e.UserId))
            .Select(e =>
            {
                var u = usersById[e.UserId];
                return new VoiceParticipantDto(u.Id, u.Username, u.DisplayName, u.AvatarUrl, e.IsMuted, e.IsDeafened);
            })
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<VoiceParticipantDto>>> GetServerVoiceParticipantsAsync(Guid serverId, CancellationToken ct)
    {
        var voiceChannels = await _mongo.Channels
            .Find(c => c.ServerId == serverId && c.Type == ChannelType.Voice)
            .ToListAsync(ct);

        var channelIds = voiceChannels.Select(c => c.Id).ToList();
        var entriesByChannel = await _voicePresence.GetParticipantsForChannelsAsync(channelIds, ct);

        var allUserIds = entriesByChannel.Values.SelectMany(v => v).Select(e => e.UserId).Distinct().ToList();
        var users = await _mongo.Users.Find(u => allUserIds.Contains(u.Id)).ToListAsync(ct);
        var usersById = users.ToDictionary(u => u.Id);

        var result = new Dictionary<Guid, IReadOnlyList<VoiceParticipantDto>>();
        foreach (var channelId in channelIds)
        {
            var entries = entriesByChannel.GetValueOrDefault(channelId, []);
            result[channelId] = entries
                .Where(e => usersById.ContainsKey(e.UserId))
                .Select(e =>
                {
                    var u = usersById[e.UserId];
                    return new VoiceParticipantDto(u.Id, u.Username, u.DisplayName, u.AvatarUrl, e.IsMuted, e.IsDeafened);
                })
                .ToList();
        }

        return result;
    }

    public async Task<Guid> GetServerIdForChannelAsync(Guid channelId, CancellationToken ct)
    {
        var channel = await _mongo.Channels.Find(c => c.Id == channelId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Channel not found.", 404);

        return channel.ServerId;
    }

    public async Task<NowPlayingDto> ShareNowPlayingAsync(Guid userId, Guid channelId, ShareNowPlayingRequest request, CancellationToken ct)
    {
        var channel = await _mongo.Channels.Find(c => c.Id == channelId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Channel not found.", 404);

        if (channel.Type != ChannelType.Voice)
            throw new AppException("This channel is not a voice channel.", 400);

        var type = request.Type.Trim().ToLowerInvariant();
        if (type is not ("youtube" or "audio"))
            throw new AppException("Type must be 'youtube' or 'audio'.");

        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out _))
            throw new AppException("A valid URL is required.");

        var user = await _mongo.Users.Find(u => u.Id == userId).SingleAsync(ct);
        var startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();

        var dto = new NowPlayingDto(type, request.Url.Trim(), title, userId, user.DisplayName, startedAt);

        var db = _redis.GetDatabase();
        await db.StringSetAsync(NowPlayingKey(channelId), JsonSerializer.Serialize(dto), TimeSpan.FromHours(6));

        return dto;
    }

    public async Task StopNowPlayingAsync(Guid userId, Guid channelId, CancellationToken ct)
    {
        var current = await GetNowPlayingAsync(channelId, ct);
        if (current is null)
            return;

        if (current.SharedByUserId != userId)
            throw new AppException("Only the person who shared it can stop it.", 403);

        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(NowPlayingKey(channelId));
    }

    public async Task<NowPlayingDto?> GetNowPlayingAsync(Guid channelId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var raw = await db.StringGetAsync(NowPlayingKey(channelId));
        return raw.IsNullOrEmpty ? null : JsonSerializer.Deserialize<NowPlayingDto>(raw.ToString()!);
    }

    private static string NowPlayingKey(Guid channelId) => $"now_playing:{channelId}";

    private string GenerateAccessToken(string identity, string displayName, string roomName, string metadata)
    {
        var now = DateTimeOffset.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.ApiSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var payload = new JwtPayload
        {
            { "iss", _options.ApiKey },
            { "sub", identity },
            { "name", displayName },
            { "metadata", metadata },
            { "iat", now.ToUnixTimeSeconds() },
            { "nbf", now.ToUnixTimeSeconds() },
            { "exp", now.AddHours(6).ToUnixTimeSeconds() },
            {
                "video", new Dictionary<string, object>
                {
                    { "room", roomName },
                    { "roomJoin", true },
                    { "canPublish", true },
                    { "canSubscribe", true },
                    { "canPublishData", true },
                    { "canUpdateOwnMetadata", true },
                }
            },
        };

        var header = new JwtHeader(credentials);
        var token = new JwtSecurityToken(header, payload);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
