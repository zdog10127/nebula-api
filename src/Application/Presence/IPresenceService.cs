using DiscordClone.Domain.Enums;

namespace DiscordClone.Application.Presence;

public interface IPresenceService
{
    Task<bool> ConnectAsync(Guid userId, string connectionId, CancellationToken ct);
    Task<bool> DisconnectAsync(Guid userId, string connectionId, CancellationToken ct);
    Task<bool> HasActiveConnectionAsync(Guid userId, CancellationToken ct);

    Task SetStatusAsync(Guid userId, PresenceStatus status, CancellationToken ct);
    Task<PresenceStatus> GetEffectiveStatusAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, PresenceStatus>> GetEffectiveStatusesAsync(IEnumerable<Guid> userIds, CancellationToken ct);
}
