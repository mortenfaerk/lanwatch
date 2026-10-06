using System.Globalization;

namespace LanWatch.Client.Services;

/// <summary>Number and time formatting, kept in one place so every page speaks the same units.</summary>
public static class Format
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>Decimal units, as network and disk tools show them: 1 GB = 10^9 bytes.</summary>
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "kB", "MB", "GB", "TB"];
        double v = bytes;
        var u = 0;
        while (Math.Abs(v) >= 1000 && u < units.Length - 1)
        {
            v /= 1000;
            u++;
        }
        return u == 0 ? $"{bytes} B" : $"{v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00", C)} {units[u]}";
    }

    public static (string Value, string Unit) BytesParts(long bytes)
    {
        var s = Bytes(bytes);
        var sp = s.LastIndexOf(' ');
        return (s[..sp], s[(sp + 1)..]);
    }

    /// <summary>Throughput in bits per second, the unit the crew's switches and ISP speak.</summary>
    public static string Rate(double bytesPerSecond)
    {
        var (v, u) = RateParts(bytesPerSecond);
        return $"{v} {u}";
    }

    public static (string Value, string Unit) RateParts(double bytesPerSecond)
    {
        var bits = bytesPerSecond * 8;
        return bits switch
        {
            >= 1e9 => ((bits / 1e9).ToString("0.00", C), "Gbit/s"),
            >= 1e6 => ((bits / 1e6).ToString(bits >= 1e8 ? "0" : "0.0", C), "Mbit/s"),
            >= 1e3 => ((bits / 1e3).ToString("0", C), "kbit/s"),
            > 0 => (bits.ToString("0", C), "bit/s"),
            _ => ("0", "Mbit/s"),
        };
    }

    public static string Percent(double fraction) => double.IsFinite(fraction) ? $"{(fraction * 100).ToString("0", C)}%" : "–";

    public static string HitRatio(long hit, long miss) => hit + miss == 0 ? "–" : Percent((double)hit / (hit + miss));

    public static string Count(long n) => n.ToString("#,0", C).Replace(',', ' ');

    public static DateTimeOffset Local(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime();

    public static string Time(long unix) => Local(unix).ToString("HH:mm", C);

    public static string TimeSec(long unix) => Local(unix).ToString("HH:mm:ss", C);

    public static string Day(long unix) => Local(unix).ToString("d MMM yyyy", C);

    public static string DayTime(long unix) => Local(unix).ToString("d MMM HH:mm", C);

    public static string Range(long from, long to)
    {
        var f = Local(from);
        var t = Local(to);
        return f.Date == t.Date
            ? $"{f.ToString("d MMM yyyy", C)}, {f.ToString("HH:mm", C)}–{t.ToString("HH:mm", C)}"
            : $"{f.ToString("d MMM HH:mm", C)} – {t.ToString("d MMM yyyy HH:mm", C)}";
    }

    public static string Duration(long seconds) => seconds switch
    {
        < 60 => $"{Math.Max(0, seconds)} s",
        < 3600 => $"{seconds / 60} min",
        < 86400 => $"{seconds / 3600} h {seconds % 3600 / 60:00} min",
        _ => $"{seconds / 86400} d {seconds % 86400 / 3600} h",
    };

    public static string Ago(long unix)
    {
        var s = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix;
        return s switch
        {
            < 10 => "just now",
            < 60 => $"{s} s ago",
            < 3600 => $"{s / 60} min ago",
            < 86400 => $"{s / 3600} h ago",
            _ => DayTime(unix),
        };
    }

    public static string Docket(long id) => $"DL-{id:00000}";
}
