namespace DiscordClone.Domain.Entities;

public class FriendRequest
{
    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
