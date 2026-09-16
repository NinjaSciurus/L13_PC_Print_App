using System.Drawing;

namespace L13.Core.Rendering;

/// <summary>Output of <see cref="LabelRenderer.Render"/>.</summary>
public sealed class RenderResult : IDisposable
{
    /// <summary>The packed-ready bitmap: <see cref="RenderOptions.HeadDots"/> wide, N rows tall.</summary>
    public required Bitmap Oriented { get; init; }

    /// <summary>WYSIWYG preview: the label as it prints, held with the first-out edge on the right.</summary>
    public required Bitmap Preview { get; init; }

    public int Rows { get; init; }
    public int WidthBytes { get; init; }
    public int Lines { get; init; }
    public double FontPx { get; init; }

    public double LengthMm(int dpi) => Rows / (dpi / 25.4);

    public void Dispose()
    {
        Oriented.Dispose();
        Preview.Dispose();
    }
}
