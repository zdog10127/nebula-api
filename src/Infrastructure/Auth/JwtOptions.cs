using Microsoft.Extensions.Configuration;

namespace DiscordClone.Infrastructure.Auth;

public class JwtOptions
{
    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public int ExpirationMinutes { get; init; }
    public int RefreshExpirationDays { get; init; }

    public static JwtOptions FromConfiguration(IConfiguration configuration) => new()
    {
        Secret = configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is not configured."),
        Issuer = configuration["JWT_ISSUER"] ?? "discordclone",
        ExpirationMinutes = int.TryParse(configuration["JWT_EXPIRATION_MINUTES"], out var exp) ? exp : 15,
        RefreshExpirationDays = int.TryParse(configuration["JWT_REFRESH_EXPIRATION_DAYS"], out var refreshExp) ? refreshExp : 30,
    };
}
