using Microsoft.Extensions.Configuration;

namespace DiscordClone.Infrastructure.Storage;

public class S3Options
{
    public string Endpoint { get; init; } = string.Empty;
    public string PublicEndpoint { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string Bucket { get; init; } = string.Empty;

    public static S3Options FromConfiguration(IConfiguration configuration) => new()
    {
        Endpoint = configuration["S3_ENDPOINT"] ?? throw new InvalidOperationException("S3_ENDPOINT is not configured."),
        PublicEndpoint = configuration["S3_PUBLIC_ENDPOINT"] ?? configuration["S3_ENDPOINT"] ?? throw new InvalidOperationException("S3_ENDPOINT is not configured."),
        AccessKey = configuration["S3_ACCESS_KEY"] ?? throw new InvalidOperationException("S3_ACCESS_KEY is not configured."),
        SecretKey = configuration["S3_SECRET_KEY"] ?? throw new InvalidOperationException("S3_SECRET_KEY is not configured."),
        Bucket = configuration["S3_BUCKET"] ?? throw new InvalidOperationException("S3_BUCKET is not configured."),
    };
}
