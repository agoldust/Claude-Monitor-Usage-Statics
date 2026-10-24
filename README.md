# Claude Usage Monitor

A small Windows desktop app (WPF) that shows your local Claude Code usage and account details: tokens, requests and estimated cost for today, the last 7 and 30 days and all time, plus breakdowns by day, model and project.

## Requirements

- Windows 10 version 1809 or later
- .NET 8 SDK (to build)
- Claude Code installed and used at least once, so its local files exist

## Build and run

```
dotnet build ClaudeUsageMonitor\ClaudeUsageMonitor.csproj -c Release
dotnet run --project ClaudeUsageMonitor\ClaudeUsageMonitor.csproj -c Release
```

## Current features

- Account and plan details read from local Claude Code files.
- Usage totals (today, 7 days, 30 days, all time).
- Last 14 days, by model (all time) and top projects (all time) tables.
- Estimated cost from list prices.

Only local Claude Code usage is shown. There is no sign-in yet, so usage from claude.ai or other apps, and live plan limits, are not available.

More: [USAGE_DATA.md](docs/USAGE_DATA.md), [ARCHITECTURE.md](docs/ARCHITECTURE.md), [ROADMAP.md](docs/ROADMAP.md).
