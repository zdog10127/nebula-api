namespace DiscordClone.Domain.Enums;

[Flags]
public enum ServerPermission : long
{
    None = 0,
    ManageServer = 1L << 0,
    ManageRoles = 1L << 1,
    ManageChannels = 1L << 2,
    KickMembers = 1L << 3,
    BanMembers = 1L << 4,
    CreateInvite = 1L << 5,
    ManageMessages = 1L << 6,
    ManageEmojis = 1L << 7,
}
