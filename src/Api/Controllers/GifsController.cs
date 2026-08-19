using DiscordClone.Application.Gifs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DiscordClone.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/gifs")]
public class GifsController : ControllerBase
{
    private readonly IGifService _gifService;

    public GifsController(IGifService gifService)
    {
        _gifService = gifService;
    }

    [HttpGet("search")]
    public async Task<ActionResult<GifSearchResult>> Search([FromQuery] string q, [FromQuery] string? pos, CancellationToken ct)
    {
        var result = await _gifService.SearchAsync(q, pos, ct);
        return Ok(result);
    }

    [HttpGet("trending")]
    public async Task<ActionResult<GifSearchResult>> Trending([FromQuery] string? pos, CancellationToken ct)
    {
        var result = await _gifService.GetTrendingAsync(pos, ct);
        return Ok(result);
    }
}
