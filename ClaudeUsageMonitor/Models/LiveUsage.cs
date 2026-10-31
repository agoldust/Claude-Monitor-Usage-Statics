namespace ClaudeUsageMonitor.Models;

/// <summary>
/// One plan limit from claude.ai, such as the current 5-hour session or the weekly limit.
/// </summary>
/// <param name="Label">A readable name for the limit, such as "Current session (5-hour)".</param>
/// <param name="Percent">How much of the limit is used, from 0 to 100.</param>
/// <param name="ResetsAt">When the limit resets, or null when claude.ai does not say.</param>
/// <param name="Severity">The severity claude.ai reports, such as "normal", or null.</param>
/// <param name="IsActive">True when claude.ai marks this as the limit that currently applies; null when not reported.</param>
/// <param name="Window">The length of the limit's time window (5 hours or 7 days), or null when unknown.</param>
/// <param name="UsedDollars">Dollar amount used against this limit, when claude.ai reports one.</param>
/// <param name="LimitDollars">Dollar size of this limit, when claude.ai reports one.</param>
/// <param name="LockedReason">Why the limit is locked, when it is.</param>
public sealed record LimitUsage(
    string Label,
    double Percent,
    DateTimeOffset? ResetsAt,
    string? Severity,
    bool? IsActive = null,
    TimeSpan? Window = null,
    decimal? UsedDollars = null,
    decimal? LimitDollars = null,
    string? LockedReason = null)
{
    /// <summary>
    /// How much of the time window has passed, from 0 to 100, or null when the window or reset time is unknown.
    /// </summary>
    /// <param name="now">The current time.</param>
    public double? ElapsedPercent(DateTimeOffset now)
    {
        if (Window is not { } window || ResetsAt is not { } reset || window <= TimeSpan.Zero)
        {
            return null;
        }

        var elapsed = window - (reset - now);
        return Math.Clamp(elapsed.TotalSeconds / window.TotalSeconds * 100, 0, 100);
    }
}

/// <summary>
/// Pay-as-you-go extra usage settings for the account.
/// </summary>
/// <param name="IsEnabled">True when extra usage is turned on.</param>
/// <param name="Percent">How much of the monthly extra-usage limit is used, from 0 to 100, or null.</param>
/// <param name="Used">The amount used this month in <paramref name="Currency"/>, or null.</param>
/// <param name="MonthlyLimit">The monthly limit in <paramref name="Currency"/>, or null when there is none.</param>
/// <param name="Currency">The currency code, such as "USD", or null.</param>
/// <param name="DisabledReason">Why extra usage is off, when claude.ai says.</param>
public sealed record ExtraUsageInfo(bool IsEnabled, double? Percent, decimal? Used, decimal? MonthlyLimit, string? Currency, string? DisabledReason);

/// <summary>
/// Spending reported by claude.ai for the current period.
/// </summary>
/// <param name="Used">The amount spent.</param>
/// <param name="Limit">The spend limit, or null when there is none.</param>
/// <param name="Currency">The currency code, such as "USD".</param>
/// <param name="Percent">How much of the limit is spent, from 0 to 100, or null.</param>
public sealed record SpendInfo(decimal Used, decimal? Limit, string Currency, double? Percent);

/// <summary>
/// Live plan usage fetched from claude.ai for the signed-in account.
/// </summary>
/// <param name="Limits">The plan limits that claude.ai reports.</param>
/// <param name="OrganizationName">The name of the organization the usage belongs to, or null.</param>
/// <param name="FetchedAt">When the data was fetched.</param>
/// <param name="ExtraUsage">Extra usage settings, or null when not reported.</param>
/// <param name="Spend">Spend for the current period, or null when not reported.</param>
/// <param name="SameAccountAsClaudeCode">
/// True when the claude.ai sign-in includes the organization Claude Code uses, false when it does not
/// (a different account is signed in), or null when Claude Code's organization is unknown.
/// </param>
public sealed record LiveUsage(
    IReadOnlyList<LimitUsage> Limits,
    string? OrganizationName,
    DateTimeOffset FetchedAt,
    ExtraUsageInfo? ExtraUsage = null,
    SpendInfo? Spend = null,
    bool? SameAccountAsClaudeCode = null);

/// <summary>
/// The outcome of one attempt to fetch live usage.
/// </summary>
public enum LiveUsageStatus
{
    /// <summary>Live usage was fetched.</summary>
    Ok,

    /// <summary>No claude.ai session exists, or it has expired. The user needs to sign in.</summary>
    SignedOut,

    /// <summary>The Microsoft Edge WebView2 Runtime is not installed.</summary>
    RuntimeMissing,

    /// <summary>The request failed for another reason. The message says why.</summary>
    Error
}

/// <summary>
/// The result of fetching live usage: a status, the data when the fetch worked, and a message otherwise.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="Usage">The usage data when <paramref name="Status"/> is <see cref="LiveUsageStatus.Ok"/>; otherwise null.</param>
/// <param name="Message">A short explanation for the user when the fetch did not work.</param>
public sealed record LiveUsageResult(LiveUsageStatus Status, LiveUsage? Usage = null, string? Message = null);
