using DiscordClone.Api.Hubs;
using DiscordClone.Application.Presence;
using DiscordClone.Application.Servers;
using DiscordClone.Application.Steam;
using DiscordClone.Infrastructure.Persistence;
using DiscordClone.Infrastructure.Steam;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;

namespace DiscordClone.Api.BackgroundServices;

/// <summary>
/// Polls the Steam Web API for every online user who has linked a Steam account, and
/// broadcasts changes the same way ChatHub.SetActivity does for locally-detected
/// activity. Lives in the Api project (not Infrastructure) specifically because it
/// needs ChatHub/IHubContext&lt;ChatHub&gt; — Infrastructure has no project reference to
/// Api, only the other way around. Runs as a singleton BackgroundService, so it
/// resolves everything it needs through a fresh DI scope each tick rather than
/// holding scoped services (MongoContext, IServerService) directly in its constructor.
/// </summary>
public class SteamActivityPollingService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SteamOptions _options;
    private readonly ILogger<SteamActivityPollingService> _logger;

    public SteamActivityPollingService(IServiceScopeFactory scopeFactory, SteamOptions options, ILogger<SteamActivityPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogInformation("STEAM_API_KEY/PUBLIC_API_URL not set — Steam activity polling stays disabled.");
            return;
        }

        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Steam activity polling tick failed; will retry next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var presence = scope.ServiceProvider.GetRequiredService<IPresenceService>();
        var mongo = scope.ServiceProvider.GetRequiredService<MongoContext>();
        var steamApi = scope.ServiceProvider.GetRequiredService<ISteamApiClient>();
        var serverService = scope.ServiceProvider.GetRequiredService<IServerService>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ChatHub>>();

        var onlineUserIds = await presence.GetOnlineUserIdsAsync(ct);
        if (onlineUserIds.Count == 0)
            return;

        // ShareActivityStatus is filtered here too (not just when building REST DTOs
        // elsewhere) because this broadcast has no per-request caller to gate it —
        // unlike the Electron flow, where GameActivityReporter.tsx simply never calls
        // SetActivity when sharing is off, nothing client-side stops this background
        // loop from pushing an update, so the check has to live here.
        var linkedUsers = await mongo.Users
            .Find(u => onlineUserIds.Contains(u.Id) && u.SteamId64 != null && u.ShareActivityStatus)
            .ToListAsync(ct);

        if (linkedUsers.Count == 0)
            return;

        // Guards the (extremely unlikely, since SteamId64 is uniquely indexed) case of
        // two accounts somehow sharing one Steam ID — GroupBy+First just keeps this
        // from throwing on a duplicate dictionary key instead of asserting it can't happen.
        var steamIdToUserId = linkedUsers
            .GroupBy(u => u.SteamId64!)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var activities = await steamApi.GetPlayerActivitiesAsync(steamIdToUserId.Keys.ToList(), ct);

        var userIds = steamIdToUserId.Values.ToList();
        var previousActivities = await presence.GetSteamActivitiesAsync(userIds, ct);

        foreach (var (steamId, userId) in steamIdToUserId)
        {
            var newActivity = activities.TryGetValue(steamId, out var activity) ? activity.GameName : null;
            var previousActivity = previousActivities.GetValueOrDefault(userId);

            if (newActivity == previousActivity)
                continue;

            await presence.SetSteamActivityAsync(userId, newActivity, ct);

            // Steam is authoritative when present (see PresenceService.GetActivitiesAsync),
            // so what's broadcast here is exactly what everyone should now see.
            var serverIds = await serverService.GetMyServerIdsAsync(userId, ct);
            foreach (var serverId in serverIds)
                await hub.Clients.Group(ChatHub.PresenceGroup(serverId)).SendAsync("ActivityChanged", userId, newActivity, ct);
        }
    }
}
