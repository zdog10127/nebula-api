namespace DiscordClone.Domain.Entities;

public class DmMessage
{
    public Guid Id { get; set; }
    public Guid DmChannelId { get; set; }
    public Guid AuthorId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
