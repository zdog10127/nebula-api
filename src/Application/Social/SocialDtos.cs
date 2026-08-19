using DiscordClone.Domain.Enums;

namespace DiscordClone.Application.Social;

public record FriendDto(Guid UserId, string Username, string DisplayName, string? AvatarUrl, PresenceStatus Status, DateTime FriendsSince);

public record FriendRequestDto(Guid Id, Guid UserId, string Username, string DisplayName, string? AvatarUrl, DateTime CreatedAt, bool IsIncoming);

public record DmChannelDto(
    Guid Id,
    Guid OtherUserId,
    string OtherUsername,
    string OtherDisplayName,
    string? OtherAvatarUrl,
    PresenceStatus OtherStatus,
    string? LastMessageContent,
    DateTime? LastMessageAt);

public record DmMessageDto(Guid Id, Guid DmChannelId, Guid AuthorId, string Content, DateTime CreatedAt, DateTime? EditedAt);

public record SendFriendRequestRequest(string Username);

public record SendDmMessageRequest(string Content);

public record EditDmMessageRequest(string Content);
