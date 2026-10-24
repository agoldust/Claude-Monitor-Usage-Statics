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

    /// <summary>
    /// True when the app is registered to start with Windows from its current location.
    /// An entry left behind by a moved copy of the app counts as not registered.
    /// </summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string command &&
               Environment.ProcessPath is not null &&
               (command == Command(widgetOnly: true) || command == Command(widgetOnly: false));
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
            key.SetValue(ValueName, Command(widgetOnly));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>The Run-key command for this exe, such as "C:\Apps\ClaudeUsageMonitor.exe" --widget.</summary>
    private static string Command(bool widgetOnly)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The app's location is unknown.");
        return $"\"{exe}\"" + (widgetOnly ? $" {WidgetSwitch}" : "");
    }
}
