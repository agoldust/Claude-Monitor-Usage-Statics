# Architecture

Diagrams of the system, components, refresh cycle, sign-in, startup and agent workflow are in [DIAGRAMS.md](DIAGRAMS.md).

## Layout

```
ClaudeUsageMonitor\
  App.xaml(.cs)            entry; single instance (named mutex); --widget switch; dark glass theme
  MainWindow.xaml(.cs)     graphical dashboard (borderless dark glass, own title buttons)
  WidgetWindow.xaml(.cs)   always-on-top glass widget
  SettingsWindow.xaml(.cs) refresh interval, opacities, blur, always on top, start with Windows
  TrayIcon.cs              notification-area icon (WinForms NotifyIcon; UseWindowsForms is set only for this)
  AboutWindow.xaml(.cs)    author, license, version and build, data and privacy, disclaimer, credits
  LoginWindow.xaml(.cs)    WebView2 sign-in to claude.ai (normal frame: WebView2 cannot draw in a transparent window)
  Controls\                RingGauge, Palette, GlassEffect (blur), GlassDialog (base for Settings and About),
                           MaximizeFix (borderless maximize to the current monitor's work area)
  Models\                  AccountInfo, UsageRecord, UsageSummary, GridRows,
                           LiveUsage, ResetCountdown, AppSettings
  Services\                ClaudePaths, ClaudeLogReader, AccountReader, PricingTable,
                           UsageAggregator, UsageFormatting,
                           ClaudeWebSession, LiveUsageService, LiveUsageParser,
                           MonitorHub, SettingsStore, StartupRegistration, AppInfo, DebugLog
.claude\agents\            project subagents
docs\                      documentation
```

## Data flow

```
*.jsonl logs -> ClaudeLogReader -> UsageRecord list -> UsageAggregator -> UsageSummary --+
.claude.json + .credentials.json -> AccountReader -> AccountInfo -----------------------+
claude.ai /api/... -> ClaudeWebSession (hidden WebView2) -> LiveUsageService            +--> MonitorHub --> MainWindow
                                                         -> LiveUsageParser -> LiveUsage +               --> WidgetWindow
```

- `MonitorHub` owns the readers, the web session, the refresh timer and a reset timer. Live fetches are serialized and the next refresh time is tracked. After each live result, `ScheduleResetRefresh` arms a one-off refresh 5 s after the next limit reset, but only when that comes before the next timed refresh; if a refresh is already running, it retries after 3 s. `ClaudeWebSession` times out requests (20 s fetch, 30 s DevTools call, 30 s browser start), restarts the hidden browser after a timeout and closes late-starting browsers (generation counter). Session logs are opened with `FileShare.ReadWrite|Delete`.
- `MainWindow` picks one of four layouts from its window width: Compact (< 860 px), Normal (860 to 1539 px), Wide (1540 to 2299 px, two columns) or ExtraWide (>= 2300 px, three columns). `ApplyLayout` places the sections in code and sets the third grid column's width to zero outside ExtraWide. A single refresh (timer, reset timer, F5 or button) feeds every open window through its `Changed` event, so the dashboard and widget always show the same data.
- `ResetCountdown` (the `Reset` property of each `LimitRow`) holds the reset text for the dashboard (`Text`) and the widget (`Short`), built by `UsageFormatting.FormatReset` and `FormatResetShort`. `MainWindow` and `WidgetWindow` each run a 1-second `DispatcherTimer` that calls `UpdateCountdowns`, which only updates these texts. `UsageFormatting.DescribeAvailability` produces the "Next reset" line on the dashboard and the red "tokens available again" line, which the widget shows only while a limit is used up.
- `ClaudeLogReader` returns one deduplicated `UsageRecord` per API response; `AccountReader` builds `AccountInfo`.
- `UsageAggregator` builds totals and the per-day, per-model, token-mix and hour-of-day figures, pricing each record with `PricingTable`.
- `LiveUsageService` fetches `/api/organizations` then `/api/organizations/{uuid}/usage` and returns a `LiveUsageResult` (never throws). `ClaudeWebSession` runs requests inside the browser via `Runtime.evaluate` and only allows `/api/...` paths.
- `App` loads `AppSettings` through `SettingsStore` (`%APPDATA%\ClaudeUsageMonitor\settings.json`) and applies changes to the hub and widget. A second launch shows the dashboard of the running instance. Closing all windows leaves the app running in the tray (`TrayIcon`); only Exit quits.
- `StartupRegistration` writes the HKCU Run key using the exe path at the time it is turned on.

## Agents (`.claude\agents`)

The coordinator (the main session) delegates to five agents. The coordinator is defined in `coordinator.md` and runs as the main session with `claude --agent coordinator`. Each description starts with its model.

| Agent | Model | Role |
|---|---|---|
| code-reviewer | Sonnet | Read-only review for bugs, error handling, security and privacy |
| comment-writer | Haiku | Adds XML doc comments only, never changes code |
| docs-git-manager | Sonnet | Keeps README and docs in step with code; local commits only, never pushes |
| error-checker | Sonnet, low effort | Builds the app, finds errors and warnings, suggests exact fixes; never edits |
| ui-advisor | Haiku, low effort | Reviews the WPF windows and recommends readability and usability changes; read-only |
