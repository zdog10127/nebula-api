using System.Text.RegularExpressions;
using DiscordClone.Application.Common;
using DiscordClone.Application.Messages;
using DiscordClone.Application.Storage;
using DiscordClone.Domain.Entities;
using DiscordClone.Domain.Enums;
using DiscordClone.Infrastructure.Persistence;
using MongoDB.Driver;

namespace DiscordClone.Infrastructure.Messages;

public partial class MessageService : IMessageService
{
    private const int MaxHistoryLimit = 100;

    [GeneratedRegex(@"@(\w+)")]
    private static partial Regex MentionPattern();

    private readonly MongoContext _mongo;
    private readonly IStorageService _storage;

    public MessageService(MongoContext mongo, IStorageService storage)
    {
        _mongo = mongo;
        _storage = storage;
    }

    public async Task EnsureChannelAccessAsync(Guid userId, Guid channelId, CancellationToken ct)
    {
        var channel = await _mongo.Channels.Find(c => c.Id == channelId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Channel not found.", 404);

        var isMember = await _mongo.ServerMembers
            .Find(m => m.ServerId == channel.ServerId && m.UserId == userId)
            .AnyAsync(ct);

        if (!isMember)
            throw new AppException("You are not a member of this server.", 403);
    }

    public async Task<Guid> GetServerIdForChannelAsync(Guid channelId, CancellationToken ct)
    {
        var channel = await _mongo.Channels.Find(c => c.Id == channelId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Channel not found.", 404);
        return channel.ServerId;
    }

    public async Task<MessageDto> SendMessageAsync(Guid userId, Guid channelId, string content, IReadOnlyList<Guid>? attachmentIds, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(userId, channelId, ct);

        var trimmed = content.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 4000)
            throw new AppException("Message content must be between 1 and 4000 characters.");

        var channel = await _mongo.Channels.Find(c => c.Id == channelId).SingleAsync(ct);
        var mentionedUserIds = await ResolveMentionsAsync(trimmed, channel.ServerId, ct);

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            AuthorId = userId,
            Content = trimmed,
            CreatedAt = DateTime.UtcNow,
            MentionedUserIds = mentionedUserIds,
        };

        var attachments = new List<Attachment>();
        if (attachmentIds is { Count: > 0 })
        {
            attachments = await _mongo.Attachments.Find(a => attachmentIds.Contains(a.Id)).ToListAsync(ct);

            foreach (var attachment in attachments)
            {
                if (attachment.UploaderId != userId)
                    throw new AppException("You can only attach files you uploaded yourself.", 403);
                if (attachment.MessageId is not null)
                    throw new AppException("Attachment is already linked to another message.", 409);

                attachment.MessageId = message.Id;
            }
        }

        using var session = await _mongo.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await _mongo.Messages.InsertOneAsync(s, message, cancellationToken: token);

            foreach (var attachment in attachments)
            {
                await _mongo.Attachments.UpdateOneAsync(
                    s,
                    a => a.Id == attachment.Id,
                    Builders<Attachment>.Update.Set(a => a.MessageId, message.Id),
                    cancellationToken: token);
            }

            return true;
        }, cancellationToken: ct);

        var author = await _mongo.Users.Find(u => u.Id == userId).SingleAsync(ct);
        return ToDto(message, author, attachments, []);
    }

    public async Task<IReadOnlyList<MessageDto>> GetHistoryAsync(Guid userId, Guid channelId, DateTime? before, int limit, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(userId, channelId, ct);

        var take = Math.Clamp(limit, 1, MaxHistoryLimit);

        var filterBuilder = Builders<Message>.Filter;
        var filter = filterBuilder.Eq(m => m.ChannelId, channelId) & filterBuilder.Eq(m => m.DeletedAt, null);
        if (before.HasValue)
            filter &= filterBuilder.Lt(m => m.CreatedAt, before.Value);

        var messages = await _mongo.Messages.Find(filter)
            .SortByDescending(m => m.CreatedAt)
            .Limit(take)
            .ToListAsync(ct);

        var resolved = await ResolveMessagesAsync(messages, ct);
        return resolved.OrderBy(m => m.CreatedAt).ToList();
    }

    public async Task<MessageDto> PinMessageAsync(Guid userId, Guid messageId, CancellationToken ct)
    {
        var message = await _mongo.Messages.Find(m => m.Id == messageId && m.DeletedAt == null).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Message not found.", 404);

        var channel = await _mongo.Channels.Find(c => c.Id == message.ChannelId).SingleAsync(ct);
        if (!await HasManageMessagesPermissionAsync(userId, channel.ServerId, ct))
            throw new AppException("You do not have permission to pin messages.", 403);

        await _mongo.Messages.UpdateOneAsync(m => m.Id == messageId, Builders<Message>.Update.Set(m => m.IsPinned, true), cancellationToken: ct);
        message.IsPinned = true;

        var author = await _mongo.Users.Find(u => u.Id == message.AuthorId).SingleAsync(ct);
        var attachments = await _mongo.Attachments.Find(a => a.MessageId == message.Id).ToListAsync(ct);
        var reactions = await GetReactionSummariesAsync(messageId, ct);
        return ToDto(message, author, attachments, reactions);
    }

    public async Task<MessageDto> UnpinMessageAsync(Guid userId, Guid messageId, CancellationToken ct)
    {
        var message = await _mongo.Messages.Find(m => m.Id == messageId && m.DeletedAt == null).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Message not found.", 404);

        var channel = await _mongo.Channels.Find(c => c.Id == message.ChannelId).SingleAsync(ct);
        if (!await HasManageMessagesPermissionAsync(userId, channel.ServerId, ct))
            throw new AppException("You do not have permission to unpin messages.", 403);

        await _mongo.Messages.UpdateOneAsync(m => m.Id == messageId, Builders<Message>.Update.Set(m => m.IsPinned, false), cancellationToken: ct);
        message.IsPinned = false;

        var author = await _mongo.Users.Find(u => u.Id == message.AuthorId).SingleAsync(ct);
        var attachments = await _mongo.Attachments.Find(a => a.MessageId == message.Id).ToListAsync(ct);
        var reactions = await GetReactionSummariesAsync(messageId, ct);
        return ToDto(message, author, attachments, reactions);
    }

    public async Task<IReadOnlyList<MessageDto>> GetPinnedMessagesAsync(Guid userId, Guid channelId, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(userId, channelId, ct);

        var messages = await _mongo.Messages
            .Find(m => m.ChannelId == channelId && m.IsPinned && m.DeletedAt == null)
            .ToListAsync(ct);

        var resolved = await ResolveMessagesAsync(messages, ct);
        return resolved.OrderByDescending(m => m.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<MessageDto>> SearchMessagesAsync(Guid userId, Guid channelId, string query, CancellationToken ct)
    {
        await EnsureChannelAccessAsync(userId, channelId, ct);

        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return [];

        var filter = Builders<Message>.Filter.Eq(m => m.ChannelId, channelId)
            & Builders<Message>.Filter.Eq(m => m.DeletedAt, null)
            & Builders<Message>.Filter.Regex(m => m.Content, new MongoDB.Bson.BsonRegularExpression(Regex.Escape(trimmed), "i"));

        var messages = await _mongo.Messages.Find(filter).SortByDescending(m => m.CreatedAt).Limit(50).ToListAsync(ct);

        var resolved = await ResolveMessagesAsync(messages, ct);
        return resolved.OrderByDescending(m => m.CreatedAt).ToList();
    }

    private async Task<List<MessageDto>> ResolveMessagesAsync(List<Message> messages, CancellationToken ct)
    {
        if (messages.Count == 0)
            return [];

        var authorIds = messages.Select(m => m.AuthorId).Distinct().ToList();
        var authors = await _mongo.Users.Find(u => authorIds.Contains(u.Id)).ToListAsync(ct);
        var authorsById = authors.ToDictionary(u => u.Id);

        var messageIds = messages.Select(m => m.Id).ToList();
        var attachmentsByMessage = await _mongo.Attachments
            .Find(a => a.MessageId != null && messageIds.Contains(a.MessageId!.Value))
            .ToListAsync(ct);

        var reactionsByMessage = await GetReactionsByMessageAsync(messageIds, ct);

        return messages
            .Where(m => authorsById.ContainsKey(m.AuthorId))
            .Select(m => ToDto(
                m,
                authorsById[m.AuthorId],
                attachmentsByMessage.Where(a => a.MessageId == m.Id),
                reactionsByMessage.GetValueOrDefault(m.Id, [])))
            .ToList();
    }

    public async Task<MessageDto> EditMessageAsync(Guid userId, Guid messageId, string content, CancellationToken ct)
    {
        var message = await _mongo.Messages.Find(m => m.Id == messageId && m.DeletedAt == null).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Message not found.", 404);

        if (message.AuthorId != userId)
            throw new AppException("You can only edit your own messages.", 403);

        var trimmed = content.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 4000)
            throw new AppException("Message content must be between 1 and 4000 characters.");

        var editedAt = DateTime.UtcNow;
        var update = Builders<Message>.Update.Set(m => m.Content, trimmed).Set(m => m.EditedAt, editedAt);
        await _mongo.Messages.UpdateOneAsync(m => m.Id == messageId, update, cancellationToken: ct);

        message.Content = trimmed;
        message.EditedAt = editedAt;

        var author = await _mongo.Users.Find(u => u.Id == message.AuthorId).SingleAsync(ct);
        var attachments = await _mongo.Attachments.Find(a => a.MessageId == message.Id).ToListAsync(ct);
        var reactions = await GetReactionSummariesAsync(messageId, ct);
        return ToDto(message, author, attachments, reactions);
    }

    public async Task<Guid> DeleteMessageAsync(Guid userId, Guid messageId, CancellationToken ct)
    {
        var message = await _mongo.Messages.Find(m => m.Id == messageId && m.DeletedAt == null).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Message not found.", 404);

        if (message.AuthorId != userId)
        {
            var channel = await _mongo.Channels.Find(c => c.Id == message.ChannelId).SingleOrDefaultAsync(ct)
                ?? throw new AppException("Channel not found.", 404);

            var canManage = await HasManageMessagesPermissionAsync(userId, channel.ServerId, ct);
            if (!canManage)
                throw new AppException("You do not have permission to delete this message.", 403);
        }

        var update = Builders<Message>.Update.Set(m => m.DeletedAt, DateTime.UtcNow);
        await _mongo.Messages.UpdateOneAsync(m => m.Id == messageId, update, cancellationToken: ct);

        return message.ChannelId;
    }

    public async Task<(Guid ChannelId, IReadOnlyList<ReactionSummary> Reactions)> AddReactionAsync(Guid userId, Guid messageId, string emoji, CancellationToken ct)
    {
        var message = await _mongo.Messages.Find(m => m.Id == messageId && m.DeletedAt == null).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Message not found.", 404);

        await EnsureChannelAccessAsync(userId, message.ChannelId, ct);

        var trimmedEmoji = emoji.Trim();
        if (string.IsNullOrWhiteSpace(trimmedEmoji) || trimmedEmoji.Length > 16)
            throw new AppException("Invalid emoji.");

        var exists = await _mongo.MessageReactions
            .Find(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == trimmedEmoji)
            .AnyAsync(ct);

        if (!exists)
        {
            var reaction = new MessageReaction
            {
                Id = Guid.NewGuid(),
                MessageId = messageId,
                UserId = userId,
                Emoji = trimmedEmoji,
                CreatedAt = DateTime.UtcNow,
            };

            try
            {
                await _mongo.MessageReactions.InsertOneAsync(reaction, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Another concurrent request already added the same reaction; ignore.
            }
        }

        return (message.ChannelId, await GetReactionSummariesAsync(messageId, ct));
    }

    public async Task<(Guid ChannelId, IReadOnlyList<ReactionSummary> Reactions)> RemoveReactionAsync(Guid userId, Guid messageId, string emoji, CancellationToken ct)
    {
        var message = await _mongo.Messages.Find(m => m.Id == messageId && m.DeletedAt == null).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Message not found.", 404);

        await EnsureChannelAccessAsync(userId, message.ChannelId, ct);

        await _mongo.MessageReactions.DeleteOneAsync(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == emoji, ct);

        return (message.ChannelId, await GetReactionSummariesAsync(messageId, ct));
    }

    private async Task<IReadOnlyList<ReactionSummary>> GetReactionSummariesAsync(Guid messageId, CancellationToken ct)
    {
        var reactions = await _mongo.MessageReactions.Find(r => r.MessageId == messageId).ToListAsync(ct);
        return reactions
            .GroupBy(r => r.Emoji)
            .Select(g => new ReactionSummary(g.Key, g.Select(r => r.UserId).ToList()))
            .OrderBy(r => r.Emoji)
            .ToList();
    }

    private async Task<Dictionary<Guid, IReadOnlyList<ReactionSummary>>> GetReactionsByMessageAsync(IReadOnlyList<Guid> messageIds, CancellationToken ct)
    {
        if (messageIds.Count == 0)
            return [];

        var reactions = await _mongo.MessageReactions.Find(r => messageIds.Contains(r.MessageId)).ToListAsync(ct);

        return reactions
            .GroupBy(r => r.MessageId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ReactionSummary>)g
                    .GroupBy(r => r.Emoji)
                    .Select(eg => new ReactionSummary(eg.Key, eg.Select(r => r.UserId).ToList()))
                    .OrderBy(r => r.Emoji)
                    .ToList());
    }

    private async Task<bool> HasManageMessagesPermissionAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        var server = await _mongo.Servers.Find(s => s.Id == serverId).SingleOrDefaultAsync(ct);
        if (server is null)
            return false;
        if (server.OwnerId == userId)
            return true;

        var membership = await _mongo.ServerMembers.Find(m => m.ServerId == serverId && m.UserId == userId).SingleOrDefaultAsync(ct);
        if (membership is null || membership.RoleIds.Count == 0)
            return false;

        var roles = await _mongo.Roles.Find(r => membership.RoleIds.Contains(r.Id)).ToListAsync(ct);
        var effective = roles.Aggregate(ServerPermission.None, (acc, r) => acc | r.Permissions);
        return effective.HasFlag(ServerPermission.ManageMessages);
    }

    private async Task<List<Guid>> ResolveMentionsAsync(string content, Guid serverId, CancellationToken ct)
    {
        var candidateUsernames = MentionPattern().Matches(content).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (candidateUsernames.Count == 0)
            return [];

        var candidateUsers = await _mongo.Users.Find(u => candidateUsernames.Contains(u.Username)).ToListAsync(ct);
        if (candidateUsers.Count == 0)
            return [];

        var candidateUserIds = candidateUsers.Select(u => u.Id).ToList();
        var memberIds = await _mongo.ServerMembers
            .Find(m => m.ServerId == serverId && candidateUserIds.Contains(m.UserId))
            .Project(m => m.UserId)
            .ToListAsync(ct);

        return memberIds;
    }

    public async Task MarkChannelReadAsync(Guid userId, Guid channelId, CancellationToken ct)
    {
        await _mongo.ChannelReadStates.UpdateOneAsync(
            s => s.UserId == userId && s.ChannelId == channelId,
            Builders<ChannelReadState>.Update
                .Set(s => s.LastReadAt, DateTime.UtcNow)
                .SetOnInsert(s => s.Id, Guid.NewGuid())
                .SetOnInsert(s => s.UserId, userId)
                .SetOnInsert(s => s.ChannelId, channelId),
            new UpdateOptions { IsUpsert = true },
            ct);
    }

    public async Task<IReadOnlyDictionary<Guid, UnreadCountDto>> GetUnreadCountsAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        var channels = await _mongo.Channels.Find(c => c.ServerId == serverId && c.Type == ChannelType.Text).ToListAsync(ct);
        if (channels.Count == 0)
            return new Dictionary<Guid, UnreadCountDto>();

        var member = await _mongo.ServerMembers.Find(m => m.ServerId == serverId && m.UserId == userId).SingleOrDefaultAsync(ct);
        var joinedAt = member?.JoinedAt ?? DateTime.MinValue;

        var channelIds = channels.Select(c => c.Id).ToList();
        var readStates = await _mongo.ChannelReadStates.Find(s => s.UserId == userId && channelIds.Contains(s.ChannelId)).ToListAsync(ct);
        var readStatesByChannel = readStates.ToDictionary(s => s.ChannelId, s => s.LastReadAt);

        var result = new Dictionary<Guid, UnreadCountDto>();
        foreach (var channel in channels)
        {
            var floor = readStatesByChannel.TryGetValue(channel.Id, out var lastReadAt) ? lastReadAt : joinedAt;

            var filterBuilder = Builders<Message>.Filter;
            var filter = filterBuilder.Eq(m => m.ChannelId, channel.Id)
                & filterBuilder.Eq(m => m.DeletedAt, null)
                & filterBuilder.Gt(m => m.CreatedAt, floor)
                & filterBuilder.Ne(m => m.AuthorId, userId);

            var count = (int)await _mongo.Messages.CountDocumentsAsync(filter, cancellationToken: ct);
            var hasMention = count > 0 && await _mongo.Messages.Find(filter & filterBuilder.AnyEq(m => m.MentionedUserIds, userId)).AnyAsync(ct);

            result[channel.Id] = new UnreadCountDto(count, hasMention);
        }

        return result;
    }

    private MessageDto ToDto(Message message, User author, IEnumerable<Attachment> attachments, IReadOnlyList<ReactionSummary> reactions) => new(
        message.Id,
        message.ChannelId,
        message.AuthorId,
        author.Username,
        author.DisplayName,
        author.AvatarUrl,
        message.Content,
        message.CreatedAt,
        message.EditedAt,
        attachments.Select(a => new AttachmentSummary(a.Id, a.FileName, a.ContentType, a.SizeBytes, _storage.GetPublicUrl(a.StorageKey))).ToList(),
        reactions,
        message.MentionedUserIds,
        message.IsPinned);
}
