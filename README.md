# Claude Usage Monitor

A Windows desktop app (WPF) that shows your Claude Code usage and, after you sign in to claude.ai, your live plan limits. It has a graphical dashboard and a small always-on-top glass widget.

## Requirements

- Windows 10 version 1809 or later
- .NET 8 SDK (to build)
- Microsoft Edge WebView2 Runtime (for sign-in and live limits; present on current Windows 10/11)
- Claude Code installed and used at least once, so its local files exist

## Build and run

```
dotnet build ClaudeUsageMonitor\ClaudeUsageMonitor.csproj -c Release
dotnet run --project ClaudeUsageMonitor\ClaudeUsageMonitor.csproj -c Release
```

Start only the widget with `--widget`.

## Features

- **Live plan limits.** Sign in to claude.ai from the app; limits are fetched from claude.ai's internal web endpoints (not a public API, so they may change without notice). Shown as ring gauges with a pace marker.
- **Graphical dashboard.** Ring gauges, a 14-day bar chart, model share, token mix, hour-of-day chart, and collapsible account details and tables. Press F5 to refresh.
- **Widget.** Always-on-top, translucent window with optional blur. Opacity defaults to 30%.
- **Settings.** Refresh interval (30 seconds to 1 hour), widget opacity, blur, always on top, start with Windows.
- **Start with Windows.** Registers the exe in the current user's Run key. The path is the exe's location at the time you turn it on; turn it off and on again if you move the app. Optionally opens only the widget.
- **Single instance.** Starting the app again shows the dashboard. The app exits when both windows are closed.
- **Local usage.** Tokens, requests and estimated cost for today, 7 days, 30 days and all time, from Claude Code logs.
- **Debug log.** `%LOCALAPPDATA%\ClaudeUsageMonitor\logs\app.log`.

More: [USAGE_DATA.md](docs/USAGE_DATA.md), [ARCHITECTURE.md](docs/ARCHITECTURE.md), [ROADMAP.md](docs/ROADMAP.md).
