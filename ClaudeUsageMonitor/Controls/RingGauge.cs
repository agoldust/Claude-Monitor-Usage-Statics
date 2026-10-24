using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ClaudeUsageMonitor.Controls;

/// <summary>
/// A circular gauge. The outer ring shows <see cref="Value"/>; an optional thin inner ring shows <see cref="InnerValue"/>.
/// The value is written as a percentage in the center.
/// </summary>
public sealed class RingGauge : FrameworkElement
{
    /// <summary>Identifies the <see cref="Value"/> property.</summary>
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), 0.0);

    /// <summary>Identifies the <see cref="InnerValue"/> property.</summary>
    public static readonly DependencyProperty InnerValueProperty = Register(nameof(InnerValue), double.NaN);

    /// <summary>Identifies the <see cref="Thickness"/> property.</summary>
    public static readonly DependencyProperty ThicknessProperty = Register(nameof(Thickness), 12.0);

    /// <summary>Identifies the <see cref="ValueBrush"/> property.</summary>
    public static readonly DependencyProperty ValueBrushProperty =
        Register<Brush>(nameof(ValueBrush), new SolidColorBrush(Color.FromRgb(0xC1, 0x5F, 0x3C)));

    /// <summary>Identifies the <see cref="TrackBrush"/> property.</summary>
    public static readonly DependencyProperty TrackBrushProperty =
        Register<Brush>(nameof(TrackBrush), new SolidColorBrush(Color.FromRgb(0xE4, 0xDF, 0xD3)));

    /// <summary>Identifies the <see cref="InnerBrush"/> property.</summary>
    public static readonly DependencyProperty InnerBrushProperty =
        Register<Brush>(nameof(InnerBrush), new SolidColorBrush(Color.FromRgb(0x6B, 0x65, 0x57)));

    /// <summary>Identifies the <see cref="TextBrush"/> property.</summary>
    public static readonly DependencyProperty TextBrushProperty = Register<Brush?>(nameof(TextBrush), null);

    /// <summary>The brush for the center percentage. Null uses <see cref="ValueBrush"/>.</summary>
    public Brush? TextBrush
    {
        get => (Brush?)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>The main value, from 0 to 100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The inner-ring value, from 0 to 100. NaN hides the inner ring.</summary>
    public double InnerValue
    {
        get => (double)GetValue(InnerValueProperty);
        set => SetValue(InnerValueProperty, value);
    }

    /// <summary>The width of the outer ring in device-independent pixels.</summary>
    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>The brush for the used part of the outer ring.</summary>
    public Brush ValueBrush
    {
        get => (Brush)GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    /// <summary>The brush for the unused track behind both rings.</summary>
    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>The brush for the inner ring.</summary>
    public Brush InnerBrush
    {
        get => (Brush)GetValue(InnerBrushProperty);
        set => SetValue(InnerBrushProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(
            double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 120 : availableSize.Height);
        return new Size(side, side);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var side = Math.Min(ActualWidth, ActualHeight);
        if (side <= Thickness * 2)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var outerRadius = side / 2 - Thickness / 2;
        dc.DrawEllipse(null, new Pen(TrackBrush, Thickness), center, outerRadius, outerRadius);
        DrawArc(dc, center, outerRadius, Value, RoundPen(ValueBrush, Thickness));

        if (!double.IsNaN(InnerValue))
        {
            const double innerThickness = 4;
            var innerRadius = outerRadius - Thickness / 2 - innerThickness / 2 - 3;
            dc.DrawEllipse(null, new Pen(TrackBrush, innerThickness), center, innerRadius, innerRadius);
            DrawArc(dc, center, innerRadius, InnerValue, RoundPen(InnerBrush, innerThickness));
        }

        var text = new FormattedText(
            $"{Math.Clamp(Value, 0, 100):0}%",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            side * 0.2,
            TextBrush ?? ValueBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static Pen RoundPen(Brush brush, double thickness) =>
        new(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    /// <summary>Draws a clockwise arc starting at 12 o'clock that covers the given percentage of the circle.</summary>
    private static void DrawArc(DrawingContext dc, Point center, double radius, double percent, Pen pen)
    {
        percent = Math.Clamp(double.IsNaN(percent) ? 0 : percent, 0, 100);
        if (percent <= 0)
        {
            return;
        }

        if (percent >= 99.95)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = percent / 100 * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(radius, radius), 0, isLargeArc: percent > 50, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(RingGauge),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}
