using FailSharp.Core;

namespace FailSharp;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _cts = new();
    private SimConnectNetClient? _simVarClient;
    private MenuClient? _menuClient;
    private FailureEngine? _engine;
    private TrayIcon? _trayIcon;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = SettingsStore.Load();
        var simVarClient = new SimConnectNetClient();
        var menuClient = new MenuClient();
        var engine = new FailureEngine(simVarClient, settings);

        _simVarClient = simVarClient;
        _menuClient = menuClient;
        _engine = engine;

        var mainWindow = new MainWindow(engine, simVarClient, menuClient);
        _trayIcon = new TrayIcon(mainWindow, menuClient);

        _ = RunConnectLoopAsync(simVarClient, menuClient, _cts.Token);
        _ = engine.RunAsync(_cts.Token);
    }

    private static async Task RunConnectLoopAsync(SimConnectNetClient simVarClient, MenuClient menuClient, CancellationToken ct)
    {
        // SimConnectNetClient auto-reconnects internally once connected; ConnectAsync just needs
        // to eventually succeed the first time. MenuClient has no auto-reconnect, so it's retried here.
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

            if (!menuClient.IsConnected)
            {
                menuClient.Connect();
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
        _menuClient?.Dispose();
        _simVarClient?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        base.OnExit(e);
    }
}
