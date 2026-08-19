using DiscordClone.Api.Common;
using DiscordClone.Application.Messages;
using DiscordClone.Application.Presence;
using DiscordClone.Application.Servers;
using DiscordClone.Application.Voice;
using DiscordClone.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DiscordClone.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IMessageService _messageService;
    private readonly IPresenceService _presenceService;
    private readonly IVoicePresenceService _voicePresenceService;
    private readonly IServerService _serverService;
    private readonly IVoiceService _voiceService;

    public ChatHub(
        IMessageService messageService,
        IPresenceService presenceService,
        IVoicePresenceService voicePresenceService,
        IServerService serverService,
        IVoiceService voiceService)
    {
        _messageService = messageService;
        _presenceService = presenceService;
        _voicePresenceService = voicePresenceService;
        _serverService = serverService;
        _voiceService = voiceService;
    }

    public static string ChannelGroup(Guid channelId) => $"channel:{channelId}";
    public static string PresenceGroup(Guid serverId) => $"server:{serverId}:presence";
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User!.GetUserId();
        var serverIds = await _serverService.GetMyServerIdsAsync(userId, Context.ConnectionAborted);

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

        foreach (var serverId in serverIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, PresenceGroup(serverId));

        var wentOnline = await _presenceService.ConnectAsync(userId, Context.ConnectionId, Context.ConnectionAborted);
        if (wentOnline)
        {
            var status = await _presenceService.GetEffectiveStatusAsync(userId, Context.ConnectionAborted);
            foreach (var serverId in serverIds)
                await Clients.Group(PresenceGroup(serverId)).SendAsync("PresenceChanged", userId, status);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User!.GetUserId();
        var wentOffline = await _presenceService.DisconnectAsync(userId, Context.ConnectionId, CancellationToken.None);

        if (wentOffline)
        {
            var serverIds = await _serverService.GetMyServerIdsAsync(userId, CancellationToken.None);
            foreach (var serverId in serverIds)
                await Clients.Group(PresenceGroup(serverId)).SendAsync("PresenceChanged", userId, PresenceStatus.Offline);
        }

        await LeaveVoiceInternalAsync(CancellationToken.None);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SetStatus(PresenceStatus status)
    {
        if (status is not (PresenceStatus.Online or PresenceStatus.Away or PresenceStatus.DoNotDisturb or PresenceStatus.Invisible))
            throw new HubException("Invalid status.");

        var userId = Context.User!.GetUserId();
        await _presenceService.SetStatusAsync(userId, status, Context.ConnectionAborted);

        var effectiveStatus = await _presenceService.GetEffectiveStatusAsync(userId, Context.ConnectionAborted);
        var serverIds = await _serverService.GetMyServerIdsAsync(userId, Context.ConnectionAborted);
        foreach (var serverId in serverIds)
            await Clients.Group(PresenceGroup(serverId)).SendAsync("PresenceChanged", userId, effectiveStatus);
    }

    public async Task JoinChannel(Guid channelId)
    {
        await _messageService.EnsureChannelAccessAsync(Context.User!.GetUserId(), channelId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, ChannelGroup(channelId));
    }

    public async Task LeaveChannel(Guid channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChannelGroup(channelId));
    }

    public async Task SendMessage(Guid channelId, string content)
    {
        var userId = Context.User!.GetUserId();
        var message = await _messageService.SendMessageAsync(userId, channelId, content, null, Context.ConnectionAborted);
        await Clients.Group(ChannelGroup(channelId)).SendAsync("ReceiveMessage", message);

        var serverId = await _messageService.GetServerIdForChannelAsync(channelId, Context.ConnectionAborted);
        await Clients.Group(PresenceGroup(serverId)).SendAsync("UnreadPing", channelId, serverId, userId, message.MentionedUserIds);
    }

    public async Task Typing(Guid channelId)
    {
        await _messageService.EnsureChannelAccessAsync(Context.User!.GetUserId(), channelId, Context.ConnectionAborted);
        await Clients.OthersInGroup(ChannelGroup(channelId)).SendAsync("UserTyping", channelId, Context.User!.GetUserId());
    }

    public async Task JoinVoiceChannel(Guid channelId)
    {
        var userId = Context.User!.GetUserId();
        await _messageService.EnsureChannelAccessAsync(userId, channelId, Context.ConnectionAborted);

        var entries = await _voicePresenceService.JoinAsync(channelId, Context.ConnectionId, userId, Context.ConnectionAborted);
        await BroadcastVoiceParticipantsAsync(channelId, entries, Context.ConnectionAborted);
    }

    public async Task LeaveVoiceChannel(Guid channelId)
    {
        await LeaveVoiceInternalAsync(Context.ConnectionAborted);
    }

    public async Task UpdateVoiceState(bool isMuted, bool isDeafened)
    {
        var result = await _voicePresenceService.UpdateStateAsync(Context.ConnectionId, isMuted, isDeafened, Context.ConnectionAborted);
        if (result is null)
            return;

        var (channelId, entries) = result.Value;
        await BroadcastVoiceParticipantsAsync(channelId, entries, Context.ConnectionAborted);
    }

    public async Task ShareNowPlaying(Guid channelId, string type, string url, string? title)
    {
        var userId = Context.User!.GetUserId();
        await _messageService.EnsureChannelAccessAsync(userId, channelId, Context.ConnectionAborted);

        var dto = await _voiceService.ShareNowPlayingAsync(userId, channelId, new ShareNowPlayingRequest(type, url, title), Context.ConnectionAborted);
        var serverId = await _voiceService.GetServerIdForChannelAsync(channelId, Context.ConnectionAborted);
        await Clients.Group(PresenceGroup(serverId)).SendAsync("NowPlayingChanged", channelId, dto);
    }

    public async Task StopNowPlaying(Guid channelId)
    {
        var userId = Context.User!.GetUserId();
        await _voiceService.StopNowPlayingAsync(userId, channelId, Context.ConnectionAborted);
        var serverId = await _voiceService.GetServerIdForChannelAsync(channelId, Context.ConnectionAborted);
        await Clients.Group(PresenceGroup(serverId)).SendAsync("NowPlayingChanged", channelId, null);
    }

    private async Task LeaveVoiceInternalAsync(CancellationToken ct)
    {
        var left = await _voicePresenceService.LeaveAsync(Context.ConnectionId, ct);
        if (left is null)
            return;

        var (channelId, entries) = left.Value;
        await BroadcastVoiceParticipantsAsync(channelId, entries, ct);
    }

    private async Task BroadcastVoiceParticipantsAsync(Guid channelId, IReadOnlyList<VoicePresenceEntry> entries, CancellationToken ct)
    {
        var participants = await _voiceService.ResolveParticipantsAsync(entries, ct);
        var serverId = await _voiceService.GetServerIdForChannelAsync(channelId, ct);
        await Clients.Group(PresenceGroup(serverId)).SendAsync("VoiceParticipantsChanged", channelId, participants);
    }
}
