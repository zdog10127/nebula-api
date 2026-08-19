namespace DiscordClone.Domain.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid AuthorId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<Guid> MentionedUserIds { get; set; } = [];
    public bool IsPinned { get; set; }
}
