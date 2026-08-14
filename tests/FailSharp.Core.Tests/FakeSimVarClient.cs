using FailSharp.Core;

namespace FailSharp.Core.Tests;

internal sealed class FakeSimVarClient : ISimVarClient
{
    private readonly HashSet<int> _failed = new();

    public bool IsConnected { get; set; } = true;

    public int EngineCount { get; set; } = 2;

    public double IndicatedAirspeed { get; set; } = 100.0;

    public event EventHandler? ConnectionStatusChanged;

    public IReadOnlySet<int> FailedEngines => _failed;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<int> GetEngineCountAsync(CancellationToken ct = default) => Task.FromResult(EngineCount);

    public Task<double> GetIndicatedAirspeedAsync(CancellationToken ct = default) => Task.FromResult(IndicatedAirspeed);

    public Task<bool> IsEngineFailedAsync(int engineIndex, CancellationToken ct = default) =>
        Task.FromResult(_failed.Contains(engineIndex));

    public Task FailEngineAsync(int engineIndex, CancellationToken ct = default)
    {
        _failed.Add(engineIndex);
        return Task.CompletedTask;
    }

    public Task RepairEngineAsync(int engineIndex, CancellationToken ct = default)
    {
        _failed.Remove(engineIndex);
        return Task.CompletedTask;
    }
}
