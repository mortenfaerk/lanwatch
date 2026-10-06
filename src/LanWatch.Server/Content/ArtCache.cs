using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Content;

/// <summary>
/// Proxies and caches game art on disk: Steam header images, and SteamGridDB grids for games outside Steam.
/// Each image is fetched from the internet once and then served locally, which helps when the event's uplink is
/// saturated.
/// </summary>
public sealed partial class ArtCache(IHttpClientFactory httpFactory, IOptions<LanWatchOptions> options, ILogger<ArtCache> logger)
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _failures = new();
    private readonly SemaphoreSlim _gate = new(4);

    public Task<string?> GetSteamHeaderAsync(long appId, CancellationToken ct) =>
        GetOrFetchAsync(Path.Combine("steam", $"{appId}.jpg"), async http =>
        {
            foreach (var url in new[]
                     {
                         $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg",
                         $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
                     })
            {
                var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode) return response;
                response.Dispose();
            }
            return null;
        }, ct);

    /// <summary>
    /// Header-shaped art (460x215, the Steam header ratio) from SteamGridDB, found by game name. Needs
    /// <c>STEAMGRIDDB_API_KEY</c>; a free key comes from https://www.steamgriddb.com/profile/preferences.
    /// </summary>
    public Task<string?> GetSteamGridDbAsync(string name, CancellationToken ct)
    {
        var key = options.Value.SteamGridDbApiKey;
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult<string?>(null);

        return GetOrFetchAsync(Path.Combine("sgdb", $"{Slug(name)}.img"), async http =>
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

            using var search = await JsonDocument.ParseAsync(
                await http.GetStreamAsync($"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(name)}", ct),
                cancellationToken: ct);
            var game = search.RootElement.GetProperty("data").EnumerateArray()
                .Select(g => g.GetProperty("id").GetInt64())
                .Cast<long?>()
                .FirstOrDefault();
            if (game is null) return null;

            using var grids = await JsonDocument.ParseAsync(
                await http.GetStreamAsync($"https://www.steamgriddb.com/api/v2/grids/game/{game}?dimensions=460x215,920x430&types=static&nsfw=false&humor=false", ct),
                cancellationToken: ct);
            var url = grids.RootElement.GetProperty("data").EnumerateArray()
                .Select(g => g.GetProperty("url").GetString())
                .FirstOrDefault(u => u is not null);
            if (url is null) return null;

            http.DefaultRequestHeaders.Authorization = null; // image CDN takes no token
            var image = await http.GetAsync(url, ct);
            if (image.IsSuccessStatusCode) return image;
            image.Dispose();
            return null;
        }, ct);
    }

    /// <summary>Serves a cached file, or fetches it once (bounded concurrency, with a back-off after failures).</summary>
    private async Task<string?> GetOrFetchAsync(string relativePath, Func<HttpClient, Task<HttpResponseMessage?>> fetch, CancellationToken ct)
    {
        var file = Path.Combine(options.Value.DataPath, "art", relativePath);
        if (File.Exists(file)) return file;
        if (_failures.TryGetValue(relativePath, out var failedAt) && DateTimeOffset.UtcNow - failedAt < RetryAfterFailure)
            return null;

        await _gate.WaitAsync(ct);
        try
        {
            if (File.Exists(file)) return file;
            using var http = httpFactory.CreateClient("art");
            using var response = await fetch(http);
            if (response is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var tmp = file + ".tmp";
                await using (var fs = File.Create(tmp))
                    await response.Content.CopyToAsync(fs, ct);
                File.Move(tmp, file, overwrite: true);
                return file;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            logger.LogDebug("Art fetch for {Path} failed: {Message}", relativePath, ex.Message);
        }
        finally
        {
            _gate.Release();
        }

        _failures[relativePath] = DateTimeOffset.UtcNow;
        return null;
    }

    /// <summary>Content type from the file's magic bytes; SteamGridDB serves both PNG and JPEG.</summary>
    public static string ContentTypeOf(string file)
    {
        Span<byte> head = stackalloc byte[4];
        using var fs = File.OpenRead(file);
        var n = fs.Read(head);
        return n >= 4 && head[0] == 0x89 && head[1] == (byte)'P' ? "image/png"
            : n >= 4 && head[0] == (byte)'R' && head[1] == (byte)'I' ? "image/webp"
            : "image/jpeg";
    }

    private static string Slug(string name) => SlugChars().Replace(name.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugChars();
}
