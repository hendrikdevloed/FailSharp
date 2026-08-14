namespace FailSharp;

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
    private const uint MenuEventId = 1;
    private const uint EngineFailureEventIdBase = 10; // 11..14 for engines 1..4
    private const int MaxEngines = 4;
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

                    IsConnected = true;
                    ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
                    break;

                case NativeSimConnect.RecvIdEvent:
                    if (NativeSimConnect.ReadEventId(data) == MenuEventId)
                    {
                        MenuClicked?.Invoke(this, EventArgs.Empty);
                    }

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
