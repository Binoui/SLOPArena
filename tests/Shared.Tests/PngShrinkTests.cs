using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>The model swap PNG reader: every accepted pixel format and filter, the shrink, and each refusal.</summary>
public class PngShrinkTests
{
    private static readonly WorkshopGlbLimits Limits = WorkshopGlbLimits.Default;

    // Samples are one value per channel per pixel, row-major from the top, at the image's bit depth.
    private sealed record Picture(int Width, int Height, int ColorType, int BitDepth, int[] Samples, byte[]? Palette = null)
    {
        public int Channels => ColorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
    }

    private static Picture Random(int width, int height, int colorType, int bitDepth, int seed = 7, byte[]? palette = null)
    {
        var random = new System.Random(seed);
        var picture = new Picture(width, height, colorType, bitDepth, System.Array.Empty<int>(), palette);
        int max = colorType == 3 ? palette!.Length / 3 : 1 << bitDepth;
        var samples = new int[width * height * picture.Channels];
        for (int i = 0; i < samples.Length; i++) samples[i] = random.Next(max);
        return picture with { Samples = samples };
    }

    private static byte[] RowBytes(Picture p, int y)
    {
        int bytes = p.BitDepth / 8, perRow = p.Width * p.Channels;
        var row = new byte[perRow * bytes];
        for (int i = 0; i < perRow; i++)
        {
            int value = p.Samples[y * perRow + i];
            if (bytes == 2) BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(i * 2), (ushort)value);
            else row[i] = (byte)value;
        }
        return row;
    }

    private static byte[] Filter(int filter, byte[] row, byte[] previous, int bpp)
    {
        var output = new byte[row.Length];
        for (int i = 0; i < row.Length; i++)
        {
            int a = i >= bpp ? row[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
            int predictor = filter switch
            {
                1 => a,
                2 => b,
                3 => (a + b) / 2,
                4 => Paeth(a, b, c),
                _ => 0,
            };
            output[i] = (byte)(row[i] - predictor);
        }
        return output;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = System.Math.Abs(p - a), pb = System.Math.Abs(p - b), pc = System.Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw);
        return output.ToArray();
    }

    private static byte[] ImageData(Picture p, Func<int, int> filterForRow)
    {
        int bpp = p.Channels * p.BitDepth / 8;
        var raw = new List<byte>();
        var previous = new byte[p.Width * bpp];
        for (int y = 0; y < p.Height; y++)
        {
            var row = RowBytes(p, y);
            int filter = filterForRow(y);
            raw.Add((byte)filter);
            raw.AddRange(Filter(filter, row, previous, bpp));
            previous = row;
        }
        return Compress(raw.ToArray());
    }

    private static byte[] Header(long width, long height, int bitDepth, int colorType, int interlace = 0)
    {
        var data = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)height);
        data[8] = (byte)bitDepth;
        data[9] = (byte)colorType;
        data[12] = (byte)interlace;
        return Chunk("IHDR", data);
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xffffffff;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    private static byte[] File(params byte[][] chunks) => Signature.Concat(chunks.SelectMany(c => c)).ToArray();

    private static byte[] Encode(Picture p, Func<int, int>? filterForRow = null)
    {
        var chunks = new List<byte[]> { Header(p.Width, p.Height, p.BitDepth, p.ColorType) };
        if (p.Palette != null) chunks.Add(Chunk("PLTE", p.Palette));
        chunks.Add(Chunk("IDAT", ImageData(p, filterForRow ?? (_ => 0))));
        chunks.Add(Chunk("IEND", System.Array.Empty<byte>()));
        return File(chunks.ToArray());
    }

    // What the reader should return: RGB from the most significant byte of each sample, averaged over factor x factor
    // blocks (partial blocks at the edges average what they hold), rows bottom-up.
    private static byte[] Expected(Picture p, int factor)
    {
        int outWidth = (p.Width + factor - 1) / factor, outHeight = (p.Height + factor - 1) / factor;
        var sums = new long[outWidth * outHeight * 3];
        var counts = new int[outWidth * outHeight];
        int shift = p.BitDepth - 8;
        for (int y = 0; y < p.Height; y++)
        for (int x = 0; x < p.Width; x++)
        {
            int at = (y * p.Width + x) * p.Channels;
            int r, g, b;
            if (p.ColorType == 3)
            {
                int index = p.Samples[at] * 3;
                (r, g, b) = (p.Palette![index], p.Palette[index + 1], p.Palette[index + 2]);
            }
            else if (p.Channels <= 2) r = g = b = p.Samples[at] >> shift;
            else (r, g, b) = (p.Samples[at] >> shift, p.Samples[at + 1] >> shift, p.Samples[at + 2] >> shift);
            int o = (outHeight - 1 - y / factor) * outWidth + x / factor;
            sums[o * 3] += r; sums[o * 3 + 1] += g; sums[o * 3 + 2] += b;
            counts[o]++;
        }
        var rgb = new byte[sums.Length];
        for (int i = 0; i < rgb.Length; i++) rgb[i] = (byte)(sums[i] / counts[i / 3]);
        return rgb;
    }

    private static readonly byte[] FourColours = { 255, 0, 0, 0, 255, 0, 0, 0, 255, 250, 200, 100 };

    private static PngShrinkImage Decodes(byte[] png, int maxSide = 1024, WorkshopGlbLimits? limits = null)
    {
        var result = PngShrink.Decode(png, maxSide, limits ?? Limits);
        Assert.True(result.Image != null, $"{result.Code}: {result.Message}");
        return result.Image!;
    }

    private static PngShrinkResult Refuses(byte[] png, string code, WorkshopGlbLimits? limits = null)
    {
        var result = PngShrink.Decode(png, 1024, limits ?? Limits);
        Assert.Null(result.Image);
        Assert.Equal(code, result.Code);
        Assert.False(string.IsNullOrEmpty(result.Message));
        return result;
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(0, 16)]
    [InlineData(2, 8)]
    [InlineData(2, 16)]
    [InlineData(3, 8)]
    [InlineData(4, 8)]
    [InlineData(4, 16)]
    [InlineData(6, 8)]
    [InlineData(6, 16)]
    public void EveryAcceptedFormatReadsExactly(int colorType, int bitDepth)
    {
        var picture = Random(13, 9, colorType, bitDepth, palette: colorType == 3 ? FourColours : null);

        var image = Decodes(Encode(picture));

        Assert.Equal((13, 9, 13, 9), (image.Width, image.Height, image.SourceWidth, image.SourceHeight));
        Assert.Equal(Expected(picture, 1), image.Rgb);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EachRowFilterIsUndone(int filter)
    {
        var picture = Random(17, 11, 2, 8, seed: 40 + filter);

        Assert.Equal(Expected(picture, 1), Decodes(Encode(picture, _ => filter)).Rgb);
    }

    [Fact]
    public void FiltersMayChangeEveryRowAtSixteenBits()
    {
        var picture = Random(9, 15, 6, 16, seed: 3);

        Assert.Equal(Expected(picture, 1), Decodes(Encode(picture, y => y % 5)).Rgb);
    }

    [Fact]
    public void ShrinksByAPowerOfTwoAndAveragesPartialBlocks()
    {
        var picture = Random(10, 7, 2, 8, seed: 11);

        var image = Decodes(Encode(picture), maxSide: 4);

        Assert.Equal((3, 2, 10, 7), (image.Width, image.Height, image.SourceWidth, image.SourceHeight));
        Assert.Equal(Expected(picture, 4), image.Rgb);
    }

    [Fact]
    public void AnImageInsideTheSideIsNotShrunk()
    {
        var picture = Random(8, 8, 2, 8);

        Assert.Equal(8, Decodes(Encode(picture), maxSide: 8).Width);
    }

    [Fact]
    public void RowsComeOutBottomUp()
    {
        var picture = new Picture(1, 2, 2, 8, new[] { 255, 0, 0, 0, 0, 255 });

        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0 }, Decodes(Encode(picture)).Rgb);
    }

    [Fact]
    public void InterlacedIsUnsupportedNotMalformed()
    {
        var png = File(Header(2, 2, 8, 2, interlace: 1), Chunk("IDAT", Compress(new byte[12])), Chunk("IEND", System.Array.Empty<byte>()));

        Assert.True(Refuses(png, "png.unsupported.interlace").Unsupported);
    }

    [Fact]
    public void PackedSamplesAreUnsupportedNotMalformed()
    {
        var png = File(Header(4, 1, 4, 3), Chunk("PLTE", FourColours), Chunk("IDAT", Compress(new byte[3])), Chunk("IEND", System.Array.Empty<byte>()));

        Assert.True(Refuses(png, "png.unsupported.bit-depth").Unsupported);
    }

    [Fact]
    public void BadSignatureIsRefused()
    {
        var png = Encode(Random(2, 2, 2, 8));
        png[1] = (byte)'Q';

        Refuses(png, "png.signature");
    }

    [Fact]
    public void ChunkRunningPastTheEndIsRefused()
    {
        var png = Encode(Random(4, 4, 2, 8));

        Refuses(png.AsSpan(0, png.Length - 20).ToArray(), "png.truncated");
    }

    [Fact]
    public void ImageDataShortOfTheLastRowIsRefused()
    {
        var shortData = Compress(new byte[(1 + 4 * 3) * 3]);
        var png = File(Header(4, 4, 8, 2), Chunk("IDAT", shortData), Chunk("IEND", System.Array.Empty<byte>()));

        Assert.Contains("row 3", Refuses(png, "png.truncated").Message);
    }

    [Fact]
    public void CorruptCompressedDataIsRefused()
    {
        var garbage = new byte[] { 0x78, 0x9c, 0xff, 0xff, 0xff, 0xff, 0x00, 0x13, 0x37 };
        var png = File(Header(2, 2, 8, 2), Chunk("IDAT", garbage), Chunk("IEND", System.Array.Empty<byte>()));

        Refuses(png, "png.zlib");
    }

    [Fact]
    public void ASecondHeaderIsRefusedEvenWhenTheFirstIsTiny()
    {
        var png = File(Header(1, 1, 8, 2), Header(16384, 16384, 8, 2), Chunk("IDAT", Compress(new byte[4])), Chunk("IEND", System.Array.Empty<byte>()));

        Refuses(png, "png.ihdr.duplicate");
    }

    [Fact]
    public void AHeaderAfterAnotherChunkIsRefused()
    {
        var png = File(Chunk("tEXt", Encoding.ASCII.GetBytes("a\0b")), Header(1, 1, 8, 2), Chunk("IDAT", Compress(new byte[4])), Chunk("IEND", System.Array.Empty<byte>()));

        Refuses(png, "png.ihdr.first");
    }

    [Theory]
    [InlineData(4097, 1)]
    [InlineData(1, 4097)]
    [InlineData(0, 1)]
    public void DimensionsOutsideThePrecheckLimitsAreRefused(long width, long height)
    {
        var png = File(Header(width, height, 8, 2), Chunk("IEND", System.Array.Empty<byte>()));

        Refuses(png, "png.dimensions");
    }

    [Fact]
    public void ThePixelLimitIsThePrecheckOne()
    {
        var png = Encode(Random(16, 16, 2, 8));

        Refuses(png, "png.dimensions", Limits with { MaxImagePixels = 255 });
        Decodes(png, limits: Limits with { MaxImagePixels = 256 });
    }

    [Fact]
    public void PaletteIndexPastTheEndIsRefused()
    {
        var picture = new Picture(2, 1, 3, 8, new[] { 0, 5 }, FourColours);

        Refuses(Encode(picture), "png.palette.index");
    }

    [Fact]
    public void PaletteImageWithoutPaletteIsRefused()
    {
        var picture = new Picture(2, 1, 3, 8, new[] { 0, 1 });

        Refuses(Encode(picture), "png.palette.missing");
    }

    [Fact]
    public void UnknownRowFilterIsRefused()
    {
        var raw = new byte[] { 7, 1, 2, 3 };
        var png = File(Header(1, 1, 8, 2), Chunk("IDAT", Compress(raw)), Chunk("IEND", System.Array.Empty<byte>()));

        Refuses(png, "png.filter");
    }

    [Theory]
    [InlineData(5, 8, "png.color-type")]
    [InlineData(3, 16, "png.bit-depth")]
    [InlineData(2, 4, "png.bit-depth")]
    public void ImpossibleFormatsAreRefused(int colorType, int bitDepth, string code)
    {
        var png = File(Header(1, 1, bitDepth, colorType), Chunk("IDAT", Compress(new byte[9])), Chunk("IEND", System.Array.Empty<byte>()));

        Refuses(png, code);
    }

    [Fact]
    public void RandomByteChangesNeverThrow()
    {
        var original = Encode(Random(12, 12, 6, 8), y => y % 5);
        var random = new System.Random(816);
        for (int n = 0; n < 2000; n++)
        {
            var png = (byte[])original.Clone();
            png[random.Next(png.Length)] = (byte)random.Next(256);
            PngShrink.Decode(png, 4, Limits);
        }
    }
}
