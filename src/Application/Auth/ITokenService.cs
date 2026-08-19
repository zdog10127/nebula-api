using DiscordClone.Domain.Entities;

namespace DiscordClone.Application.Auth;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(User user);
    string GenerateRefreshToken();
    string HashRefreshToken(string refreshToken);
    TimeSpan RefreshTokenLifetime { get; }
}
