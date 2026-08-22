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

            // Turning game-activity sharing off should stop showing "Jogando X" to
            // everyone right away, not just once the user's connection happens to drop.
            if (request.ShareActivityStatus == false)
                await _hub.Clients.Group(ChatHub.PresenceGroup(serverId)).SendAsync("ActivityChanged", userId, null, ct);
        }

        return Ok(profile);
    }

    [Authorize]
    [HttpPost("steam/link-start")]
    public async Task<ActionResult<SteamLinkStartResult>> StartSteamLink(CancellationToken ct)
    {
        var result = await _authService.StartSteamLinkAsync(User.GetUserId(), ct);
        return Ok(result);
    }

    // No [Authorize]: Steam redirects the user's browser here directly (a plain
    // top-level navigation, not a fetch with our JWT attached) once they finish
    // logging in on steamcommunity.com. The unauthenticated linkId query param (see
    // AuthService.StartSteamLinkAsync) is what ties this request back to a real user.
    [HttpGet("steam/callback")]
    public async Task<ContentResult> SteamCallback(CancellationToken ct)
    {
        var query = Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        var result = await _authService.CompleteSteamLinkAsync(query, ct);
        return Content(BuildSteamCallbackHtml(result), "text/html", System.Text.Encoding.UTF8);
    }

    [Authorize]
    [HttpPost("steam/unlink")]
    public async Task<IActionResult> UnlinkSteam(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var effectiveActivity = await _authService.UnlinkSteamAsync(userId, ct);

        var serverIds = await _serverService.GetMyServerIdsAsync(userId, ct);
        foreach (var serverId in serverIds)
            await _hub.Clients.Group(ChatHub.PresenceGroup(serverId)).SendAsync("ActivityChanged", userId, effectiveActivity, ct);

        return NoContent();
    }

    private static string BuildSteamCallbackHtml(SteamLinkCallbackResult result)
    {
        var accentColor = result.Success ? "#22d3ee" : "#f87171";
        var title = result.Success ? "Conta vinculada!" : "Não foi possível vincular";
        var message = System.Net.WebUtility.HtmlEncode(result.Message);

        return "<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\" />"
            + "<title>Nébula — Steam</title>"
            + "<style>body{background:#07080b;color:#e5e7eb;font-family:system-ui,sans-serif;"
            + "display:flex;align-items:center;justify-content:center;height:100vh;margin:0;}"
            + ".card{text-align:center;max-width:360px;padding:24px;}"
            + "h1{font-size:18px;margin-bottom:8px;color:" + accentColor + ";}"
            + "p{font-size:14px;color:#9ca3af;}</style></head>"
            + "<body><div class=\"card\"><h1>" + title + "</h1><p>" + message + "</p></div></body></html>";
    }
}
