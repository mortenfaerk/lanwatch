using LanWatch.Server.Data;
using LanWatch.Server.Ingestion;
using LanWatch.Shared.Parsing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LanWatch.Tests.Ingestion;

public sealed class LogFileReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lanwatch-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Reads_only_complete_lines_and_resumes()
    {
        var path = Path.Combine(_dir, "access.log");
        File.WriteAllText(path, "one\ntwo\nthr");

        var first = LogFileReader.Read(path, 0, "", 1 << 20)!;
        Assert.Equal(["one", "two"], first.Lines);

        File.AppendAllText(path, "ee\nfour\n");
        var second = LogFileReader.Read(path, first.Offset, first.Fingerprint, 1 << 20)!;
        Assert.Equal(["three", "four"], second.Lines);
        Assert.False(second.Rotated);
    }

    [Fact]
    public void Detects_rotation_and_drains_previous_file()
    {
        var path = Path.Combine(_dir, "access.log");
        File.WriteAllText(path, "old-1\nold-2\n");
        var first = LogFileReader.Read(path, 0, "", 1 << 20)!;

        // nginx wrote one more line, then logrotate renamed the file and a new one started.
        File.AppendAllText(path, "old-3\n");
        File.Move(path, path + ".1");
        File.WriteAllText(path, "new-1\n");

        var next = LogFileReader.Read(path, first.Offset, first.Fingerprint, 1 << 20)!;
        Assert.True(next.Rotated);
        Assert.Equal(["old-3", "new-1"], next.Lines);
    }

    [Fact]
    public void Truncated_file_restarts_from_zero()
    {
        var path = Path.Combine(_dir, "access.log");
        File.WriteAllText(path, "aaaa\nbbbb\n");
        var first = LogFileReader.Read(path, 0, "", 1 << 20)!;

        File.WriteAllText(path, "cc\n");
        var next = LogFileReader.Read(path, first.Offset, first.Fingerprint, 1 << 20)!;
        Assert.True(next.Rotated);
        Assert.Equal(["cc"], next.Lines);
    }
}

public class EventClusterTests
{
    [Fact]
    public void Splits_on_gap()
    {
        const long h = 3600;
        var clusters = EventDetector.Cluster([(0, 10), (h, 10), (20 * h, 5), (21 * h, 5)], 12 * h).ToList();

        Assert.Equal([(0L, 2 * h, 20L), (20 * h, 22 * h, 10L)], clusters);
    }
}

public sealed class AggregatorTests : IDisposable
{
    private readonly SqliteConnection _conn;

    public AggregatorTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        using var db = new LanWatchDb(new DbContextOptionsBuilder<LanWatchDb>().UseSqlite(_conn).Options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private static string Line(string time, string depot, string status = "MISS", long bytes = 1000, string client = "10.0.0.5") =>
        $"[steam] {client} / - - - [{time} +0100] \"GET /depot/{depot}/chunk/abc HTTP/1.1\" 200 {bytes} \"-\" \"Valve/Steam HTTP Client 1.0\" \"{status}\" \"cache1-fra1.steamcontent.com\" \"-\"";

    [Fact]
    public void Splits_sessions_on_gap_and_counts_hits()
    {
        var agg = new Aggregator(new ClientResolver([]), TimeSpan.FromMinutes(5), null);
        Assert.True(agg.AddAccess(Line("11/Oct/2025:10:00:00", "100")));
        Assert.True(agg.AddAccess(Line("11/Oct/2025:10:03:00", "100", "HIT", 500)));
        Assert.True(agg.AddAccess(Line("11/Oct/2025:10:20:00", "100"))); // 17 min gap: new session

        using (var tx = _conn.BeginTransaction())
        {
            agg.Flush(_conn, tx);
            tx.Commit();
        }

        using var db = new LanWatchDb(new DbContextOptionsBuilder<LanWatchDb>().UseSqlite(_conn).Options);
        var sessions = db.Downloads.OrderBy(d => d.StartUnix).ToList();
        Assert.Equal(2, sessions.Count);
        Assert.Equal(500, sessions[0].HitBytes);
        Assert.Equal(1000, sessions[0].MissBytes);
        Assert.Equal(2, sessions[0].Requests);
        Assert.Equal(1, sessions[1].Requests);

        Assert.Equal(500, db.TrafficMinutes.Sum(t => t.HitBytes));
        Assert.Equal(2000, db.TrafficMinutes.Sum(t => t.MissBytes));
        Assert.Single(db.Clients);
    }

    [Fact]
    public void Flushing_twice_accumulates_into_same_session()
    {
        var agg = new Aggregator(new ClientResolver([]), TimeSpan.FromMinutes(5), null);
        agg.AddAccess(Line("11/Oct/2025:10:00:00", "100"));
        using (var tx = _conn.BeginTransaction()) { agg.Flush(_conn, tx); tx.Commit(); }
        agg.AddAccess(Line("11/Oct/2025:10:01:00", "100"));
        using (var tx = _conn.BeginTransaction()) { agg.Flush(_conn, tx); tx.Commit(); }

        using var db = new LanWatchDb(new DbContextOptionsBuilder<LanWatchDb>().UseSqlite(_conn).Options);
        var s = Assert.Single(db.Downloads);
        Assert.Equal(2, s.Requests);
        Assert.Equal(2000, s.MissBytes);
    }

    [Fact]
    public void Error_log_time_uses_access_log_offset()
    {
        var agg = new Aggregator(new ClientResolver([]), TimeSpan.FromMinutes(5), null);
        agg.AddAccess(Line("11/Oct/2025:10:00:00", "100"));
        Assert.True(agg.AddError("2025/10/11 10:00:00 [error] 1#1: *1 upstream timed out (110: Connection timed out) while connecting to upstream, client: 127.0.0.1, server: , host: \"x\"", ErrorSource.Upstream));
        using (var tx = _conn.BeginTransaction()) { agg.Flush(_conn, tx); tx.Commit(); }

        using var db = new LanWatchDb(new DbContextOptionsBuilder<LanWatchDb>().UseSqlite(_conn).Options);
        var e = Assert.Single(db.Errors);
        Assert.Equal(new DateTimeOffset(2025, 10, 11, 9, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), e.Unix);
        Assert.Equal(ErrorKind.UpstreamTimeout, e.Kind);
    }
}
