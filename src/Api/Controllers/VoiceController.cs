using DiscordClone.Api.Common;
using DiscordClone.Application.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DiscordClone.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/channels/{channelId:guid}/voice")]
public class VoiceController : ControllerBase
{
    private readonly IVoiceService _voiceService;

    public VoiceController(IVoiceService voiceService)
    {
        _voiceService = voiceService;
    }

    [HttpPost("token")]
    public async Task<ActionResult<VoiceTokenResult>> GetToken(Guid channelId, CancellationToken ct)
    {
        var result = await _voiceService.GetJoinTokenAsync(User.GetUserId(), channelId, ct);
        return Ok(result);
    }

    [HttpGet("now-playing")]
    public async Task<ActionResult<NowPlayingDto?>> GetNowPlaying(Guid channelId, CancellationToken ct)
    {
        var result = await _voiceService.GetNowPlayingAsync(channelId, ct);
        return Ok(result);
    }
}
