using Microsoft.Extensions.Configuration;

namespace DiscordClone.Infrastructure.Gifs;

public class TenorOptions
{
    // Optional on purpose: unlike Mongo/JWT/LiveKit, a missing key should only disable GIF search
    // (handled per-request in TenorGifService), not fail the whole app's startup.
    public string ApiKey { get; init; } = string.Empty;

    public static TenorOptions FromConfiguration(IConfiguration configuration) => new()
    {
        ApiKey = configuration["TENOR_API_KEY"] ?? string.Empty,
    };
}
