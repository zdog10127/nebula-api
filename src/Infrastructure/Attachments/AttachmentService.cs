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

    // Extensions that are never safe to let anyone else's browser open directly, no matter
    // what content-type the uploader's client claimed — executables/scripts obviously, plus
    // markup types (.html/.htm/.svg/.xhtml) that can carry a <script> and run it if someone
    // navigates straight to the file's public S3 URL (e.g. the "open in new tab" link on a
    // non-image attachment).
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".msi", ".bat", ".cmd", ".com", ".scr", ".vbs", ".vbe", ".ps1", ".psm1",
        ".js", ".jse", ".jar", ".sh", ".wsf", ".hta", ".reg", ".apk",
        ".html", ".htm", ".xhtml", ".svg",
    };

    private static readonly HashSet<string> BlockedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/html", "application/xhtml+xml", "image/svg+xml",
        "application/x-msdownload", "application/x-msdos-program",
    };

    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/gif",
    };

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif",
    };

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
        ValidateNotDangerous(fileName, contentType);

        var key = $"attachments/{userId}/{Guid.NewGuid()}-{SanitizeFileName(fileName)}";
        await _storage.UploadAsync(key, content, contentType, InlineDispositionOrDownload(fileName, contentType), ct);

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
        ValidateIsImage(fileName, contentType);

        var exists = await _mongo.Users.Find(u => u.Id == userId).AnyAsync(ct);
        if (!exists)
            throw new AppException("User not found.", 404);

        var key = $"avatars/{userId}/{Guid.NewGuid()}-{SanitizeFileName(fileName)}";
        await _storage.UploadAsync(key, content, contentType, null, ct);

        var url = _storage.GetPublicUrl(key);
        var update = Builders<User>.Update.Set(u => u.AvatarUrl, url);
        await _mongo.Users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);

        return url;
    }

    public async Task<string> UploadBannerAsync(Guid userId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken ct)
    {
        ValidateSize(sizeBytes);
        ValidateIsImage(fileName, contentType);

        var exists = await _mongo.Users.Find(u => u.Id == userId).AnyAsync(ct);
        if (!exists)
            throw new AppException("User not found.", 404);

        var key = $"banners/{userId}/{Guid.NewGuid()}-{SanitizeFileName(fileName)}";
        await _storage.UploadAsync(key, content, contentType, null, ct);

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

    // General attachments: anything goes except the specific extensions/content-types that
    // could execute as code or script in a browser — see BlockedExtensions/BlockedContentTypes.
    private static void ValidateNotDangerous(string fileName, string contentType)
    {
        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(ext) && BlockedExtensions.Contains(ext))
            throw new AppException($"Files of type '{ext}' are not allowed.");

        if (BlockedContentTypes.Contains(contentType))
            throw new AppException("This file type is not allowed.");
    }

    // Avatars/banners: the opposite policy — only known-safe raster image types, checking
    // both the extension and the claimed content-type so one lying about the other doesn't
    // slip through (e.g. a .svg renamed to .png still has contentType image/svg+xml, and a
    // real .svg renamed to .png still has that extension check to catch it too).
    private static void ValidateIsImage(string fileName, string contentType)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedImageExtensions.Contains(ext) || !AllowedImageContentTypes.Contains(contentType))
            throw new AppException("Only PNG, JPG, GIF or WEBP images are allowed.");
    }

    // Images/video/audio keep rendering inline in chat/profile the way they always have;
    // everything else gets forced to download instead of opening directly in a browser tab
    // (see IStorageService.UploadAsync) — the actual mitigation for content-type spoofing,
    // independent of the extension/content-type checks above.
    private static string? InlineDispositionOrDownload(string fileName, string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return null;

        return $"attachment; filename=\"{SanitizeFileName(fileName)}\"";
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(fileName.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "file" : clean;
    }
}
