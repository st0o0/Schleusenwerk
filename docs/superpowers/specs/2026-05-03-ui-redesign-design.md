# Schleusenwerk UI Redesign — Blueprint Theme

## Summary

Complete visual redesign of the Schleusenwerk Blazor Server management UI. Replaces the default MudBlazor look with a custom "Blueprint" theme inspired by technical engineering drawings and canal lock (Schleuse) metaphors. Uses consistent Schleusen-Terminologie throughout. Target audience: DevOps/Sysadmins (primary), Developers (secondary).

## Technical Approach

**MudBlazor Custom Theme** — keep MudBlazor for components (Table, Dialog, Snackbar, Forms), customize via `MudTheme` palette + CSS overrides for Blueprint-specific effects (grid background, monospace headings, colored borders).

## Color Palette

| Token          | Hex       | Usage                                      |
|----------------|-----------|----------------------------------------------|
| Background     | `#0a192f` | Page background                              |
| Surface        | `#112240` | Cards, panels, table backgrounds             |
| Border/Grid    | `#1e3a5f` | Borders, grid lines, dividers                |
| Primary (Mint) | `#64ffda` | Active elements, healthy status, links, CTA  |
| Text Primary   | `#ccd6f6` | Headings, body text, data values             |
| Text Secondary | `#8892b0` | Labels, timestamps, descriptions             |
| Error          | `#ff6b6b` | Unhealthy/blocked status, error indicators   |
| Warning        | `#f0c000` | Expiring certs, conflicts, caution states    |
| Info Blue      | `#58a6ff` | POST method, secondary accents               |

## Typography

- **Headlines & Branding**: Monospace (`JetBrains Mono`, fallback `Cascadia Code`, `Fira Code`, `monospace`), uppercase, letter-spacing 1-2px
- **Body text**: System sans-serif (`Inter`, `Segoe UI`, `system-ui`) for readability
- **KPI numbers**: Monospace, large (26-32px), Primary color
- **Labels**: 9px uppercase, letter-spacing 1.5px, Text Secondary color

## Schleusen-Terminologie

All UI labels use canal lock terminology consistently:

| Technical Term   | Schleusen-Term    | English Hint   |
|------------------|-------------------|----------------|
| Route            | Tor               | Gate           |
| Routes (list)    | Schleusentore     | Lock gates     |
| Upstream         | Kammer            | Chamber        |
| Healthy          | Offen             | Open           |
| Unhealthy        | Gesperrt          | Blocked        |
| Throughput       | Pegel             | Water level    |
| Certificate      | Siegel            | Seal           |
| Event Log        | Flussprotokoll    | Flow log       |
| Docker Discovery | Hafenbecken       | Harbor basin   |
| Dashboard        | Leitstand         | Control room   |
| Settings         | Stellwerk         | Signal box     |
| Force HTTPS      | Versiegelt        | Sealed         |
| Add Route        | Tor einsetzen     | Install gate   |
| Delete Route     | Tor ausbauen      | Remove gate    |

## Layout

### Blueprint Grid Background

Subtle CSS grid pattern on the page background using `background-image` with two linear gradients (horizontal + vertical lines) at `rgba(100,200,255,0.03)` opacity, 24px spacing.

### Navigation — Top Schaltleiste

Replaces the MudBlazor sidebar drawer with a horizontal top bar:

- **Left**: Logo icon (schematic lock gate SVG) + "SCHLEUSENWERK" in monospace uppercase
- **Center**: Navigation tabs with bottom-border indicator (2px solid Primary when active, transparent when inactive). Tab labels: LEITSTAND, SCHLEUSENTORE, SIEGEL, FLUSSPROTOKOLL, HAFENBECKEN, STELLWERK
- **Right**: System status indicator (green dot + "SYSTEM OK" or red dot + error text)
- **Height**: 52px
- **Background**: Surface color with bottom border

### Content Area

- Max-width 1200px, centered
- 24px padding
- Page header: title in monospace 18px bold + separator dash + subtitle in Text Secondary

## Pages

### 1. Leitstand (Dashboard) — `/`

Compact status overview, no live-updating charts.

**Components:**
- **4 KPI cards** in a grid row: Tore gesamt, Tore offen (Primary), Tore gesperrt (Error), Siegel ablaufend (Warning). Surface background, left-border or standard border, monospace large numbers.
- **Störungen panel** (half-width): List of current problems — unhealthy routes and expiring certificates. Each entry: colored dot + domain + description. Error entries get red-tinted background, warning entries get yellow-tinted background.
- **Letzter Durchfluss panel** (half-width): Last 4-5 proxy events from SignalR stream. Timestamp, flow arrow (→ for success, ⊧ for error), domain, status code, duration. Serves as a quick health indicator, not a full log.

### 2. Schleusentore (Route List) — `/tore`

**Header:** Title + count + "TOR EINSETZEN" button (outlined, Primary border).

**Table columns:**
- Status indicator (colored square: Primary=offen, Error=gesperrt)
- Domain (monospace, clickable link to detail)
- Quelle (badge: "docker" in Primary tint, "manuell" in neutral)
- Status (text: "offen"/"gesperrt")
- Kammern (upstream count)
- Siegel (diamond icon, colored by cert status)
- Actions (menu icon)

Rows with gesperrt status get subtle red-tinted background.

### 3. Tor einsetzen (Create Route) — `/tore/neu`

Form card (max-width 600px) with Blueprint styling:
- **Domain** text field (monospace, placeholder "example.com")
- **Erste Kammer** text field (placeholder "http://backend:8080")
- **Versiegelung** toggle (HTTPS)
- **Timeout** slider with value display
- **Actions**: "TOR EINSETZEN" button (filled Primary) + "Abbrechen" link back to `/tore`
- Submit button shows loading spinner while creating

### 4. Tor-Detail — `/tore/{domain}`

**Breadcrumb:** SCHLEUSENTORE / {domain}

**Header:** Status dot + domain name + status badges ("offen", "versiegelt")

**Two-column layout:**
- **Left (280px) — Konfiguration**: Versiegelung toggle (HTTPS), Timeout slider with value display, Save button
- **Right (flex) — Kammern**: List of upstream entries, each showing health dot + URL + weight + remove button. Add-chamber input at bottom with text field + button.

### 5. Siegel (Certificates) — `/siegel`

**Table columns:**
- Status diamond icon (Primary=valid, Warning=expiring soon, Secondary=self-signed)
- Domain
- Fingerabdruck (truncated, monospace small)
- Gültig bis (date, Warning-colored if < 14 days)
- Typ (badge: "ACME" Primary tint, "Selbst" Warning tint)
- Erneuern button (Warning-colored border if expiring soon)

### 6. Flussprotokoll (Event Log) — `/flussprotokoll`

**Header:** Title + LIVE indicator (pulsing green dot + "LIVE" text)

**Filter bar:** Three dropdown filters (Tor, Status, Methode) + Pause button on the right.

**Log table columns:**
- Zeit (timestamp, Text Secondary dimmed)
- Methode (color-coded: GET=Primary, POST=Info Blue, PUT=Warning, DELETE=Error)
- Tor/Pfad (domain in Text Primary + path in Text Secondary)
- Status (color-coded: 2xx=Primary, 4xx=Warning, 5xx=Error)
- Kammer (short upstream identifier)
- Dauer (right-aligned, Error-colored if > 1000ms)

Rows with 5xx status get subtle red-tinted background. Auto-scrolls with new events via SignalR. Infinite scroll for older entries.

### 7. Hafenbecken (Docker Discovery) — `/hafenbecken`

**Header:** Title + Docker socket connection status indicator.

**Container cards in 2-column grid:**
- **Normal card**: Green status dot + container name + "running" status. Image name, Schleusenwerk labels listed. Link to assigned Tor at bottom.
- **Conflict card**: Yellow border, yellow status dot + "Konflikt". Warning banner explaining the conflict.
- **Unlabeled card**: Dimmed/reduced opacity. Grey dot + "kein Label". Note: "Keine Schleusenwerk-Labels erkannt".

### 8. Stellwerk (Settings) — `/stellwerk`

Standard settings form (not yet mockup'd in detail):
- ACME configuration (email, provider, staging/production toggle)
- Default timeout
- Docker socket path
- System info display (version, uptime, actor system status)

## Shared Components

### BlueprintLayout (replaces MainLayout)
- Blueprint grid background
- Top navigation bar (Schaltleiste)
- Content area with max-width container

### StatusIndicator (replaces HealthIndicator)
- Square dot (not circle) with 2px border-radius
- Colors: Primary (offen), Error (gesperrt), Warning (conflict/expiring)

### QuelleBadge (replaces SourceBadge)
- "docker" in Primary-tinted background
- "manuell" in neutral/secondary-tinted background

### SiegelIcon
- Diamond shape (◆)
- Colored by certificate status: Primary (valid), Warning (expiring/self-signed)

## CSS Implementation

### MudTheme Configuration

Custom `MudTheme` with `PaletteDark`:
- `Black` = Background (#0a192f)
- `Background` = Background (#0a192f)
- `Surface` = Surface (#112240)
- `Primary` = Mint (#64ffda)
- `Error` = Error (#ff6b6b)
- `Warning` = Warning (#f0c000)
- `Info` = Info Blue (#58a6ff)
- `TextPrimary` = Text Primary (#ccd6f6)
- `TextSecondary` = Text Secondary (#8892b0)
- `DrawerBackground` = Surface (#112240)
- `AppbarBackground` = Surface (#112240)

### CSS Overrides

Global CSS file for:
- Blueprint grid background on `body`/`.mud-layout`
- Monospace font overrides for `.mud-typography-h5`, `.mud-typography-h6`
- Table row tinting for error states
- Custom top navigation styling (replacing drawer)
- Letter-spacing and uppercase transforms on labels
- Scrollbar styling matching the dark theme

## Data Flow

### Existing (no changes needed)
- **gRPC clients** (RouteClient, CertificateClient, HealthClient) → proxy backend
- **SignalR EventHub** → receives events from EventStreamBackgroundService

### New connections
- **Flussprotokoll page**: Connects to ProxyEventHub via SignalR for live events. Stores recent events in a client-side circular buffer.
- **Hafenbecken page**: New gRPC client for Docker discovery data (requires new gRPC service on the proxy, or exposes existing DockerDiscoveryActor state).
- **Leitstand**: Aggregates data from HealthClient (KPIs) + last N events from EventHub (recent flow) + certificate expiry warnings from CertificateClient.

## New gRPC Services Required

### DockerDiscoveryService
The proxy needs to expose Docker container discovery state via a new gRPC service:
- `ListContainers` — returns discovered containers with labels, images, assigned routes, conflicts
- This wraps the existing `DockerDiscoveryActor` state

### EventService Enhancement
The existing `EventService.Subscribe` stream works for Flussprotokoll. No changes needed for basic functionality. Filter support (by domain, status code, method) can be implemented client-side initially.

## Out of Scope

- Authentication/authorization for the management UI
- Dark/light theme toggle (Blueprint is dark-only by design)
- Mobile responsive layout (desktop DevOps tool)
- Internationalization (German Schleusen-terms are the identity, not i18n)
- Real-time metrics/charts (KPIs are point-in-time, Flussprotokoll is the live view)
