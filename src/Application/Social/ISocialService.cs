namespace DiscordClone.Application.Social;

public interface ISocialService
{
    Task<FriendRequestDto> SendFriendRequestAsync(Guid userId, string targetUsername, CancellationToken ct);
    Task<Guid> AcceptFriendRequestAsync(Guid userId, Guid requestId, CancellationToken ct);
    Task<Guid> DeclineFriendRequestAsync(Guid userId, Guid requestId, CancellationToken ct);
    Task<Guid> CancelFriendRequestAsync(Guid userId, Guid requestId, CancellationToken ct);
    Task RemoveFriendAsync(Guid userId, Guid friendUserId, CancellationToken ct);
    Task<IReadOnlyList<FriendRequestDto>> GetFriendRequestsAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<FriendDto>> GetFriendsAsync(Guid userId, CancellationToken ct);

    Task<DmChannelDto> GetOrCreateDmChannelAsync(Guid userId, Guid otherUserId, CancellationToken ct);
    Task<IReadOnlyList<DmChannelDto>> GetDmChannelsAsync(Guid userId, CancellationToken ct);
    Task EnsureDmChannelAccessAsync(Guid userId, Guid dmChannelId, CancellationToken ct);
    Task<(Guid UserAId, Guid UserBId)> GetDmParticipantsAsync(Guid dmChannelId, CancellationToken ct);

    Task<DmMessageDto> SendDmMessageAsync(Guid userId, Guid dmChannelId, string content, CancellationToken ct);
    Task<IReadOnlyList<DmMessageDto>> GetDmHistoryAsync(Guid userId, Guid dmChannelId, DateTime? before, int limit, CancellationToken ct);
    Task<DmMessageDto> EditDmMessageAsync(Guid userId, Guid dmMessageId, string content, CancellationToken ct);
    Task<Guid> DeleteDmMessageAsync(Guid userId, Guid dmMessageId, CancellationToken ct);
}
