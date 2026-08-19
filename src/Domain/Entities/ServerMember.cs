namespace DiscordClone.Domain.Entities;

public class ServerMember
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid UserId { get; set; }
    public string? Nickname { get; set; }
    public List<Guid> RoleIds { get; set; } = [];
    public DateTime JoinedAt { get; set; }
}
