using System.Text.RegularExpressions;
using DiscordClone.Application.Auth;
using DiscordClone.Application.Common;
using DiscordClone.Application.Presence;
using DiscordClone.Application.Steam;
using DiscordClone.Domain.Entities;
using DiscordClone.Infrastructure.Persistence;
using DiscordClone.Infrastructure.Steam;
using MongoDB.Driver;

namespace DiscordClone.Infrastructure.Auth;

public partial class AuthService : IAuthService
{
    // Deliberately permissive — the only goal is to catch obviously malformed input
    // client-side validation didn't; the real proof an email address works is the
    // person receiving mail there, which no regex can verify anyway.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    // Mirrors the kind of handle Discord itself allows — letters, digits, underscore,
    // period, hyphen — so a username can't smuggle in whitespace, emoji, or Unicode
    // bidi/override control characters that could be used to spoof another user's name.
    [GeneratedRegex(@"^[a-zA-Z0-9_.-]{3,32}$")]
    private static partial Regex UsernamePattern();

    // bcrypt (see PasswordHasher) silently truncates at 72 bytes — anything past that
    // is ignored when hashing, so two different passwords sharing the same first 72
    // characters would verify as equal. Rejecting long input up front avoids that trap
    // entirely rather than relying on people never noticing.
    private const int MaxPasswordLength = 72;
    private const int MaxDisplayNameLength = 64;

    // How long a "password verified, waiting for the 2FA code" challenge stays valid.
    // Short enough that a stolen/leaked login token is useless a few minutes later;
    // the Mongo TTL index on PendingTwoFactorLogin.ExpiresAt (see MongoIndexInitializer)
    // is what actually reclaims the document once it passes.
    private const int PendingTwoFactorLoginMinutes = 5;

    // How long a "redirected to Steam, waiting for them to come back" link attempt
    // stays valid. Mirrors PendingTwoFactorLoginMinutes above; the Mongo TTL index on
    // PendingSteamLink.ExpiresAt (see MongoIndexInitializer) reclaims it either way.
    private const int PendingSteamLinkMinutes = 10;

    private readonly MongoContext _mongo;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly TotpSecretProtector _totpProtector;
    private readonly IPresenceService _presenceService;
    private readonly ISteamOpenIdService _steamOpenId;
    private readonly SteamOptions _steamOptions;

    public AuthService(
        MongoContext mongo,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        TotpSecretProtector totpProtector,
        IPresenceService presenceService,
        ISteamOpenIdService steamOpenId,
        SteamOptions steamOptions)
    {
        _presenceService = presenceService;
        _mongo = mongo;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _totpProtector = totpProtector;
        _steamOpenId = steamOpenId;
        _steamOptions = steamOptions;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (!UsernamePattern().IsMatch(username))
            throw new AppException("Username must be 3-32 characters and contain only letters, numbers, '.', '_' or '-'.");

        if (email.Length > 254 || !EmailPattern().IsMatch(email))
            throw new AppException("Enter a valid email address.");

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new AppException("Password must have at least 8 characters.");

        if (request.Password.Length > MaxPasswordLength)
            throw new AppException($"Password must be at most {MaxPasswordLength} characters.");

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim();
        if (displayName.Length is < 1 or > MaxDisplayNameLength)
            throw new AppException($"Display name must have between 1 and {MaxDisplayNameLength} characters.");

        var exists = await _mongo.Users.Find(u => u.Username == username || u.Email == email).AnyAsync(ct);
        if (exists)
            throw new AppException("Username or email already in use.", 409);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        };

        try
        {
            await _mongo.Users.InsertOneAsync(user, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Belt-and-suspenders for the race between the AnyAsync check above and this
            // insert: two concurrent registrations for the same username/email can both
            // pass the check, but the unique index (see MongoIndexInitializer) rejects the
            // second insert. Without this catch that surfaces as a raw 500, not the same
            // clean 409 a non-concurrent duplicate gets above.
            throw new AppException("Username or email already in use.", 409);
        }

        return await IssueTokensAsync(user, ct);
    }

    public async Task<LoginOutcome> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _mongo.Users.Find(u => u.Email == email).SingleOrDefaultAsync(ct);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new AppException("Invalid email or password.", 401);

        if (user.TotpEnabled)
        {
            // Password is correct, but this account opted into 2FA: hold off on issuing
            // real tokens until VerifyTwoFactorAsync confirms the second factor too.
            var pending = new PendingTwoFactorLogin
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(PendingTwoFactorLoginMinutes),
            };
            await _mongo.PendingTwoFactorLogins.InsertOneAsync(pending, cancellationToken: ct);

            return new LoginOutcome(true, pending.Id.ToString(), null);
        }

        var result = await IssueTokensAsync(user, ct);
        return new LoginOutcome(false, null, result);
    }

    public async Task<AuthResult> VerifyTwoFactorAsync(VerifyTwoFactorRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.LoginToken, out var pendingId))
            throw new AppException("Invalid or expired login challenge.", 401);

        var pending = await _mongo.PendingTwoFactorLogins.Find(p => p.Id == pendingId).SingleOrDefaultAsync(ct);
        if (pending is null || pending.ExpiresAt < DateTime.UtcNow)
            throw new AppException("Invalid or expired login challenge.", 401);

        var user = await _mongo.Users.Find(u => u.Id == pending.UserId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("User not found.", 404);

        var code = request.Code?.Trim() ?? string.Empty;
        var accepted = user.TotpEnabled
            && !string.IsNullOrEmpty(user.TotpSecret)
            && TotpService.Validate(_totpProtector.Decrypt(user.TotpSecret), code);

        if (!accepted)
            accepted = await TryConsumeRecoveryCodeAsync(user, code, ct);

        if (!accepted)
            throw new AppException("Invalid verification code.", 401);

        // Spend this challenge whether it succeeded via TOTP or a recovery code, so a
        // leaked/replayed login token can't be reused for a second login.
        await _mongo.PendingTwoFactorLogins.DeleteOneAsync(p => p.Id == pendingId, ct);

        return await IssueTokensAsync(user, ct);
    }

    private async Task<bool> TryConsumeRecoveryCodeAsync(User user, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || user.TotpRecoveryCodeHashes.Count == 0)
            return false;

        var normalized = code.Trim().ToUpperInvariant();
        var matchedHash = user.TotpRecoveryCodeHashes.FirstOrDefault(h => _passwordHasher.Verify(normalized, h));
        if (matchedHash is null)
            return false;

        var update = Builders<User>.Update.Pull(u => u.TotpRecoveryCodeHashes, matchedHash);
        await _mongo.Users.UpdateOneAsync(u => u.Id == user.Id, update, cancellationToken: ct);
        return true;
    }

    public async Task<TwoFactorSetupResult> SetupTwoFactorAsync(Guid userId, CancellationToken ct)
    {
        var user = await _mongo.Users.Find(u => u.Id == userId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("User not found.", 404);

        if (user.TotpEnabled)
            throw new AppException("Two-factor authentication is already enabled.");

        // Generating a fresh secret here (even if setup was already called once before
        // without being confirmed via EnableTwoFactorAsync) is safe: TotpEnabled only
        // flips to true once the user proves possession of the new secret.
        var secret = TotpService.GenerateSecret();
        var update = Builders<User>.Update.Set(u => u.TotpSecret, _totpProtector.Encrypt(secret));
        await _mongo.Users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);

        var otpAuthUri = TotpService.BuildOtpAuthUri(secret, user.Email, "Nebula");
        return new TwoFactorSetupResult(secret, otpAuthUri);
    }

    public async Task<EnableTwoFactorResult> EnableTwoFactorAsync(Guid userId, EnableTwoFactorRequest request, CancellationToken ct)
    {
        var user = await _mongo.Users.Find(u => u.Id == userId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("User not found.", 404);

        if (user.TotpEnabled)
            throw new AppException("Two-factor authentication is already enabled.");

        if (string.IsNullOrEmpty(user.TotpSecret))
            throw new AppException("Call setup before enabling two-factor authentication.");

        var secret = _totpProtector.Decrypt(user.TotpSecret);
        if (!TotpService.Validate(secret, request.Code))
            throw new AppException("Invalid verification code.");

        var recoveryCodes = RecoveryCodeGenerator.Generate();
        var recoveryHashes = recoveryCodes.Select(c => _passwordHasher.Hash(c)).ToList();

        var update = Builders<User>.Update
            .Set(u => u.TotpEnabled, true)
            .Set(u => u.TotpRecoveryCodeHashes, recoveryHashes);
        await _mongo.Users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);

        return new EnableTwoFactorResult(recoveryCodes);
    }

    public async Task DisableTwoFactorAsync(Guid userId, DisableTwoFactorRequest request, CancellationToken ct)
    {
        var user = await _mongo.Users.Find(u => u.Id == userId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("User not found.", 404);

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new AppException("Incorrect password.", 401);

        var update = Builders<User>.Update
            .Set(u => u.TotpEnabled, false)
            .Set(u => u.TotpSecret, null)
            .Set(u => u.TotpRecoveryCodeHashes, new List<string>());
        await _mongo.Users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = _tokenService.HashRefreshToken(refreshToken);
        var stored = await _mongo.RefreshTokens.Find(rt => rt.TokenHash == hash).SingleOrDefaultAsync(ct);

        if (stored is null || !stored.IsActive)
            throw new AppException("Invalid or expired refresh token.", 401);

        var user = await _mongo.Users.Find(u => u.Id == stored.UserId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("User not found.", 404);

        var (result, newTokenDoc) = await IssueTokensWithDocAsync(user, ct);

        var update = Builders<RefreshToken>.Update
            .Set(rt => rt.RevokedAt, DateTime.UtcNow)
            .Set(rt => rt.ReplacedByTokenId, newTokenDoc.Id);

        await _mongo.RefreshTokens.UpdateOneAsync(rt => rt.Id == stored.Id, update, cancellationToken: ct);

        return result;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = _tokenService.HashRefreshToken(refreshToken);
        var stored = await _mongo.RefreshTokens.Find(rt => rt.TokenHash == hash).SingleOrDefaultAsync(ct);

        if (stored is null || !stored.IsActive)
            return;

        var update = Builders<RefreshToken>.Update.Set(rt => rt.RevokedAt, DateTime.UtcNow);
        await _mongo.RefreshTokens.UpdateOneAsync(rt => rt.Id == stored.Id, update, cancellationToken: ct);
    }

    public async Task<UserProfile> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await _mongo.Users.Find(u => u.Id == userId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("User not found.", 404);

        return ToProfile(user);
    }

    public async Task<UserProfile> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var updates = new List<UpdateDefinition<User>>();

        if (request.DisplayName is not null)
        {
            var trimmed = request.DisplayName.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length is < 1 or > 64)
                throw new AppException("Display name must have between 1 and 64 characters.");
            updates.Add(Builders<User>.Update.Set(u => u.DisplayName, trimmed));
        }

        if (request.Bio is not null)
        {
            var trimmed = request.Bio.Trim();
            if (trimmed.Length > 190)
                throw new AppException("Bio must be at most 190 characters.");
            updates.Add(Builders<User>.Update.Set(u => u.Bio, trimmed.Length == 0 ? null : trimmed));
        }

        if (request.Pronouns is not null)
        {
            var trimmed = request.Pronouns.Trim();
            if (trimmed.Length > 40)
                throw new AppException("Pronouns must be at most 40 characters.");
            updates.Add(Builders<User>.Update.Set(u => u.Pronouns, trimmed.Length == 0 ? null : trimmed));
        }

        if (request.BannerColor is not null)
        {
            var trimmed = request.BannerColor.Trim();
            if (trimmed.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^#[0-9a-fA-F]{6}$"))
                throw new AppException("Banner color must be a hex value like #22d3ee.");
            updates.Add(Builders<User>.Update.Set(u => u.BannerColor, trimmed.Length == 0 ? null : trimmed));
        }

        if (request.CustomStatusText is not null)
        {
            var trimmed = request.CustomStatusText.Trim();
            if (trimmed.Length > 128)
                throw new AppException("Status must be at most 128 characters.");
            updates.Add(Builders<User>.Update.Set(u => u.CustomStatusText, trimmed.Length == 0 ? null : trimmed));
        }

        if (request.CustomStatusEmoji is not null)
        {
            var trimmed = request.CustomStatusEmoji.Trim();
            updates.Add(Builders<User>.Update.Set(u => u.CustomStatusEmoji, trimmed.Length == 0 ? null : trimmed));
        }

        if (request.ShareActivityStatus is not null)
            updates.Add(Builders<User>.Update.Set(u => u.ShareActivityStatus, request.ShareActivityStatus.Value));

        if (updates.Count == 0)
            throw new AppException("Nothing to update.");

        var user = await _mongo.Users.FindOneAndUpdateAsync<User>(
            u => u.Id == userId,
            Builders<User>.Update.Combine(updates),
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
            ct) ?? throw new AppException("User not found.", 404);

        if (request.ShareActivityStatus == false)
        {
            // Wipe whatever was already broadcast, immediately — otherwise "Jogando X"
            // would keep showing to others until the user's connection happened to drop.
            // Both sources: a locally-detected activity and a Steam-polled one (see
            // PresenceService.GetActivitiesAsync, which checks Steam first) — leaving
            // either one behind would let it leak back into view.
            await _presenceService.SetActivityAsync(userId, null, ct);
            await _presenceService.SetSteamActivityAsync(userId, null, ct);
        }

        return ToProfile(user);
    }

    public async Task<PublicProfileDto> GetPublicProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await _mongo.Users.Find(u => u.Id == userId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("User not found.", 404);

        var activity = user.ShareActivityStatus ? await _presenceService.GetActivityAsync(userId, ct) : null;

        return new PublicProfileDto(
            user.Id,
            user.Username,
            user.DisplayName,
            user.AvatarUrl,
            user.BannerUrl,
            user.BannerColor,
            user.Bio,
            user.Pronouns,
            user.CustomStatusText,
            user.CustomStatusEmoji,
            user.CreatedAt,
            activity);
    }

    private static UserProfile ToProfile(User user) => new(
        user.Id,
        user.Username,
        user.Email,
        user.DisplayName,
        user.AvatarUrl,
        user.BannerUrl,
        user.BannerColor,
        user.Bio,
        user.Pronouns,
        user.CustomStatusText,
        user.CustomStatusEmoji,
        user.TotpEnabled,
        user.ShareActivityStatus,
        user.SteamId64 is not null);

    public async Task<SteamLinkStartResult> StartSteamLinkAsync(Guid userId, CancellationToken ct)
    {
        if (!_steamOptions.IsConfigured)
            throw new AppException("A integração com a Steam não está configurada neste servidor.", 503);

        var pending = new PendingSteamLink
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(PendingSteamLinkMinutes),
        };
        await _mongo.PendingSteamLinks.InsertOneAsync(pending, cancellationToken: ct);

        // linkId rides through Steam's redirect untouched alongside its own openid.*
        // params (Steam only appends to return_to, never strips existing query
        // params), which is what lets CompleteSteamLinkAsync find this pending link
        // back on an otherwise-anonymous callback request.
        var returnTo = $"{_steamOptions.PublicApiUrl}/api/auth/steam/callback?linkId={pending.Id}";
        var redirectUrl = _steamOpenId.BuildLoginRedirectUrl(returnTo, _steamOptions.PublicApiUrl);
        return new SteamLinkStartResult(redirectUrl);
    }

    public async Task<SteamLinkCallbackResult> CompleteSteamLinkAsync(IReadOnlyDictionary<string, string> callbackQuery, CancellationToken ct)
    {
        const string GenericFailureMessage = "Não foi possível vincular sua conta Steam. Feche esta aba e tente novamente.";

        if (!callbackQuery.TryGetValue("linkId", out var linkIdRaw) || !Guid.TryParse(linkIdRaw, out var linkId))
            return new SteamLinkCallbackResult(false, GenericFailureMessage);

        var pending = await _mongo.PendingSteamLinks.Find(p => p.Id == linkId).SingleOrDefaultAsync(ct);
        if (pending is null || pending.ExpiresAt < DateTime.UtcNow)
            return new SteamLinkCallbackResult(false, "Esse link expirou. Feche esta aba e tente vincular sua conta Steam de novo.");

        var steamId64 = await _steamOpenId.VerifyAndExtractSteamId64Async(callbackQuery, ct);
        if (steamId64 is null)
            return new SteamLinkCallbackResult(false, GenericFailureMessage);

        var update = Builders<User>.Update.Set(u => u.SteamId64, steamId64);
        try
        {
            await _mongo.Users.UpdateOneAsync(u => u.Id == pending.UserId, update, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The unique+sparse index on User.SteamId64 (see MongoIndexInitializer)
            // caught this — that Steam account is already linked to a different
            // Nébula account.
            return new SteamLinkCallbackResult(false, "Essa conta Steam já está vinculada a outro usuário do Nébula.");
        }

        await _mongo.PendingSteamLinks.DeleteOneAsync(p => p.Id == linkId, ct);

        // No immediate hub broadcast here — SteamActivityPollingService picks the new
        // link up on its next tick (within ~60s), same as any other Steam activity
        // change. This endpoint has no authenticated connection to broadcast through
        // anyway: it's a plain, unauthenticated redirect from Steam's servers.
        return new SteamLinkCallbackResult(true, "Conta Steam vinculada! Pode fechar esta aba e voltar pro Nébula.");
    }

    public async Task<string?> UnlinkSteamAsync(Guid userId, CancellationToken ct)
    {
        var update = Builders<User>.Update.Set(u => u.SteamId64, null);
        await _mongo.Users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
        await _presenceService.SetSteamActivityAsync(userId, null, ct);

        // A locally-detected (Electron) activity may still be running even after
        // unlinking Steam — recompute rather than assuming null, so the caller
        // broadcasts the right thing instead of wiping a still-valid activity.
        return await _presenceService.GetActivityAsync(userId, ct);
    }

    private async Task<AuthResult> IssueTokensAsync(User user, CancellationToken ct)
    {
        var (result, _) = await IssueTokensWithDocAsync(user, ct);
        return result;
    }

    private async Task<(AuthResult Result, RefreshToken TokenDoc)> IssueTokensWithDocAsync(User user, CancellationToken ct)
    {
        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        var tokenDoc = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = _tokenService.HashRefreshToken(refreshToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_tokenService.RefreshTokenLifetime),
        };

        await _mongo.RefreshTokens.InsertOneAsync(tokenDoc, cancellationToken: ct);

        var result = new AuthResult(user.Id, user.Username, user.Email, user.DisplayName, accessToken, expiresAt, refreshToken);
        return (result, tokenDoc);
    }
}
