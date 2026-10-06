using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LanWatch.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Content;

/// <summary>
/// Keeps the Steam depot to app mapping current. It uses the dataset DeveLanCacheUI publishes from
/// SteamDepotFinder (<c>appId;appName;depotId</c>, ~240k rows). The data is stored in SQLite, so names still
/// resolve when the event's internet is down.
/// </summary>
public sealed class SteamDepotMapper(
    IHttpClientFactory httpFactory,
    IDbContextFactory<LanWatchDb> dbFactory,
    IOptions<LanWatchOptions> options,
    ContentCatalog catalog,
    ILogger<SteamDepotMapper> logger) : BackgroundService
{
    public const string VersionSetting = "steam.depotmap.version";
    private const string LatestRelease = "https://api.github.com/repos/devedse/DeveLanCacheUI_SteamDepotFinder_Runner/releases/latest";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.FetchSteamDepotMap) return;
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Steam depot map refresh failed: {Message}", ex.Message);
            }
            await Task.Delay(TimeSpan.FromHours(6), ct);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var http = httpFactory.CreateClient("github");
        var release = await http.GetFromJsonAsync<Release>(LatestRelease, ct);
        var asset = release?.Assets.FirstOrDefault(a => a.Name == "app-depot-output-cleaned.csv.gz");
        if (release is null || asset is null) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var current = await db.Settings.Where(s => s.Key == VersionSetting).Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (current == release.Name) return;

        logger.LogInformation("Downloading Steam depot map {Version}", release.Name);
        await using var gz = await http.GetStreamAsync(asset.BrowserDownloadUrl, ct);
        await using var unzip = new GZipStream(gz, CompressionMode.Decompress);
        using var reader = new StreamReader(unzip);

        var count = await ImportAsync(reader, release.Name, ct);
        catalog.InvalidateSteam();
        logger.LogInformation("Imported {Count} Steam depots ({Version})", count, release.Name);
    }

    internal async Task<int> ImportAsync(TextReader reader, string version, CancellationToken ct)
    {
        // A depot can be shared by several apps (redistributables, DLC); the lowest app id is usually the base game.
        var map = new Dictionary<long, (long AppId, string Name)>();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var first = line.IndexOf(';');
            var last = line.LastIndexOf(';');
            if (first <= 0 || last <= first) continue;
            if (!long.TryParse(line.AsSpan(0, first), out var appId) || !long.TryParse(line.AsSpan(last + 1), out var depotId)) continue;
            var name = line[(first + 1)..last];
            if (!map.TryGetValue(depotId, out var existing) || appId < existing.AppId)
                map[depotId] = (appId, name);
        }

        await using var conn = new SqliteConnection($"Data Source={options.Value.DatabasePath}");
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        await using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM SteamDepots";
            await del.ExecuteNonQueryAsync(ct);
        }
        await using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO SteamDepots (DepotId, AppId, AppName) VALUES ($d, $a, $n)";
            var d = ins.Parameters.Add("$d", SqliteType.Integer);
            var a = ins.Parameters.Add("$a", SqliteType.Integer);
            var n = ins.Parameters.Add("$n", SqliteType.Text);
            foreach (var (depot, (app, name)) in map)
            {
                d.Value = depot;
                a.Value = app;
                n.Value = name;
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
        await using (var set = conn.CreateCommand())
        {
            set.Transaction = tx;
            set.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($k, $v) ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value";
            set.Parameters.AddWithValue("$k", VersionSetting);
            set.Parameters.AddWithValue("$v", version);
            await set.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return map.Count;
    }

    private sealed record Release(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("assets")] List<Asset> Assets);

    private sealed record Asset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
