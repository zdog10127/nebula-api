using DiscordClone.Application.Common;
using DiscordClone.Application.Presence;
using DiscordClone.Application.Social;
using DiscordClone.Domain.Entities;
using DiscordClone.Infrastructure.Persistence;
using MongoDB.Driver;

namespace DiscordClone.Infrastructure.Social;

public class SocialService : ISocialService
{
    private const int MaxHistoryLimit = 100;

    private readonly MongoContext _mongo;
    private readonly IPresenceService _presence;

    public SocialService(MongoContext mongo, IPresenceService presence)
    {
        _mongo = mongo;
        _presence = presence;
    }

    private static (Guid A, Guid B) Normalize(Guid x, Guid y) => x.CompareTo(y) <= 0 ? (x, y) : (y, x);

    public async Task<FriendRequestDto> SendFriendRequestAsync(Guid userId, string targetUsername, CancellationToken ct)
    {
        var target = await _mongo.Users.Find(u => u.Username == targetUsername.Trim()).SingleOrDefaultAsync(ct)
            ?? throw new AppException("User not found.", 404);

        if (target.Id == userId)
            throw new AppException("You cannot send a friend request to yourself.");

        var (a, b) = Normalize(userId, target.Id);
        var alreadyFriends = await _mongo.Friendships.Find(f => f.UserAId == a && f.UserBId == b).AnyAsync(ct);
        if (alreadyFriends)
            throw new AppException("You are already friends with this user.", 409);

        var reverseExists = await _mongo.FriendRequests
            .Find(r => r.FromUserId == target.Id && r.ToUserId == userId).AnyAsync(ct);
        if (reverseExists)
            throw new AppException("This user already sent you a friend request — accept it instead.", 409);

        var request = new FriendRequest
        {
            Id = Guid.NewGuid(),
            FromUserId = userId,
            ToUserId = target.Id,
            CreatedAt = DateTime.UtcNow,
        };

        try
        {
            await _mongo.FriendRequests.InsertOneAsync(request, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new AppException("Friend request already sent.", 409);
        }

        return new FriendRequestDto(request.Id, target.Id, target.Username, target.DisplayName, target.AvatarUrl, request.CreatedAt, IsIncoming: false);
    }

    public async Task<Guid> AcceptFriendRequestAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        var request = await _mongo.FriendRequests.Find(r => r.Id == requestId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Friend request not found.", 404);

        if (request.ToUserId != userId)
            throw new AppException("You cannot accept this friend request.", 403);

        var (a, b) = Normalize(request.FromUserId, request.ToUserId);

        using var session = await _mongo.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await _mongo.FriendRequests.DeleteOneAsync(s, r => r.Id == requestId, cancellationToken: token);
            try
            {
                await _mongo.Friendships.InsertOneAsync(
                    s, new Friendship { Id = Guid.NewGuid(), UserAId = a, UserBId = b, CreatedAt = DateTime.UtcNow }, cancellationToken: token);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Already friends somehow (race) — the request is still consumed, nothing else to do.
            }
            return true;
        }, cancellationToken: ct);

        return request.FromUserId;
    }

    public async Task<Guid> DeclineFriendRequestAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        var request = await _mongo.FriendRequests.Find(r => r.Id == requestId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Friend request not found.", 404);

        if (request.ToUserId != userId)
            throw new AppException("You cannot decline this friend request.", 403);

        await _mongo.FriendRequests.DeleteOneAsync(r => r.Id == requestId, ct);
        return request.FromUserId;
    }

    public async Task<Guid> CancelFriendRequestAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        var request = await _mongo.FriendRequests.Find(r => r.Id == requestId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Friend request not found.", 404);

        if (request.FromUserId != userId)
            throw new AppException("You cannot cancel this friend request.", 403);

        await _mongo.FriendRequests.DeleteOneAsync(r => r.Id == requestId, ct);
        return request.ToUserId;
    }

    public async Task RemoveFriendAsync(Guid userId, Guid friendUserId, CancellationToken ct)
    {
        var (a, b) = Normalize(userId, friendUserId);
        await _mongo.Friendships.DeleteOneAsync(f => f.UserAId == a && f.UserBId == b, ct);
    }

    public async Task<IReadOnlyList<FriendRequestDto>> GetFriendRequestsAsync(Guid userId, CancellationToken ct)
    {
        var requests = await _mongo.FriendRequests
            .Find(r => r.FromUserId == userId || r.ToUserId == userId)
            .ToListAsync(ct);

        var otherIds = requests.Select(r => r.FromUserId == userId ? r.ToUserId : r.FromUserId).Distinct().ToList();
        var others = await _mongo.Users.Find(u => otherIds.Contains(u.Id)).ToListAsync(ct);
        var othersById = others.ToDictionary(u => u.Id);

        return requests
            .Where(r => othersById.ContainsKey(r.FromUserId == userId ? r.ToUserId : r.FromUserId))
            .Select(r =>
            {
                var isIncoming = r.ToUserId == userId;
                var other = othersById[isIncoming ? r.FromUserId : r.ToUserId];
                return new FriendRequestDto(r.Id, other.Id, other.Username, other.DisplayName, other.AvatarUrl, r.CreatedAt, isIncoming);
            })
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<FriendDto>> GetFriendsAsync(Guid userId, CancellationToken ct)
    {
        var friendships = await _mongo.Friendships
            .Find(f => f.UserAId == userId || f.UserBId == userId)
            .ToListAsync(ct);

        var friendIds = friendships.Select(f => f.UserAId == userId ? f.UserBId : f.UserAId).ToList();
        var friends = await _mongo.Users.Find(u => friendIds.Contains(u.Id)).ToListAsync(ct);
        var friendsById = friends.ToDictionary(u => u.Id);
        var statuses = await _presence.GetEffectiveStatusesAsync(friendIds, ct);

        return friendships
            .Select(f =>
            {
                var friendId = f.UserAId == userId ? f.UserBId : f.UserAId;
                return friendsById.TryGetValue(friendId, out var friend)
                    ? new FriendDto(friend.Id, friend.Username, friend.DisplayName, friend.AvatarUrl, statuses.GetValueOrDefault(friend.Id), f.CreatedAt)
                    : null;
            })
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderBy(f => f.DisplayName)
            .ToList();
    }

    public async Task<DmChannelDto> GetOrCreateDmChannelAsync(Guid userId, Guid otherUserId, CancellationToken ct)
    {
        if (otherUserId == userId)
            throw new AppException("You cannot message yourself.");

        var (a, b) = Normalize(userId, otherUserId);
        var isFriend = await _mongo.Friendships.Find(f => f.UserAId == a && f.UserBId == b).AnyAsync(ct);
        if (!isFriend)
            throw new AppException("You can only message friends.", 403);

        var channel = await _mongo.DmChannels.Find(d => d.UserAId == a && d.UserBId == b).SingleOrDefaultAsync(ct);
        if (channel is null)
        {
            channel = new DmChannel { Id = Guid.NewGuid(), UserAId = a, UserBId = b, CreatedAt = DateTime.UtcNow };
            try
            {
                await _mongo.DmChannels.InsertOneAsync(channel, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                channel = await _mongo.DmChannels.Find(d => d.UserAId == a && d.UserBId == b).SingleAsync(ct);
            }
        }

        var other = await _mongo.Users.Find(u => u.Id == otherUserId).SingleAsync(ct);
        var status = await _presence.GetEffectiveStatusAsync(otherUserId, ct);
        return new DmChannelDto(channel.Id, other.Id, other.Username, other.DisplayName, other.AvatarUrl, status, null, channel.LastMessageAt);
    }

    public async Task<IReadOnlyList<DmChannelDto>> GetDmChannelsAsync(Guid userId, CancellationToken ct)
    {
        var channels = await _mongo.DmChannels.Find(d => d.UserAId == userId || d.UserBId == userId).ToListAsync(ct);
        if (channels.Count == 0)
            return [];

        var otherIds = channels.Select(c => c.UserAId == userId ? c.UserBId : c.UserAId).Distinct().ToList();
        var others = await _mongo.Users.Find(u => otherIds.Contains(u.Id)).ToListAsync(ct);
        var othersById = others.ToDictionary(u => u.Id);
        var statuses = await _presence.GetEffectiveStatusesAsync(otherIds, ct);

        var channelIds = channels.Select(c => c.Id).ToList();
        var lastMessages = await _mongo.DmMessages
            .Find(m => channelIds.Contains(m.DmChannelId) && m.DeletedAt == null)
            .SortByDescending(m => m.CreatedAt)
            .ToListAsync(ct);
        var lastMessageByChannel = lastMessages.GroupBy(m => m.DmChannelId).ToDictionary(g => g.Key, g => g.First());

        return channels
            .Where(c => othersById.ContainsKey(c.UserAId == userId ? c.UserBId : c.UserAId))
            .Select(c =>
            {
                var other = othersById[c.UserAId == userId ? c.UserBId : c.UserAId];
                var last = lastMessageByChannel.GetValueOrDefault(c.Id);
                return new DmChannelDto(
                    c.Id, other.Id, other.Username, other.DisplayName, other.AvatarUrl,
                    statuses.GetValueOrDefault(other.Id), last?.Content, c.LastMessageAt);
            })
            .OrderByDescending(c => c.LastMessageAt ?? DateTime.MinValue)
            .ToList();
    }

    public async Task EnsureDmChannelAccessAsync(Guid userId, Guid dmChannelId, CancellationToken ct)
    {
        var channel = await _mongo.DmChannels.Find(d => d.Id == dmChannelId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Conversation not found.", 404);

        if (channel.UserAId != userId && channel.UserBId != userId)
            throw new AppException("You do not have access to this conversation.", 403);
    }

    public async Task<(Guid UserAId, Guid UserBId)> GetDmParticipantsAsync(Guid dmChannelId, CancellationToken ct)
    {
        var channel = await _mongo.DmChannels.Find(d => d.Id == dmChannelId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Conversation not found.", 404);
        return (channel.UserAId, channel.UserBId);
    }

    public async Task<DmMessageDto> SendDmMessageAsync(Guid userId, Guid dmChannelId, string content, CancellationToken ct)
    {
        await EnsureDmChannelAccessAsync(userId, dmChannelId, ct);

        var trimmed = content.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 4000)
            throw new AppException("Message content must be between 1 and 4000 characters.");

        var message = new DmMessage
        {
            Id = Guid.NewGuid(),
            DmChannelId = dmChannelId,
            AuthorId = userId,
            Content = trimmed,
            CreatedAt = DateTime.UtcNow,
        };

        await _mongo.DmMessages.InsertOneAsync(message, cancellationToken: ct);
        await _mongo.DmChannels.UpdateOneAsync(
            d => d.Id == dmChannelId, Builders<DmChannel>.Update.Set(d => d.LastMessageAt, message.CreatedAt), cancellationToken: ct);

        return ToDto(message);
    }

    public async Task<IReadOnlyList<DmMessageDto>> GetDmHistoryAsync(Guid userId, Guid dmChannelId, DateTime? before, int limit, CancellationToken ct)
    {
        await EnsureDmChannelAccessAsync(userId, dmChannelId, ct);

        var take = Math.Clamp(limit, 1, MaxHistoryLimit);
        var filterBuilder = Builders<DmMessage>.Filter;
        var filter = filterBuilder.Eq(m => m.DmChannelId, dmChannelId) & filterBuilder.Eq(m => m.DeletedAt, null);
        if (before.HasValue)
            filter &= filterBuilder.Lt(m => m.CreatedAt, before.Value);

        var messages = await _mongo.DmMessages.Find(filter)
            .SortByDescending(m => m.CreatedAt)
            .Limit(take)
            .ToListAsync(ct);

        return messages.OrderBy(m => m.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<DmMessageDto> EditDmMessageAsync(Guid userId, Guid dmMessageId, string content, CancellationToken ct)
    {
        var message = await _mongo.DmMessages.Find(m => m.Id == dmMessageId && m.DeletedAt == null).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Message not found.", 404);

        if (message.AuthorId != userId)
            throw new AppException("You can only edit your own messages.", 403);

        var trimmed = content.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 4000)
            throw new AppException("Message content must be between 1 and 4000 characters.");

        var editedAt = DateTime.UtcNow;
        await _mongo.DmMessages.UpdateOneAsync(
            m => m.Id == dmMessageId,
            Builders<DmMessage>.Update.Set(m => m.Content, trimmed).Set(m => m.EditedAt, editedAt),
            cancellationToken: ct);

        message.Content = trimmed;
        message.EditedAt = editedAt;
        return ToDto(message);
    }

    public async Task<Guid> DeleteDmMessageAsync(Guid userId, Guid dmMessageId, CancellationToken ct)
    {
        var message = await _mongo.DmMessages.Find(m => m.Id == dmMessageId && m.DeletedAt == null).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Message not found.", 404);

        if (message.AuthorId != userId)
            throw new AppException("You can only delete your own messages.", 403);

        await _mongo.DmMessages.UpdateOneAsync(
            m => m.Id == dmMessageId, Builders<DmMessage>.Update.Set(m => m.DeletedAt, DateTime.UtcNow), cancellationToken: ct);

        return message.DmChannelId;
    }

    private static DmMessageDto ToDto(DmMessage m) => new(m.Id, m.DmChannelId, m.AuthorId, m.Content, m.CreatedAt, m.EditedAt);
}
