# Roadmap

## Done

- Read local Claude Code logs with deduplication
- Read account and plan details from local files (no tokens)
- Totals, per-day, per-model and per-project views
- Estimated cost from a price table
- Sign-in to claude.ai (WebView2)
- Live usage limits from claude.ai
- Graphical dashboard (ring gauges, charts, collapsible tables)
- Always-on-top glass widget with settings
- Start with Windows
- Debug log
- Notification-area (tray) icon; app keeps running when windows close
- Responsive dashboard layout (Compact, Normal, Wide, Extra-wide; window minimum 360 px)
- Reset countdown on each live limit, a headline for the next reset, and a refresh 5 s after a reset (0.4.0)
- Timeouts, browser restart and plain-language errors for live limits
- Windows app identity `de.goldoust.claudeusagemonitor` for the taskbar, notifications and installer shortcuts (0.4.1)
- Product page with installer download on goldoust.de, in English and German
- Coordinator agent (`claude --agent coordinator`)
- Five project subagents: review, comments, docs/git, error checking, UI advice

## Open

- Verify prices in `PricingTable.cs` against Anthropic's pricing page
- Confirm the meaning of `SubscriptionTypeSource` and `OrganizationType`
- Live limits rely on claude.ai internal endpoints that may change
