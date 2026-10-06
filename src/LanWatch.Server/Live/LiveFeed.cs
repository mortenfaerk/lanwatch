using LanWatch.Server.Ingestion;
using LanWatch.Shared.Parsing;

namespace LanWatch.Server.Live;

/// <summary>
/// In-memory view of the last few minutes of traffic. The ingester feeds it, and the broadcaster reads it.
/// Lines older than the window, e.g. during a history import, are ignored.
/// </summary>
public sealed class LiveFeed(TimeProvider time) : IIngestObserver
{
    public const int WindowSeconds = 120;
    private const int ActiveSeconds = 60;

    private readonly Lock _lock = new();
    private readonly Dictionary<long, (long Hit, long Miss)> _seconds = [];
    private readonly Dictionary<(string Client, string Service, string Id), Active> _active = [];
    private int _newErrors;

    public void OnTraffic(in TrafficObservation o)
    {
        var sec = o.Timestamp.ToUnixTimeSeconds();
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        if (sec < now - WindowSeconds) return;

        lock (_lock)
        {
            var s = _seconds.GetValueOrDefault(sec);
            _seconds[sec] = o.Hit ? (s.Hit + o.Bytes, s.Miss) : (s.Hit, s.Miss + o.Bytes);

            var key = (o.Client, o.Content.Service, o.Content.Id);
            if (!_active.TryGetValue(key, out var a)) _active[key] = a = new Active();
            a.LastSeen = Math.Max(a.LastSeen, sec);
            a.Recent.Enqueue((sec, o.Bytes));
            a.TotalBytes += o.Bytes;
            if (o.Hit) a.HitBytes += o.Bytes;
        }
    }

    public void OnError(ErrorSource source, ErrorKind kind, long unix)
    {
        if (unix < time.GetUtcNow().ToUnixTimeSeconds() - WindowSeconds) return;
        Interlocked.Increment(ref _newErrors);
    }

    public Snapshot TakeSnapshot()
    {
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        lock (_lock)
        {
            foreach (var old in _seconds.Keys.Where(k => k < now - WindowSeconds).ToList()) _seconds.Remove(old);
            foreach (var (key, a) in _active.Where(kv => kv.Value.LastSeen < now - ActiveSeconds).ToList()) _active.Remove(key);

            var window = new List<(long T, long Hit, long Miss)>(WindowSeconds);
            for (var t = now - WindowSeconds + 1; t <= now; t++)
            {
                var s = _seconds.GetValueOrDefault(t);
                window.Add((t, s.Hit, s.Miss));
            }

            // Rate over the last 10 complete seconds; the current second is still filling.
            long hit = 0, miss = 0;
            for (var t = now - 10; t < now; t++)
            {
                var s = _seconds.GetValueOrDefault(t);
                hit += s.Hit;
                miss += s.Miss;
            }

            var active = new List<ActiveDownload>();
            foreach (var (key, a) in _active)
            {
                while (a.Recent.TryPeek(out var r) && r.Sec < now - 10) a.Recent.Dequeue();
                var recent = a.Recent.Sum(r => r.Bytes);
                active.Add(new ActiveDownload(key.Client, key.Service, key.Id, recent / 10,
                    a.TotalBytes == 0 ? 0 : (double)a.HitBytes / a.TotalBytes, a.TotalBytes, a.LastSeen));
            }

            return new Snapshot(now, hit / 10, miss / 10, window, active, Interlocked.Exchange(ref _newErrors, 0));
        }
    }

    public sealed record ActiveDownload(string Client, string Service, string Id, long BytesPerSecond, double HitRatio, long TotalBytes, long LastSeen);

    public sealed record Snapshot(long Now, long HitPerSecond, long MissPerSecond, List<(long T, long Hit, long Miss)> Window,
        List<ActiveDownload> Active, int NewErrors);

    private sealed class Active
    {
        public long LastSeen;
        public long TotalBytes;
        public long HitBytes;
        public readonly Queue<(long Sec, long Bytes)> Recent = new();
    }
}
