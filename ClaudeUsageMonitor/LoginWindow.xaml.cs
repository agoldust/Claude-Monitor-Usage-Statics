using System.Windows;
using ClaudeUsageMonitor.Services;
using Microsoft.Web.WebView2.Core;

namespace ClaudeUsageMonitor;

/// <summary>
/// Shows the claude.ai sign-in page in an embedded browser and closes itself once the account is signed in.
/// It shares cookies with <see cref="ClaudeWebSession"/> through the same WebView2 environment.
/// </summary>
public partial class LoginWindow : Window
{
    private bool _checking;

    /// <summary>Creates the window. The browser starts when the window loads.</summary>
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await ClaudeWebSession.GetEnvironmentAsync();
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            Browser.CoreWebView2.Navigate(new Uri(ClaudeWebSession.BaseUri, "login").AbsoluteUri);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            HintText.Text = "Sign-in needs the Microsoft Edge WebView2 Runtime. Install it from Microsoft, then try again.";
        }
        catch (Exception ex)
        {
            HintText.Text = $"The sign-in page could not open: {ex.Message}";
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        DebugLog.Write($"Login: navigated to {DebugLog.SafeUrl(Browser.CoreWebView2?.Source)} (success={e.IsSuccess}, status={e.HttpStatusCode})");
        if (_checking || !IsOnClaudeAppPage())
        {
            return;
        }

        _checking = true;
        try
        {
            // The sign-in flow ends on a claude.ai page; confirm the session works before closing.
            if (Browser.CoreWebView2 is not { } core)
            {
                return;
            }

            var (status, _) = await ClaudeWebSession.FetchAsync(core, "/api/organizations");
            if (status == 200)
            {
                DialogResult = true;
                Close();
            }
        }
        catch (Exception)
        {
            // Not signed in yet, the page is mid-navigation, or the window is closing. This is an async void
            // event handler, so any exception that escapes would crash the app; the next navigation checks again.
        }
        finally
        {
            _checking = false;
        }
    }

    private bool IsOnClaudeAppPage()
    {
        if (!Uri.TryCreate(Browser.CoreWebView2?.Source, UriKind.Absolute, out var current) ||
            !current.Host.Equals(ClaudeWebSession.BaseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = current.AbsolutePath;
        return !path.StartsWith("/login", StringComparison.OrdinalIgnoreCase) &&
               !path.StartsWith("/magic-link", StringComparison.OrdinalIgnoreCase) &&
               !path.StartsWith("/oauth", StringComparison.OrdinalIgnoreCase);
    }
}
