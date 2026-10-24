# Architecture

## Layout

```
ClaudeUsageMonitor\
  App.xaml(.cs)            entry; single instance (named mutex); --widget switch
  MainWindow.xaml(.cs)     graphical dashboard
  WidgetWindow.xaml(.cs)   always-on-top glass widget
  SettingsWindow.xaml(.cs) refresh interval, opacity, blur, always on top, start with Windows
  LoginWindow.xaml(.cs)    WebView2 sign-in to claude.ai
  Controls\                RingGauge, Palette, GlassEffect (blur)
  Models\                  AccountInfo, UsageRecord, UsageSummary, GridRows,
                           LiveUsage, AppSettings
  Services\                ClaudePaths, ClaudeLogReader, AccountReader, PricingTable,
                           UsageAggregator, UsageFormatting,
                           ClaudeWebSession, LiveUsageService, LiveUsageParser,
                           MonitorHub, SettingsStore, StartupRegistration, DebugLog
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

- `MonitorHub` owns the readers, the web session and one timer. A single refresh (timer, F5 or button) feeds every open window through its `Changed` event, so the dashboard and widget always show the same data.
- `ClaudeLogReader` returns one deduplicated `UsageRecord` per API response; `AccountReader` builds `AccountInfo`.
- `UsageAggregator` builds totals and the per-day, per-model, token-mix and hour-of-day figures, pricing each record with `PricingTable`.
- `LiveUsageService` fetches `/api/organizations` then `/api/organizations/{uuid}/usage` and returns a `LiveUsageResult` (never throws). `ClaudeWebSession` runs requests inside the browser via `Runtime.evaluate` and only allows `/api/...` paths.
- `App` loads `AppSettings` through `SettingsStore` (`%APPDATA%\ClaudeUsageMonitor\settings.json`) and applies changes to the hub and widget. A second launch shows the dashboard of the running instance. The app exits when both windows are closed.
- `StartupRegistration` writes the HKCU Run key using the exe path at the time it is turned on.

## Agents (`.claude\agents`)

The coordinator (the main session) delegates to five agents. Each description starts with its model.

| Agent | Model | Role |
|---|---|---|
| code-reviewer | Sonnet | Read-only review for bugs, error handling, security and privacy |
| comment-writer | Haiku | Adds XML doc comments only, never changes code |
| docs-git-manager | Sonnet | Keeps README and docs in step with code; local commits only, never pushes |
| error-checker | Sonnet, low effort | Builds the app, finds errors and warnings, suggests exact fixes; never edits |
| ui-advisor | Haiku, low effort | Reviews the WPF windows and recommends readability and usability changes; read-only |
