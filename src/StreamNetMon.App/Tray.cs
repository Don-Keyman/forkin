using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace StreamNetMon.App;

/// <summary>The notification-area icon: coloured by the verdict, with Open and Exit.</summary>
public sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Dictionary<VerdictLevel, Drawing.Icon> _icons;
    private VerdictLevel? _level;
    private string? _text;

    public Tray(Action open, Action exit)
    {
        _icons = new()
        {
            [VerdictLevel.Ok] = Dot(Drawing.Color.FromArgb(0x2E, 0x9E, 0x5B)),
            [VerdictLevel.Warning] = Dot(Drawing.Color.FromArgb(0xE0, 0xA1, 0x00)),
            [VerdictLevel.Problem] = Dot(Drawing.Color.FromArgb(0xD6, 0x45, 0x45)),
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => open());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _icons[VerdictLevel.Ok],
            Text = "Stream network monitor",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    public void Show(Verdict verdict)
    {
        if (_level != verdict.Level)
        {
            _level = verdict.Level;
            _icon.Icon = _icons[verdict.Level];
        }

        // The tooltip is limited to 63 characters.
        var text = "Stream network monitor\n" + verdict.Message;
        if (text.Length > 63) text = text[..62] + "…";
        if (_text == text) return;
        _text = text;
        _icon.Text = text;
    }

    public void Notify(string message) =>
        _icon.ShowBalloonTip(5000, "Stream network monitor", message, Forms.ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var icon in _icons.Values)
        {
            DestroyIcon(icon.Handle);
            icon.Dispose();
        }
    }

    private static Drawing.Icon Dot(Drawing.Color color)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var brush = new Drawing.SolidBrush(color);
            g.FillEllipse(brush, 3, 3, 26, 26);
        }
        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
