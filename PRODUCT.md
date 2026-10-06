# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

Blazor WebAssembly client hosted by an ASP.NET Core (.NET 10) server in one container, beside lancache on the same VM. SQLite storage, SignalR for live updates. No Bootstrap and no component library: a custom CSS design system. (User decision.)

## Users

The crew of a small LAN party. The venue caps attendance at 50, and a typical event has 20–30 attendees. Crew members watch LanWatch from:
- crew laptops at the crew/NOC desk, during the event, while running it;
- crew desktop PCs at the venue;
- after the event, to review how it went.

They are technical volunteers who know what a cache hit, a DNS rewrite and an upstream timeout are. There is no attendee-facing or wall-display use.

## Product Purpose

LanWatch monitors the event network, starting with the lancache (lancachenet/monolithic + lancache-dns) and the AdGuard Home instance that lancache-dns forwards to. AdGuard rewrites DNS so internal services get LAN-only addresses with SSL. LanWatch answers, at a glance and live:
- Is the cache working?
- Who is downloading what?
- How much internet traffic did the cache save?
- Is anything failing upstream (CDN timeouts, DNS resolution failures, loops, clients bypassing the cache)?

Success: the crew spots a problem before an attendee complains, and can show after the event what the cache delivered.

## Positioning

A crew instrument built for this exact stack. It reads lancache's own logs and links them to the DNS layer behind them:
- per-game downloads (Steam depots resolved to game names);
- hit ratio and bytes saved per event;
- upstream and DNS failure diagnosis.

Inspired by DeveLanCacheUI, but its own product: one container, password-protected, and event-aware history across LAN parties.

## Operating Context

- **Data sources.** lancache logs come from `${CACHE_ROOT}/logs`, mounted read-only:
  - `access.log` in lancache's cachelog format;
  - `stream-access.log` for the 443 SNI passthrough;
  - `error.log`, `upstream-error.log` and `stream-error.log`.
  The AdGuard Home REST API is the DNS source.
- **Events.** One VM is reused across events. History is split into LAN events, which are auto-detected from 12-hour traffic gaps and renamed by the crew.
- **Reading conditions.** Used during the event, a dim hall with long sessions, and in calm daylight review afterwards.
- **Clients** are identified by IP and can be given nicknames, e.g. "Table 4". Some traffic arrives through the Docker gateway (172.18.0.1) with the real client IP lost. This must be surfaced honestly, not hidden.

## Capabilities and Constraints

- **Live:** throughput (hit vs miss), active downloads, new errors, pushed about every 2 s.
- **History per event:**
  - overview totals and hit ratio;
  - throughput timeline;
  - downloads (sessions split on 5-minute gaps);
  - clients with sparklines;
  - top games/content;
  - errors by kind, host and status, with timeline;
  - HTTPS passthrough hosts.
- **Services seen:** steam, epicgames, blizzard, wsus, sony, riot. Steam is ~80% of requests.
- **Error kinds:** upstream timeout, DNS resolve failed (resolver via UPSTREAM_DNS), upstream closed, connection reset, slice status, no SNI host, 508 loop detected.
- **Access:** one shared crew password protects everything; there is no anonymous view.
- **Language:** English only.
- **Phase 5 (planned):** AdGuard panel (status, query stats, upstream latency, clients querying cache domains directly) and active health probes (heartbeat, DNS, cache disk usage).

## Brand Commitments

Neutral crew tool named **LanWatch**. No event branding, logo or sponsor marks.

## Evidence on Hand

- Real logs from three past events (Aug 2025, Oct 2025, Sep 2026; ≈1M access lines, 840 GB served) are in `logs/`. Use them for realistic states, not invented numbers.
- Steam game names and header art come from the SteamDepotFinder dataset and the Steam CDN.
- No screenshots, logos or brand assets exist.

## Product Principles

1. **Problems first.** Failures, bypasses and anomalies outrank vanity totals whenever both compete for attention.
2. **Live by default, history on demand.** The current event is the default scope; past events are one switch away.
3. **Honest data.** Show unknowns as unknowns, such as gateway-hidden clients and unmapped depots. Never smooth over gaps in the logs.
4. **Plain words for crew decisions.** Explain each error class in terms of what to check next, not raw nginx text.
5. **Lightweight.** Runs next to the cache on the same VM; must never compete with it for resources.
