using System.Globalization;
using LanWatch.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Ingestion;

/// <summary>
/// Finds LAN events in the traffic history. Hours with cache traffic are grouped into clusters, and a gap of
/// <see cref="LanWatchOptions.EventGapHours"/> separates two clusters. Each cluster becomes an auto event, or extends
/// the auto event that overlaps it. Events the crew edited by hand (<c>IsAuto == false</c>) are never touched.
/// </summary>
public sealed class EventDetector(IDbContextFactory<LanWatchDb> dbFactory, IOptions<LanWatchOptions> options)
{
    public async Task DetectAsync(CancellationToken ct)
    {
        var o = options.Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var hours = await db.TrafficMinutes
            .GroupBy(t => t.Minute / 3600)
            .Select(g => new { Hour = g.Key, Bytes = g.Sum(t => t.HitBytes + t.MissBytes) })
            .OrderBy(h => h.Hour)
            .ToListAsync(ct);

        var clusters = Cluster(hours.Select(h => (h.Hour * 3600, h.Bytes)), o.EventGapHours * 3600L)
            .Where(c => c.Bytes >= o.MinEventBytes)
            .ToList();

        var events = await db.Events.AsTracking().ToListAsync(ct);
        foreach (var (start, end, _) in clusters)
        {
            var overlapping = events.Where(e => e.StartUnix <= end && e.EndUnix >= start).ToList();
            if (overlapping.Count == 0)
            {
                var e = new LanEvent { Name = DefaultName(start), StartUnix = start, EndUnix = end, IsAuto = true };
                db.Events.Add(e);
                events.Add(e);
            }
            else
            {
                foreach (var e in overlapping.Where(e => e.IsAuto))
                {
                    e.StartUnix = Math.Min(e.StartUnix, start);
                    e.EndUnix = Math.Max(e.EndUnix, end);
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Groups hour buckets (start unix, bytes) into clusters separated by at least <paramref name="gapSeconds"/>.</summary>
    internal static IEnumerable<(long Start, long End, long Bytes)> Cluster(IEnumerable<(long HourStart, long Bytes)> hours, long gapSeconds)
    {
        long? start = null;
        long end = 0, bytes = 0;
        foreach (var (hourStart, b) in hours)
        {
            if (start is not null && hourStart - end >= gapSeconds)
            {
                yield return (start.Value, end, bytes);
                start = null;
            }
            if (start is null)
            {
                start = hourStart;
                bytes = 0;
            }
            end = hourStart + 3600;
            bytes += b;
        }
        if (start is not null) yield return (start.Value, end, bytes);
    }

    private static string DefaultName(long startUnix) =>
        "LAN " + DateTimeOffset.FromUnixTimeSeconds(startUnix).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
