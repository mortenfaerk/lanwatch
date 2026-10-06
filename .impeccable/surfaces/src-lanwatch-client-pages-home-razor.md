---
version: 1
slug: "src-lanwatch-client-pages-home-razor"
primary_target: "src/LanWatch.Client/Pages/Home.razor"
related_targets: ["src/LanWatch.Client/Layout/MainLayout.razor"]
---

# Overview (LanWatch home)

Scope: the default landing screen after login, and the shell every other page inherits. Mode: **Operate**.

## Audience and task
- **Who:** crew at laptops and desktops at the crew desk during a 20–30 person LAN party, plus after-event review.
- **First glance must answer, in order:**
  1. Is anything broken right now?
  2. Who is downloading what?
- **Then:** how hard the cache is working, and what this event has saved.
- **Constraints:**
  - Dense, not sparse.
  - Must not read as a generic SaaS admin template.
  - Live data pushed every 2 s.
  - Event scope switchable.
  - English only.

## Direction contract

THESIS: The cache is the venue's local warehouse. A hit ships from stock and a miss is imported over the WAN. Every download is a consignment on today's manifest, and every failure is an exception stamped on the docket until a crew member signs it off. It refuses the category default: a grid of equal stat tiles over pastel line charts.

OWN-WORLD:
- **Surfaces.** Manifest-form paper ground (#F5F3EC) with ink (#1B1B19) rules, and a kraft-board rail (#B98E58) as the shell. The dark theme is carbon copy: blue-black ground with a pale carbon impression.
- **Data colour.** Stock green (#3A7D44) for hits, carbon blue (#2D4A8A) for misses, hazard yellow for warnings, stamp red (#C23B2A) for failures. All flat, with no gradients or glows.
- **Type.** Form labels in condensed small caps (Archivo, condensed width). Filled-in data and IDs in a tabular mono (JetBrains Mono), like a docket typed onto a pre-printed form.
- **Components.** Ruled tables, boxed form fields, docket numbers.

STORY: The crew reads the exceptions band first, and an empty band says "No exceptions" plainly. Next they read who is pulling which game and how much comes from stock. They sign off exceptions they have handled. They switch to a past event to review what the cache delivered.

FIRST VIEWPORT (1440×900):
- **Rail (left, 216px).** Kraft rail with the LanWatch wordmark and nav: Overview, Downloads, Clients, Games, Errors, DNS, Events. The ingest/live state sits at the bottom of the rail.
- **Top bar.** Event selector, live pulse, scope dates.
- **Row 1, the exceptions band, full width.** Open exceptions as stamped dockets (kind, host, count, first/last seen, Sign off). When there are none, a single quiet "No exceptions · last 15 min" line.
- **Row 2, left, 8 of 12 columns: "On the dock now".** A dense live table of active downloads, one per row:
  - client label: nickname or IP, with gateway-hidden clients flagged;
  - game, with a 46px header-art thumb;
  - rate;
  - stock/import split bar;
  - session total;
  - docket number.
- **Row 2, right, 4 of 12 columns.**
  - Live throughput trace: 120 s, stacked hit over miss, with the current from-stock and imported rates as large tabular figures.
  - The event ledger: served from cache, pulled from internet, hit ratio, clients, downloads.
- **Row 3, partly below the fold.** Event throughput timeline and service mix.

There is no primary CTA; the main action is Sign off on an exception.

FORM: Freight manifest / warehouse docket (IMPECCABLE'S PICK, my #1 grounded candidate, chosen by the user over the rolled Label Tape & Gaffer and the competitive Live Gate Board). Seed key b53cbb8f. The signature interaction is **the stamp**:
- A new exception lands on the band as an ink-red rubber stamp, set slightly off-axis with one 160 ms thud and no bounce.
- It stays inked (unacknowledged) until it is signed off.
- Once signed off, it fades to a carbon-grey impression with the signer's time.
- Under reduced motion it appears without the thud.

The stock/import split bar on every consignment row is the persistent second voice of the world.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Vocabulary guard
The world supplies type, palette, density and the stamp. Network labels stay plain: "Hit / from cache", "Miss / from internet", "Client", "Game", "Errors". Never "freight", "pallet" or "shipment" in a column header.

## Unresolved
- The AdGuard/DNS panel arrives in Phase 5. Reserve a slot for its status in the exceptions band and the rail.
- Exception sign-off is a new capability: it needs server-side state (acknowledged-at per exception group).
