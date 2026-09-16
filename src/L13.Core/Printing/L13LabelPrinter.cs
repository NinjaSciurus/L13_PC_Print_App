using System.Text;
using L13.Core.Bluetooth;

namespace L13.Core.Printing;

/// <summary>
/// Orchestrates a print job over RFCOMM. The blocking Winsock work runs on a
/// thread-pool thread; progress is reported via <see cref="IProgress{T}"/> so
/// the UI thread stays responsive.
///
/// <para><b>L13 command reference</b> (reverse-engineered; see docs/PROTOCOL.md for the
/// full write-up). All multi-byte commands are sent verbatim over the RFCOMM stream:</para>
/// <list type="table">
///   <item><term>10 FF 40</term><description>Query status → 1 byte bitmask (0x00 = ready, 0x04 = out of paper)</description></item>
///   <item><term>10 FF 20 F0/F1/F2</term><description>Query model / firmware / serial → ASCII</description></item>
///   <item><term>10 FF 50 F1</term><description>Query battery → [status, percent]</description></item>
///   <item><term>10 FF 10 00 nn</term><description>Set density (0/1/2)</description></item>
///   <item><term>10 FF 84 00</term><description>Set gap/label paper mode</description></item>
///   <item><term>10 FF F1 03</term><description>Enable print mode (L13/Lujiang class)</description></item>
///   <item><term>1D 76 30 00 …</term><description>ESC/POS GS v 0 raster band</description></item>
///   <item><term>0C</term><description>Form feed: sensor seek to next label border</description></item>
///   <item><term>1B 4A nn</term><description>Feed nn dots (nn ≤ 255)</description></item>
///   <item><term>10 FF F1 45</term><description>Stop print job</description></item>
/// </list>
/// </summary>
public sealed class L13LabelPrinter
{
    public Task PrintAsync(
        IReadOnlyList<byte[]> bands,
        PrintOptions options,
        IProgress<PrintProgress>? progress = null,
        CancellationToken ct = default)
        => Task.Run(() => PrintCore(bands, options, progress, ct), ct);

    public Task<DeviceInfo> QueryInfoAsync(PrintOptions options, CancellationToken ct = default)
        => Task.Run(() => QueryInfo(options), ct);

    private static void PrintCore(
        IReadOnlyList<byte[]> bands, PrintOptions o, IProgress<PrintProgress>? progress, CancellationToken ct)
    {
        ulong addr = BluetoothAddress.Parse(o.BtAddr);

        using var client = new RfcommClient();
        Report(progress, "Connecting", 0, bands.Count);
        client.Connect(addr, o.Channel, o.RecvTimeoutMs);

        // Status
        client.SendAll([0x10, 0xFF, 0x40]);
        var statusBytes = client.Receive(16);
        if (statusBytes.Length == 0)
            throw new IOException("No status response from printer.");
        var status = PrinterStatus.FromByte(statusBytes[0]);
        if (status.OutOfPaper)
            throw new IOException("Out of paper.");

        // Optional config
        if (o.Density is >= 0 and <= 2)
        {
            client.SendAll([0x10, 0xFF, 0x10, 0x00, (byte)o.Density]);
            Thread.Sleep(60);
            client.Receive(16);
        }
        if (o.GapMode)
        {
            client.SendAll([0x10, 0xFF, 0x84, 0x00]);
            Thread.Sleep(60);
            client.Receive(16);
        }

        // Enable + wake
        client.SendAll([0x10, 0xFF, 0xF1, 0x03]);
        client.SendAll(new byte[12]);

        // Raster bands
        for (int i = 0; i < bands.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            client.SendAll(bands[i]);
            Report(progress, "Printing", i + 1, bands.Count);
            Thread.Sleep(15);
        }

        // Feed / position next label
        switch (o.FeedAfter)
        {
            case FeedMode.Gap:
                client.SendAll(FormFeedBytes(o.FormFeed));
                Thread.Sleep(900);
                client.Receive(8);
                break;
            case FeedMode.Label:
                client.SendAll([0x10, 0x0C]);
                Thread.Sleep(40);
                client.Receive(8);
                SendFeed(client, o.FeedDots);
                break;
            case FeedMode.Dots:
                SendFeed(client, o.FeedDots);
                break;
            case FeedMode.None:
            default:
                break;
        }

        int ejectDots = (int)Math.Round(o.EjectMm * o.Dpi / 25.4);
        if (ejectDots > 0)
            SendFeed(client, ejectDots);

        // Stop
        client.SendAll([0x10, 0xFF, 0xF1, 0x45]);
        Thread.Sleep(300);
        Report(progress, "Done", bands.Count, bands.Count);
    }

    private static DeviceInfo QueryInfo(PrintOptions o)
    {
        ulong addr = BluetoothAddress.Parse(o.BtAddr);
        using var client = new RfcommClient();
        client.Connect(addr, o.Channel, o.RecvTimeoutMs);

        string Ascii(byte[] cmd)
        {
            client.SendAll(cmd);
            Thread.Sleep(80);
            var r = client.Receive(32);
            return Encoding.ASCII.GetString(r).Trim();
        }

        string model = Ascii([0x10, 0xFF, 0x20, 0xF0]);
        string fw = Ascii([0x10, 0xFF, 0x20, 0xF1]);
        string serial = Ascii([0x10, 0xFF, 0x20, 0xF2]);

        client.SendAll([0x10, 0xFF, 0x50, 0xF1]);
        Thread.Sleep(80);
        var battery = client.Receive(8);
        int percent = battery.Length >= 2 ? battery[1] : -1;

        client.SendAll([0x10, 0xFF, 0x40]);
        Thread.Sleep(80);
        var st = client.Receive(8);
        var status = st.Length > 0 ? PrinterStatus.FromByte(st[0]) : new PrinterStatus();

        return new DeviceInfo(model, fw, serial, percent, status);
    }

    private static void SendFeed(RfcommClient client, int dots)
    {
        while (dots > 0)
        {
            int chunk = Math.Min(255, dots);
            client.SendAll([0x1B, 0x4A, (byte)chunk]);
            dots -= chunk;
            Thread.Sleep(20);
        }
    }

    private static byte[] FormFeedBytes(string name) => name switch
    {
        "1D0C" => [0x1D, 0x0C],
        "100C" => [0x10, 0x0C],
        _ => [0x0C],
    };

    private static void Report(IProgress<PrintProgress>? progress, string phase, int current, int total)
        => progress?.Report(new PrintProgress(phase, current, total));
}
