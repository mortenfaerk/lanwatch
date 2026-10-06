using System.Text.Json;
using LanWatch.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace LanWatch.Server.Api;

/// <summary>Caches the crew-maintained bits every query needs: client nicknames and IPs excluded from stats.</summary>
public sealed class ClientDirectory(IDbContextFactory<LanWatchDb> dbFactory)
{
    public const string ExcludedIpsSetting = "clients.excluded";

    private Dictionary<string, string>? _names;
    private string[]? _excluded;

    public async Task<IReadOnlyDictionary<string, string>> GetNamesAsync(CancellationToken ct = default)
    {
        if (_names is { } cached) return cached;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return _names = await db.Clients.Where(c => c.Name != null).ToDictionaryAsync(c => c.Ip, c => c.Name!, ct);
    }

    public async Task<string[]> GetExcludedAsync(CancellationToken ct = default)
    {
        if (_excluded is { } cached) return cached;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var json = await db.Settings.Where(s => s.Key == ExcludedIpsSetting).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return _excluded = json is null ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];
    }

    public async Task RenameAsync(string ip, string? name, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var client = await db.Clients.AsTracking().FirstOrDefaultAsync(c => c.Ip == ip, ct);
        if (client is null)
        {
            client = new Data.Client { Ip = ip };
            db.Clients.Add(client);
        }
        client.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await db.SaveChangesAsync(ct);
        _names = null;
    }

    public async Task SetExcludedAsync(IEnumerable<string> ips, CancellationToken ct)
    {
        var clean = ips.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct().Order().ToArray();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var setting = await db.Settings.AsTracking().FirstOrDefaultAsync(s => s.Key == ExcludedIpsSetting, ct);
        if (setting is null) db.Settings.Add(setting = new Setting { Key = ExcludedIpsSetting });
        setting.Value = JsonSerializer.Serialize(clean);
        await db.SaveChangesAsync(ct);
        _excluded = clean;
    }
}
