namespace DiscordClone.Domain.Entities;

/// <summary>
/// Bridges Steam's OpenID callback — a plain, unauthenticated browser redirect that
/// carries no JWT — back to the Nébula user who started the link flow. Mirrors
/// PendingTwoFactorLogin exactly: created by AuthService.StartSteamLinkAsync, consumed
/// by AuthService.CompleteSteamLinkAsync, auto-expires via the Mongo TTL index on
/// ExpiresAt (see MongoIndexInitializer) if the user never finishes logging into Steam.
/// </summary>
public class PendingSteamLink
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
