using System.IO;
using System.Text.Json;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in %APPDATA%\ClaudeUsageMonitor\settings.json.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The full path of the settings file.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageMonitor", "settings.json");

    /// <summary>
    /// Reads the settings file. A missing or unreadable file gives the defaults.
    /// </summary>
    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            DebugLog.Write($"Settings: could not read, using defaults ({ex.GetType().Name})");
            settings = new AppSettings();
        }

        settings.Normalize();
        return settings;
    }

    /// <summary>
    /// Writes the settings file. Failures are logged and otherwise ignored, so a read-only disk never stops the app.
    /// </summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Write($"Settings: could not save ({ex.GetType().Name})");
        }
    }
}
