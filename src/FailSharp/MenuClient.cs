namespace FailSharp;

/// <summary>
/// A second, independent SimConnect connection whose only job is to add a "FailSharp Setup"
/// entry to the sim's Add-ons menu and notify us when it's clicked. Uses the event-driven
/// WaitHandle + ThreadPool.RegisterWaitForSingleObject pattern (no window handle, no polling)
/// per the community guidance on efficient managed SimConnect message loops.
/// </summary>
public sealed class MenuClient : IDisposable
{
    private const uint MenuEventId = 1;
    private const string MenuText = "FailSharp Setup";

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
        var hr = NativeSimConnect.SimConnect_Open(out var handle, "FailSharp Menu", IntPtr.Zero, 0, signal.SafeWaitHandle.DangerousGetHandle(), 0);
        if (hr < 0)
        {
            signal.Dispose();
            return;
        }

        _handle = handle;
        _signal = signal;
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(signal, OnSignaled, null, -1, executeOnlyOnce: false);
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
