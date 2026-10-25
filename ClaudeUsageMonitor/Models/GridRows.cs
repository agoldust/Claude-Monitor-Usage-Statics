using System.Windows;
using System.Windows.Media;

namespace ClaudeUsageMonitor.Models;

/// <summary>Flat row for the daily table.</summary>
public sealed record DailyRow(DateTime Day, int Requests, long Tokens, double Cost);

/// <summary>Flat row for the per-model table.</summary>
public sealed record ModelRow(string Model, int Requests, long Tokens, double Cost);

/// <summary>Flat row for the per-project table.</summary>
public sealed record ProjectRow(string ProjectFolder, int SessionCount, long Tokens, double Cost);

/// <summary>One live limit tile with a ring gauge.</summary>
/// <param name="Label">The limit name, such as "Current session (5-hour)".</param>
/// <param name="ShortLabel">A short name for the widget, such as "Session".</param>
/// <param name="Percent">Usage from 0 to 100, for the outer ring.</param>
/// <param name="ElapsedPercent">Time elapsed in the window from 0 to 100, for the inner ring; NaN hides it.</param>
/// <param name="RingBrush">The outer ring color, which reflects how close the limit is.</param>
/// <param name="UsageText">Used and remaining percent.</param>
/// <param name="Reset">The live countdown to the reset, updated every second by the window that shows it.</param>
/// <param name="PaceText">Elapsed time, pace and projection, or empty.</param>
/// <param name="Detail">Severity, dollars and lock reason, or empty.</param>
public sealed record LimitRow(
    string Label,
    string ShortLabel,
    double Percent,
    double ElapsedPercent,
    Brush RingBrush,
    string UsageText,
    ResetCountdown Reset,
    string PaceText,
    string Detail);

/// <summary>One vertical bar in a column chart. The bar height is the ratio of <see cref="Filled"/> to <see cref="Empty"/>.</summary>
/// <param name="Label">The text under the bar; may be empty to keep the axis uncluttered.</param>
/// <param name="Filled">The star height of the bar.</param>
/// <param name="Empty">The star height of the space above the bar.</param>
/// <param name="Brush">The bar color.</param>
/// <param name="Tooltip">The text shown on hover.</param>
public sealed record ColumnBar(string Label, GridLength Filled, GridLength Empty, Brush Brush, string Tooltip);

/// <summary>One horizontal bar with a label and a value, used for shares such as tokens per model.</summary>
/// <param name="Label">The name on the left.</param>
/// <param name="Filled">The star width of the bar.</param>
/// <param name="Empty">The star width of the space after the bar.</param>
/// <param name="Brush">The bar color.</param>
/// <param name="ValueText">The text on the right.</param>
/// <param name="Tooltip">The text shown on hover.</param>
public sealed record ShareBar(string Label, GridLength Filled, GridLength Empty, Brush Brush, string ValueText, string Tooltip);

/// <summary>One legend entry: a color swatch and text.</summary>
public sealed record LegendItem(Brush Brush, string Text);
