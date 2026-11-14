using System.Windows;
using System.Windows.Media;

namespace StreamNetMon.App.Controls;

/// <summary>One thin bar per probe over the last 60 seconds; a lost probe is a full-height red bar.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<ProbeResult>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CeilingProperty = DependencyProperty.Register(
        nameof(Ceiling), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ProbeResult>? Samples
    {
        get => (IReadOnlyList<ProbeResult>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    /// <summary>The latency drawn as a full-height bar, at least.</summary>
    public double Ceiling
    {
        get => (double)GetValue(CeilingProperty);
        set => SetValue(CeilingProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var samples = Samples;
        if (samples is null || samples.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        const int slots = 60;
        var slot = ActualWidth / slots;
        var end = samples[^1].At;
        var top = Math.Max(Ceiling, samples.Max(s => s.RttMs ?? 0));

        foreach (var s in samples)
        {
            var age = (int)Math.Round((end - s.At).TotalSeconds);
            if (age is < 0 or >= slots) continue;
            var x = (slots - 1 - age) * slot;
            var height = s.RttMs is { } rtt ? Math.Max(3, rtt / top * ActualHeight) : ActualHeight;
            dc.DrawRectangle(s.RttMs is null ? Palette.Problem : Palette.Accent, null,
                new Rect(x, ActualHeight - height, Math.Max(1, slot - 0.5), height));
        }
    }
}
