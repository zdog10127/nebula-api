namespace DiscordClone.Application.Auth;

public record RegisterRequest(string Username, string Email, string Password, string? DisplayName);

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record AuthResult(
    Guid UserId,
    string Username,
    string Email,
    string DisplayName,
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken);

public record UserProfile(
    Guid UserId,
    string Username,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    string? BannerUrl,
    string? BannerColor,
    string? Bio,
    string? Pronouns,
    string? CustomStatusText,
    string? CustomStatusEmoji);

public record PublicProfileDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    string? BannerUrl,
    string? BannerColor,
    string? Bio,
    string? Pronouns,
    string? CustomStatusText,
    string? CustomStatusEmoji,
    DateTime CreatedAt);

public record UpdateProfileRequest(
    string? DisplayName,
    string? Bio,
    string? Pronouns,
    string? BannerColor,
    string? CustomStatusText,
    string? CustomStatusEmoji);
