using FailSharp.Core;
using SimConnect.NET;

namespace FailSharp;

/// <summary>ISimVarClient backed by the SimConnect.NET package. Auto-reconnects on its own.</summary>
public sealed class SimConnectNetClient : ISimVarClient, IAsyncDisposable
{
    private readonly SimConnectClient _client = new("FailSharp");

    public SimConnectNetClient()
    {
        _client.AutoReconnectEnabled = true;
        _client.ConnectionStatusChanged += (_, _) => ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsConnected => _client.IsConnected;

    public event EventHandler? ConnectionStatusChanged;

    public Task ConnectAsync(CancellationToken ct = default) => _client.ConnectAsync(cancellationToken: ct);

    public async Task<int> GetEngineCountAsync(CancellationToken ct = default) =>
        await _client.SimVars.GetAsync<int>("NUMBER OF ENGINES", "number", cancellationToken: ct).ConfigureAwait(false);

    public async Task<bool> IsEngineFailedAsync(int engineIndex, CancellationToken ct = default) =>
        await _client.SimVars.GetAsync<int>($"GENERAL ENG FAILED:{engineIndex}", "Bool", cancellationToken: ct).ConfigureAwait(false) != 0;

    public Task FailEngineAsync(int engineIndex, CancellationToken ct = default) =>
        _client.SimVars.SetAsync($"GENERAL ENG FAILED:{engineIndex}", "Bool", 1, cancellationToken: ct);

    public Task RepairEngineAsync(int engineIndex, CancellationToken ct = default) =>
        _client.SimVars.SetAsync($"GENERAL ENG FAILED:{engineIndex}", "Bool", 0, cancellationToken: ct);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
