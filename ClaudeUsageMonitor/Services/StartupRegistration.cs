using Microsoft.Win32;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Turns "start with Windows" on or off through the current user's Run key.
/// Only HKEY_CURRENT_USER is touched, so no administrator rights are needed.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeUsageMonitor";

    /// <summary>The command-line switch that opens only the widget.</summary>
    public const string WidgetSwitch = "--widget";

    /// <summary>True when the app is registered to start with Windows.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>
    /// Registers or removes the app from the Run key. The registered path is this exe's current location,
    /// so the setting must be turned on again if the app is moved.
    /// </summary>
    /// <param name="enabled">True to start with Windows.</param>
    /// <param name="widgetOnly">True to open only the widget at sign-in.</param>
    public static void SetEnabled(bool enabled, bool widgetOnly)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The app's location is unknown.");
            key.SetValue(ValueName, $"\"{exe}\"" + (widgetOnly ? $" {WidgetSwitch}" : ""));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
