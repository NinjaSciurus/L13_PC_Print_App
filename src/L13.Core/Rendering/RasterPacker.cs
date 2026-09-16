using System.Drawing;
using System.Drawing.Imaging;

namespace L13.Core.Rendering;

/// <summary>
/// Packs the oriented bitmap into ESC/POS GS v 0 raster bands (MSB = leftmost dot,
/// 1 = black). Splitting into bands avoids the firmware dropping a single oversized
/// command; consecutive bands print contiguously.
/// </summary>
public static class RasterPacker
{
    public static IReadOnlyList<byte[]> ToBands(Bitmap bmp, int bandRows)
    {
        int width = bmp.Width;
        int rows = bmp.Height;
        int wBytes = (width + 7) / 8;

        var packed = new byte[rows * wBytes];

        var rect = new Rectangle(0, 0, width, rows);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* scan0 = (byte*)data.Scan0;
                int stride = data.Stride;
                for (int y = 0; y < rows; y++)
                {
                    byte* row = scan0 + (y * stride);
                    int baseIndex = y * wBytes;
                    for (int x = 0; x < width; x++)
                    {
                        // 32bpp ARGB in memory is B,G,R,A.
                        byte b = row[(x * 4) + 0];
                        byte g = row[(x * 4) + 1];
                        byte r = row[(x * 4) + 2];
                        double lum = (0.299 * r) + (0.587 * g) + (0.114 * b);
                        if (lum < 128)
                            packed[baseIndex + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        var bands = new List<byte[]>();
        int rowsPerBand = Math.Max(1, bandRows);
        for (int start = 0; start < rows; start += rowsPerBand)
        {
            int h = Math.Min(rowsPerBand, rows - start);
            var payload = new byte[8 + (h * wBytes)];
            payload[0] = 0x1D;
            payload[1] = 0x76;
            payload[2] = 0x30;
            payload[3] = 0x00;
            payload[4] = (byte)(wBytes & 0xFF);
            payload[5] = (byte)((wBytes >> 8) & 0xFF);
            payload[6] = (byte)(h & 0xFF);
            payload[7] = (byte)((h >> 8) & 0xFF);
            Array.Copy(packed, start * wBytes, payload, 8, h * wBytes);
            bands.Add(payload);
        }

        return bands;
    }
}
