using System.Windows.Media;

namespace StreamNetMon.App.Controls;

public static class Palette
{
    public static readonly Brush Ok = Frozen(0x2E, 0x9E, 0x5B);
    public static readonly Brush Warning = Frozen(0xE0, 0xA1, 0x00);
    public static readonly Brush Problem = Frozen(0xD6, 0x45, 0x45);
    public static readonly Brush Idle = Frozen(0x9A, 0xA0, 0xA6);
    public static readonly Brush Accent = Frozen(0x2F, 0x6F, 0xD0);
    public static readonly Brush Grid = Frozen(0xE3, 0xE6, 0xEA);
    public static readonly Brush Text = Frozen(0x5F, 0x66, 0x6D);
    public static readonly Brush Band = new SolidColorBrush(Color.FromArgb(0x30, 0x2F, 0x6F, 0xD0)).AsFrozen();

    public static Brush For(VerdictLevel level) => level switch
    {
        VerdictLevel.Ok => Ok,
        VerdictLevel.Warning => Warning,
        _ => Problem,
    };

    private static Brush Frozen(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b)).AsFrozen();

    private static Brush AsFrozen(this SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
