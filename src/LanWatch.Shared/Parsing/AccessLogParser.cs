namespace LanWatch.Shared.Parsing;

public enum CacheStatus : byte
{
    None,
    Hit,
    Miss,
    Expired,
    Bypass,
    Stale,
    Updating,
    Revalidated,
    Other,
}

/// <summary>
/// One line of the monolithic <c>access.log</c>, written in lancache's <c>cachelog</c> format:
/// <c>[$cacheidentifier] $remote_addr / $http_x_forwarded_for - $remote_user [$time_local] "$request" $status
/// $body_bytes_sent "$http_referer" "$http_user_agent" "$upstream_cache_status" "$host" "$http_range"</c>.
/// </summary>
public sealed record AccessLogEntry(
    string CacheId,
    string RemoteAddr,
    string? ForwardedFor,
    DateTimeOffset Timestamp,
    string Method,
    string Path,
    int Status,
    long Bytes,
    string UserAgent,
    CacheStatus CacheStatus,
    string Host,
    string? Range)
{
    public bool IsHeartbeat => Path == "/lancache-heartbeat";
}

public static class AccessLogParser
{
    public static bool TryParse(string line, out AccessLogEntry entry)
    {
        entry = null!;
        var r = new LineReader(line);

        if (!r.TryReadBracketed(out var cacheId)) return false;
        r.SkipSpaces();

        // remote / xff - user
        var remote = r.ReadToken();
        r.SkipSpaces();
        if (!r.TryExpect('/')) return false;
        r.SkipSpaces();
        var xff = r.ReadToken();
        r.SkipSpaces();
        r.ReadToken(); // literal "-"
        r.SkipSpaces();
        r.ReadToken(); // $remote_user
        r.SkipSpaces();

        if (!r.TryReadBracketed(out var time) || !LogTime.TryParseAccess(time, out var timestamp)) return false;
        r.SkipSpaces();

        if (!r.TryReadQuoted(out var request)) return false;
        r.SkipSpaces();

        if (!int.TryParse(r.ReadToken(), out var status)) return false;
        r.SkipSpaces();
        if (!long.TryParse(r.ReadToken(), out var bytes)) bytes = 0;
        r.SkipSpaces();

        if (!r.TryReadQuoted(out _)) return false; // referer
        r.SkipSpaces();
        if (!r.TryReadQuoted(out var userAgent)) return false;
        r.SkipSpaces();
        if (!r.TryReadQuoted(out var cacheStatus)) return false;
        r.SkipSpaces();
        if (!r.TryReadQuoted(out var host)) return false;
        r.SkipSpaces();
        r.TryReadQuoted(out var range);

        SplitRequest(request, out var method, out var path);

        entry = new AccessLogEntry(
            CacheId: cacheId.ToString(),
            RemoteAddr: remote.ToString(),
            ForwardedFor: NullIfDash(xff),
            Timestamp: timestamp,
            Method: method,
            Path: path,
            Status: status,
            Bytes: bytes,
            UserAgent: userAgent.ToString(),
            CacheStatus: ParseCacheStatus(cacheStatus),
            Host: host.ToString(),
            Range: NullIfDash(range));
        return true;
    }

    public static CacheStatus ParseCacheStatus(ReadOnlySpan<char> s) => s switch
    {
        "HIT" => CacheStatus.Hit,
        "MISS" => CacheStatus.Miss,
        "EXPIRED" => CacheStatus.Expired,
        "BYPASS" => CacheStatus.Bypass,
        "STALE" => CacheStatus.Stale,
        "UPDATING" => CacheStatus.Updating,
        "REVALIDATED" => CacheStatus.Revalidated,
        "" or "-" => CacheStatus.None,
        _ => CacheStatus.Other,
    };

    private static void SplitRequest(ReadOnlySpan<char> request, out string method, out string path)
    {
        // "GET /path HTTP/1.1"; malformed requests (TLS garbage, "-") keep the raw text as the path.
        var first = request.IndexOf(' ');
        var last = request.LastIndexOf(' ');
        if (first <= 0 || last <= first)
        {
            method = "";
            path = request.ToString();
            return;
        }
        method = request[..first].ToString();
        path = request[(first + 1)..last].ToString();
    }

    private static string? NullIfDash(ReadOnlySpan<char> s) => s.IsEmpty || s is "-" ? null : s.ToString();
}
