# LanWatch

A crew dashboard for a LAN party's [lancache](https://lancache.net) and the AdGuard Home behind it. It shows what is downloading, how much the cache saved, and what is failing. It runs as one container next to `lancachenet/monolithic` on the cache VM.

- **Live:** throughput split into from-cache and from-internet, active downloads per client and game, and new errors. All pushed every 2 s.
- **Exceptions:**
  - Upstream timeouts, DNS lookups failing through `UPSTREAM_DNS`, request loops (508), and HTTPS without SNI.
  - Health probes: cache heartbeat, lancache-dns answer, cache disk.
  - From AdGuard: an unreachable AdGuard, and clients that bypass the cache by asking AdGuard for CDN names directly.
  - Each exception stays stamped until a crew member signs it off. A new occurrence opens it again.
- **History by LAN event:** events are detected from 12-hour gaps in traffic, and you can rename or adjust them. History covers downloads, clients with nicknames, games (Steam depots resolved to names and art), errors, and HTTPS passthrough.
- **Access:** one shared crew password protects everything.

Stack: ASP.NET Core (.NET 10), Blazor WebAssembly, SignalR and SQLite. LanWatch reads lancache's own log files read-only and stores only aggregates, so 1M log lines take about 5 s to import.

## Run it next to lancache

1. Put `deploy/docker-compose.lanwatch.yml` next to your lancache `docker-compose.yml`. Then add to the lancache `.env`:
   ```
   LANWATCH_PASSWORD=choose-something
   ADGUARD_URL=http://10.0.0.53      # optional: your AdGuard Home
   ADGUARD_USER=admin
   ADGUARD_PASSWORD=...
   ```
2. Start it:
   ```
   docker compose -f docker-compose.yml -f docker-compose.lanwatch.yml up -d --build
   ```
3. Open `http://<cache-ip>:8080` and sign in with the crew password.

The first start imports the whole log history. Steam game names download from the SteamDepotFinder dataset (about 2.5 MB) and are kept in SQLite, so they still resolve when the event's internet is down. Game art is cached on first view.

If the lancache logs aren't world-readable (`ls -l ${CACHE_ROOT}/logs`), uncomment `user: "0"` in the compose file.

### Configuration

| Variable | Default | Purpose |
|---|---|---|
| `LANWATCH_PASSWORD` | random, logged at start | Shared crew password |
| `LOGS_PATH` | `/logs` | lancache log folder (`${CACHE_ROOT}/logs`) |
| `DATA_PATH` | `/data` | SQLite database and cached art |
| `LANCACHE_URL` | – | e.g. `http://monolithic`; enables the heartbeat probe |
| `LANCACHE_DNS` | – | lancache-dns host or IP; enables the DNS probe |
| `LANCACHE_IP` | – | Expected answer(s) from lancache-dns |
| `CACHE_PATH` | – | Cache volume mounted read-only, for disk usage |
| `ADGUARD_URL` / `ADGUARD_USER` / `ADGUARD_PASSWORD` | – | AdGuard Home API |
| `ADGUARD_IGNORE_CLIENTS` | `LANCACHE_IP` + gateways | Clients allowed to ask AdGuard for cache domains (lancache-dns itself) |
| `DOCKER_GATEWAY_IPS` | `172.17.0.1,172.18.0.1` | Bridge gateways; real client taken from X-Forwarded-For |
| `ERROR_LOG_UTC_OFFSET` | learned from access.log | Offset for nginx error logs, which have none |
| `TZ` | – | Container time zone |

Every option can also be set as `LanWatch__<Name>`; see `src/LanWatch.Server/LanWatchOptions.cs`.

## Develop

```
dotnet test
cd src/LanWatch.Server
LANWATCH_PASSWORD=testpw dotnet run    # Development: reads ../../logs, stores ../../data
```
`src/LanWatch.Server/LanWatch.Server.http` has smoke requests for every endpoint.

Layout:
- `src/LanWatch.Shared`: log parsers and API contracts.
- `src/LanWatch.Server`: ingestion, API, SignalR hub, health and AdGuard pollers.
- `src/LanWatch.Client`: the WebAssembly UI.
- `tests/`: unit and integration tests.

The design system ("freight manifest") is documented in `DESIGN.md`, and product context is in `PRODUCT.md`.

## Known limits

- **Hidden client IPs.** Traffic that reaches monolithic through Docker's userland proxy shows up as the bridge gateway, and the real client IP is lost. LanWatch labels it "Hidden behind Docker".
- **Partial name mapping.** Blizzard, Epic, Riot and PlayStation names come from a small built-in map; unknown product codes show as codes.
- **Time zones.** The event detector works in UTC hours. Default event names use the UTC date.
