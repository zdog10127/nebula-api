namespace DiscordClone.Application.Storage;

public interface IStorageService
{
    Task EnsureBucketExistsAsync(CancellationToken ct);
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct);
    string GetPublicUrl(string key);
}
