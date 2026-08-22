using DiscordClone.Api.Common;
using DiscordClone.Api.Hubs;
using DiscordClone.Application.Auth;
using DiscordClone.Application.Servers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace DiscordClone.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IServerService _serverService;
    private readonly IHubContext<ChatHub> _hub;

    public AuthController(IAuthService authService, IServerService serverService, IHubContext<ChatHub> hub)
    {
        _authService = authService;
        _serverService = serverService;
        _hub = hub;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResult>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginOutcome>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("2fa/verify")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResult>> VerifyTwoFactor(VerifyTwoFactorRequest request, CancellationToken ct)
    {
        var result = await _authService.VerifyTwoFactorAsync(request, ct);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("2fa/setup")]
    public async Task<ActionResult<TwoFactorSetupResult>> SetupTwoFactor(CancellationToken ct)
    {
        var result = await _authService.SetupTwoFactorAsync(User.GetUserId(), ct);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("2fa/enable")]
    public async Task<ActionResult<EnableTwoFactorResult>> EnableTwoFactor(EnableTwoFactorRequest request, CancellationToken ct)
    {
        var result = await _authService.EnableTwoFactorAsync(User.GetUserId(), request, ct);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("2fa/disable")]
    public async Task<IActionResult> DisableTwoFactor(DisableTwoFactorRequest request, CancellationToken ct)
    {
        await _authService.DisableTwoFactorAsync(User.GetUserId(), request, ct);
        return NoContent();
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResult>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var result = await _authService.RefreshAsync(request.RefreshToken, ct);
        return Ok(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await _authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserProfile>> Me(CancellationToken ct)
    {
        var profile = await _authService.GetProfileAsync(User.GetUserId(), ct);
        return Ok(profile);
    }

    [Authorize]
    [HttpPatch("me")]
    public async Task<ActionResult<UserProfile>> UpdateMe(UpdateProfileRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var profile = await _authService.UpdateProfileAsync(userId, request, ct);

        var serverIds = await _serverService.GetMyServerIdsAsync(userId, ct);
        foreach (var serverId in serverIds)
        {
            await _hub.Clients.Group(ChatHub.PresenceGroup(serverId))
                .SendAsync("CustomStatusChanged", userId, profile.CustomStatusText, profile.CustomStatusEmoji, ct);
        }

        return Ok(profile);
    }
}
