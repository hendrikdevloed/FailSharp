namespace FailSharp.Core;

public interface ISimVarClient
{
    bool IsConnected { get; }

    event EventHandler? ConnectionStatusChanged;

    Task ConnectAsync(CancellationToken ct = default);

    Task<int> GetEngineCountAsync(CancellationToken ct = default);

    Task<bool> IsEngineFailedAsync(int engineIndex, CancellationToken ct = default);

    Task FailEngineAsync(int engineIndex, CancellationToken ct = default);

    Task RepairEngineAsync(int engineIndex, CancellationToken ct = default);
}
