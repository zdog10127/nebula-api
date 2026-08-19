namespace DiscordClone.Application.Servers;

public interface IServerService
{
    Task<ServerSummary> CreateServerAsync(Guid userId, CreateServerRequest request, CancellationToken ct);
    Task<IReadOnlyList<ServerSummary>> GetMyServersAsync(Guid userId, CancellationToken ct);
    Task<ServerDetail> GetServerAsync(Guid userId, Guid serverId, CancellationToken ct);
    Task<ServerSummary> UpdateServerAsync(Guid userId, Guid serverId, UpdateServerRequest request, CancellationToken ct);
    Task<ServerSummary> UpdateServerIconAsync(Guid userId, Guid serverId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken ct);
    Task DeleteServerAsync(Guid userId, Guid serverId, CancellationToken ct);

    Task RemoveMemberAsync(Guid userId, Guid serverId, Guid targetUserId, CancellationToken ct);
    Task<BanDto> BanMemberAsync(Guid userId, Guid serverId, Guid targetUserId, BanUserRequest request, CancellationToken ct);
    Task UnbanMemberAsync(Guid userId, Guid serverId, Guid targetUserId, CancellationToken ct);
    Task<IReadOnlyList<BanDto>> GetBansAsync(Guid userId, Guid serverId, CancellationToken ct);

    Task<IReadOnlyList<RoleDto>> GetRolesAsync(Guid userId, Guid serverId, CancellationToken ct);
    Task<RoleDto> CreateRoleAsync(Guid userId, Guid serverId, CreateRoleRequest request, CancellationToken ct);
    Task<RoleDto> UpdateRoleAsync(Guid userId, Guid serverId, Guid roleId, UpdateRoleRequest request, CancellationToken ct);
    Task DeleteRoleAsync(Guid userId, Guid serverId, Guid roleId, CancellationToken ct);
    Task AssignRoleAsync(Guid userId, Guid serverId, Guid targetUserId, Guid roleId, CancellationToken ct);
    Task UnassignRoleAsync(Guid userId, Guid serverId, Guid targetUserId, Guid roleId, CancellationToken ct);
    Task<MyPermissionsDto> GetMyPermissionsAsync(Guid userId, Guid serverId, CancellationToken ct);

    Task<CategoryDto> CreateCategoryAsync(Guid userId, Guid serverId, CreateCategoryRequest request, CancellationToken ct);
    Task<CategoryDto> UpdateCategoryAsync(Guid userId, Guid serverId, Guid categoryId, UpdateCategoryRequest request, CancellationToken ct);
    Task DeleteCategoryAsync(Guid userId, Guid serverId, Guid categoryId, CancellationToken ct);

    Task<ChannelDto> CreateChannelAsync(Guid userId, Guid serverId, CreateChannelRequest request, CancellationToken ct);
    Task<ChannelDto> MoveChannelAsync(Guid userId, Guid serverId, Guid channelId, MoveChannelRequest request, CancellationToken ct);
    Task<IReadOnlyList<ChannelDto>> GetChannelsAsync(Guid userId, Guid serverId, CancellationToken ct);

    Task<IReadOnlyList<MemberDto>> GetMembersAsync(Guid userId, Guid serverId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetMyServerIdsAsync(Guid userId, CancellationToken ct);
    Task EnsureMembershipAsync(Guid userId, Guid serverId, CancellationToken ct);

    Task<InviteDto> CreateInviteAsync(Guid userId, Guid serverId, CreateInviteRequest request, CancellationToken ct);
    Task<ServerSummary> JoinByInviteAsync(Guid userId, string code, CancellationToken ct);
    Task<IReadOnlyList<InviteDto>> GetInvitesAsync(Guid userId, Guid serverId, CancellationToken ct);
    Task RevokeInviteAsync(Guid userId, Guid serverId, Guid inviteId, CancellationToken ct);

    Task<IReadOnlyList<CustomEmojiDto>> GetCustomEmojisAsync(Guid userId, Guid serverId, CancellationToken ct);
    Task<CustomEmojiDto> CreateCustomEmojiAsync(Guid userId, Guid serverId, string name, Stream content, string contentType, long sizeBytes, CancellationToken ct);
    Task DeleteCustomEmojiAsync(Guid userId, Guid serverId, Guid emojiId, CancellationToken ct);
}
