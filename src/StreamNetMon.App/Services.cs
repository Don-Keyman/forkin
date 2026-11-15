using System.IO;
using System.Net.Http;
using System.Net.Security;
using StreamNetMon.App.ViewModels;
using StreamNetMon.App.Windows;

namespace StreamNetMon.App;

/// <summary>Everything the app is made of, built once at startup.</summary>
public sealed class Services : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SocketsHttpHandler _handler;
    private Task? _loop;
    private string? _token;

    public Services()
    {
        DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamNetMon");
        Directory.CreateDirectory(DataDirectory);

        SettingsStore = new SettingsStore(DataDirectory);
        Settings = SettingsStore.Load();
        Protector = new DpapiProtector();
        _token = Settings.ProtectedToken is { } stored ? Protector.Unprotect(stored) : null;

        Clock = new SystemClock();
        Network = new WindowsNetwork();
        History = new HistoryStore(Path.Combine(DataDirectory, "history.db"));

        _handler = new SocketsHttpHandler
        {
            // Only ever this PC, and without the two-second wait on the loopback address
            // the server is not listening on.
            ConnectCallback = (context, ct) => LoopbackConnector.ConnectAsync(context.DnsEndPoint, ct),
            SslOptions = new SslClientAuthenticationOptions
            {
                // The server's certificate is self-signed. The connection can only reach this PC, so accept it.
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
            UseProxy = false,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        Server = new StreamServerLink(_handler, Clock, () => Settings.ServerMode, () => Settings.ServerPort, () => _token);
        Engine = new MonitorEngine(Settings, new IcmpPinger(), Network, Server, Network, History, Clock);
        Live = new LiveViewModel();
    }

    public string DataDirectory { get; }
    public SettingsStore SettingsStore { get; }
    public AppSettings Settings { get; }
    public ISecretProtector Protector { get; }
    public IClock Clock { get; }
    public WindowsNetwork Network { get; }
    public HistoryStore History { get; }
    public StreamServerLink Server { get; }
    public MonitorEngine Engine { get; }
    public LiveViewModel Live { get; }

    public bool HasToken => !string.IsNullOrEmpty(_token);

    /// <summary>Stores a new access token (or clears it with null) and saves settings.</summary>
    public void SetToken(string? token)
    {
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        Settings.ProtectedToken = _token is null ? null : Protector.Protect(_token);
        SettingsStore.Save(Settings);
    }

    public string? TokenHint => _token is { Length: > 4 } t ? "…" + t[^4..] : _token is null ? null : "…";

    public void Start()
    {
        _loop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                do
                {
                    try
                    {
                        await Engine.TickAsync(_stop.Token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        Log.Write(DataDirectory, e);
                    }
                } while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }
        History.Dispose();
        _handler.Dispose();
        _stop.Dispose();
    }
}

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string directory, Exception e)
    {
        try
        {
            lock (Gate)
            {
                var path = Path.Combine(directory, "log.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Delete(path);
                File.AppendAllText(path, $"{DateTimeOffset.Now:u} {e}\n\n");
            }
        }
        catch (IOException)
        {
        }
    }
}
