using System.Text.Json;
using DiscordClone.Application.Presence;
using DiscordClone.Application.Push;
using DiscordClone.Domain.Entities;
using DiscordClone.Infrastructure.Persistence;
using MongoDB.Driver;
using WebPush;
using WebPushSubscription = WebPush.PushSubscription;

namespace DiscordClone.Infrastructure.Push;

public class PushService : IPushService
{
    private readonly MongoContext _mongo;
    private readonly IPresenceService _presence;
    private readonly PushOptions _options;
    private readonly WebPushClient _client = new();

    public PushService(MongoContext mongo, IPresenceService presence, PushOptions options)
    {
        _mongo = mongo;
        _presence = presence;
        _options = options;
    }

    public async Task SubscribeAsync(Guid userId, PushSubscribeRequest request, CancellationToken ct)
    {
        var subscription = new Domain.Entities.PushSubscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Endpoint = request.Endpoint,
            P256dhKey = request.P256dhKey,
            AuthKey = request.AuthKey,
            CreatedAt = DateTime.UtcNow,
        };

        await _mongo.PushSubscriptions.ReplaceOneAsync(
            s => s.Endpoint == request.Endpoint,
            subscription,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public async Task UnsubscribeAsync(Guid userId, string endpoint, CancellationToken ct)
    {
        await _mongo.PushSubscriptions.DeleteOneAsync(s => s.UserId == userId && s.Endpoint == endpoint, ct);
    }

    public async Task NotifyIfOfflineAsync(Guid userId, string title, string body, string? url, CancellationToken ct)
    {
        if (!_options.IsConfigured)
            return;

        if (await _presence.HasActiveConnectionAsync(userId, ct))
            return;

        var subscriptions = await _mongo.PushSubscriptions.Find(s => s.UserId == userId).ToListAsync(ct);
        if (subscriptions.Count == 0)
            return;

        var vapidDetails = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);
        var payload = JsonSerializer.Serialize(new { title, body, url });

        foreach (var subscription in subscriptions)
        {
            var pushSubscription = new WebPushSubscription(subscription.Endpoint, subscription.P256dhKey, subscription.AuthKey);
            try
            {
                await _client.SendNotificationAsync(pushSubscription, payload, vapidDetails, cancellationToken: ct);
            }
            catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
            {
                // Subscription is no longer valid on the browser's push service — stop trying.
                await _mongo.PushSubscriptions.DeleteOneAsync(s => s.Id == subscription.Id, ct);
            }
            catch (WebPushException)
            {
                // Best-effort: a single failed device push should not fail the caller's request.
            }
        }
    }
}
