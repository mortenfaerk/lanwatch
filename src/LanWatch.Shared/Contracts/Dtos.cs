namespace LanWatch.Shared.Contracts;

// Wire contracts between LanWatch.Server and LanWatch.Client. Times are unix seconds (UTC).

public sealed record LoginRequest(string Password);

public sealed record SessionInfo(bool Authenticated);

public sealed record LanEventDto(int Id, string Name, long StartUnix, long EndUnix, bool IsAuto);

public sealed record LanEventUpsert(string Name, long StartUnix, long EndUnix);

/// <summary>The time window a query covers; resolved from an event id or explicit bounds.</summary>
public sealed record ScopeDto(long FromUnix, long ToUnix, int? EventId, string Label);

public sealed record ServiceTotal(string Service, long HitBytes, long MissBytes, long Requests);

public sealed record OverviewDto(
    ScopeDto Scope,
    long HitBytes,
    long MissBytes,
    long Requests,
    long ErrorRequests,
    int Clients,
    int Downloads,
    int ErrorEvents,
    IReadOnlyList<ServiceTotal> Services,
    IngestSummary Ingest);

public sealed record IngestSummary(bool CaughtUp, long? LastCycleUnix, IReadOnlyList<IngestFile> Files);

public sealed record IngestFile(string Name, bool Exists, long Offset, long Length, long LinesParsed, long LinesFailed);

public sealed record TimePoint(long T, long HitBytes, long MissBytes, long ErrorRequests);

public sealed record TimeSeriesDto(long BucketSeconds, IReadOnlyList<TimePoint> Points);

/// <summary>A resolved piece of content: a game, an app or an update stream.</summary>
public sealed record ContentInfo(string Service, string Id, string Name, string? ArtUrl, string? StoreUrl);

public sealed record DownloadDto(
    long Id,
    string ClientIp,
    string? ClientName,
    ContentInfo Content,
    long StartUnix,
    long LastUnix,
    long HitBytes,
    long MissBytes,
    long Requests,
    bool Active);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);

public sealed record ClientDto(
    string Ip,
    string? Name,
    bool IsGateway,
    long HitBytes,
    long MissBytes,
    long Requests,
    int Downloads,
    long FirstSeenUnix,
    long LastSeenUnix,
    IReadOnlyList<long> Sparkline);

public sealed record ClientRename(string? Name);

public sealed record ContentTotalDto(ContentInfo Content, long HitBytes, long MissBytes, int Clients, int Sessions, long LastUnix);

public sealed record ErrorKindCount(string Source, string Kind, int Count);

public sealed record ErrorHostCount(string Host, string Kind, int Count, long LastUnix);

public sealed record StatusCount(int Status, string Service, long Count);

public sealed record ErrorEventDto(long Unix, string Source, string Kind, string? ClientIp, string? Host, string? Upstream, string Message);

public sealed record ErrorBucket(long T, IReadOnlyDictionary<string, int> ByKind);

public sealed record ErrorsDto(
    IReadOnlyList<ErrorKindCount> Kinds,
    IReadOnlyList<ErrorHostCount> TopHosts,
    IReadOnlyList<StatusCount> Statuses,
    long BucketSeconds,
    IReadOnlyList<ErrorBucket> Timeline,
    IReadOnlyList<ErrorEventDto> Recent);

public sealed record StreamHostDto(string SniHost, long Connections, long BytesSent, long BytesReceived, int Clients, long Failures);

public sealed record SettingsDto(IReadOnlyList<string> ExcludedIps);

public sealed record SystemDto(string LogsPath, IReadOnlyList<string> DockerGatewayIps, int SessionGapMinutes, IngestSummary Ingest, string? DepotMapVersion, int DepotMapCount, bool SteamGridDbEnabled = false);

// ---- Live feed (SignalR "tick", every ~2 s) ----

public sealed record LiveSample(long T, long HitBytes, long MissBytes);

public sealed record LiveDownload(string ClientIp, string? ClientName, ContentInfo Content, long BytesPerSecond, double HitRatio, long SessionBytes, long? DocketId = null);

public sealed record LiveTick(
    long Now,
    long HitBytesPerSecond,
    long MissBytesPerSecond,
    IReadOnlyList<LiveSample> Window,
    IReadOnlyList<LiveDownload> Active,
    int ActiveClients,
    int NewErrors,
    bool CaughtUp,
    HealthDto? Health = null);

public static class LiveHubContract
{
    public const string Path = "/hubs/live";
    public const string Tick = "tick";
    public const string DataChanged = "dataChanged";
}
