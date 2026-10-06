using LanWatch.Shared.Contracts;
using LanWatch.Shared.Parsing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Health;

/// <summary>Latest probe and AdGuard results, plus a writer that records failures as exceptions.</summary>
public sealed class HealthState(IOptions<LanWatchOptions> options, TimeProvider time, ILogger<HealthState> logger)
{
    private static readonly ProbeResult Pending = new(ProbeState.Unknown, "Not checked yet", null, 0, null);

    public HealthDto Health { get; set; } = new(Pending, Pending, Pending, Pending, null);

    public AdGuardDto AdGuard { get; set; } =
        new(false, false, null, null, false, "hours", 0, 0, 0, [], [], [], [], [], null);

    /// <summary>
    /// Records a health problem as an error event, so it shows up as an exception the crew can sign off.
    /// Repeats are expected: every failed probe adds one, which keeps a signed-off exception re-opening while the
    /// problem lasts.
    /// </summary>
    public async Task RecordAsync(ErrorKind kind, string? host, string? client, string message, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqliteConnection($"Data Source={options.Value.DatabasePath};Default Timeout=10");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Errors (Unix, Source, Kind, ClientIp, Host, Upstream, Message) VALUES ($u, $s, $k, $c, $h, NULL, $m)";
            cmd.Parameters.AddWithValue("$u", time.GetUtcNow().ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("$s", (int)ErrorSource.Health);
            cmd.Parameters.AddWithValue("$k", (int)kind);
            cmd.Parameters.AddWithValue("$c", (object?)client ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$h", (object?)host ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$m", message.Length > 300 ? message[..300] : message);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqliteException ex)
        {
            logger.LogWarning("Could not record health event: {Message}", ex.Message);
        }
    }
}
