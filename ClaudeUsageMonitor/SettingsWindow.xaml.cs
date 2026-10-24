using System.Security;
using System.Windows;
using System.Windows.Controls;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor;

/// <summary>
/// Lets the user change the refresh interval, the widget's look, and start-with-Windows.
/// Every change applies and saves immediately, so the widget previews it live.
/// </summary>
public partial class SettingsWindow : Controls.GlassDialog
{
    private static readonly (int Seconds, string Text)[] Intervals =
    {
        (30, "30 seconds"), (60, "1 minute"), (120, "2 minutes"), (300, "5 minutes"),
        (600, "10 minutes"), (900, "15 minutes"), (1800, "30 minutes"), (3600, "1 hour")
    };

    private readonly AppSettings _settings = App.Current.Settings;

    // True while the controls are being filled, so their change events do not write settings back.
    private bool _loading = true;

    /// <summary>Creates the window and fills it from the current settings.</summary>
    public SettingsWindow()
    {
        InitializeComponent();

        foreach (var (seconds, text) in Intervals)
        {
            RefreshCombo.Items.Add(new ComboBoxItem { Content = text, Tag = seconds });
        }

        if (Intervals.All(i => i.Seconds != _settings.RefreshSeconds))
        {
            RefreshCombo.Items.Add(new ComboBoxItem { Content = $"{_settings.RefreshSeconds} seconds", Tag = _settings.RefreshSeconds });
        }

        RefreshCombo.SelectedItem = RefreshCombo.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == _settings.RefreshSeconds);
        OpacitySlider.Value = Math.Round(_settings.WidgetOpacity * 100);
        OpacityValueText.Text = OpacityLabel(OpacitySlider.Value);
        DashboardOpacitySlider.Value = Math.Round(_settings.DashboardOpacity * 100);
        DashboardOpacityValueText.Text = OpacityLabel(DashboardOpacitySlider.Value);
        BlurCheck.IsChecked = _settings.WidgetBlur;
        TopmostCheck.IsChecked = _settings.WidgetTopmost;
        StartupCheck.IsChecked = StartupRegistration.IsEnabled();
        WidgetOnlyCheck.IsChecked = _settings.StartWidgetOnly;
        UpdateStartupNote();

        _loading = false;
    }

    private void RefreshCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RefreshCombo.SelectedItem is not ComboBoxItem { Tag: int seconds })
        {
            return;
        }

        _settings.RefreshSeconds = seconds;
        App.Current.ApplySettings();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValueText is not null)
        {
            OpacityValueText.Text = OpacityLabel(e.NewValue);
        }

        if (_loading)
        {
            return;
        }

        _settings.WidgetOpacity = e.NewValue / 100;
        App.Current.ApplySettings();
    }

    private void DashboardOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DashboardOpacityValueText is not null)
        {
            DashboardOpacityValueText.Text = OpacityLabel(e.NewValue);
        }

        if (_loading)
        {
            return;
        }

        _settings.DashboardOpacity = e.NewValue / 100;
        App.Current.ApplySettings();
    }

    /// <summary>Shows both readings of a slider value, such as "20% (80% transparent)", so neither is mistaken for the other.</summary>
    private static string OpacityLabel(double percent) => $"{percent:0}% ({100 - percent:0}% transparent)";

    private void BlurCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.WidgetBlur = BlurCheck.IsChecked == true;
        App.Current.ApplySettings();
    }

    private void TopmostCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.WidgetTopmost = TopmostCheck.IsChecked == true;
        App.Current.ApplySettings();
    }

    private void StartupCheck_Click(object sender, RoutedEventArgs e) => UpdateStartup();

    private void WidgetOnlyCheck_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartWidgetOnly = WidgetOnlyCheck.IsChecked == true;
        App.Current.SaveSettings();

        // Re-register so the saved command line matches the new choice.
        if (StartupRegistration.IsEnabled())
        {
            UpdateStartup();
        }
    }

    /// <summary>Writes the start-with-Windows choice to the registry, and undoes the checkbox if that fails.</summary>
    private void UpdateStartup()
    {
        var enable = StartupCheck.IsChecked == true;
        try
        {
            StartupRegistration.SetEnabled(enable, _settings.StartWidgetOnly);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or InvalidOperationException or System.IO.IOException)
        {
            StartupCheck.IsChecked = StartupRegistration.IsEnabled();
            StartupNote.Text = $"Could not change start-with-Windows: {ex.Message}";
            return;
        }

        UpdateStartupNote();
    }

    private void UpdateStartupNote()
    {
        WidgetOnlyCheck.IsEnabled = StartupCheck.IsChecked == true;
        StartupNote.Text = StartupCheck.IsChecked == true
            ? $"Starts from {Environment.ProcessPath}. If you move the app, turn this off and on again."
            : "The app does not start with Windows.";
    }
}
