using System.Net;
using LanWatch.Server.Data;
using LanWatch.Shared.Contracts;
using LanWatch.Shared.Parsing;
using Microsoft.EntityFrameworkCore;

namespace LanWatch.Server.Api;

/// <summary>
/// Exceptions are error groups (kind + domain) the crew can sign off. In a live scope only the last
/// <see cref="LiveWindowSeconds"/> count; for a past event the whole event is shown as a record.
/// </summary>
public static class ExceptionEndpoints
{
    public const long LiveWindowSeconds = 15 * 60;

    public static void MapExceptionEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/exceptions").RequireAuthorization();
        api.MapGet("", List);
        api.MapPost("/signoff", SignOff);
    }

    private static async Task<ExceptionsDto> List(int? @event, long? from, long? to, LanWatchDb db, TimeProvider time, CancellationToken ct)
    {
        var scope = await QueryEndpoints.ResolveScopeAsync(db, @event, from, to, time, ct);
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var live = scope.ToUnix >= now - 60;
        var windowFrom = live ? Math.Max(scope.FromUnix, now - LiveWindowSeconds) : scope.FromUnix;

        var errors = await db.Errors.Where(e => e.Unix >= windowFrom && e.Unix < scope.ToUnix)
            .Select(e => new { e.Kind, e.Host, e.ClientIp, e.Unix })
            .ToListAsync(ct);
        var signOffs = await db.ExceptionSignOffs.ToDictionaryAsync(s => (s.Kind, s.Domain), s => s.SignedOffUnix, ct);

        var items = errors
            .GroupBy(e => (e.Kind, Domain: DomainOf(e.Host)))
            .Select(g => new ExceptionDto(
                g.Key.Kind.ToString(),
                g.Key.Domain,
                g.Count(),
                g.Select(e => e.Host).Distinct().Count(),
                g.Where(e => e.ClientIp is not null && e.ClientIp != "127.0.0.1").Select(e => e.ClientIp).Distinct().Count(),
                g.Min(e => e.Unix),
                g.Max(e => e.Unix),
                signOffs.TryGetValue(g.Key, out var signed) ? signed : null))
            .OrderByDescending(x => x.Open)
            .ThenByDescending(x => x.FirstUnix) // stable: new errors must not reshuffle the dockets
            .ToList();

        return new ExceptionsDto(live, windowFrom, items);
    }

    private static async Task<IResult> SignOff(SignOffRequest body, LanWatchDb db, TimeProvider time, CancellationToken ct)
    {
        if (!Enum.TryParse<ErrorKind>(body.Kind, out var kind)) return Results.BadRequest("Unknown kind");
        var existing = await db.ExceptionSignOffs.AsTracking().FirstOrDefaultAsync(s => s.Kind == kind && s.Domain == body.Domain, ct);
        if (existing is null) db.ExceptionSignOffs.Add(existing = new ExceptionSignOff { Kind = kind, Domain = body.Domain });
        existing.SignedOffUnix = time.GetUtcNow().ToUnixTimeSeconds();
        await db.SaveChangesAsync(ct);
        return Results.Ok(existing.SignedOffUnix);
    }

    /// <summary>Groups hosts by registrable-ish domain: cache3-fra1.steamcontent.com becomes steamcontent.com.</summary>
    internal static string DomainOf(string? host)
    {
        if (string.IsNullOrEmpty(host)) return "";
        if (IPAddress.TryParse(host, out _)) return host;
        var parts = host.TrimEnd('.').Split('.');
        return parts.Length <= 2 ? host.ToLowerInvariant() : string.Join('.', parts[^2..]).ToLowerInvariant();
    }
}
