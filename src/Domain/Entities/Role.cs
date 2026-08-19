using DiscordClone.Domain.Enums;

namespace DiscordClone.Domain.Entities;

public class Role
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#99aab5";
    public ServerPermission Permissions { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; }
}
