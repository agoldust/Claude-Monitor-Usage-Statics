using System.Runtime.InteropServices;
using System.Windows;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor;

/// <summary>
/// Starts the app in dashboard or widget mode, keeps one running instance, and exits when no window is open.
/// </summary>
public partial class App : Application
{
    private const string InstanceName = "ClaudeUsageMonitor.SingleInstance";
    private const string ShowSignalName = "ClaudeUsageMonitor.ShowDashboard";

    /// <summary>
    /// Windows app identity for taskbar grouping and notifications. The installer gives its shortcuts the same
    /// value, so pinned icons match the running app. Never change it once released.
    /// </summary>
    public const string AppUserModelId = "de.goldoust.claudeusagemonitor";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private MainWindow? _dashboard;
    private WidgetWindow? _widget;
    private TrayIcon? _tray;
    private bool _exiting;

    /// <summary>The running app.</summary>
    public static new App Current => (App)Application.Current;

    /// <summary>The shared data source for every window.</summary>
    public MonitorHub Hub { get; private set; } = null!;

    /// <summary>The user's settings. Call <see cref="SaveSettings"/> after changing them.</summary>
    public AppSettings Settings { get; private set; } = null!;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // Must be set before the first window or tray icon exists. A failure only affects taskbar grouping.
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

        base.OnStartup(e);

        // A second copy asks the running one to show its dashboard, then exits.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        if (!isFirst)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.BeginInvoke(ShowDashboard), null, Timeout.Infinite, executeOnlyOnce: false);

        Settings = SettingsStore.Load();
        Hub = new MonitorHub(Settings);
        _tray = new TrayIcon(Hub);

        var widgetOnly = e.Args.Any(a => a.Equals(StartupRegistration.WidgetSwitch, StringComparison.OrdinalIgnoreCase));
        if (!widgetOnly)
        {
            ShowDashboard();
        }

        if (widgetOnly || Settings.WidgetVisible)
        {
            ShowWidget();
        }

        Hub.Start();
    }

    /// <summary>Opens the dashboard, or brings it to the front if it is already open.</summary>
    public void ShowDashboard()
    {
        if (_exiting)
        {
            return;
        }

        if (_dashboard is null)
        {
            _dashboard = new MainWindow(Hub);
            _dashboard.Closed += (_, _) =>
            {
                _dashboard = null;
                ExitIfNoWindows();
            };
            MainWindow = _dashboard;
            _dashboard.Show();
        }

        if (_dashboard.WindowState == WindowState.Minimized)
        {
            _dashboard.WindowState = WindowState.Normal;
        }

        _dashboard.Activate();
    }

    /// <summary>True when the widget is open.</summary>
    public bool IsWidgetOpen => _widget is not null;

    /// <summary>Opens the widget, or brings it to the front if it is already open.</summary>
    public void ShowWidget()
    {
        if (_exiting)
        {
            return;
        }

        if (_widget is null)
        {
            _widget = new WidgetWindow(Hub, Settings);
            _widget.Closed += (_, _) =>
            {
                _widget = null;
                if (!_exiting)
                {
                    // The user closed the widget, so do not reopen it next time.
                    Settings.WidgetVisible = false;
                    SaveSettings();
                }

                _dashboard?.UpdateWidgetButton();
                ExitIfNoWindows();
            };
            _widget.Show();
            Settings.WidgetVisible = true;
            SaveSettings();
            _dashboard?.UpdateWidgetButton();
        }

        _widget.Activate();
    }

    /// <summary>Opens the widget if it is closed, or closes it if it is open.</summary>
    public void ToggleWidget()
    {
        if (_widget is null)
        {
            ShowWidget();
        }
        else
        {
            _widget.Close();
        }
    }

    /// <summary>Applies changed settings to the hub and the widget, and saves them.</summary>
    public void ApplySettings()
    {
        Settings.Normalize();
        Hub.SetInterval(TimeSpan.FromSeconds(Settings.RefreshSeconds));
        _widget?.ApplySettings();
        _dashboard?.ApplyGlass();
        foreach (var dialog in Windows.OfType<Controls.GlassDialog>())
        {
            dialog.ApplyGlass();
        }

        SaveSettings();
    }

    /// <summary>Saves the settings file.</summary>
    public void SaveSettings() => SettingsStore.Save(Settings);

    /// <summary>
    /// Shows the claude.ai sign-in window and refreshes live data when sign-in works.
    /// </summary>
    /// <param name="owner">The window to center the sign-in window over.</param>
    public async Task SignInAsync(Window owner)
    {
        var login = new LoginWindow { Owner = owner };
        if (login.ShowDialog() == true)
        {
            await Hub.RefreshLiveAsync();
        }
    }

    /// <summary>Closes every window and exits. The widget stays marked as open, so it returns next time.</summary>
    public void ExitApp()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _widget?.Close();
        _dashboard?.Close();
        _tray?.Dispose();
        _tray = null;
        Hub.Dispose();
        Shutdown();
    }

    private void ExitIfNoWindows()
    {
        // With the notification-area icon, closing the last window leaves the app running there; Exit is in its menu.
        if (!_exiting && _dashboard is null && _widget is null && _tray is null)
        {
            ExitApp();
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        // Windows sign-out or shutdown skips ExitApp, so close the hidden browser here too.
        if (!_exiting)
        {
            Hub?.Dispose();
        }

        // Otherwise a dead icon stays next to the clock until the mouse passes over it.
        _tray?.Dispose();

        _showWait?.Unregister(null);
        _showSignal?.Dispose();
        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned: this was the second copy.
            }

            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
