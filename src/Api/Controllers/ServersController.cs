using DiscordClone.Api.Common;
using DiscordClone.Application.Servers;
using DiscordClone.Application.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DiscordClone.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/servers")]
public class ServersController : ControllerBase
{
    private readonly IServerService _serverService;
    private readonly IVoiceService _voiceService;

    public ServersController(IServerService serverService, IVoiceService voiceService)
    {
        _serverService = serverService;
        _voiceService = voiceService;
    }

    [HttpPost]
    public async Task<ActionResult<ServerSummary>> Create(CreateServerRequest request, CancellationToken ct)
    {
        var result = await _serverService.CreateServerAsync(User.GetUserId(), request, ct);
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServerSummary>>> GetMyServers(CancellationToken ct)
    {
        var result = await _serverService.GetMyServersAsync(User.GetUserId(), ct);
        return Ok(result);
    }

    [HttpGet("{serverId:guid}")]
    public async Task<ActionResult<ServerDetail>> GetServer(Guid serverId, CancellationToken ct)
    {
        var result = await _serverService.GetServerAsync(User.GetUserId(), serverId, ct);
        return Ok(result);
    }

    [HttpPatch("{serverId:guid}")]
    public async Task<ActionResult<ServerSummary>> UpdateServer(Guid serverId, UpdateServerRequest request, CancellationToken ct)
    {
        var result = await _serverService.UpdateServerAsync(User.GetUserId(), serverId, request, ct);
        return Ok(result);
    }

    [HttpPost("{serverId:guid}/icon")]
    [RequestSizeLimit(10_485_760)]
    public async Task<ActionResult<ServerSummary>> UpdateServerIcon(Guid serverId, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await _serverService.UpdateServerIconAsync(User.GetUserId(), serverId, stream, file.FileName, file.ContentType, file.Length, ct);
        return Ok(result);
    }

    [HttpDelete("{serverId:guid}")]
    public async Task<IActionResult> DeleteServer(Guid serverId, CancellationToken ct)
    {
        await _serverService.DeleteServerAsync(User.GetUserId(), serverId, ct);
        return NoContent();
    }

    [HttpDelete("{serverId:guid}/members/{targetUserId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid serverId, Guid targetUserId, CancellationToken ct)
    {
        await _serverService.RemoveMemberAsync(User.GetUserId(), serverId, targetUserId, ct);
        return NoContent();
    }

    [HttpPost("{serverId:guid}/bans/{targetUserId:guid}")]
    public async Task<ActionResult<BanDto>> BanMember(Guid serverId, Guid targetUserId, BanUserRequest request, CancellationToken ct)
    {
        var result = await _serverService.BanMemberAsync(User.GetUserId(), serverId, targetUserId, request, ct);
        return Ok(result);
    }

    [HttpDelete("{serverId:guid}/bans/{targetUserId:guid}")]
    public async Task<IActionResult> UnbanMember(Guid serverId, Guid targetUserId, CancellationToken ct)
    {
        await _serverService.UnbanMemberAsync(User.GetUserId(), serverId, targetUserId, ct);
        return NoContent();
    }

    [HttpGet("{serverId:guid}/bans")]
    public async Task<ActionResult<IReadOnlyList<BanDto>>> GetBans(Guid serverId, CancellationToken ct)
    {
        var result = await _serverService.GetBansAsync(User.GetUserId(), serverId, ct);
        return Ok(result);
    }

    [HttpGet("{serverId:guid}/roles")]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> GetRoles(Guid serverId, CancellationToken ct)
    {
        var result = await _serverService.GetRolesAsync(User.GetUserId(), serverId, ct);
        return Ok(result);
    }

    [HttpPost("{serverId:guid}/roles")]
    public async Task<ActionResult<RoleDto>> CreateRole(Guid serverId, CreateRoleRequest request, CancellationToken ct)
    {
        var result = await _serverService.CreateRoleAsync(User.GetUserId(), serverId, request, ct);
        return Ok(result);
    }

    [HttpPatch("{serverId:guid}/roles/{roleId:guid}")]
    public async Task<ActionResult<RoleDto>> UpdateRole(Guid serverId, Guid roleId, UpdateRoleRequest request, CancellationToken ct)
    {
        var result = await _serverService.UpdateRoleAsync(User.GetUserId(), serverId, roleId, request, ct);
        return Ok(result);
    }

    [HttpDelete("{serverId:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> DeleteRole(Guid serverId, Guid roleId, CancellationToken ct)
    {
        await _serverService.DeleteRoleAsync(User.GetUserId(), serverId, roleId, ct);
        return NoContent();
    }

    [HttpPut("{serverId:guid}/members/{targetUserId:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> AssignRole(Guid serverId, Guid targetUserId, Guid roleId, CancellationToken ct)
    {
        await _serverService.AssignRoleAsync(User.GetUserId(), serverId, targetUserId, roleId, ct);
        return NoContent();
    }

    [HttpDelete("{serverId:guid}/members/{targetUserId:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> UnassignRole(Guid serverId, Guid targetUserId, Guid roleId, CancellationToken ct)
    {
        await _serverService.UnassignRoleAsync(User.GetUserId(), serverId, targetUserId, roleId, ct);
        return NoContent();
    }

    [HttpGet("{serverId:guid}/my-permissions")]
    public async Task<ActionResult<MyPermissionsDto>> GetMyPermissions(Guid serverId, CancellationToken ct)
    {
        var result = await _serverService.GetMyPermissionsAsync(User.GetUserId(), serverId, ct);
        return Ok(result);
    }

    [HttpPost("{serverId:guid}/categories")]
    public async Task<ActionResult<CategoryDto>> CreateCategory(Guid serverId, CreateCategoryRequest request, CancellationToken ct)
    {
        var result = await _serverService.CreateCategoryAsync(User.GetUserId(), serverId, request, ct);
        return Ok(result);
    }

    [HttpPatch("{serverId:guid}/categories/{categoryId:guid}")]
    public async Task<ActionResult<CategoryDto>> UpdateCategory(Guid serverId, Guid categoryId, UpdateCategoryRequest request, CancellationToken ct)
    {
        var result = await _serverService.UpdateCategoryAsync(User.GetUserId(), serverId, categoryId, request, ct);
        return Ok(result);
    }

    [HttpDelete("{serverId:guid}/categories/{categoryId:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid serverId, Guid categoryId, CancellationToken ct)
    {
        await _serverService.DeleteCategoryAsync(User.GetUserId(), serverId, categoryId, ct);
        return NoContent();
    }

    [HttpPost("{serverId:guid}/channels")]
    public async Task<ActionResult<ChannelDto>> CreateChannel(Guid serverId, CreateChannelRequest request, CancellationToken ct)
    {
        var result = await _serverService.CreateChannelAsync(User.GetUserId(), serverId, request, ct);
        return Ok(result);
    }

    [HttpPatch("{serverId:guid}/channels/{channelId:guid}/move")]
    public async Task<ActionResult<ChannelDto>> MoveChannel(Guid serverId, Guid channelId, MoveChannelRequest request, CancellationToken ct)
    {
        var result = await _serverService.MoveChannelAsync(User.GetUserId(), serverId, channelId, request, ct);
        return Ok(result);
    }

    [HttpGet("{serverId:guid}/channels")]
    public async Task<ActionResult<IReadOnlyList<ChannelDto>>> GetChannels(Guid serverId, CancellationToken ct)
    {
        var result = await _serverService.GetChannelsAsync(User.GetUserId(), serverId, ct);
        return Ok(result);
    }

    [HttpGet("{serverId:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> GetMembers(Guid serverId, CancellationToken ct)
    {
        var result = await _serverService.GetMembersAsync(User.GetUserId(), serverId, ct);
        return Ok(result);
    }

    [HttpPost("{serverId:guid}/invites")]
    public async Task<ActionResult<InviteDto>> CreateInvite(Guid serverId, CreateInviteRequest request, CancellationToken ct)
    {
        var result = await _serverService.CreateInviteAsync(User.GetUserId(), serverId, request, ct);
        return Ok(result);
    }

    [HttpPost("join/{code}")]
    public async Task<ActionResult<ServerSummary>> Join(string code, CancellationToken ct)
    {
        var result = await _serverService.JoinByInviteAsync(User.GetUserId(), code, ct);
        return Ok(result);
    }

    [HttpGet("{serverId:guid}/invites")]
    public async Task<ActionResult<IReadOnlyList<InviteDto>>> GetInvites(Guid serverId, CancellationToken ct)
    {
        return Ok(await _serverService.GetInvitesAsync(User.GetUserId(), serverId, ct));
    }

    [HttpDelete("{serverId:guid}/invites/{inviteId:guid}")]
    public async Task<IActionResult> RevokeInvite(Guid serverId, Guid inviteId, CancellationToken ct)
    {
        await _serverService.RevokeInviteAsync(User.GetUserId(), serverId, inviteId, ct);
        return NoContent();
    }

    [HttpGet("{serverId:guid}/emojis")]
    public async Task<ActionResult<IReadOnlyList<CustomEmojiDto>>> GetEmojis(Guid serverId, CancellationToken ct)
    {
        return Ok(await _serverService.GetCustomEmojisAsync(User.GetUserId(), serverId, ct));
    }

    [HttpPost("{serverId:guid}/emojis")]
    [RequestSizeLimit(2_097_152)]
    public async Task<ActionResult<CustomEmojiDto>> CreateEmoji(Guid serverId, [FromForm] string name, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await _serverService.CreateCustomEmojiAsync(User.GetUserId(), serverId, name, stream, file.ContentType, file.Length, ct);
        return Ok(result);
    }

    [HttpDelete("{serverId:guid}/emojis/{emojiId:guid}")]
    public async Task<IActionResult> DeleteEmoji(Guid serverId, Guid emojiId, CancellationToken ct)
    {
        await _serverService.DeleteCustomEmojiAsync(User.GetUserId(), serverId, emojiId, ct);
        return NoContent();
    }

    [HttpGet("{serverId:guid}/voice-participants")]
    public async Task<ActionResult<IReadOnlyDictionary<Guid, IReadOnlyList<VoiceParticipantDto>>>> GetVoiceParticipants(Guid serverId, CancellationToken ct)
    {
        await _serverService.EnsureMembershipAsync(User.GetUserId(), serverId, ct);
        var result = await _voiceService.GetServerVoiceParticipantsAsync(serverId, ct);
        return Ok(result);
    }
}
