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
        History.Attach(services);
        Devices.Attach(services);
        SettingsTab.Attach(services);
    }

    /// <summary>Refreshes whichever tab is showing. Called once a second while the window is visible.</summary>
    public void Show(Snapshot snapshot)
    {
        if (Tabs.SelectedIndex == 0) _services.Live.Show(snapshot, _services.Settings.Thresholds);
        else if (Devices.IsVisible) Devices.Show(snapshot);
        else if (SettingsTab.IsVisible) SettingsTab.Show(snapshot);
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
