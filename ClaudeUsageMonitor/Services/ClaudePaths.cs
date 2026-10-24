using System.IO;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Well-known locations used by Claude Code on Windows.
/// </summary>
public static class ClaudePaths
{
    /// <summary>The %USERPROFILE%\.claude folder.</summary>
    public static string ClaudeHome { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <summary>The %USERPROFILE%\.claude.json file (holds the oauthAccount block).</summary>
    public static string AccountFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    /// <summary>The %USERPROFILE%\.claude\.credentials.json file. Only non-secret fields are read from it.</summary>
    public static string CredentialsFile { get; } = Path.Combine(ClaudeHome, ".credentials.json");
}
