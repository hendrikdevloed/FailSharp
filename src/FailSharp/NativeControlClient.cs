using System.Runtime.InteropServices;
using FailSharp.Core;

namespace FailSharp;

public sealed record AirportListEntry(string Ident, string Region, double Latitude, double Longitude, double AltitudeMeters);

/// <summary>
/// A native SimConnect connection (P/Invoke against SimConnect.dll directly — see NativeSimConnect)
/// for the two things SimConnect.NET doesn't cover: the in-sim Add-ons menu entry, and transmitting
/// the classic TOGGLE_ENGINEn_FAILURE client events used to actually fail/repair an engine
/// ("GENERAL ENG FAILED" is readable but NOT settable — confirmed against the official SDK docs).
/// Uses the event-driven WaitHandle + ThreadPool.RegisterWaitForSingleObject pattern (no window
/// handle, no polling) per the community guidance on efficient managed SimConnect message loops.
/// </summary>
public sealed class NativeControlClient : IDisposable
{
    private const uint FacilityDefineId = 20;
    private const uint InitPositionDefineId = 21;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    // SIMCONNECT_RECV header is 12 bytes; FACILITIES_LIST adds dwRequestID, dwArraySize, dwEntryNumber, dwOutOf.
    private const int FacilitiesListHeaderSize = 28;

    // SIMCONNECT_RECV_FACILITY_DATA: header + UserRequestId, UniqueRequestId, ParentUniqueRequestId, Type, IsListItem, ItemIndex, ListSize.
    private const int FacilityDataPayloadOffset = 40;

    private readonly object _requestLock = new();
    private readonly Dictionary<uint, PendingAirportList> _pendingAirportLists = new();
    private readonly Dictionary<uint, PendingFacility> _pendingFacilities = new();
    private uint _nextRequestId = 100;

    private const uint MenuEventId = 1;
    private const uint EngineFailureEventIdBase = 10; // 11..14 for engines 1..4
    private const int MaxEngines = 4;
    private const uint EngineAutoStartEventId = 30;
    private const string MenuText = "FailSharp Setup";

    // Without GROUPID_IS_PRIORITY, TransmitClientEvent reports success but the event is never
    // actually routed anywhere (confirmed by direct testing) — it has to be sent at a real
    // priority for the sim's own systems to pick it up, per SimConnect.h's own usage example.
    private const uint SimconnectGroupPriorityHighest = 1;
    private const uint SimconnectEventFlagGroupidIsPriority = 0x00000010;

    private IntPtr _handle = IntPtr.Zero;
    private AutoResetEvent? _signal;
    private RegisteredWaitHandle? _registeredWait;
    private bool _disposed;

    public bool IsConnected { get; private set; }

    public event EventHandler? MenuClicked;

    public event EventHandler? ConnectionStatusChanged;

    /// <summary>Attempts to open the connection. Safe to call repeatedly (e.g. from a retry timer); it's a no-op once connected, and simply fails silently if the sim isn't up yet.</summary>
    public void Connect()
    {
        if (_disposed || IsConnected)
        {
            return;
        }

        var signal = new AutoResetEvent(false);
        var hr = NativeSimConnect.SimConnect_Open(out var handle, "FailSharp Control", IntPtr.Zero, 0, signal.SafeWaitHandle.DangerousGetHandle(), 0);
        if (hr < 0)
        {
            signal.Dispose();
            return;
        }

        _handle = handle;
        _signal = signal;
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(signal, OnSignaled, null, -1, executeOnlyOnce: false);
    }

    /// <summary>Flips an engine's failed state. Idempotent: no-op if already in the requested state (the underlying sim event is a toggle, not a set).</summary>
    public bool ToggleEngineFailure(int engineIndex)
    {
        if (!IsConnected || _handle == IntPtr.Zero || engineIndex is < 1 or > MaxEngines)
        {
            return false;
        }

        var eventId = EngineFailureEventIdBase + (uint)engineIndex;
        var hr = NativeSimConnect.SimConnect_TransmitClientEvent(_handle, 0, eventId, 0, SimconnectGroupPriorityHighest, SimconnectEventFlagGroupidIsPriority);
        return hr >= 0;
    }

    /// <summary>Runs the aircraft's auto-start procedure (the sim's Ctrl+E). Starts engines that aren't running; doesn't shut down running ones.</summary>
    public bool AutoStartEngines()
    {
        if (!IsConnected || _handle == IntPtr.Zero)
        {
            return false;
        }

        return NativeSimConnect.SimConnect_TransmitClientEvent(_handle, 0, EngineAutoStartEventId, 0, SimconnectGroupPriorityHighest, SimconnectEventFlagGroupidIsPriority) >= 0;
    }

    /// <summary>All airports currently in the sim's facility cache (roughly the area around the user aircraft).</summary>
    public async Task<IReadOnlyList<AirportListEntry>> GetNearbyAirportsAsync(CancellationToken ct = default)
    {
        var pending = new PendingAirportList();
        var requestId = Register(_pendingAirportLists, pending);
        if (!IsConnected || NativeSimConnect.SimConnect_RequestFacilitiesList(_handle, NativeSimConnect.FacilityListTypeAirport, requestId) < 0)
        {
            Unregister(_pendingAirportLists, requestId);
            throw new InvalidOperationException("Not connected to the simulator.");
        }

        return await AwaitWithTimeout(pending.Completion.Task, () => Unregister(_pendingAirportLists, requestId), ct).ConfigureAwait(false);
    }

    /// <summary>Position, elevation and runways of one airport, via the SimConnect facility data API.</summary>
    public async Task<AirportInfo> GetAirportAsync(string ident, string region, CancellationToken ct = default)
    {
        var pending = new PendingFacility(ident);
        var requestId = Register(_pendingFacilities, pending);
        if (!IsConnected || NativeSimConnect.SimConnect_RequestFacilityData(_handle, FacilityDefineId, requestId, ident, region) < 0)
        {
            Unregister(_pendingFacilities, requestId);
            throw new InvalidOperationException("Not connected to the simulator.");
        }

        return await AwaitWithTimeout(pending.Completion.Task, () => Unregister(_pendingFacilities, requestId), ct).ConfigureAwait(false);
    }

    /// <summary>Teleports the user aircraft into level flight at the given position, heading and airspeed.</summary>
    public bool SetUserAircraftPosition(Placement placement, double airspeedKts)
    {
        if (!IsConnected || _handle == IntPtr.Zero)
        {
            return false;
        }

        var init = new NativeSimConnect.InitPosition
        {
            Latitude = placement.Latitude,
            Longitude = placement.Longitude,
            Altitude = placement.AltitudeFtMsl,
            Pitch = 0,
            Bank = 0,
            Heading = placement.HeadingTrue,
            OnGround = 0,
            Airspeed = (uint)Math.Max(0, Math.Round(airspeedKts)),
        };

        var size = (uint)Marshal.SizeOf<NativeSimConnect.InitPosition>();
        return NativeSimConnect.SimConnect_SetDataOnSimObject(_handle, InitPositionDefineId, 0, 0, 0, size, ref init) >= 0;
    }

    private uint Register<T>(Dictionary<uint, T> map, T pending)
    {
        lock (_requestLock)
        {
            var id = _nextRequestId++;
            map[id] = pending;
            return id;
        }
    }

    private void Unregister<T>(Dictionary<uint, T> map, uint id)
    {
        lock (_requestLock)
        {
            map.Remove(id);
        }
    }

    private static async Task<T> AwaitWithTimeout<T>(Task<T> task, Action cleanup, CancellationToken ct)
    {
        try
        {
            return await task.WaitAsync(RequestTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("The simulator did not answer the facility request in time.");
        }
        finally
        {
            cleanup();
        }
    }

    private void RegisterDefinitions()
    {
        string[] facilityFields =
        {
            "OPEN AIRPORT", "LATITUDE", "LONGITUDE", "ALTITUDE", "N_RUNWAYS",
            "OPEN RUNWAY", "LATITUDE", "LONGITUDE", "ALTITUDE", "HEADING", "LENGTH",
            "PRIMARY_NUMBER", "PRIMARY_DESIGNATOR", "SECONDARY_NUMBER", "SECONDARY_DESIGNATOR",
            "CLOSE RUNWAY", "CLOSE AIRPORT",
        };
        foreach (var field in facilityFields)
        {
            NativeSimConnect.SimConnect_AddToFacilityDefinition(_handle, FacilityDefineId, field);
        }

        NativeSimConnect.SimConnect_AddToDataDefinition(_handle, InitPositionDefineId, "Initial Position", null, NativeSimConnect.DataTypeInitPosition, 0, NativeSimConnect.Unused);
    }

    private void HandleAirportList(IntPtr data)
    {
        var size = NativeSimConnect.ReadUInt(data, 0);
        var requestId = NativeSimConnect.ReadUInt(data, 12);
        var arraySize = NativeSimConnect.ReadUInt(data, 16);
        var entryNumber = NativeSimConnect.ReadUInt(data, 20);
        var outOf = NativeSimConnect.ReadUInt(data, 24);

        PendingAirportList? pending;
        lock (_requestLock)
        {
            _pendingAirportLists.TryGetValue(requestId, out pending);
        }

        if (pending is null)
        {
            return;
        }

        if (arraySize > 0)
        {
            // SIMCONNECT_DATA_FACILITY_AIRPORT: char ident[6]; char region[3]; double lat, lon, alt (packed).
            // The entry size is derived from the packet rather than hard-coded, so either SDK layout works.
            var entrySize = (int)((size - FacilitiesListHeaderSize) / arraySize);
            var charsSize = entrySize - 24;
            for (var i = 0; i < arraySize; i++)
            {
                var entry = FacilitiesListHeaderSize + i * entrySize;
                var doubles = entry + charsSize;
                var ident = NativeSimConnect.ReadFixedString(data, entry, Math.Min(6, charsSize));
                var region = charsSize >= 9 ? NativeSimConnect.ReadFixedString(data, entry + 6, 3) : string.Empty;
                pending.Airports.Add(new AirportListEntry(
                    ident,
                    region,
                    NativeSimConnect.ReadDouble(data, doubles),
                    NativeSimConnect.ReadDouble(data, doubles + 8),
                    NativeSimConnect.ReadDouble(data, doubles + 16)));
            }
        }

        if (outOf == 0 || entryNumber + 1 >= outOf)
        {
            pending.Completion.TrySetResult(pending.Airports);
        }
    }

    private void HandleFacilityData(IntPtr data)
    {
        var requestId = NativeSimConnect.ReadUInt(data, 12);
        var type = NativeSimConnect.ReadUInt(data, 24);

        PendingFacility? pending;
        lock (_requestLock)
        {
            _pendingFacilities.TryGetValue(requestId, out pending);
        }

        if (pending is null)
        {
            return;
        }

        // Fields arrive packed, in the order they were added in RegisterDefinitions.
        var p = FacilityDataPayloadOffset;
        if (type == NativeSimConnect.FacilityDataTypeAirport)
        {
            pending.Latitude = NativeSimConnect.ReadDouble(data, p);
            pending.Longitude = NativeSimConnect.ReadDouble(data, p + 8);
            pending.ElevationFt = NativeSimConnect.ReadDouble(data, p + 16) * Geo.FeetPerMeter;
        }
        else if (type == NativeSimConnect.FacilityDataTypeRunway)
        {
            pending.Runways.Add(new RunwayInfo(
                NativeSimConnect.ReadDouble(data, p),
                NativeSimConnect.ReadDouble(data, p + 8),
                NativeSimConnect.ReadDouble(data, p + 16) * Geo.FeetPerMeter,
                NativeSimConnect.ReadFloat(data, p + 24),
                NativeSimConnect.ReadFloat(data, p + 28) * Geo.FeetPerMeter,
                NativeSimConnect.ReadInt(data, p + 32),
                NativeSimConnect.ReadInt(data, p + 36),
                NativeSimConnect.ReadInt(data, p + 40),
                NativeSimConnect.ReadInt(data, p + 44)));
        }
    }

    private void HandleFacilityDataEnd(IntPtr data)
    {
        var requestId = NativeSimConnect.ReadUInt(data, 12);
        PendingFacility? pending;
        lock (_requestLock)
        {
            _pendingFacilities.TryGetValue(requestId, out pending);
        }

        pending?.Completion.TrySetResult(new AirportInfo(pending.Ident, pending.Latitude, pending.Longitude, pending.ElevationFt, pending.Runways));
    }

    private void OnSignaled(object? state, bool timedOut)
    {
        if (_disposed || _handle == IntPtr.Zero)
        {
            return;
        }

        while (NativeSimConnect.SimConnect_GetNextDispatch(_handle, out var data, out _) == 0 && data != IntPtr.Zero)
        {
            switch (NativeSimConnect.ReadRecvId(data))
            {
                case NativeSimConnect.RecvIdOpen:
                    NativeSimConnect.SimConnect_MenuAddItem(_handle, MenuText, MenuEventId, 0);
                    for (uint i = 1; i <= MaxEngines; i++)
                    {
                        NativeSimConnect.SimConnect_MapClientEventToSimEvent(_handle, EngineFailureEventIdBase + i, $"TOGGLE_ENGINE{i}_FAILURE");
                    }

                    NativeSimConnect.SimConnect_MapClientEventToSimEvent(_handle, EngineAutoStartEventId, "ENGINE_AUTO_START");
                    RegisterDefinitions();
                    IsConnected = true;
                    ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
                    break;

                case NativeSimConnect.RecvIdEvent:
                    if (NativeSimConnect.ReadEventId(data) == MenuEventId)
                    {
                        MenuClicked?.Invoke(this, EventArgs.Empty);
                    }

                    break;

                case NativeSimConnect.RecvIdAirportList:
                    HandleAirportList(data);
                    break;

                case NativeSimConnect.RecvIdFacilityData:
                    HandleFacilityData(data);
                    break;

                case NativeSimConnect.RecvIdFacilityDataEnd:
                    HandleFacilityDataEnd(data);
                    break;

                case NativeSimConnect.RecvIdQuit:
                    TearDown();
                    break;
            }
        }
    }

    private void TearDown()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeSimConnect.SimConnect_Close(_handle);
            _handle = IntPtr.Zero;
        }

        _registeredWait?.Unregister(null);
        _registeredWait = null;
        _signal?.Dispose();
        _signal = null;

        lock (_requestLock)
        {
            foreach (var pending in _pendingAirportLists.Values)
            {
                pending.Completion.TrySetCanceled();
            }

            foreach (var pending in _pendingFacilities.Values)
            {
                pending.Completion.TrySetCanceled();
            }

            _pendingAirportLists.Clear();
            _pendingFacilities.Clear();
        }

        if (IsConnected)
        {
            IsConnected = false;
            ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        TearDown();
    }
}

internal sealed class PendingAirportList
{
    public List<AirportListEntry> Airports { get; } = new();

    public TaskCompletionSource<IReadOnlyList<AirportListEntry>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class PendingFacility(string ident)
{
    public string Ident { get; } = ident;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double ElevationFt { get; set; }

    public List<RunwayInfo> Runways { get; } = new();

    public TaskCompletionSource<AirportInfo> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
