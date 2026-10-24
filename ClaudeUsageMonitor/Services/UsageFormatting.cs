using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ClaudeUsageMonitor.Controls;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Formatting and chart-row building shared by the dashboard and the widget.
/// </summary>
public static class UsageFormatting
{
    /// <summary>
    /// Builds one gauge tile: usage, reset time, pace against the time window, and any extra fields.
    /// The ring goes amber at 70% used or when on track to use it all, and red at 90% used,
    /// or when well past pace (projected 120% or more) with at least 40% already used.
    /// </summary>
    public static LimitRow ToLimitRow(LimitUsage limit, DateTimeOffset now)
    {
        var elapsed = limit.ElapsedPercent(now);

        // Only project once enough of the window has passed for the rate to mean something.
        double? projected = elapsed is >= 3 ? limit.Percent / elapsed.Value * 100 : null;

        var ring = limit.Percent >= 90 || (projected >= 120 && limit.Percent >= 40) ? Palette.Critical
            : limit.Percent >= 70 || projected >= 100 ? Palette.Warning
            : Palette.Accent;

        var pace = "";
        if (elapsed is { } e)
        {
            // Usage comes in bursts, so a few percent either side of the window still counts as on pace.
            var verdict = projected >= 105 ? "Ahead of pace: may run out before it resets"
                : limit.Percent < e - 5 ? "Under pace"
                : "On pace";
            pace = $"{e:0}% of the window has passed. {verdict}.";
            if (projected is { } p)
            {
                pace += $" At this rate: about {Math.Min(p, 999):0}% by reset.";
            }
        }

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(limit.Severity) && !limit.Severity.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            details.Add($"Status: {limit.Severity}");
        }

        if (limit.UsedDollars is not null || limit.LimitDollars is not null)
        {
            details.Add($"${limit.UsedDollars ?? 0:0.00} used" + (limit.LimitDollars is { } cap ? $" of ${cap:0.00}" : ""));
        }

        if (!string.IsNullOrWhiteSpace(limit.LockedReason))
        {
            details.Add($"Locked: {limit.LockedReason}");
        }

        return new LimitRow(
            limit.Label,
            ShortLabel(limit.Label),
            limit.Percent,
            elapsed ?? double.NaN,
            ring,
            $"{limit.Percent:0}% used · {100 - limit.Percent:0}% left",
            FormatReset(limit.ResetsAt, now),
            FormatResetShort(limit.ResetsAt, now),
            pace,
            string.Join(" · ", details));
    }

    /// <summary>Describes extra usage and spend in one or two lines, or returns empty when neither is reported.</summary>
    public static string DescribeExtras(LiveUsage usage)
    {
        var lines = new List<string>();
        if (usage.ExtraUsage is { } extra)
        {
            if (extra.IsEnabled)
            {
                var amount = extra.Used is { } used
                    ? $" ({used:0.00}{(extra.MonthlyLimit is { } cap ? $" of {cap:0.00}" : "")} {extra.Currency})"
                    : "";
                lines.Add($"Extra usage: on{(extra.Percent is { } p ? $" · {p:0}% of the monthly limit used" : "")}{amount}");
            }
            else
            {
                lines.Add($"Extra usage: off{(string.IsNullOrWhiteSpace(extra.DisabledReason) ? "" : $" ({extra.DisabledReason})")}");
            }
        }

        if (usage.Spend is { } spend)
        {
            lines.Add($"Spend reported by claude.ai: {spend.Used:0.00} {spend.Currency}" +
                      (spend.Limit is { } limit ? $" of {limit:0.00}" : " (no spend limit set)"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Describes when a limit resets, such as "Resets in 2h 37m, at 16:40" or "Resets in 5d 3h, Thu 20:00".
    /// </summary>
    public static string FormatReset(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is not { } reset)
        {
            return "No reset time reported";
        }

        var left = reset - now;
        var local = reset.ToLocalTime();
        if (left <= TimeSpan.Zero)
        {
            return "Resetting now";
        }

        return left.TotalDays >= 1
            ? $"Resets in {(int)left.TotalDays}d {left.Hours}h, {local.ToString("ddd HH:mm", CultureInfo.CurrentCulture)}"
            : $"Resets in {(int)left.TotalHours}h {left.Minutes}m, at {local.ToString("HH:mm", CultureInfo.CurrentCulture)}";
    }

    /// <summary>A short reset countdown for the widget, such as "resets 3h 41m" or "resets 5d 7h".</summary>
    public static string FormatResetShort(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is not { } reset)
        {
            return "";
        }

        var left = reset - now;
        if (left <= TimeSpan.Zero)
        {
            return "resetting now";
        }

        return left.TotalDays >= 1
            ? $"resets {(int)left.TotalDays}d {left.Hours}h"
            : $"resets {(int)left.TotalHours}h {left.Minutes}m";
    }

    /// <summary>Shortens a limit name for the widget, such as "Current session (5-hour)" to "Session".</summary>
    public static string ShortLabel(string label) => label switch
    {
        "Current session (5-hour)" => "Session",
        "Weekly (all models)" => "Weekly",
        "Weekly (Opus)" => "Opus week",
        "Weekly (Sonnet)" => "Sonnet week",
        "Extra usage (monthly)" => "Extra",
        _ => label
    };

    /// <summary>Scales values to the largest one and returns chart bars.</summary>
    public static List<ColumnBar> Columns(IEnumerable<(string Label, double Value, string Tooltip, Brush Brush)> items)
    {
        var list = items.ToList();
        var max = list.Count == 0 ? 0 : list.Max(i => i.Value);
        return list.Select(i =>
        {
            var fraction = max > 0 ? i.Value / max : 0;
            return new ColumnBar(
                i.Label,
                new GridLength(fraction, GridUnitType.Star),
                new GridLength(1 - fraction, GridUnitType.Star),
                i.Brush,
                i.Tooltip);
        }).ToList();
    }

    /// <summary>Builds the 14-day column bars, with today in a darker color.</summary>
    /// <param name="days">The daily totals, oldest first.</param>
    /// <param name="withLabels">False to leave the axis labels empty, for small charts.</param>
    public static List<ColumnBar> DailyColumns(IReadOnlyList<DailyUsage> days, bool withLabels)
    {
        var today = DateTime.Today;
        return Columns(days.Select(d => (
            Label: !withLabels ? "" : d.Day == today ? "Today" : d.Day.ToString("ddd d", CultureInfo.CurrentCulture),
            Value: (double)d.Totals.TotalTokens,
            Tooltip: $"{d.Day.ToString("dddd d MMMM", CultureInfo.CurrentCulture)}\n" +
                     $"{d.Totals.TotalTokens:N0} tokens · {d.Totals.Requests:N0} requests\n" +
                     $"Est. cost ${d.Totals.EstimatedCostUsd:F2}",
            Brush: d.Day == today ? Palette.AccentDark : Palette.Accent)));
    }

    /// <summary>Turns "claude-opus-5-5" into "Opus 5.5". Unknown formats are returned unchanged.</summary>
    public static string ShortModelName(string model)
    {
        var parts = model.Split('-');
        if (parts.Length >= 3 && parts[0] == "claude")
        {
            var family = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[1]);
            var version = string.Join(".", parts.Skip(2).TakeWhile(p => p.All(char.IsDigit) && p.Length <= 2));
            return version.Length > 0 ? $"{family} {version}" : family;
        }

        return model;
    }

    /// <summary>
    /// Formats a token count with a B, M or K suffix for values of one thousand or more.
    /// </summary>
    public static string Compact(long value)
    {
        return value switch
        {
            >= 1_000_000_000 => $"{value / 1_000_000_000.0:F2}B",
            >= 1_000_000 => $"{value / 1_000_000.0:F2}M",
            >= 1_000 => $"{value / 1_000.0:F1}K",
            _ => value.ToString("N0", CultureInfo.CurrentCulture)
        };
    }
}
