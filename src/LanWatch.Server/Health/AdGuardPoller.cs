using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LanWatch.Server.Api;
using LanWatch.Shared.Contracts;
using LanWatch.Shared.Parsing;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Health;

/// <summary>
/// Polls AdGuard Home's REST API (<c>/control/status</c>, <c>/control/stats</c>, <c>/control/querylog</c>).
/// It also spots clients that resolve game CDN names through AdGuard directly: those lookups should go to
/// lancache-dns, so those clients bypass the cache.
/// </summary>
public sealed class AdGuardPoller(
    IOptions<LanWatchOptions> options,
    IHttpClientFactory httpFactory,
    HealthState state,
    ClientDirectory clients,
    TimeProvider time,
    ILogger<AdGuardPoller> logger) : BackgroundService
{
    // Domains lancache serves (from uklans/cache-domains); a direct lookup of one of these is a bypass.
    internal static readonly string[] CacheDomainSuffixes =
    [
        "steamcontent.com", "steampipe.akamaized.net", "steamcdn-a.akamaihd.net", "epicgames.com", "epicgamescdn.com",
        "epicgames-download1.akamaized.net", "download.epicgames.com", "blizzard.com", "blzddist1-a.akamaihd.net",
        "blizzard.vo.llnwd.net", "battle.net", "windowsupdate.com", "delivery.mp.microsoft.com", "update.microsoft.com",
        "dl.playstation.net", "riotcdn.net", "origin.com", "akamai.cdn.ea.com", "uplaypc-s-ubisoft.cdn.ubi.com",
        "wargaming.net", "arenanetworks.com", "xboxlive.com", "assets.xboxlive.com",
    ];

    private static readonly TimeSpan BypassWindow = TimeSpan.FromMinutes(15);
    private DateTimeOffset _lastBypassRecord = DateTimeOffset.MinValue;
    private DateTimeOffset _lastUnreachableRecord = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.AdGuardUrl))
        {
            state.Health = state.Health with
            {
                AdGuard = new ProbeResult(ProbeState.NotConfigured, "Set ADGUARD_URL to watch AdGuard", null, time.GetUtcNow().ToUnixTimeSeconds(), null),
            };
            return;
        }

        var http = httpFactory.CreateClient("adguard");
        http.BaseAddress = new Uri(o.AdGuardUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrEmpty(o.AdGuardUser))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.AdGuardUser}:{o.AdGuardPassword}")));

        var ignore = (o.AdGuardIgnoreClients.Length > 0 ? o.AdGuardIgnoreClients : [.. o.LancacheIps, .. o.DockerGatewayIps, "127.0.0.1"])
            .ToHashSet(StringComparer.Ordinal);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, o.AdGuardIntervalSeconds)));
        do
        {
            await PollAsync(http, ignore, ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task PollAsync(HttpClient http, HashSet<string> ignore, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var status = await GetJson(http, "control/status", ct);
            var latency = sw.Elapsed.TotalMilliseconds;
            using var stats = await GetJson(http, "control/stats", ct);
            using var log = await GetJson(http, "control/querylog?limit=1000", ct);

            var s = stats.RootElement;
            var names = await clients.GetNamesAsync(ct);
            var bypass = FindBypass(log.RootElement, ignore, now - BypassWindow)
                .Select(b => b with { ClientName = names.GetValueOrDefault(b.ClientIp) })
                .ToList();

            var protection = status.RootElement.TryGetProperty("protection_enabled", out var p) && p.GetBoolean();
            state.AdGuard = new AdGuardDto(
                Configured: true,
                Reachable: true,
                Error: null,
                Version: status.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null,
                ProtectionEnabled: protection,
                TimeUnits: Str(s, "time_units") ?? "hours",
                Queries: Long(s, "num_dns_queries"),
                Blocked: Long(s, "num_blocked_filtering"),
                AvgProcessingMs: Double(s, "avg_processing_time") * 1000,
                QueriesSeries: s.TryGetProperty("dns_queries", out var q) && q.ValueKind == JsonValueKind.Array ? q.EnumerateArray().Select(x => x.GetInt64()).ToList() : [],
                TopClients: Top(s, "top_clients", 10),
                TopDomains: Top(s, "top_queried_domains", 10),
                Upstreams: Upstreams(s),
                Bypass: bypass,
                CheckedUnix: now.ToUnixTimeSeconds());

            state.Health = state.Health with
            {
                AdGuard = new ProbeResult(protection ? ProbeState.Ok : ProbeState.Warn,
                    protection ? $"Answering in {latency:0} ms" : "Protection is off", null, now.ToUnixTimeSeconds(), latency),
            };

            if (bypass.Count > 0 && now - _lastBypassRecord > TimeSpan.FromMinutes(1))
            {
                _lastBypassRecord = now;
                foreach (var b in bypass)
                    await state.RecordAsync(ErrorKind.DnsBypass, b.Domains.FirstOrDefault(), b.ClientIp,
                        $"{b.ClientIp} asked AdGuard for {string.Join(", ", b.Domains.Take(3))}", ct);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            var message = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized }
                ? "AdGuard rejected the login (check ADGUARD_USER / ADGUARD_PASSWORD)"
                : ex.Message;
            state.AdGuard = state.AdGuard with { Configured = true, Reachable = false, Error = message, CheckedUnix = now.ToUnixTimeSeconds() };
            state.Health = state.Health with { AdGuard = new ProbeResult(ProbeState.Fail, "AdGuard unreachable", message, now.ToUnixTimeSeconds(), null) };
            if (now - _lastUnreachableRecord > TimeSpan.FromSeconds(30))
            {
                _lastUnreachableRecord = now;
                await state.RecordAsync(ErrorKind.AdGuardUnreachable, http.BaseAddress?.Host, null, message, ct);
            }
            logger.LogDebug("AdGuard poll failed: {Message}", message);
        }
    }

    internal static IEnumerable<BypassClient> FindBypass(JsonElement log, HashSet<string> ignore, DateTimeOffset since)
    {
        if (!log.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        var hits = new List<(string Client, string Domain, DateTimeOffset Time)>();
        foreach (var item in data.EnumerateArray())
        {
            var client = Str(item, "client");
            if (client is null || ignore.Contains(client)) continue;
            if (!item.TryGetProperty("time", out var t) || !DateTimeOffset.TryParse(t.GetString(), out var when) || when < since) continue;
            var name = item.TryGetProperty("question", out var qn) ? Str(qn, "name")?.TrimEnd('.').ToLowerInvariant() : null;
            if (name is null || !IsCacheDomain(name)) continue;
            hits.Add((client, name, when));
        }
        return hits.GroupBy(h => h.Client)
            .Select(g => new BypassClient(g.Key, null, g.Count(), g.Select(h => h.Domain).Distinct().Take(5).ToList(), g.Max(h => h.Time).ToUnixTimeSeconds()))
            .OrderByDescending(b => b.Queries);
    }

    internal static bool IsCacheDomain(string name) =>
        CacheDomainSuffixes.Any(s => name == s || name.EndsWith("." + s, StringComparison.Ordinal));

    private static async Task<JsonDocument> GetJson(HttpClient http, string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    // AdGuard's "top" arrays are lists of single-key objects: [{"example.com": 123}, ...].
    private static List<CountEntry> Top(JsonElement s, string prop, int take) =>
        !s.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array ? []
        : arr.EnumerateArray().SelectMany(o => o.EnumerateObject()).Select(kv => new CountEntry(kv.Name, (long)kv.Value.GetDouble())).Take(take).ToList();

    private static List<UpstreamEntry> Upstreams(JsonElement s)
    {
        var avgExact = !s.TryGetProperty("top_upstreams_avg_time", out var a) || a.ValueKind != JsonValueKind.Array ? new Dictionary<string, double>()
            : a.EnumerateArray().SelectMany(o => o.EnumerateObject()).ToDictionary(kv => kv.Name, kv => kv.Value.GetDouble());
        return Top(s, "top_upstreams_responses", 20)
            .Select(r => new UpstreamEntry(r.Name, r.Count, avgExact.GetValueOrDefault(r.Name) * 1000))
            .ToList();
    }

    private static string? Str(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static long Long(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
    private static double Double(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
