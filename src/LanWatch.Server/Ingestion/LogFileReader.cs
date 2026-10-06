using System.Security.Cryptography;
using System.Text;

namespace LanWatch.Server.Ingestion;

/// <summary>Result of one read: complete lines plus the cursor that follows them.</summary>
public sealed record LogChunk(IReadOnlyList<string> Lines, long Offset, string Fingerprint, bool Rotated, long FileLength);

/// <summary>
/// Reads complete lines from a log file that nginx is appending to, resuming from a byte offset. It notices
/// logrotate (the file was replaced or truncated) through a fingerprint of the first line. After a rotation it first
/// drains what is left in <c>file.1</c> so the lines written just before the rotation are not lost.
/// </summary>
public static class LogFileReader
{
    private const int FingerprintMaxBytes = 1024;

    public static LogChunk? Read(string path, long offset, string fingerprint, int maxBytes)
    {
        if (!File.Exists(path)) return null;

        using var fs = Open(path);
        var current = Fingerprint(fs);
        var rotated = fingerprint.Length > 0 && (current != fingerprint || fs.Length < offset);

        if (rotated)
        {
            // Drain the rest of the previous file if logrotate renamed it to .1.
            var previous = path + ".1";
            if (File.Exists(previous))
            {
                using var pfs = Open(previous);
                if (Fingerprint(pfs) == fingerprint && pfs.Length > offset)
                {
                    var (tail, _) = ReadLines(pfs, offset, int.MaxValue, includePartialLastLine: true);
                    var (lines, next) = ReadLines(fs, 0, maxBytes);
                    return new LogChunk([.. tail, .. lines], next, current, true, fs.Length);
                }
            }
            offset = 0;
        }

        if (current.Length == 0)
            return new LogChunk([], 0, "", rotated, fs.Length); // no complete first line yet

        var (read, newOffset) = ReadLines(fs, offset, maxBytes);
        return new LogChunk(read, newOffset, current, rotated, fs.Length);
    }

    private static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);

    private static (List<string> Lines, long Offset) ReadLines(FileStream fs, long offset, int maxBytes, bool includePartialLastLine = false)
    {
        var lines = new List<string>();
        if (offset >= fs.Length) return (lines, offset);

        var toRead = (int)Math.Min(maxBytes, fs.Length - offset);
        var buffer = new byte[toRead];
        fs.Seek(offset, SeekOrigin.Begin);
        var n = fs.ReadAtLeast(buffer, toRead, throwOnEndOfStream: false);
        var span = buffer.AsSpan(0, n);

        // Only consume complete lines; a partial last line is re-read next cycle once nginx finishes writing it.
        var last = span.LastIndexOf((byte)'\n');
        var usable = last < 0 ? (includePartialLastLine ? n : 0) : last + 1;
        if (usable == 0 && n == maxBytes)
        {
            // A single line longer than the chunk; skip it rather than stall forever.
            return (lines, offset + n);
        }

        var text = Encoding.UTF8.GetString(span[..usable]);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length > 0) lines.Add(trimmed);
        }
        return (lines, offset + usable);
    }

    private static string Fingerprint(FileStream fs)
    {
        Span<byte> head = stackalloc byte[FingerprintMaxBytes];
        fs.Seek(0, SeekOrigin.Begin);
        var n = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        var nl = head[..n].IndexOf((byte)'\n');
        if (nl < 0 && n < FingerprintMaxBytes) return "";
        var firstLine = nl < 0 ? head[..n] : head[..nl];
        return Convert.ToHexString(SHA256.HashData(firstLine))[..16];
    }
}
