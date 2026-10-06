using System.Net;
using LanWatch.Server;
using LanWatch.Server.Api;
using LanWatch.Server.Content;
using LanWatch.Server.Data;
using LanWatch.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LanWatch.Tests.Content;

public class KnownProductTests
{
    [Theory]
    [InlineData("blizzard", "ovw", "Overwatch 2", 2357570L)]
    [InlineData("blizzard", "fenris", "Diablo IV", 2344520L)]
    [InlineData("blizzard", "sc2", "StarCraft II", null)]
    [InlineData("epicgames", "fortnite", "Fortnite", null)]
    [InlineData("riot", "lol", "League of Legends", null)]
    public void Names_launcher_codes(string service, string id, string name, long? steamApp)
    {
        var p = KnownProducts.Find(service, id);
        Assert.NotNull(p);
        Assert.Equal(name, p.Name);
        Assert.Equal(steamApp, p.SteamAppId);
    }

    [Fact]
    public void Marks_launcher_plumbing() => Assert.True(KnownProducts.Find("blizzard", "bnt002")!.Infrastructure);
}

public sealed class ContentCatalogTests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");

    public ContentCatalogTests()
    {
        _conn.Open();
        using var db = Db();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private LanWatchDb Db() => new(new DbContextOptionsBuilder<LanWatchDb>().UseSqlite(_conn).Options);

    private GameArtSettings Art(string? envKey, HttpStatusCode validation = HttpStatusCode.OK) =>
        new(new Factory(Db), Options.Create(new LanWatchOptions { SteamGridDbApiKey = envKey }), new FakeHttp(validation));

    private ContentCatalog Catalog(string? sgdbKey) => new(new Factory(Db), Art(sgdbKey));

    [Fact]
    public async Task Saved_key_overrides_environment_and_clearing_falls_back()
    {
        var art = Art("env-key-0000000000000001");
        Assert.Equal(ArtKeySource.Environment, art.Source);

        Assert.Null(await art.SaveAsync("  saved-key-00000000000abcd ", default));
        Assert.Equal(ArtKeySource.Settings, art.Source);
        Assert.Equal("saved-key-00000000000abcd", art.ApiKey);
        Assert.Equal("••••abcd", art.ToDto().MaskedKey);
        Assert.True(art.ToDto().OverridesEnvironment);

        // A fresh instance (server restart) reads the saved key back.
        var reloaded = Art("env-key-0000000000000001");
        await reloaded.LoadAsync();
        Assert.Equal("saved-key-00000000000abcd", reloaded.ApiKey);

        await reloaded.ClearAsync(default);
        Assert.Equal(ArtKeySource.Environment, reloaded.Source);
    }

    [Fact]
    public async Task Rejected_or_malformed_keys_are_not_saved()
    {
        var art = Art(null, HttpStatusCode.Unauthorized);
        Assert.Contains("rejected", await art.SaveAsync("wrong-key-000000000000000", default));
        Assert.NotNull(await art.SaveAsync("short", default));
        Assert.Equal(ArtKeySource.None, art.Source);
        Assert.Null(art.ToDto().MaskedKey);
    }

    [Fact]
    public async Task Art_comes_from_steam_then_steamgriddb_then_nothing()
    {
        var withoutKey = await Catalog(null).ResolveAsync([("blizzard", "ovw"), ("blizzard", "sc2"), ("blizzard", "bnt002"), ("blizzard", "zzz")]);
        Assert.Equal("/api/art/steam/2357570", withoutKey[("blizzard", "ovw")].ArtUrl);
        Assert.Null(withoutKey[("blizzard", "sc2")].ArtUrl);
        Assert.Null(withoutKey[("blizzard", "bnt002")].ArtUrl);
        Assert.Equal("Battle.net zzz", withoutKey[("blizzard", "zzz")].Name);

        var withKey = await Catalog("key").ResolveAsync([("blizzard", "sc2"), ("blizzard", "bnt002")]);
        Assert.Equal("/api/art/sgdb/blizzard/sc2", withKey[("blizzard", "sc2")].ArtUrl);
        Assert.Null(withKey[("blizzard", "bnt002")].ArtUrl); // plumbing never gets art
    }

    private sealed class Factory(Func<LanWatchDb> create) : IDbContextFactory<LanWatchDb>
    {
        public LanWatchDb CreateDbContext() => create();
    }

    /// <summary>Answers every request with one status code, so key validation never calls SteamGridDB.</summary>
    private sealed class FakeHttp(HttpStatusCode status) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(status));

        private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"success\":true,\"data\":[]}") });
        }
    }
}

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("https://github.com/mortenfaerk/lanwatch", "mortenfaerk", "lanwatch")]
    [InlineData("https://github.com/mortenfaerk/lanwatch.git", "mortenfaerk", "lanwatch")]
    [InlineData("https://github.com/mortenfaerk/lanwatch/", "mortenfaerk", "lanwatch")]
    public void Parses_repository_urls(string url, string owner, string repo)
    {
        Assert.True(UpdateChecker.TryParseRepo(url, out var o, out var r));
        Assert.Equal((owner, repo), (o, r));
    }

    [Theory]
    [InlineData("https://gitlab.com/a/b")]
    [InlineData("https://github.com/onlyowner")]
    [InlineData("not a url")]
    public void Rejects_non_github(string url) => Assert.False(UpdateChecker.TryParseRepo(url, out _, out _));

    [Fact]
    public void Build_knows_its_commit() => Assert.Matches("^[0-9a-f]{40}$", UpdateChecker.Commit!);
}
