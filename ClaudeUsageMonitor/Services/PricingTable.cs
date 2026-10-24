using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Approximate list prices in USD per million tokens. These are estimates for display only.
/// Edit this table when Anthropic prices change. The rates are not verified against the live pricing page yet.
/// </summary>
public static class PricingTable
{
    /// <summary>The cost of a cache write as a multiple of the input rate.</summary>
    private const double CacheWriteMultiplier = 1.25;

    /// <summary>The cost of a cache read as a multiple of the input rate.</summary>
    private const double CacheReadMultiplier = 0.10;

    /// <summary>
    /// Estimates the list-price cost of one API response in US dollars.
    /// Cache writes cost 1.25 times the input rate, and cache reads cost 0.10 times.
    /// </summary>
    /// <param name="record">The usage record to price.</param>
    /// <returns>The estimated cost in US dollars. This is a display estimate, not a bill.</returns>
    public static double Estimate(UsageRecord record)
    {
        var (inputRate, outputRate) = RatesFor(record.Model);
        const double perMillion = 1_000_000.0;

        return record.InputTokens * inputRate / perMillion
             + record.OutputTokens * outputRate / perMillion
             + record.CacheCreationTokens * inputRate * CacheWriteMultiplier / perMillion
             + record.CacheReadTokens * inputRate * CacheReadMultiplier / perMillion;
    }

    /// <summary>
    /// Returns the input and output rates for a model name. Sonnet is the default for unknown models.
    /// </summary>
    private static (double Input, double Output) RatesFor(string model)
    {
        var name = model.ToLowerInvariant();
        if (name.Contains("opus")) return (15.0, 75.0);
        if (name.Contains("haiku")) return (0.80, 4.0);
        return (3.0, 15.0);
    }
}
