namespace DiscordClone.Application.Push;

public record PushSubscribeRequest(string Endpoint, string P256dhKey, string AuthKey);

public interface IPushService
{
    Task SubscribeAsync(Guid userId, PushSubscribeRequest request, CancellationToken ct);
    Task UnsubscribeAsync(Guid userId, string endpoint, CancellationToken ct);

    // No-ops (including when unconfigured) if the user currently has an active SignalR
    // connection — they're already seeing this live in the app, a push would be redundant.
    Task NotifyIfOfflineAsync(Guid userId, string title, string body, string? url, CancellationToken ct);
}
