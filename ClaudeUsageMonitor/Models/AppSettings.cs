namespace ClaudeUsageMonitor.Models;

/// <summary>
/// User settings saved to %APPDATA%\ClaudeUsageMonitor\settings.json.
/// Start-with-Windows is not stored here; it is read from the registry.
/// </summary>
public sealed class AppSettings
{
    /// <summary>The shortest refresh interval allowed, so claude.ai is not polled too often.</summary>
    public const int MinRefreshSeconds = 30;

    /// <summary>The longest refresh interval allowed.</summary>
    public const int MaxRefreshSeconds = 3600;

    /// <summary>How often local and live data are reloaded, in seconds.</summary>
    public int RefreshSeconds { get; set; } = 60;

    /// <summary>How opaque the widget background is, from 0 (clear) to 1 (solid). The default is 30%.</summary>
    public double WidgetOpacity { get; set; } = 0.30;

    /// <summary>How opaque the dashboard background is, from 0 to 1. The default is 20% (80% transparent).</summary>
    public double DashboardOpacity { get; set; } = 0.20;

    /// <summary>True to blur whatever is behind the widget and the dashboard, like frosted glass.</summary>
    public bool WidgetBlur { get; set; } = true;

    /// <summary>True to keep the widget above other windows.</summary>
    public bool WidgetTopmost { get; set; } = true;

    /// <summary>True when the widget was open the last time the app closed, so it opens again.</summary>
    public bool WidgetVisible { get; set; }

    /// <summary>When starting with Windows, true to open only the widget instead of the full dashboard.</summary>
    public bool StartWidgetOnly { get; set; } = true;

    /// <summary>The widget's last left position on screen, or null to use the default.</summary>
    public double? WidgetLeft { get; set; }

    /// <summary>The widget's last top position on screen, or null to use the default.</summary>
    public double? WidgetTop { get; set; }

    /// <summary>Clamps every value into its valid range, for settings read from a file that may have been edited.</summary>
    public void Normalize()
    {
        RefreshSeconds = Math.Clamp(RefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds);
        WidgetOpacity = double.IsFinite(WidgetOpacity) ? Math.Clamp(WidgetOpacity, 0, 1) : 0.30;
        DashboardOpacity = double.IsFinite(DashboardOpacity) ? Math.Clamp(DashboardOpacity, 0, 1) : 0.20;
        if (WidgetLeft is { } left && !double.IsFinite(left)) WidgetLeft = null;
        if (WidgetTop is { } top && !double.IsFinite(top)) WidgetTop = null;
    }
}
