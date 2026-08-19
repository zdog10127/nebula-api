namespace DiscordClone.Domain.Entities;

public class PushSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dhKey { get; set; } = string.Empty;
    public string AuthKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
