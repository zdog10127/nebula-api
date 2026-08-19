using DiscordClone.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DiscordClone.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IAuthService _authService;

    public UsersController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<PublicProfileDto>> GetProfile(Guid userId, CancellationToken ct)
    {
        var result = await _authService.GetPublicProfileAsync(userId, ct);
        return Ok(result);
    }
}
