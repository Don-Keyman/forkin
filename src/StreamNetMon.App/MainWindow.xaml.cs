using System.ComponentModel;

namespace StreamNetMon.App;

public partial class MainWindow : System.Windows.Window
{
    private readonly Services _services;

    public MainWindow(Services services)
    {
        _services = services;
        InitializeComponent();
        Live.DataContext = services.Live;
    }

    /// <summary>Set by the tray's Exit command; otherwise closing only hides the window.</summary>
    public bool ReallyClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ReallyClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
