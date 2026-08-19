using DiscordClone.Application.Auth;
using DiscordClone.Application.Common;
using DiscordClone.Domain.Entities;
using DiscordClone.Infrastructure.Persistence;
using MongoDB.Driver;

namespace DiscordClone.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly MongoContext _mongo;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthService(MongoContext mongo, IPasswordHasher passwordHasher, ITokenService tokenService)
    {
        _mongo = mongo;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            throw new AppException("Username must have at least 3 characters.");

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new AppException("Password must have at least 8 characters.");

        var exists = await _mongo.Users.Find(u => u.Username == username || u.Email == email).AnyAsync(ct);
        if (exists)
            throw new AppException("Username or email already in use.", 409);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim(),
            CreatedAt = DateTime.UtcNow,
        };

        await _mongo.Users.InsertOneAsync(user, cancellationToken: ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _mongo.Users.Find(u => u.Email == email).SingleOrDefaultAsync(ct);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new AppException("Invalid email or password.", 401);

        return await IssueTokensAsync(user, ct);
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

        if (updates.Count == 0)
            throw new AppException("Nothing to update.");

        var user = await _mongo.Users.FindOneAndUpdateAsync<User>(
            u => u.Id == userId,
            Builders<User>.Update.Combine(updates),
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
            ct) ?? throw new AppException("User not found.", 404);

        return ToProfile(user);
    }

    public async Task<PublicProfileDto> GetPublicProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await _mongo.Users.Find(u => u.Id == userId).SingleOrDefaultAsync(ct)
            ?? throw new AppException("User not found.", 404);

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
            user.CreatedAt);
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
        user.CustomStatusEmoji);

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
