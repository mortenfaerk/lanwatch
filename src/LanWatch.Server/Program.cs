using System.Security.Cryptography;
using System.Threading.RateLimiting;
using LanWatch.Server;
using LanWatch.Server.Api;
using LanWatch.Server.Content;
using LanWatch.Server.Data;
using LanWatch.Server.Health;
using LanWatch.Server.Ingestion;
using LanWatch.Server.Live;
using LanWatch.Shared.Contracts;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<LanWatchOptions>()
    .Bind(builder.Configuration.GetSection(LanWatchOptions.Section))
    .PostConfigure(o => ApplyFlatEnvironment(o, builder.Configuration));

builder.Services.AddDbContextFactory<LanWatchDb>((sp, db) =>
{
    var o = sp.GetRequiredService<IOptions<LanWatchOptions>>().Value;
    db.UseSqlite($"Data Source={o.DatabasePath}");
    db.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<LanWatchDb>>().CreateDbContext());
builder.Services.AddSingleton(TimeProvider.System);

// Ingestion
builder.Services.AddSingleton<IngestStatus>();
builder.Services.AddSingleton<EventDetector>();
builder.Services.AddSingleton<LiveFeed>();
builder.Services.AddSingleton<IIngestObserver>(sp => sp.GetRequiredService<LiveFeed>());
builder.Services.AddHostedService<IngestionService>();

// Content names and art
builder.Services.AddSingleton<GameArtSettings>();
builder.Services.AddSingleton<ContentCatalog>();
builder.Services.AddSingleton<ArtCache>();
builder.Services.AddSingleton<ClientDirectory>();
builder.Services.AddSingleton<UpdateChecker>();
builder.Services.AddHostedService<SteamDepotMapper>();
builder.Services.AddHttpClient("github", c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd("LanWatch/1.0");
    c.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient("art", c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd("LanWatch/1.0 (+https://github.com/mortenfaerk/lanwatch)");
    c.Timeout = TimeSpan.FromSeconds(10);
});

// Health probes and AdGuard
builder.Services.AddSingleton<HealthState>();
builder.Services.AddHostedService<HealthProbe>();
builder.Services.AddHostedService<AdGuardPoller>();
builder.Services.AddHttpClient("probe", c => c.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddHttpClient("adguard", c => c.Timeout = TimeSpan.FromSeconds(5));

// Live
builder.Services.AddSignalR();
builder.Services.AddHostedService<LiveBroadcaster>();

// Auth: one shared crew password, cookie session. API calls get 401/403 instead of login redirects.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "lanwatch";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // LAN deployments are often plain HTTP
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

await InitializeDatabaseAsync(app.Services);
await app.Services.GetRequiredService<GameArtSettings>().LoadAsync();
EnsurePassword(app);

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();

// Behind a reverse proxy (nginx with TLS): take the client IP and scheme from X-Forwarded-*, so the login rate
// limit is per client rather than per proxy and the session cookie is marked Secure. Only proxies on the
// TRUSTED_PROXIES networks are believed (private ranges by default), so a client cannot spoof its address.
app.UseForwardedHeaders(ForwardedHeadersFor(app.Configuration));

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Fingerprinted, pre-compressed WASM assets.
app.MapStaticAssets();
app.MapAuthEndpoints();
app.MapQueryEndpoints();
app.MapExceptionEndpoints();
app.MapGet("/api/health", (HealthState h) => h.Health).RequireAuthorization();
app.MapGet("/api/version", (bool? refresh, UpdateChecker updates, CancellationToken ct) => updates.GetAsync(refresh == true, ct)).RequireAuthorization();
app.MapGet("/api/adguard", (HealthState h) => h.AdGuard).RequireAuthorization();
app.MapHub<LiveHub>(LiveHubContract.Path);
app.MapGet("/healthz", (IngestStatus s) => Results.Ok(new { s.CaughtUp, s.LastCycle })).AllowAnonymous();
app.MapFallbackToFile("index.html");

app.Run();

static ForwardedHeadersOptions ForwardedHeadersFor(IConfiguration config)
{
    var options = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
    };
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    var trusted = config["TRUSTED_PROXIES"] is { Length: > 0 } list
        ? Split(list)
        : ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];
    foreach (var entry in trusted)
    {
        if (System.Net.IPNetwork.TryParse(entry, out var network)) options.KnownIPNetworks.Add(network);
        else if (System.Net.IPAddress.TryParse(entry, out var ip)) options.KnownProxies.Add(ip);
    }
    return options;
}

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    var o = services.GetRequiredService<IOptions<LanWatchOptions>>().Value;
    Directory.CreateDirectory(o.DataPath);
    await using var db = await services.GetRequiredService<IDbContextFactory<LanWatchDb>>().CreateDbContextAsync();
    await db.Database.MigrateAsync();
    // WAL lets the API read while the ingester writes.
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
}

static void EnsurePassword(WebApplication app)
{
    var o = app.Services.GetRequiredService<IOptions<LanWatchOptions>>().Value;
    if (!string.IsNullOrEmpty(o.Password)) return;
    o.Password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).TrimEnd('=');
    app.Logger.LogWarning("LANWATCH_PASSWORD is not set. Generated a password for this run: {Password}", o.Password);
}

// Short, compose-friendly env names (LANWATCH_PASSWORD, LOGS_PATH, ...) next to the standard LanWatch__X form.
static void ApplyFlatEnvironment(LanWatchOptions o, IConfiguration c)
{
    if (c["LANWATCH_PASSWORD"] is { Length: > 0 } password) o.Password = password;
    if (c["LOGS_PATH"] is { Length: > 0 } logs) o.LogsPath = logs;
    if (c["DATA_PATH"] is { Length: > 0 } data) o.DataPath = data;
    if (c["DOCKER_GATEWAY_IPS"] is { Length: > 0 } gw)
        o.DockerGatewayIps = Split(gw);
    if (c["ERROR_LOG_UTC_OFFSET"] is { Length: > 0 } off) o.ErrorLogUtcOffset = off;
    if (c["LANCACHE_URL"] is { Length: > 0 } lc) o.LancacheUrl = lc;
    if (c["LANCACHE_DNS"] is { Length: > 0 } ld) o.LancacheDns = ld;
    if (c["LANCACHE_IP"] is { Length: > 0 } lip) o.LancacheIps = Split(lip);
    if (c["CACHE_PATH"] is { Length: > 0 } cp) o.CachePath = cp;
    if (c["ADGUARD_URL"] is { Length: > 0 } au) o.AdGuardUrl = au;
    if (c["ADGUARD_USER"] is { Length: > 0 } auser) o.AdGuardUser = auser;
    if (c["ADGUARD_PASSWORD"] is { Length: > 0 } apw) o.AdGuardPassword = apw;
    if (c["STEAMGRIDDB_API_KEY"] is { Length: > 0 } sgdb) o.SteamGridDbApiKey = sgdb;
    if (c["LANWATCH_REPOSITORY"] is { Length: > 0 } repo) o.RepositoryUrl = repo.TrimEnd('/');
    if (c["GITHUB_TOKEN"] is { Length: > 0 } gh) o.GitHubToken = gh;
    if (c["ADGUARD_IGNORE_CLIENTS"] is { Length: > 0 } aic) o.AdGuardIgnoreClients = Split(aic);
    o.LogsPath = Path.GetFullPath(o.LogsPath);
    o.DataPath = Path.GetFullPath(o.DataPath);
}

static string[] Split(string list) => list.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries);

public partial class Program;
