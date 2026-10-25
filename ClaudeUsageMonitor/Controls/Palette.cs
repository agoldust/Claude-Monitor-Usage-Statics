using System.Windows.Media;

namespace ClaudeUsageMonitor.Controls;

/// <summary>
/// Chart and status colors used in code. Colors used only in XAML live in App.xaml.
/// Every color-coded value also has a text label, so color is never the only signal.
/// </summary>
public static class Palette
{
    /// <summary>The main accent, used for normal usage.</summary>
    public static Brush Accent { get; } = Make(0xC1, 0x5F, 0x3C);

    /// <summary>A lighter accent, used to mark today on the dark glass.</summary>
    public static Brush Today { get; } = Make(0xF2, 0xA9, 0x85);

    /// <summary>Warning: a limit is getting close.</summary>
    public static Brush Warning { get; } = Make(0xC9, 0x8A, 0x1B);

    /// <summary>Critical: a limit is nearly used up or will run out before it resets.</summary>
    public static Brush Critical { get; } = Make(0xB3, 0x26, 0x1E);

    /// <summary>Critical, lightened for text, which needs more contrast on the dark glass than a ring does.</summary>
    public static Brush CriticalText { get; } = Make(0xFF, 0x8A, 0x80);

    /// <summary>Opus models.</summary>
    public static Brush Opus { get; } = Make(0xC1, 0x5F, 0x3C);

    /// <summary>Sonnet models.</summary>
    public static Brush Sonnet { get; } = Make(0x3D, 0x7A, 0x8C);

    /// <summary>Haiku models.</summary>
    public static Brush Haiku { get; } = Make(0x7D, 0x8F, 0x4E);

    /// <summary>Any other model.</summary>
    public static Brush OtherModel { get; } = Make(0x8A, 0x84, 0x77);

    /// <summary>Input tokens.</summary>
    public static Brush Input { get; } = Make(0x4F, 0x7C, 0xAC);

    /// <summary>Output tokens.</summary>
    public static Brush Output { get; } = Make(0xC1, 0x5F, 0x3C);

    /// <summary>Cache-write tokens.</summary>
    public static Brush CacheWrite { get; } = Make(0xD9, 0xA4, 0x41);

    /// <summary>Cache-read tokens.</summary>
    public static Brush CacheRead { get; } = Make(0x9D, 0xB0, 0xA3);

    /// <summary>Returns the color for a model name by family.</summary>
    /// <param name="model">The model ID, such as "claude-opus-5-5".</param>
    public static Brush ForModel(string model)
    {
        var name = model.ToLowerInvariant();
        if (name.Contains("opus")) return Opus;
        if (name.Contains("sonnet")) return Sonnet;
        if (name.Contains("haiku")) return Haiku;
        return OtherModel;
    }

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
