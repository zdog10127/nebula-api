namespace DiscordClone.Application.Messages;

public interface IMessageService
{
    Task<MessageDto> SendMessageAsync(Guid userId, Guid channelId, string content, IReadOnlyList<Guid>? attachmentIds, CancellationToken ct);
    Task<IReadOnlyList<MessageDto>> GetHistoryAsync(Guid userId, Guid channelId, DateTime? before, int limit, CancellationToken ct);
    Task<MessageDto> EditMessageAsync(Guid userId, Guid messageId, string content, CancellationToken ct);
    Task<Guid> DeleteMessageAsync(Guid userId, Guid messageId, CancellationToken ct);
    Task EnsureChannelAccessAsync(Guid userId, Guid channelId, CancellationToken ct);
    Task<Guid> GetServerIdForChannelAsync(Guid channelId, CancellationToken ct);

    Task<(Guid ChannelId, IReadOnlyList<ReactionSummary> Reactions)> AddReactionAsync(Guid userId, Guid messageId, string emoji, CancellationToken ct);
    Task<(Guid ChannelId, IReadOnlyList<ReactionSummary> Reactions)> RemoveReactionAsync(Guid userId, Guid messageId, string emoji, CancellationToken ct);

    Task MarkChannelReadAsync(Guid userId, Guid channelId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, UnreadCountDto>> GetUnreadCountsAsync(Guid userId, Guid serverId, CancellationToken ct);

    Task<MessageDto> PinMessageAsync(Guid userId, Guid messageId, CancellationToken ct);
    Task<MessageDto> UnpinMessageAsync(Guid userId, Guid messageId, CancellationToken ct);
    Task<IReadOnlyList<MessageDto>> GetPinnedMessagesAsync(Guid userId, Guid channelId, CancellationToken ct);
    Task<IReadOnlyList<MessageDto>> SearchMessagesAsync(Guid userId, Guid channelId, string query, CancellationToken ct);
}
