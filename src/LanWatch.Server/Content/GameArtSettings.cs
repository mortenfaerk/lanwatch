using System.Net;
using System.Net.Http.Headers;
using LanWatch.Server.Data;
using LanWatch.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Content;

/// <summary>
/// The SteamGridDB API key. A key saved in Settings wins; otherwise the <c>STEAMGRIDDB_API_KEY</c> environment
/// variable applies. The key never leaves the server: the UI only sees its last four characters.
/// </summary>
public sealed class GameArtSettings(
    IDbContextFactory<LanWatchDb> dbFactory,
    IOptions<LanWatchOptions> options,
    IHttpClientFactory httpFactory)
{
    public const string SettingKey = "art.steamgriddb.key";

    private string? _saved;

    /// <summary>Raised when the effective key changes, so cached "no art" answers can be retried.</summary>
    public event Action? Changed;

    public string? ApiKey => !string.IsNullOrWhiteSpace(_saved) ? _saved
        : !string.IsNullOrWhiteSpace(options.Value.SteamGridDbApiKey) ? options.Value.SteamGridDbApiKey
        : null;

    public ArtKeySource Source => !string.IsNullOrWhiteSpace(_saved) ? ArtKeySource.Settings
        : !string.IsNullOrWhiteSpace(options.Value.SteamGridDbApiKey) ? ArtKeySource.Environment
        : ArtKeySource.None;

    public ArtSettingsDto ToDto() => new(Source, Mask(ApiKey), Source == ArtKeySource.Settings && !string.IsNullOrWhiteSpace(options.Value.SteamGridDbApiKey));

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        _saved = await db.Settings.Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
    }

    /// <summary>Checks the key against SteamGridDB, then stores it. Returns an error message, or null on success.</summary>
    public async Task<string?> SaveAsync(string key, CancellationToken ct)
    {
        key = key.Trim();
        if (key.Length is < 16 or > 128 || key.Any(char.IsWhiteSpace))
            return "That does not look like a SteamGridDB API key.";

        var problem = await ValidateAsync(key, ct);
        if (problem is not null) return problem;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Settings.AsTracking().FirstOrDefaultAsync(s => s.Key == SettingKey, ct);
        if (row is null) db.Settings.Add(row = new Setting { Key = SettingKey });
        row.Value = key;
        await db.SaveChangesAsync(ct);
        _saved = key;
        Changed?.Invoke();
        return null;
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Settings.Where(s => s.Key == SettingKey).ExecuteDeleteAsync(ct);
        _saved = null;
        Changed?.Invoke();
    }

    private async Task<string?> ValidateAsync(string key, CancellationToken ct)
    {
        try
        {
            // The grids endpoint always enforces the key (search can answer anonymous requests), so validate against it.
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/grids/game/36434?dimensions=460x215");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await httpFactory.CreateClient("art").SendAsync(request, ct);
            return response.StatusCode switch
            {
                _ when response.IsSuccessStatusCode => null,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "SteamGridDB rejected that key. Copy it again from your SteamGridDB preferences.",
                _ => $"SteamGridDB answered {(int)response.StatusCode}; try again in a moment.",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            return "Could not reach SteamGridDB to check the key. Is the VM online?";
        }
    }

    internal static string? Mask(string? key) =>
        string.IsNullOrEmpty(key) ? null : key.Length <= 4 ? new string('•', key.Length) : $"••••{key[^4..]}";
}
