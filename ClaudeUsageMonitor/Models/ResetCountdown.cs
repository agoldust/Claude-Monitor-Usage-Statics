using System.ComponentModel;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor.Models;

/// <summary>
/// A countdown to when a limit resets, for binding in a limit tile. The window calls <see cref="Update"/> every
/// second, so only the countdown text changes and the tile itself (ring, tooltips) is not rebuilt.
/// </summary>
public sealed class ResetCountdown : INotifyPropertyChanged
{
    /// <summary>Creates the countdown and fills its text for the given time.</summary>
    /// <param name="resetsAt">When the limit resets, or null when claude.ai did not say.</param>
    /// <param name="now">The current time.</param>
    public ResetCountdown(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        ResetsAt = resetsAt;
        Update(now);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>When the limit resets, or null when unknown.</summary>
    public DateTimeOffset? ResetsAt { get; }

    /// <summary>The full text for the dashboard, such as "Resets in 2h 32m 15s, at 12:10".</summary>
    public string Text { get; private set; } = "";

    /// <summary>The short text for the widget, such as "resets 2h 32m 15s".</summary>
    public string Short { get; private set; } = "";

    /// <summary>Recomputes the text for the given time and notifies bindings when it changed.</summary>
    /// <param name="now">The current time.</param>
    public void Update(DateTimeOffset now)
    {
        var text = UsageFormatting.FormatReset(ResetsAt, now);
        if (text != Text)
        {
            Text = text;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }

        var shortText = UsageFormatting.FormatResetShort(ResetsAt, now);
        if (shortText != Short)
        {
            Short = shortText;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Short)));
        }
    }
}
