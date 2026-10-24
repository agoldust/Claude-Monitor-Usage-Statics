# Where the numbers come from

All data is read locally. Nothing is sent over the network.

| Data | Source |
|---|---|
| Tokens, requests, models, projects, sessions | `%USERPROFILE%\.claude\projects\**\*.jsonl` |
| Email, name, organization, billing type, rate limit tiers, account and subscription dates, extra usage flag | `oauthAccount` block in `%USERPROFILE%\.claude.json` |
| Plan, plan source, rate limit tier, login expiry | `%USERPROFILE%\.claude\.credentials.json` |

## Credentials file

Only `subscriptionType`, `subscriptionTypeSource`, `rateLimitTier` and `expiresAt` are read, from the `claudeAiOauth` block. Tokens are never read or copied. If a user rate limit tier is stored in `.claude.json`, the credentials file value takes precedence.

## Deduplication

Claude Code writes one API response as several log lines (one per content block), each repeating the usage block. Lines that share the same message id and requestId are counted once; the last line wins because it carries the final output token count. Lines with no message id are counted individually. Malformed lines and unreadable files are skipped.

## Cost estimates

Cost is a display estimate at list prices, not a bill. Rates per million tokens are in `ClaudeUsageMonitor\Services\PricingTable.cs`: Opus 15 / 75, Haiku 0.80 / 4, any other model (including Sonnet) 3 / 15 (input / output). Cache writes are 1.25 times the input rate and cache reads 0.10 times.

The prices are not yet verified against Anthropic's pricing page.

## Unconfirmed

- The meaning of `AccountInfo.SubscriptionTypeSource` is not confirmed.
- The meaning of `OrganizationType` is inferred from its name.
