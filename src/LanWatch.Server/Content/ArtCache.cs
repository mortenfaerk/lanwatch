using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Content;

/// <summary>
/// Proxies and caches Steam header art on disk. Each image is fetched from the internet once, and every client
/// after that is served locally, which helps when the event's uplink is saturated.
/// </summary>
public sealed class ArtCache(IHttpClientFactory httpFactory, IOptions<LanWatchOptions> options, ILogger<ArtCache> logger)
{
    private readonly ConcurrentDictionary<long, DateTimeOffset> _failures = new();
    private readonly SemaphoreSlim _gate = new(4);

    public async Task<string?> GetSteamHeaderAsync(long appId, CancellationToken ct)
    {
        var dir = Path.Combine(options.Value.DataPath, "art", "steam");
        var file = Path.Combine(dir, $"{appId}.jpg");
        if (File.Exists(file)) return file;

        // Don't hammer the CDN for apps without art (or while offline).
        if (_failures.TryGetValue(appId, out var failedAt) && DateTimeOffset.UtcNow - failedAt < TimeSpan.FromHours(1))
            return null;

        await _gate.WaitAsync(ct);
        try
        {
            if (File.Exists(file)) return file;
            var http = httpFactory.CreateClient("art");
            foreach (var url in new[]
                     {
                         $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg",
                         $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
                     })
            {
                using var response = await http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) continue;
                Directory.CreateDirectory(dir);
                var tmp = file + ".tmp";
                await using (var fs = File.Create(tmp))
                    await response.Content.CopyToAsync(fs, ct);
                File.Move(tmp, file, overwrite: true);
                return file;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            logger.LogDebug("Art fetch for app {AppId} failed: {Message}", appId, ex.Message);
        }
        finally
        {
            _gate.Release();
        }

        _failures[appId] = DateTimeOffset.UtcNow;
        return null;
    }
}
