using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeUsageMonitor.Controls;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor;

/// <summary>
/// The dashboard. It shows live plan limits from claude.ai, and charts and totals from the local Claude Code files.
/// Data comes from the shared <see cref="MonitorHub"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MonitorHub _hub;

    /// <summary>
    /// Creates the dashboard and draws whatever the hub already has.
    /// </summary>
    /// <param name="hub">The shared data source.</param>
    public MainWindow(MonitorHub hub)
    {
        InitializeComponent();
        _hub = hub;
        _hub.Changed += OnHubChanged;
        Closed += (_, _) => _hub.Changed -= OnHubChanged;
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                await _hub.RefreshAsync();
            }
        };
        Activated += (_, _) => WidgetButton.Content = App.Current.IsWidgetOpen ? "Hide widget" : "Show widget";
        Render();
    }

    private void OnHubChanged(object? sender, EventArgs e) => Render();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _hub.RefreshAsync();
    }

    private void WidgetButton_Click(object sender, RoutedEventArgs e)
    {
        App.Current.ToggleWidget();
        WidgetButton.Content = App.Current.IsWidgetOpen ? "Hide widget" : "Show widget";
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this }.ShowDialog();
    }

    /// <summary>
    /// Opens the claude.ai sign-in window, or signs out when already signed in.
    /// </summary>
    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        try
        {
            if (_hub.SignedIn)
            {
                await _hub.SignOutAsync();
            }
            else
            {
                await App.Current.SignInAsync(this);
            }
        }
        catch (Exception ex)
        {
            LiveStatusText.Text = $"Sign-in failed: {ex.Message}";
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    /// <summary>Redraws every section from the hub's latest data.</summary>
    private void Render()
    {
        RefreshButton.IsEnabled = !_hub.IsLoading;
        SignInButton.Content = _hub.SignedIn ? "Sign out" : "Sign in";

        if (_hub.IsLoading && _hub.LastUpdated is null)
        {
            StatusText.Text = "Loading...";
        }
        else if (_hub.LocalError is { } error)
        {
            StatusText.Text = $"Could not refresh: {error}";
        }
        else if (_hub.LastUpdated is { } updated)
        {
            var every = _hub.Interval.TotalMinutes >= 1 ? $"{_hub.Interval.TotalMinutes:0.#} min" : $"{_hub.Interval.TotalSeconds:0} s";
            StatusText.Text = _hub.LogsFound
                ? $"Updated {updated:HH:mm:ss} · refreshes every {every} · F5 to refresh now"
                : $"Updated {updated:HH:mm:ss}. No Claude Code logs found at {ClaudePaths.ClaudeHome}";
        }

        if (_hub.Account is { } account)
        {
            ShowAccount(account);
        }

        if (_hub.Summary is { } summary)
        {
            ShowSummary(summary);
        }

        ShowLive();
    }

    // ---------------------------------------------------------------- Live limits

    /// <summary>
    /// Updates the live card. On an error, the last good tiles stay visible and the status line says how old they are.
    /// </summary>
    private void ShowLive()
    {
        var result = _hub.LiveResult;
        if (result is null)
        {
            LiveStatusText.Text = "Checking claude.ai...";
            return;
        }

        if (_hub.LastGoodLive is { } live)
        {
            var now = DateTimeOffset.Now;
            LimitsList.ItemsSource = live.Limits.Select(l => UsageFormatting.ToLimitRow(l, now)).ToList();
            LiveExtraText.Text = UsageFormatting.DescribeExtras(live);
            LiveExtraText.Visibility = LiveExtraText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            LiveLegendText.Visibility = Visibility.Visible;
        }
        else
        {
            LimitsList.ItemsSource = null;
            LiveExtraText.Visibility = Visibility.Collapsed;
            LiveLegendText.Visibility = Visibility.Collapsed;
        }

        LiveStatusText.Text = result.Status switch
        {
            LiveUsageStatus.Ok when result.Usage is { Limits.Count: 0 } => "Signed in, but claude.ai returned no limits for this plan.",
            LiveUsageStatus.Ok when result.Usage is { } u =>
                $"Live from claude.ai{(string.IsNullOrWhiteSpace(u.OrganizationName) ? "" : $" · {u.OrganizationName}")} · updated {u.FetchedAt:HH:mm:ss}",
            LiveUsageStatus.SignedOut => "Sign in to see your current session and weekly limits.",
            _ => $"Could not update live limits: {result.Message}" +
                 (_hub.LastGoodLive is { } last ? $" Showing data from {last.FetchedAt:HH:mm:ss}." : "")
        };
    }

    // ---------------------------------------------------------------- Account and local stats

    /// <summary>
    /// Writes the account details and the collapsed account header.
    /// </summary>
    private void ShowAccount(AccountInfo a)
    {
        var plan = string.IsNullOrWhiteSpace(a.SubscriptionType)
            ? null
            : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(a.SubscriptionType);

        AccountHeaderText.Text = string.Join(" · ", new[] { "Account", a.Email, plan is null ? null : $"{plan} plan" }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        EmailText.Text = Label("Email", a.Email);
        NameText.Text = Label("Name", a.FullName);
        PlanText.Text = Label("Plan", plan);
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
    /// Writes the totals, the charts, and the detailed tables.
    /// </summary>
    private void ShowSummary(UsageSummary s)
    {
        SetTotals(s.Today, TodayTokensText, TodayDetailText);
        SetTotals(s.Last7Days, WeekTokensText, WeekDetailText);
        SetTotals(s.Last30Days, MonthTokensText, MonthDetailText);
        SetTotals(s.AllTime, AllTokensText, AllDetailText);

        DailyChart.ItemsSource = UsageFormatting.DailyColumns(s.ByDay, withLabels: true);
        var busiest = s.ByDay.MaxBy(d => d.Totals.TotalTokens);
        DailyChartNote.Text = busiest is null || busiest.Totals.TotalTokens == 0
            ? "No usage in the last 14 days"
            : $"Busiest: {busiest.Day.ToString("ddd d MMM", CultureInfo.CurrentCulture)}, {UsageFormatting.Compact(busiest.Totals.TotalTokens)} tokens";

        ShowModelShare(s.ByModel);
        ShowTokenMix(s.Last30Days);

        HourChart.ItemsSource = UsageFormatting.Columns(s.ByHour.Select((h, hour) => (
            Label: hour % 3 == 0 ? hour.ToString("00", CultureInfo.InvariantCulture) : "",
            Value: (double)h.TotalTokens,
            Tooltip: $"{hour:00}:00 to {(hour + 1) % 24:00}:00\n{h.TotalTokens:N0} tokens · {h.Requests:N0} requests",
            Brush: Palette.Sonnet)));

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

    private void ShowModelShare(IReadOnlyList<ModelUsage> models)
    {
        var total = models.Sum(m => (double)m.Totals.TotalTokens);
        ModelShareList.ItemsSource = models.Select(m =>
        {
            var share = total > 0 ? m.Totals.TotalTokens / total : 0;
            return new ShareBar(
                UsageFormatting.ShortModelName(m.Model),
                new GridLength(share, GridUnitType.Star),
                new GridLength(1 - share, GridUnitType.Star),
                Palette.ForModel(m.Model),
                $"{share * 100:0.#}% · {UsageFormatting.Compact(m.Totals.TotalTokens)}",
                $"{m.Model}\n{m.Totals.TotalTokens:N0} tokens · {m.Totals.Requests:N0} requests\nEst. cost ${m.Totals.EstimatedCostUsd:F2}");
        }).ToList();
    }

    /// <summary>Builds the stacked token-mix bar and its legend for a set of totals.</summary>
    private void ShowTokenMix(UsageTotals t)
    {
        var parts = new (string Name, long Value, Brush Brush)[]
        {
            ("Input", t.InputTokens, Palette.Input),
            ("Output", t.OutputTokens, Palette.Output),
            ("Cache write", t.CacheCreationTokens, Palette.CacheWrite),
            ("Cache read", t.CacheReadTokens, Palette.CacheRead)
        };
        var total = (double)parts.Sum(p => p.Value);

        TokenMixBar.Children.Clear();
        TokenMixBar.ColumnDefinitions.Clear();
        foreach (var part in parts.Where(p => p.Value > 0))
        {
            TokenMixBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(part.Value, GridUnitType.Star) });
            var segment = new Border
            {
                Background = part.Brush,
                ToolTip = $"{part.Name}: {part.Value:N0} tokens ({part.Value / total * 100:0.#}%)"
            };
            Grid.SetColumn(segment, TokenMixBar.ColumnDefinitions.Count - 1);
            TokenMixBar.Children.Add(segment);
        }

        TokenMixLegend.ItemsSource = parts
            .Select(p => new LegendItem(p.Brush,
                $"{p.Name}: {UsageFormatting.Compact(p.Value)} ({(total > 0 ? p.Value / total * 100 : 0):0.#}%)"))
            .ToList();
    }

    /// <summary>
    /// Fills one summary card with the total token count and the request, cost and token-type details.
    /// </summary>
    private static void SetTotals(UsageTotals t, TextBlock big, TextBlock detail)
    {
        big.Text = UsageFormatting.Compact(t.TotalTokens) + " tokens";
        detail.Text = $"{t.Requests:N0} requests · Est. cost ${t.EstimatedCostUsd:F2}\n" +
                      $"In {UsageFormatting.Compact(t.InputTokens)} · out {UsageFormatting.Compact(t.OutputTokens)} · " +
                      $"cache {UsageFormatting.Compact(t.CacheCreationTokens + t.CacheReadTokens)}";
    }

    /// <summary>
    /// Builds a "name: value" line for the account panel, with "not available" in place of a missing value.
    /// </summary>
    private static string Label(string name, string? value)
    {
        return $"{name}: {(string.IsNullOrWhiteSpace(value) ? "not available" : value)}";
    }
}
