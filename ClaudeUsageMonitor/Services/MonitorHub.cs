using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Loads local and live usage on a timer and shares the latest results with every window,
/// so the dashboard and the widget show the same data and claude.ai is polled once per interval.
/// All members must be used on the UI thread.
/// </summary>
public sealed class MonitorHub : IDisposable
{
    private readonly ClaudeLogReader _logReader = new();
    private readonly AccountReader _accountReader = new();
    private readonly ClaudeWebSession _webSession = new();
    private readonly LiveUsageService _liveService;
    private readonly DispatcherTimer _timer = new();

    // The hidden browser needs a parent window that lives as long as the app, whichever windows are open.
    private readonly Window _browserHost;

    /// <summary>Creates the hub. Call <see cref="Start"/> to begin loading.</summary>
    /// <param name="settings">The settings that hold the refresh interval.</param>
    public MonitorHub(AppSettings settings)
    {
        _liveService = new LiveUsageService(_webSession);
        _browserHost = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "Claude Usage Monitor browser host"
        };
        new WindowInteropHelper(_browserHost).EnsureHandle();

        SetInterval(TimeSpan.FromSeconds(settings.RefreshSeconds));
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    /// <summary>Raised on the UI thread when loading starts or finishes, so windows can redraw.</summary>
    public event EventHandler? Changed;

    /// <summary>True while a refresh is running.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>The account details from the last refresh, or null before the first one.</summary>
    public AccountInfo? Account { get; private set; }

    /// <summary>The local usage summary from the last refresh, or null before the first one.</summary>
    public UsageSummary? Summary { get; private set; }

    /// <summary>True when Claude Code's log folder exists.</summary>
    public bool LogsFound => _logReader.RootExists;

    /// <summary>The error from the last local refresh, or null when it worked.</summary>
    public string? LocalError { get; private set; }

    /// <summary>When the last refresh finished, or null before the first one.</summary>
    public DateTimeOffset? LastUpdated { get; private set; }

    /// <summary>The result of the last live fetch, or null before the first one.</summary>
    public LiveUsageResult? LiveResult { get; private set; }

    /// <summary>The last live data that loaded, kept when a later fetch fails. Null when signed out.</summary>
    public LiveUsage? LastGoodLive { get; private set; }

    /// <summary>True when the last live fetch found a working claude.ai session.</summary>
    public bool SignedIn { get; private set; }

    /// <summary>The time between automatic refreshes.</summary>
    public TimeSpan Interval => _timer.Interval;

    /// <summary>Starts the timer and runs the first refresh.</summary>
    public void Start()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    /// <summary>Changes the refresh interval, within the limits in <see cref="AppSettings"/>.</summary>
    public void SetInterval(TimeSpan interval)
    {
        var seconds = Math.Clamp(interval.TotalSeconds, AppSettings.MinRefreshSeconds, AppSettings.MaxRefreshSeconds);
        _timer.Interval = TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Reloads local data on a background thread, then live data. Does nothing if a refresh is already running.
    /// Never throws; problems are stored in <see cref="LocalError"/> and <see cref="LiveResult"/>.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        OnChanged();
        try
        {
            try
            {
                var (account, summary) = await Task.Run(() =>
                {
                    var accountInfo = _accountReader.Read();
                    var records = _logReader.ReadAll();
                    return (accountInfo, UsageAggregator.Build(records, DateTimeOffset.UtcNow));
                });

                Account = account;
                Summary = summary;
                LocalError = null;
            }
            catch (Exception ex)
            {
                LocalError = ex.Message;
                DebugLog.Write($"Local refresh failed: {ex.GetType().Name}");
            }

            await RefreshLiveCoreAsync();
            LastUpdated = DateTimeOffset.Now;
        }
        finally
        {
            IsLoading = false;
            OnChanged();
        }
    }

    /// <summary>Fetches live data only, for example right after signing in.</summary>
    public async Task RefreshLiveAsync()
    {
        await RefreshLiveCoreAsync();
        OnChanged();
    }

    /// <summary>Clears the claude.ai session and forgets the live data.</summary>
    public async Task SignOutAsync()
    {
        await _webSession.SignOutAsync();
        SetLive(new LiveUsageResult(LiveUsageStatus.SignedOut));
        OnChanged();
    }

    private async Task RefreshLiveCoreAsync()
    {
        LiveUsageResult result;
        try
        {
            await _webSession.InitializeAsync(new WindowInteropHelper(_browserHost).Handle);
            result = await _liveService.FetchAsync(Account?.OrganizationId);
        }
        catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            result = new LiveUsageResult(LiveUsageStatus.RuntimeMissing,
                Message: "Live data needs the Microsoft Edge WebView2 Runtime. Install it from Microsoft, then restart the app.");
        }
        catch (Exception ex)
        {
            result = new LiveUsageResult(LiveUsageStatus.Error, Message: ex.Message);
        }

        DebugLog.Write($"Live: {result.Status}{(result.Message is null ? "" : $" - {result.Message}")}" +
                       (result.Usage is null ? "" : $" ({result.Usage.Limits.Count} limits)"));
        SetLive(result);
    }

    private void SetLive(LiveUsageResult result)
    {
        LiveResult = result;
        switch (result.Status)
        {
            case LiveUsageStatus.Ok when result.Usage is not null:
                SignedIn = true;
                LastGoodLive = result.Usage;
                break;
            case LiveUsageStatus.SignedOut:
                SignedIn = false;
                LastGoodLive = null;
                break;
        }
    }

    private void OnChanged()
    {
        // Windows redraw in this event; a drawing bug must not escape into the timer's async void handler.
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Redraw failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Stops the timer and closes the hidden browser.</summary>
    public void Dispose()
    {
        _timer.Stop();
        _webSession.Dispose();
        _browserHost.Close();
    }
}
