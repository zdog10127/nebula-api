namespace DiscordClone.Domain.Entities;

public class ServerInvite
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int Uses { get; set; }

    public bool IsValid =>
        (ExpiresAt is null || DateTime.UtcNow < ExpiresAt) &&
        (MaxUses is null || Uses < MaxUses);
}
