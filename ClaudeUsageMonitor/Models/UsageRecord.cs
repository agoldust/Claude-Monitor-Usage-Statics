namespace ClaudeUsageMonitor.Models;

/// <summary>
/// One billed API response from a Claude Code session log (an "assistant" line with a usage block).
/// </summary>
public sealed record UsageRecord(
    DateTimeOffset Timestamp,
    string Model,
    string SessionId,
    string ProjectFolder,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens)
{
    /// <summary>The sum of input, output, cache-write and cache-read tokens for this response.</summary>
    public long TotalTokens => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;
}
