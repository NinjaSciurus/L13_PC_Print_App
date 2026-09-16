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

        const double probe = 100.0;
        string[] lines;
        double px;

        using (var probeFont = new Font(o.FontName, (float)probe, style, GraphicsUnit.Pixel))
        {
            double probeH = mg.MeasureString("Ag", probeFont, PointF.Empty, fmt).Height;

            if (o.FontSize > 0)
            {
                lines = [o.Text];
                px = o.FontSize;
            }
            else
            {
                lines = [o.Text];
                px = FitPx(mg, lines, probeFont, probe, probeH, availLen, availH, o.LineGapDots, fmt);

                if (px < o.MinFontPx)
                {
                    var split = SplitMiddle(o.Text);
                    if (split is not null)
                    {
                        double px2 = FitPx(mg, split, probeFont, probe, probeH, availLen, availH, o.LineGapDots, fmt);
                        if (px2 >= px) { lines = split; px = px2; }
                    }
                }

                if (px < o.MinFontPx)
                    px = o.MinFontPx; // clamp; may overflow the body (caller/UI can warn)
            }
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

    private static double FitPx(
        Graphics g, string[] lines, Font probeFont, double probeSize, double probeH,
        double availLen, double availH, int lineGap, StringFormat fmt)
    {
        int n = lines.Length;
        double wProbe = Math.Max(1, MeasureMaxWidth(g, lines, probeFont, fmt));
        double budgetH = (availH - ((n - 1) * lineGap)) / n;
        double heightFit = probeSize * (budgetH / probeH);
        double widthFit = probeSize * (availLen / wProbe);
        return Math.Min(heightFit, widthFit);
    }

    private static double MeasureMaxWidth(Graphics g, string[] lines, Font f, StringFormat fmt)
    {
        double w = 0;
        foreach (var line in lines)
        {
            double lw = g.MeasureString(line, f, PointF.Empty, fmt).Width;
            if (lw > w) w = lw;
        }
        return w;
    }

    /// <summary>Splits at the space nearest the middle; null if there is no space.</summary>
    private static string[]? SplitMiddle(string text)
    {
        var t = text.Trim();
        var positions = new List<int>();
        for (int i = 0; i < t.Length; i++)
            if (t[i] == ' ') positions.Add(i);

        if (positions.Count == 0)
            return null;

        double mid = t.Length / 2.0;
        int best = positions[0];
        foreach (var p in positions)
            if (Math.Abs(p - mid) < Math.Abs(best - mid)) best = p;

        return [t[..best].Trim(), t[(best + 1)..].Trim()];
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
