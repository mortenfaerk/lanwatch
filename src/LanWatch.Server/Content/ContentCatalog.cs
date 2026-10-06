using System.Collections.Concurrent;
using LanWatch.Server.Data;
using LanWatch.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LanWatch.Server.Content;

/// <summary>Turns (service, content id) into something a human recognises: a game name and artwork.</summary>
public sealed class ContentCatalog(IDbContextFactory<LanWatchDb> dbFactory, GameArtSettings artSettings)
{
    private readonly ConcurrentDictionary<long, (long AppId, string Name)?> _steam = new();

    // Apps the depot dataset leaves unnamed but every LAN downloads.
    private static readonly Dictionary<long, string> SteamKnownApps = new()
    {
        [228980] = "Steamworks Common Redistributables",
        [7] = "Steam Client",
    };

    public void InvalidateSteam() => _steam.Clear();

    public async Task<Dictionary<(string Service, string Id), ContentInfo>> ResolveAsync(
        IEnumerable<(string Service, string Id)> keys, CancellationToken ct = default)
    {
        var distinct = keys.Distinct().ToList();
        var depots = distinct
            .Where(k => k.Service == "steam" && long.TryParse(k.Id, out _))
            .Select(k => long.Parse(k.Id))
            .Where(d => !_steam.ContainsKey(d))
            .Distinct()
            .ToList();

        if (depots.Count > 0)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            foreach (var chunk in depots.Chunk(500))
            {
                var found = await db.SteamDepots.Where(d => chunk.Contains(d.DepotId))
                    .ToDictionaryAsync(d => d.DepotId, d => (d.AppId, d.AppName), ct);
                // Some requests carry an app id where the depot id would be (e.g. /depot/730/...); fall back to the app.
                var missing = chunk.Where(d => !found.ContainsKey(d)).ToArray();
                var apps = await db.SteamDepots.Where(d => missing.Contains(d.AppId))
                    .GroupBy(d => d.AppId).Select(g => new { AppId = g.Key, Name = g.Min(d => d.AppName)! })
                    .ToDictionaryAsync(a => a.AppId, a => (a.AppId, a.Name), ct);
                foreach (var depot in chunk)
                    _steam[depot] = found.TryGetValue(depot, out var app) ? app
                        : apps.TryGetValue(depot, out var asApp) ? asApp
                        : null;
            }
        }

        return distinct.ToDictionary(k => k, k => Resolve(k.Service, k.Id));
    }

    private ContentInfo Resolve(string service, string id)
    {
        switch (service)
        {
            case "steam" when long.TryParse(id, out var depot):
                if (_steam.TryGetValue(depot, out var app) && app is { } a)
                    return new ContentInfo(service, id,
                        string.IsNullOrWhiteSpace(a.Name) || a.Name.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                            ? SteamKnownApps.GetValueOrDefault(a.AppId, $"Steam app {a.AppId}")
                            : a.Name,
                        $"/api/art/steam/{a.AppId}",
                        $"https://store.steampowered.com/app/{a.AppId}");
                return new ContentInfo(service, id, $"Steam depot {id}", null, $"https://steamdb.info/depot/{id}/");
            case "blizzard" or "riot" or "epicgames":
                return Launcher(service, id);
            case "sony":
                return new ContentInfo(service, id, $"PlayStation {id}", null, null);
            case "wsus":
                return new ContentInfo(service, id, "Windows Update", null, null);
            default:
                return new ContentInfo(service, id, id, null, null);
        }
    }

    /// <summary>
    /// Names a Battle.net, Epic or Riot code. Art comes from Steam when the game is also sold there; otherwise from
    /// SteamGridDB when an API key is configured. Launcher plumbing gets no art.
    /// </summary>
    private ContentInfo Launcher(string service, string id)
    {
        var product = KnownProducts.Find(service, id);
        if (product is null)
        {
            var prefix = service switch { "blizzard" => "Battle.net", "riot" => "Riot", _ => "Epic" };
            return new ContentInfo(service, id, $"{prefix} {id}", null, null);
        }

        string? art = null;
        if (!product.Infrastructure)
        {
            if (product.SteamAppId is { } steamApp) art = $"/api/art/steam/{steamApp}";
            else if (artSettings.ApiKey is not null) art = $"/api/art/sgdb/{service}/{Uri.EscapeDataString(id)}";
        }
        return new ContentInfo(service, id, product.Name, art, product.StoreUrl);
    }
}
