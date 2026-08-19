namespace DiscordClone.Domain.Entities;

public class Server
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public string? IconUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}
