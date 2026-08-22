namespace DiscordClone.Application.Storage;

public interface IStorageService
{
    Task EnsureBucketExistsAsync(CancellationToken ct);

    // contentDisposition is null for anything meant to render inline (images/video/audio,
    // as before); AttachmentService sets "attachment; filename=..." for everything else so
    // a browser navigating straight to the file's public URL always downloads it instead of
    // executing/rendering it — the actual fix for the fact that S3 serves whatever
    // content-type the uploader claims, which can't be trusted on its own.
    Task UploadAsync(string key, Stream content, string contentType, string? contentDisposition, CancellationToken ct);
    string GetPublicUrl(string key);
}
