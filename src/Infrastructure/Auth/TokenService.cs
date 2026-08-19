using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DiscordClone.Application.Auth;
using DiscordClone.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace DiscordClone.Infrastructure.Auth;

public class TokenService : ITokenService
{
    private readonly JwtOptions _options;

    public TokenService(JwtOptions options)
    {
        _options = options;
    }

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshExpirationDays);

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(User user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("username", user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public string HashRefreshToken(string refreshToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes);
    }
}
