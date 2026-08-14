using FailSharp.Core;

namespace FailSharp;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _cts = new();
    private SimConnectNetClient? _simVarClient;
    private NativeControlClient? _nativeControlClient;
    private FailureEngine? _engine;
    private TrayIcon? _trayIcon;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = SettingsStore.Load();
        var nativeControlClient = new NativeControlClient();
        var simVarClient = new SimConnectNetClient(nativeControlClient);
        var engine = new FailureEngine(simVarClient, settings);

        _simVarClient = simVarClient;
        _nativeControlClient = nativeControlClient;
        _engine = engine;

        var mainWindow = new MainWindow(engine, simVarClient, nativeControlClient);
        _trayIcon = new TrayIcon(mainWindow, nativeControlClient, engine);

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
        _trayIcon?.Dispose();
        _nativeControlClient?.Dispose();
        _simVarClient?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        base.OnExit(e);
    }
}
