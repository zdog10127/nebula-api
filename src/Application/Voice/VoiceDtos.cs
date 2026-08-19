namespace DiscordClone.Application.Voice;

public record VoiceTokenResult(string Url, string Token, string RoomName, string Identity);

public record VoiceParticipantDto(Guid UserId, string Username, string DisplayName, string? AvatarUrl, bool IsMuted, bool IsDeafened);

public record ShareNowPlayingRequest(string Type, string Url, string? Title);

public record NowPlayingDto(string Type, string Url, string? Title, Guid SharedByUserId, string SharedByDisplayName, long StartedAtUnixMs);
