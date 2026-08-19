namespace DiscordClone.Domain.Entities;

public class ServerBan
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid UserId { get; set; }
    public Guid BannedByUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
