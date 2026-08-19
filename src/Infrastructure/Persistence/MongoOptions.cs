using Microsoft.Extensions.Configuration;

namespace DiscordClone.Infrastructure.Persistence;

public class MongoOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;

    public static MongoOptions FromConfiguration(IConfiguration configuration) => new()
    {
        ConnectionString = configuration["MONGODB_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("MONGODB_CONNECTION_STRING is not configured."),
        DatabaseName = configuration["MONGODB_DATABASE"] ?? "discordclone",
    };
}
