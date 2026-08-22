using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DiscordClone.Application.Common;
using DiscordClone.Application.Presence;
using DiscordClone.Application.Servers;
using DiscordClone.Application.Storage;
using DiscordClone.Domain.Entities;
using DiscordClone.Domain.Enums;
using DiscordClone.Infrastructure.Persistence;
using MongoDB.Driver;

namespace DiscordClone.Infrastructure.Servers;

public partial class ServerService : IServerService
{
    private const long MaxIconSizeBytes = 10 * 1024 * 1024;

    // Same allowlist AttachmentService uses for avatars/banners: server icons and custom
    // emojis are rendered inline everywhere (member list, chat, reactions), so only
    // known-safe raster image types are accepted — otherwise a malicious upload here (e.g.
    // an .svg with an embedded <script>, disguised with an image content-type) could run
    // in another member's browser the moment the icon/emoji loads.
    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/gif",
    };

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif",
    };

    private readonly MongoContext _mongo;
    private readonly IPresenceService _presenceService;
    private readonly IStorageService _storage;

    public ServerService(MongoContext mongo, IPresenceService presenceService, IStorageService storage)
    {
        _mongo = mongo;
        _presenceService = presenceService;
        _storage = storage;
    }

    public async Task<ServerSummary> CreateServerAsync(Guid userId, CreateServerRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length is < 2 or > 100)
            throw new AppException("Server name must have between 2 and 100 characters.");

        var server = new Server
        {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        var ownerMembership = new ServerMember
        {
            Id = Guid.NewGuid(),
            ServerId = server.Id,
            UserId = userId,
            RoleIds = [],
            JoinedAt = DateTime.UtcNow,
        };

        var generalChannel = new Channel
        {
            Id = Guid.NewGuid(),
            ServerId = server.Id,
            Name = "general",
            Type = ChannelType.Text,
            Position = 0,
            CreatedAt = DateTime.UtcNow,
        };

        var generalVoiceChannel = new Channel
        {
            Id = Guid.NewGuid(),
            ServerId = server.Id,
            Name = "General",
            Type = ChannelType.Voice,
            Position = 1,
            CreatedAt = DateTime.UtcNow,
        };

        using var session = await _mongo.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await _mongo.Servers.InsertOneAsync(s, server, cancellationToken: token);
            await _mongo.ServerMembers.InsertOneAsync(s, ownerMembership, cancellationToken: token);
            await _mongo.Channels.InsertManyAsync(s, [generalChannel, generalVoiceChannel], cancellationToken: token);
            return true;
        }, cancellationToken: ct);

        return new ServerSummary(server.Id, server.Name, server.IconUrl, true, 1);
    }

    public async Task<IReadOnlyList<ServerSummary>> GetMyServersAsync(Guid userId, CancellationToken ct)
    {
        var memberships = await _mongo.ServerMembers.Find(m => m.UserId == userId).ToListAsync(ct);
        if (memberships.Count == 0)
            return [];

        var serverIds = memberships.Select(m => m.ServerId).ToList();
        var servers = await _mongo.Servers.Find(s => serverIds.Contains(s.Id)).ToListAsync(ct);
        var serversById = servers.ToDictionary(s => s.Id);

        var allMembers = await _mongo.ServerMembers.Find(m => serverIds.Contains(m.ServerId)).ToListAsync(ct);
        var countsByServer = allMembers.GroupBy(m => m.ServerId).ToDictionary(g => g.Key, g => g.Count());

        return memberships
            .Where(m => serversById.ContainsKey(m.ServerId))
            .Select(m =>
            {
                var server = serversById[m.ServerId];
                return new ServerSummary(server.Id, server.Name, server.IconUrl, server.OwnerId == userId, countsByServer.GetValueOrDefault(m.ServerId, 1));
            })
            .ToList();
    }

    public async Task<ServerDetail> GetServerAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        var (server, _) = await RequireMembershipAsync(userId, serverId, ct);

        var channels = await _mongo.Channels.Find(c => c.ServerId == serverId).ToListAsync(ct);
        var channelDtos = channels
            .OrderBy(c => c.Position)
            .Select(ToChannelDto)
            .ToList();

        var categories = await _mongo.ChannelCategories.Find(c => c.ServerId == serverId).SortBy(c => c.Position).ToListAsync(ct);
        var categoryDtos = categories.Select(c => new CategoryDto(c.Id, c.ServerId, c.Name, c.Position)).ToList();

        return new ServerDetail(server.Id, server.Name, server.Description, server.IconUrl, server.OwnerId, server.CreatedAt, channelDtos, categoryDtos);
    }

    public async Task<ServerSummary> UpdateServerAsync(Guid userId, Guid serverId, UpdateServerRequest request, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageServer, ct);

        var updates = new List<UpdateDefinition<Server>>();

        if (request.Name is not null)
        {
            var trimmed = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length is < 2 or > 100)
                throw new AppException("Server name must have between 2 and 100 characters.");
            updates.Add(Builders<Server>.Update.Set(s => s.Name, trimmed));
        }

        if (request.Description is not null)
        {
            var description = request.Description.Trim();
            if (description.Length > 1024)
                throw new AppException("Server description must be at most 1024 characters.");
            updates.Add(Builders<Server>.Update.Set(s => s.Description, description.Length == 0 ? null : description));
        }

        if (updates.Count == 0)
            throw new AppException("Nothing to update.");

        var server = await _mongo.Servers.FindOneAndUpdateAsync<Server>(
            s => s.Id == serverId,
            Builders<Server>.Update.Combine(updates),
            new FindOneAndUpdateOptions<Server> { ReturnDocument = ReturnDocument.After },
            ct) ?? throw new AppException("Server not found.", 404);

        var memberCount = await _mongo.ServerMembers.CountDocumentsAsync(m => m.ServerId == serverId, cancellationToken: ct);
        return new ServerSummary(server.Id, server.Name, server.IconUrl, server.OwnerId == userId, (int)memberCount);
    }

    public async Task<ServerSummary> UpdateServerIconAsync(Guid userId, Guid serverId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageServer, ct);

        if (sizeBytes <= 0 || sizeBytes > MaxIconSizeBytes)
            throw new AppException($"Icon size must be between 1 byte and {MaxIconSizeBytes / (1024 * 1024)}MB.");

        ValidateIsImage(fileName, contentType);

        var key = $"server-icons/{serverId}/{Guid.NewGuid()}-{SanitizeFileName(fileName)}";
        await _storage.UploadAsync(key, content, contentType, null, ct);
        var url = _storage.GetPublicUrl(key);

        var update = Builders<Server>.Update.Set(s => s.IconUrl, url);
        var server = await _mongo.Servers.FindOneAndUpdateAsync<Server>(
            s => s.Id == serverId,
            update,
            new FindOneAndUpdateOptions<Server> { ReturnDocument = ReturnDocument.After },
            ct) ?? throw new AppException("Server not found.", 404);

        var memberCount = await _mongo.ServerMembers.CountDocumentsAsync(m => m.ServerId == serverId, cancellationToken: ct);
        return new ServerSummary(server.Id, server.Name, server.IconUrl, server.OwnerId == userId, (int)memberCount);
    }

    public async Task DeleteServerAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        var server = await _mongo.Servers.Find(s => s.Id == serverId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Server not found.", 404);

        if (server.OwnerId != userId)
            throw new AppException("Only the server owner can delete the server.", 403);

        var channelIds = await _mongo.Channels.Find(c => c.ServerId == serverId).Project(c => c.Id).ToListAsync(ct);
        var messageIds = await _mongo.Messages.Find(m => channelIds.Contains(m.ChannelId)).Project(m => m.Id).ToListAsync(ct);

        using var session = await _mongo.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await _mongo.MessageReactions.DeleteManyAsync(s, r => messageIds.Contains(r.MessageId), cancellationToken: token);
            await _mongo.Attachments.DeleteManyAsync(s, a => a.MessageId != null && messageIds.Contains(a.MessageId.Value), cancellationToken: token);
            await _mongo.Messages.DeleteManyAsync(s, m => channelIds.Contains(m.ChannelId), cancellationToken: token);
            await _mongo.Channels.DeleteManyAsync(s, c => c.ServerId == serverId, cancellationToken: token);
            await _mongo.ChannelCategories.DeleteManyAsync(s, cat => cat.ServerId == serverId, cancellationToken: token);
            await _mongo.Roles.DeleteManyAsync(s, r => r.ServerId == serverId, cancellationToken: token);
            await _mongo.ServerMembers.DeleteManyAsync(s, m => m.ServerId == serverId, cancellationToken: token);
            await _mongo.ServerInvites.DeleteManyAsync(s, i => i.ServerId == serverId, cancellationToken: token);
            await _mongo.ServerBans.DeleteManyAsync(s, b => b.ServerId == serverId, cancellationToken: token);
            await _mongo.Servers.DeleteOneAsync(s, sv => sv.Id == serverId, cancellationToken: token);
            return true;
        }, cancellationToken: ct);
    }

    public async Task RemoveMemberAsync(Guid userId, Guid serverId, Guid targetUserId, CancellationToken ct)
    {
        var (server, _) = await RequirePermissionAsync(userId, serverId, ServerPermission.KickMembers, ct);

        if (server.OwnerId == targetUserId)
            throw new AppException("Cannot remove the server owner.", 400);

        var target = await _mongo.ServerMembers
            .Find(m => m.ServerId == serverId && m.UserId == targetUserId)
            .SingleOrDefaultAsync(ct)
            ?? throw new AppException("Member not found.", 404);

        await _mongo.ServerMembers.DeleteOneAsync(m => m.Id == target.Id, ct);
    }

    public async Task<BanDto> BanMemberAsync(Guid userId, Guid serverId, Guid targetUserId, BanUserRequest request, CancellationToken ct)
    {
        var (server, _) = await RequirePermissionAsync(userId, serverId, ServerPermission.BanMembers, ct);

        if (server.OwnerId == targetUserId)
            throw new AppException("Cannot ban the server owner.", 400);

        var target = await _mongo.Users.Find(u => u.Id == targetUserId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("User not found.", 404);

        var ban = new ServerBan
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            UserId = targetUserId,
            BannedByUserId = userId,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            CreatedAt = DateTime.UtcNow,
        };

        using var session = await _mongo.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await _mongo.ServerMembers.DeleteOneAsync(s, m => m.ServerId == serverId && m.UserId == targetUserId, cancellationToken: token);
            await _mongo.ServerBans.ReplaceOneAsync(
                s,
                b => b.ServerId == serverId && b.UserId == targetUserId,
                ban,
                new ReplaceOptions { IsUpsert = true },
                token);
            return true;
        }, cancellationToken: ct);

        return new BanDto(target.Id, target.Username, target.DisplayName, target.AvatarUrl, ban.BannedByUserId, ban.Reason, ban.CreatedAt);
    }

    public async Task UnbanMemberAsync(Guid userId, Guid serverId, Guid targetUserId, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.BanMembers, ct);
        await _mongo.ServerBans.DeleteOneAsync(b => b.ServerId == serverId && b.UserId == targetUserId, ct);
    }

    public async Task<IReadOnlyList<BanDto>> GetBansAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.BanMembers, ct);

        var bans = await _mongo.ServerBans.Find(b => b.ServerId == serverId).SortByDescending(b => b.CreatedAt).ToListAsync(ct);
        var userIds = bans.Select(b => b.UserId).ToList();
        var users = await _mongo.Users.Find(u => userIds.Contains(u.Id)).ToListAsync(ct);
        var usersById = users.ToDictionary(u => u.Id);

        return bans
            .Where(b => usersById.ContainsKey(b.UserId))
            .Select(b =>
            {
                var user = usersById[b.UserId];
                return new BanDto(user.Id, user.Username, user.DisplayName, user.AvatarUrl, b.BannedByUserId, b.Reason, b.CreatedAt);
            })
            .ToList();
    }

    public async Task<ChannelDto> CreateChannelAsync(Guid userId, Guid serverId, CreateChannelRequest request, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageChannels, ct);

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length is < 1 or > 100)
            throw new AppException("Channel name must have between 1 and 100 characters.");

        if (request.CategoryId is { } categoryId)
        {
            var categoryExists = await _mongo.ChannelCategories.Find(c => c.Id == categoryId && c.ServerId == serverId).AnyAsync(ct);
            if (!categoryExists)
                throw new AppException("Category not found.", 404);
        }

        var existing = await _mongo.Channels.Find(c => c.ServerId == serverId && c.CategoryId == request.CategoryId).ToListAsync(ct);
        var maxPosition = existing.Count > 0 ? existing.Max(c => c.Position) : -1;

        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            Name = name,
            Type = request.Type,
            CategoryId = request.CategoryId,
            Position = maxPosition + 1,
            CreatedAt = DateTime.UtcNow,
        };

        await _mongo.Channels.InsertOneAsync(channel, cancellationToken: ct);

        return ToChannelDto(channel);
    }

    public async Task<ChannelDto> MoveChannelAsync(Guid userId, Guid serverId, Guid channelId, MoveChannelRequest request, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageChannels, ct);

        if (request.CategoryId is { } categoryId)
        {
            var categoryExists = await _mongo.ChannelCategories.Find(c => c.Id == categoryId && c.ServerId == serverId).AnyAsync(ct);
            if (!categoryExists)
                throw new AppException("Category not found.", 404);
        }

        var update = Builders<Channel>.Update
            .Set(c => c.CategoryId, request.CategoryId)
            .Set(c => c.Position, request.Position);

        var channel = await _mongo.Channels.FindOneAndUpdateAsync<Channel>(
            c => c.Id == channelId && c.ServerId == serverId,
            update,
            new FindOneAndUpdateOptions<Channel> { ReturnDocument = ReturnDocument.After },
            ct) ?? throw new AppException("Channel not found.", 404);

        return ToChannelDto(channel);
    }

    public async Task<IReadOnlyList<ChannelDto>> GetChannelsAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        await RequireMembershipAsync(userId, serverId, ct);

        var channels = await _mongo.Channels.Find(c => c.ServerId == serverId).SortBy(c => c.Position).ToListAsync(ct);
        return channels.Select(ToChannelDto).ToList();
    }

    public async Task<CategoryDto> CreateCategoryAsync(Guid userId, Guid serverId, CreateCategoryRequest request, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageChannels, ct);

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length is < 1 or > 100)
            throw new AppException("Category name must have between 1 and 100 characters.");

        var existing = await _mongo.ChannelCategories.Find(c => c.ServerId == serverId).ToListAsync(ct);
        var maxPosition = existing.Count > 0 ? existing.Max(c => c.Position) : -1;

        var category = new ChannelCategory
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            Name = name,
            Position = maxPosition + 1,
            CreatedAt = DateTime.UtcNow,
        };

        await _mongo.ChannelCategories.InsertOneAsync(category, cancellationToken: ct);

        return new CategoryDto(category.Id, category.ServerId, category.Name, category.Position);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid userId, Guid serverId, Guid categoryId, UpdateCategoryRequest request, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageChannels, ct);

        var updates = new List<UpdateDefinition<ChannelCategory>>();

        if (request.Name is not null)
        {
            var trimmed = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length is < 1 or > 100)
                throw new AppException("Category name must have between 1 and 100 characters.");
            updates.Add(Builders<ChannelCategory>.Update.Set(c => c.Name, trimmed));
        }

        if (request.Position is { } position)
            updates.Add(Builders<ChannelCategory>.Update.Set(c => c.Position, position));

        if (updates.Count == 0)
            throw new AppException("Nothing to update.");

        var category = await _mongo.ChannelCategories.FindOneAndUpdateAsync<ChannelCategory>(
            c => c.Id == categoryId && c.ServerId == serverId,
            Builders<ChannelCategory>.Update.Combine(updates),
            new FindOneAndUpdateOptions<ChannelCategory> { ReturnDocument = ReturnDocument.After },
            ct) ?? throw new AppException("Category not found.", 404);

        return new CategoryDto(category.Id, category.ServerId, category.Name, category.Position);
    }

    public async Task DeleteCategoryAsync(Guid userId, Guid serverId, Guid categoryId, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageChannels, ct);

        using var session = await _mongo.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await _mongo.Channels.UpdateManyAsync(
                s,
                c => c.ServerId == serverId && c.CategoryId == categoryId,
                Builders<Channel>.Update.Set(c => c.CategoryId, null),
                cancellationToken: token);
            await _mongo.ChannelCategories.DeleteOneAsync(s, c => c.Id == categoryId && c.ServerId == serverId, cancellationToken: token);
            return true;
        }, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        await RequireMembershipAsync(userId, serverId, ct);

        var roles = await _mongo.Roles.Find(r => r.ServerId == serverId).SortBy(r => r.Position).ToListAsync(ct);
        return roles.Select(ToRoleDto).ToList();
    }

    public async Task<RoleDto> CreateRoleAsync(Guid userId, Guid serverId, CreateRoleRequest request, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageRoles, ct);

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length is < 1 or > 50)
            throw new AppException("Role name must have between 1 and 50 characters.");

        var color = NormalizeColor(request.Color);

        var existing = await _mongo.Roles.Find(r => r.ServerId == serverId).ToListAsync(ct);
        var maxPosition = existing.Count > 0 ? existing.Max(r => r.Position) : -1;

        var role = new Role
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            Name = name,
            Color = color,
            Permissions = CombinePermissions(request.Permissions),
            Position = maxPosition + 1,
            CreatedAt = DateTime.UtcNow,
        };

        await _mongo.Roles.InsertOneAsync(role, cancellationToken: ct);

        return ToRoleDto(role);
    }

    public async Task<RoleDto> UpdateRoleAsync(Guid userId, Guid serverId, Guid roleId, UpdateRoleRequest request, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageRoles, ct);

        var updates = new List<UpdateDefinition<Role>>();

        if (request.Name is not null)
        {
            var trimmed = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length is < 1 or > 50)
                throw new AppException("Role name must have between 1 and 50 characters.");
            updates.Add(Builders<Role>.Update.Set(r => r.Name, trimmed));
        }

        if (request.Color is not null)
            updates.Add(Builders<Role>.Update.Set(r => r.Color, NormalizeColor(request.Color)));

        if (request.Permissions is not null)
            updates.Add(Builders<Role>.Update.Set(r => r.Permissions, CombinePermissions(request.Permissions)));

        if (request.Position is { } position)
            updates.Add(Builders<Role>.Update.Set(r => r.Position, position));

        if (updates.Count == 0)
            throw new AppException("Nothing to update.");

        var role = await _mongo.Roles.FindOneAndUpdateAsync<Role>(
            r => r.Id == roleId && r.ServerId == serverId,
            Builders<Role>.Update.Combine(updates),
            new FindOneAndUpdateOptions<Role> { ReturnDocument = ReturnDocument.After },
            ct) ?? throw new AppException("Role not found.", 404);

        return ToRoleDto(role);
    }

    public async Task DeleteRoleAsync(Guid userId, Guid serverId, Guid roleId, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageRoles, ct);

        using var session = await _mongo.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await _mongo.ServerMembers.UpdateManyAsync(
                s,
                m => m.ServerId == serverId && m.RoleIds.Contains(roleId),
                Builders<ServerMember>.Update.Pull(m => m.RoleIds, roleId),
                cancellationToken: token);
            await _mongo.Roles.DeleteOneAsync(s, r => r.Id == roleId && r.ServerId == serverId, cancellationToken: token);
            return true;
        }, cancellationToken: ct);
    }

    public async Task AssignRoleAsync(Guid userId, Guid serverId, Guid targetUserId, Guid roleId, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageRoles, ct);

        var roleExists = await _mongo.Roles.Find(r => r.Id == roleId && r.ServerId == serverId).AnyAsync(ct);
        if (!roleExists)
            throw new AppException("Role not found.", 404);

        var target = await _mongo.ServerMembers.Find(m => m.ServerId == serverId && m.UserId == targetUserId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Member not found.", 404);

        await _mongo.ServerMembers.UpdateOneAsync(
            m => m.Id == target.Id,
            Builders<ServerMember>.Update.AddToSet(m => m.RoleIds, roleId),
            cancellationToken: ct);
    }

    public async Task UnassignRoleAsync(Guid userId, Guid serverId, Guid targetUserId, Guid roleId, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageRoles, ct);

        var target = await _mongo.ServerMembers.Find(m => m.ServerId == serverId && m.UserId == targetUserId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Member not found.", 404);

        await _mongo.ServerMembers.UpdateOneAsync(
            m => m.Id == target.Id,
            Builders<ServerMember>.Update.Pull(m => m.RoleIds, roleId),
            cancellationToken: ct);
    }

    public async Task<MyPermissionsDto> GetMyPermissionsAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        var (server, member) = await RequireMembershipAsync(userId, serverId, ct);

        if (server.OwnerId == userId)
            return new MyPermissionsDto(true, Enum.GetValues<ServerPermission>().Where(p => p != ServerPermission.None).ToList());

        var effective = await ComputeEffectivePermissionsAsync(member, ct);
        return new MyPermissionsDto(false, DecomposePermissions(effective));
    }

    public async Task<IReadOnlyList<MemberDto>> GetMembersAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        var (server, _) = await RequireMembershipAsync(userId, serverId, ct);

        var members = await _mongo.ServerMembers.Find(m => m.ServerId == serverId).SortBy(m => m.JoinedAt).ToListAsync(ct);
        var userIds = members.Select(m => m.UserId).ToList();
        var users = await _mongo.Users.Find(u => userIds.Contains(u.Id)).ToListAsync(ct);
        var usersById = users.ToDictionary(u => u.Id);

        var statuses = await _presenceService.GetEffectiveStatusesAsync(members.Select(m => m.UserId), ct);
        var activities = await _presenceService.GetActivitiesAsync(members.Select(m => m.UserId), ct);

        return members
            .Where(m => usersById.ContainsKey(m.UserId))
            .Select(m =>
            {
                var user = usersById[m.UserId];
                var status = statuses.GetValueOrDefault(m.UserId, PresenceStatus.Offline);
                var activity = user.ShareActivityStatus ? activities.GetValueOrDefault(m.UserId) : null;
                return new MemberDto(
                    m.UserId, user.Username, user.DisplayName, m.Nickname, user.AvatarUrl, m.RoleIds,
                    server.OwnerId == m.UserId, m.JoinedAt, status, user.CustomStatusText, user.CustomStatusEmoji, activity);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetMyServerIdsAsync(Guid userId, CancellationToken ct)
    {
        var memberships = await _mongo.ServerMembers.Find(m => m.UserId == userId).ToListAsync(ct);
        return memberships.Select(m => m.ServerId).ToList();
    }

    public async Task<InviteDto> CreateInviteAsync(Guid userId, Guid serverId, CreateInviteRequest request, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.CreateInvite, ct);

        var invite = new ServerInvite
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            Code = GenerateInviteCode(),
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = request.ExpiresInHours.HasValue ? DateTime.UtcNow.AddHours(request.ExpiresInHours.Value) : null,
            MaxUses = request.MaxUses,
        };

        await _mongo.ServerInvites.InsertOneAsync(invite, cancellationToken: ct);

        return ToInviteDto(invite);
    }

    public async Task<IReadOnlyList<InviteDto>> GetInvitesAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageServer, ct);

        var invites = await _mongo.ServerInvites.Find(i => i.ServerId == serverId).SortByDescending(i => i.CreatedAt).ToListAsync(ct);
        return invites.Select(ToInviteDto).ToList();
    }

    public async Task RevokeInviteAsync(Guid userId, Guid serverId, Guid inviteId, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageServer, ct);

        await _mongo.ServerInvites.DeleteOneAsync(i => i.Id == inviteId && i.ServerId == serverId, ct);
    }

    private static InviteDto ToInviteDto(ServerInvite invite) =>
        new(invite.Id, invite.Code, invite.CreatedAt, invite.ExpiresAt, invite.MaxUses, invite.Uses, invite.IsValid);

    public async Task<IReadOnlyList<CustomEmojiDto>> GetCustomEmojisAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        await RequireMembershipAsync(userId, serverId, ct);

        var emojis = await _mongo.CustomEmojis.Find(e => e.ServerId == serverId).SortBy(e => e.Name).ToListAsync(ct);
        return emojis.Select(e => new CustomEmojiDto(e.Id, e.ServerId, e.Name, e.ImageUrl, e.CreatedAt)).ToList();
    }

    public async Task<CustomEmojiDto> CreateCustomEmojiAsync(
        Guid userId, Guid serverId, string name, Stream content, string contentType, long sizeBytes, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageEmojis, ct);

        var trimmedName = name.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 32 || !trimmedName.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw new AppException("Emoji name must be 1-32 characters, letters/digits/underscore only.");

        if (sizeBytes <= 0 || sizeBytes > MaxIconSizeBytes)
            throw new AppException($"Emoji image size must be between 1 byte and {MaxIconSizeBytes / (1024 * 1024)}MB.");

        // No file name/extension comes through for emoji uploads (see method signature) —
        // only the claimed content-type is available to check, so that's the whole check.
        if (!AllowedImageContentTypes.Contains(contentType))
            throw new AppException("Only PNG, JPG, GIF or WEBP images are allowed.");

        var key = $"custom-emojis/{serverId}/{Guid.NewGuid()}-{trimmedName}";
        await _storage.UploadAsync(key, content, contentType, null, ct);

        var emoji = new CustomEmoji
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            Name = trimmedName,
            ImageUrl = _storage.GetPublicUrl(key),
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        try
        {
            await _mongo.CustomEmojis.InsertOneAsync(emoji, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new AppException("An emoji with this name already exists on this server.", 409);
        }

        return new CustomEmojiDto(emoji.Id, emoji.ServerId, emoji.Name, emoji.ImageUrl, emoji.CreatedAt);
    }

    public async Task DeleteCustomEmojiAsync(Guid userId, Guid serverId, Guid emojiId, CancellationToken ct)
    {
        await RequirePermissionAsync(userId, serverId, ServerPermission.ManageEmojis, ct);
        await _mongo.CustomEmojis.DeleteOneAsync(e => e.Id == emojiId && e.ServerId == serverId, ct);
    }

    public async Task<ServerSummary> JoinByInviteAsync(Guid userId, string code, CancellationToken ct)
    {
        var invite = await _mongo.ServerInvites.Find(i => i.Code == code).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Invite not found.", 404);

        if (!invite.IsValid)
            throw new AppException("Invite is expired or has reached its usage limit.", 410);

        var server = await _mongo.Servers.Find(s => s.Id == invite.ServerId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Server not found.", 404);

        var isBanned = await _mongo.ServerBans.Find(b => b.ServerId == invite.ServerId && b.UserId == userId).AnyAsync(ct);
        if (isBanned)
            throw new AppException("You are banned from this server.", 403);

        var membership = await _mongo.ServerMembers
            .Find(m => m.ServerId == invite.ServerId && m.UserId == userId)
            .SingleOrDefaultAsync(ct);

        if (membership is null)
        {
            membership = new ServerMember
            {
                Id = Guid.NewGuid(),
                ServerId = invite.ServerId,
                UserId = userId,
                RoleIds = [],
                JoinedAt = DateTime.UtcNow,
            };

            using var session = await _mongo.Client.StartSessionAsync(cancellationToken: ct);
            await session.WithTransactionAsync(async (s, token) =>
            {
                await _mongo.ServerMembers.InsertOneAsync(s, membership, cancellationToken: token);
                await _mongo.ServerInvites.UpdateOneAsync(s, i => i.Id == invite.Id, Builders<ServerInvite>.Update.Inc(i => i.Uses, 1), cancellationToken: token);
                return true;
            }, cancellationToken: ct);
        }

        var memberCount = await _mongo.ServerMembers.CountDocumentsAsync(m => m.ServerId == invite.ServerId, cancellationToken: ct);

        return new ServerSummary(server.Id, server.Name, server.IconUrl, server.OwnerId == userId, (int)memberCount);
    }

    public async Task EnsureMembershipAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        await RequireMembershipAsync(userId, serverId, ct);
    }

    private async Task<(Server Server, ServerMember Member)> RequireMembershipAsync(Guid userId, Guid serverId, CancellationToken ct)
    {
        var server = await _mongo.Servers.Find(s => s.Id == serverId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("Server not found.", 404);

        var membership = await _mongo.ServerMembers
            .Find(m => m.ServerId == serverId && m.UserId == userId)
            .SingleOrDefaultAsync(ct)
            ?? throw new AppException("You are not a member of this server.", 403);

        return (server, membership);
    }

    private async Task<(Server Server, ServerMember Member)> RequirePermissionAsync(Guid userId, Guid serverId, ServerPermission permission, CancellationToken ct)
    {
        var (server, member) = await RequireMembershipAsync(userId, serverId, ct);

        if (server.OwnerId == userId)
            return (server, member);

        var effective = await ComputeEffectivePermissionsAsync(member, ct);
        if (!effective.HasFlag(permission))
            throw new AppException("You do not have permission to perform this action.", 403);

        return (server, member);
    }

    private async Task<ServerPermission> ComputeEffectivePermissionsAsync(ServerMember member, CancellationToken ct)
    {
        if (member.RoleIds.Count == 0)
            return ServerPermission.None;

        var roles = await _mongo.Roles.Find(r => member.RoleIds.Contains(r.Id)).ToListAsync(ct);
        return roles.Aggregate(ServerPermission.None, (acc, r) => acc | r.Permissions);
    }

    private static IReadOnlyList<ServerPermission> DecomposePermissions(ServerPermission combined) =>
        Enum.GetValues<ServerPermission>().Where(p => p != ServerPermission.None && combined.HasFlag(p)).ToList();

    private static ServerPermission CombinePermissions(IReadOnlyList<ServerPermission> permissions) =>
        permissions.Aggregate(ServerPermission.None, (acc, p) => acc | p);

    private static ChannelDto ToChannelDto(Channel c) => new(c.Id, c.ServerId, c.Name, c.Type, c.Position, c.CategoryId);

    private static RoleDto ToRoleDto(Role r) => new(r.Id, r.ServerId, r.Name, r.Color, DecomposePermissions(r.Permissions), r.Position);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

    private static string NormalizeColor(string color)
    {
        var trimmed = color.Trim();
        if (!HexColorRegex().IsMatch(trimmed))
            throw new AppException("Color must be a hex value like #22d3ee.");
        return trimmed.ToLowerInvariant();
    }

    private static string GenerateInviteCode()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = RandomNumberGenerator.GetBytes(8);
        var chars = new char[8];
        for (var i = 0; i < 8; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];

        return new string(chars);
    }

    private static void ValidateIsImage(string fileName, string contentType)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedImageExtensions.Contains(ext) || !AllowedImageContentTypes.Contains(contentType))
            throw new AppException("Only PNG, JPG, GIF or WEBP images are allowed.");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(fileName.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "file" : clean;
    }
}
