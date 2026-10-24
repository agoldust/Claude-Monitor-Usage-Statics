# Architecture

## Layout

```
ClaudeUsageMonitor\
  App.xaml(.cs)          application entry
  MainWindow.xaml(.cs)   the only window
  Models\                AccountInfo, UsageRecord, UsageSummary, GridRows
  Services\              ClaudePaths, ClaudeLogReader, AccountReader,
                         PricingTable, UsageAggregator
.claude\agents\          project subagents
docs\                    documentation
```

## Data flow

```
*.jsonl logs  -> ClaudeLogReader -> UsageRecord list --\
                                                        > UsageAggregator -> UsageSummary -> MainWindow
.claude.json + .credentials.json -> AccountReader -> AccountInfo -------------------------> MainWindow
```

- `ClaudeLogReader` scans the logs and returns one deduplicated `UsageRecord` per API response.
- `AccountReader` builds `AccountInfo` from the account and credentials files.
- `UsageAggregator` groups records into totals, per-model, per-day (14 days) and per-project (top 10) figures, pricing each record with `PricingTable`.
- `MainWindow` displays the summary and account info.

## Agents (`.claude\agents`)

| Agent | Model | Role |
|---|---|---|
| code-reviewer | Sonnet | Read-only review for bugs, error handling, security and privacy |
| docs-git-manager | Sonnet | Keeps README and docs in step with code; local commits only, never pushes |
| comment-writer | Haiku | Adds XML doc comments only, never changes code |
