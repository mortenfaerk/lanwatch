using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LanWatch.Shared.Contracts;
using LanWatch.Shared.Parsing;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Health;

/// <summary>
/// Active checks, every <see cref="LanWatchOptions.ProbeIntervalSeconds"/>:
/// <list type="bullet">
/// <item>monolithic answers <c>/lancache-heartbeat</c> with 204;</item>
/// <item>lancache-dns hands out the cache IP for a cached hostname;</item>
/// <item>the cache volume has room left.</item>
/// </list>
/// Failures are recorded as exceptions.
/// </summary>
public sealed class HealthProbe(
    IOptions<LanWatchOptions> options,
    IHttpClientFactory httpFactory,
    HealthState state,
    TimeProvider time,
    ILogger<HealthProbe> logger) : BackgroundService
{
    private const double DiskWarnFraction = 0.05;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Value;
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, o.ProbeIntervalSeconds)));
        do
        {
            try
            {
                var heartbeat = await HeartbeatAsync(o, ct);
                var dns = await DnsAsync(o, ct);
                var (diskProbe, disk) = Disk(o);
                state.Health = state.Health with { Heartbeat = heartbeat, LancacheDns = dns, Disk = diskProbe, CacheDisk = disk };

                if (heartbeat.State == ProbeState.Fail)
                    await state.RecordAsync(ErrorKind.CacheUnreachable, HostOf(o.LancacheUrl), null, heartbeat.Detail ?? heartbeat.Summary, ct);
                if (dns.State == ProbeState.Fail)
                    await state.RecordAsync(ErrorKind.CacheDnsWrong, o.DnsProbeName, null, dns.Detail ?? dns.Summary, ct);
                if (diskProbe.State == ProbeState.Fail)
                    await state.RecordAsync(ErrorKind.DiskLow, o.CachePath, null, diskProbe.Summary, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Health probe cycle failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task<ProbeResult> HeartbeatAsync(LanWatchOptions o, CancellationToken ct)
    {
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        if (string.IsNullOrWhiteSpace(o.LancacheUrl))
            return new(ProbeState.NotConfigured, "Set LANCACHE_URL to check the cache", null, now, null);

        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await httpFactory.CreateClient("probe").GetAsync(o.LancacheUrl.TrimEnd('/') + "/lancache-heartbeat", ct);
            var ms = sw.Elapsed.TotalMilliseconds;
            return response.StatusCode == HttpStatusCode.NoContent
                ? new(ProbeState.Ok, "Heartbeat answered", null, now, ms)
                : new(ProbeState.Fail, $"Heartbeat returned {(int)response.StatusCode}", $"GET /lancache-heartbeat returned {(int)response.StatusCode} instead of 204", now, ms);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            return new(ProbeState.Fail, "Cache not answering", ex.Message, now, null);
        }
    }

    private async Task<ProbeResult> DnsAsync(LanWatchOptions o, CancellationToken ct)
    {
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        if (string.IsNullOrWhiteSpace(o.LancacheDns))
            return new(ProbeState.NotConfigured, "Set LANCACHE_DNS to check lancache-dns", null, now, null);

        try
        {
            var server = await ResolveEndpointAsync(o.LancacheDns, ct);
            var sw = Stopwatch.StartNew();
            var answers = await DnsQuery.QueryAAsync(server, o.DnsProbeName, TimeSpan.FromSeconds(3), ct);
            var ms = sw.Elapsed.TotalMilliseconds;
            if (answers.Count == 0)
                return new(ProbeState.Fail, "No answer from lancache-dns", $"{o.DnsProbeName} returned no A record", now, ms);

            var expected = o.LancacheIps.Select(IPAddress.Parse).ToHashSet();
            var good = expected.Count > 0 ? answers.Any(expected.Contains) : answers.All(IsPrivate);
            var list = string.Join(", ", answers);
            return good
                ? new(ProbeState.Ok, $"Points at {list}", null, now, ms)
                : new(ProbeState.Fail, $"Answers {list}", $"{o.DnsProbeName} resolved to {list}, not the cache ({string.Join(", ", o.LancacheIps)})", now, ms);
        }
        catch (Exception ex) when (ex is SocketException or FormatException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // A timeout surfaces as OperationCanceledException from the linked token in DnsQuery.
            return new(ProbeState.Fail, "lancache-dns not answering", ex is OperationCanceledException ? "No reply within 3 s" : ex.Message, now, null);
        }
    }

    private (ProbeResult, DiskDto?) Disk(LanWatchOptions o)
    {
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        if (string.IsNullOrWhiteSpace(o.CachePath))
            return (new(ProbeState.NotConfigured, "Mount the cache at CACHE_PATH to see disk use", null, now, null), null);
        if (!Directory.Exists(o.CachePath))
            return (new(ProbeState.Warn, $"{o.CachePath} is not mounted", null, now, null), null);

        var drive = new DriveInfo(o.CachePath);
        var disk = new DiskDto(o.CachePath, drive.TotalSize, drive.AvailableFreeSpace);
        var freeFraction = drive.TotalSize == 0 ? 1 : (double)drive.AvailableFreeSpace / drive.TotalSize;
        var summary = $"{freeFraction:P0} free";
        return (freeFraction < DiskWarnFraction ? new(ProbeState.Fail, summary, null, now, null) : new(ProbeState.Ok, summary, null, now, null), disk);
    }

    private static async Task<IPEndPoint> ResolveEndpointAsync(string hostPort, CancellationToken ct)
    {
        var (host, port) = hostPort.LastIndexOf(':') is var i && i > 0 && int.TryParse(hostPort[(i + 1)..], out var p)
            ? (hostPort[..i], p)
            : (hostPort, 53);
        if (IPAddress.TryParse(host, out var ip)) return new IPEndPoint(ip, port);
        var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
        return new IPEndPoint(addresses.First(), port);
    }

    private static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    private static string? HostOf(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
}
