using System.Globalization;

namespace LanWatch.Shared.Parsing;

/// <summary>
/// One line of <c>stream-access.log</c>, the HTTPS/443 SNI passthrough:
/// <c>$remote_addr [$time_local] $protocol $status $ssl_preread_server_name $bytes_sent $bytes_received $session_time</c>.
/// </summary>
public sealed record StreamLogEntry(
    string ClientIp,
    DateTimeOffset Timestamp,
    string Protocol,
    int Status,
    string? SniHost,
    long BytesSent,
    long BytesReceived,
    double SessionSeconds);

public static class StreamLogParser
{
    public static bool TryParse(string line, out StreamLogEntry entry)
    {
        entry = null!;
        var r = new LineReader(line);

        var ip = r.ReadToken();
        r.SkipSpaces();
        if (!r.TryReadBracketed(out var time) || !LogTime.TryParseAccess(time, out var timestamp)) return false;
        r.SkipSpaces();
        var protocol = r.ReadToken();
        r.SkipOneSpace();
        if (!int.TryParse(r.ReadToken(), out var status)) return false;
        r.SkipOneSpace();
        var sni = r.ReadToken(); // empty (double space) when the client sent no SNI
        r.SkipOneSpace();
        long.TryParse(r.ReadToken(), out var sent);
        r.SkipOneSpace();
        long.TryParse(r.ReadToken(), out var received);
        r.SkipOneSpace();
        double.TryParse(r.ReadToken(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds);

        entry = new StreamLogEntry(ip.ToString(), timestamp, protocol.ToString(), status,
            sni.IsEmpty || sni is "-" ? null : sni.ToString(), sent, received, seconds);
        return true;
    }
}
