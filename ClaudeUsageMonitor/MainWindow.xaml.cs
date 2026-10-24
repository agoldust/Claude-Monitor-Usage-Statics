using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor;

/// <summary>
/// The main window. It shows the account details and usage totals read from the local Claude Code files.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>How often the usage data is reloaded while the window is open.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    /// <summary>Reads the Claude Code session logs.</summary>
    private readonly ClaudeLogReader _logReader = new();

    /// <summary>Reads the account and subscription details.</summary>
    private readonly AccountReader _accountReader = new();

    /// <summary>Triggers a reload every <see cref="RefreshInterval"/>.</summary>
    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };

    /// <summary>True while a reload is in progress, so overlapping reloads are skipped.</summary>
    private bool _loading;

    /// <summary>
    /// Initializes the window and starts the refresh timer once the window has loaded.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await LoadAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await LoadAsync();
        };
    }

    /// <summary>
    /// Handles the Refresh button by reloading the account and usage data.
    /// </summary>
    /// <param name="sender">The button that was clicked.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    /// <summary>
    /// Reads the account and usage data on a background thread and shows it in the window.
    /// Does nothing if a reload is already running. Errors are shown in the status bar.
    /// </summary>
    /// <returns>A task that completes when the reload has finished.</returns>
    private async Task LoadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Refreshing...";

        try
        {
            // File and JSON reads run off the UI thread so the window stays responsive.
            var (account, summary) = await Task.Run(() =>
            {
                var accountInfo = _accountReader.Read();
                var records = _logReader.ReadAll();
                return (accountInfo, UsageAggregator.Build(records, DateTimeOffset.UtcNow));
            });

            ShowAccount(account);
            ShowSummary(summary);

            var when = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            StatusText.Text = _logReader.RootExists
                ? $"Updated {when} from {ClaudePaths.ClaudeHome}"
                : $"Updated {when}. No Claude Code logs found at {ClaudePaths.ClaudeHome}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not refresh: {ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            _loading = false;
        }
    }

    /// <summary>
    /// Writes the account and subscription details into the account text blocks.
    /// </summary>
    /// <param name="a">The account details to show.</param>
    private void ShowAccount(AccountInfo a)
    {
        EmailText.Text = Label("Email", a.Email);
        NameText.Text = Label("Name", a.FullName);
        PlanText.Text = Label("Plan", a.SubscriptionType);
        BillingText.Text = Label("Billing", a.BillingType);
        OrgText.Text = Label("Organization", a.OrganizationName);
        RoleText.Text = Label("Role", a.OrganizationRole);
        RateTierText.Text = Label("Rate limit tier", a.RateLimitTier ?? a.OrganizationRateLimitTier);
        ExtraUsageText.Text = Label("Extra usage", a.ExtraUsageEnabled switch
        {
            true => "Enabled",
            false => "Disabled",
            _ => null
        });
        AccountCreatedText.Text = Label("Account created", a.AccountCreatedAt?.LocalDateTime.ToString("d", CultureInfo.CurrentCulture));
        SubscriptionCreatedText.Text = Label("Subscribed since", a.SubscriptionCreatedAt?.LocalDateTime.ToString("d", CultureInfo.CurrentCulture));
        CredentialsExpireText.Text = Label("Local login expires", a.CredentialsExpireAt?.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
    }

    /// <summary>
    /// Writes the usage totals and the daily, per-model and per-project tables.
    /// </summary>
    /// <param name="s">The usage summary to show.</param>
    private void ShowSummary(UsageSummary s)
    {
        SetTotals(s.Today, TodayTokensText, TodayDetailText);
        SetTotals(s.Last7Days, WeekTokensText, WeekDetailText);
        SetTotals(s.Last30Days, MonthTokensText, MonthDetailText);
        SetTotals(s.AllTime, AllTokensText, AllDetailText);

        DailyGrid.ItemsSource = s.ByDay
            .Select(d => new DailyRow(d.Day, d.Totals.Requests, d.Totals.TotalTokens, d.Totals.EstimatedCostUsd))
            .ToList();

        ModelGrid.ItemsSource = s.ByModel
            .Select(m => new ModelRow(m.Model, m.Totals.Requests, m.Totals.TotalTokens, m.Totals.EstimatedCostUsd))
            .ToList();

        ProjectGrid.ItemsSource = s.ByProject
            .Select(p => new ProjectRow(p.ProjectFolder, p.SessionCount, p.Totals.TotalTokens, p.Totals.EstimatedCostUsd))
            .ToList();
    }

    /// <summary>
    /// Fills one summary card with the total token count and the request, cost and token-type details.
    /// </summary>
    /// <param name="t">The totals to show.</param>
    /// <param name="big">The text block that shows the total token count.</param>
    /// <param name="detail">The text block that shows the request count, cost and token breakdown.</param>
    private static void SetTotals(UsageTotals t, System.Windows.Controls.TextBlock big, System.Windows.Controls.TextBlock detail)
    {
        big.Text = Compact(t.TotalTokens) + " tokens";
        detail.Text = $"{t.Requests:N0} requests · est. ${t.EstimatedCostUsd:F2}\n" +
                      $"in {Compact(t.InputTokens)} · out {Compact(t.OutputTokens)} · cache {Compact(t.CacheCreationTokens + t.CacheReadTokens)}";
    }

    /// <summary>
    /// Formats a token count with a B, M or K suffix for values of one thousand or more.
    /// </summary>
    /// <param name="value">The count to format.</param>
    /// <returns>The formatted count, such as "1.23M" or "4,567".</returns>
    private static string Compact(long value)
    {
        return value switch
        {
            >= 1_000_000_000 => $"{value / 1_000_000_000.0:F2}B",
            >= 1_000_000 => $"{value / 1_000_000.0:F2}M",
            >= 1_000 => $"{value / 1_000.0:F1}K",
            _ => value.ToString("N0", CultureInfo.CurrentCulture)
        };
    }

    /// <summary>
    /// Builds a "name: value" line for the account panel.
    /// </summary>
    /// <param name="name">The field name shown before the colon.</param>
    /// <param name="value">The field value, or null or blank when it is unknown.</param>
    /// <returns>The label text, with "not available" in place of a missing value.</returns>
    private static string Label(string name, string? value)
    {
        return $"{name}: {(string.IsNullOrWhiteSpace(value) ? "not available" : value)}";
    }
}
