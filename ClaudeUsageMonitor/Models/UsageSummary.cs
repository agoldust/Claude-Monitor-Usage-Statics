namespace ClaudeUsageMonitor.Models;

/// <summary>
/// Token counts, request count and estimated cost for a set of usage records.
/// </summary>
public sealed class UsageTotals
{
    /// <summary>The number of API responses in the set.</summary>
    public int Requests { get; set; }

    /// <summary>The total input tokens.</summary>
    public long InputTokens { get; set; }

    /// <summary>The total output tokens.</summary>
    public long OutputTokens { get; set; }

    /// <summary>The total tokens written to the prompt cache.</summary>
    public long CacheCreationTokens { get; set; }

    /// <summary>The total tokens read from the prompt cache.</summary>
    public long CacheReadTokens { get; set; }

    /// <summary>The estimated list-price cost in US dollars. This is a display estimate, not a bill.</summary>
    public double EstimatedCostUsd { get; set; }

    /// <summary>The sum of input, output, cache-write and cache-read tokens.</summary>
    public long TotalTokens => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;
}

/// <summary>
/// The usage totals for one model.
/// </summary>
/// <param name="Model">The model name.</param>
/// <param name="Totals">The totals for that model.</param>
public sealed record ModelUsage(string Model, UsageTotals Totals);

/// <summary>
/// The usage totals for one local calendar day.
/// </summary>
/// <param name="Day">The local date.</param>
/// <param name="Totals">The totals for that day.</param>
public sealed record DailyUsage(DateTime Day, UsageTotals Totals);

/// <summary>
/// The usage totals for one project folder.
/// </summary>
/// <param name="ProjectFolder">The project folder name under the Claude Code projects folder.</param>
/// <param name="SessionCount">The number of distinct sessions in the project.</param>
/// <param name="Totals">The totals for that project.</param>
public sealed record ProjectUsage(string ProjectFolder, int SessionCount, UsageTotals Totals);

/// <summary>
/// The usage figures shown in the main window, built by <see cref="Services.UsageAggregator"/>.
/// </summary>
public sealed class UsageSummary
{
    /// <summary>The totals for the current local day.</summary>
    public UsageTotals Today { get; init; } = new();

    /// <summary>The totals for the last seven local days, including today.</summary>
    public UsageTotals Last7Days { get; init; } = new();

    /// <summary>The totals for the last thirty local days, including today.</summary>
    public UsageTotals Last30Days { get; init; } = new();

    /// <summary>The totals for all records.</summary>
    public UsageTotals AllTime { get; init; } = new();

    /// <summary>The totals for each model, sorted by total tokens in descending order.</summary>
    public IReadOnlyList<ModelUsage> ByModel { get; init; } = Array.Empty<ModelUsage>();

    /// <summary>The totals for each of the last 14 local days, oldest first.</summary>
    public IReadOnlyList<DailyUsage> ByDay { get; init; } = Array.Empty<DailyUsage>();

    /// <summary>The ten project folders with the most tokens, sorted by total tokens in descending order.</summary>
    public IReadOnlyList<ProjectUsage> ByProject { get; init; } = Array.Empty<ProjectUsage>();

    /// <summary>The totals for each local hour of the day (index 0 is midnight) over the last 30 days.</summary>
    public IReadOnlyList<UsageTotals> ByHour { get; init; } = Array.Empty<UsageTotals>();

    /// <summary>The number of distinct sessions across all records.</summary>
    public int SessionCount { get; init; }

    /// <summary>The time of the latest record, or null when there are no records.</summary>
    public DateTimeOffset? LastActivity { get; init; }
}
