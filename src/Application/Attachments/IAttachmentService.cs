namespace DiscordClone.Application.Attachments;

public interface IAttachmentService
{
    Task<AttachmentDto> UploadAsync(Guid userId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken ct);
    Task<string> UploadAvatarAsync(Guid userId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken ct);
    Task<string> UploadBannerAsync(Guid userId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken ct);
}
