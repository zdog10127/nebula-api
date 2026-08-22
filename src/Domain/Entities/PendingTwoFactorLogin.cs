namespace DiscordClone.Domain.Entities;

/// <summary>
/// A short-lived bridge between "password verified" and "fully logged in" for accounts
/// with 2FA enabled: created by AuthService.LoginAsync once the password checks out,
/// consumed by AuthService.VerifyTwoFactorAsync once the TOTP/recovery code checks out.
/// Auto-expires via a Mongo TTL index on ExpiresAt (see MongoIndexInitializer) so an
/// abandoned login attempt doesn't linger in the database.
/// </summary>
public class PendingTwoFactorLogin
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
