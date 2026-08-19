using DiscordClone.Application.Attachments;
using DiscordClone.Application.Common;
using DiscordClone.Application.Storage;
using DiscordClone.Domain.Entities;
using DiscordClone.Infrastructure.Persistence;
using MongoDB.Driver;

namespace DiscordClone.Infrastructure.Attachments;

public class AttachmentService : IAttachmentService
{
    private const long MaxFileSizeBytes = 25 * 1024 * 1024;

    private readonly MongoContext _mongo;
    private readonly IStorageService _storage;

    public AttachmentService(MongoContext mongo, IStorageService storage)
    {
        _mongo = mongo;
        _storage = storage;
    }

    public async Task<AttachmentDto> UploadAsync(Guid userId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken ct)
    {
        ValidateSize(sizeBytes);

        var key = $"attachments/{userId}/{Guid.NewGuid()}-{SanitizeFileName(fileName)}";
        await _storage.UploadAsync(key, content, contentType, ct);

        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            UploaderId = userId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StorageKey = key,
            CreatedAt = DateTime.UtcNow,
        };

        await _mongo.Attachments.InsertOneAsync(attachment, cancellationToken: ct);

        return new AttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, _storage.GetPublicUrl(key));
    }

    public async Task<string> UploadAvatarAsync(Guid userId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken ct)
    {
        ValidateSize(sizeBytes);

        var exists = await _mongo.Users.Find(u => u.Id == userId).AnyAsync(ct);
        if (!exists)
            throw new AppException("User not found.", 404);

        var key = $"avatars/{userId}/{Guid.NewGuid()}-{SanitizeFileName(fileName)}";
        await _storage.UploadAsync(key, content, contentType, ct);

        var url = _storage.GetPublicUrl(key);
        var update = Builders<User>.Update.Set(u => u.AvatarUrl, url);
        await _mongo.Users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);

        return url;
    }

    public async Task<string> UploadBannerAsync(Guid userId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken ct)
    {
        ValidateSize(sizeBytes);

        var exists = await _mongo.Users.Find(u => u.Id == userId).AnyAsync(ct);
        if (!exists)
            throw new AppException("User not found.", 404);

        var key = $"banners/{userId}/{Guid.NewGuid()}-{SanitizeFileName(fileName)}";
        await _storage.UploadAsync(key, content, contentType, ct);

        var url = _storage.GetPublicUrl(key);
        var update = Builders<User>.Update.Set(u => u.BannerUrl, url);
        await _mongo.Users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);

        return url;
    }

    private static void ValidateSize(long sizeBytes)
    {
        if (sizeBytes <= 0 || sizeBytes > MaxFileSizeBytes)
            throw new AppException($"File size must be between 1 byte and {MaxFileSizeBytes / (1024 * 1024)}MB.");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(fileName.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "file" : clean;
    }
}
