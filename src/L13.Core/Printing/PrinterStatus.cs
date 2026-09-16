namespace L13.Core.Printing;

/// <summary>Decoded 10 FF 40 status bitmask.</summary>
public sealed class PrinterStatus
{
    public bool Printing { get; init; }
    public bool CoverOpen { get; init; }
    public bool OutOfPaper { get; init; }
    public bool LowBattery { get; init; }
    public bool Charging { get; init; }
    public bool Overheated { get; init; }

    public bool IsReady => !(Printing || CoverOpen || OutOfPaper || LowBattery || Overheated);

    public static PrinterStatus FromByte(byte b) => new()
    {
        Printing = (b & 0x01) != 0,
        CoverOpen = (b & 0x02) != 0,
        OutOfPaper = (b & 0x04) != 0,
        LowBattery = (b & 0x08) != 0,
        Charging = (b & 0x20) != 0,
        Overheated = (b & 0x40) != 0,
    };

    public override string ToString()
    {
        if (IsReady) return "Ready";
        var flags = new List<string>();
        if (Printing) flags.Add("printing");
        if (CoverOpen) flags.Add("cover open");
        if (OutOfPaper) flags.Add("out of paper");
        if (LowBattery) flags.Add("low battery");
        if (Overheated) flags.Add("overheated");
        if (Charging) flags.Add("charging");
        return string.Join(", ", flags);
    }
}

/// <summary>Progress callback payload for a print job.</summary>
public readonly record struct PrintProgress(string Phase, int Current, int Total);

/// <summary>Result of an info query (model / firmware / serial / battery / status).</summary>
public readonly record struct DeviceInfo(string Model, string Firmware, string Serial, int BatteryPercent, PrinterStatus Status);
