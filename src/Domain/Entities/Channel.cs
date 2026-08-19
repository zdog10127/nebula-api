using DiscordClone.Domain.Enums;

namespace DiscordClone.Domain.Entities;

public class Channel
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChannelType Type { get; set; }
    public Guid? CategoryId { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; }
}
