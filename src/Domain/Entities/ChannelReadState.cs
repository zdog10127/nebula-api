namespace DiscordClone.Domain.Entities;

public class ChannelReadState
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ChannelId { get; set; }
    public DateTime LastReadAt { get; set; }
}
