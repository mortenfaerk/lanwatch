using System.Collections.Concurrent;
using LanWatch.Server.Data;
using LanWatch.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LanWatch.Server.Content;

/// <summary>Turns (service, content id) into something a human recognises: a game name and artwork.</summary>
public sealed class ContentCatalog(IDbContextFactory<LanWatchDb> dbFactory)
{
    private readonly ConcurrentDictionary<long, (long AppId, string Name)?> _steam = new();

    // Blizzard's TACT product codes as they appear in /tpr/{code}/ paths.
    private static readonly Dictionary<string, string> Blizzard = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ovw"] = "Overwatch 2", ["pro"] = "Overwatch 2", ["sc2"] = "StarCraft II", ["s1"] = "StarCraft",
        ["Hero-Live-a"] = "Heroes of the Storm", ["hs"] = "Hearthstone", ["wow"] = "World of Warcraft",
        ["fenris"] = "Diablo IV", ["d3"] = "Diablo III", ["w3"] = "Warcraft III", ["odin"] = "Call of Duty",
        ["viper"] = "Call of Duty: Black Ops 4", ["zeus"] = "Call of Duty: Black Ops Cold War",
        ["auks"] = "Call of Duty", ["bna"] = "Battle.net", ["agent"] = "Battle.net Agent", ["wlby"] = "Crash Bandicoot 4",
        ["anbs"] = "Diablo Immortal", ["gryphon"] = "Warcraft Rumble",
        // Launcher plumbing rather than games.
        ["catalogs"] = "Battle.net catalog data", ["configs"] = "Battle.net configuration",
        ["bnt001"] = "Battle.net app", ["bnt002"] = "Battle.net app",
    };

    private static readonly Dictionary<string, string> Riot = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lol"] = "League of Legends", ["valorant"] = "VALORANT", ["bacon"] = "Legends of Runeterra",
        ["riot-client"] = "Riot Client",
    };

    private static readonly Dictionary<string, string> Epic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fortnite"] = "Fortnite", ["UnrealEngineLauncher"] = "Epic Games Launcher",
    };

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
            case "blizzard":
                return new ContentInfo(service, id, Blizzard.GetValueOrDefault(id, $"Blizzard {id}"), null, null);
            case "riot":
                return new ContentInfo(service, id, Riot.GetValueOrDefault(id, $"Riot {id}"), null, null);
            case "epicgames":
                return new ContentInfo(service, id, Epic.GetValueOrDefault(id, id), null, null);
            case "sony":
                return new ContentInfo(service, id, $"PlayStation {id}", null, null);
            case "wsus":
                return new ContentInfo(service, id, "Windows Update", null, null);
            default:
                return new ContentInfo(service, id, id, null, null);
        }
    }
}
