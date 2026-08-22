using DiscordClone.Application.Music;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DiscordClone.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/music")]
public class MusicController : ControllerBase
{
    private readonly IMusicService _musicService;

    public MusicController(IMusicService musicService)
    {
        _musicService = musicService;
    }

    // Resolves either a pasted YouTube link or a typed search term (e.g. "artist - song") to
    // a specific video id + title, so the frontend can call ShareNowPlaying either way.
    [HttpGet("resolve")]
    public async Task<ActionResult<MusicResolveResult>> Resolve([FromQuery] string q, CancellationToken ct)
    {
        var result = await _musicService.ResolveAsync(q, ct);
        return Ok(result);
    }
}
