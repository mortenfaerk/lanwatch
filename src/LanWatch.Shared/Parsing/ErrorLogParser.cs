using System.Text.RegularExpressions;

namespace LanWatch.Shared.Parsing;

public enum ErrorKind : byte
{
    Other,
    /// <summary>The CDN did not answer in time (connect, header or body).</summary>
    UpstreamTimeout,
    /// <summary>nginx's resolver could not resolve the CDN hostname via UPSTREAM_DNS.</summary>
    DnsResolveFailed,
    UpstreamClosed,
    ConnectionReset,
    /// <summary>The slice module got an unexpected status, usually a 502/504 from upstream.</summary>
    SliceStatus,
    /// <summary>The stream proxy got no SNI, so it had nowhere to forward the connection.</summary>
    NoHostInUpstream,
    ConnectionRefused,
    /// <summary>HTTP 508: a request looped back into the cache, typically a client browsing to the cache IP directly.</summary>
    LoopDetected,
    /// <summary>Health probe: the monolithic heartbeat endpoint did not answer 204.</summary>
    CacheUnreachable,
    /// <summary>Health probe: lancache-dns did not return the cache IP for a cached hostname.</summary>
    CacheDnsWrong,
    /// <summary>Health probe: AdGuard Home's API did not answer.</summary>
    AdGuardUnreachable,
    /// <summary>A client resolved a cache domain through AdGuard directly, bypassing lancache-dns.</summary>
    DnsBypass,
    /// <summary>Health probe: the cache disk is nearly full.</summary>
    DiskLow,
}

public enum ErrorSource : byte
{
    Cache,    // error.log
    Upstream, // upstream-error.log
    Stream,   // stream-error.log
    Access,   // derived from access.log (508 loops)
    Health,   // LanWatch's own probes (heartbeat, DNS, AdGuard, disk)
}

public sealed record ErrorLogEntry(
    DateTime LocalTime,
    string Level,
    ErrorKind Kind,
    string Message,
    string? Client,
    string? Host,
    string? Upstream,
    string? Request);

/// <summary>
/// Parses nginx error-log lines:
/// <c>2025/08/24 15:47:16 [error] 1888#1888: *214732 message, client: X, server: Y, request: "..", upstream: "..", host: ".."</c>.
/// </summary>
public static partial class ErrorLogParser
{
    public static bool TryParse(string line, out ErrorLogEntry entry)
    {
        entry = null!;
        if (line.Length < 22 || !LogTime.TryParseError(line.AsSpan(0, 19), out var time)) return false;

        var r = new LineReader(line.AsSpan(19));
        r.SkipSpaces();
        if (!r.TryReadBracketed(out var level)) return false;
        r.SkipSpaces();
        r.ReadToken(); // pid#tid:
        r.SkipSpaces();
        var rest = r.Rest;
        if (rest.Length > 0 && rest[0] == '*')
        {
            var sp = rest.IndexOf(' ');
            rest = sp < 0 ? [] : rest[(sp + 1)..];
        }

        // The message runs until the first ", key: " pair.
        var restStr = rest.ToString();
        var firstKv = FirstKeyValue().Match(restStr);
        var message = firstKv.Success ? restStr[..firstKv.Index] : restStr;

        string? client = null, host = null, upstream = null, request = null;
        if (firstKv.Success)
        {
            foreach (Match m in KeyValue().Matches(restStr, firstKv.Index))
            {
                var value = m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["v"].Value;
                switch (m.Groups["k"].Value)
                {
                    case "client": client = value; break;
                    case "host": host = value; break;
                    case "upstream": upstream = value; break;
                    case "request": request = value; break;
                }
            }
        }

        var kind = Classify(message);
        if (kind == ErrorKind.DnsResolveFailed && host is null)
        {
            // "cache3-fra1.steamcontent.com could not be resolved (...)": the hostname leads the message.
            var sp = message.IndexOf(' ');
            if (sp > 0) host = message[..sp];
        }

        entry = new ErrorLogEntry(time, level.ToString(), kind, message, client, host, upstream, request);
        return true;
    }

    public static ErrorKind Classify(string message)
    {
        if (message.Contains("could not be resolved", StringComparison.Ordinal)) return ErrorKind.DnsResolveFailed;
        if (message.Contains("timed out", StringComparison.Ordinal)) return ErrorKind.UpstreamTimeout;
        if (message.Contains("prematurely closed", StringComparison.Ordinal)) return ErrorKind.UpstreamClosed;
        if (message.Contains("Connection reset", StringComparison.Ordinal)) return ErrorKind.ConnectionReset;
        if (message.Contains("Connection refused", StringComparison.Ordinal)) return ErrorKind.ConnectionRefused;
        if (message.Contains("in slice response", StringComparison.Ordinal)) return ErrorKind.SliceStatus;
        if (message.StartsWith("no host in upstream", StringComparison.Ordinal)) return ErrorKind.NoHostInUpstream;
        return ErrorKind.Other;
    }

    [GeneratedRegex(@", (client|server|request|upstream|host|bytes from/to client|bytes from/to upstream|subrequest|referrer): ")]
    private static partial Regex FirstKeyValue();

    [GeneratedRegex(@", (?<k>[a-z/ ]+): (?:""(?<q>[^""]*)""|(?<v>[^,]*))")]
    private static partial Regex KeyValue();
}
