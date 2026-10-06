using System.Collections.Concurrent;
using System.Diagnostics;
using LanWatch.Server.Data;
using LanWatch.Shared.Parsing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Ingestion;

public enum LogKind { Access, Stream, Error }

public sealed record LogSource(string FileName, LogKind Kind, ErrorSource ErrorSource = default);

/// <summary>Ingest progress per file, for the UI and health checks.</summary>
public sealed class IngestStatus
{
    public ConcurrentDictionary<string, FileStatus> Files { get; } = new();
    public bool CaughtUp { get; set; }
    public DateTimeOffset? LastCycle { get; set; }

    /// <summary>Bumped after every committed cycle that ingested lines; lets readers notice new data cheaply.</summary>
    public long Version => Interlocked.Read(ref _version);
    private long _version;
    internal void BumpVersion() => Interlocked.Increment(ref _version);

    public sealed class FileStatus
    {
        public bool Exists { get; set; }
        public long Offset { get; set; }
        public long Length { get; set; }
        public long LinesParsed { get; set; }
        public long LinesFailed { get; set; }
        public int Rotations { get; set; }
    }
}

/// <summary>
/// Tails every lancache log file and folds new lines into the database. On first start it imports the whole
/// history; afterwards it polls about once per second. Each cycle writes aggregates and file cursors in a single
/// transaction, so a crash never double-counts or skips lines.
/// </summary>
public sealed class IngestionService(
    IOptions<LanWatchOptions> options,
    IngestStatus status,
    EventDetector eventDetector,
    IIngestObserver observer,
    ILogger<IngestionService> logger) : BackgroundService
{
    public static readonly LogSource[] Sources =
    [
        new("access.log", LogKind.Access),
        new("stream-access.log", LogKind.Stream),
        new("error.log", LogKind.Error, ErrorSource.Cache),
        new("upstream-error.log", LogKind.Error, ErrorSource.Upstream),
        new("stream-error.log", LogKind.Error, ErrorSource.Stream),
    ];

    private static readonly TimeSpan EventDetectionInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Value;
        TimeSpan? errorOffset = string.IsNullOrWhiteSpace(o.ErrorLogUtcOffset)
            ? null
            : TimeSpan.Parse(o.ErrorLogUtcOffset.TrimStart('+'));
        var aggregator = new Aggregator(new ClientResolver(o.DockerGatewayIps), TimeSpan.FromMinutes(o.SessionGapMinutes),
            errorOffset, observer);

        await using var conn = new SqliteConnection($"Data Source={o.DatabasePath}");
        await conn.OpenAsync(ct);
        aggregator.LoadOpenSessions(conn);
        var cursors = LoadCursors(conn);

        logger.LogInformation("Ingesting lancache logs from {LogsPath}", o.LogsPath);
        var lastEventDetection = DateTimeOffset.MinValue;

        while (!ct.IsCancellationRequested)
        {
            var linesThisCycle = 0;
            var moreWaiting = false;
            var sw = Stopwatch.StartNew();

            try
            {
                using var tx = conn.BeginTransaction();
                foreach (var source in Sources)
                {
                    var (lines, pending) = IngestFile(source, cursors, aggregator, o);
                    linesThisCycle += lines;
                    moreWaiting |= pending;
                }
                aggregator.Flush(conn, tx);
                SaveCursors(conn, tx, cursors);
                tx.Commit();
                if (linesThisCycle > 0) status.BumpVersion();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Roll back to the last committed cursors; the lines are re-read next cycle.
                logger.LogError(ex, "Ingest cycle failed");
                cursors = LoadCursors(conn);
                aggregator = new Aggregator(new ClientResolver(o.DockerGatewayIps), TimeSpan.FromMinutes(o.SessionGapMinutes),
                    errorOffset, observer);
                aggregator.LoadOpenSessions(conn);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                continue;
            }

            if (linesThisCycle > 0 && moreWaiting)
                logger.LogInformation("Imported {Lines} lines in {Ms} ms", linesThisCycle, sw.ElapsedMilliseconds);

            var wasCaughtUp = status.CaughtUp;
            status.CaughtUp = !moreWaiting;
            status.LastCycle = DateTimeOffset.UtcNow;
            if (!wasCaughtUp && status.CaughtUp) logger.LogInformation("Caught up with all log files");

            if (status.CaughtUp && DateTimeOffset.UtcNow - lastEventDetection > EventDetectionInterval)
            {
                lastEventDetection = DateTimeOffset.UtcNow;
                try { await eventDetector.DetectAsync(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Event detection failed"); }
            }

            if (!moreWaiting)
                await Task.Delay(o.PollIntervalMs, ct);
        }
    }

    private (int Lines, bool MoreWaiting) IngestFile(LogSource source, Dictionary<string, IngestCursor> cursors,
        Aggregator aggregator, LanWatchOptions o)
    {
        var fs = status.Files.GetOrAdd(source.FileName, _ => new IngestStatus.FileStatus());
        if (!cursors.TryGetValue(source.FileName, out var cursor))
            cursors[source.FileName] = cursor = new IngestCursor { File = source.FileName };

        var chunk = LogFileReader.Read(Path.Combine(o.LogsPath, source.FileName), cursor.Offset, cursor.Fingerprint, o.ReadChunkBytes);
        fs.Exists = chunk is not null;
        if (chunk is null) return (0, false);

        if (chunk.Rotated)
        {
            fs.Rotations++;
            logger.LogInformation("{File} was rotated; continuing in the new file", source.FileName);
        }

        foreach (var line in chunk.Lines)
        {
            var ok = source.Kind switch
            {
                LogKind.Access => aggregator.AddAccess(line),
                LogKind.Stream => aggregator.AddStream(line),
                _ => aggregator.AddError(line, source.ErrorSource),
            };
            if (ok) fs.LinesParsed++;
            else fs.LinesFailed++;
        }

        if (chunk.Offset != cursor.Offset || chunk.Fingerprint != cursor.Fingerprint)
        {
            cursor.Offset = chunk.Offset;
            cursor.Fingerprint = chunk.Fingerprint;
            cursor.UpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        fs.Offset = chunk.Offset;
        fs.Length = chunk.FileLength;
        // Still importing history if a meaningful amount is left. A small remainder (or a half-written last line)
        // is picked up on the next poll instead of spinning.
        return (chunk.Lines.Count, chunk.Lines.Count > 0 && chunk.FileLength - chunk.Offset > 64 * 1024);
    }

    private static Dictionary<string, IngestCursor> LoadCursors(SqliteConnection conn)
    {
        var result = new Dictionary<string, IngestCursor>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT File, Offset, Fingerprint, UpdatedUnix FROM IngestCursors";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var c = new IngestCursor { File = r.GetString(0), Offset = r.GetInt64(1), Fingerprint = r.GetString(2), UpdatedUnix = r.GetInt64(3) };
            result[c.File] = c;
        }
        return result;
    }

    private static void SaveCursors(SqliteConnection conn, SqliteTransaction tx, Dictionary<string, IngestCursor> cursors)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO IngestCursors (File, Offset, Fingerprint, UpdatedUnix) VALUES ($f, $o, $fp, $u)
            ON CONFLICT (File) DO UPDATE SET Offset = excluded.Offset, Fingerprint = excluded.Fingerprint, UpdatedUnix = excluded.UpdatedUnix
            """;
        foreach (var c in cursors.Values)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$f", c.File);
            cmd.Parameters.AddWithValue("$o", c.Offset);
            cmd.Parameters.AddWithValue("$fp", c.Fingerprint);
            cmd.Parameters.AddWithValue("$u", c.UpdatedUnix);
            cmd.ExecuteNonQuery();
        }
    }
}

/// <summary>Default observer until the live feed is wired in.</summary>
public sealed class NullIngestObserver : IIngestObserver
{
    public void OnTraffic(in TrafficObservation observation) { }
    public void OnError(ErrorSource source, ErrorKind kind, long unix) { }
}
