namespace LanWatch.Shared.Parsing;

/// <summary>Allocation-free parsers for the two timestamp shapes nginx writes.</summary>
public static class LogTime
{
    /// <summary>Parses the access/stream log format <c>24/Aug/2025:14:38:24 +0100</c>.</summary>
    public static bool TryParseAccess(ReadOnlySpan<char> s, out DateTimeOffset value)
    {
        value = default;
        if (s.Length < 26 || s[2] != '/' || s[6] != '/' || s[11] != ':' || s[20] != ' ')
            return false;

        if (!TryDigits(s[..2], out var day) ||
            !TryMonth(s.Slice(3, 3), out var month) ||
            !TryDigits(s.Slice(7, 4), out var year) ||
            !TryDigits(s.Slice(12, 2), out var hour) ||
            !TryDigits(s.Slice(15, 2), out var minute) ||
            !TryDigits(s.Slice(18, 2), out var second))
            return false;

        var sign = s[21] switch { '+' => 1, '-' => -1, _ => 0 };
        if (sign == 0 || !TryDigits(s.Slice(22, 2), out var oh) || !TryDigits(s.Slice(24, 2), out var om))
            return false;

        try
        {
            value = new DateTimeOffset(year, month, day, hour, minute, second,
                TimeSpan.FromMinutes(sign * (oh * 60 + om)));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>Parses the error log format <c>2025/08/24 15:47:16</c> (no offset; local container time).</summary>
    public static bool TryParseError(ReadOnlySpan<char> s, out DateTime value)
    {
        value = default;
        if (s.Length < 19 || s[4] != '/' || s[7] != '/' || s[10] != ' ' || s[13] != ':' || s[16] != ':')
            return false;

        if (!TryDigits(s[..4], out var year) ||
            !TryDigits(s.Slice(5, 2), out var month) ||
            !TryDigits(s.Slice(8, 2), out var day) ||
            !TryDigits(s.Slice(11, 2), out var hour) ||
            !TryDigits(s.Slice(14, 2), out var minute) ||
            !TryDigits(s.Slice(17, 2), out var second))
            return false;

        try
        {
            value = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryDigits(ReadOnlySpan<char> s, out int value)
    {
        value = 0;
        foreach (var c in s)
        {
            var d = c - '0';
            if ((uint)d > 9) return false;
            value = value * 10 + d;
        }
        return true;
    }

    private static bool TryMonth(ReadOnlySpan<char> s, out int month)
    {
        month = s switch
        {
            "Jan" => 1, "Feb" => 2, "Mar" => 3, "Apr" => 4, "May" => 5, "Jun" => 6,
            "Jul" => 7, "Aug" => 8, "Sep" => 9, "Oct" => 10, "Nov" => 11, "Dec" => 12,
            _ => 0,
        };
        return month != 0;
    }
}
