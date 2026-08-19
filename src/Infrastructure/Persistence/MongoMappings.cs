using DiscordClone.Domain.Entities;
using DiscordClone.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace DiscordClone.Infrastructure.Persistence;

public static class MongoMappings
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;

        _registered = true;

        BsonSerializer.RegisterSerializer(typeof(Guid), new GuidSerializer(BsonType.String));
        BsonSerializer.RegisterSerializer(typeof(Guid?), new NullableSerializer<Guid>(new GuidSerializer(BsonType.String)));
        BsonSerializer.RegisterSerializer(typeof(ServerPermission), new EnumSerializer<ServerPermission>(BsonType.Int64));

        BsonClassMap.RegisterClassMap<User>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(u => u.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<RefreshToken>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(rt => rt.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Server>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(s => s.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Channel>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(c => c.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<ServerMember>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(m => m.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<ServerInvite>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(i => i.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Message>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(m => m.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Attachment>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(a => a.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<MessageReaction>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(r => r.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Role>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(r => r.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<ServerBan>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(b => b.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<ChannelCategory>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(c => c.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<FriendRequest>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(r => r.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Friendship>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(f => f.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<DmChannel>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(d => d.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<DmMessage>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(m => m.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<ChannelReadState>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(s => s.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<PushSubscription>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(s => s.Id);
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<CustomEmoji>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(e => e.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
