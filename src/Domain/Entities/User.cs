namespace DiscordClone.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? BannerColor { get; set; }
    public string? Bio { get; set; }
    public string? Pronouns { get; set; }
    public string? CustomStatusText { get; set; }
    public string? CustomStatusEmoji { get; set; }
    public DateTime CreatedAt { get; set; }
}
