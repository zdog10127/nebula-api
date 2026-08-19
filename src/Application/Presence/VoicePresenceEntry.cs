namespace DiscordClone.Application.Presence;

public record VoicePresenceEntry(Guid UserId, bool IsMuted, bool IsDeafened);
