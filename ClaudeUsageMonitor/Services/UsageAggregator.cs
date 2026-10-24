using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Turns raw usage records into totals, per-model and per-day breakdowns, and cost estimates.
/// </summary>
public static class UsageAggregator
{
    /// <summary>
    /// Builds the usage summary for the main window from a list of records.
    /// Day ranges use the local calendar, so "today" is the local date of <paramref name="now"/>.
    /// </summary>
    /// <param name="records">The usage records to aggregate.</param>
    /// <param name="now">The reference time for the "today", "last 7 days" and "last 30 days" ranges.</param>
    /// <returns>The summary with totals, per-model, per-day and per-project breakdowns.</returns>
    public static UsageSummary Build(IReadOnlyList<UsageRecord> records, DateTimeOffset now)
    {
        var localToday = now.ToLocalTime().Date;
        var weekStart = localToday.AddDays(-6);
        var monthStart = localToday.AddDays(-29);

        var local = records
            .Select(r => (Record: r, Day: r.Timestamp.ToLocalTime().Date))
            .ToList();

        return new UsageSummary
        {
            Today = Totals(local.Where(x => x.Day == localToday).Select(x => x.Record)),
            Last7Days = Totals(local.Where(x => x.Day >= weekStart).Select(x => x.Record)),
            Last30Days = Totals(local.Where(x => x.Day >= monthStart).Select(x => x.Record)),
            AllTime = Totals(records),
            ByModel = records
                .GroupBy(r => r.Model)
                .Select(g => new ModelUsage(g.Key, Totals(g)))
                .OrderByDescending(m => m.Totals.TotalTokens)
                .ToList(),
            ByDay = Enumerable.Range(0, 14)
                .Select(i => localToday.AddDays(i - 13))
                .Select(day => new DailyUsage(day, Totals(local.Where(x => x.Day == day).Select(x => x.Record))))
                .ToList(),
            ByProject = records
                .GroupBy(r => r.ProjectFolder)
                .Select(g => new ProjectUsage(g.Key, g.Select(r => r.SessionId).Distinct().Count(), Totals(g)))
                .OrderByDescending(p => p.Totals.TotalTokens)
                .Take(10)
                .ToList(),
            ByHour = Enumerable.Range(0, 24)
                .Select(hour => Totals(local
                    .Where(x => x.Day >= monthStart && x.Record.Timestamp.ToLocalTime().Hour == hour)
                    .Select(x => x.Record)))
                .ToList(),
            SessionCount = records.Select(r => r.SessionId).Distinct().Count(),
            LastActivity = records.Count == 0 ? null : records.Max(r => r.Timestamp)
        };
    }

    /// <summary>
    /// Adds up the request count, token counts and estimated cost of a set of records.
    /// </summary>
    /// <param name="records">The records to add up.</param>
    /// <returns>The totals. All values are zero when the set is empty.</returns>
    public static UsageTotals Totals(IEnumerable<UsageRecord> records)
    {
        var list = records as IList<UsageRecord> ?? records.ToList();
        var totals = new UsageTotals
        {
            Requests = list.Count,
            InputTokens = list.Sum(r => r.InputTokens),
            OutputTokens = list.Sum(r => r.OutputTokens),
            CacheCreationTokens = list.Sum(r => r.CacheCreationTokens),
            CacheReadTokens = list.Sum(r => r.CacheReadTokens)
        };

        totals.EstimatedCostUsd = list.Sum(r => PricingTable.Estimate(r));
        return totals;
    }
}
