using DiscordClone.Api.Common;
using DiscordClone.Api.Hubs;
using DiscordClone.Application.Auth;
using DiscordClone.Application.Servers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    public async Task<ActionResult<AuthResult>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResult>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("refresh")]
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
