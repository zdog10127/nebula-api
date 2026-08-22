using DiscordClone.Domain.Entities;
using MongoDB.Driver;

namespace DiscordClone.Infrastructure.Persistence;

public static class MongoIndexInitializer
{
    public static async Task EnsureIndexesAsync(MongoContext context, CancellationToken ct)
    {
        await context.Users.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.Username), new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.Email), new CreateIndexOptions { Unique = true }),
                // Sparse: most users never link Steam, so SteamId64 is null/absent on
                // most documents — a sparse index excludes those from the uniqueness
                // check entirely, so it's only "one Nébula account per Steam account"
                // among the ones that actually linked one.
                new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.SteamId64), new CreateIndexOptions { Unique = true, Sparse = true }),
            ], ct);

        await context.RefreshTokens.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<RefreshToken>(Builders<RefreshToken>.IndexKeys.Ascending(rt => rt.TokenHash), new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<RefreshToken>(Builders<RefreshToken>.IndexKeys.Ascending(rt => rt.UserId)),
            ], ct);

        await context.Servers.Indexes.CreateOneAsync(
            new CreateIndexModel<Server>(Builders<Server>.IndexKeys.Ascending(s => s.OwnerId)), cancellationToken: ct);

        await context.Channels.Indexes.CreateOneAsync(
            new CreateIndexModel<Channel>(Builders<Channel>.IndexKeys.Ascending(c => c.ServerId)), cancellationToken: ct);

        await context.ServerMembers.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ServerMember>(
                    Builders<ServerMember>.IndexKeys.Ascending(m => m.ServerId).Ascending(m => m.UserId),
                    new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<ServerMember>(Builders<ServerMember>.IndexKeys.Ascending(m => m.UserId)),
            ], ct);

        await context.ServerInvites.Indexes.CreateOneAsync(
            new CreateIndexModel<ServerInvite>(Builders<ServerInvite>.IndexKeys.Ascending(i => i.Code), new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        await context.Messages.Indexes.CreateOneAsync(
            new CreateIndexModel<Message>(Builders<Message>.IndexKeys.Ascending(m => m.ChannelId).Descending(m => m.CreatedAt)),
            cancellationToken: ct);

        await context.Attachments.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Attachment>(Builders<Attachment>.IndexKeys.Ascending(a => a.MessageId)),
                new CreateIndexModel<Attachment>(Builders<Attachment>.IndexKeys.Ascending(a => a.UploaderId)),
            ], ct);

        await context.MessageReactions.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<MessageReaction>(
                    Builders<MessageReaction>.IndexKeys.Ascending(r => r.MessageId).Ascending(r => r.UserId).Ascending(r => r.Emoji),
                    new CreateIndexOptions { Unique = true }),
            ], ct);

        await context.Roles.Indexes.CreateOneAsync(
            new CreateIndexModel<Role>(Builders<Role>.IndexKeys.Ascending(r => r.ServerId)), cancellationToken: ct);

        await context.ServerBans.Indexes.CreateOneAsync(
            new CreateIndexModel<ServerBan>(
                Builders<ServerBan>.IndexKeys.Ascending(b => b.ServerId).Ascending(b => b.UserId),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        await context.ChannelCategories.Indexes.CreateOneAsync(
            new CreateIndexModel<ChannelCategory>(Builders<ChannelCategory>.IndexKeys.Ascending(c => c.ServerId)), cancellationToken: ct);

        await context.FriendRequests.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<FriendRequest>(
                    Builders<FriendRequest>.IndexKeys.Ascending(r => r.FromUserId).Ascending(r => r.ToUserId),
                    new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<FriendRequest>(Builders<FriendRequest>.IndexKeys.Ascending(r => r.ToUserId)),
            ], ct);

        await context.Friendships.Indexes.CreateOneAsync(
            new CreateIndexModel<Friendship>(
                Builders<Friendship>.IndexKeys.Ascending(f => f.UserAId).Ascending(f => f.UserBId),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        await context.DmChannels.Indexes.CreateOneAsync(
            new CreateIndexModel<DmChannel>(
                Builders<DmChannel>.IndexKeys.Ascending(d => d.UserAId).Ascending(d => d.UserBId),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        await context.DmMessages.Indexes.CreateOneAsync(
            new CreateIndexModel<DmMessage>(Builders<DmMessage>.IndexKeys.Ascending(m => m.DmChannelId).Descending(m => m.CreatedAt)),
            cancellationToken: ct);

        await context.ChannelReadStates.Indexes.CreateOneAsync(
            new CreateIndexModel<ChannelReadState>(
                Builders<ChannelReadState>.IndexKeys.Ascending(s => s.UserId).Ascending(s => s.ChannelId),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        await context.PushSubscriptions.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<PushSubscription>(Builders<PushSubscription>.IndexKeys.Ascending(s => s.UserId)),
                new CreateIndexModel<PushSubscription>(Builders<PushSubscription>.IndexKeys.Ascending(s => s.Endpoint), new CreateIndexOptions { Unique = true }),
            ], ct);

        await context.CustomEmojis.Indexes.CreateOneAsync(
            new CreateIndexModel<CustomEmoji>(
                Builders<CustomEmoji>.IndexKeys.Ascending(e => e.ServerId).Ascending(e => e.Name),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct);

        // TTL index: Mongo automatically deletes a pending 2FA login once ExpiresAt is in
        // the past, so an abandoned "password verified, waiting for code" attempt never
        // accumulates in the database.
        await context.PendingTwoFactorLogins.Indexes.CreateOneAsync(
            new CreateIndexModel<PendingTwoFactorLogin>(
                Builders<PendingTwoFactorLogin>.IndexKeys.Ascending(p => p.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
            cancellationToken: ct);

        // Same TTL pattern as PendingTwoFactorLogins above — an abandoned Steam link
        // attempt (user closes the tab before finishing Steam's login) cleans itself up.
        await context.PendingSteamLinks.Indexes.CreateOneAsync(
            new CreateIndexModel<PendingSteamLink>(
                Builders<PendingSteamLink>.IndexKeys.Ascending(p => p.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
            cancellationToken: ct);
    }
}
