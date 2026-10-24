using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor;

/// <summary>
/// Shows what the app is, who made it, its license, version details, how it handles data, and credits.
/// </summary>
public partial class AboutWindow : Controls.GlassDialog
{
    /// <summary>Creates the window and fills in the version details.</summary>
    public AboutWindow()
    {
        InitializeComponent();

        VersionLine.Text = $"Version {AppInfo.Version}";
        DescriptionText.Text = AppInfo.Description;
        LicenseText.Text = $"{AppInfo.License} · {AppInfo.Copyright}";

        AddBuildRow("Version", AppInfo.Version);
        AddBuildRow("Built", AppInfo.BuildDate?.ToString("g", CultureInfo.CurrentCulture) ?? "unknown");
        AddBuildRow(".NET runtime", AppInfo.Runtime);
        AddBuildRow("WebView2 Runtime", AppInfo.WebView2Version);
        AddBuildRow("Windows", AppInfo.OperatingSystem);
    }

    private void AddBuildRow(string name, string value)
    {
        var row = BuildGrid.RowDefinitions.Count;
        BuildGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock { Text = name, Margin = new Thickness(0, 0, 0, 4) };
        label.SetResourceReference(StyleProperty, "LabelStyle");
        Grid.SetRow(label, row);

        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);

        BuildGrid.Children.Add(label);
        BuildGrid.Children.Add(text);
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { Tag: string url })
        {
            TryRun(() => AppInfo.OpenLink(url), "Could not open the link");
        }
    }

    private void CopyInfo_Click(object sender, RoutedEventArgs e)
    {
        TryRun(() =>
        {
            Clipboard.SetText(AppInfo.BuildReport());
            CopiedText.Text = "Version info copied.";
        }, "Could not copy");
    }

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) =>
        TryRun(() => AppInfo.OpenFolder(Path.GetDirectoryName(SettingsStore.FilePath)!), "Could not open the folder");

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) =>
        TryRun(() => AppInfo.OpenFolder(Path.GetDirectoryName(DebugLog.FilePath)!), "Could not open the folder");

    private void OpenSessionFolder_Click(object sender, RoutedEventArgs e) =>
        TryRun(() => AppInfo.OpenFolder(ClaudeWebSession.UserDataFolder), "Could not open the folder");

    /// <summary>Runs a click action and shows a short message instead of crashing if it fails.</summary>
    private void TryRun(Action action, string failure)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            CopiedText.Text = $"{failure}: {ex.Message}";
        }
    }
}
