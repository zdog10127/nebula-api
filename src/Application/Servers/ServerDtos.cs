using DiscordClone.Domain.Enums;

namespace DiscordClone.Application.Servers;

public record CreateServerRequest(string Name);

public record UpdateServerRequest(string? Name, string? Description);

public record ServerSummary(Guid Id, string Name, string? IconUrl, bool IsOwner, int MemberCount);

public record ServerDetail(
    Guid Id,
    string Name,
    string? Description,
    string? IconUrl,
    Guid OwnerId,
    DateTime CreatedAt,
    IReadOnlyList<ChannelDto> Channels,
    IReadOnlyList<CategoryDto> Categories);

public record CreateChannelRequest(string Name, ChannelType Type, Guid? CategoryId);

public record MoveChannelRequest(Guid? CategoryId, int Position);

public record ChannelDto(Guid Id, Guid ServerId, string Name, ChannelType Type, int Position, Guid? CategoryId);

public record CreateCategoryRequest(string Name);

public record UpdateCategoryRequest(string? Name, int? Position);

public record CategoryDto(Guid Id, Guid ServerId, string Name, int Position);

public record RoleDto(Guid Id, Guid ServerId, string Name, string Color, IReadOnlyList<ServerPermission> Permissions, int Position);

public record CreateRoleRequest(string Name, string Color, IReadOnlyList<ServerPermission> Permissions);

public record UpdateRoleRequest(string? Name, string? Color, IReadOnlyList<ServerPermission>? Permissions, int? Position);

public record AssignRoleRequest(Guid RoleId);

public record MemberDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Nickname,
    string? AvatarUrl,
    IReadOnlyList<Guid> RoleIds,
    bool IsOwner,
    DateTime JoinedAt,
    PresenceStatus Status,
    string? CustomStatusText,
    string? CustomStatusEmoji);

public record MyPermissionsDto(bool IsOwner, IReadOnlyList<ServerPermission> Permissions);

public record CreateInviteRequest(int? MaxUses, int? ExpiresInHours);

public record InviteDto(Guid Id, string Code, DateTime CreatedAt, DateTime? ExpiresAt, int? MaxUses, int Uses, bool IsValid);

public record BanUserRequest(string? Reason);

public record BanDto(Guid UserId, string Username, string DisplayName, string? AvatarUrl, Guid BannedByUserId, string? Reason, DateTime CreatedAt);

public record CustomEmojiDto(Guid Id, Guid ServerId, string Name, string ImageUrl, DateTime CreatedAt);
