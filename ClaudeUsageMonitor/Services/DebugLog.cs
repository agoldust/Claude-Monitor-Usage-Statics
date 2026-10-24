using System.IO;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// A small diagnostic log at %LOCALAPPDATA%\ClaudeUsageMonitor\logs\app.log.
/// Callers must never pass secrets, cookies, query strings, or response bodies that hold account data.
/// </summary>
public static class DebugLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    /// <summary>The full path of the log file.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsageMonitor", "logs", "app.log");

    /// <summary>
    /// Appends one timestamped line. Logging failures are ignored so they never affect the app.
    /// </summary>
    /// <param name="message">The text to log. Must not contain secrets.</param>
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Delete(FilePath);
                }

                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging is best effort.
        }
    }

    /// <summary>
    /// Returns the scheme, host and path of an address without its query string or fragment,
    /// because sign-in links can carry one-time codes.
    /// </summary>
    /// <param name="address">The address to shorten.</param>
    public static string SafeUrl(string? address)
    {
        return Uri.TryCreate(address, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}"
            : "(invalid url)";
    }

    /// <summary>
    /// Describes a response body without logging its content: JSON or HTML, its length,
    /// and for JSON errors the "type" field only.
    /// </summary>
    /// <param name="body">The response body.</param>
    public static string DescribeBody(string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('<'))
        {
            var challenge = body.Contains("challenge", StringComparison.OrdinalIgnoreCase) ||
                            body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase);
            return $"html, {body.Length} chars{(challenge ? ", looks like a Cloudflare challenge" : "")}";
        }

        var errorType = System.Text.RegularExpressions.Regex.Match(body, "\"type\"\\s*:\\s*\"([a-z_]{1,40})\"");
        return $"json, {body.Length} chars{(errorType.Success ? $", type={errorType.Groups[1].Value}" : "")}";
    }
}
