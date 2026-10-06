---
name: LanWatch
description: A crew instrument for a LAN-party lancache, set as a freight manifest and warehouse docket.
colors:
  paper: "#f5f3ec"
  paper-2: "#ebe7db"
  paper-raised: "#fbfaf6"
  ink: "#1b1b19"
  ink-2: "#47443d"
  ink-3: "#6b675d"
  rule: "#d6d0c1"
  rule-strong: "#8f887a"
  frame: "#2b2a26"
  kraft: "#b98e58"
  kraft-2: "#a97f4a"
  kraft-ink: "#241a0e"
  kraft-ink-2: "#3f2f1b"
  hit: "#3e8a48"
  miss: "#34539a"
  stamp: "#c23b2a"
  stamp-wash: "#f6e3dd"
  on-stamp: "#ffffff"
  hazard: "#e8b500"
  hazard-ink: "#6e5100"
  hazard-wash: "#f8efcc"
  ok: "#2e7d4f"
  svc-steam: "#6a4fc0"
  svc-epicgames: "#a0561a"
  svc-blizzard: "#00879f"
  svc-wsus: "#c2457f"
  svc-other: "#8f887a"
  focus: "#2d4a8a"
  selection: "#c9d4ef"
  row-hover: "#eeeadf"
  carbon-paper: "#131a2a"
  carbon-paper-2: "#192136"
  carbon-paper-raised: "#1c2540"
  carbon-ink: "#dfe5f3"
  carbon-ink-2: "#b0bbd2"
  carbon-ink-3: "#8b97b3"
  carbon-rule: "#2b3552"
  carbon-rule-strong: "#4c5878"
  carbon-frame: "#6c7898"
  carbon-kraft: "#3a2e20"
  carbon-kraft-ink: "#f0e2c8"
  carbon-hit: "#56a35f"
  carbon-miss: "#6f8fdc"
  carbon-stamp: "#e0604f"
  carbon-ok: "#5fb173"
  carbon-focus: "#8fb0ff"
typography:
  display:
    fontFamily: "JetBrains Mono, ui-monospace, monospace"
    fontSize: "1.75rem"
    fontWeight: 500
    lineHeight: 1
    letterSpacing: "-0.03em"
    fontFeature: "tnum, zero"
  amount:
    fontFamily: "JetBrains Mono, ui-monospace, monospace"
    fontSize: "1.4375rem"
    fontWeight: 500
    letterSpacing: "-0.03em"
    fontFeature: "tnum, zero"
  headline:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "1.1875rem"
    fontWeight: 650
    lineHeight: 1.2
    letterSpacing: "-0.01em"
    fontVariation: "'wdth' 88"
  title:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "-0.01em"
  body:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.45
    fontFeature: "tnum"
  data:
    fontFamily: "JetBrains Mono, ui-monospace, monospace"
    fontSize: "0.8125rem"
    letterSpacing: "-0.01em"
    fontFeature: "tnum, zero"
  label:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "0.6875rem"
    fontWeight: 650
    lineHeight: 1.2
    letterSpacing: "0.07em"
    fontVariation: "'wdth' 75"
  stamp:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 800
    lineHeight: 1.2
    letterSpacing: "0.08em"
    fontVariation: "'wdth' 70"
rounded:
  hairline: "2px"
  sm: "3px"
  stamp: "4px"
spacing:
  s-1: "4px"
  s-2: "8px"
  s-3: "12px"
  s-4: "16px"
  s-5: "20px"
  s-6: "24px"
  s-8: "32px"
components:
  button:
    backgroundColor: "{colors.paper-raised}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.sm}"
    padding: "0 12px"
    height: "32px"
  button-hover:
    backgroundColor: "{colors.paper-2}"
  button-primary:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
    rounded: "{rounded.sm}"
    padding: "0 12px"
    height: "32px"
  button-primary-hover:
    backgroundColor: "{colors.ink-2}"
  button-sm:
    padding: "0 8px"
    height: "26px"
  button-on-kraft:
    textColor: "{colors.kraft-ink}"
    rounded: "{rounded.sm}"
  input:
    backgroundColor: "{colors.paper-raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    padding: "0 10px"
    height: "32px"
  panel:
    backgroundColor: "{colors.paper}"
    rounded: "{rounded.sm}"
  panel-head:
    typography: "{typography.title}"
    padding: "0 16px"
    height: "40px"
  manifest-header:
    backgroundColor: "{colors.paper-2}"
    textColor: "{colors.ink-2}"
    typography: "{typography.label}"
    padding: "0 12px"
    height: "30px"
  manifest-row:
    typography: "{typography.data}"
    padding: "4px 12px"
    height: "34px"
  docket:
    backgroundColor: "{colors.paper-raised}"
    rounded: "{rounded.sm}"
    padding: "12px 16px"
  stamp:
    textColor: "{colors.stamp}"
    typography: "{typography.stamp}"
    rounded: "{rounded.stamp}"
    padding: "3px 8px 2px"
  tag:
    textColor: "{colors.ink-2}"
    typography: "{typography.label}"
    rounded: "{rounded.hairline}"
    padding: "0 6px"
    height: "18px"
  nav-link:
    textColor: "{colors.kraft-ink}"
    padding: "0 20px"
    height: "36px"
  nav-link-active:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
  rail:
    backgroundColor: "{colors.kraft}"
    textColor: "{colors.kraft-ink}"
    width: "216px"
---

# Design System: LanWatch

## Overview

**Creative North Star: "The Freight Manifest"**

The cache is the venue's local warehouse. A hit ships from stock, a miss is imported over the WAN, every download is a consignment line on today's manifest, and every failure is an exception stamped on a docket until a crew member signs it off. The interface is a pre-printed manifest form: off-white paper boxed and ruled in near-black ink, filled in with typed tabular mono, held in a kraft-board folder (the rail). The dark theme is the carbon copy of the same form: a blue-black carbon sheet with a pale impression, not an inverted light theme.

Density is high and deliberate. A crew member at a NOC laptop reads ruled tables, boxed form fields and ledger lines, never a grid of equal stat tiles over pastel line charts. Colour is spent almost entirely on data: stock green and carbon blue for hit and miss, stamp red and hazard yellow for trouble. Everything else is paper, ink and kraft.

The world has one signature motion: the rubber stamp landing on a new exception. Beyond that there is only the live pulse, the loading shimmer and 150 ms state transitions.

**Key Characteristics:**
- Paper ground, ink frames, kraft shell; carbon copy in the dark.
- Condensed small-caps form labels over typed tabular-mono data.
- Ruled manifest tables and boxed field grids instead of cards and tiles.
- Hit/miss split bar on every consignment line as the persistent second voice.
- Flat colour throughout: no gradients, no glows, one functional shadow.
- The stamp: ink-red, off-axis, one 160 ms thud.

## Colors

A warm paper-and-ink neutral system with a kraft shell, where saturated colour is reserved for data and status.

### Primary
- **Stock Green** (hit): every byte served from cache. Split bars, stacked chart segments, ledger key, live dot on an active row. Carbon copy shifts it to the lighter carbon-hit.
- **Carbon Blue** (miss): every byte pulled from the internet. Always set beside Stock Green and always labelled "From internet". Carbon copy uses carbon-miss.

### Secondary
- **Kraft Board** (kraft, kraft-2): the rail and the login/boot ground; kraft-2 is the hover and the separator on kraft. Kraft text uses kraft-ink and kraft-ink-2, never ink. In carbon copy the kraft becomes a dark brown board with pale kraft-ink.

### Tertiary (status, reserved)
- **Stamp Red** (stamp, stamp-wash, on-stamp): failures and open exceptions. The stamp, failing tags, the error banner, the open-exceptions count, error segments in the event timeline.
- **Hazard Yellow** (hazard, hazard-ink, hazard-wash): warnings. Hazard fills borders and dots only; text on hazard wash is hazard-ink.
- **Signal Green** (ok): healthy status: the live pulse, passing health checks, the all-clear check icon.
- **Service hues** (svc-steam, svc-epicgames, svc-blizzard, svc-wsus, svc-other): identity, not status. Steam, Epic Games, Blizzard and WSUS each own a hue outside the hit/miss/hazard/stamp families; Sony, Riot and anything rarer fold into svc-other. A service swatch is always followed by its name.

### Neutral
- **Manifest Paper** (paper): the page ground and panel fill. Active nav tab, so the tab runs into the sheet.
- **Form Tint** (paper-2): table header band, button hover, skeleton base.
- **Raised Sheet** (paper-raised): buttons, inputs, open dockets, tooltips: anything that sits on the form rather than being printed on it.
- **Ink** (ink, ink-2, ink-3): primary text, secondary text, muted labels and units.
- **Rules** (rule, rule-strong, frame): rule is the line between table rows and field boxes; rule-strong is input borders and signed-off states; frame is the heavy ink box around panels, dockets, buttons and the top-bar underline.
- **Focus Blue** (focus) and selection: keyboard focus ring and text selection.

### Named Rules
**The Data Owns Colour Rule.** Saturated colour appears only as data (hit, miss, service) or status (stamp, hazard, ok). Chrome is paper, ink and kraft.

**The Validated Pair Rule.** Hit and miss values are the validated pair in both modes (light #3e8a48 / #34539a, dark #56a35f / #6f8fdc). Do not drift them toward the contract's original #3A7D44 / #2D4A8A; the miss step was chosen as the nearest passing value.

**The Reserved Status Rule.** Stamp red, hazard yellow and ok green mean status and nothing else, and each is paired with an icon and a word. Never use them for decoration, for a service, or for a series.

**The Folded Tail Rule.** Only four services own a hue. Everything else is svc-other and is always labelled by name.

## Typography

**Display Font:** Archivo variable (wdth 62–125, wght 100–900), self-hosted, with system-ui fallback
**Body Font:** Archivo
**Label/Mono Font:** JetBrains Mono variable, self-hosted, with ui-monospace fallback

**Character:** A pre-printed form filled in on a typewriter. Archivo, condensed through its width axis, is the printed form: labels, headings, the wordmark. JetBrains Mono with tabular and slashed-zero figures is everything typed in: IPs, rates, sizes, times, docket numbers.

### Hierarchy
- **Display** (JetBrains Mono 500, 1.75rem, line-height 1): live figures in a field box, e.g. current from-cache rate, with the unit set small and muted beside it.
- **Amount** (JetBrains Mono 500, 1.4375rem): ledger amounts and field-grid figures; the unit sits in a fixed-width muted column so amounts align.
- **Headline** (Archivo 650, 1.1875rem in the top bar, width 88%): page title only.
- **Title** (Archivo 700, 0.875rem): panel heads and docket titles.
- **Body** (Archivo 400, 0.875rem, line-height 1.45): prose, advice lines (max 62ch), table text.
- **Data** (JetBrains Mono, 0.8125rem; 0.75rem and 0.6875rem for secondary lines): every machine value.
- **Label** (Archivo 650, 0.6875rem, width 75%, 0.07em, uppercase, ink-3): form-field labels, table column headers, tags.
- **Stamp** (Archivo 800, 0.75rem, width 70%, 0.08em, uppercase): the stamp only.

### Named Rules
**The Typed Entry Rule.** If a machine produced the value, it is set in JetBrains Mono with tabular, slashed-zero figures. If the form printed it, it is Archivo.

**The Label Belongs To A Field Rule.** Small-caps labels name the field, column or group directly beneath them. They are never free-floating kickers above a heading.

## Layout

A fixed shell: a 216px kraft rail on the left, a 52px sticky top bar ruled underneath in frame ink (page title, event picker, live pulse), and a content column padded 20px/24px with a 20px gap between blocks. Spacing runs on a 4px base (4, 8, 12, 16, 20, 24, 32).

Pages sit on a 12-column grid with 20px gutters, using 4/5/6/7/8/12 spans. On the Overview the exceptions band runs full width; below it two independent column stacks (8 and 4) grow at their own heights, so no panel is stretched around empty space. Tables run 34px rows; panel heads are 40px.

Below 1180px every span goes full width. On the Overview the stacks dissolve and panels reorder to read live load before history: dock, live throughput, ledger, timeline, services. Below 760px the rail becomes a horizontal icon strip with a right-edge fade, nav labels hide, the top bar wraps with the event picker on its own line, field grids of four or five drop to two columns, and manifest tables keep a 640px minimum inside a horizontal scroller.

## Elevation & Depth

The system is flat. Depth is a printing question, not a lighting one: panels are boxed by a 1px frame rule, rows by 1px rules, totals by a double rule, and things that sit on the form (buttons, inputs, open dockets, tooltips) use the raised-sheet fill instead of a shadow.

### Shadow Vocabulary
- **Tooltip lift** (`box-shadow: 0 6px 18px -6px rgb(0 0 0 / 0.25)`): chart tooltips only, the one element that floats over content.
- **Live pulse ring** (animated `box-shadow` from ok at 55% to 7px transparent, 2s): the live dot only.

### Named Rules
**The Printed Not Lit Rule.** Separation comes from ink rules and fills. A new surface that needs a shadow is a tooltip or a popover; nothing resting on the page gets one.

## Shapes

Corners are barely softened: 3px on panels, buttons, inputs, dockets and banners; 2px on bars, swatches, tags and thumbnails; 4px on the stamp's border. The only round shapes are status dots and the nav count pill. Borders do the work: 1px frame for boxes, 1px rule for internal lines, 3px double frame for a total, 2px stamp red for the stamp, 1px dashed rule-strong for the quiet all-clear line. The active nav tab is squared on its sheet side so it runs into the paper.

## Components

### Buttons
Plain, ink-framed, typed-form buttons.
- **Shape:** 3px corners, 1px frame border, 32px tall (26px small).
- **Default:** raised-sheet fill, ink text, Archivo 600 at 0.8125rem, 6px gap to a 16px Lucide icon.
- **Primary:** ink fill, paper text; hover lifts to ink-2. Used sparingly; there is no page-level primary CTA.
- **Hover / Focus / Active:** fill shifts to form tint over 150 ms; 2px focus-blue outline offset 2px; active nudges down 1px. Disabled at 50% opacity.
- **Ghost:** transparent until hover (row-hover). **On kraft:** transparent with a kraft-ink-2 border, for the rail.

### Chips
- **Tags:** 18px, 2px corners, condensed uppercase label type, rule-strong border. Warn uses hazard border on hazard wash with hazard-ink text; bad uses stamp on stamp wash; good uses ok border and text. A status tag always carries its icon and word.

### Cards / Containers
- **Panels:** 3px corners, paper fill, 1px frame box, a 40px head ruled in frame with a 0.875rem bold title and muted meta (legend keys, scope, links) pushed right. No shadow.
- **Field grid:** a panel section split into labelled boxes sharing 1px rule lines, label above a display or amount figure.

### Inputs / Fields
- **Style:** 32px, raised-sheet fill, 1px rule-strong border, 3px corners; hover darkens the border to frame. Selects draw their caret from two gradients.
- **Focus:** 2px focus-blue outline with a matching border.
- **Error:** stamp-red border; error text in stamp red with an icon.

### Navigation
The rail is a kraft folder. Condensed uppercase wordmark (800, width 80%) with a small-caps subtitle. 36px links in Archivo 560 with 16px Lucide icons; hover is kraft-2; the active link becomes a paper tab running into the sheet. The Errors link carries a stamp-red count pill. Health checks and the log-reader state sit in the rail foot above Sign out. On mobile it becomes a horizontal icon strip with 44px tabs.

### Manifest Table (signature)
Ruled consignment lines. A sticky form-tint header in label type over a frame rule; 34px rows ruled in rule; numbers right-aligned in mono; row hover in row-hover. The client cell sets name or IP on the first line and a muted mono docket number (DL-00620) on a second line. Gateway-hidden clients say so in words. Game cells pair a 46×22 header-art thumb with the name and a service swatch line. Group breaks ("Finished recently") use a label-type sub-head row; finished rows step down to ink-2.

### Split Bar (signature)
The persistent second voice. An 8px bar, hit then miss with a 2px gap and 2px outer corners, and a muted mono line beneath ("68% cached · 934 MB in"). The empty state is a plain rule bar. It appears on every consignment row and under the ledger.

### Docket and Stamp (signature)
Each open exception is a docket: raised sheet, frame box, title, mono host line, advice in plain words, a mono time range and a small "Sign off" button. The stamp sits top right: stamp-red 2px outline, condensed 800 uppercase, rotated −4°. A newly arrived stamp lands once (160 ms from −9° at 1.35 scale, no bounce; no motion under reduced-motion). Signed dockets fade to paper and rule-strong, and the stamp becomes a grey −2° impression with the signer's time. When nothing is open, the band is one dashed all-clear line: check icon, "No exceptions." and when it was checked.

### Event Ledger (signature)
Ruled ledger lines, not stat tiles. Each line has a key swatch, a label and a right-aligned mono amount with its unit in a fixed-width column. The total ("Delivered to clients") sits under a 3px double frame rule. Beneath, a split bar and a row of small label/value counts (hit ratio, clients, downloads, failed).

### Live Charts
Stacked hit-over-miss bars on rule gridlines with a frame baseline and 10.5px mono axis labels. Live charts scale robustly to the 95th percentile ×1.25; bars above the ceiling are clipped and capped with a 3px ink mark while the tooltip keeps the true value. Event timelines add stamp-red error marks. Hover shows a 6% ink band, a dashed ink cursor and a raised-sheet tooltip.

### Service Mix
One 14px segmented 100% bar in service hues, then a four-column list: swatch, name, muted share, mono bytes.

## Do's and Don'ts

### Do:
- **Do** set every machine value (IP, rate, size, time, docket number) in JetBrains Mono with tabular, slashed-zero figures.
- **Do** box panels with a 1px frame rule at 3px corners and separate rows with 1px rule lines.
- **Do** show hit and miss together and always name them "From cache" and "From internet".
- **Do** put a split bar on every consignment row.
- **Do** pair every status colour with an icon and a word.
- **Do** fold services beyond Steam, Epic Games, Blizzard and WSUS into svc-other and label them by name.
- **Do** scale live charts to p95×1.25 and cap clipped bars with an ink mark.
- **Do** say "No exceptions" plainly in one quiet line when the band is empty.
- **Do** design the carbon copy as its own blue-black sheet, using the carbon tokens.

### Don't:
- **Don't** build a grid of equal stat tiles; totals are ledger lines or field boxes.
- **Don't** use gradients or glows on data or status colours.
- **Don't** add shadows to anything resting on the page; only tooltips float.
- **Don't** use stamp red, hazard yellow or ok green for a service, a series or decoration.
- **Don't** set small-caps labels as kickers or eyebrows above headings.
- **Don't** put freight vocabulary ("freight", "pallet", "shipment") in column headers; the world supplies type, palette, density and the stamp, and labels stay plain.
- **Don't** add motion beyond the stamp landing, the live pulse, the loading shimmer and 150 ms state transitions.
