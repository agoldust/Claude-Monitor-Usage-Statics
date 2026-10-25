using System.Globalization;
using System.Windows;
using System.Windows.Automation;
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
    // Window widths (device-independent pixels) where the dashboard switches layout. They use the window width,
    // not the content width, because each layout changes the margins, which would change the content width again.
    private const double CompactLayoutBelow = 860;
    private const double WideLayoutFrom = 1540;
    private const double ExtraWideLayoutFrom = 2300;

    // Width of the custom scroll bar in App.xaml. Its space counts toward the right-hand spacing.
    private const double ScrollBarWidth = 10;

    // Live limit tiles are at least this wide; the rest of the row is shared out so the tiles fill it.
    private const double MinLimitTileWidth = 300;

    // Below this window width the top-bar actions move to a second row under the title.
    private const double TopBarTwoRowsBelow = 1000;

    // Below this window width the Refresh button also drops its label, so the action row fits.
    private const double RefreshIconOnlyBelow = 440;

    private readonly MonitorHub _hub;

    // Ticks the reset countdowns every second between data refreshes.
    private readonly System.Windows.Threading.DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private LayoutMode? _layout;
    private bool? _topBarTwoRows;

    /// <summary>How the dashboard sections are arranged for the available width.</summary>
    private enum LayoutMode
    {
        /// <summary>Small, phone-like windows: one column, two totals per row, shorter charts, fewer axis labels.</summary>
        Compact,

        /// <summary>One column with the model share and token mix side by side.</summary>
        Normal,

        /// <summary>Large screens: two columns of sections and taller charts.</summary>
        Wide,

        /// <summary>Very large screens: three columns of sections.</summary>
        ExtraWide
    }

    /// <summary>
    /// Creates the dashboard and draws whatever the hub already has.
    /// </summary>
    /// <param name="hub">The shared data source.</param>
    public MainWindow(MonitorHub hub)
    {
        InitializeComponent();
        _hub = hub;
        _hub.Changed += OnHubChanged;
        Closed += (_, _) =>
        {
            _countdown.Stop();
            _hub.Changed -= OnHubChanged;
        };
        _countdown.Tick += (_, _) => UpdateCountdowns();
        _countdown.Start();
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                await _hub.RefreshAsync();
            }
        };
        Activated += (_, _) => UpdateWidgetButton();
        SourceInitialized += (_, _) =>
        {
            ApplyGlass();
            MaximizeFix.Attach(this);
        };
        StateChanged += (_, _) => OnStateChanged();
        FitToWorkArea();
        SizeChanged += (_, e) =>
        {
            ApplyLayout(e.NewSize.Width switch
            {
                < CompactLayoutBelow => LayoutMode.Compact,
                >= ExtraWideLayoutFrom => LayoutMode.ExtraWide,
                >= WideLayoutFrom => LayoutMode.Wide,
                _ => LayoutMode.Normal
            });
            ApplyTopBarLayout(e.NewSize.Width < TopBarTwoRowsBelow);

            // The label hides (and with it Alt+R), but F5 still refreshes and the tooltip names the button.
            var iconOnly = e.NewSize.Width < RefreshIconOnlyBelow;
            RefreshLabel.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;
            RefreshGlyph.Margin = iconOnly ? new Thickness(0) : new Thickness(0, 0, 8, 0);
        };

        // The scroll bar appears and disappears as content grows or the window resizes; keep the edges even.
        MainScroll.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
            {
                UpdateEdgeSpacing();
            }
        };
        LimitsList.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                UpdateLimitTileWidth();
            }
        };
        ApplyLayout(LayoutMode.Normal);
        ApplyTopBarLayout(Width < TopBarTwoRowsBelow);
        Render();
    }

    /// <summary>
    /// Shrinks the default size on small screens, such as a 1366 x 768 laptop at 150% scaling,
    /// so the whole window, including its title bar buttons, starts on screen.
    /// </summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width * 0.95));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height * 0.95));
    }

    /// <summary>
    /// On narrow windows, moves the actions to a second row under the title, hides the logo and the
    /// "Last refresh" text (it moves into the Refresh tooltip), and shows Sign in as an icon only.
    /// </summary>
    private void ApplyTopBarLayout(bool twoRows)
    {
        if (_topBarTwoRows == twoRows)
        {
            return;
        }

        _topBarTwoRows = twoRows;
        DockPanel.SetDock(ActionBar, twoRows ? Dock.Bottom : Dock.Right);
        ActionBar.HorizontalAlignment = twoRows ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        AppLogo.Visibility = twoRows ? Visibility.Collapsed : Visibility.Visible;
        TitleText.FontSize = twoRows ? 18 : 22;
        LastRefreshText.Visibility = twoRows ? Visibility.Collapsed : Visibility.Visible;
        SignInButtonText.Visibility = twoRows ? Visibility.Collapsed : Visibility.Visible;
        SignInGlyph.Margin = twoRows ? new Thickness(0) : new Thickness(0, 0, 8, 0);
        UpdateEdgeSpacing();
    }

    /// <summary>
    /// Gives the window the same spacing on the left and the right. The right side includes the scroll bar
    /// when it shows, so the outer margin there is smaller by the scroll bar's width. The top bar adds the
    /// difference back, so the window buttons keep the same gap to the edge as the content on the left.
    /// </summary>
    private void UpdateEdgeSpacing()
    {
        var side = _layout == LayoutMode.Compact ? 10.0 : 16.0;
        var scrollBar = MainScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible ? ScrollBarWidth : 0;
        var right = Math.Max(0, side - scrollBar);

        Shell.Margin = new Thickness(side, _layout == LayoutMode.Compact ? 8 : 10, right, side);
        TopBar.Margin = new Thickness(0, 0, side - right, _topBarTwoRows == true ? 10 : 16);
    }

    /// <summary>
    /// Arranges the sections for the available width. Compact (phone-like) keeps one column with two totals per
    /// row and shorter charts; Normal is one column; Wide puts the sections in two columns with taller charts;
    /// ExtraWide uses three columns. Wide and ExtraWide show the tables side by side.
    /// </summary>
    private void ApplyLayout(LayoutMode layout)
    {
        if (_layout == layout)
        {
            return;
        }

        _layout = layout;
        var compact = layout == LayoutMode.Compact;
        var big = layout is LayoutMode.Wide or LayoutMode.ExtraWide;
        const int full = 3; // Spans every column; the third has zero width outside ExtraWide.
        ThirdColumn.Width = layout == LayoutMode.ExtraWide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        switch (layout)
        {
            case LayoutMode.ExtraWide:
                Place(LiveCard, 0, 0);
                Place(TotalsGrid, 0, 1);
                Place(ModelCard, 0, 2);
                Place(DailyCard, 1, 0);
                Place(HourCard, 1, 1);
                Place(MixCard, 1, 2);
                Place(AccountCard, 2, 0, span: full);
                Place(TablesCard, 3, 0, span: full);
                Place(FooterText, 4, 0, span: full);
                break;

            case LayoutMode.Wide:
                Place(LiveCard, 0, 0);
                Place(TotalsGrid, 0, 1);
                Place(DailyCard, 1, 0);
                Place(HourCard, 1, 1);
                Place(ModelCard, 2, 0);
                Place(MixCard, 2, 1);
                Place(AccountCard, 3, 0, span: full);
                Place(TablesCard, 4, 0, span: full);
                Place(FooterText, 5, 0, span: full);
                break;

            default:
                Place(LiveCard, 0, 0, span: full);
                Place(TotalsGrid, 1, 0, span: full);
                Place(DailyCard, 2, 0, span: full);

                // Side by side the model names and legend would be squeezed on small screens, so they stack.
                Place(ModelCard, 3, 0, span: compact ? full : 1);
                Place(MixCard, compact ? 4 : 3, compact ? 0 : 1, span: compact ? full : 1);
                Place(HourCard, 5, 0, span: full);
                Place(AccountCard, 6, 0, span: full);
                Place(TablesCard, 7, 0, span: full);
                Place(FooterText, 8, 0, span: full);
                break;
        }

        // Beside the live card, the four totals form a 2 x 2 block as tall as that card,
        // so the numbers get larger and sit in the middle of each card. Small screens also use 2 x 2.
        TotalsGrid.Columns = layout == LayoutMode.Normal ? 4 : 2;
        foreach (var number in new[] { TodayTokensText, WeekTokensText, MonthTokensText, AllTokensText })
        {
            number.FontSize = big ? 36 : 24;
            ((FrameworkElement)number.Parent).VerticalAlignment = big ? VerticalAlignment.Center : VerticalAlignment.Top;
        }

        DockPanel.SetDock(DailyChartHeading, compact ? Dock.Top : Dock.Left);
        DailyChartNote.TextAlignment = compact ? TextAlignment.Left : TextAlignment.Right;
        DailyChart.Height = big ? 240 : compact ? 150 : 190;
        HourChart.Height = big ? 240 : compact ? 120 : 150;
        AccountGrid.Columns = layout switch { LayoutMode.ExtraWide => 4, LayoutMode.Wide => 3, LayoutMode.Compact => 1, _ => 2 };
        UpdateEdgeSpacing();

        var tables = new[] { DailyTable, ModelTable, ProjectTable };
        for (var i = 0; i < tables.Length; i++)
        {
            Place(tables[i], big ? 0 : i, big ? i : 0, span: big ? 1 : 3);
            tables[i].Margin = big && i < tables.Length - 1 ? new Thickness(0, 0, 16, 0) : new Thickness(0);
        }

        // Axis label density follows the chart width.
        if (_hub.Summary is { } summary)
        {
            ShowDailyChart(summary);
            ShowHourChart(summary);
        }
    }

    /// <summary>
    /// Sizes the live limit tiles so each row is filled edge to edge: as many tiles per row as fit at
    /// <see cref="MinLimitTileWidth"/>, but never more columns than there are tiles.
    /// </summary>
    private void UpdateLimitTileWidth()
    {
        const double gap = 12; // The tile's right margin.
        var available = LimitsList.ActualWidth;
        if (available <= 0)
        {
            return;
        }

        var tiles = Math.Max(LimitsList.Items.Count, 1);
        var columns = (int)Math.Clamp(Math.Floor((available + gap) / (MinLimitTileWidth + gap)), 1, tiles);

        // Floor, so rounding can never push the last tile onto a new row.
        LimitsList.Tag = Math.Floor(available / columns - gap);
    }

    private static void Place(UIElement element, int row, int column, int span = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, span);
    }

    /// <summary>Applies the dashboard opacity and the blur setting, like the widget's glass.</summary>
    public void ApplyGlass()
    {
        var settings = App.Current.Settings;

        // Keep a little alpha even at 0%, so the window still takes clicks and can be moved.
        var alpha = (byte)Math.Round(Math.Max(settings.DashboardOpacity, 0.02) * 255);
        var background = new SolidColorBrush(Color.FromArgb(alpha, 0x1C, 0x1B, 0x19));
        background.Freeze();
        Root.Background = background;

        GlassEffect.SetBlur(this, settings.WidgetBlur);
    }

    /// <summary>
    /// Swaps the maximize glyph and hides the border when maximized. <see cref="MaximizeFix"/> keeps the size to the work area.
    /// </summary>
    private void OnStateChanged()
    {
        var maximized = WindowState == WindowState.Maximized;
        Root.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void OnHubChanged(object? sender, EventArgs e) => Render();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _hub.RefreshAsync();
    }

    private void WidgetButton_Click(object sender, RoutedEventArgs e)
    {
        App.Current.ToggleWidget();
        UpdateWidgetButton();
    }

    /// <summary>Names the icon-only widget button after what a click will do.</summary>
    public void UpdateWidgetButton()
    {
        var text = App.Current.IsWidgetOpen ? "Hide widget" : "Show widget";
        WidgetButton.ToolTip = text;
        AutomationProperties.SetName(WidgetButton, text);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this }.ShowDialog();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
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
            // Type only in the log; the message can contain arbitrary text.
            DebugLog.Write($"Sign-in failed: {ex.GetType().Name}");
            LiveStatusText.Text = _hub.SignedIn
                ? "Sign-out did not finish. Try again in a moment."
                : "Sign-in did not finish. Check the internet connection and try again.";
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

        // The widget can also be opened or closed from the tray or its own menu while this window stays active.
        UpdateWidgetButton();
        SignInButtonText.Text = _hub.SignedIn ? "Sign out" : "Sign in";

        LastRefreshText.Text = _hub.IsLoading
            ? "Refreshing..."
            : _hub.LastUpdated is { } updated ? $"Last refresh {updated:HH:mm:ss}" : "";

        // On narrow windows the text above is hidden, so the tooltip carries it too.
        RefreshButton.ToolTip = LastRefreshText.Text.Length == 0 ? "Refresh now (F5)" : $"Refresh now (F5)\n{LastRefreshText.Text}";

        var every = _hub.Interval.TotalMinutes >= 1 ? $"{_hub.Interval.TotalMinutes:0.#} min" : $"{_hub.Interval.TotalSeconds:0} s";
        if (_hub.IsLoading && _hub.LastUpdated is null)
        {
            StatusText.Text = "Loading...";
        }
        else if (_hub.LocalError is { } error)
        {
            StatusText.Text = $"Local usage not updated. {error}" +
                              (_hub.NextRefresh is { } next ? $" Trying again at {next:HH:mm:ss}." : "");
        }
        else if (!_hub.LogsFound)
        {
            StatusText.Text = $"No Claude Code logs found in {ClaudePaths.ClaudeHome}. Use Claude Code once, then press Refresh.";
        }
        else if (_hub.LastUpdated is not null)
        {
            StatusText.Text = $"Local logs · refreshes every {every} · F5 to refresh now";
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
            UpdateLimitTileWidth();
            UpdateCountdowns();
            LiveExtraText.Text = UsageFormatting.DescribeExtras(live);
            LiveExtraText.Visibility = LiveExtraText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            LiveLegendText.Visibility = Visibility.Visible;
        }
        else
        {
            LimitsList.ItemsSource = null;
            LiveExtraText.Visibility = Visibility.Collapsed;
            LiveLegendText.Visibility = Visibility.Collapsed;
            AvailabilityText.Visibility = Visibility.Collapsed;
        }

        LiveStatusText.Text = result.Status switch
        {
            LiveUsageStatus.Ok when result.Usage is { Limits.Count: 0 } => "Signed in, but claude.ai returned no limits for this plan.",
            LiveUsageStatus.Ok when result.Usage is { } u =>
                $"Live from claude.ai{(string.IsNullOrWhiteSpace(u.OrganizationName) ? "" : $" · {u.OrganizationName}")} · fetched {u.FetchedAt:HH:mm:ss}",
            LiveUsageStatus.SignedOut => "Sign in to see your current session and weekly limits.",
            LiveUsageStatus.RuntimeMissing => result.Message ?? "",
            _ => $"Live limits not updated. {result.Message}" +
                 (_hub.LastGoodLive is { } last ? $" Showing claude.ai data fetched at {last.FetchedAt:HH:mm:ss}." : "") +
                 (_hub.NextRefresh is { } next ? $" Trying again at {next:HH:mm:ss}." : "")
        };
    }

    /// <summary>
    /// Ticks the countdown in each limit tile and the "tokens available again" line. Runs every second.
    /// </summary>
    private void UpdateCountdowns()
    {
        var now = DateTimeOffset.Now;
        if (LimitsList.ItemsSource is IEnumerable<LimitRow> rows)
        {
            foreach (var row in rows)
            {
                row.Reset.Update(now);
            }
        }

        if (_hub.LastGoodLive is { } live && UsageFormatting.DescribeAvailability(live.Limits, now) is { } availability)
        {
            AvailabilityText.Text = availability.Text;
            AvailabilityText.Foreground = availability.Blocked ? Palette.CriticalText : (Brush)FindResource("TextBrush");
            AvailabilityText.Visibility = Visibility.Visible;
        }
        else
        {
            AvailabilityText.Visibility = Visibility.Collapsed;
        }
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
        SetTotals(s.Today, TodayCard, TodayTokensText, TodayDetailText);
        SetTotals(s.Last7Days, WeekCard, WeekTokensText, WeekDetailText);
        SetTotals(s.Last30Days, MonthCard, MonthTokensText, MonthDetailText);
        SetTotals(s.AllTime, AllCard, AllTokensText, AllDetailText);

        ShowDailyChart(s);
        var busiest = s.ByDay.MaxBy(d => d.Totals.TotalTokens);
        DailyChartNote.Text = busiest is null || busiest.Totals.TotalTokens == 0
            ? "No usage in the last 14 days"
            : $"Busiest: {busiest.Day.ToString("ddd d MMM", CultureInfo.CurrentCulture)}, {UsageFormatting.Compact(busiest.Totals.TotalTokens)} tokens";

        ShowModelShare(s.ByModel);
        ShowTokenMix(s.Last30Days);

        ShowHourChart(s);

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
    /// Builds the 14-day chart. On small screens the bars are narrow, so every other day is labelled with
    /// its day number only ("19" instead of "Mon 19").
    /// </summary>
    private void ShowDailyChart(UsageSummary s)
    {
        var compact = _layout == LayoutMode.Compact;
        DailyChart.ItemsSource = UsageFormatting.DailyColumns(s.ByDay, withLabels: true,
            labelEvery: compact ? 2 : 1, dayFormat: compact ? "%d" : "ddd d",
            todayLabel: compact ? null : "Today"); // Today keeps its lighter bar color either way.
    }

    /// <summary>Builds the hour-of-day chart, labelling every hour on wide screens, every sixth on small ones.</summary>
    private void ShowHourChart(UsageSummary s)
    {
        var labelEvery = _layout switch { LayoutMode.Wide or LayoutMode.ExtraWide => 1, LayoutMode.Compact => 6, _ => 3 };
        HourChart.ItemsSource = UsageFormatting.Columns(s.ByHour.Select((h, hour) => (
            Label: hour % labelEvery == 0 ? hour.ToString("00", CultureInfo.InvariantCulture) : "",
            Value: (double)h.TotalTokens,
            Tooltip: $"{hour:00}:00 to {(hour + 1) % 24:00}:00\n{h.TotalTokens:N0} tokens · {h.Requests:N0} requests",
            Brush: Palette.Sonnet)));
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
    /// Fills one summary card: the token count, one line of requests and cost, and the split per token type
    /// in the card's tooltip, using the same four types as the token mix.
    /// </summary>
    private static void SetTotals(UsageTotals t, Border card, TextBlock big, TextBlock detail)
    {
        big.Text = UsageFormatting.Compact(t.TotalTokens);
        detail.Text = $"tokens · {t.Requests:N0} requests · Est. cost ${t.EstimatedCostUsd:F2}";
        card.ToolTip = $"{t.TotalTokens:N0} tokens\n" +
                       $"Input: {t.InputTokens:N0}\nOutput: {t.OutputTokens:N0}\n" +
                       $"Cache write: {t.CacheCreationTokens:N0}\nCache read: {t.CacheReadTokens:N0}";
        AutomationProperties.SetHelpText(card, card.ToolTip.ToString());
    }

    /// <summary>
    /// Builds a "name: value" line for the account panel, with "not available" in place of a missing value.
    /// </summary>
    private static string Label(string name, string? value)
    {
        return $"{name}: {(string.IsNullOrWhiteSpace(value) ? "not available" : value)}";
    }
}
