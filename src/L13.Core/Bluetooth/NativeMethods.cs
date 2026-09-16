using System.Runtime.InteropServices;

namespace L13.Core.Bluetooth;

/// <summary>
/// Winsock (<c>Ws2_32.dll</c>) P/Invoke surface for talking to the printer over a
/// <b>Classic Bluetooth RFCOMM</b> socket (Serial Port Profile).
///
/// <para>
/// Windows exposes Bluetooth RFCOMM as an ordinary stream socket, but in a
/// dedicated address family (<see cref="AF_BTH"/>) with its own socket-address
/// struct (<see cref="SOCKADDR_BTH"/>). Once connected, it behaves like any TCP
/// stream: <c>send</c>/<c>recv</c> move raw bytes, which is exactly what the L13's
/// ESC/POS-style command set needs.
/// </para>
///
/// <para>
/// This uses source-generated <see cref="LibraryImportAttribute"/> (the modern,
/// AOT-friendly replacement for <c>DllImport</c>). Every signature here is
/// blittable, so no custom marshalling is generated.
/// </para>
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>Address family for Bluetooth (<c>AF_BTH</c> in ws2bth.h).</summary>
    public const int AF_BTH = 32;

    /// <summary>Reliable, connection-oriented stream socket.</summary>
    public const int SOCK_STREAM = 1;

    /// <summary>RFCOMM protocol (<c>BTHPROTO_RFCOMM</c>).</summary>
    public const int BTHPROTO_RFCOMM = 3;

    /// <summary>Socket option level for generic socket options.</summary>
    public const int SOL_SOCKET = 0xFFFF;

    /// <summary>Receive-timeout option, in milliseconds (<c>SO_RCVTIMEO</c>).</summary>
    public const int SO_RCVTIMEO = 0x1006;

    /// <summary>Return value of a failed socket call.</summary>
    public const int SOCKET_ERROR = -1;

    /// <summary>Sentinel returned by <c>socket()</c> on failure (all bits set, i.e. -1).</summary>
    public static readonly IntPtr INVALID_SOCKET = new(-1);

    /// <summary>
    /// The RFCOMM / Serial Port Profile service-class UUID. Passing it in the
    /// socket address lets Windows resolve the RFCOMM channel via SDP when
    /// <see cref="SOCKADDR_BTH.port"/> is 0; we set the channel explicitly (1),
    /// but the field must still be a valid GUID.
    /// </summary>
    public static readonly Guid ServiceClassId = new("00001101-0000-1000-8000-00805F9B34FB");

    /// <summary>
    /// Bluetooth socket address (<c>SOCKADDR_BTH</c>). <c>Pack = 1</c> matches the
    /// unpadded native layout exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SOCKADDR_BTH
    {
        /// <summary>Must be <see cref="AF_BTH"/>.</summary>
        public ushort addressFamily;

        /// <summary>
        /// The 48-bit device MAC packed into a 64-bit integer. The value is the
        /// address read left-to-right as hex, e.g. MAC <c>55:55:09:22:3F:9B</c> =
        /// <c>0x0000_5555_0922_3F9B</c> (see <see cref="BluetoothAddress.Parse"/>).
        /// </summary>
        public ulong btAddr;

        /// <summary>Service class GUID (see <see cref="ServiceClassId"/>).</summary>
        public Guid serviceClassId;

        /// <summary>RFCOMM channel number (the L13 listens on channel 1).</summary>
        public uint port;
    }

    /// <summary>Initialises Winsock. <paramref name="lpWSAData"/> is a scratch buffer for the returned WSADATA.</summary>
    [LibraryImport("Ws2_32.dll")]
    public static partial int WSAStartup(ushort wVersionRequested, [Out] byte[] lpWSAData);

    /// <summary>Tears down the Winsock usage started by <see cref="WSAStartup"/>.</summary>
    [LibraryImport("Ws2_32.dll")]
    public static partial int WSACleanup();

    /// <summary>Returns the error code for the last failed Winsock call on this thread.</summary>
    [LibraryImport("Ws2_32.dll")]
    public static partial int WSAGetLastError();

    /// <summary>Creates a socket; returns <see cref="INVALID_SOCKET"/> on failure.</summary>
    [LibraryImport("Ws2_32.dll")]
    public static partial IntPtr socket(int af, int type, int protocol);

    /// <summary>Connects the socket to the address in <paramref name="name"/>.</summary>
    [LibraryImport("Ws2_32.dll")]
    public static partial int connect(IntPtr s, ref SOCKADDR_BTH name, int namelen);

    /// <summary>
    /// Sends up to <paramref name="len"/> bytes from <paramref name="buf"/>.
    /// Uses a raw pointer so the caller can resume a partial send from an offset
    /// without copying (see <see cref="RfcommClient.SendAll"/>).
    /// </summary>
    [LibraryImport("Ws2_32.dll")]
    public static unsafe partial int send(IntPtr s, byte* buf, int len, int flags);

    /// <summary>Receives up to <paramref name="len"/> bytes into <paramref name="buf"/>.</summary>
    [LibraryImport("Ws2_32.dll")]
    public static partial int recv(IntPtr s, [Out] byte[] buf, int len, int flags);

    /// <summary>Sets a socket option; used here to apply the receive timeout.</summary>
    [LibraryImport("Ws2_32.dll")]
    public static partial int setsockopt(IntPtr s, int level, int optname, ref int optval, int optlen);

    /// <summary>Closes the socket handle.</summary>
    [LibraryImport("Ws2_32.dll")]
    public static partial int closesocket(IntPtr s);
}
