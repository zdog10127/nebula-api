namespace DiscordClone.Domain.Entities;

// UserAId/UserBId are always stored with UserAId < UserBId (string comparison of the
// Guid) so a friendship between two users is a single row regardless of who sent the
// original request, and duplicate-pair inserts are rejected by a unique index.
public class Friendship
{
    public Guid Id { get; set; }
    public Guid UserAId { get; set; }
    public Guid UserBId { get; set; }
    public DateTime CreatedAt { get; set; }
}
