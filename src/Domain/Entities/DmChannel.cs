namespace DiscordClone.Domain.Entities;

// Same UserAId < UserBId normalization as Friendship — one DM channel per pair of users.
// 1:1 only, no group DMs.
public class DmChannel
{
    public Guid Id { get; set; }
    public Guid UserAId { get; set; }
    public Guid UserBId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
}
