using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace StreamNetMon.App.Controls;

public enum ChartUnit { Rate, Milliseconds, Percent }

public sealed record ChartPoint(DateTimeOffset At, double? Value);

public sealed record ChartSeries(Brush Stroke, IReadOnlyList<ChartPoint> Points);

public sealed record ChartBand(DateTimeOffset From, DateTimeOffset To, string Label);

/// <summary>A plain line chart over time, with optional shaded bands. Drawn once per data change, no animation.</summary>
public sealed class TimeChart : FrameworkElement
{
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Pen GridPen = Frozen(new Pen(Palette.Grid, 1));

    public static readonly DependencyProperty SeriesProperty = Register<IReadOnlyList<ChartSeries>?>(nameof(Series), null);
    public static readonly DependencyProperty BandsProperty = Register<IReadOnlyList<ChartBand>?>(nameof(Bands), null);
    public static readonly DependencyProperty FromProperty = Register(nameof(From), DateTimeOffset.MinValue);
    public static readonly DependencyProperty ToProperty = Register(nameof(To), DateTimeOffset.MinValue);
    public static readonly DependencyProperty UnitProperty = Register(nameof(Unit), ChartUnit.Milliseconds);
    public static readonly DependencyProperty TitleProperty = Register(nameof(Title), "");
    public static readonly DependencyProperty MinimumTopProperty = Register(nameof(MinimumTop), 1.0);

    public IReadOnlyList<ChartSeries>? Series { get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public IReadOnlyList<ChartBand>? Bands { get => (IReadOnlyList<ChartBand>?)GetValue(BandsProperty); set => SetValue(BandsProperty, value); }
    public DateTimeOffset From { get => (DateTimeOffset)GetValue(FromProperty); set => SetValue(FromProperty, value); }
    public DateTimeOffset To { get => (DateTimeOffset)GetValue(ToProperty); set => SetValue(ToProperty, value); }
    public ChartUnit Unit { get => (ChartUnit)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>The smallest value the top of the axis may have, so a flat quiet line does not fill the chart.</summary>
    public double MinimumTop { get => (double)GetValue(MinimumTopProperty); set => SetValue(MinimumTopProperty, value); }

    private static DependencyProperty Register<T>(string name, T initial) => DependencyProperty.Register(
        name, typeof(T), typeof(TimeChart), new FrameworkPropertyMetadata(initial, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override void OnRender(DrawingContext dc)
    {
        const double left = 64, right = 8, top = 18, bottom = 18;
        var plot = new Rect(left, top, Math.Max(0, ActualWidth - left - right), Math.Max(0, ActualHeight - top - bottom));
        if (plot.Width < 20 || plot.Height < 20) return;

        dc.DrawText(Label(Title, Palette.Text, 11), new Point(left, 0));

        var span = (To - From).TotalSeconds;
        var series = Series ?? [];
        var max = series.SelectMany(s => s.Points).Select(p => p.Value ?? 0).DefaultIfEmpty(0).Max();
        var axisTop = NiceCeiling(Math.Max(max, MinimumTop));

        for (var i = 0; i <= 2; i++)
        {
            var y = plot.Bottom - plot.Height * i / 2;
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var text = Label(FormatValue(axisTop * i / 2), Palette.Text, 10);
            dc.DrawText(text, new Point(plot.Left - text.Width - 6, y - text.Height / 2));
        }

        if (span <= 0) return;
        double X(DateTimeOffset t) => plot.Left + (t - From).TotalSeconds / span * plot.Width;
        double Y(double v) => plot.Bottom - Math.Min(v, axisTop) / axisTop * plot.Height;

        foreach (var band in Bands ?? [])
        {
            var x1 = Math.Max(plot.Left, X(band.From));
            var x2 = Math.Min(plot.Right, X(band.To));
            if (x2 <= x1) continue;
            dc.DrawRectangle(Palette.Band, null, new Rect(x1, plot.Top, x2 - x1, plot.Height));
            var text = Label(band.Label, Palette.Accent, 10);
            if (text.Width + 4 < x2 - x1) dc.DrawText(text, new Point(x1 + 2, plot.Top + 1));
        }

        var timeFormat = span > 86_400 ? "ddd HH:mm" : span > 600 ? "HH:mm" : "HH:mm:ss";
        for (var i = 0; i <= 4; i++)
        {
            var at = From.AddSeconds(span * i / 4).ToLocalTime();
            var text = Label(at.ToString(timeFormat, CultureInfo.CurrentCulture), Palette.Text, 10);
            var x = plot.Left + plot.Width * i / 4 - (i == 0 ? 0 : i == 4 ? text.Width : text.Width / 2);
            dc.DrawText(text, new Point(x, plot.Bottom + 2));
        }

        dc.PushClip(new RectangleGeometry(plot));
        foreach (var s in series)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                var drawing = false;
                foreach (var p in s.Points)
                {
                    if (p.Value is not { } v || p.At < From || p.At > To)
                    {
                        drawing = false;   // a gap: offline, or no data
                        continue;
                    }
                    var point = new Point(X(p.At), Y(v));
                    if (drawing) g.LineTo(point, true, false);
                    else g.BeginFigure(point, false, false);
                    drawing = true;
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, Frozen(new Pen(s.Stroke, 1.5)), geometry);
        }
        dc.Pop();
    }

    private string FormatValue(double v) => Unit switch
    {
        ChartUnit.Rate => Format.Rate(v),
        ChartUnit.Percent => Format.Pct(v),
        _ => Format.Ms(v),
    };

    /// <summary>Rounds up to 1, 2 or 5 times a power of ten so axis labels are readable.</summary>
    private static double NiceCeiling(double v)
    {
        if (v <= 0 || double.IsNaN(v) || double.IsInfinity(v)) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var step in (double[])[1, 2, 5, 10])
            if (v <= step * magnitude) return step * magnitude;
        return 10 * magnitude;
    }

    private FormattedText Label(string text, Brush brush, double size) => new(
        text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
