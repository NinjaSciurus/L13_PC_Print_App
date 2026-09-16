using System.Runtime.InteropServices;

namespace L13.Core.Bluetooth;

/// <summary>
/// Minimal synchronous RFCOMM (Serial Port Profile) client over Winsock.
///
/// <para>
/// The lifecycle mirrors a plain TCP client: <c>WSAStartup</c> → <c>socket</c> →
/// (set timeout) → <c>connect</c> → <c>send</c>/<c>recv</c> → <c>closesocket</c> →
/// <c>WSACleanup</c>. The only Bluetooth-specific parts are the address family
/// (<see cref="NativeMethods.AF_BTH"/>), the protocol (RFCOMM) and the socket
/// address (<see cref="NativeMethods.SOCKADDR_BTH"/>).
/// </para>
///
/// <para>
/// All calls block. Callers must run them off the UI thread — see
/// <see cref="Printing.L13LabelPrinter"/>, which wraps everything in
/// <see cref="System.Threading.Tasks.Task.Run(System.Action)"/>.
/// </para>
/// </summary>
public sealed class RfcommClient : IDisposable
{
    private IntPtr _socket = NativeMethods.INVALID_SOCKET;
    private bool _wsaStarted;

    /// <summary>
    /// Opens an RFCOMM connection to <paramref name="btAddr"/> on the given channel.
    /// </summary>
    /// <param name="btAddr">Device MAC packed into a ulong (see <see cref="BluetoothAddress"/>).</param>
    /// <param name="channel">RFCOMM channel (the L13 uses 1).</param>
    /// <param name="recvTimeoutMs">Blocking receive timeout in milliseconds.</param>
    /// <exception cref="IOException">Any step of the Winsock sequence failed.</exception>
    public void Connect(ulong btAddr, int channel, int recvTimeoutMs)
    {
        // 1) Winsock init. 0x0202 = request version 2.2. The 512-byte buffer is
        //    just scratch space for the WSADATA struct the call fills in.
        var wsaData = new byte[512];
        int startup = NativeMethods.WSAStartup(0x0202, wsaData);
        if (startup != 0)
            throw new IOException($"WSAStartup failed ({startup}).");
        _wsaStarted = true;

        // 2) Create a Bluetooth RFCOMM stream socket.
        _socket = NativeMethods.socket(NativeMethods.AF_BTH, NativeMethods.SOCK_STREAM, NativeMethods.BTHPROTO_RFCOMM);
        if (_socket == NativeMethods.INVALID_SOCKET)
            throw new IOException($"socket() failed. WSA error {NativeMethods.WSAGetLastError()}.");

        // 3) Apply a receive timeout so a silent printer can't hang us forever.
        int timeout = recvTimeoutMs;
        NativeMethods.setsockopt(_socket, NativeMethods.SOL_SOCKET, NativeMethods.SO_RCVTIMEO, ref timeout, sizeof(int));

        // 4) Build the Bluetooth socket address and connect.
        var addr = new NativeMethods.SOCKADDR_BTH
        {
            addressFamily = NativeMethods.AF_BTH,
            btAddr = btAddr,
            serviceClassId = NativeMethods.ServiceClassId,
            port = (uint)channel,
        };

        int size = Marshal.SizeOf<NativeMethods.SOCKADDR_BTH>();
        if (NativeMethods.connect(_socket, ref addr, size) == NativeMethods.SOCKET_ERROR)
            throw new IOException($"connect() failed. WSA error {NativeMethods.WSAGetLastError()}.");
    }

    /// <summary>
    /// Sends the whole buffer. <c>send()</c> may accept fewer bytes than requested,
    /// so we loop from an advancing offset (using a pinned pointer to avoid copies)
    /// until everything is on the wire. Essential for multi-KB rasters.
    /// </summary>
    /// <exception cref="IOException"><c>send()</c> returned an error.</exception>
    public unsafe void SendAll(byte[] data)
    {
        int total = 0;
        fixed (byte* p = data)
        {
            while (total < data.Length)
            {
                int sent = NativeMethods.send(_socket, p + total, data.Length - total, 0);
                if (sent <= 0)
                    throw new IOException($"send() failed. WSA error {NativeMethods.WSAGetLastError()}.");
                total += sent;
            }
        }
    }

    /// <summary>
    /// Reads up to <paramref name="max"/> bytes. Returns an empty array on timeout
    /// or a closed connection (the printer answers most queries with a few bytes).
    /// </summary>
    public byte[] Receive(int max)
    {
        var buffer = new byte[max];
        int received = NativeMethods.recv(_socket, buffer, buffer.Length, 0);
        if (received <= 0)
            return [];
        if (received == max)
            return buffer;

        // Trim to the number of bytes actually received.
        var result = new byte[received];
        Array.Copy(buffer, result, received);
        return result;
    }

    /// <summary>Closes the socket and shuts down Winsock. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_socket != NativeMethods.INVALID_SOCKET)
        {
            NativeMethods.closesocket(_socket);
            _socket = NativeMethods.INVALID_SOCKET;
        }
        if (_wsaStarted)
        {
            NativeMethods.WSACleanup();
            _wsaStarted = false;
        }
    }
}
