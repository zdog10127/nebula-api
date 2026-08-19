using DiscordClone.Api.Common;
using DiscordClone.Application.Push;
using DiscordClone.Infrastructure.Push;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DiscordClone.Api.Controllers;

[ApiController]
[Route("api/push")]
public class PushController : ControllerBase
{
    private readonly IPushService _push;
    private readonly PushOptions _options;

    public PushController(IPushService push, PushOptions options)
    {
        _push = push;
        _options = options;
    }

    [HttpGet("vapid-public-key")]
    public ActionResult<object> GetVapidPublicKey()
    {
        return Ok(new { publicKey = _options.IsConfigured ? _options.PublicKey : null });
    }

    [Authorize]
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(PushSubscribeRequest request, CancellationToken ct)
    {
        await _push.SubscribeAsync(User.GetUserId(), request, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest request, CancellationToken ct)
    {
        await _push.UnsubscribeAsync(User.GetUserId(), request.Endpoint, ct);
        return NoContent();
    }
}

public record UnsubscribeRequest(string Endpoint);
