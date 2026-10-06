using LanWatch.Server.Api;
using LanWatch.Server.Content;
using LanWatch.Server.Data;
using LanWatch.Server.Health;
using LanWatch.Server.Ingestion;
using LanWatch.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LanWatch.Server.Live;

[Authorize]
public sealed class LiveHub : Hub;

/// <summary>
/// Pushes a <see cref="LiveTick"/> to every connected dashboard about every 2 s. It also sends a throttled
/// "dataChanged" signal so pages can re-query aggregates once new lines have landed.
/// </summary>
public sealed class LiveBroadcaster(
    LiveFeed feed,
    IngestStatus ingest,
    ContentCatalog catalog,
    ClientDirectory clients,
    HealthState health,
    IDbContextFactory<LanWatchDb> dbFactory,
    IHubContext<LiveHub> hub,
    ILogger<LiveBroadcaster> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DataChangedInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TickInterval);
        long lastVersion = -1;
        var lastDataChanged = DateTimeOffset.MinValue;

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await hub.Clients.All.SendAsync(LiveHubContract.Tick, await BuildTickAsync(ct), ct);

                if (ingest.Version != lastVersion && DateTimeOffset.UtcNow - lastDataChanged >= DataChangedInterval)
                {
                    lastVersion = ingest.Version;
                    lastDataChanged = DateTimeOffset.UtcNow;
                    await hub.Clients.All.SendAsync(LiveHubContract.DataChanged, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Live tick failed");
            }
        }
    }

    public async Task<LiveTick> BuildTickAsync(CancellationToken ct)
    {
        var s = feed.TakeSnapshot();
        var content = await catalog.ResolveAsync(s.Active.Select(a => (a.Service, a.Id)), ct);
        var names = await clients.GetNamesAsync(ct);
        var dockets = await DocketsAsync(s.Now, ct);

        // A game is usually several depots; the crew wants one row per client and game.
        var merged = s.Active
            .GroupBy(a => (a.Client, Content: content[(a.Service, a.Id)] is var c && c.ArtUrl is not null ? c.Name : $"{a.Service}:{a.Id}"))
            .Select(g =>
            {
                var total = g.Sum(a => a.TotalBytes);
                var hit = g.Sum(a => a.TotalBytes * a.HitRatio);
                var lead = g.MaxBy(a => a.TotalBytes)!;
                return new
                {
                    Download = new LiveDownload(g.Key.Client, names.GetValueOrDefault(g.Key.Client), content[(lead.Service, lead.Id)],
                        g.Sum(a => a.BytesPerSecond), total == 0 ? 0 : hit / total, total,
                        dockets.TryGetValue((g.Key.Client, lead.Service, lead.Id), out var docket) ? docket : null),
                    LastSeen = g.Max(a => a.LastSeen),
                };
            })
            .OrderByDescending(x => x.Download.BytesPerSecond)
            .ThenByDescending(x => x.LastSeen)
            .Take(24)
            .Select(x => x.Download)
            .ToList();

        return new LiveTick(
            s.Now,
            s.HitPerSecond,
            s.MissPerSecond,
            s.Window.Select(w => new LiveSample(w.T, w.Hit, w.Miss)).ToList(),
            merged,
            s.Active.Select(a => a.Client).Distinct().Count(),
            s.NewErrors,
            ingest.CaughtUp,
            health.Health);
    }

    /// <summary>The download session id (docket number) behind each live row: the newest session per client and content.</summary>
    private async Task<Dictionary<(string, string, string), long>> DocketsAsync(long now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var recent = await db.Downloads.Where(d => d.LastUnix >= now - 600)
            .Select(d => new { d.ClientIp, d.Service, d.ContentId, d.StartUnix, d.Id })
            .ToListAsync(ct);
        return recent.GroupBy(d => (d.ClientIp, d.Service, d.ContentId))
            .ToDictionary(g => g.Key, g => g.MaxBy(d => d.StartUnix)!.Id);
    }
}
