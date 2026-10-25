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
    private static readonly Regex AllowedPath = new(@"^/api/[A-Za-z0-9/_-]+\z", RegexOptions.CultureInvariant);

    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);

    // The page aborts its fetch after 20 s; the extra time covers the DevTools round trip.
    private const int FetchTimeoutMs = 20_000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static Task<CoreWebView2Environment>? _environment;

    private CoreWebView2Controller? _controller;
    private Task? _ready;

    // Bumped by every start and by Dispose, so a start that finishes late can tell it has been replaced.
    private int _generation;

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
        var generation = ++_generation;
        _controller?.Close();
        _controller = null;
        DebugLog.Write("Web session: starting hidden browser");
        var environment = await GetEnvironmentAsync();
        DebugLog.Write($"Web session: WebView2 runtime {environment.BrowserVersionString}");

        var creating = environment.CreateCoreWebView2ControllerAsync(parentWindow);
        CoreWebView2Controller controller;
        try
        {
            controller = await creating.WaitAsync(NavigationTimeout);
        }
        catch (TimeoutException)
        {
            // The creation keeps running; close the browser it makes later instead of leaking it.
            _ = creating.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    t.Result.Close();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
            throw new TimeoutException("The hidden browser did not start in time. Restart the app if this keeps happening.");
        }

        if (generation != _generation)
        {
            // Disposed or restarted while this start was waiting.
            controller.Close();
            throw new InvalidOperationException("The web session was closed.");
        }

        _controller = controller;
        _controller.IsVisible = false;

        var core = _controller.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        // A small same-origin page is enough; the full claude.ai app is not needed.
        // With no connection (for example right after Windows starts) the page is an error page on another origin,
        // so fail here: the start is then retried on the next refresh instead of fetching from the error page.
        var reached = await NavigateAsync(core, new Uri(BaseUri, "robots.txt"));
        if (generation != _generation)
        {
            // Dispose already closed this controller.
            throw new InvalidOperationException("The web session was closed.");
        }

        if (!reached)
        {
            DebugLog.Write("Web session: claude.ai could not be reached");
            throw new InvalidOperationException("claude.ai could not be reached. Check the internet connection.");
        }

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

        try
        {
            return await FetchAsync(_controller.CoreWebView2, path);
        }
        catch (TimeoutException)
        {
            // The page may be stuck; close it so the next refresh starts a fresh browser.
            DebugLog.Write("Web session: request timed out, restarting the hidden browser on the next refresh");
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs a same-origin fetch in a WebView2 page and returns the status and body.
    /// Shared by the hidden session and the sign-in window.
    /// </summary>
    /// <param name="core">A WebView2 page currently on claude.ai.</param>
    /// <param name="path">An allowed API path.</param>
    /// <returns>The HTTP status code and the response body.</returns>
    /// <exception cref="InvalidOperationException">The script failed, for example because of a network error.</exception>
    /// <exception cref="TimeoutException">claude.ai did not answer in time.</exception>
    internal static async Task<(int Status, string Body)> FetchAsync(CoreWebView2 core, string path)
    {
        // The path is passed as a JSON string literal, so it cannot break out of the script.
        var expression =
            "fetch(" + JsonSerializer.Serialize(path) + ", { credentials: 'include', headers: { 'accept': 'application/json' }, " +
            $"signal: AbortSignal.timeout({FetchTimeoutMs}) }})" +
            ".then(async r => JSON.stringify({ status: r.status, body: await r.text() }))";
        var parameters = JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true });

        string raw;
        try
        {
            raw = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters).WaitAsync(RequestTimeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("claude.ai did not answer in time. Check the internet connection.");
        }
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
    /// Starts the hidden browser first if needed, so sign-out never reports success without clearing anything.
    /// </summary>
    /// <param name="parentWindow">The handle of a window to host the hidden browser if it is not running yet.</param>
    /// <exception cref="InvalidOperationException">The browser could not start, so the session could not be cleared.</exception>
    public async Task SignOutAsync(IntPtr parentWindow)
    {
        await InitializeAsync(parentWindow);
        if (_controller is null)
        {
            throw new InvalidOperationException("The web session was closed.");
        }

        await _controller.CoreWebView2.Profile.ClearBrowsingDataAsync();
    }

    /// <summary>
    /// Navigates a WebView2 page and waits until the navigation finishes or times out.
    /// </summary>
    /// <param name="core">The page to navigate.</param>
    /// <param name="uri">The address to open.</param>
    /// <returns>
    /// False when the server could not be reached at all (no HTTP response) or did not answer in time.
    /// An HTTP error page still counts as reached, because the page is then on the right origin.
    /// </returns>
    internal static async Task<bool> NavigateAsync(CoreWebView2 core, Uri uri)
    {
        var done = new TaskCompletionSource<bool>();
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) =>
            done.TrySetResult(e.IsSuccess || e.HttpStatusCode != 0);

        core.NavigationCompleted += OnCompleted;
        try
        {
            core.Navigate(uri.AbsoluteUri);
            return await done.Task.WaitAsync(NavigationTimeout);
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            core.NavigationCompleted -= OnCompleted;
        }
    }

    /// <summary>Closes the hidden browser.</summary>
    public void Dispose()
    {
        _generation++;
        _controller?.Close();
        _controller = null;
        _ready = null;
    }
}
