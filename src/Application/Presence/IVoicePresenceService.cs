namespace DiscordClone.Application.Presence;

public interface IVoicePresenceService
{
    Task<IReadOnlyList<VoicePresenceEntry>> JoinAsync(Guid channelId, string connectionId, Guid userId, CancellationToken ct);

    Task<(Guid ChannelId, IReadOnlyList<VoicePresenceEntry> Entries)?> LeaveAsync(string connectionId, CancellationToken ct);

    Task<(Guid ChannelId, IReadOnlyList<VoicePresenceEntry> Entries)?> UpdateStateAsync(string connectionId, bool isMuted, bool isDeafened, CancellationToken ct);

    Task<IReadOnlyList<VoicePresenceEntry>> GetParticipantEntriesAsync(Guid channelId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<VoicePresenceEntry>>> GetParticipantsForChannelsAsync(IEnumerable<Guid> channelIds, CancellationToken ct);
}
