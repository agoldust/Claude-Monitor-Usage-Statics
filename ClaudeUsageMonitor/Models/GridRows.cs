namespace ClaudeUsageMonitor.Models;

/// <summary>Flat row for the daily table.</summary>
public sealed record DailyRow(DateTime Day, int Requests, long Tokens, double Cost);

/// <summary>Flat row for the per-model table.</summary>
public sealed record ModelRow(string Model, int Requests, long Tokens, double Cost);

/// <summary>Flat row for the per-project table.</summary>
public sealed record ProjectRow(string ProjectFolder, int SessionCount, long Tokens, double Cost);
