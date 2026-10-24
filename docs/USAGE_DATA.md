# Where the numbers come from

## Local data (always)

Read from disk. Nothing is sent over the network for these.

| Data | Source |
|---|---|
| Tokens, requests, models, projects, sessions | `%USERPROFILE%\.claude\projects\**\*.jsonl` |
| Email, name, organization, billing type, rate limit tiers, account and subscription dates, extra usage flag | `oauthAccount` block in `%USERPROFILE%\.claude.json` |
| Plan, plan source, rate limit tier, login expiry | `%USERPROFILE%\.claude\.credentials.json` |

### Credentials file

Only `subscriptionType`, `subscriptionTypeSource`, `rateLimitTier` and `expiresAt` are read, from the `claudeAiOauth` block. Tokens are never read or copied. If a user rate limit tier is stored in `.claude.json`, the credentials file value takes precedence.

### Deduplication

Claude Code writes one API response as several log lines (one per content block), each repeating the usage block. Lines that share the same message id and requestId are counted once; the last line wins because it carries the final output token count. Lines with no message id are counted individually. Malformed lines and unreadable files are skipped.

## Live limits (after sign-in)

If you sign in, the app also contacts claude.ai. These are claude.ai's internal web endpoints, not a public API, and can change or break at any time.

| Request | Purpose |
|---|---|
| `GET /api/organizations` | Find your organization (the one Claude Code uses if known, else one with chat access) |
| `GET /api/organizations/{uuid}/usage` | Plan limits, percent used, reset times, extra usage and spend |

How it works:

- Sign-in happens in a WebView2 window (`LoginWindow`) on claude.ai itself. The app never sees your password.
- A hidden WebView2 (`ClaudeWebSession`) makes the requests inside the browser, so the browser's own session is used. Only paths matching `/api/...` are allowed.
- The browser profile (cookies) is stored by WebView2 in `%LOCALAPPDATA%\ClaudeUsageMonitor\WebView2`. Delete that folder to sign out.

### What is stored

- Stored: the WebView2 session folder above, and `%APPDATA%\ClaudeUsageMonitor\settings.json` (refresh interval, widget opacity, blur, always on top, widget position and visibility).
- Not stored: live limit values are kept in memory only; the app does not write them to disk. Cookies and tokens are never read by app code or copied.

## Debug log

`%LOCALAPPDATA%\ClaudeUsageMonitor\logs\app.log` (capped at about 512 KB, then restarted). It records status codes, request paths and field names only. It never holds cookies, query strings or values from responses.

## Cost estimates

Cost is a display estimate at list prices, not a bill. Rates per million tokens are in `ClaudeUsageMonitor\Services\PricingTable.cs`: Opus 15 / 75, Haiku 0.80 / 4, any other model (including Sonnet) 3 / 15 (input / output). Cache writes are 1.25 times the input rate and cache reads 0.10 times.

The prices are not yet verified against Anthropic's pricing page.

## Unconfirmed

- The meaning of `AccountInfo.SubscriptionTypeSource` is not confirmed.
- The meaning of `OrganizationType` is inferred from its name.
