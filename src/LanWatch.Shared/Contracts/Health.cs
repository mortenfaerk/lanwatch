namespace LanWatch.Shared.Contracts;

public enum ProbeState { Unknown, Ok, Warn, Fail, NotConfigured }

/// <summary>One active check: what was tried, what came back, and when.</summary>
public sealed record ProbeResult(ProbeState State, string Summary, string? Detail, long CheckedUnix, double? LatencyMs);

public sealed record DiskDto(string Path, long TotalBytes, long FreeBytes);

public sealed record HealthDto(ProbeResult Heartbeat, ProbeResult LancacheDns, ProbeResult AdGuard, ProbeResult Disk, DiskDto? CacheDisk);

public sealed record CountEntry(string Name, long Count);

public sealed record UpstreamEntry(string Upstream, long Responses, double AvgMs);

/// <summary>A client asking AdGuard directly for a domain lancache serves: it is not using lancache-dns, so it bypasses the cache.</summary>
public sealed record BypassClient(string ClientIp, string? ClientName, int Queries, IReadOnlyList<string> Domains, long LastUnix);

public sealed record AdGuardDto(
    bool Configured,
    bool Reachable,
    string? Error,
    string? Version,
    bool ProtectionEnabled,
    string TimeUnits,
    long Queries,
    long Blocked,
    double AvgProcessingMs,
    IReadOnlyList<long> QueriesSeries,
    IReadOnlyList<CountEntry> TopClients,
    IReadOnlyList<CountEntry> TopDomains,
    IReadOnlyList<UpstreamEntry> Upstreams,
    IReadOnlyList<BypassClient> Bypass,
    long? CheckedUnix);

public enum ArtKeySource { None, Environment, Settings }

/// <summary>SteamGridDB key status. The key itself never leaves the server; only its last four characters do.</summary>
public sealed record ArtSettingsDto(ArtKeySource Source, string? MaskedKey, bool OverridesEnvironment);

public sealed record ArtKeyRequest(string ApiKey);
