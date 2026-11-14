using System.Windows;

namespace StreamNetMon.App;

public partial class App : System.Windows.Application
{
    private Mutex? _single;
    private Services? _services;
    private Tray? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _single = new Mutex(true, @"Local\StreamNetMon.SingleInstance", out var first);
        if (!first)
        {
            MessageBox.Show("Stream network monitor is already running. Look for its icon near the clock.",
                "Stream network monitor", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _services = new Services();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Stream network monitor could not start:\n\n" + ex.Message,
                "Stream network monitor", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write(_services.DataDirectory, args.Exception);
            args.Handled = true;
        };

        _window = new MainWindow(_services);
        _tray = new Tray(OpenWindow, Quit);
        _services.Engine.Updated += OnUpdated;
        _services.Start();

        if (!e.Args.Contains("--minimized")) OpenWindow();
    }

    private void OnUpdated(Snapshot snapshot)
    {
        // Raised on the engine's thread once a second.
        Dispatcher.BeginInvoke(() =>
        {
            if (_services is null || _window is null) return;
            _services.Live.Collect(snapshot);
            _tray?.Show(snapshot.Verdict);
            if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
                _services.Live.Show(snapshot, _services.Settings.Thresholds);
        });
    }

    private void OpenWindow()
    {
        if (_window is null || _services is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _services.Live.Show(_services.Engine.Current, _services.Settings.Thresholds);
    }

    private void Quit()
    {
        if (_window is not null)
        {
            _window.ReallyClose = true;
            _window.Close();
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _services?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
