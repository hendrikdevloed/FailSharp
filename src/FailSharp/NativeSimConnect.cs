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

    [DllImport(DllName, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_MapClientEventToSimEvent(IntPtr hSimConnect, uint eventId, string eventName);

    /// <summary>ObjectID 0 == SIMCONNECT_OBJECT_ID_USER (the user's own aircraft).</summary>
    [DllImport(DllName)]
    public static extern int SimConnect_TransmitClientEvent(IntPtr hSimConnect, uint objectId, uint eventId, uint data, uint groupId, uint flags);

    public const uint RecvIdAirportList = 18;
    public const uint RecvIdFacilityData = 28;
    public const uint RecvIdFacilityDataEnd = 29;

    public const uint FacilityListTypeAirport = 0;
    public const uint FacilityDataTypeAirport = 0;
    public const uint FacilityDataTypeRunway = 1;
    public const uint DataTypeInitPosition = 12;
    public const uint Unused = 0xFFFFFFFF;

    [DllImport(DllName, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_AddToFacilityDefinition(IntPtr hSimConnect, uint defineId, string fieldName);

    [DllImport(DllName)]
    public static extern int SimConnect_RequestFacilitiesList(IntPtr hSimConnect, uint type, uint requestId);

    [DllImport(DllName, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_RequestFacilityData(IntPtr hSimConnect, uint defineId, uint requestId, string icao, string region);

    [DllImport(DllName, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_AddToDataDefinition(IntPtr hSimConnect, uint defineId, string datumName, string? unitsName, uint datumType, float epsilon, uint datumId);

    [DllImport(DllName)]
    public static extern int SimConnect_SetDataOnSimObject(IntPtr hSimConnect, uint defineId, uint objectId, uint flags, uint arrayCount, uint cbUnitSize, ref InitPosition data);

    /// <summary>SIMCONNECT_DATA_INITPOSITION (SimConnect.h is #pragma pack(1)).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InitPosition
    {
        public double Latitude;
        public double Longitude;
        public double Altitude; // feet MSL
        public double Pitch;
        public double Bank;
        public double Heading; // degrees true
        public uint OnGround;
        public uint Airspeed; // knots
    }

    public static uint ReadUInt(IntPtr data, int offset) => unchecked((uint)Marshal.ReadInt32(data, offset));

    public static int ReadInt(IntPtr data, int offset) => Marshal.ReadInt32(data, offset);

    public static double ReadDouble(IntPtr data, int offset) => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, offset));

    public static float ReadFloat(IntPtr data, int offset) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, offset));

    public static string ReadFixedString(IntPtr data, int offset, int length)
    {
        var bytes = new byte[length];
        Marshal.Copy(data + offset, bytes, 0, length);
        var end = Array.IndexOf(bytes, (byte)0);
        return System.Text.Encoding.ASCII.GetString(bytes, 0, end < 0 ? length : end).Trim();
    }

    /// <summary>SIMCONNECT_RECV.dwID is the third DWORD in the struct (offset 8).</summary>
    public static uint ReadRecvId(IntPtr data) => unchecked((uint)Marshal.ReadInt32(data, 8));

    /// <summary>SIMCONNECT_RECV_EVENT.uEventID is the second DWORD after the SIMCONNECT_RECV header (offset 16).</summary>
    public static uint ReadEventId(IntPtr data) => unchecked((uint)Marshal.ReadInt32(data, 16));
}
