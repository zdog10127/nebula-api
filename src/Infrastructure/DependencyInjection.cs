using DiscordClone.Application.Attachments;
using DiscordClone.Application.Auth;
using DiscordClone.Application.Gifs;
using DiscordClone.Application.Messages;
using DiscordClone.Application.Music;
using DiscordClone.Application.Presence;
using DiscordClone.Application.Push;
using DiscordClone.Application.Servers;
using DiscordClone.Application.Social;
using DiscordClone.Application.Steam;
using DiscordClone.Application.Storage;
using DiscordClone.Application.Voice;
using DiscordClone.Infrastructure.Attachments;
using DiscordClone.Infrastructure.Auth;
using DiscordClone.Infrastructure.Gifs;
using DiscordClone.Infrastructure.Messages;
using DiscordClone.Infrastructure.Music;
using DiscordClone.Infrastructure.Persistence;
using DiscordClone.Infrastructure.Presence;
using DiscordClone.Infrastructure.Push;
using DiscordClone.Infrastructure.Servers;
using DiscordClone.Infrastructure.Social;
using DiscordClone.Infrastructure.Steam;
using DiscordClone.Infrastructure.Storage;
using DiscordClone.Infrastructure.Voice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace DiscordClone.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(MongoOptions.FromConfiguration(configuration));
        services.AddSingleton<MongoContext>();

        var redisConnectionString = configuration["REDIS_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("REDIS_CONNECTION_STRING is not configured.");

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddSingleton<IPresenceService, PresenceService>();
        services.AddSingleton<IVoicePresenceService, VoicePresenceService>();

        services.AddSingleton(JwtOptions.FromConfiguration(configuration));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<TotpSecretProtector>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IServerService, ServerService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<ISocialService, SocialService>();

        services.AddSingleton(LiveKitOptions.FromConfiguration(configuration));
        services.AddScoped<IVoiceService, VoiceService>();

        services.AddSingleton(S3Options.FromConfiguration(configuration));
        services.AddSingleton<IStorageService, S3StorageService>();
        services.AddScoped<IAttachmentService, AttachmentService>();

        services.AddSingleton(TenorOptions.FromConfiguration(configuration));
        services.AddHttpClient<IGifService, TenorGifService>();

        services.AddHttpClient<IMusicService, YoutubeMusicService>();

        services.AddSingleton(PushOptions.FromConfiguration(configuration));
        services.AddScoped<IPushService, PushService>();

        // Both optional (see SteamOptions) — a deploy without STEAM_API_KEY/PUBLIC_API_URL
        // just has Steam linking disabled, it doesn't fail to start. The actual
        // AddHostedService<SteamActivityPollingService>() registration lives in Program.cs
        // instead of here: that type needs ChatHub, which Infrastructure can't reference.
        services.AddSingleton(SteamOptions.FromConfiguration(configuration));
        services.AddHttpClient<ISteamOpenIdService, SteamOpenIdService>();
        services.AddHttpClient<ISteamApiClient, SteamApiClient>();

        return services;
    }
}
