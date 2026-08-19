using Microsoft.Extensions.Configuration;

namespace DiscordClone.Infrastructure.Voice;

public class LiveKitOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;

    public static LiveKitOptions FromConfiguration(IConfiguration configuration) => new()
    {
        ApiKey = configuration["LIVEKIT_API_KEY"] ?? throw new InvalidOperationException("LIVEKIT_API_KEY is not configured."),
        ApiSecret = configuration["LIVEKIT_API_SECRET"] ?? throw new InvalidOperationException("LIVEKIT_API_SECRET is not configured."),
        Url = configuration["LIVEKIT_URL"] ?? throw new InvalidOperationException("LIVEKIT_URL is not configured."),
    };
}
