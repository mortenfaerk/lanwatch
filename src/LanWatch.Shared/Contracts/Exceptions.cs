namespace LanWatch.Shared.Contracts;

/// <summary>
/// An exception is a group of errors of one kind against one domain (e.g. DNS failures for *.steamcontent.com).
/// It stays open until the crew signs it off; a newer error after the sign-off opens it again.
/// </summary>
public sealed record ExceptionDto(
    string Kind,
    string Domain,
    int Count,
    int Hosts,
    int Clients,
    long FirstUnix,
    long LastUnix,
    long? SignedOffUnix)
{
    public bool Open => SignedOffUnix is null || SignedOffUnix < LastUnix;
}

public sealed record ExceptionsDto(bool Live, long WindowFromUnix, IReadOnlyList<ExceptionDto> Items);

public sealed record SignOffRequest(string Kind, string Domain);

/// <summary>Plain-language explanations of each error kind: what happened and what to check next.</summary>
public static class ErrorKinds
{
    public static string Title(string kind) => kind switch
    {
        "UpstreamTimeout" => "Upstream timeouts",
        "DnsResolveFailed" => "DNS lookups failing",
        "UpstreamClosed" => "Upstream closed early",
        "ConnectionReset" => "Connections reset",
        "SliceStatus" => "Bad upstream status",
        "NoHostInUpstream" => "HTTPS without SNI",
        "ConnectionRefused" => "Upstream refused",
        "LoopDetected" => "Request loop (508)",
        "CacheUnreachable" => "Cache not answering",
        "CacheDnsWrong" => "lancache-dns answer wrong",
        "AdGuardUnreachable" => "AdGuard unreachable",
        "DnsBypass" => "Clients bypassing the cache",
        "DiskLow" => "Cache disk nearly full",
        _ => "Other errors",
    };

    public static string Advice(string kind) => kind switch
    {
        "UpstreamTimeout" => "The CDN did not answer in time. Check the WAN link and whether the uplink is saturated.",
        "DnsResolveFailed" => "The cache could not resolve the CDN hostname through UPSTREAM_DNS. Check AdGuard is up and reachable from the cache.",
        "UpstreamClosed" => "The CDN dropped the connection mid-download. Usually transient; repeated hits point at a flaky CDN node or the WAN.",
        "ConnectionReset" => "A passthrough (443) connection was reset by the remote end. Mostly harmless unless it is constant.",
        "SliceStatus" => "The CDN answered a chunk with an error status (502/504). Clients will retry; watch for a pattern on one host.",
        "NoHostInUpstream" => "A client opened HTTPS to the cache without a hostname (SNI). Often a client browsing to the cache IP directly.",
        "ConnectionRefused" => "The upstream refused the connection. Check firewall rules and that the CDN address is right.",
        "LoopDetected" => "A request for the cache itself came back into the cache. Someone browsed to the cache IP, or DNS points a non-cache name at it.",
        "CacheUnreachable" => "The monolithic heartbeat did not answer. Check the container is running and port 80 is free.",
        "CacheDnsWrong" => "lancache-dns did not hand out the cache IP for a cached name. Check LANCACHE_IP and that the dns container is up.",
        "AdGuardUnreachable" => "LanWatch cannot reach AdGuard's API. If AdGuard is down, every non-cached lookup and the cache's own CDN lookups fail.",
        "DnsBypass" => "These clients ask AdGuard for game CDN names directly, so their downloads skip the cache. Point their DNS at lancache-dns only (and check for IPv6 DNS).",
        "DiskLow" => "The cache volume is almost full. lancache evicts old data, but check CACHE_DISK_SIZE fits the disk.",
        _ => "An error nginx logged that LanWatch does not classify yet.",
    };
}
