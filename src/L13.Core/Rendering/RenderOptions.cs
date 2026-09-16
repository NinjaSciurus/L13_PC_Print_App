namespace L13.Core.Rendering;

/// <summary>Everything the renderer needs to turn text (or an image) into a label raster.</summary>
public sealed class RenderOptions
{
    public string Text { get; set; } = "HELLO";
    public string FontName { get; set; } = "Arial";

    /// <summary>Fixed pixel size; 0 = auto-fit (never below <see cref="MinFontPx"/>).</summary>
    public double FontSize { get; set; }
    public double MinFontPx { get; set; } = 12;
    public bool Bold { get; set; } = true;

    /// <summary>0 / 90 / 180 / 270. Default 270 = upright on the L13 head.</summary>
    public int Rotate { get; set; } = 270;
    public bool Flip { get; set; }

    /// <summary>Printable BODY length in mm (label pitch minus the inter-label gap).</summary>
    public double LabelLengthMm { get; set; } = 28;
    public int HeadDots { get; set; } = 96;
    public int Dpi { get; set; } = 203;

    public int MarginDots { get; set; } = 8;
    public int SideMarginDots { get; set; } = 6;
    public int LineGapDots { get; set; } = 4;

    public bool Border { get; set; }
    public int BorderWidth { get; set; } = 2;

    /// <summary>If set, the image is rendered (scaled + centred) instead of text.</summary>
    public string? ImagePath { get; set; }
}
