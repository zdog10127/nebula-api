using DiscordClone.Application.Presence;

namespace DiscordClone.Application.Voice;

public interface IVoiceService
{
    Task<VoiceTokenResult> GetJoinTokenAsync(Guid userId, Guid channelId, CancellationToken ct);

    Task<IReadOnlyList<VoiceParticipantDto>> ResolveParticipantsAsync(IEnumerable<VoicePresenceEntry> entries, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<VoiceParticipantDto>>> GetServerVoiceParticipantsAsync(Guid serverId, CancellationToken ct);

    Task<Guid> GetServerIdForChannelAsync(Guid channelId, CancellationToken ct);

    Task<NowPlayingDto> ShareNowPlayingAsync(Guid userId, Guid channelId, ShareNowPlayingRequest request, CancellationToken ct);
    Task StopNowPlayingAsync(Guid userId, Guid channelId, CancellationToken ct);
    Task<NowPlayingDto?> GetNowPlayingAsync(Guid channelId, CancellationToken ct);
}
