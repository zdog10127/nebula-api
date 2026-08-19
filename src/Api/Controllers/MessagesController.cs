using DiscordClone.Api.Common;
using DiscordClone.Api.Hubs;
using DiscordClone.Application.Messages;
using DiscordClone.Application.Push;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DiscordClone.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IPushService _push;

    public MessagesController(IMessageService messageService, IHubContext<ChatHub> hub, IPushService push)
    {
        _messageService = messageService;
        _hub = hub;
        _push = push;
    }

    [HttpGet("channels/{channelId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> GetHistory(
        Guid channelId, [FromQuery] DateTime? before, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var result = await _messageService.GetHistoryAsync(User.GetUserId(), channelId, before, limit, ct);
        return Ok(result);
    }

    [HttpPost("channels/{channelId:guid}/messages")]
    public async Task<ActionResult<MessageDto>> Send(Guid channelId, SendMessageRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var message = await _messageService.SendMessageAsync(userId, channelId, request.Content, request.AttachmentIds, ct);
        await _hub.Clients.Group(ChatHub.ChannelGroup(channelId)).SendAsync("ReceiveMessage", message, ct);

        var serverId = await _messageService.GetServerIdForChannelAsync(channelId, ct);
        await _hub.Clients.Group(ChatHub.PresenceGroup(serverId))
            .SendAsync("UnreadPing", channelId, serverId, userId, message.MentionedUserIds, ct);

        var preview = message.Content.Length > 120 ? message.Content[..120] + "…" : message.Content;
        foreach (var mentionedUserId in message.MentionedUserIds.Where(id => id != userId))
            await _push.NotifyIfOfflineAsync(mentionedUserId, $"{message.AuthorDisplayName} mencionou você", preview, null, ct);

        return Ok(message);
    }

    [HttpPost("channels/{channelId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid channelId, CancellationToken ct)
    {
        await _messageService.MarkChannelReadAsync(User.GetUserId(), channelId, ct);
        return NoContent();
    }

    [HttpGet("servers/{serverId:guid}/unread")]
    public async Task<ActionResult<IReadOnlyDictionary<Guid, UnreadCountDto>>> GetUnread(Guid serverId, CancellationToken ct)
    {
        return Ok(await _messageService.GetUnreadCountsAsync(User.GetUserId(), serverId, ct));
    }

    [HttpPatch("messages/{messageId:guid}")]
    public async Task<ActionResult<MessageDto>> Edit(Guid messageId, EditMessageRequest request, CancellationToken ct)
    {
        var message = await _messageService.EditMessageAsync(User.GetUserId(), messageId, request.Content, ct);
        await _hub.Clients.Group(ChatHub.ChannelGroup(message.ChannelId)).SendAsync("MessageEdited", message, ct);
        return Ok(message);
    }

    [HttpDelete("messages/{messageId:guid}")]
    public async Task<IActionResult> Delete(Guid messageId, CancellationToken ct)
    {
        var channelId = await _messageService.DeleteMessageAsync(User.GetUserId(), messageId, ct);
        await _hub.Clients.Group(ChatHub.ChannelGroup(channelId)).SendAsync("MessageDeleted", messageId, ct);
        return NoContent();
    }

    [HttpPost("messages/{messageId:guid}/reactions")]
    public async Task<IActionResult> AddReaction(Guid messageId, AddReactionRequest request, CancellationToken ct)
    {
        var (channelId, reactions) = await _messageService.AddReactionAsync(User.GetUserId(), messageId, request.Emoji, ct);
        await _hub.Clients.Group(ChatHub.ChannelGroup(channelId)).SendAsync("MessageReactionsChanged", messageId, reactions, ct);
        return NoContent();
    }

    [HttpDelete("messages/{messageId:guid}/reactions/{emoji}")]
    public async Task<IActionResult> RemoveReaction(Guid messageId, string emoji, CancellationToken ct)
    {
        var (channelId, reactions) = await _messageService.RemoveReactionAsync(User.GetUserId(), messageId, Uri.UnescapeDataString(emoji), ct);
        await _hub.Clients.Group(ChatHub.ChannelGroup(channelId)).SendAsync("MessageReactionsChanged", messageId, reactions, ct);
        return NoContent();
    }

    [HttpPost("messages/{messageId:guid}/pin")]
    public async Task<ActionResult<MessageDto>> Pin(Guid messageId, CancellationToken ct)
    {
        var message = await _messageService.PinMessageAsync(User.GetUserId(), messageId, ct);
        await _hub.Clients.Group(ChatHub.ChannelGroup(message.ChannelId)).SendAsync("MessagePinned", message, ct);
        return Ok(message);
    }

    [HttpDelete("messages/{messageId:guid}/pin")]
    public async Task<ActionResult<MessageDto>> Unpin(Guid messageId, CancellationToken ct)
    {
        var message = await _messageService.UnpinMessageAsync(User.GetUserId(), messageId, ct);
        await _hub.Clients.Group(ChatHub.ChannelGroup(message.ChannelId)).SendAsync("MessageUnpinned", message, ct);
        return Ok(message);
    }

    [HttpGet("channels/{channelId:guid}/messages/pinned")]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> GetPinned(Guid channelId, CancellationToken ct)
    {
        return Ok(await _messageService.GetPinnedMessagesAsync(User.GetUserId(), channelId, ct));
    }

    [HttpGet("channels/{channelId:guid}/messages/search")]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> Search(Guid channelId, [FromQuery] string q, CancellationToken ct)
    {
        return Ok(await _messageService.SearchMessagesAsync(User.GetUserId(), channelId, q, ct));
    }
}
