namespace FailSharp.Core;

public sealed class FailureEngine
{
    private readonly ISimVarClient _client;
    private readonly Random _random;

    public FailureEngine(ISimVarClient client, FailureSettings settings, Random? random = null)
    {
        _client = client;
        Settings = settings;
        _random = random ?? Random.Shared;
    }

    /// <summary>Live settings; the UI mutates this instance directly and changes take effect on the next tick.</summary>
    public FailureSettings Settings { get; set; }

    /// <summary>Runs the check/fail loop until cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var intervalMinutes = Math.Max(0.1, Settings.CheckIntervalMinutes);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ConsiderFailureAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Transient SimConnect errors (e.g. sim reloading) shouldn't kill the loop.
            }
        }
    }

    /// <summary>Runs one probability check and, if it hits, fails a random eligible engine. Returns the engine that was failed, or null.</summary>
    public async Task<int?> ConsiderFailureAsync(CancellationToken ct = default)
    {
        var settings = Settings;
        if (!settings.Enabled || !_client.IsConnected)
        {
            return null;
        }

        var airspeed = await _client.GetIndicatedAirspeedAsync(ct).ConfigureAwait(false);
        if (airspeed < settings.MinSpeedKts || airspeed > settings.MaxSpeedKts)
        {
            return null;
        }

        if (_random.NextDouble() >= settings.ProbabilityPercent / 100.0)
        {
            return null;
        }

        var candidates = await GetFailableEnginesAsync(settings, ct).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return null;
        }

        var chosen = candidates[_random.Next(candidates.Count)];
        await _client.FailEngineAsync(chosen, ct).ConfigureAwait(false);
        return chosen;
    }

    public async Task RepairAllAsync(CancellationToken ct = default)
    {
        var engineCount = await _client.GetEngineCountAsync(ct).ConfigureAwait(false);
        for (var i = 1; i <= engineCount; i++)
        {
            await _client.RepairEngineAsync(i, ct).ConfigureAwait(false);
        }
    }

    private async Task<List<int>> GetFailableEnginesAsync(FailureSettings settings, CancellationToken ct)
    {
        var engineCount = await _client.GetEngineCountAsync(ct).ConfigureAwait(false);
        var candidates = new List<int>();
        for (var i = 1; i <= engineCount; i++)
        {
            if (!settings.EligibleEngines.Contains(i))
            {
                continue;
            }

            if (await _client.IsEngineFailedAsync(i, ct).ConfigureAwait(false))
            {
                continue;
            }

            candidates.Add(i);
        }

        return candidates;
    }
}
