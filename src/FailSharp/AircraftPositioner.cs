using System.Collections.Concurrent;
using FailSharp.Core;

namespace FailSharp;

/// <summary>
/// Finds the nearest airport with a usable runway, picks the runway end the current wind favours,
/// and puts the user aircraft into flight at the requested pattern position.
/// </summary>
public sealed class AircraftPositioner
{
    /// <summary>How many of the nearest airports to try before giving up (the closest may be a heliport or water-only).</summary>
    private const int MaxAirportsTried = 8;

    private static readonly TimeSpan EngineCheckDelay = TimeSpan.FromMilliseconds(750);

    private readonly SimConnectNetClient _simVarClient;
    private readonly NativeControlClient _nativeClient;
    private readonly FailureEngine _failureEngine;
    private readonly ConcurrentDictionary<string, AirportInfo> _airportCache = new();

    /// <summary>Last landing end used per airport, for hysteresis across clicks (not persisted: it's about the current weather).</summary>
    private readonly ConcurrentDictionary<string, RunwayReference> _lastEnds = new();

    public AircraftPositioner(SimConnectNetClient simVarClient, NativeControlClient nativeClient, FailureEngine failureEngine)
    {
        _simVarClient = simVarClient;
        _nativeClient = nativeClient;
        _failureEngine = failureEngine;
    }

    public bool IsAvailable => _simVarClient.IsConnected && _nativeClient.IsConnected;

    /// <returns>A short human-readable description of where the aircraft was placed.</returns>
    public async Task<string> PlaceAsync(PositionTarget target, double heightAglFt, double speedKts, bool rightHandPattern, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Not connected to the simulator.");
        }

        var (lat, lon, windFrom, windKts) = await _simVarClient.GetPositionAndWindAsync(ct).ConfigureAwait(false);
        var atcHeading = await _simVarClient.GetAtcRunwayHeadingAsync(ct).ConfigureAwait(false);
        var atcReference = atcHeading is { } h ? new RunwayReference(h, null, "ATC") : null;
        var airports = await _nativeClient.GetNearbyAirportsAsync(ct).ConfigureAwait(false);

        foreach (var candidate in airports
                     .Where(a => !string.IsNullOrEmpty(a.Ident))
                     .OrderBy(a => Geo.DistanceNm(lat, lon, a.Latitude, a.Longitude))
                     .Take(MaxAirportsTried))
        {
            var airport = await GetAirportCachedAsync(candidate, ct).ConfigureAwait(false);
            _lastEnds.TryGetValue(airport.Ident, out var lastReference);
            var selection = PatternPlanner.ChooseLandingEnd(airport.Runways, windFrom, windKts, atcReference, lastReference);
            if (selection is null)
            {
                continue;
            }

            var end = selection.End;
            _lastEnds[airport.Ident] = new RunwayReference(end.LandingHeadingTrue, end.Name, "kept");

            // Clear any (simulated or sim-native) engine failure first so the aircraft arrives healthy.
            await _failureEngine.RepairAllAsync(ct).ConfigureAwait(false);

            var placement = PatternPlanner.Place(target, airport, end, heightAglFt, rightHandPattern);
            if (!_nativeClient.SetUserAircraftPosition(placement, speedKts))
            {
                throw new InvalidOperationException("The simulator rejected the new position.");
            }

            var started = await EnsureEnginesRunningAsync(ct).ConfigureAwait(false);

            var wind = windKts < 1 ? "calm" : $"{Geo.NormalizeDegrees(windFrom):000}°/{windKts:0} kt";
            var where = target switch
            {
                PositionTarget.Overhead => "overhead",
                PositionTarget.Downwind => $"downwind {end.Name}",
                PositionTarget.Base => $"base {end.Name}",
                _ => $"final {end.Name}",
            };
            var why = selection.Reason == "wind" ? string.Empty : $" ({selection.Reason})";
            return $"{airport.Ident} {where}{why} · wind {wind}" + (started ? " · engine start" : string.Empty);
        }

        throw new InvalidOperationException("No airport with a usable runway found nearby.");
    }

    /// <summary>Triggers the sim's auto-start if any engine isn't combusting. Returns true if a start was requested.</summary>
    private async Task<bool> EnsureEnginesRunningAsync(CancellationToken ct)
    {
        // Let the repositioning settle before sampling engine state.
        await Task.Delay(EngineCheckDelay, ct).ConfigureAwait(false);

        var engineCount = await _simVarClient.GetEngineCountAsync(ct).ConfigureAwait(false);
        for (var i = 1; i <= engineCount; i++)
        {
            if (!await _simVarClient.IsEngineRunningAsync(i, ct).ConfigureAwait(false))
            {
                // Auto-start handles fuel, mixture, magnetos/ignition and starter for every engine at once.
                return _nativeClient.AutoStartEngines();
            }
        }

        return false;
    }

    private async Task<AirportInfo> GetAirportCachedAsync(AirportListEntry entry, CancellationToken ct)
    {
        var key = entry.Ident + "|" + entry.Region;
        if (_airportCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var airport = await _nativeClient.GetAirportAsync(entry.Ident, entry.Region, ct).ConfigureAwait(false);
        if (airport.Latitude == 0 && airport.Longitude == 0)
        {
            // No airport record in the reply; fall back to the facility list's reference point.
            airport = airport with
            {
                Latitude = entry.Latitude,
                Longitude = entry.Longitude,
                ElevationFt = entry.AltitudeMeters * Geo.FeetPerMeter,
            };
        }

        _airportCache[key] = airport;
        return airport;
    }
}
