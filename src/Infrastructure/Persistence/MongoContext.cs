using DiscordClone.Domain.Entities;
using MongoDB.Driver;

namespace DiscordClone.Infrastructure.Persistence;

public class MongoContext
{
    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }

    public MongoContext(MongoOptions options)
    {
        MongoMappings.Register();

        Client = new MongoClient(options.ConnectionString);
        Database = Client.GetDatabase(options.DatabaseName);
    }

    public IMongoCollection<User> Users => Database.GetCollection<User>("users");
    public IMongoCollection<RefreshToken> RefreshTokens => Database.GetCollection<RefreshToken>("refresh_tokens");
    public IMongoCollection<Server> Servers => Database.GetCollection<Server>("servers");
    public IMongoCollection<Channel> Channels => Database.GetCollection<Channel>("channels");
    public IMongoCollection<ServerMember> ServerMembers => Database.GetCollection<ServerMember>("server_members");
    public IMongoCollection<ServerInvite> ServerInvites => Database.GetCollection<ServerInvite>("server_invites");
    public IMongoCollection<Message> Messages => Database.GetCollection<Message>("messages");
    public IMongoCollection<Attachment> Attachments => Database.GetCollection<Attachment>("attachments");
    public IMongoCollection<MessageReaction> MessageReactions => Database.GetCollection<MessageReaction>("message_reactions");
    public IMongoCollection<Role> Roles => Database.GetCollection<Role>("roles");
    public IMongoCollection<ServerBan> ServerBans => Database.GetCollection<ServerBan>("server_bans");
    public IMongoCollection<ChannelCategory> ChannelCategories => Database.GetCollection<ChannelCategory>("channel_categories");
    public IMongoCollection<FriendRequest> FriendRequests => Database.GetCollection<FriendRequest>("friend_requests");
    public IMongoCollection<Friendship> Friendships => Database.GetCollection<Friendship>("friendships");
    public IMongoCollection<DmChannel> DmChannels => Database.GetCollection<DmChannel>("dm_channels");
    public IMongoCollection<DmMessage> DmMessages => Database.GetCollection<DmMessage>("dm_messages");
    public IMongoCollection<ChannelReadState> ChannelReadStates => Database.GetCollection<ChannelReadState>("channel_read_states");
    public IMongoCollection<PushSubscription> PushSubscriptions => Database.GetCollection<PushSubscription>("push_subscriptions");
    public IMongoCollection<CustomEmoji> CustomEmojis => Database.GetCollection<CustomEmoji>("custom_emojis");
    public IMongoCollection<PendingTwoFactorLogin> PendingTwoFactorLogins => Database.GetCollection<PendingTwoFactorLogin>("pending_two_factor_logins");
}
