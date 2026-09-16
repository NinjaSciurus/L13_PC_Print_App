using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace L13.Core.Rendering;

/// <summary>
/// Renders text or an image to a 1-bit-ready label bitmap: auto-fit with a minimum
/// font size, two-line wrapping, horizontal + vertical centring on a full-body
/// canvas, optional border, then orientation for the 96-dot head.
/// </summary>
public static class LabelRenderer
{
    public static RenderResult Render(RenderOptions o)
    {
        int labelLenDots = (int)Math.Round(o.LabelLengthMm * o.Dpi / 25.4);

        Bitmap canvas = string.IsNullOrEmpty(o.ImagePath)
            ? BuildTextCanvas(o, labelLenDots, out int lines, out double fontPx)
            : BuildImageCanvas(o, labelLenDots, out lines, out fontPx);

        if (o.Border)
            DrawBorder(canvas, o.BorderWidth);

        Bitmap oriented = Orient(canvas, o.Rotate, o.Flip);
        canvas.Dispose();

        // WYSIWYG preview = oriented rotated back 90 deg CW (see PS header derivation).
        var preview = (Bitmap)oriented.Clone();
        preview.RotateFlip(RotateFlipType.Rotate90FlipNone);

        return new RenderResult
        {
            Oriented = oriented,
            Preview = preview,
            Rows = oriented.Height,
            WidthBytes = (oriented.Width + 7) / 8,
            Lines = lines,
            FontPx = fontPx,
        };
    }

    private static Bitmap BuildTextCanvas(RenderOptions o, int labelLenDots, out int lineCount, out double fontPx)
    {
        var style = o.Bold ? FontStyle.Bold : FontStyle.Regular;
        // GenericTypographic gives tight, padding-free metrics. It returns a fresh
        // instance on each access, so own it and dispose it.
        using var fmt = new StringFormat(StringFormat.GenericTypographic);

        int pad = o.Border ? o.BorderWidth + 2 : 0;
        double availLen = labelLenDots - (2 * o.SideMarginDots) - (2 * pad);
        double availH = o.HeadDots - (2 * o.MarginDots) - (2 * pad);

        using var measureBmp = new Bitmap(8, 8);
        using var mg = Graphics.FromImage(measureBmp);
        mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // Explicit line breaks define paragraphs; long paragraphs get word-wrapped.
        string[] paragraphs = SplitInputLines(o.Text);
        double px;
        string[] lines;

        if (o.FontSize > 0)
        {
            px = o.FontSize;
            using var fixedFont = new Font(o.FontName, (float)px, style, GraphicsUnit.Pixel);
            lines = WrapParagraphs(mg, paragraphs, fixedFont, fmt, availLen).ToArray();
        }
        else
        {
            // Largest size at which the word-wrapped block still fits the height.
            // Every wrapped line already fits the width, so nothing can overrun.
            px = FitWrapped(mg, paragraphs, o.FontName, style, fmt,
                            availLen, availH, o.LineGapDots, o.MinFontPx, availH, out lines);
        }

        lineCount = lines.Length;
        fontPx = px;

        var canvas = new Bitmap(labelLenDots, o.HeadDots);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var f = new Font(o.FontName, (float)px, style, GraphicsUnit.Pixel);
            double lineH = g.MeasureString("Ag", f, PointF.Empty, fmt).Height;

            int n = lines.Length;
            double blockH = (n * lineH) + ((n - 1) * o.LineGapDots);
            double startY = (o.HeadDots - blockH) / 2.0;   // vertical centring

            for (int i = 0; i < n; i++)
            {
                double lw = g.MeasureString(lines[i], f, PointF.Empty, fmt).Width;
                double x = (labelLenDots - lw) / 2.0;       // horizontal centring
                double y = startY + (i * (lineH + o.LineGapDots));
                g.DrawString(lines[i], f, Brushes.Black, (float)x, (float)y, fmt);
            }
        }

        return canvas;
    }

    private static Bitmap BuildImageCanvas(RenderOptions o, int labelLenDots, out int lineCount, out double fontPx)
    {
        lineCount = 0;
        fontPx = 0;

        int pad = o.Border ? o.BorderWidth + 2 : 0;
        double availLen = labelLenDots - (2 * o.SideMarginDots) - (2 * pad);
        double availH = o.HeadDots - (2 * o.MarginDots) - (2 * pad);

        var canvas = new Bitmap(labelLenDots, o.HeadDots);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var src = new Bitmap(o.ImagePath!);
            double scale = Math.Min(availLen / src.Width, availH / src.Height);
            if (scale <= 0) scale = 1;

            double w = src.Width * scale;
            double h = src.Height * scale;
            double x = (labelLenDots - w) / 2.0;
            double y = (o.HeadDots - h) / 2.0;

            // Drawn as grayscale-ish; RasterPacker applies the luminance threshold.
            g.DrawImage(src, (float)x, (float)y, (float)w, (float)h);
        }

        return canvas;
    }

    /// <summary>
    /// Binary-searches the largest pixel size in [minPx, maxPx] at which the input,
    /// word-wrapped to <paramref name="availLen"/>, still fits <paramref name="availH"/>
    /// tall. Returns the size and the wrapped display lines at that size.
    /// </summary>
    private static double FitWrapped(Graphics g, string[] paragraphs, string fontName, FontStyle style,
                                     StringFormat fmt, double availLen, double availH, int lineGap,
                                     double minPx, double maxPx, out string[] wrapped)
    {
        double lo = minPx, hi = maxPx, best = minPx;
        string[]? bestLines = null;

        for (int i = 0; i < 22; i++)
        {
            double mid = (lo + hi) / 2.0;
            using var f = new Font(fontName, (float)mid, style, GraphicsUnit.Pixel);
            var lines = WrapParagraphs(g, paragraphs, f, fmt, availLen);
            double lineH = g.MeasureString("Ag", f, PointF.Empty, fmt).Height;
            double blockH = (lines.Count * lineH) + ((lines.Count - 1) * lineGap);
            if (blockH <= availH) { best = mid; lo = mid; bestLines = [.. lines]; }
            else hi = mid;
        }

        if (bestLines is null)
        {
            // Too much text even at the minimum size: wrap at the floor and let it overflow.
            using var f = new Font(fontName, (float)best, style, GraphicsUnit.Pixel);
            bestLines = [.. WrapParagraphs(g, paragraphs, f, fmt, availLen)];
        }

        wrapped = bestLines;
        return best;
    }

    /// <summary>
    /// Word-wraps each paragraph to <paramref name="availLen"/> at the given font.
    /// Words longer than the line are hard-broken by character so nothing overruns.
    /// </summary>
    private static List<string> WrapParagraphs(Graphics g, string[] paragraphs, Font f, StringFormat fmt, double availLen)
    {
        double Width(string s) => g.MeasureString(s, f, PointF.Empty, fmt).Width;

        var outLines = new List<string>();
        foreach (var para in paragraphs)
        {
            var words = para.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { outLines.Add(""); continue; }

            var current = "";
            foreach (var word in words)
            {
                while (true)
                {
                    var candidate = current.Length == 0 ? word : current + " " + word;
                    if (Width(candidate) <= availLen) { current = candidate; break; }

                    if (current.Length > 0) { outLines.Add(current); current = ""; continue; }

                    // A single word wider than the line: hard-break it by characters.
                    var piece = "";
                    foreach (var ch in word)
                    {
                        if (piece.Length > 0 && Width(piece + ch) > availLen) { outLines.Add(piece); piece = ""; }
                        piece += ch;
                    }
                    current = piece;
                    break;
                }
            }
            if (current.Length > 0) outLines.Add(current);
        }
        return outLines;
    }

    /// <summary>
    /// Splits user input into physical lines on any newline style, trims each,
    /// and drops blank lines (so a trailing Enter doesn't add an empty line that
    /// throws off the font-fit and vertical centring).
    /// </summary>
    private static string[] SplitInputLines(string text)
    {
        var parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lines = new List<string>();
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                lines.Add(trimmed);
        }
        return lines.Count > 0 ? lines.ToArray() : [text.Trim()];
    }

    private static Bitmap Orient(Bitmap canvas, int rotate, bool flip)
    {
        var b = (Bitmap)canvas.Clone();
        switch (rotate)
        {
            case 90: b.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
            case 180: b.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
            case 270: b.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
        }
        if (flip)
            b.RotateFlip(RotateFlipType.RotateNoneFlipX);
        return b;
    }

    private static void DrawBorder(Bitmap canvas, int borderWidth)
    {
        int w = canvas.Width;
        int h = canvas.Height;
        for (int k = 0; k < borderWidth; k++)
        {
            for (int x = 0; x < w; x++)
            {
                canvas.SetPixel(x, k, Color.Black);
                canvas.SetPixel(x, h - 1 - k, Color.Black);
            }
            for (int y = 0; y < h; y++)
            {
                canvas.SetPixel(k, y, Color.Black);
                canvas.SetPixel(w - 1 - k, y, Color.Black);
            }
        }
    }
}
