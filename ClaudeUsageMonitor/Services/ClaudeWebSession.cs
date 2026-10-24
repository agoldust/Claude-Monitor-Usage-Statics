using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// A hidden WebView2 browser that shares its sign-in cookies with <see cref="LoginWindow"/>.
/// Requests run inside the browser page, so the session cookie never leaves WebView2 and is never read by this app.
/// All members must be called on the UI thread.
/// </summary>
public sealed class ClaudeWebSession : IDisposable
{
    /// <summary>The claude.ai origin that every request goes to.</summary>
    public static readonly Uri BaseUri = new("https://claude.ai/");

    /// <summary>Where WebView2 keeps its cookies and cache for this app.</summary>
    public static string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsageMonitor", "WebView2");

    // Only fixed API paths are allowed, so nothing from a response can steer a request elsewhere.
    private static readonly Regex AllowedPath = new("^/api/[A-Za-z0-9/_-]+$", RegexOptions.CultureInvariant);

    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);

    private static Task<CoreWebView2Environment>? _environment;

    private CoreWebView2Controller? _controller;
    private Task? _ready;

    /// <summary>
    /// Returns the WebView2 environment shared by the hidden session and the sign-in window.
    /// Both must use the same environment so they share cookies.
    /// </summary>
    /// <exception cref="WebView2RuntimeNotFoundException">The WebView2 Runtime is not installed.</exception>
    public static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment is null || _environment.IsFaulted || _environment.IsCanceled)
        {
            _environment = CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: UserDataFolder);
        }

        return _environment;
    }

    /// <summary>
    /// Creates the hidden browser and opens a claude.ai page, so later requests are same-origin.
    /// Safe to call more than once; a failed start is retried on the next call.
    /// </summary>
    /// <param name="parentWindow">The handle of a window to host the hidden browser.</param>
    public Task InitializeAsync(IntPtr parentWindow)
    {
        if (_ready is null || _ready.IsFaulted || _ready.IsCanceled)
        {
            _ready = InitializeCoreAsync(parentWindow);
        }

        return _ready;
    }

    private async Task InitializeCoreAsync(IntPtr parentWindow)
    {
        _controller?.Close();
        DebugLog.Write("Web session: starting hidden browser");
        var environment = await GetEnvironmentAsync();
        DebugLog.Write($"Web session: WebView2 runtime {environment.BrowserVersionString}");
        _controller = await environment.CreateCoreWebView2ControllerAsync(parentWindow);
        _controller.IsVisible = false;

        var core = _controller.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        // A small same-origin page is enough; the full claude.ai app is not needed.
        await NavigateAsync(core, new Uri(BaseUri, "robots.txt"));
        DebugLog.Write($"Web session: ready on {DebugLog.SafeUrl(core.Source)}");
    }

    /// <summary>
    /// Sends a GET request to a claude.ai API path from inside the hidden browser, with its cookies.
    /// </summary>
    /// <param name="path">An API path such as "/api/organizations". Other paths are rejected.</param>
    /// <returns>The HTTP status code and the response body.</returns>
    /// <exception cref="ArgumentException">The path is not an allowed API path.</exception>
    /// <exception cref="InvalidOperationException">The session is not initialized, or the request could not be sent.</exception>
    public async Task<(int Status, string Body)> GetAsync(string path)
    {
        if (!AllowedPath.IsMatch(path))
        {
            throw new ArgumentException("Only claude.ai API paths are allowed.", nameof(path));
        }

        if (_ready is null)
        {
            throw new InvalidOperationException("The web session is not initialized.");
        }

        // Wait for start-up first, so a call made while the browser is still starting does not fail.
        await _ready;
        if (_controller is null)
        {
            throw new InvalidOperationException("The web session was closed.");
        }

        return await FetchAsync(_controller.CoreWebView2, path);
    }

    /// <summary>
    /// Runs a same-origin fetch in a WebView2 page and returns the status and body.
    /// Shared by the hidden session and the sign-in window.
    /// </summary>
    /// <param name="core">A WebView2 page currently on claude.ai.</param>
    /// <param name="path">An allowed API path.</param>
    /// <returns>The HTTP status code and the response body.</returns>
    /// <exception cref="InvalidOperationException">The script failed, for example because of a network error.</exception>
    internal static async Task<(int Status, string Body)> FetchAsync(CoreWebView2 core, string path)
    {
        // The path is passed as a JSON string literal, so it cannot break out of the script.
        var expression =
            "fetch(" + JsonSerializer.Serialize(path) + ", { credentials: 'include', headers: { 'accept': 'application/json' } })" +
            ".then(async r => JSON.stringify({ status: r.status, body: await r.text() }))";
        var parameters = JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true });

        var raw = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters);
        using var outer = JsonDocument.Parse(raw);
        if (outer.RootElement.TryGetProperty("exceptionDetails", out _) ||
            !outer.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("value", out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("The request to claude.ai could not be sent. Check the internet connection.");
        }

        using var inner = JsonDocument.Parse(value.GetString()!);
        if (!inner.RootElement.TryGetProperty("status", out var statusElement) || !statusElement.TryGetInt32(out var status) ||
            !inner.RootElement.TryGetProperty("body", out var bodyElement) || bodyElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Unexpected response from claude.ai.");
        }

        var body = bodyElement.GetString() ?? string.Empty;
        DebugLog.Write($"GET {path} -> {status} ({(status == 200 ? $"{body.Length} chars" : DebugLog.DescribeBody(body))})");
        return (status, body);
    }

    /// <summary>
    /// Signs out by clearing the cookies and stored data of the shared WebView2 profile.
    /// </summary>
    public async Task SignOutAsync()
    {
        if (_ready is null || _controller is null)
        {
            return;
        }

        await _ready;
        await _controller.CoreWebView2.Profile.ClearBrowsingDataAsync();
    }

    /// <summary>
    /// Navigates a WebView2 page and waits until the navigation finishes or times out.
    /// </summary>
    /// <param name="core">The page to navigate.</param>
    /// <param name="uri">The address to open.</param>
    internal static async Task NavigateAsync(CoreWebView2 core, Uri uri)
    {
        var done = new TaskCompletionSource();
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult();

        core.NavigationCompleted += OnCompleted;
        try
        {
            core.Navigate(uri.AbsoluteUri);
            await done.Task.WaitAsync(NavigationTimeout);
        }
        finally
        {
            core.NavigationCompleted -= OnCompleted;
        }
    }

    /// <summary>Closes the hidden browser.</summary>
    public void Dispose()
    {
        _controller?.Close();
        _controller = null;
        _ready = null;
    }
}
