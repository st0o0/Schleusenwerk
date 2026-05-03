# Schleusenwerk UI Redesign — Blueprint Theme Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the default MudBlazor UI with a custom "Blueprint" theme using Schleusen-Terminologie, top navigation, and 8 pages (Leitstand, Schleusentore, Tor-Detail, Tor einsetzen, Siegel, Flussprotokoll, Hafenbecken, Stellwerk).

**Architecture:** MudBlazor Custom Theme with `PaletteDark` overrides + global CSS for Blueprint grid background, monospace typography, and component styling. Top navigation replaces sidebar drawer. New pages (Flussprotokoll, Hafenbecken, Stellwerk) connect to existing gRPC/SignalR services. Hafenbecken requires a new `discovery.proto` + gRPC client.

**Tech Stack:** Blazor Server (.NET 10), MudBlazor, gRPC, SignalR, CSS custom properties

**Spec:** `docs/superpowers/specs/2026-05-03-ui-redesign-design.md`

---

## File Map

### Modified files
- `src/Schleusenwerk.UI/Components/App.razor` — replace Google Fonts link with JetBrains Mono, add blueprint CSS
- `src/Schleusenwerk.UI/Components/Layout/MainLayout.razor` — complete rewrite to BlueprintLayout with top nav
- `src/Schleusenwerk.UI/Components/Shared/NavMenu.razor` — delete (navigation moves into MainLayout)
- `src/Schleusenwerk.UI/Components/Shared/HealthIndicator.razor` — rewrite as StatusIndicator (square dots)
- `src/Schleusenwerk.UI/Components/Shared/SourceBadge.razor` — rewrite as QuelleBadge
- `src/Schleusenwerk.UI/Components/Pages/Dashboard.razor` — rewrite as Leitstand
- `src/Schleusenwerk.UI/Components/Pages/RouteList.razor` — rewrite as Schleusentore
- `src/Schleusenwerk.UI/Components/Pages/RouteDetail.razor` — rewrite with Blueprint styling
- `src/Schleusenwerk.UI/Components/Pages/RouteCreate.razor` — rewrite as TorEinsetzen
- `src/Schleusenwerk.UI/Components/Pages/CertificateList.razor` — rewrite as Siegel
- `src/Schleusenwerk.UI/Components/_Imports.razor` — add layout using
- `src/Schleusenwerk.UI/Program.cs` — no changes needed (services already registered)

### New files
- `src/Schleusenwerk.UI/wwwroot/css/blueprint.css` — Blueprint theme CSS (grid, typography, overrides)
- `src/Schleusenwerk.UI/Components/Shared/SiegelIcon.razor` — certificate status diamond icon
- `src/Schleusenwerk.UI/Components/Pages/Flussprotokoll.razor` — live event log page
- `src/Schleusenwerk.UI/Components/Pages/Hafenbecken.razor` — Docker discovery page
- `src/Schleusenwerk.UI/Components/Pages/Stellwerk.razor` — settings page
- `src/Schleusenwerk.Contracts/Protos/discovery.proto` — Docker discovery gRPC service definition
- `src/Schleusenwerk.UI/Services/IDiscoveryClient.cs` — discovery client interface
- `src/Schleusenwerk.UI/Services/DiscoveryClient.cs` — discovery client implementation

---

## Task 1: Blueprint CSS + Theme Foundation

**Files:**
- Create: `src/Schleusenwerk.UI/wwwroot/css/blueprint.css`
- Modify: `src/Schleusenwerk.UI/Components/App.razor`

- [ ] **Step 1: Create blueprint.css with CSS custom properties and grid background**

```css
/* src/Schleusenwerk.UI/wwwroot/css/blueprint.css */

:root {
    --bp-background: #0a192f;
    --bp-surface: #112240;
    --bp-border: #1e3a5f;
    --bp-primary: #64ffda;
    --bp-text-primary: #ccd6f6;
    --bp-text-secondary: #8892b0;
    --bp-error: #ff6b6b;
    --bp-warning: #f0c000;
    --bp-info: #58a6ff;
    --bp-font-mono: 'JetBrains Mono', 'Cascadia Code', 'Fira Code', monospace;
    --bp-font-sans: 'Inter', 'Segoe UI', system-ui, sans-serif;
}

html, body {
    background: var(--bp-background);
    color: var(--bp-text-primary);
    font-family: var(--bp-font-sans);
    margin: 0;
}

.mud-layout {
    background: var(--bp-background);
}

.blueprint-grid {
    position: fixed;
    inset: 0;
    background-image:
        linear-gradient(rgba(100,200,255,0.03) 1px, transparent 1px),
        linear-gradient(90deg, rgba(100,200,255,0.03) 1px, transparent 1px);
    background-size: 24px 24px;
    pointer-events: none;
    z-index: 0;
}

/* Top navigation - Schaltleiste */
.schaltleiste {
    background: var(--bp-surface);
    border-bottom: 1px solid var(--bp-border);
    height: 52px;
    display: flex;
    align-items: center;
    padding: 0 24px;
    position: relative;
    z-index: 10;
}

.schaltleiste-logo {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-right: 32px;
}

.schaltleiste-logo span {
    color: var(--bp-text-primary);
    font-family: var(--bp-font-mono);
    font-size: 13px;
    font-weight: 700;
    letter-spacing: 2px;
}

.schaltleiste-tabs {
    display: flex;
    height: 100%;
    gap: 0;
}

.schaltleiste-tab {
    display: flex;
    align-items: center;
    padding: 0 16px;
    border-bottom: 2px solid transparent;
    color: var(--bp-text-secondary);
    font-family: var(--bp-font-mono);
    font-size: 12px;
    letter-spacing: 0.5px;
    text-decoration: none;
    transition: color 0.15s, border-color 0.15s;
    cursor: pointer;
    white-space: nowrap;
}

.schaltleiste-tab:hover {
    color: var(--bp-text-primary);
}

.schaltleiste-tab.active {
    color: var(--bp-primary);
    border-bottom-color: var(--bp-primary);
}

.schaltleiste-status {
    margin-left: auto;
    display: flex;
    align-items: center;
    gap: 8px;
}

.schaltleiste-status .dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--bp-primary);
    box-shadow: 0 0 6px rgba(100,255,218,0.4);
}

.schaltleiste-status .dot.error {
    background: var(--bp-error);
    box-shadow: 0 0 6px rgba(255,107,107,0.4);
}

.schaltleiste-status span {
    color: var(--bp-text-secondary);
    font-family: var(--bp-font-mono);
    font-size: 11px;
}

/* Content area */
.blueprint-content {
    position: relative;
    z-index: 1;
    max-width: 1200px;
    margin: 0 auto;
    padding: 24px;
}

/* Page headers */
.page-header {
    display: flex;
    align-items: baseline;
    gap: 12px;
    margin-bottom: 20px;
}

.page-header h1 {
    color: var(--bp-text-primary);
    font-family: var(--bp-font-mono);
    font-size: 18px;
    font-weight: 700;
    letter-spacing: 1px;
    margin: 0;
    text-transform: uppercase;
}

.page-header .separator {
    color: var(--bp-border);
    font-family: var(--bp-font-mono);
    font-size: 12px;
}

.page-header .subtitle {
    color: var(--bp-text-secondary);
    font-family: var(--bp-font-mono);
    font-size: 12px;
}

/* Blueprint panels */
.bp-panel {
    background: var(--bp-surface);
    border: 1px solid var(--bp-border);
    border-radius: 6px;
    padding: 16px;
}

.bp-panel-label {
    color: var(--bp-text-secondary);
    font-size: 9px;
    text-transform: uppercase;
    letter-spacing: 1.5px;
    margin-bottom: 12px;
}

/* KPI cards */
.bp-kpi {
    background: var(--bp-surface);
    border: 1px solid var(--bp-border);
    border-radius: 6px;
    padding: 16px;
}

.bp-kpi-label {
    color: var(--bp-text-secondary);
    font-size: 9px;
    text-transform: uppercase;
    letter-spacing: 1.5px;
    margin-bottom: 6px;
}

.bp-kpi-value {
    font-family: var(--bp-font-mono);
    font-size: 28px;
    font-weight: 700;
}

/* Status badges */
.bp-badge {
    font-family: var(--bp-font-mono);
    font-size: 11px;
    padding: 2px 8px;
    border-radius: 3px;
    display: inline-block;
}

.bp-badge-primary {
    color: var(--bp-primary);
    background: rgba(100,255,218,0.08);
}

.bp-badge-neutral {
    color: var(--bp-text-secondary);
    background: rgba(136,146,176,0.08);
}

.bp-badge-warning {
    color: var(--bp-warning);
    background: rgba(240,192,0,0.08);
}

.bp-badge-error {
    color: var(--bp-error);
    background: rgba(255,107,107,0.08);
}

/* Status indicator (square dot) */
.bp-status-dot {
    width: 8px;
    height: 8px;
    border-radius: 2px;
    display: inline-block;
    flex-shrink: 0;
}

.bp-status-dot.offen { background: var(--bp-primary); }
.bp-status-dot.gesperrt { background: var(--bp-error); }
.bp-status-dot.warnung { background: var(--bp-warning); }
.bp-status-dot.neutral { background: var(--bp-text-secondary); }

/* Alert rows */
.bp-alert-row {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 10px;
    border-radius: 4px;
}

.bp-alert-row.error {
    background: rgba(255,107,107,0.06);
    border: 1px solid rgba(255,107,107,0.15);
}

.bp-alert-row.warning {
    background: rgba(240,192,0,0.06);
    border: 1px solid rgba(240,192,0,0.15);
}

/* Outlined button */
.bp-btn-outline {
    background: transparent;
    border: 1px solid var(--bp-primary);
    color: var(--bp-primary);
    padding: 6px 14px;
    border-radius: 4px;
    font-family: var(--bp-font-mono);
    font-size: 12px;
    cursor: pointer;
    letter-spacing: 0.5px;
    transition: background 0.15s;
}

.bp-btn-outline:hover {
    background: rgba(100,255,218,0.08);
}

/* Live indicator */
.bp-live-dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--bp-primary);
    box-shadow: 0 0 6px rgba(100,255,218,0.5);
    animation: bp-pulse 2s infinite;
}

@keyframes bp-pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.4; }
}

/* Table row error tint */
.bp-row-error {
    background: rgba(255,107,107,0.03);
}

/* MudBlazor overrides */
.mud-table .mud-table-head .mud-table-cell {
    color: var(--bp-text-secondary);
    font-size: 9px;
    text-transform: uppercase;
    letter-spacing: 1.5px;
    background: rgba(30,58,95,0.3);
    border-bottom: 1px solid var(--bp-border);
}

.mud-table .mud-table-body .mud-table-cell {
    color: var(--bp-text-primary);
    font-family: var(--bp-font-mono);
    font-size: 13px;
    border-bottom: 1px solid rgba(30,58,95,0.5);
}

.mud-table .mud-table-root {
    background: var(--bp-surface);
    border: 1px solid var(--bp-border);
    border-radius: 6px;
}

.mud-input,
.mud-input-control .mud-input-root {
    color: var(--bp-text-primary) !important;
    font-family: var(--bp-font-mono) !important;
}

.mud-input-label {
    color: var(--bp-text-secondary) !important;
}

/* Breadcrumb */
.bp-breadcrumb {
    color: var(--bp-text-secondary);
    font-family: var(--bp-font-mono);
    font-size: 11px;
    margin-bottom: 16px;
}

.bp-breadcrumb a {
    color: var(--bp-text-secondary);
    text-decoration: underline;
    text-decoration-color: var(--bp-border);
    text-underline-offset: 3px;
}

.bp-breadcrumb a:hover {
    color: var(--bp-primary);
}

.bp-breadcrumb .current {
    color: var(--bp-primary);
}

/* Scrollbar */
::-webkit-scrollbar {
    width: 8px;
    height: 8px;
}

::-webkit-scrollbar-track {
    background: var(--bp-background);
}

::-webkit-scrollbar-thumb {
    background: var(--bp-border);
    border-radius: 4px;
}

::-webkit-scrollbar-thumb:hover {
    background: var(--bp-text-secondary);
}
```

- [ ] **Step 2: Update App.razor to load Blueprint CSS and fonts**

Replace the full content of `src/Schleusenwerk.UI/Components/App.razor`:

```html
<!DOCTYPE html>
<html lang="de">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;700&family=Inter:wght@400;500;600&display=swap" rel="stylesheet" />
    <link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
    <link href="css/blueprint.css" rel="stylesheet" />
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
</body>
</html>
```

- [ ] **Step 3: Verify the CSS loads**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds, no errors.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.UI/wwwroot/css/blueprint.css src/Schleusenwerk.UI/Components/App.razor
git commit -m "feat(ui): add Blueprint CSS foundation with custom properties, grid, and MudBlazor overrides"
```

---

## Task 2: MudTheme + BlueprintLayout (MainLayout rewrite)

**Files:**
- Modify: `src/Schleusenwerk.UI/Components/Layout/MainLayout.razor`
- Delete: `src/Schleusenwerk.UI/Components/Shared/NavMenu.razor`

- [ ] **Step 1: Rewrite MainLayout.razor as BlueprintLayout with top nav and MudTheme**

Replace the full content of `src/Schleusenwerk.UI/Components/Layout/MainLayout.razor`:

```razor
@inherits LayoutComponentBase
@inject NavigationManager Nav
@inject IHealthClient HealthClient

<MudThemeProvider Theme="_theme" IsDarkMode="true" />
<MudSnackbarProvider />
<MudDialogProvider />

<div class="blueprint-grid"></div>

<div style="position: relative; z-index: 1; min-height: 100vh;">
    <nav class="schaltleiste">
        <div class="schaltleiste-logo">
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none">
                <rect x="2" y="6" width="20" height="12" rx="2" stroke="#64ffda" stroke-width="1.5" fill="none" />
                <line x1="12" y1="6" x2="12" y2="18" stroke="#64ffda" stroke-width="1.5" stroke-dasharray="3 2" />
            </svg>
            <span>SCHLEUSENWERK</span>
        </div>

        <div class="schaltleiste-tabs">
            <NavLink href="" class="schaltleiste-tab" Match="NavLinkMatch.All">
                LEITSTAND
            </NavLink>
            <NavLink href="tore" class="schaltleiste-tab" Match="NavLinkMatch.Prefix">
                SCHLEUSENTORE
            </NavLink>
            <NavLink href="siegel" class="schaltleiste-tab" Match="NavLinkMatch.All">
                SIEGEL
            </NavLink>
            <NavLink href="flussprotokoll" class="schaltleiste-tab" Match="NavLinkMatch.All">
                FLUSSPROTOKOLL
            </NavLink>
            <NavLink href="hafenbecken" class="schaltleiste-tab" Match="NavLinkMatch.All">
                HAFENBECKEN
            </NavLink>
            <NavLink href="stellwerk" class="schaltleiste-tab" Match="NavLinkMatch.All">
                STELLWERK
            </NavLink>
        </div>

        <div class="schaltleiste-status">
            <div class="dot @(_systemOk ? "" : "error")"></div>
            <span>@(_systemOk ? "SYSTEM OK" : "STÖRUNG")</span>
        </div>
    </nav>

    <main class="blueprint-content">
        @Body
    </main>
</div>

@code {
    private bool _systemOk = true;

    private readonly MudTheme _theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Black = "#0a192f",
            Background = "#0a192f",
            Surface = "#112240",
            Primary = "#64ffda",
            Error = "#ff6b6b",
            Warning = "#f0c000",
            Info = "#58a6ff",
            Success = "#64ffda",
            TextPrimary = "#ccd6f6",
            TextSecondary = "#8892b0",
            DrawerBackground = "#112240",
            AppbarBackground = "#112240",
            ActionDefault = "#8892b0",
            ActionDisabled = "#1e3a5f",
            LinesDefault = "#1e3a5f",
            TableLines = "#1e3a5f",
            Divider = "#1e3a5f"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Inter", "Segoe UI", "system-ui", "sans-serif"]
            }
        }
    };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var health = await HealthClient.GetHealthAsync();
            _systemOk = health.UnhealthyCount == 0;
        }
        catch
        {
            _systemOk = false;
        }
    }
}
```

- [ ] **Step 2: Delete NavMenu.razor**

Delete `src/Schleusenwerk.UI/Components/Shared/NavMenu.razor` — navigation is now in MainLayout.

- [ ] **Step 3: Add active CSS class for NavLink**

Blazor's `NavLink` adds class `active` automatically. The CSS in `blueprint.css` already targets `.schaltleiste-tab.active`. Verify by adding this to `blueprint.css` if not already present (it is — check Task 1 Step 1):

```css
.schaltleiste-tab.active {
    color: var(--bp-primary);
    border-bottom-color: var(--bp-primary);
}
```

No change needed — already in blueprint.css.

- [ ] **Step 4: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk.UI/Components/Layout/MainLayout.razor
git rm src/Schleusenwerk.UI/Components/Shared/NavMenu.razor
git commit -m "feat(ui): replace sidebar with Blueprint top navigation (Schaltleiste) and MudTheme"
```

---

## Task 3: Shared Components (StatusIndicator, QuelleBadge, SiegelIcon)

**Files:**
- Modify: `src/Schleusenwerk.UI/Components/Shared/HealthIndicator.razor` — rewrite as StatusIndicator
- Modify: `src/Schleusenwerk.UI/Components/Shared/SourceBadge.razor` — rewrite as QuelleBadge
- Create: `src/Schleusenwerk.UI/Components/Shared/SiegelIcon.razor`

- [ ] **Step 1: Rewrite HealthIndicator.razor as StatusIndicator**

Replace the full content of `src/Schleusenwerk.UI/Components/Shared/HealthIndicator.razor`:

```razor
<span class="bp-status-dot @CssClass" title="@Title"></span>

@code {
    [Parameter, EditorRequired] public string Status { get; set; } = "offen";

    private string CssClass => Status switch
    {
        "offen" => "offen",
        "gesperrt" => "gesperrt",
        "warnung" => "warnung",
        _ => "neutral"
    };

    private string Title => Status switch
    {
        "offen" => "Offen",
        "gesperrt" => "Gesperrt",
        "warnung" => "Warnung",
        _ => Status
    };
}
```

- [ ] **Step 2: Rewrite SourceBadge.razor as QuelleBadge**

Replace the full content of `src/Schleusenwerk.UI/Components/Shared/SourceBadge.razor`:

```razor
<span class="bp-badge @CssClass">@Label</span>

@code {
    [Parameter, EditorRequired] public string Source { get; set; } = "manuell";

    private string CssClass => Source == "docker" ? "bp-badge-primary" : "bp-badge-neutral";
    private string Label => Source == "docker" ? "docker" : "manuell";
}
```

- [ ] **Step 3: Create SiegelIcon.razor**

Create `src/Schleusenwerk.UI/Components/Shared/SiegelIcon.razor`:

```razor
<span style="color: @Color; font-size: 13px;" title="@Title">&#9670;</span>

@code {
    [Parameter, EditorRequired] public string Status { get; set; } = "valid";

    private string Color => Status switch
    {
        "valid" => "var(--bp-primary)",
        "expiring" => "var(--bp-warning)",
        "self-signed" => "var(--bp-text-secondary)",
        _ => "var(--bp-text-secondary)"
    };

    private string Title => Status switch
    {
        "valid" => "Gültiges Siegel",
        "expiring" => "Siegel läuft bald ab",
        "self-signed" => "Selbstsigniert",
        _ => "Unbekannt"
    };
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds (pages referencing old components will break — that's expected, we fix them in subsequent tasks).

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk.UI/Components/Shared/HealthIndicator.razor src/Schleusenwerk.UI/Components/Shared/SourceBadge.razor src/Schleusenwerk.UI/Components/Shared/SiegelIcon.razor
git commit -m "feat(ui): rewrite shared components for Blueprint theme (StatusIndicator, QuelleBadge, SiegelIcon)"
```

---

## Task 4: Leitstand (Dashboard)

**Files:**
- Modify: `src/Schleusenwerk.UI/Components/Pages/Dashboard.razor`

- [ ] **Step 1: Rewrite Dashboard.razor as Leitstand**

Replace the full content of `src/Schleusenwerk.UI/Components/Pages/Dashboard.razor`:

```razor
@page "/"
@inject IHealthClient HealthClient
@inject ICertificateClient CertClient
@inject IRouteClient RouteClient
@rendermode @(new InteractiveServerRenderMode())

<PageTitle>Leitstand — Schleusenwerk</PageTitle>

<div class="page-header">
    <h1>LEITSTAND</h1>
    <span class="separator">——</span>
    <span class="subtitle">Systemübersicht</span>
</div>

@if (_loading)
{
    <MudProgressCircular Indeterminate="true" Color="Color.Primary" />
}
else if (_error is not null)
{
    <MudAlert Severity="Severity.Error">@_error</MudAlert>
}
else
{
    <MudGrid Spacing="3" Class="mb-4">
        <MudItem xs="12" sm="3">
            <div class="bp-kpi">
                <div class="bp-kpi-label">Tore gesamt</div>
                <div class="bp-kpi-value" style="color: var(--bp-text-primary);">@_health!.RouteCount</div>
            </div>
        </MudItem>
        <MudItem xs="12" sm="3">
            <div class="bp-kpi">
                <div class="bp-kpi-label">Tore offen</div>
                <div class="bp-kpi-value" style="color: var(--bp-primary);">@_health!.HealthyCount</div>
            </div>
        </MudItem>
        <MudItem xs="12" sm="3">
            <div class="bp-kpi">
                <div class="bp-kpi-label">Tore gesperrt</div>
                <div class="bp-kpi-value" style="color: var(--bp-error);">@_health!.UnhealthyCount</div>
            </div>
        </MudItem>
        <MudItem xs="12" sm="3">
            <div class="bp-kpi">
                <div class="bp-kpi-label">Siegel ablaufend</div>
                <div class="bp-kpi-value" style="color: var(--bp-warning);">@_expiringCerts</div>
            </div>
        </MudItem>
    </MudGrid>

    <MudGrid Spacing="3">
        <MudItem xs="12" md="6">
            <div class="bp-panel">
                <div class="bp-panel-label">Störungen</div>
                @if (_alerts.Count == 0)
                {
                    <MudText Style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">Keine Störungen</MudText>
                }
                else
                {
                    <div style="display: flex; flex-direction: column; gap: 8px;">
                        @foreach (var alert in _alerts)
                        {
                            <div class="bp-alert-row @alert.Severity">
                                <span class="bp-status-dot @(alert.Severity == "error" ? "gesperrt" : "warnung")"></span>
                                <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 12px; flex: 1;">@alert.Domain</span>
                                <span style="color: var(--bp-@alert.Severity); font-family: var(--bp-font-mono); font-size: 11px;">@alert.Message</span>
                            </div>
                        }
                    </div>
                }
            </div>
        </MudItem>
        <MudItem xs="12" md="6">
            <div class="bp-panel">
                <div class="bp-panel-label">Letzter Durchfluss</div>
                <MudText Style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">
                    Events werden im Flussprotokoll angezeigt
                </MudText>
            </div>
        </MudItem>
    </MudGrid>
}

@code {
    private ProxyHealthResponse? _health;
    private bool _loading = true;
    private string? _error;
    private int _expiringCerts;
    private List<AlertEntry> _alerts = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var healthTask = HealthClient.GetHealthAsync();
            var certsTask = CertClient.ListCertificatesAsync();
            var routesTask = RouteClient.ListRoutesAsync();

            await Task.WhenAll(healthTask, certsTask, routesTask);

            _health = healthTask.Result;
            var certs = certsTask.Result;
            var routes = routesTask.Result;

            _expiringCerts = certs.Count(c =>
                DateTime.TryParse(c.NotAfter, out var expiry) &&
                expiry < DateTime.UtcNow.AddDays(14));

            foreach (var route in routes.Where(r => r.Upstreams.Count == 0))
            {
                _alerts.Add(new AlertEntry(route.Domain, "Keine Kammern konfiguriert", "warning"));
            }

            foreach (var cert in certs.Where(c =>
                DateTime.TryParse(c.NotAfter, out var ex) && ex < DateTime.UtcNow.AddDays(14)))
            {
                var days = (DateTime.Parse(cert.NotAfter) - DateTime.UtcNow).Days;
                _alerts.Add(new AlertEntry(cert.Domain, $"Siegel läuft ab in {days}d", "warning"));
            }
        }
        catch (Exception ex)
        {
            _error = $"Verbindung zum Proxy fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private sealed record AlertEntry(string Domain, string Message, string Severity);
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk.UI/Components/Pages/Dashboard.razor
git commit -m "feat(ui): rewrite Dashboard as Leitstand with Blueprint KPIs and Störungen panel"
```

---

## Task 5: Schleusentore (Route List)

**Files:**
- Modify: `src/Schleusenwerk.UI/Components/Pages/RouteList.razor`

- [ ] **Step 1: Rewrite RouteList.razor as Schleusentore**

Replace the full content of `src/Schleusenwerk.UI/Components/Pages/RouteList.razor`:

```razor
@page "/tore"
@inject IRouteClient RouteClient
@inject ICertificateClient CertClient
@inject NavigationManager Nav
@inject ISnackbar Snackbar
@rendermode @(new InteractiveServerRenderMode())

<PageTitle>Schleusentore — Schleusenwerk</PageTitle>

<div class="page-header">
    <h1>SCHLEUSENTORE</h1>
    <span class="separator">——</span>
    <span class="subtitle">@_routes.Count Tore konfiguriert</span>
    <div style="margin-left: auto;">
        <a href="/tore/neu" class="bp-btn-outline">+ TOR EINSETZEN</a>
    </div>
</div>

@if (_loading)
{
    <MudProgressCircular Indeterminate="true" Color="Color.Primary" />
}
else
{
    <MudTable Items="_routes" Hover="true" Dense="true" Elevation="0">
        <HeaderContent>
            <MudTh Style="width: 28px;"></MudTh>
            <MudTh>Domain</MudTh>
            <MudTh>Quelle</MudTh>
            <MudTh>Status</MudTh>
            <MudTh>Kammern</MudTh>
            <MudTh>Siegel</MudTh>
            <MudTh Style="width: 60px;"></MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd>
                <HealthIndicator Status="@(IsGesperrt(context.Domain) ? "gesperrt" : "offen")" />
            </MudTd>
            <MudTd>
                <MudLink Href="@($"/tore/{context.Domain}")" Style="color: var(--bp-text-primary); font-family: var(--bp-font-mono);">
                    @context.Domain
                </MudLink>
            </MudTd>
            <MudTd><SourceBadge Source="@context.Source" /></MudTd>
            <MudTd Style="font-family: var(--bp-font-mono); font-size: 11px;">
                @if (IsGesperrt(context.Domain))
                {
                    <span style="color: var(--bp-error);">gesperrt</span>
                }
                else
                {
                    <span style="color: var(--bp-primary);">offen</span>
                }
            </MudTd>
            <MudTd Style="text-align: center;">@context.Upstreams.Count</MudTd>
            <MudTd Style="text-align: center;">
                <SiegelIcon Status="@GetSiegelStatus(context.Domain)" />
            </MudTd>
            <MudTd>
                <MudIconButton Icon="@Icons.Material.Filled.Delete" Size="Size.Small"
                               Style="color: var(--bp-error);"
                               OnClick="@(() => DeleteAsync(context.Domain))" />
            </MudTd>
        </RowTemplate>
        <RowClassFunc>@((item, _) => IsGesperrt(item.Domain) ? "bp-row-error" : "")</RowClassFunc>
    </MudTable>
}

@code {
    private List<RouteSummary> _routes = [];
    private HashSet<string> _unhealthyDomains = [];
    private Dictionary<string, string> _certStatus = new();
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var routesTask = RouteClient.ListRoutesAsync();
            var certsTask = CertClient.ListCertificatesAsync();

            await Task.WhenAll(routesTask, certsTask);

            _routes = routesTask.Result.ToList();

            foreach (var cert in certsTask.Result)
            {
                if (cert.IsSelfSigned)
                {
                    _certStatus[cert.Domain] = "self-signed";
                }
                else if (DateTime.TryParse(cert.NotAfter, out var expiry) && expiry < DateTime.UtcNow.AddDays(14))
                {
                    _certStatus[cert.Domain] = "expiring";
                }
                else
                {
                    _certStatus[cert.Domain] = "valid";
                }
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private bool IsGesperrt(string domain) => _unhealthyDomains.Contains(domain);

    private string GetSiegelStatus(string domain) =>
        _certStatus.GetValueOrDefault(domain, "valid");

    private async Task DeleteAsync(string domain)
    {
        var result = await RouteClient.DeleteRouteAsync(domain);
        if (result.Success)
        {
            _routes.RemoveAll(r => r.Domain == domain);
            Snackbar.Add($"Tor {domain} ausgebaut", Severity.Success);
        }
        else
        {
            Snackbar.Add(result.ErrorMessage, Severity.Error);
        }
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk.UI/Components/Pages/RouteList.razor
git commit -m "feat(ui): rewrite RouteList as Schleusentore with Blueprint table and Siegel icons"
```

---

## Task 6: Tor einsetzen (Route Create)

**Files:**
- Modify: `src/Schleusenwerk.UI/Components/Pages/RouteCreate.razor`

- [ ] **Step 1: Rewrite RouteCreate.razor as Tor einsetzen**

Replace the full content of `src/Schleusenwerk.UI/Components/Pages/RouteCreate.razor`:

```razor
@page "/tore/neu"
@inject IRouteClient RouteClient
@inject NavigationManager Nav
@inject ISnackbar Snackbar
@rendermode @(new InteractiveServerRenderMode())

<PageTitle>Tor einsetzen — Schleusenwerk</PageTitle>

<div class="bp-breadcrumb">
    <a href="/tore">SCHLEUSENTORE</a>
    <span style="color: var(--bp-border); margin: 0 6px;">/</span>
    <span class="current">NEU</span>
</div>

<div class="page-header">
    <h1>TOR EINSETZEN</h1>
</div>

<div class="bp-panel" style="max-width: 600px;">
    <MudTextField @bind-Value="_domain" Label="Domain" Required="true"
                  Placeholder="example.com" Class="mb-3"
                  Style="font-family: var(--bp-font-mono);" />
    <MudTextField @bind-Value="_firstUpstreamUrl" Label="Erste Kammer" Required="true"
                  Placeholder="http://backend:8080" Class="mb-3"
                  Style="font-family: var(--bp-font-mono);" />
    <MudSwitch @bind-Value="_forceHttps" Label="Versiegelt (HTTPS)" Color="Color.Primary" Class="mb-3" />
    <MudSlider @bind-Value="_timeoutSeconds" Min="5" Max="300" Step="5"
               Color="Color.Primary"
               Class="mb-3">
        <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">
            Timeout: @(_timeoutSeconds)s
        </span>
    </MudSlider>

    <div style="display: flex; gap: 12px; margin-top: 16px;">
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="SubmitAsync" Disabled="_submitting">
            @if (_submitting)
            {
                <MudProgressCircular Size="Size.Small" Indeterminate="true" />
            }
            else
            {
                <span>TOR EINSETZEN</span>
            }
        </MudButton>
        <MudButton Href="/tore" Variant="Variant.Text" Style="color: var(--bp-text-secondary);">
            Abbrechen
        </MudButton>
    </div>
</div>

@code {
    private string _domain = "";
    private string _firstUpstreamUrl = "";
    private bool _forceHttps;
    private int _timeoutSeconds = 30;
    private bool _submitting;

    private async Task SubmitAsync()
    {
        _submitting = true;
        try
        {
            var result = await RouteClient.AddRouteAsync(new AddRouteRequest
            {
                Domain = _domain,
                ForceHttps = _forceHttps,
                TimeoutSeconds = _timeoutSeconds,
                FirstUpstreamUrl = _firstUpstreamUrl
            });

            if (result.Success)
            {
                Nav.NavigateTo("/tore");
            }
            else
            {
                Snackbar.Add(result.ErrorMessage, Severity.Error);
            }
        }
        finally
        {
            _submitting = false;
        }
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk.UI/Components/Pages/RouteCreate.razor
git commit -m "feat(ui): rewrite RouteCreate as 'Tor einsetzen' with Blueprint styling"
```

---

## Task 7: Tor-Detail (Route Detail)

**Files:**
- Modify: `src/Schleusenwerk.UI/Components/Pages/RouteDetail.razor`

- [ ] **Step 1: Rewrite RouteDetail.razor with Blueprint styling**

Replace the full content of `src/Schleusenwerk.UI/Components/Pages/RouteDetail.razor`:

```razor
@page "/tore/{Domain}"
@inject IRouteClient RouteClient
@inject ISnackbar Snackbar
@rendermode @(new InteractiveServerRenderMode())

<PageTitle>@Domain — Schleusenwerk</PageTitle>

<div class="bp-breadcrumb">
    <a href="/tore">SCHLEUSENTORE</a>
    <span style="color: var(--bp-border); margin: 0 6px;">/</span>
    <span class="current">@Domain</span>
</div>

@if (_loading)
{
    <MudProgressCircular Indeterminate="true" Color="Color.Primary" />
}
else if (_detail is null)
{
    <MudAlert Severity="Severity.Error">Tor nicht gefunden.</MudAlert>
}
else
{
    <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 24px;">
        <HealthIndicator Status="@(IsHealthy() ? "offen" : "gesperrt")" />
        <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 18px; font-weight: 700;">@Domain</span>
        <span class="bp-badge @(IsHealthy() ? "bp-badge-primary" : "bp-badge-error")">
            @(IsHealthy() ? "offen" : "gesperrt")
        </span>
        @if (_detail.ForceHttps)
        {
            <span class="bp-badge bp-badge-primary">versiegelt</span>
        }
    </div>

    <MudGrid Spacing="3">
        <MudItem xs="12" md="4">
            <div class="bp-panel">
                <div class="bp-panel-label">Konfiguration</div>

                <MudSwitch @bind-Value="_forceHttps" Label="Versiegelung (HTTPS)" Color="Color.Primary" Class="mb-3" />
                <MudSlider @bind-Value="_timeoutSeconds" Min="5" Max="300" Step="5"
                           Color="Color.Primary" Class="mb-3">
                    <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">
                        Timeout: @(_timeoutSeconds)s
                    </span>
                </MudSlider>

                <MudButton Variant="Variant.Filled" Color="Color.Primary" FullWidth="true"
                           OnClick="SaveConfigAsync" Style="font-family: var(--bp-font-mono); letter-spacing: 0.5px;">
                    SPEICHERN
                </MudButton>
            </div>
        </MudItem>
        <MudItem xs="12" md="8">
            <div class="bp-panel">
                <div style="display: flex; align-items: center; margin-bottom: 14px;">
                    <span class="bp-panel-label" style="margin-bottom: 0;">Kammern</span>
                    <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 11px; margin-left: 8px;">
                        @_detail.Upstreams.Count
                    </span>
                </div>

                <div style="display: flex; flex-direction: column; gap: 8px; margin-bottom: 14px;">
                    @foreach (var upstream in _detail.Upstreams)
                    {
                        <div style="display: flex; align-items: center; gap: 10px; padding: 8px 10px; border: 1px solid rgba(30,58,95,0.8); border-radius: 4px;">
                            <HealthIndicator Status="@(IsUpstreamHealthy(upstream.Url) ? "offen" : "gesperrt")" />
                            <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 12px; flex: 1;">
                                @upstream.Url
                            </span>
                            <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 11px;">
                                w:@upstream.Weight
                            </span>
                            <MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small"
                                           Style="color: var(--bp-error);"
                                           OnClick="@(() => RemoveUpstreamAsync(upstream.Url))" />
                        </div>
                    }
                </div>

                <div style="display: flex; gap: 8px;">
                    <MudTextField @bind-Value="_newUpstreamUrl" Placeholder="http://upstream:port"
                                  Style="font-family: var(--bp-font-mono); flex: 1;" />
                    <MudButton Variant="Variant.Outlined" Color="Color.Primary"
                               OnClick="AddUpstreamAsync"
                               Style="font-family: var(--bp-font-mono); letter-spacing: 0.5px;">
                        + KAMMER
                    </MudButton>
                </div>
            </div>
        </MudItem>
    </MudGrid>
}

@code {
    [Parameter] public string Domain { get; set; } = "";

    private RouteDetail? _detail;
    private bool _loading = true;
    private bool _forceHttps;
    private int _timeoutSeconds = 30;
    private string _newUpstreamUrl = "";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _detail = await RouteClient.GetRouteAsync(Domain);
            _forceHttps = _detail.ForceHttps;
            _timeoutSeconds = _detail.TimeoutSeconds > 0 ? _detail.TimeoutSeconds : 30;
        }
        finally
        {
            _loading = false;
        }
    }

    private bool IsHealthy() =>
        _detail?.Health.All(h => h.IsHealthy) ?? true;

    private bool IsUpstreamHealthy(string url) =>
        _detail?.Health.FirstOrDefault(h => h.Url == url)?.IsHealthy ?? true;

    private async Task SaveConfigAsync()
    {
        var result = await RouteClient.UpdateRouteAsync(new UpdateRouteRequest
        {
            Domain = Domain,
            ForceHttps = _forceHttps,
            TimeoutSeconds = _timeoutSeconds
        });
        Snackbar.Add(result.Success ? "Konfiguration gespeichert" : result.ErrorMessage,
            result.Success ? Severity.Success : Severity.Error);
    }

    private async Task AddUpstreamAsync()
    {
        if (string.IsNullOrWhiteSpace(_newUpstreamUrl)) { return; }
        var result = await RouteClient.AddUpstreamAsync(Domain, _newUpstreamUrl);
        if (result.Success)
        {
            _newUpstreamUrl = "";
            _detail = await RouteClient.GetRouteAsync(Domain);
        }
        else
        {
            Snackbar.Add(result.ErrorMessage, Severity.Error);
        }
    }

    private async Task RemoveUpstreamAsync(string url)
    {
        var result = await RouteClient.RemoveUpstreamAsync(Domain, url);
        if (result.Success)
        {
            _detail = await RouteClient.GetRouteAsync(Domain);
        }
        else
        {
            Snackbar.Add(result.ErrorMessage, Severity.Error);
        }
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk.UI/Components/Pages/RouteDetail.razor
git commit -m "feat(ui): rewrite RouteDetail as Tor-Detail with Blueprint two-column layout"
```

---

## Task 8: Siegel (Certificate List)

**Files:**
- Modify: `src/Schleusenwerk.UI/Components/Pages/CertificateList.razor`

- [ ] **Step 1: Rewrite CertificateList.razor as Siegel**

Replace the full content of `src/Schleusenwerk.UI/Components/Pages/CertificateList.razor`:

```razor
@page "/siegel"
@inject ICertificateClient CertClient
@inject ISnackbar Snackbar
@rendermode @(new InteractiveServerRenderMode())

<PageTitle>Siegel — Schleusenwerk</PageTitle>

<div class="page-header">
    <h1>SIEGEL</h1>
    <span class="separator">——</span>
    <span class="subtitle">TLS-Zertifikate</span>
</div>

@if (_loading)
{
    <MudProgressCircular Indeterminate="true" Color="Color.Primary" />
}
else
{
    <MudTable Items="_certs" Hover="true" Dense="true" Elevation="0">
        <HeaderContent>
            <MudTh Style="width: 28px;"></MudTh>
            <MudTh>Domain</MudTh>
            <MudTh>Fingerabdruck</MudTh>
            <MudTh>Gültig bis</MudTh>
            <MudTh>Typ</MudTh>
            <MudTh Style="width: 80px;"></MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd>
                <SiegelIcon Status="@GetSiegelStatus(context)" />
            </MudTd>
            <MudTd Style="color: var(--bp-text-primary); font-family: var(--bp-font-mono);">
                @context.Domain
            </MudTd>
            <MudTd Style="font-family: var(--bp-font-mono); font-size: 11px; color: var(--bp-text-secondary);">
                @context.Thumbprint[..Math.Min(12, context.Thumbprint.Length)]…
            </MudTd>
            <MudTd>
                @if (DateTime.TryParse(context.NotAfter, out var expiry))
                {
                    <span style="font-family: var(--bp-font-mono); color: @(IsExpiringSoon(expiry) ? "var(--bp-warning)" : "var(--bp-primary)");">
                        @expiry.ToString("yyyy-MM-dd")
                    </span>
                }
            </MudTd>
            <MudTd>
                @if (context.IsSelfSigned)
                {
                    <span class="bp-badge bp-badge-warning">Selbst</span>
                }
                else
                {
                    <span class="bp-badge bp-badge-primary">ACME</span>
                }
            </MudTd>
            <MudTd>
                <MudButton Size="Size.Small" Variant="Variant.Outlined"
                           Style="@($"font-family: var(--bp-font-mono); font-size: 11px; color: {(IsExpiringSoon(context) ? "var(--bp-warning)" : "var(--bp-text-secondary)")}; border-color: {(IsExpiringSoon(context) ? "var(--bp-warning)" : "var(--bp-border)")};")"
                           OnClick="@(() => RenewAsync(context.Domain))">
                    Erneuern
                </MudButton>
            </MudTd>
        </RowTemplate>
    </MudTable>
}

@code {
    private List<CertificateSummary> _certs = [];
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _certs = (await CertClient.ListCertificatesAsync()).ToList();
        }
        finally
        {
            _loading = false;
        }
    }

    private static bool IsExpiringSoon(DateTime expiry) =>
        expiry < DateTime.UtcNow.AddDays(14);

    private static bool IsExpiringSoon(CertificateSummary cert) =>
        DateTime.TryParse(cert.NotAfter, out var expiry) && IsExpiringSoon(expiry);

    private static string GetSiegelStatus(CertificateSummary cert)
    {
        if (cert.IsSelfSigned) { return "self-signed"; }
        if (DateTime.TryParse(cert.NotAfter, out var expiry) && IsExpiringSoon(expiry)) { return "expiring"; }
        return "valid";
    }

    private async Task RenewAsync(string domain)
    {
        var result = await CertClient.ProvisionCertificateAsync(domain);
        Snackbar.Add(
            result.Success ? $"Siegelerneuerung für {domain} angestoßen" : result.ErrorMessage,
            result.Success ? Severity.Info : Severity.Error);
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk.UI/Components/Pages/CertificateList.razor
git commit -m "feat(ui): rewrite CertificateList as Siegel with Blueprint table and status icons"
```

---

## Task 9: Flussprotokoll (Live Event Log)

**Files:**
- Create: `src/Schleusenwerk.UI/Components/Pages/Flussprotokoll.razor`

- [ ] **Step 1: Create Flussprotokoll.razor with SignalR live event stream**

Create `src/Schleusenwerk.UI/Components/Pages/Flussprotokoll.razor`:

```razor
@page "/flussprotokoll"
@using Microsoft.AspNetCore.SignalR.Client
@inject NavigationManager Nav
@implements IAsyncDisposable
@rendermode @(new InteractiveServerRenderMode())

<PageTitle>Flussprotokoll — Schleusenwerk</PageTitle>

<div class="page-header">
    <h1>FLUSSPROTOKOLL</h1>
    <span class="separator">——</span>
    <div class="bp-live-dot"></div>
    <span style="color: var(--bp-primary); font-family: var(--bp-font-mono); font-size: 11px;">LIVE</span>
</div>

<div style="display: flex; gap: 8px; margin-bottom: 16px;">
    <MudSelect T="string" @bind-Value="_filterDomain" Label="Tor" Dense="true" Variant="Variant.Outlined"
               Style="max-width: 200px; font-family: var(--bp-font-mono); font-size: 11px;">
        <MudSelectItem Value="@("")">Alle Tore</MudSelectItem>
        @foreach (var domain in _knownDomains)
        {
            <MudSelectItem Value="@domain">@domain</MudSelectItem>
        }
    </MudSelect>

    <MudSelect T="string" @bind-Value="_filterStatus" Label="Status" Dense="true" Variant="Variant.Outlined"
               Style="max-width: 150px; font-family: var(--bp-font-mono); font-size: 11px;">
        <MudSelectItem Value="@("")">Alle Status</MudSelectItem>
        <MudSelectItem Value="@("2xx")">2xx</MudSelectItem>
        <MudSelectItem Value="@("4xx")">4xx</MudSelectItem>
        <MudSelectItem Value="@("5xx")">5xx</MudSelectItem>
    </MudSelect>

    <div style="margin-left: auto;">
        <MudButton Variant="Variant.Outlined" Style="font-family: var(--bp-font-mono); font-size: 11px;"
                   OnClick="TogglePause">
            @(_paused ? "▶ Fortsetzen" : "⏸ Pausieren")
        </MudButton>
    </div>
</div>

<div class="bp-panel" style="padding: 0; overflow: hidden;">
    <div style="display: grid; grid-template-columns: 80px 50px 1fr 50px 60px 60px; padding: 10px 14px; border-bottom: 1px solid var(--bp-border); background: rgba(30,58,95,0.3);">
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">ZEIT</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">TYP</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">TOR</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">STATUS</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">KAMMER</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px; text-align: right;">INFO</span>
    </div>

    @foreach (var evt in FilteredEvents)
    {
        <div style="display: grid; grid-template-columns: 80px 50px 1fr 50px 60px 60px; padding: 7px 14px; border-bottom: 1px solid rgba(30,58,95,0.4); align-items: center; font-family: var(--bp-font-mono); font-size: 12px; @(evt.IsError ? "background: rgba(255,107,107,0.03);" : "")">
            <span style="color: var(--bp-border);">@evt.Time</span>
            <span style="color: @GetTypeColor(evt.Type);">@evt.Type</span>
            <span style="color: var(--bp-text-primary);">@evt.Domain</span>
            <span style="color: @(evt.IsError ? "var(--bp-error)" : "var(--bp-primary)");">
                @(evt.IsHealthy ? "→" : "⊧")
            </span>
            <span style="color: var(--bp-text-secondary); font-size: 11px;">@evt.Upstream</span>
            <span style="color: var(--bp-text-secondary); text-align: right; font-size: 11px;">@evt.Message</span>
        </div>
    }

    @if (_events.Count == 0)
    {
        <div style="padding: 24px; text-align: center; color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">
            Warte auf Ereignisse…
        </div>
    }
</div>

@code {
    private HubConnection? _hub;
    private readonly List<FlowEntry> _events = [];
    private readonly HashSet<string> _knownDomains = [];
    private string _filterDomain = "";
    private string _filterStatus = "";
    private bool _paused;
    private const int MaxEvents = 200;

    private IEnumerable<FlowEntry> FilteredEvents => _events
        .Where(e => string.IsNullOrEmpty(_filterDomain) || e.Domain == _filterDomain)
        .Where(e => string.IsNullOrEmpty(_filterStatus) || MatchesStatusFilter(e));

    protected override async Task OnInitializedAsync()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/hubs/events"))
            .WithAutomaticReconnect()
            .Build();

        _hub.On<ProxyEvent>("OnProxyEvent", evt =>
        {
            if (_paused) { return; }

            _events.Insert(0, new FlowEntry(
                DateTime.Now.ToString("HH:mm:ss"),
                evt.Type.ToString(),
                evt.Domain,
                evt.IsHealthy,
                evt.UpstreamUrl,
                evt.Message));

            if (!string.IsNullOrEmpty(evt.Domain))
            {
                _knownDomains.Add(evt.Domain);
            }

            while (_events.Count > MaxEvents)
            {
                _events.RemoveAt(_events.Count - 1);
            }

            InvokeAsync(StateHasChanged);
        });

        await _hub.StartAsync();
    }

    private void TogglePause() => _paused = !_paused;

    private bool MatchesStatusFilter(FlowEntry entry) => _filterStatus switch
    {
        "2xx" => entry.IsHealthy,
        "4xx" => !entry.IsHealthy && !entry.IsError,
        "5xx" => entry.IsError,
        _ => true
    };

    private static string GetTypeColor(string type) => type switch
    {
        "ROUTE_UPDATED" => "var(--bp-primary)",
        "ROUTE_REMOVED" => "var(--bp-error)",
        "UPSTREAM_HEALTH_CHANGED" => "var(--bp-warning)",
        "CERTIFICATE_PROVISIONED" => "var(--bp-primary)",
        "CERTIFICATE_EXPIRING" => "var(--bp-warning)",
        _ => "var(--bp-text-secondary)"
    };

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
        }
    }

    private sealed record FlowEntry(
        string Time,
        string Type,
        string Domain,
        bool IsHealthy,
        string Upstream,
        string Message)
    {
        public bool IsError => !IsHealthy && Type == "UPSTREAM_HEALTH_CHANGED";
    }
}
```

- [ ] **Step 2: Add Microsoft.AspNetCore.SignalR.Client package**

Run: `dotnet add src/Schleusenwerk.UI/Schleusenwerk.UI.csproj package Microsoft.AspNetCore.SignalR.Client`

- [ ] **Step 3: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.UI/Components/Pages/Flussprotokoll.razor src/Schleusenwerk.UI/Schleusenwerk.UI.csproj
git commit -m "feat(ui): add Flussprotokoll page with live SignalR event stream"
```

---

## Task 10: Discovery Proto + Client for Hafenbecken

**Files:**
- Create: `src/Schleusenwerk.Contracts/Protos/discovery.proto`
- Create: `src/Schleusenwerk.UI/Services/IDiscoveryClient.cs`
- Create: `src/Schleusenwerk.UI/Services/DiscoveryClient.cs`
- Modify: `src/Schleusenwerk.UI/Program.cs`

- [ ] **Step 1: Create discovery.proto**

Create `src/Schleusenwerk.Contracts/Protos/discovery.proto`:

```protobuf
syntax = "proto3";
option csharp_namespace = "Schleusenwerk.Contracts";
import "google/protobuf/empty.proto";

service DiscoveryService {
  rpc ListContainers (google.protobuf.Empty) returns (ListContainersResponse);
}

message ListContainersResponse {
  repeated DiscoveredContainer containers = 1;
}

message DiscoveredContainer {
  string name = 1;
  string image = 2;
  string status = 3;
  map<string, string> labels = 4;
  string assigned_domain = 5;
  string conflict_reason = 6;
}
```

- [ ] **Step 2: Build contracts to verify proto compiles**

Run: `dotnet build --project src/Schleusenwerk.Contracts/Schleusenwerk.Contracts.csproj`
Expected: Build succeeds, generates `DiscoveryGrpc.cs`.

- [ ] **Step 3: Create IDiscoveryClient.cs**

Create `src/Schleusenwerk.UI/Services/IDiscoveryClient.cs`:

```csharp
using Schleusenwerk.Contracts;

namespace Schleusenwerk.UI.Services;

public interface IDiscoveryClient
{
    Task<IReadOnlyList<DiscoveredContainer>> ListContainersAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Create DiscoveryClient.cs**

Create `src/Schleusenwerk.UI/Services/DiscoveryClient.cs`:

```csharp
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Schleusenwerk.Contracts;

namespace Schleusenwerk.UI.Services;

internal sealed class DiscoveryClient : IDiscoveryClient
{
    private readonly DiscoveryService.DiscoveryServiceClient _client;

    public DiscoveryClient(GrpcChannel channel) =>
        _client = new DiscoveryService.DiscoveryServiceClient(channel);

    public async Task<IReadOnlyList<DiscoveredContainer>> ListContainersAsync(CancellationToken ct = default)
    {
        var response = await _client.ListContainersAsync(new Empty(), cancellationToken: ct);
        return response.Containers;
    }
}
```

- [ ] **Step 5: Register DiscoveryClient in Program.cs**

Add after the existing `AddSingleton<IHealthClient, HealthClient>()` line in `src/Schleusenwerk.UI/Program.cs`:

```csharp
builder.Services.AddSingleton<IDiscoveryClient, DiscoveryClient>();
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/Schleusenwerk.Contracts/Protos/discovery.proto src/Schleusenwerk.UI/Services/IDiscoveryClient.cs src/Schleusenwerk.UI/Services/DiscoveryClient.cs src/Schleusenwerk.UI/Program.cs
git commit -m "feat(contracts): add DiscoveryService proto and UI client for Hafenbecken"
```

---

## Task 11: Hafenbecken (Docker Discovery Page)

**Files:**
- Create: `src/Schleusenwerk.UI/Components/Pages/Hafenbecken.razor`

- [ ] **Step 1: Create Hafenbecken.razor**

Create `src/Schleusenwerk.UI/Components/Pages/Hafenbecken.razor`:

```razor
@page "/hafenbecken"
@inject IDiscoveryClient DiscoveryClient
@inject ISnackbar Snackbar
@rendermode @(new InteractiveServerRenderMode())

<PageTitle>Hafenbecken — Schleusenwerk</PageTitle>

<div class="page-header">
    <h1>HAFENBECKEN</h1>
    <span class="separator">——</span>
    <span class="subtitle">Docker-Erkennung</span>
    <div style="margin-left: auto; display: flex; align-items: center; gap: 6px;">
        <div class="@(_connected ? "bp-live-dot" : "bp-status-dot gesperrt")" style="width: 7px; height: 7px;"></div>
        <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 11px;">
            @(_connected ? "Socket verbunden" : "Nicht verbunden")
        </span>
    </div>
</div>

@if (_loading)
{
    <MudProgressCircular Indeterminate="true" Color="Color.Primary" />
}
else if (_error is not null)
{
    <MudAlert Severity="Severity.Error">@_error</MudAlert>
}
else
{
    <MudGrid Spacing="3">
        @foreach (var container in _containers)
        {
            <MudItem xs="12" md="6">
                <div class="bp-panel" style="@GetContainerStyle(container)">
                    <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 10px;">
                        <HealthIndicator Status="@GetContainerStatus(container)" />
                        <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 13px; font-weight: 600;">
                            @container.Name
                        </span>
                        <span style="color: @GetStatusColor(container); font-family: var(--bp-font-mono); font-size: 10px; margin-left: auto;">
                            @GetStatusLabel(container)
                        </span>
                    </div>

                    <div style="font-family: var(--bp-font-mono); font-size: 11px; color: var(--bp-text-secondary); line-height: 1.8;">
                        <div>Image: <span style="color: var(--bp-text-primary);">@container.Image</span></div>
                        @if (container.Labels.Count > 0)
                        {
                            <div>Labels:</div>
                            @foreach (var label in container.Labels.Where(l => l.Key.StartsWith("schleusenwerk.")))
                            {
                                <div style="margin-left: 12px;">
                                    <span style="color: var(--bp-primary);">@label.Key</span>=<span style="color: var(--bp-text-primary);">@label.Value</span>
                                </div>
                            }
                        }
                        else
                        {
                            <div style="color: var(--bp-border);">Keine Schleusenwerk-Labels erkannt</div>
                        }
                    </div>

                    @if (!string.IsNullOrEmpty(container.AssignedDomain))
                    {
                        <div style="margin-top: 10px; display: flex; align-items: center; gap: 6px; font-size: 11px;">
                            <span style="color: var(--bp-primary);">→</span>
                            <span style="color: var(--bp-text-secondary);">Zugeordnet an Tor</span>
                            <a href="/tore/@container.AssignedDomain"
                               style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); text-decoration: underline; text-decoration-color: var(--bp-border);">
                                @container.AssignedDomain
                            </a>
                        </div>
                    }

                    @if (!string.IsNullOrEmpty(container.ConflictReason))
                    {
                        <div style="margin-top: 10px; padding: 6px 10px; background: rgba(240,192,0,0.06); border: 1px solid rgba(240,192,0,0.15); border-radius: 4px;">
                            <span style="color: var(--bp-warning); font-size: 11px;">⚠ @container.ConflictReason</span>
                        </div>
                    }
                </div>
            </MudItem>
        }
    </MudGrid>

    @if (_containers.Count == 0)
    {
        <div class="bp-panel" style="text-align: center;">
            <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">
                Keine Container erkannt
            </span>
        </div>
    }
}

@code {
    private List<DiscoveredContainer> _containers = [];
    private bool _loading = true;
    private bool _connected = true;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _containers = (await DiscoveryClient.ListContainersAsync()).ToList();
        }
        catch (Exception ex)
        {
            _error = $"Docker-Erkennung nicht erreichbar: {ex.Message}";
            _connected = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private static bool HasLabels(DiscoveredContainer c) =>
        c.Labels.Any(l => l.Key.StartsWith("schleusenwerk."));

    private static string GetContainerStatus(DiscoveredContainer c)
    {
        if (!string.IsNullOrEmpty(c.ConflictReason)) { return "warnung"; }
        if (!HasLabels(c)) { return "neutral"; }
        return "offen";
    }

    private static string GetContainerStyle(DiscoveredContainer c)
    {
        if (!string.IsNullOrEmpty(c.ConflictReason))
        {
            return "border-color: rgba(240,192,0,0.3);";
        }
        if (!HasLabels(c))
        {
            return "opacity: 0.6;";
        }
        return "";
    }

    private static string GetStatusColor(DiscoveredContainer c)
    {
        if (!string.IsNullOrEmpty(c.ConflictReason)) { return "var(--bp-warning)"; }
        if (!HasLabels(c)) { return "var(--bp-text-secondary)"; }
        return "var(--bp-primary)";
    }

    private static string GetStatusLabel(DiscoveredContainer c)
    {
        if (!string.IsNullOrEmpty(c.ConflictReason)) { return "Konflikt"; }
        if (!HasLabels(c)) { return "kein Label"; }
        return c.Status;
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk.UI/Components/Pages/Hafenbecken.razor
git commit -m "feat(ui): add Hafenbecken page showing Docker-discovered containers"
```

---

## Task 12: Stellwerk (Settings Page)

**Files:**
- Create: `src/Schleusenwerk.UI/Components/Pages/Stellwerk.razor`

- [ ] **Step 1: Create Stellwerk.razor**

Create `src/Schleusenwerk.UI/Components/Pages/Stellwerk.razor`:

```razor
@page "/stellwerk"
@inject IHealthClient HealthClient
@rendermode @(new InteractiveServerRenderMode())

<PageTitle>Stellwerk — Schleusenwerk</PageTitle>

<div class="page-header">
    <h1>STELLWERK</h1>
    <span class="separator">——</span>
    <span class="subtitle">Systemkonfiguration</span>
</div>

<MudGrid Spacing="3">
    <MudItem xs="12" md="6">
        <div class="bp-panel">
            <div class="bp-panel-label">ACME-Konfiguration</div>

            <MudTextField @bind-Value="_acmeEmail" Label="Kontakt-E-Mail"
                          Placeholder="admin@example.com" Class="mb-3"
                          Style="font-family: var(--bp-font-mono);" />

            <MudSelect T="string" @bind-Value="_acmeProvider" Label="Anbieter" Variant="Variant.Outlined"
                       Class="mb-3" Style="font-family: var(--bp-font-mono);">
                <MudSelectItem Value="@("letsencrypt")">Let's Encrypt</MudSelectItem>
                <MudSelectItem Value="@("letsencrypt-staging")">Let's Encrypt (Staging)</MudSelectItem>
            </MudSelect>

            <MudButton Variant="Variant.Filled" Color="Color.Primary" Disabled="true"
                       Style="font-family: var(--bp-font-mono); letter-spacing: 0.5px;">
                SPEICHERN
            </MudButton>
            <MudText Style="color: var(--bp-text-secondary); font-size: 11px; margin-top: 8px;">
                Konfiguration wird über appsettings.json verwaltet
            </MudText>
        </div>
    </MudItem>

    <MudItem xs="12" md="6">
        <div class="bp-panel">
            <div class="bp-panel-label">Systeminformation</div>

            <div style="font-family: var(--bp-font-mono); font-size: 12px; line-height: 2.2;">
                <div style="display: flex; justify-content: space-between;">
                    <span style="color: var(--bp-text-secondary);">Version</span>
                    <span style="color: var(--bp-text-primary);">0.1.0</span>
                </div>
                <div style="display: flex; justify-content: space-between;">
                    <span style="color: var(--bp-text-secondary);">Laufzeit</span>
                    <span style="color: var(--bp-text-primary);">@_uptime</span>
                </div>
                <div style="display: flex; justify-content: space-between;">
                    <span style="color: var(--bp-text-secondary);">Tore</span>
                    <span style="color: var(--bp-primary);">@_routeCount</span>
                </div>
                <div style="display: flex; justify-content: space-between;">
                    <span style="color: var(--bp-text-secondary);">Status</span>
                    <span style="color: @(_healthy ? "var(--bp-primary)" : "var(--bp-error)");">
                        @(_healthy ? "Alle Systeme offen" : "Störungen vorhanden")
                    </span>
                </div>
            </div>
        </div>
    </MudItem>
</MudGrid>

@code {
    private string _acmeEmail = "";
    private string _acmeProvider = "letsencrypt";
    private string _uptime = "—";
    private int _routeCount;
    private bool _healthy = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var health = await HealthClient.GetHealthAsync();
            _routeCount = health.RouteCount;
            _healthy = health.UnhealthyCount == 0;
            _uptime = FormatUptime(System.Diagnostics.Process.GetCurrentProcess().StartTime);
        }
        catch
        {
            _healthy = false;
        }
    }

    private static string FormatUptime(DateTime startTime)
    {
        var elapsed = DateTime.Now - startTime;
        if (elapsed.TotalDays >= 1) { return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h"; }
        if (elapsed.TotalHours >= 1) { return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"; }
        return $"{(int)elapsed.TotalMinutes}m";
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk.UI/Components/Pages/Stellwerk.razor
git commit -m "feat(ui): add Stellwerk settings page with system info and ACME config display"
```

---

## Task 13: Delete Home.razor + Final Build Verification

**Files:**
- Delete: `src/Schleusenwerk.UI/Components/Pages/Home.razor` (already deleted in git status)

- [ ] **Step 1: Ensure Home.razor is removed from git tracking**

The git status shows `Home.razor` is already deleted. Confirm:

Run: `git status -- src/Schleusenwerk.UI/Components/Pages/Home.razor`
Expected: Shows deleted status.

- [ ] **Step 2: Full solution build**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Run existing tests**

Run: `dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass (UI changes don't affect backend tests).

- [ ] **Step 4: Commit all remaining changes**

```bash
git add -A
git commit -m "feat(ui): complete Blueprint theme redesign with Schleusen-Terminologie

- Blueprint CSS with custom properties, grid background, monospace typography
- MudTheme PaletteDark configuration for Blueprint colors
- Top navigation Schaltleiste replacing sidebar drawer
- All pages rewritten: Leitstand, Schleusentore, Tor-Detail, Tor einsetzen, Siegel
- New pages: Flussprotokoll (live SignalR events), Hafenbecken (Docker discovery), Stellwerk (settings)
- Shared components: StatusIndicator, QuelleBadge, SiegelIcon
- Discovery proto + gRPC client for Hafenbecken"
```
