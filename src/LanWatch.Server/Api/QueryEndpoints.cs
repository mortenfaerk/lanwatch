using LanWatch.Server.Content;
using LanWatch.Server.Data;
using LanWatch.Server.Ingestion;
using LanWatch.Shared.Contracts;
using LanWatch.Shared.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Api;

/// <summary>
/// Read and edit endpoints for the dashboard. Every query takes the same scope parameters: <c>event</c> (an event id)
/// or <c>from</c>/<c>to</c> (unix seconds). Without either, the scope is the latest event, extended to now while
/// it is still running.
/// </summary>
public static class QueryEndpoints
{
    private const long ActiveWindowSeconds = 120;

    public static void MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/overview", Overview);
        api.MapGet("/timeseries", TimeSeries);
        api.MapGet("/downloads", Downloads);
        api.MapGet("/clients", Clients);
        api.MapPut("/clients/{ip}/name", RenameClient);
        api.MapGet("/content", ContentTotals);
        api.MapGet("/errors", Errors);
        api.MapGet("/stream", Stream);

        api.MapGet("/events", Events);
        api.MapPost("/events", CreateEvent);
        api.MapPut("/events/{id:int}", UpdateEvent);
        api.MapDelete("/events/{id:int}", DeleteEvent);

        api.MapGet("/settings", async (ClientDirectory dir, CancellationToken ct) => new SettingsDto(await dir.GetExcludedAsync(ct)));
        api.MapPut("/settings", async (SettingsDto dto, ClientDirectory dir, CancellationToken ct) =>
        {
            await dir.SetExcludedAsync(dto.ExcludedIps, ct);
            return new SettingsDto(await dir.GetExcludedAsync(ct));
        });
        api.MapGet("/system", SystemInfo);

        api.MapGet("/settings/art", (GameArtSettings art) => art.ToDto());
        api.MapPut("/settings/art", async (ArtKeyRequest body, GameArtSettings art, CancellationToken ct) =>
            await art.SaveAsync(body.ApiKey ?? "", ct) is { } problem ? Results.BadRequest(problem) : Results.Ok(art.ToDto()));
        api.MapDelete("/settings/art", async (GameArtSettings art, CancellationToken ct) =>
        {
            await art.ClearAsync(ct);
            return Results.Ok(art.ToDto());
        });

        api.MapGet("/art/steam/{appId:long}", async (long appId, ArtCache art, HttpContext http, CancellationToken ct) =>
        {
            var file = await art.GetSteamHeaderAsync(appId, ct);
            if (file is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=604800";
            return Results.File(file, "image/jpeg");
        });

        api.MapGet("/art/sgdb/{service}/{id}", async (string service, string id, ArtCache art, HttpContext http, CancellationToken ct) =>
        {
            if (KnownProducts.Find(service, id) is not { Infrastructure: false } product) return Results.NotFound();
            var file = await art.GetSteamGridDbAsync(product.Name, ct);
            if (file is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=604800";
            return Results.File(file, ArtCache.ContentTypeOf(file));
        });
    }

    // ---- scope ----

    internal static async Task<ScopeDto> ResolveScopeAsync(LanWatchDb db, int? eventId, long? from, long? to, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        if (eventId is { } id && await db.Events.FirstOrDefaultAsync(x => x.Id == id, ct) is { } e)
            return new ScopeDto(e.StartUnix, LiveEnd(e, now), e.Id, e.Name);

        if (from is not null || to is not null)
        {
            var f = from ?? (to ?? now) - 86400;
            return new ScopeDto(f, to ?? now, null, "Custom range");
        }

        var latest = await db.Events.OrderByDescending(x => x.StartUnix).FirstOrDefaultAsync(ct);
        return latest is null
            ? new ScopeDto(now - 86400, now, null, "Last 24 hours")
            : new ScopeDto(latest.StartUnix, LiveEnd(latest, now), latest.Id, latest.Name);
    }

    // An event that ended less than a few hours ago is probably still running (detection is hour-granular).
    private static long LiveEnd(LanEvent e, long now) => now - e.EndUnix < 6 * 3600 ? Math.Max(e.EndUnix, now) : e.EndUnix;

    private static long PickBucket(long span, int targetPoints)
    {
        long[] steps = [60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400];
        var ideal = span / Math.Max(1, targetPoints);
        return steps.FirstOrDefault(s => s >= ideal, 86400);
    }

    // ---- handlers ----

    private static async Task<OverviewDto> Overview(int? @event, long? from, long? to, LanWatchDb db, ClientDirectory dir,
        IngestStatus ingest, TimeProvider time, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(db, @event, from, to, time, ct);
        var ex = await dir.GetExcludedAsync(ct);
        var traffic = db.TrafficMinutes.Where(t => t.Minute >= scope.FromUnix && t.Minute < scope.ToUnix && !ex.Contains(t.ClientIp));

        var services = await traffic.GroupBy(t => t.Service)
            .Select(g => new ServiceTotal(g.Key, g.Sum(t => t.HitBytes), g.Sum(t => t.MissBytes), g.Sum(t => t.HitRequests + t.MissRequests)))
            .ToListAsync(ct);
        var errorRequests = await traffic.SumAsync(t => t.ErrorRequests, ct);
        var clients = await traffic.Where(t => t.HitRequests + t.MissRequests > 0).Select(t => t.ClientIp).Distinct().CountAsync(ct);
        var downloads = await db.Downloads.CountAsync(d => d.LastUnix >= scope.FromUnix && d.StartUnix < scope.ToUnix && !ex.Contains(d.ClientIp), ct);
        var errors = await db.Errors.CountAsync(e => e.Unix >= scope.FromUnix && e.Unix < scope.ToUnix, ct);

        return new OverviewDto(scope,
            services.Sum(s => s.HitBytes), services.Sum(s => s.MissBytes), services.Sum(s => s.Requests), errorRequests,
            clients, downloads, errors,
            services.OrderByDescending(s => s.HitBytes + s.MissBytes).ToList(),
            Summarize(ingest));
    }

    private static async Task<TimeSeriesDto> TimeSeries(int? @event, long? from, long? to, string? client, string? service, int? points,
        LanWatchDb db, ClientDirectory dir, TimeProvider time, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(db, @event, from, to, time, ct);
        var bucket = PickBucket(scope.ToUnix - scope.FromUnix, Math.Clamp(points ?? 240, 12, 1000));
        var ex = await dir.GetExcludedAsync(ct);

        var q = db.TrafficMinutes.Where(t => t.Minute >= scope.FromUnix && t.Minute < scope.ToUnix && !ex.Contains(t.ClientIp));
        if (!string.IsNullOrEmpty(client)) q = q.Where(t => t.ClientIp == client);
        if (!string.IsNullOrEmpty(service)) q = q.Where(t => t.Service == service);

        var rows = await q.GroupBy(t => t.Minute / bucket * bucket)
            .Select(g => new TimePoint(g.Key, g.Sum(t => t.HitBytes), g.Sum(t => t.MissBytes), g.Sum(t => t.ErrorRequests)))
            .ToListAsync(ct);

        // Fill gaps so charts show silence as zero instead of interpolating across it.
        var byT = rows.ToDictionary(r => r.T);
        var points2 = new List<TimePoint>();
        for (var t = scope.FromUnix / bucket * bucket; t < scope.ToUnix; t += bucket)
            points2.Add(byT.GetValueOrDefault(t) ?? new TimePoint(t, 0, 0, 0));
        return new TimeSeriesDto(bucket, points2);
    }

    private static async Task<PagedResult<DownloadDto>> Downloads(int? @event, long? from, long? to, string? client, string? service,
        string? q, bool? active, int? skip, int? take, LanWatchDb db, ClientDirectory dir, ContentCatalog catalog, TimeProvider time,
        CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(db, @event, from, to, time, ct);
        var ex = await dir.GetExcludedAsync(ct);
        var now = time.GetUtcNow().ToUnixTimeSeconds();

        var query = db.Downloads.Where(d => d.LastUnix >= scope.FromUnix && d.StartUnix < scope.ToUnix && !ex.Contains(d.ClientIp));
        if (!string.IsNullOrEmpty(client)) query = query.Where(d => d.ClientIp == client);
        if (!string.IsNullOrEmpty(service)) query = query.Where(d => d.Service == service);
        if (active == true) query = query.Where(d => d.LastUnix >= now - ActiveWindowSeconds);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var depots = await db.SteamDepots.Where(s => EF.Functions.Like(s.AppName, $"%{term}%")).OrderBy(s => s.DepotId)
                .Select(s => s.DepotId.ToString()).Take(2000).ToListAsync(ct);
            query = query.Where(d => depots.Contains(d.ContentId) || EF.Functions.Like(d.ContentId, $"%{term}%")
                                     || EF.Functions.Like(d.ClientIp, $"%{term}%"));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(d => d.LastUnix)
            .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 50, 1, 500)).ToListAsync(ct);

        var content = await catalog.ResolveAsync(rows.Select(r => (r.Service, r.ContentId)), ct);
        var names = await dir.GetNamesAsync(ct);
        return new PagedResult<DownloadDto>(rows.Select(r => new DownloadDto(r.Id, r.ClientIp, names.GetValueOrDefault(r.ClientIp),
            content[(r.Service, r.ContentId)], r.StartUnix, r.LastUnix, r.HitBytes, r.MissBytes, r.Requests,
            r.LastUnix >= now - ActiveWindowSeconds)).ToList(), total);
    }

    private static async Task<List<ClientDto>> Clients(int? @event, long? from, long? to, LanWatchDb db, ClientDirectory dir,
        IOptions<LanWatchOptions> options, TimeProvider time, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(db, @event, from, to, time, ct);
        var ex = await dir.GetExcludedAsync(ct);
        const int sparkPoints = 24;
        var bucket = Math.Max(60, (scope.ToUnix - scope.FromUnix) / sparkPoints);
        var gateways = options.Value.DockerGatewayIps.ToHashSet();

        var traffic = db.TrafficMinutes.Where(t => t.Minute >= scope.FromUnix && t.Minute < scope.ToUnix && !ex.Contains(t.ClientIp));
        var totals = await traffic.GroupBy(t => t.ClientIp)
            .Select(g => new
            {
                Ip = g.Key,
                Hit = g.Sum(t => t.HitBytes),
                Miss = g.Sum(t => t.MissBytes),
                Requests = g.Sum(t => t.HitRequests + t.MissRequests),
                First = g.Min(t => t.Minute),
                Last = g.Max(t => t.Minute),
            })
            .Where(x => x.Requests > 0)
            .ToListAsync(ct);

        var spark = await traffic.GroupBy(t => new { t.ClientIp, B = (t.Minute - scope.FromUnix) / bucket })
            .Select(g => new { g.Key.ClientIp, g.Key.B, Bytes = g.Sum(t => t.HitBytes + t.MissBytes) })
            .ToListAsync(ct);
        var sparkByClient = spark.ToLookup(s => s.ClientIp);

        var downloads = await db.Downloads.Where(d => d.LastUnix >= scope.FromUnix && d.StartUnix < scope.ToUnix)
            .GroupBy(d => d.ClientIp).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var names = await dir.GetNamesAsync(ct);

        return totals.Select(t =>
        {
            var series = new long[sparkPoints];
            foreach (var s in sparkByClient[t.Ip])
                if (s.B >= 0 && s.B < sparkPoints) series[s.B] += s.Bytes;
            return new ClientDto(t.Ip, names.GetValueOrDefault(t.Ip), gateways.Contains(t.Ip), t.Hit, t.Miss, t.Requests,
                downloads.GetValueOrDefault(t.Ip), t.First, t.Last + 60, series);
        }).OrderByDescending(c => c.HitBytes + c.MissBytes).ToList();
    }

    private static async Task<IResult> RenameClient(string ip, ClientRename body, ClientDirectory dir, CancellationToken ct)
    {
        if (body.Name is { Length: > 64 }) return Results.BadRequest("Name too long");
        await dir.RenameAsync(ip, body.Name, ct);
        return Results.NoContent();
    }

    private static async Task<List<ContentTotalDto>> ContentTotals(int? @event, long? from, long? to, string? service, int? take,
        LanWatchDb db, ClientDirectory dir, ContentCatalog catalog, TimeProvider time, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(db, @event, from, to, time, ct);
        var ex = await dir.GetExcludedAsync(ct);
        var q = db.Downloads.Where(d => d.LastUnix >= scope.FromUnix && d.StartUnix < scope.ToUnix && !ex.Contains(d.ClientIp));
        if (!string.IsNullOrEmpty(service)) q = q.Where(d => d.Service == service);

        var rows = await q.GroupBy(d => new { d.Service, d.ContentId })
            .Select(g => new
            {
                g.Key.Service,
                g.Key.ContentId,
                Hit = g.Sum(d => d.HitBytes),
                Miss = g.Sum(d => d.MissBytes),
                Clients = g.Select(d => d.ClientIp).Distinct().Count(),
                Sessions = g.Count(),
                Last = g.Max(d => d.LastUnix),
            })
            .OrderByDescending(x => x.Hit + x.Miss)
            .Take(Math.Clamp(take ?? 100, 1, 500))
            .ToListAsync(ct);

        var content = await catalog.ResolveAsync(rows.Select(r => (r.Service, r.ContentId)), ct);

        // Several depots usually belong to one game; merge them so a game appears once.
        return rows
            .GroupBy(r => content[(r.Service, r.ContentId)] is var c ? $"{c.Service}:{c.Name}" : "")
            .Select(g => new ContentTotalDto(content[(g.First().Service, g.First().ContentId)],
                g.Sum(r => r.Hit), g.Sum(r => r.Miss), g.Max(r => r.Clients), g.Sum(r => r.Sessions), g.Max(r => r.Last)))
            .OrderByDescending(c => c.HitBytes + c.MissBytes)
            .ToList();
    }

    private static async Task<ErrorsDto> Errors(int? @event, long? from, long? to, LanWatchDb db, TimeProvider time, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(db, @event, from, to, time, ct);
        var errors = db.Errors.Where(e => e.Unix >= scope.FromUnix && e.Unix < scope.ToUnix);

        var kinds = (await errors.GroupBy(e => new { e.Source, e.Kind }).Select(g => new { g.Key.Source, g.Key.Kind, Count = g.Count() }).ToListAsync(ct))
            .Select(k => new ErrorKindCount(k.Source.ToString(), k.Kind.ToString(), k.Count))
            .OrderByDescending(k => k.Count).ToList();

        var hosts = (await errors.Where(e => e.Host != null)
                .GroupBy(e => new { e.Host, e.Kind })
                .Select(g => new { g.Key.Host, g.Key.Kind, Count = g.Count(), Last = g.Max(e => e.Unix) })
                .OrderByDescending(x => x.Count).Take(20).ToListAsync(ct))
            .Select(h => new ErrorHostCount(h.Host!, h.Kind.ToString(), h.Count, h.Last)).ToList();

        var statuses = await db.StatusMinutes
            .Where(s => s.Minute >= scope.FromUnix && s.Minute < scope.ToUnix && s.Status >= 400)
            .GroupBy(s => new { s.Status, s.Service })
            .Select(g => new StatusCount(g.Key.Status, g.Key.Service, g.Sum(s => s.Count)))
            .ToListAsync(ct);

        var bucket = PickBucket(scope.ToUnix - scope.FromUnix, 120);
        var timelineRows = await errors.GroupBy(e => new { T = e.Unix / bucket * bucket, e.Kind })
            .Select(g => new { g.Key.T, g.Key.Kind, Count = g.Count() }).ToListAsync(ct);
        var timeline = timelineRows.GroupBy(r => r.T).OrderBy(g => g.Key)
            .Select(g => new ErrorBucket(g.Key, g.ToDictionary(r => r.Kind.ToString(), r => r.Count))).ToList();

        var recent = (await errors.OrderByDescending(e => e.Unix).Take(100).ToListAsync(ct))
            .Select(e => new ErrorEventDto(e.Unix, e.Source.ToString(), e.Kind.ToString(), e.ClientIp, e.Host, e.Upstream, e.Message)).ToList();

        return new ErrorsDto(kinds, hosts, statuses.OrderByDescending(s => s.Count).ToList(), bucket, timeline, recent);
    }

    private static async Task<List<StreamHostDto>> Stream(int? @event, long? from, long? to, LanWatchDb db, TimeProvider time, CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(db, @event, from, to, time, ct);
        var rows = await db.StreamMinutes.Where(s => s.Minute >= scope.FromUnix && s.Minute < scope.ToUnix)
            .GroupBy(s => s.SniHost)
            .Select(g => new
            {
                Host = g.Key,
                Connections = g.Sum(s => s.Connections),
                Sent = g.Sum(s => s.BytesSent),
                Received = g.Sum(s => s.BytesReceived),
                Clients = g.Select(s => s.ClientIp).Distinct().Count(),
                Failures = g.Sum(s => s.Status != 200 ? s.Connections : 0),
            })
            .OrderByDescending(s => s.Connections)
            .Take(50)
            .ToListAsync(ct);
        return rows.Select(r => new StreamHostDto(r.Host, r.Connections, r.Sent, r.Received, r.Clients, r.Failures)).ToList();
    }

    // ---- events ----

    private static async Task<List<LanEventDto>> Events(LanWatchDb db, CancellationToken ct) =>
        (await db.Events.OrderByDescending(e => e.StartUnix).ToListAsync(ct)).Select(ToDto).ToList();

    private static async Task<IResult> CreateEvent(LanEventUpsert body, LanWatchDb db, CancellationToken ct)
    {
        if (Validate(body) is { } problem) return problem;
        var e = new LanEvent { Name = body.Name.Trim(), StartUnix = body.StartUnix, EndUnix = body.EndUnix, IsAuto = false };
        db.Events.Add(e);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(e));
    }

    private static async Task<IResult> UpdateEvent(int id, LanEventUpsert body, LanWatchDb db, CancellationToken ct)
    {
        if (Validate(body) is { } problem) return problem;
        var e = await db.Events.AsTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Results.NotFound();
        // Renaming keeps auto bounds; moving the bounds hands ownership to the crew.
        if (e.StartUnix != body.StartUnix || e.EndUnix != body.EndUnix) e.IsAuto = false;
        e.Name = body.Name.Trim();
        e.StartUnix = body.StartUnix;
        e.EndUnix = body.EndUnix;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(e));
    }

    private static async Task<IResult> DeleteEvent(int id, LanWatchDb db, CancellationToken ct)
    {
        var deleted = await db.Events.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static IResult? Validate(LanEventUpsert body) =>
        string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 80 ? Results.BadRequest("Name is required (max 80 chars)")
        : body.EndUnix <= body.StartUnix ? Results.BadRequest("End must be after start")
        : null;

    private static LanEventDto ToDto(LanEvent e) => new(e.Id, e.Name, e.StartUnix, e.EndUnix, e.IsAuto);

    // ---- system ----

    private static async Task<SystemDto> SystemInfo(LanWatchDb db, IngestStatus ingest, IOptions<LanWatchOptions> options, GameArtSettings art, CancellationToken ct)
    {
        var o = options.Value;
        var version = await db.Settings.Where(s => s.Key == SteamDepotMapper.VersionSetting).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return new SystemDto(o.LogsPath, o.DockerGatewayIps, o.SessionGapMinutes, Summarize(ingest), version, await db.SteamDepots.CountAsync(ct),
            art.ApiKey is not null);
    }

    internal static IngestSummary Summarize(IngestStatus s) => new(
        s.CaughtUp,
        s.LastCycle?.ToUnixTimeSeconds(),
        IngestionService.Sources.Select(src =>
        {
            var f = s.Files.GetValueOrDefault(src.FileName);
            return new IngestFile(src.FileName, f?.Exists ?? false, f?.Offset ?? 0, f?.Length ?? 0, f?.LinesParsed ?? 0, f?.LinesFailed ?? 0);
        }).ToList());
}
