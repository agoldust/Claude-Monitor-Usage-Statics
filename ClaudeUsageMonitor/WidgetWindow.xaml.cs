using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageMonitor.Controls;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor;

/// <summary>
/// A small glass window with the most important numbers: live limit gauges, today's tokens, and a 14-day mini chart.
/// Opacity, blur and always-on-top come from <see cref="AppSettings"/>.
/// </summary>
public partial class WidgetWindow : Window
{
    private const double ScreenMargin = 16;

    private readonly MonitorHub _hub;
    private readonly AppSettings _settings;

    // Redraws the gauges between data refreshes, so the inner (elapsed time) ring keeps moving.
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };

    // Ticks the reset countdowns every second without rebuilding the gauges.
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Creates the widget and draws whatever the hub already has.</summary>
    /// <param name="hub">The shared data source.</param>
    /// <param name="settings">The settings for opacity, blur, always-on-top and position.</param>
    public WidgetWindow(MonitorHub hub, AppSettings settings)
    {
        InitializeComponent();
        _hub = hub;
        _settings = settings;

        _hub.Changed += OnHubChanged;
        _clock.Tick += (_, _) => Render();
        _clock.Start();
        _countdown.Tick += (_, _) => UpdateCountdowns();
        _countdown.Start();

        SourceInitialized += (_, _) => ApplySettings();
        Loaded += (_, _) => PlaceOnScreen();
        Closed += (_, _) =>
        {
            _clock.Stop();
            _countdown.Stop();
            _hub.Changed -= OnHubChanged;
            RememberPosition();
        };

        Render();
    }

    /// <summary>Applies opacity, blur and always-on-top from the settings.</summary>
    public void ApplySettings()
    {
        Topmost = _settings.WidgetTopmost;
        TopmostMenuItem.IsChecked = Topmost;

        // Keep a little alpha even at 0%, because fully clear pixels do not take mouse clicks and the widget could not be dragged.
        var alpha = (byte)Math.Round(Math.Max(_settings.WidgetOpacity, 0.02) * 255);
        var background = new SolidColorBrush(Color.FromArgb(alpha, 0x1C, 0x1B, 0x19));
        background.Freeze();
        Root.Background = background;

        var blurred = _settings.WidgetBlur && GlassEffect.SetBlur(this, true);
        if (!_settings.WidgetBlur)
        {
            GlassEffect.SetBlur(this, false);
        }

        // Blur fills the whole window rectangle, so rounded corners would show square blurred corners.
        Root.CornerRadius = blurred ? new CornerRadius(0) : new CornerRadius(12);
    }

    private void OnHubChanged(object? sender, EventArgs e) => Render();

    /// <summary>Redraws the gauges, today's numbers, the mini chart and the status line.</summary>
    private void Render()
    {
        var now = DateTimeOffset.Now;

        if (_hub.LastGoodLive is { Limits.Count: > 0 } live)
        {
            GaugeList.ItemsSource = live.Limits.Select(l => UsageFormatting.ToLimitRow(l, now)).ToList();
            GaugeList.Visibility = Visibility.Visible;
        }
        else
        {
            GaugeList.ItemsSource = null;
            GaugeList.Visibility = Visibility.Collapsed;
        }

        UpdateCountdowns();

        var result = _hub.LiveResult;
        LiveText.Text = result?.Status switch
        {
            null => "Checking claude.ai...",
            LiveUsageStatus.Ok => "",
            LiveUsageStatus.SignedOut => "Sign in to see live limits (click here)",
            _ => $"Live limits unavailable: {result.Message}"
        };
        LiveButton.Visibility = LiveText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        LiveButton.IsEnabled = result?.Status == LiveUsageStatus.SignedOut;

        if (_hub.Summary is { } summary)
        {
            TodayTokensRun.Text = UsageFormatting.Compact(summary.Today.TotalTokens);
            TodayDetailText.Text = $"{summary.Today.Requests:N0} req · est. ${summary.Today.EstimatedCostUsd:F2}";
            MiniChart.ItemsSource = UsageFormatting.DailyColumns(summary.ByDay, withLabels: false);
        }

        var every = _hub.Interval.TotalMinutes >= 1 ? $"{_hub.Interval.TotalMinutes:0.#} min" : $"{_hub.Interval.TotalSeconds:0} s";
        UpdatedText.Text = _hub.IsLoading
            ? "Refreshing..."
            : _hub.LastUpdated is { } updated
                ? $"Updated {updated:HH:mm} · every {every} · right-click for options"
                : "Loading...";
    }

    /// <summary>
    /// Ticks the countdown under each gauge, and shows when tokens are available again while a limit is used up.
    /// The rings already show the next resets, so the line stays hidden otherwise to keep the widget small.
    /// </summary>
    private void UpdateCountdowns()
    {
        var now = DateTimeOffset.Now;
        if (GaugeList.ItemsSource is IEnumerable<LimitRow> rows)
        {
            foreach (var row in rows)
            {
                row.Reset.Update(now);
            }
        }

        if (_hub.LastGoodLive is { } live && UsageFormatting.DescribeAvailability(live.Limits, now) is { Blocked: true } availability)
        {
            BlockedText.Text = availability.Text;
            BlockedText.Foreground = Palette.CriticalText;
            BlockedText.Visibility = Visibility.Visible;
        }
        else
        {
            BlockedText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Restores the saved position if it is still on a screen; otherwise uses the top-right corner.</summary>
    private void PlaceOnScreen()
    {
        if (_settings.WidgetLeft is { } left && _settings.WidgetTop is { } top &&
            left >= SystemParameters.VirtualScreenLeft &&
            top >= SystemParameters.VirtualScreenTop &&
            left + ActualWidth <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            top + 40 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            Left = left;
            Top = top;
            return;
        }

        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - ScreenMargin;
        Top = area.Top + ScreenMargin;
    }

    private void RememberPosition()
    {
        _settings.WidgetLeft = Left;
        _settings.WidgetTop = Top;
        App.Current.SaveSettings();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button was released before the drag started.
            return;
        }

        RememberPosition();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _hub.RefreshAsync();

    private void Dashboard_Click(object sender, RoutedEventArgs e) => App.Current.ShowDashboard();

    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.WidgetTopmost = TopmostMenuItem.IsChecked;
        App.Current.ApplySettings();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitApp();

    private async void LiveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.Current.SignInAsync(this);
        }
        catch (Exception ex)
        {
            LiveText.Text = $"Sign-in failed: {ex.Message}";
        }
    }
}
