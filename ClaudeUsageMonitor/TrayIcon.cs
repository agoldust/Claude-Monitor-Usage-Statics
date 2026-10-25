using System.Windows;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;
using Forms = System.Windows.Forms;

namespace ClaudeUsageMonitor;

/// <summary>
/// The icon in the Windows notification area, next to the clock. Its menu opens the dashboard, the widget,
/// settings and About, refreshes, and exits. The tooltip shows the live limits.
/// Must be created and used on the UI thread.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string AppName = "Claude Usage Monitor";

    // NotifyIcon.Text is limited to 127 characters.
    private const int MaxTooltipLength = 127;

    private readonly MonitorHub _hub;
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _image;
    private readonly Forms.ToolStripMenuItem _widgetItem;

    /// <summary>Creates the icon and shows it.</summary>
    /// <param name="hub">The shared data source, used for Refresh and the tooltip.</param>
    public TrayIcon(MonitorHub hub)
    {
        _hub = hub;

        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream)
        {
            _image = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }

        var dashboardItem = new Forms.ToolStripMenuItem("Open dashboard", null, (_, _) => Safe(App.Current.ShowDashboard));
        dashboardItem.Font = new System.Drawing.Font(dashboardItem.Font, System.Drawing.FontStyle.Bold);
        _widgetItem = new Forms.ToolStripMenuItem("Show widget", null, (_, _) => Safe(App.Current.ToggleWidget));

        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            dashboardItem,
            _widgetItem,
            new Forms.ToolStripMenuItem("Refresh now", null, async (_, _) => await _hub.RefreshAsync()),
            new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem("Settings...", null, (_, _) => Safe(ShowDialog<SettingsWindow>)),
            new Forms.ToolStripMenuItem("About...", null, (_, _) => Safe(ShowDialog<AboutWindow>)),
            new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem("Exit", null, (_, _) => Safe(App.Current.ExitApp))
        });
        menu.Opening += (_, _) => _widgetItem.Text = App.Current.IsWidgetOpen ? "Hide widget" : "Show widget";

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = AppName,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                Safe(App.Current.ShowDashboard);
            }
        };

        _hub.Changed += OnHubChanged;
        UpdateTooltip();
    }

    /// <summary>
    /// Runs a menu action. These run in the Windows Forms message path, where an exception would show the
    /// Windows Forms error dialog or end the app, so failures are logged (type only) and ignored.
    /// </summary>
    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Tray menu action failed: {ex.GetType().Name}");
        }
    }

    private void OnHubChanged(object? sender, EventArgs e) => UpdateTooltip();

    /// <summary>Shows the app name and each live limit, such as "Current session (5-hour): 66%".</summary>
    private void UpdateTooltip()
    {
        var lines = new List<string> { AppName };
        if (_hub.LastGoodLive is { } live)
        {
            lines.AddRange(live.Limits.Select(l => $"{l.Label}: {l.Percent:0}%"));
        }
        else if (_hub.LiveResult?.Status == LiveUsageStatus.SignedOut)
        {
            lines.Add("Not signed in to claude.ai");
        }

        var text = string.Join("\n", lines);
        _icon.Text = text.Length <= MaxTooltipLength ? text : text[..(MaxTooltipLength - 3)] + "...";
    }

    /// <summary>
    /// Opens a dialog over the dashboard, opening the dashboard first if needed.
    /// If the dialog is already open, brings it to the front instead of opening a second one.
    /// </summary>
    private static void ShowDialog<T>() where T : Window, new()
    {
        if (Application.Current.Windows.OfType<T>().FirstOrDefault() is { } open)
        {
            open.Activate();
            return;
        }

        App.Current.ShowDashboard();
        new T { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    /// <summary>Removes the icon from the notification area.</summary>
    public void Dispose()
    {
        _hub.Changed -= OnHubChanged;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }
}
