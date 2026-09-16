namespace L13.Core.Printing;

public enum FeedMode
{
    None,
    /// <summary>Sensor seek to the next label border (confirmed on fw V3.08).</summary>
    Gap,
    /// <summary>Legacy 10 0C nudge + fixed feed.</summary>
    Label,
    /// <summary>Fixed feed only.</summary>
    Dots,
}

/// <summary>Connection + feed behaviour for a print job.</summary>
public sealed class PrintOptions
{
    public string BtAddr { get; set; } = "55:55:09:22:3F:9B";
    public int Channel { get; set; } = 1;
    public int RecvTimeoutMs { get; set; } = 4000;

    public int BandRows { get; set; } = 24;
    public int Dpi { get; set; } = 203;

    public FeedMode FeedAfter { get; set; } = FeedMode.Gap;

    /// <summary>"0C", "1D0C" or "100C" (used by <see cref="FeedMode.Gap"/>).</summary>
    public string FormFeed { get; set; } = "0C";
    public int FeedDots { get; set; } = 40;

    /// <summary>Tear feed after the seek; 6 mm matches the stock labels / iOS app.</summary>
    public double EjectMm { get; set; } = 6;

    /// <summary>-1 = leave as-is; 0/1/2 = light/medium/thick.</summary>
    public int Density { get; set; } = -1;

    /// <summary>Send 10 FF 84 00 (gap/label paper mode) before printing.</summary>
    public bool GapMode { get; set; }
}
