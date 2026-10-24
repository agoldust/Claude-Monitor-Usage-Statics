using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeUsageMonitor.Controls;

/// <summary>
/// Base class for dialogs with the same dark glass look as the dashboard and widget:
/// borderless, blurred, a draggable title bar and a close button. The look comes from the GlassDialogStyle in App.xaml.
/// </summary>
public class GlassDialog : Window
{
    /// <summary>The lowest background opacity a dialog uses, so its text stays readable over other windows.</summary>
    private const double MinimumOpacity = 0.70;

    /// <summary>Identifies the <see cref="GlassBackground"/> property.</summary>
    public static readonly DependencyProperty GlassBackgroundProperty = DependencyProperty.Register(
        nameof(GlassBackground), typeof(Brush), typeof(GlassDialog), new PropertyMetadata(Brushes.Black));

    /// <summary>The translucent background drawn by the template.</summary>
    public Brush GlassBackground
    {
        get => (Brush)GetValue(GlassBackgroundProperty);
        set => SetValue(GlassBackgroundProperty, value);
    }

    /// <summary>Applies the glass style and wires the title bar's close button.</summary>
    public GlassDialog()
    {
        SetResourceReference(StyleProperty, "GlassDialogStyle");
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => Close()));
        ApplyGlass();
    }

    /// <summary>
    /// Applies the dashboard opacity (never below 70%) and the blur setting. Called again when settings change.
    /// </summary>
    public void ApplyGlass()
    {
        var settings = App.Current.Settings;
        var alpha = (byte)Math.Round(Math.Max(settings.DashboardOpacity, MinimumOpacity) * 255);
        var background = new SolidColorBrush(Color.FromArgb(alpha, 0x1C, 0x1B, 0x19));
        background.Freeze();
        GlassBackground = background;

        GlassEffect.SetBlur(this, settings.WidgetBlur);
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Blur needs a window handle, which exists only from here on.
        GlassEffect.SetBlur(this, App.Current.Settings.WidgetBlur);
    }
}
