using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using LanWatch.Shared.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace LanWatch.Tests.Api;

public sealed class LanWatchFactory : WebApplicationFactory<Program>
{
    public string Root { get; } = Directory.CreateTempSubdirectory("lanwatch-it-").FullName;
    public string LogsDir => Path.Combine(Root, "logs");

    public LanWatchFactory()
    {
        Directory.CreateDirectory(LogsDir);
        var now = DateTimeOffset.Now;
        File.WriteAllLines(Path.Combine(LogsDir, "access.log"),
        [
            Line(now.AddSeconds(-3), "HIT", 5_000_000),
            Line(now.AddSeconds(-2), "MISS", 3_000_000),
        ]);
    }

    public static string Line(DateTimeOffset t, string status, long bytes)
    {
        var off = t.Offset;
        var time = t.ToString("dd/MMM/yyyy:HH:mm:ss", CultureInfo.InvariantCulture)
                   + $" {(off < TimeSpan.Zero ? '-' : '+')}{Math.Abs(off.Hours):00}{Math.Abs(off.Minutes):00}";
        return $"[steam] 10.0.0.42 / - - - [{time}] \"GET /depot/730/chunk/abc HTTP/1.1\" 200 {bytes} \"-\" \"Valve/Steam HTTP Client 1.0\" \"{status}\" \"cache1.steamcontent.com\" \"-\"";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LANWATCH_PASSWORD"] = "pw",
            ["LanWatch:LogsPath"] = LogsDir,
            ["LanWatch:DataPath"] = Path.Combine(Root, "data"),
            ["LanWatch:FetchSteamDepotMap"] = "false",
            ["LanWatch:PollIntervalMs"] = "200",
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { /* sqlite handle may linger */ }
    }
}

public sealed class ApiIntegrationTests(LanWatchFactory factory) : IClassFixture<LanWatchFactory>
{
    private async Task<HttpClient> LoggedInClientAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("pw"));
        login.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Api_requires_login()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"{LiveHubContract.Path}/negotiate?negotiateVersion=1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("wrong"))).StatusCode);

        var me = await client.GetFromJsonAsync<SessionInfo>("/api/auth/me");
        Assert.False(me!.Authenticated);
    }

    [Fact]
    public async Task Ingests_and_serves_overview()
    {
        var client = await LoggedInClientAsync();
        var since = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();

        OverviewDto? overview = null;
        for (var i = 0; i < 50 && (overview?.HitBytes ?? 0) < 5_000_000; i++)
        {
            await Task.Delay(100);
            overview = await client.GetFromJsonAsync<OverviewDto>($"/api/overview?from={since}");
        }
        Assert.True(overview!.HitBytes >= 5_000_000); // the hub test may append another hit
        Assert.Equal(3_000_000, overview.MissBytes);
        Assert.Equal(1, overview.Clients);

        foreach (var path in new[] { "timeseries", "downloads", "clients", "content", "errors", "stream", "events", "settings", "system" })
            Assert.True((await client.GetAsync($"/api/{path}?from={since}")).IsSuccessStatusCode, path);
    }

    [Fact]
    public async Task Hub_pushes_live_ticks()
    {
        var client = await LoggedInClientAsync();
        var cookie = (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("pw"))).Headers.GetValues("Set-Cookie").First().Split(';')[0];

        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, LiveHubContract.Path), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Headers["Cookie"] = cookie;
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();

        var tick = new TaskCompletionSource<LiveTick>();
        hub.On<LiveTick>(LiveHubContract.Tick, t =>
        {
            if (t.Active.Count > 0) tick.TrySetResult(t);
        });
        await hub.StartAsync();

        // Append a fresh line so the live window sees traffic regardless of test ordering.
        File.AppendAllLines(Path.Combine(factory.LogsDir, "access.log"), [LanWatchFactory.Line(DateTimeOffset.Now, "HIT", 1_000_000)]);

        var result = await tick.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("10.0.0.42", result.Active[0].ClientIp);
        Assert.Equal("steam", result.Active[0].Content.Service);
        await hub.DisposeAsync();
    }
}
