namespace LanWatch.Server;

public class LanWatchOptions
{
    public const string Section = "LanWatch";

    /// <summary>Shared crew password (<c>LANWATCH_PASSWORD</c>). If unset, a random one is generated and logged at startup.</summary>
    public string Password { get; set; } = "";

    /// <summary>Directory holding monolithic's logs (<c>${CACHE_ROOT}/logs</c>, mounted read-only).</summary>
    public string LogsPath { get; set; } = "/logs";

    /// <summary>Directory for the SQLite database.</summary>
    public string DataPath { get; set; } = "/data";

    /// <summary>
    /// Docker bridge gateway addresses. Requests from these come through Docker's userland proxy (or loop back
    /// through the cache), so the real client is taken from X-Forwarded-For when present.
    /// </summary>
    public string[] DockerGatewayIps { get; set; } = ["172.17.0.1", "172.18.0.1"];

    /// <summary>A gap longer than this ends a download session.</summary>
    public int SessionGapMinutes { get; set; } = 5;

    /// <summary>A gap in traffic longer than this separates two LAN events.</summary>
    public int EventGapHours { get; set; } = 12;

    /// <summary>Activity clusters with less traffic than this are not turned into events (stray test downloads).</summary>
    public long MinEventBytes { get; set; } = 1L << 30;

    /// <summary>
    /// UTC offset of the nginx error logs, which carry no offset. Leave empty to learn it from the access log.
    /// Format: <c>+01:00</c>.
    /// </summary>
    public string? ErrorLogUtcOffset { get; set; }

    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>Download the Steam depot to game mapping from GitHub. Turn off for air-gapped setups and tests.</summary>
    public bool FetchSteamDepotMap { get; set; } = true;

    /// <summary>Maximum bytes read from one file per ingest cycle; bounds memory and transaction size during import.</summary>
    public int ReadChunkBytes { get; set; } = 8 * 1024 * 1024;

    // ---- Health probes (all optional; an empty value means "not configured") ----

    /// <summary>Base URL of monolithic as seen from LanWatch, e.g. <c>http://monolithic</c> inside the compose network.</summary>
    public string? LancacheUrl { get; set; }

    /// <summary>lancache-dns address (host or IP, optional :port). Probed with a lookup of <see cref="DnsProbeName"/>.</summary>
    public string? LancacheDns { get; set; }

    /// <summary>The cache IP(s) lancache-dns should hand out (LANCACHE_IP). If empty, any private answer passes.</summary>
    public string[] LancacheIps { get; set; } = [];

    public string DnsProbeName { get; set; } = "lancache.steamcontent.com";

    /// <summary>The cache volume mounted read-only, for disk usage.</summary>
    public string? CachePath { get; set; }

    public int ProbeIntervalSeconds { get; set; } = 30;

    // ---- Game art ----

    /// <summary>Optional SteamGridDB API key: art for Battle.net, Epic and Riot games (free key at steamgriddb.com/profile/preferences).</summary>
    public string? SteamGridDbApiKey { get; set; }

    // ---- Updates ----

    /// <summary>Where the LanWatch source lives; used for the GitHub link and the update check.</summary>
    public string RepositoryUrl { get; set; } = "https://github.com/mortenfaerk/lanwatch";

    public string UpdateBranch { get; set; } = "master";

    /// <summary>Optional read-only GitHub token, needed only when the repository is private.</summary>
    public string? GitHubToken { get; set; }

    // ---- AdGuard Home ----

    public string? AdGuardUrl { get; set; }
    public string? AdGuardUser { get; set; }
    public string? AdGuardPassword { get; set; }
    public int AdGuardIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Clients whose cache-domain lookups are expected at AdGuard: lancache-dns itself (it forwards upstream) and the
    /// cache host. Defaults to <see cref="LancacheIps"/> plus the gateway IPs when empty.
    /// </summary>
    public string[] AdGuardIgnoreClients { get; set; } = [];

    public string DatabasePath => Path.Combine(DataPath, "lanwatch.db");
}
