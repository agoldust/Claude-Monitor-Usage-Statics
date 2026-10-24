using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Facts about this app for the About window: name, version, author, links, and the runtime it is running on.
/// </summary>
public static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    /// <summary>The app's name.</summary>
    public const string Name = "Claude Usage Monitor";

    /// <summary>The author's name.</summary>
    public const string Author = "Akbar Goldoust";

    /// <summary>A short public bio for the About window.</summary>
    public const string AuthorBio =
        "Software developer building Windows desktop apps with C#, .NET 8 and WPF. " +
        "This project covers Win32 interop, embedded web views (WebView2), live data dashboards and AI-assisted development with Claude Code.";

    /// <summary>The author's GitHub profile.</summary>
    public const string ProfileUrl = "https://github.com/agoldust";

    /// <summary>The author's LinkedIn profile. Empty hides the link in the About window.</summary>
    public const string LinkedInUrl = "";

    /// <summary>The source code repository.</summary>
    public const string RepositoryUrl = "https://github.com/agoldust/Claude-Monitor-Usage-Statics";

    /// <summary>The license file in the repository.</summary>
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";

    /// <summary>The page for reporting problems.</summary>
    public const string IssuesUrl = RepositoryUrl + "/issues";

    /// <summary>The license name.</summary>
    public const string License = "MIT License";

    /// <summary>The version from the project file, such as "0.2.0".</summary>
    public static string Version { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    /// <summary>The copyright line from the project file.</summary>
    public static string Copyright { get; } =
        Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? $"Copyright (c) {Author}";

    /// <summary>The one-sentence description from the project file.</summary>
    public static string Description { get; } =
        Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "";

    /// <summary>When this build was made, in local time, or null if the build did not record it.</summary>
    public static DateTimeOffset? BuildDate { get; } =
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDateUtc")?.Value is { } value &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date.ToLocalTime()
            : null;

    /// <summary>The .NET runtime, such as ".NET 8.0.21".</summary>
    public static string Runtime => RuntimeInformation.FrameworkDescription;

    /// <summary>The Windows version, such as "Microsoft Windows 10.0.19045".</summary>
    public static string OperatingSystem => RuntimeInformation.OSDescription;

    /// <summary>The installed WebView2 Runtime version, or a note when it is missing.</summary>
    public static string WebView2Version
    {
        get
        {
            try
            {
                return CoreWebView2Environment.GetAvailableBrowserVersionString() ?? "not installed";
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return "not installed";
            }
        }
    }

    /// <summary>All version facts as plain text, for pasting into a bug report.</summary>
    public static string BuildReport() => string.Join(Environment.NewLine,
        $"{Name} {Version}",
        $"Built: {BuildDate?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "unknown"}",
        $"Runtime: {Runtime}",
        $"WebView2 Runtime: {WebView2Version}",
        $"OS: {OperatingSystem} ({RuntimeInformation.OSArchitecture})");

    /// <summary>
    /// Opens one of this app's own web links in the default browser. Other addresses are refused.
    /// </summary>
    /// <param name="url">One of the link constants in this class.</param>
    public static void OpenLink(string url)
    {
        var allowed = url is ProfileUrl or RepositoryUrl or LicenseUrl or IssuesUrl ||
                      (LinkedInUrl.Length > 0 && url == LinkedInUrl);
        if (!allowed)
        {
            throw new ArgumentException("Only the app's own links can be opened.", nameof(url));
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Opens a folder in File Explorer, creating it first if needed.</summary>
    /// <param name="folder">The folder to open.</param>
    public static void OpenFolder(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);

        // Use the full path, so a file named explorer.exe next to the app can never be started instead.
        var explorer = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { folder }, UseShellExecute = false });
    }
}
