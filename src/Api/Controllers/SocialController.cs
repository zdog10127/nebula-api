using DiscordClone.Api.Common;
using DiscordClone.Api.Hubs;
using DiscordClone.Application.Push;
using DiscordClone.Application.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DiscordClone.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class SocialController : ControllerBase
{
    private readonly ISocialService _social;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IPushService _push;

    public SocialController(ISocialService social, IHubContext<ChatHub> hub, IPushService push)
    {
        _social = social;
        _hub = hub;
        _push = push;
    }

    [HttpGet("friends")]
    public async Task<ActionResult<IReadOnlyList<FriendDto>>> GetFriends(CancellationToken ct)
    {
        return Ok(await _social.GetFriendsAsync(User.GetUserId(), ct));
    }

    [HttpDelete("friends/{friendUserId:guid}")]
    public async Task<IActionResult> RemoveFriend(Guid friendUserId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        await _social.RemoveFriendAsync(userId, friendUserId, ct);
        await _hub.Clients.Groups(ChatHub.UserGroup(userId), ChatHub.UserGroup(friendUserId)).SendAsync("FriendRemoved", userId, friendUserId, ct);
        return NoContent();
    }

    [HttpGet("friends/requests")]
    public async Task<ActionResult<IReadOnlyList<FriendRequestDto>>> GetFriendRequests(CancellationToken ct)
    {
        return Ok(await _social.GetFriendRequestsAsync(User.GetUserId(), ct));
    }

    [HttpPost("friends/requests")]
    public async Task<ActionResult<FriendRequestDto>> SendFriendRequest(SendFriendRequestRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var dto = await _social.SendFriendRequestAsync(userId, request.Username, ct);
        await _hub.Clients.Group(ChatHub.UserGroup(dto.UserId)).SendAsync("FriendRequestReceived", ct);
        return Ok(dto);
    }

    [HttpPost("friends/requests/{requestId:guid}/accept")]
    public async Task<IActionResult> AcceptFriendRequest(Guid requestId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var otherUserId = await _social.AcceptFriendRequestAsync(userId, requestId, ct);
        await _hub.Clients.Groups(ChatHub.UserGroup(userId), ChatHub.UserGroup(otherUserId)).SendAsync("FriendRequestAccepted", ct);
        return NoContent();
    }

    [HttpPost("friends/requests/{requestId:guid}/decline")]
    public async Task<IActionResult> DeclineFriendRequest(Guid requestId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var otherUserId = await _social.DeclineFriendRequestAsync(userId, requestId, ct);
        await _hub.Clients.Group(ChatHub.UserGroup(otherUserId)).SendAsync("FriendRequestDeclined", ct);
        return NoContent();
    }

    [HttpDelete("friends/requests/{requestId:guid}")]
    public async Task<IActionResult> CancelFriendRequest(Guid requestId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var otherUserId = await _social.CancelFriendRequestAsync(userId, requestId, ct);
        await _hub.Clients.Group(ChatHub.UserGroup(otherUserId)).SendAsync("FriendRequestDeclined", ct);
        return NoContent();
    }

    [HttpGet("dm/channels")]
    public async Task<ActionResult<IReadOnlyList<DmChannelDto>>> GetDmChannels(CancellationToken ct)
    {
        return Ok(await _social.GetDmChannelsAsync(User.GetUserId(), ct));
    }

    [HttpPost("dm/channels")]
    public async Task<ActionResult<DmChannelDto>> GetOrCreateDmChannel([FromBody] GetOrCreateDmChannelRequest request, CancellationToken ct)
    {
        return Ok(await _social.GetOrCreateDmChannelAsync(User.GetUserId(), request.UserId, ct));
    }

    [HttpGet("dm/channels/{dmChannelId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<DmMessageDto>>> GetDmHistory(
        Guid dmChannelId, [FromQuery] DateTime? before, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        return Ok(await _social.GetDmHistoryAsync(User.GetUserId(), dmChannelId, before, limit, ct));
    }

    [HttpPost("dm/channels/{dmChannelId:guid}/messages")]
    public async Task<ActionResult<DmMessageDto>> SendDmMessage(Guid dmChannelId, SendDmMessageRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var message = await _social.SendDmMessageAsync(userId, dmChannelId, request.Content, ct);
        var (a, b) = await _social.GetDmParticipantsAsync(dmChannelId, ct);
        await _hub.Clients.Groups(ChatHub.UserGroup(a), ChatHub.UserGroup(b)).SendAsync("DmMessageReceived", message, ct);

        var otherUserId = a == userId ? b : a;
        var preview = message.Content.Length > 120 ? message.Content[..120] + "…" : message.Content;
        await _push.NotifyIfOfflineAsync(otherUserId, "Nova mensagem direta", preview, null, ct);

        return Ok(message);
    }

    [HttpPatch("dm/messages/{dmMessageId:guid}")]
    public async Task<ActionResult<DmMessageDto>> EditDmMessage(Guid dmMessageId, EditDmMessageRequest request, CancellationToken ct)
    {
        var message = await _social.EditDmMessageAsync(User.GetUserId(), dmMessageId, request.Content, ct);
        var (a, b) = await _social.GetDmParticipantsAsync(message.DmChannelId, ct);
        await _hub.Clients.Groups(ChatHub.UserGroup(a), ChatHub.UserGroup(b)).SendAsync("DmMessageEdited", message, ct);
        return Ok(message);
    }

    [HttpDelete("dm/messages/{dmMessageId:guid}")]
    public async Task<IActionResult> DeleteDmMessage(Guid dmMessageId, CancellationToken ct)
    {
        var dmChannelId = await _social.DeleteDmMessageAsync(User.GetUserId(), dmMessageId, ct);
        var (a, b) = await _social.GetDmParticipantsAsync(dmChannelId, ct);
        await _hub.Clients.Groups(ChatHub.UserGroup(a), ChatHub.UserGroup(b)).SendAsync("DmMessageDeleted", dmMessageId, ct);
        return NoContent();
    }
}

public record GetOrCreateDmChannelRequest(Guid UserId);
