using System;
using System.IO;
using System.IO.Compression;

namespace SlopArena.Shared;

/// <summary>An RGB image, three bytes per pixel, rows bottom-up as Unity textures store them.</summary>
public sealed class PngShrinkImage
{
    public PngShrinkImage(int width, int height, int sourceWidth, int sourceHeight, byte[] rgb)
    {
        Width = width;
        Height = height;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Rgb = rgb;
    }

    public int Width { get; }
    public int Height { get; }
    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public byte[] Rgb { get; }
}

/// <summary>An image, or why there is none. <see cref="Unsupported"/> marks a valid PNG this reader does not decode.</summary>
public sealed record PngShrinkResult(PngShrinkImage? Image, string Code, string Message, bool Unsupported)
{
    internal static PngShrinkResult Refuse(string code, string message, bool unsupported = false) => new(null, code, message, unsupported);
}

/// <summary>
/// A small PNG reader for the model swap texture budget (ADR-0065). It reads 8- and 16-bit grey, RGB, palette, grey
/// with alpha and RGBA, not interlaced, and box-filters rows into the output by a power of two as it inflates them, so
/// a 4096 px image never sits in memory whole. Alpha is dropped. The header must be the first chunk and appear once,
/// with dimensions inside the same limits <see cref="WorkshopGlbPrecheck"/> enforces on the first header.
/// </summary>
public static class PngShrink
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data.Slice(0, 8).SequenceEqual(Signature);

    public static PngShrinkResult Decode(ReadOnlySpan<byte> png, int maxSide, WorkshopGlbLimits limits)
    {
        if (limits == null) throw new ArgumentNullException(nameof(limits));
        if (maxSide < 1) throw new ArgumentOutOfRangeException(nameof(maxSide));
        if (!IsPng(png)) return PngShrinkResult.Refuse("png.signature", "The image is not a PNG: its signature is wrong.");
        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        bool header = false;
        byte[]? palette = null;
        using var idat = new MemoryStream();
        int at = 8;
        while (true)
        {
            if (png.Length - at < 12) return PngShrinkResult.Refuse("png.truncated", "The PNG ends inside a chunk header.");
            long length = ReadUInt(png, at);
            if (length > png.Length - at - 12) return PngShrinkResult.Refuse("png.truncated", "A PNG chunk runs past the end of the image.");
            var type = png.Slice(at + 4, 4);
            var body = png.Slice(at + 8, (int)length);
            bool isHeader = type.SequenceEqual("IHDR"u8);
            if (!header && !isHeader) return PngShrinkResult.Refuse("png.ihdr.first", "IHDR must be the first PNG chunk.");
            if (header && isHeader) return PngShrinkResult.Refuse("png.ihdr.duplicate", "The PNG has a second IHDR chunk.");
            if (isHeader)
            {
                if (length != 13) return PngShrinkResult.Refuse("png.ihdr.size", "The PNG header is not 13 bytes.");
                header = true;
                long w = ReadUInt(body, 0), h = ReadUInt(body, 4);
                if (w < 1 || h < 1 || w > limits.MaxImageDimension || h > limits.MaxImageDimension || w * h > limits.MaxImagePixels)
                    return PngShrinkResult.Refuse("png.dimensions",
                        $"The PNG is {w} by {h}; the limits are {limits.MaxImageDimension} per side and {limits.MaxImagePixels} pixels.");
                width = (int)w;
                height = (int)h;
                bitDepth = body[8];
                colorType = body[9];
                if (body[10] != 0 || body[11] != 0) return PngShrinkResult.Refuse("png.ihdr.method", "The PNG names an unknown compression or filter method.");
                if (body[12] != 0) return PngShrinkResult.Refuse("png.unsupported.interlace", "The PNG is interlaced.", unsupported: true);
            }
            else if (type.SequenceEqual("PLTE"u8)) palette = body.ToArray();
            else if (type.SequenceEqual("IDAT"u8)) idat.Write(body);
            else if (type.SequenceEqual("IEND"u8)) break;
            at += 12 + (int)length;
        }

        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (channels == 0) return PngShrinkResult.Refuse("png.color-type", $"The PNG has an unknown colour type {colorType}.");
        bool knownDepth = colorType == 3 ? bitDepth is 1 or 2 or 4 or 8 : colorType == 0 ? bitDepth is 1 or 2 or 4 or 8 or 16 : bitDepth is 8 or 16;
        if (!knownDepth) return PngShrinkResult.Refuse("png.bit-depth", $"Colour type {colorType} cannot have bit depth {bitDepth}.");
        if (bitDepth < 8) return PngShrinkResult.Refuse("png.unsupported.bit-depth", $"The PNG packs {bitDepth} bits per sample.", unsupported: true);
        if (colorType == 3 && palette == null) return PngShrinkResult.Refuse("png.palette.missing", "The palette PNG has no PLTE chunk.");
        if (idat.Length < 2) return PngShrinkResult.Refuse("png.truncated", "The PNG has no image data.");

        int factor = 1;
        while ((width + factor - 1) / factor > maxSide || (height + factor - 1) / factor > maxSide) factor *= 2;
        int outWidth = (width + factor - 1) / factor, outHeight = (height + factor - 1) / factor;
        int sampleBytes = bitDepth / 8;
        int bytesPerPixel = channels * sampleBytes;
        var row = new byte[width * bytesPerPixel];
        var previous = new byte[row.Length];
        var sums = new int[outWidth * 3];
        var counts = new int[outWidth];
        var rgb = new byte[outWidth * outHeight * 3];
        try
        {
            idat.Position = 2;
            using var inflate = new DeflateStream(idat, CompressionMode.Decompress);
            for (int y = 0; y < height; y++)
            {
                int filter = inflate.ReadByte();
                if (filter < 0 || !ReadFully(inflate, row)) return PngShrinkResult.Refuse("png.truncated", $"The PNG image data ends at row {y} of {height}.");
                if (filter > 4) return PngShrinkResult.Refuse("png.filter", $"Row {y} names an unknown filter {filter}.");
                Unfilter(filter, row, previous, bytesPerPixel);
                for (int x = 0; x < width; x++)
                {
                    int p = x * bytesPerPixel, r, g, b;
                    switch (colorType)
                    {
                        case 3:
                            int entry = row[p] * 3;
                            if (entry + 2 >= palette!.Length) return PngShrinkResult.Refuse("png.palette.index", $"Pixel {x},{y} uses palette entry {row[p]}, past the palette's end.");
                            r = palette[entry]; g = palette[entry + 1]; b = palette[entry + 2];
                            break;
                        case 0:
                        case 4:
                            r = g = b = row[p];
                            break;
                        default:
                            r = row[p]; g = row[p + sampleBytes]; b = row[p + 2 * sampleBytes];
                            break;
                    }
                    int o = x / factor;
                    sums[o * 3] += r; sums[o * 3 + 1] += g; sums[o * 3 + 2] += b;
                    counts[o]++;
                }
                if ((y + 1) % factor == 0 || y == height - 1)
                {
                    int outRow = outHeight - 1 - y / factor;
                    for (int o = 0; o < outWidth; o++)
                    {
                        int n = Math.Max(1, counts[o]), d = (outRow * outWidth + o) * 3;
                        rgb[d] = (byte)(sums[o * 3] / n);
                        rgb[d + 1] = (byte)(sums[o * 3 + 1] / n);
                        rgb[d + 2] = (byte)(sums[o * 3 + 2] / n);
                    }
                    Array.Clear(sums, 0, sums.Length);
                    Array.Clear(counts, 0, counts.Length);
                }
                (row, previous) = (previous, row);
            }
        }
        catch (InvalidDataException exception)
        {
            return PngShrinkResult.Refuse("png.zlib", "The PNG's compressed data is corrupt: " + exception.Message);
        }
        return new PngShrinkResult(new PngShrinkImage(outWidth, outHeight, width, height, rgb), "", "", false);
    }

    private static long ReadUInt(ReadOnlySpan<byte> data, int at) => (long)data[at] << 24 | (long)data[at + 1] << 16 | (long)data[at + 2] << 8 | data[at + 3];

    private static bool ReadFully(Stream stream, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    private static void Unfilter(int filter, byte[] row, byte[] previous, int bpp)
    {
        switch (filter)
        {
            case 1:
                for (int i = bpp; i < row.Length; i++) row[i] += row[i - bpp];
                break;
            case 2:
                for (int i = 0; i < row.Length; i++) row[i] += previous[i];
                break;
            case 3:
                for (int i = 0; i < row.Length; i++)
                    row[i] += (byte)(((i >= bpp ? row[i - bpp] : 0) + previous[i]) / 2);
                break;
            case 4:
                for (int i = 0; i < row.Length; i++)
                {
                    int a = i >= bpp ? row[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                    int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    row[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                }
                break;
        }
    }
}
