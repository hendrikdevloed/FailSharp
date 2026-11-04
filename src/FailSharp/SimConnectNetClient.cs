using FailSharp.Core;
using SimConnect.NET;

namespace FailSharp;

/// <summary>
/// ISimVarClient backed by the SimConnect.NET package for reads, and NativeControlClient for the
/// actual fail/repair trigger (SimConnect.NET has no client-event mapping API, and "GENERAL ENG
/// FAILED" is readable but not settable — see NativeControlClient). Auto-reconnects on its own.
/// </summary>
public sealed class SimConnectNetClient : ISimVarClient, IAsyncDisposable
{
    private readonly SimConnectClient _client = new("FailSharp");
    private readonly NativeControlClient _nativeControlClient;

    public SimConnectNetClient(NativeControlClient nativeControlClient)
    {
        _nativeControlClient = nativeControlClient;
        _client.AutoReconnectEnabled = true;
        _client.ConnectionStatusChanged += (_, _) => ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsConnected => _client.IsConnected;

    public event EventHandler? ConnectionStatusChanged;

    public Task ConnectAsync(CancellationToken ct = default) => _client.ConnectAsync(cancellationToken: ct);

    public async Task<int> GetEngineCountAsync(CancellationToken ct = default) =>
        await _client.SimVars.GetAsync<int>("NUMBER OF ENGINES", "number", cancellationToken: ct).ConfigureAwait(false);

    public async Task<double> GetIndicatedAirspeedAsync(CancellationToken ct = default) =>
        await _client.SimVars.GetAsync<double>("AIRSPEED INDICATED", "knots", cancellationToken: ct).ConfigureAwait(false);

    /// <summary>User aircraft position and the ambient wind at it (direction the wind blows FROM, degrees true).</summary>
    public async Task<(double Latitude, double Longitude, double WindFromTrue, double WindKts)> GetPositionAndWindAsync(CancellationToken ct = default)
    {
        var lat = await _client.SimVars.GetAsync<double>("PLANE LATITUDE", "degrees", cancellationToken: ct).ConfigureAwait(false);
        var lon = await _client.SimVars.GetAsync<double>("PLANE LONGITUDE", "degrees", cancellationToken: ct).ConfigureAwait(false);
        var windDir = await _client.SimVars.GetAsync<double>("AMBIENT WIND DIRECTION", "degrees", cancellationToken: ct).ConfigureAwait(false);
        var windKts = await _client.SimVars.GetAsync<double>("AMBIENT WIND VELOCITY", "knots", cancellationToken: ct).ConfigureAwait(false);
        return (lat, lon, windDir, windKts);
    }

    public async Task<bool> IsEngineRunningAsync(int engineIndex, CancellationToken ct = default) =>
        await _client.SimVars.GetAsync<int>($"GENERAL ENG COMBUSTION:{engineIndex}", "Bool", cancellationToken: ct).ConfigureAwait(false) != 0;

    public async Task<bool> IsEngineFailedAsync(int engineIndex, CancellationToken ct = default) =>
        await _client.SimVars.GetAsync<int>($"GENERAL ENG FAILED:{engineIndex}", "Bool", cancellationToken: ct).ConfigureAwait(false) != 0;

    public async Task FailEngineAsync(int engineIndex, CancellationToken ct = default)
    {
        if (await IsEngineFailedAsync(engineIndex, ct).ConfigureAwait(false))
        {
            return; // already failed; the sim event is a toggle, not a set
        }

        _nativeControlClient.ToggleEngineFailure(engineIndex);
    }

    public async Task RepairEngineAsync(int engineIndex, CancellationToken ct = default)
    {
        if (!await IsEngineFailedAsync(engineIndex, ct).ConfigureAwait(false))
        {
            return; // already healthy
        }

        _nativeControlClient.ToggleEngineFailure(engineIndex);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
