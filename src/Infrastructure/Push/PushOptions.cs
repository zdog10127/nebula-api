using Microsoft.Extensions.Configuration;

namespace DiscordClone.Infrastructure.Push;

public class PushOptions
{
    // Optional on purpose, same reasoning as TenorOptions: a missing key pair should only
    // disable push notifications (handled per-send in PushService), not fail app startup.
    public string PublicKey { get; init; } = string.Empty;
    public string PrivateKey { get; init; } = string.Empty;
    public string Subject { get; init; } = "mailto:admin@nebula.local";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);

    public static PushOptions FromConfiguration(IConfiguration configuration) => new()
    {
        PublicKey = configuration["VAPID_PUBLIC_KEY"] ?? string.Empty,
        PrivateKey = configuration["VAPID_PRIVATE_KEY"] ?? string.Empty,
    };
}
