namespace DiscordClone.Domain.Entities;

public class Attachment
{
    public Guid Id { get; set; }
    public Guid UploaderId { get; set; }
    public Guid? MessageId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
