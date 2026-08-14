using System.Runtime.InteropServices;

namespace FailSharp;

/// <summary>
/// Minimal P/Invoke surface over the native SimConnect.dll, covering only what
/// SimConnect.NET doesn't expose: the Add-ons menu API. See C:\MSFS SDK\SimConnect SDK\include\SimConnect.h
/// for the authoritative signatures and SIMCONNECT_RECV struct layouts this mirrors.
/// </summary>
internal static class NativeSimConnect
{
    private const string DllName = "SimConnect.dll";

    // SIMCONNECT_RECV_ID values (declaration order in SimConnect.h == numeric value).
    public const uint RecvIdException = 1;
    public const uint RecvIdOpen = 2;
    public const uint RecvIdQuit = 3;
    public const uint RecvIdEvent = 4;

    [DllImport(DllName, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_Open(out IntPtr phSimConnect, string szName, IntPtr hWnd, uint userEventWin32, IntPtr hEventHandle, uint configIndex);

    [DllImport(DllName)]
    public static extern int SimConnect_Close(IntPtr hSimConnect);

    [DllImport(DllName, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_MenuAddItem(IntPtr hSimConnect, string szMenuItem, uint menuEventId, uint dwData);

    [DllImport(DllName)]
    public static extern int SimConnect_MenuDeleteItem(IntPtr hSimConnect, uint menuEventId);

    [DllImport(DllName)]
    public static extern int SimConnect_GetNextDispatch(IntPtr hSimConnect, out IntPtr ppData, out uint pcbData);

    /// <summary>SIMCONNECT_RECV.dwID is the third DWORD in the struct (offset 8).</summary>
    public static uint ReadRecvId(IntPtr data) => unchecked((uint)Marshal.ReadInt32(data, 8));

    /// <summary>SIMCONNECT_RECV_EVENT.uEventID is the second DWORD after the SIMCONNECT_RECV header (offset 16).</summary>
    public static uint ReadEventId(IntPtr data) => unchecked((uint)Marshal.ReadInt32(data, 16));
}
