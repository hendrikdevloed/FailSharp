using FailSharp.Core;

namespace FailSharp;

public partial class App : System.Windows.Application
{
    private const string MutexName = "FailSharp-SingleInstance-3F1B2C4D-9E3B-4B7A-8F2E-6D1C0A5E7B2F";
    private const string ShowWindowEventName = "FailSharp-ShowWindow-3F1B2C4D-9E3B-4B7A-8F2E-6D1C0A5E7B2F";

    private readonly CancellationTokenSource _cts = new();
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private RegisteredWaitHandle? _showWindowWait;
    private SimConnectNetClient? _simVarClient;
    private NativeControlClient? _nativeControlClient;
    private FailureEngine? _engine;
    private TrayIcon? _trayIcon;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isNewInstance);
        if (!isNewInstance)
        {
            // Another instance is already running: ask it to show its window, then exit immediately
            // without touching SimConnect, settings, or the tray icon.
            if (EventWaitHandle.TryOpenExisting(ShowWindowEventName, out var existingSignal))
            {
                using (existingSignal)
                {
                    existingSignal.Set();
                }
            }

            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;
        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);

        var settings = SettingsStore.Load();
        var nativeControlClient = new NativeControlClient();
        var simVarClient = new SimConnectNetClient(nativeControlClient);
        var engine = new FailureEngine(simVarClient, settings);

        _simVarClient = simVarClient;
        _nativeControlClient = nativeControlClient;
        _engine = engine;

        var mainWindow = new MainWindow(engine, simVarClient, nativeControlClient);
        var trayIcon = new TrayIcon(mainWindow, nativeControlClient, engine);
        _trayIcon = trayIcon;

        _showWindowWait = ThreadPool.RegisterWaitForSingleObject(
            _showWindowEvent,
            (_, _) => mainWindow.Dispatcher.BeginInvoke(trayIcon.ShowMainWindow),
            null,
            -1,
            executeOnlyOnce: false);

        _ = RunConnectLoopAsync(simVarClient, nativeControlClient, _cts.Token);
        _ = engine.RunAsync(_cts.Token);
    }

    private static async Task RunConnectLoopAsync(SimConnectNetClient simVarClient, NativeControlClient nativeControlClient, CancellationToken ct)
    {
        // SimConnectNetClient auto-reconnects internally once connected; ConnectAsync just needs
        // to eventually succeed the first time. NativeControlClient has no auto-reconnect, so it's retried here.
        while (!ct.IsCancellationRequested)
        {
            if (!simVarClient.IsConnected)
            {
                try
                {
                    await simVarClient.ConnectAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Sim not running yet; retry on the next loop iteration.
                }
            }

            if (!nativeControlClient.IsConnected)
            {
                nativeControlClient.Connect();
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _cts.Cancel();

        try
        {
            // Leave the aircraft in a flyable state rather than making the player reload to clear
            // a failure just because they closed the tool.
            _engine?.RepairAllAsync().Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Not connected, or a transient SimConnect error — nothing to repair on the way out.
        }

        _showWindowWait?.Unregister(null);
        _showWindowEvent?.Dispose();
        _trayIcon?.Dispose();
        _nativeControlClient?.Dispose();
        _simVarClient?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));

        if (_singleInstanceMutex is not null)
        {
            if (_ownsSingleInstanceMutex)
            {
                _singleInstanceMutex.ReleaseMutex();
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
