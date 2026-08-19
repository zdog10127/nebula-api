namespace DiscordClone.Domain.Entities;

public class CustomEmoji
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
