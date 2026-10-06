using LanWatch.Shared.Parsing;
using Microsoft.Data.Sqlite;

namespace LanWatch.Server.Ingestion;

/// <summary>
/// Turns parsed log lines into deltas for the aggregate tables and writes them in one transaction, together with
/// the file cursors. Raw lines are never stored. Not thread-safe; owned by <see cref="IngestionService"/>.
/// </summary>
public sealed class Aggregator(ClientResolver clients, TimeSpan sessionGap, TimeSpan? errorLogOffset, IIngestObserver? observer = null)
{
    private readonly long _gapSeconds = (long)sessionGap.TotalSeconds;

    private readonly Dictionary<(long Minute, string Client, string Service), TrafficDelta> _traffic = [];
    private readonly Dictionary<(long Minute, string Service, int Status), long> _statuses = [];
    private readonly Dictionary<(string Client, string Service, string Content), OpenSession> _openSessions = [];
    private readonly List<OpenSession> _closedSessions = [];
    private readonly Dictionary<string, (long First, long Last)> _clientsSeen = [];
    private readonly List<PendingError> _errors = [];
    private readonly Dictionary<(long Minute, string Client, string Sni, int Status), StreamDelta> _streams = [];

    // nginx error logs have no UTC offset; we take the one the access log uses (same container, same TZ).
    private TimeSpan? _learnedOffset;

    /// <summary>Timestamp of the newest access line seen, used to prune finished sessions.</summary>
    public long NewestUnix { get; private set; }

    public bool AddAccess(string line)
    {
        if (!AccessLogParser.TryParse(line, out var e)) return false;
        _learnedOffset = e.Timestamp.Offset;
        if (e.IsHeartbeat) return true;

        var unix = e.Timestamp.ToUnixTimeSeconds();
        var minute = unix / 60 * 60;
        var client = clients.Resolve(e.RemoteAddr, e.ForwardedFor);
        var content = ContentResolver.Resolve(e.CacheId, e.Host, e.Path);
        if (unix > NewestUnix) NewestUnix = unix;

        // 508 = Loop Detected: the cache identifier is an IP, meaning somebody browsed to the cache itself.
        if (e.Status == 508)
        {
            Count(_statuses, (minute, "loop", e.Status));
            _errors.Add(new PendingError(unix, ErrorSource.Access, ErrorKind.LoopDetected, client, e.Host, null,
                $"Loop detected: request for {e.Host}{e.Path} came back into the cache"));
            return true;
        }

        Count(_statuses, (minute, content.Service, e.Status));
        SeeClient(client, unix);

        var hit = e.CacheStatus == CacheStatus.Hit;
        var counted = e.CacheStatus != CacheStatus.None;
        ref var t = ref GetDelta(_traffic, (minute, client, content.Service));
        if (counted)
        {
            if (hit) { t.HitBytes += e.Bytes; t.HitRequests++; }
            else { t.MissBytes += e.Bytes; t.MissRequests++; }
        }
        if (e.Status >= 500) t.ErrorRequests++;

        if (counted)
        {
            TrackSession(client, content, unix, hit ? e.Bytes : 0, hit ? 0 : e.Bytes);
            observer?.OnTraffic(new TrafficObservation(e.Timestamp, client, content, hit, e.Bytes));
        }
        return true;
    }

    public bool AddStream(string line)
    {
        if (!StreamLogParser.TryParse(line, out var e)) return false;
        var unix = e.Timestamp.ToUnixTimeSeconds();
        SeeClient(e.ClientIp, unix);
        ref var s = ref GetDelta(_streams, (unix / 60 * 60, e.ClientIp, e.SniHost ?? "", e.Status));
        s.Connections++;
        s.BytesSent += e.BytesSent;
        s.BytesReceived += e.BytesReceived;
        return true;
    }

    public bool AddError(string line, ErrorSource source)
    {
        if (!ErrorLogParser.TryParse(line, out var e)) return false;
        var offset = errorLogOffset ?? _learnedOffset ?? TimeZoneInfo.Local.GetUtcOffset(e.LocalTime);
        var unix = new DateTimeOffset(e.LocalTime, offset).ToUnixTimeSeconds();
        var message = e.Message.Length > 300 ? e.Message[..300] : e.Message;
        _errors.Add(new PendingError(unix, source, e.Kind, e.Client, e.Host, e.Upstream, message));
        observer?.OnError(source, e.Kind, unix);
        return true;
    }

    private void SeeClient(string ip, long unix)
    {
        _clientsSeen[ip] = _clientsSeen.TryGetValue(ip, out var seen)
            ? (Math.Min(seen.First, unix), Math.Max(seen.Last, unix))
            : (unix, unix);
    }

    private void TrackSession(string client, ContentKey content, long unix, long hitBytes, long missBytes)
    {
        var key = (client, content.Service, content.Id);
        if (!_openSessions.TryGetValue(key, out var s) || unix - s.LastUnix > _gapSeconds)
        {
            if (s is not null && s.Dirty) _closedSessions.Add(s);
            s = new OpenSession(client, content.Service, content.Id, unix);
            _openSessions[key] = s;
        }
        if (unix > s.LastUnix) s.LastUnix = unix;
        s.HitBytes += hitBytes;
        s.MissBytes += missBytes;
        s.Requests++;
    }

    /// <summary>Re-opens sessions that were still active when the process last stopped.</summary>
    public void LoadOpenSessions(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ClientIp, Service, ContentId, StartUnix, LastUnix FROM Downloads
            WHERE LastUnix >= (SELECT IFNULL(MAX(LastUnix), 0) FROM Downloads) - $gap
            """;
        cmd.Parameters.AddWithValue("$gap", _gapSeconds);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var s = new OpenSession(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3)) { LastUnix = r.GetInt64(4) };
            _openSessions[(s.Client, s.Service, s.Content)] = s;
            if (s.LastUnix > NewestUnix) NewestUnix = s.LastUnix;
        }
    }

    /// <summary>Writes all pending deltas inside <paramref name="tx"/> and clears them.</summary>
    public void Flush(SqliteConnection conn, SqliteTransaction tx)
    {
        FlushTraffic(conn, tx);
        FlushStatuses(conn, tx);
        FlushSessions(conn, tx);
        FlushClients(conn, tx);
        FlushErrors(conn, tx);
        FlushStreams(conn, tx);
    }

    private void FlushTraffic(SqliteConnection conn, SqliteTransaction tx)
    {
        if (_traffic.Count == 0) return;
        using var cmd = Command(conn, tx, """
            INSERT INTO TrafficMinutes (Minute, ClientIp, Service, HitBytes, MissBytes, HitRequests, MissRequests, ErrorRequests)
            VALUES ($m, $c, $s, $hb, $mb, $hr, $mr, $er)
            ON CONFLICT (Minute, ClientIp, Service) DO UPDATE SET
              HitBytes = HitBytes + excluded.HitBytes, MissBytes = MissBytes + excluded.MissBytes,
              HitRequests = HitRequests + excluded.HitRequests, MissRequests = MissRequests + excluded.MissRequests,
              ErrorRequests = ErrorRequests + excluded.ErrorRequests
            """, "$m", "$c", "$s", "$hb", "$mb", "$hr", "$mr", "$er");
        foreach (var ((minute, client, service), d) in _traffic)
            Execute(cmd, minute, client, service, d.HitBytes, d.MissBytes, d.HitRequests, d.MissRequests, d.ErrorRequests);
        _traffic.Clear();
    }

    private void FlushStatuses(SqliteConnection conn, SqliteTransaction tx)
    {
        if (_statuses.Count == 0) return;
        using var cmd = Command(conn, tx, """
            INSERT INTO StatusMinutes (Minute, Service, Status, Count) VALUES ($m, $s, $st, $n)
            ON CONFLICT (Minute, Service, Status) DO UPDATE SET Count = Count + excluded.Count
            """, "$m", "$s", "$st", "$n");
        foreach (var ((minute, service, status), n) in _statuses)
            Execute(cmd, minute, service, status, n);
        _statuses.Clear();
    }

    private void FlushSessions(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = Command(conn, tx, """
            INSERT INTO Downloads (ClientIp, Service, ContentId, StartUnix, LastUnix, HitBytes, MissBytes, Requests)
            VALUES ($c, $s, $id, $start, $last, $hb, $mb, $n)
            ON CONFLICT (ClientIp, Service, ContentId, StartUnix) DO UPDATE SET
              LastUnix = MAX(LastUnix, excluded.LastUnix), HitBytes = HitBytes + excluded.HitBytes,
              MissBytes = MissBytes + excluded.MissBytes, Requests = Requests + excluded.Requests
            """, "$c", "$s", "$id", "$start", "$last", "$hb", "$mb", "$n");

        void Write(OpenSession s)
        {
            Execute(cmd, s.Client, s.Service, s.Content, s.StartUnix, s.LastUnix, s.HitBytes, s.MissBytes, s.Requests);
            s.HitBytes = s.MissBytes = s.Requests = 0;
        }

        foreach (var s in _closedSessions) Write(s);
        _closedSessions.Clear();

        List<(string, string, string)>? finished = null;
        foreach (var (key, s) in _openSessions)
        {
            if (s.Dirty) Write(s);
            if (NewestUnix - s.LastUnix > _gapSeconds) (finished ??= []).Add(key);
        }
        if (finished is not null)
            foreach (var key in finished) _openSessions.Remove(key);
    }

    private void FlushClients(SqliteConnection conn, SqliteTransaction tx)
    {
        if (_clientsSeen.Count == 0) return;
        using var cmd = Command(conn, tx, """
            INSERT INTO Clients (Ip, FirstSeenUnix, LastSeenUnix) VALUES ($ip, $f, $l)
            ON CONFLICT (Ip) DO UPDATE SET
              FirstSeenUnix = MIN(FirstSeenUnix, excluded.FirstSeenUnix), LastSeenUnix = MAX(LastSeenUnix, excluded.LastSeenUnix)
            """, "$ip", "$f", "$l");
        foreach (var (ip, (first, last)) in _clientsSeen)
            Execute(cmd, ip, first, last);
        _clientsSeen.Clear();
    }

    private void FlushErrors(SqliteConnection conn, SqliteTransaction tx)
    {
        if (_errors.Count == 0) return;
        using var cmd = Command(conn, tx, """
            INSERT INTO Errors (Unix, Source, Kind, ClientIp, Host, Upstream, Message)
            VALUES ($u, $src, $k, $c, $h, $up, $msg)
            """, "$u", "$src", "$k", "$c", "$h", "$up", "$msg");
        foreach (var e in _errors)
            Execute(cmd, e.Unix, (int)e.Source, (int)e.Kind, e.Client, e.Host, e.Upstream, e.Message);
        _errors.Clear();
    }

    private void FlushStreams(SqliteConnection conn, SqliteTransaction tx)
    {
        if (_streams.Count == 0) return;
        using var cmd = Command(conn, tx, """
            INSERT INTO StreamMinutes (Minute, ClientIp, SniHost, Status, Connections, BytesSent, BytesReceived)
            VALUES ($m, $c, $sni, $st, $n, $tx, $rx)
            ON CONFLICT (Minute, ClientIp, SniHost, Status) DO UPDATE SET
              Connections = Connections + excluded.Connections, BytesSent = BytesSent + excluded.BytesSent,
              BytesReceived = BytesReceived + excluded.BytesReceived
            """, "$m", "$c", "$sni", "$st", "$n", "$tx", "$rx");
        foreach (var ((minute, client, sni, status), d) in _streams)
            Execute(cmd, minute, client, sni, status, d.Connections, d.BytesSent, d.BytesReceived);
        _streams.Clear();
    }

    private static SqliteCommand Command(SqliteConnection conn, SqliteTransaction tx, string sql, params string[] parameters)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(new SqliteParameter { ParameterName = p });
        cmd.Prepare();
        return cmd;
    }

    private static void Execute(SqliteCommand cmd, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    private static void Count<TKey>(Dictionary<TKey, long> d, TKey key) where TKey : notnull =>
        System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(d, key, out _)++;

    private static ref TValue GetDelta<TKey, TValue>(Dictionary<TKey, TValue> d, TKey key) where TKey : notnull =>
        ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(d, key, out _)!;

    private struct TrafficDelta
    {
        public long HitBytes, MissBytes, HitRequests, MissRequests, ErrorRequests;
    }

    private struct StreamDelta
    {
        public long Connections, BytesSent, BytesReceived;
    }

    private sealed class OpenSession(string client, string service, string content, long startUnix)
    {
        public string Client { get; } = client;
        public string Service { get; } = service;
        public string Content { get; } = content;
        public long StartUnix { get; } = startUnix;
        public long LastUnix { get; set; } = startUnix;
        public long HitBytes { get; set; }
        public long MissBytes { get; set; }
        public long Requests { get; set; }
        public bool Dirty => Requests > 0;
    }

    private sealed record PendingError(long Unix, ErrorSource Source, ErrorKind Kind, string? Client, string? Host, string? Upstream, string Message);
}

public readonly record struct TrafficObservation(DateTimeOffset Timestamp, string Client, ContentKey Content, bool Hit, long Bytes);

/// <summary>Receives every ingested line as it is aggregated; used by the live feed.</summary>
public interface IIngestObserver
{
    void OnTraffic(in TrafficObservation observation);
    void OnError(ErrorSource source, ErrorKind kind, long unix);
}
