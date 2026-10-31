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
            new ResetCountdown(limit.ResetsAt, now),
            pace,
            string.Join(" · ", details));
    }

    /// <summary>
    /// The plan limits plus an "Extra usage (monthly)" gauge while extra usage is on and claude.ai reports how much is used.
    /// Limits that claude.ai does not report, such as the Opus weekly limit on some plans, are simply not in the list.
    /// </summary>
    public static IReadOnlyList<LimitUsage> GaugeLimits(LiveUsage usage)
    {
        if (usage.ExtraUsage is not { IsEnabled: true, Percent: { } percent } extra)
        {
            return usage.Limits;
        }

        // The tile writes amounts with a dollar sign, so other currencies go in the extras line instead.
        var dollars = extra.Currency is null || extra.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase);
        var tile = new LimitUsage(
            "Extra usage (monthly)",
            percent,
            null,
            null,
            UsedDollars: dollars ? extra.Used : null,
            LimitDollars: dollars ? extra.MonthlyLimit : null);
        return usage.Limits.Append(tile).ToList();
    }

    /// <summary>
    /// Describes what the gauges do not show: extra usage without a gauge, spend, and a warning when the claude.ai
    /// sign-in is a different account from Claude Code's. Returns empty when there is nothing to say.
    /// </summary>
    public static string DescribeExtras(LiveUsage usage)
    {
        var lines = new List<string>();
        if (usage.SameAccountAsClaudeCode == false)
        {
            lines.Add("The claude.ai sign-in is a different account from the one Claude Code uses, so these limits may not match Claude Code. Sign out and sign in with the Claude Code account.");
        }

        if (usage.ExtraUsage is { IsEnabled: true } extra)
        {
            var hasGauge = extra.Percent is not null;
            var foreign = extra.Currency is not null && !extra.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase);
            if (!hasGauge || foreign)
            {
                var amount = extra.Used is { } used
                    ? $" ({used:0.00}{(extra.MonthlyLimit is { } cap ? $" of {cap:0.00}" : "")} {extra.Currency})"
                    : "";
                lines.Add($"Extra usage: on{(extra.Percent is { } p ? $" · {p:0}% of the monthly limit used" : "")}{amount}" +
                          (extra.MonthlyLimit is null && extra.Used is not null ? " · no monthly limit set" : ""));
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
    /// Describes when a limit resets, such as "Resets in 2h 37m 05s, at 16:40" or "Resets in 5d 3h 20m, Thu 20:00".
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
            ? $"Resets in {FormatCountdown(left)}, {local.ToString("ddd HH:mm", CultureInfo.CurrentCulture)}"
            : $"Resets in {FormatCountdown(left)}, at {local.ToString("HH:mm", CultureInfo.CurrentCulture)}";
    }

    /// <summary>A short reset countdown for the widget, such as "resets 3h 41m 09s" or "resets 5d 7h 02m".</summary>
    public static string FormatResetShort(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is not { } reset)
        {
            return "";
        }

        var left = reset - now;
        return left <= TimeSpan.Zero ? "resetting now" : $"resets {FormatCountdown(left)}";
    }

    /// <summary>
    /// Formats time left as a ticking countdown: "4d 11h 03m" past a day, "2h 32m 15s" past an hour, else "7m 05s".
    /// </summary>
    public static string FormatCountdown(TimeSpan left)
    {
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        return left.TotalDays >= 1 ? $"{(int)left.TotalDays}d {left.Hours}h {left.Minutes:00}m"
            : left.TotalHours >= 1 ? $"{(int)left.TotalHours}h {left.Minutes:00}m {left.Seconds:00}s"
            : $"{left.Minutes}m {left.Seconds:00}s";
    }

    /// <summary>
    /// Says when tokens are available again. When a limit is used up, this is when the last used-up limit resets,
    /// because every one of them blocks use until then. Otherwise it names the next limit to reset.
    /// </summary>
    /// <param name="limits">The live plan limits, without the extra-usage gauge.</param>
    /// <param name="now">The current time.</param>
    /// <param name="extra">Extra usage, so the text can say when it carries on after a plan limit is used up.</param>
    /// <returns>The text and whether a plan limit is used up now, or null when there are no limits.</returns>
    public static (string Text, bool Blocked)? DescribeAvailability(IReadOnlyList<LimitUsage> limits, DateTimeOffset now,
        ExtraUsageInfo? extra = null)
    {
        if (limits.Count == 0)
        {
            return null;
        }

        // Rounded like the tiles, so a tile that shows "100% used" always gets the red line too.
        var usedUp = limits.Where(l => Math.Round(l.Percent) >= 100).ToList();
        if (usedUp.Count > 0)
        {
            if (usedUp.Any(l => l.ResetsAt is null))
            {
                return ("Limit reached. claude.ai did not say when it resets.", true);
            }

            // While extra usage is on and has room left, use goes on at pay-as-you-go rates instead of stopping.
            var paying = extra is { IsEnabled: true } && (extra.Percent is null || Math.Round(extra.Percent.Value) < 100);
            var until = usedUp.Max(l => l.ResetsAt!.Value);
            return until <= now
                ? ("Limit reached · resetting now", true)
                : ($"Limit reached · {(paying ? "extra usage in use; plan tokens back" : "tokens available again")} in {FormatCountdown(until - now)}, " +
                   $"at {until.ToLocalTime().ToString(until - now >= TimeSpan.FromDays(1) ? "ddd HH:mm" : "HH:mm", CultureInfo.CurrentCulture)}",
                   true);
        }

        var next = limits.Where(l => l.ResetsAt > now).MinBy(l => l.ResetsAt);
        return next is null
            ? null
            : ($"Next reset: {next.Label} in {FormatCountdown(next.ResetsAt!.Value - now)}", false);
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

    /// <summary>Builds the 14-day column bars, with today in a lighter color.</summary>
    /// <param name="days">The daily totals, oldest first.</param>
    /// <param name="withLabels">False to leave the axis labels empty, for small charts.</param>
    /// <param name="labelEvery">Label every Nth day only, for narrow charts where the labels would overlap.</param>
    /// <param name="dayFormat">The label format; "%d" (day number only) suits narrow charts.</param>
    /// <param name="todayLabel">The label for today, or null to use <paramref name="dayFormat"/>, for bars too narrow for "Today".</param>
    public static List<ColumnBar> DailyColumns(IReadOnlyList<DailyUsage> days, bool withLabels, int labelEvery = 1,
        string dayFormat = "ddd d", string? todayLabel = "Today")
    {
        var today = DateTime.Today;

        // Counted back from the last day, so the newest day (normally "Today") always keeps its label.
        bool Labelled(int index) => withLabels && (days.Count - 1 - index) % Math.Max(labelEvery, 1) == 0;

        return Columns(days.Select((d, i) => (
            Label: !Labelled(i) ? "" : d.Day == today && todayLabel is not null ? todayLabel : d.Day.ToString(dayFormat, CultureInfo.CurrentCulture),
            Value: (double)d.Totals.TotalTokens,
            Tooltip: $"{d.Day.ToString("dddd d MMMM", CultureInfo.CurrentCulture)}\n" +
                     $"{d.Totals.TotalTokens:N0} tokens · {d.Totals.Requests:N0} requests\n" +
                     $"Est. cost ${d.Totals.EstimatedCostUsd:F2}",
            Brush: d.Day == today ? Palette.Today : Palette.Accent)));
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
