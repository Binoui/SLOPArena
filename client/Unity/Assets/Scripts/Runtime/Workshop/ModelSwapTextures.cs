#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Addons;
using TextureBase = GLTFast.Schema.TextureBase;
using Unity.Collections;
using UnityEngine;

namespace SlopArena.Client.Workshop
{
    /// <summary>
    /// The model swap texture budget: every texture keeps at most a fixed largest side, with mipmaps, DXT1 where the
    /// GPU takes it. Fighters draw opaque, so alpha is dropped.
    /// </summary>
    public static class ModelSwapTextures
    {
        public static long EstimateBytes(Texture2D texture)
        {
            double bytesPerPixel = texture.format switch
            {
                TextureFormat.DXT1 or TextureFormat.BC4 => 0.5,
                TextureFormat.DXT5 or TextureFormat.BC5 or TextureFormat.BC7 => 1,
                TextureFormat.RGB24 => 3,
                _ => 4,
            };
            double bytes = texture.width * (double)texture.height * bytesPerPixel;
            return (long)(texture.mipmapCount > 1 ? bytes * 4.0 / 3.0 : bytes);
        }

        public static bool WithinBudget(Texture2D texture, int maxSide)
            => Mathf.Max(texture.width, texture.height) <= maxSide && texture.format == TextureFormat.DXT1;

        /// <summary>A GPU copy of a loaded texture at the budget's size; for images the PNG loader did not take.</summary>
        public static Texture2D Shrink(Texture2D source, int maxSide)
        {
            var (width, height) = Fit(source.width, source.height, maxSide);
            // Halving one step at a time keeps each bilinear tap a 2x2 box, so the shrink does not alias.
            Texture input = source;
            RenderTexture? current = null;
            int stepWidth = source.width, stepHeight = source.height;
            do
            {
                stepWidth = Mathf.Max(width, stepWidth / 2);
                stepHeight = Mathf.Max(height, stepHeight / 2);
                var next = RenderTexture.GetTemporary(stepWidth, stepHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                next.filterMode = FilterMode.Bilinear;
                Graphics.Blit(input, next);
                if (current != null) RenderTexture.ReleaseTemporary(current);
                current = next;
                input = next;
            } while (stepWidth > width || stepHeight > height);

            var copy = NewTexture(source.name, width, height, false);
            copy.wrapModeU = source.wrapModeU;
            copy.wrapModeV = source.wrapModeV;
            var active = RenderTexture.active;
            RenderTexture.active = current;
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(current);
            Finish(copy);
            return copy;
        }

        private static (int Width, int Height) Fit(int width, int height, int maxSide)
        {
            while (Mathf.Max(width, height) > maxSide)
            {
                width = Mathf.Max(1, width / 2);
                height = Mathf.Max(1, height / 2);
            }
            return (width, height);
        }

        private static Texture2D NewTexture(string name, int width, int height, bool linear) =>
            new(width, height, TextureFormat.RGB24, true, linear)
            {
                name = name + " (model swap)",
                filterMode = FilterMode.Trilinear,
            };

        private static void Finish(Texture2D texture)
        {
            texture.Apply(true, false);
            if (texture.width % 4 == 0 && texture.height % 4 == 0 && SystemInfo.SupportsTextureFormat(TextureFormat.DXT1))
                texture.Compress(false);
            texture.Apply(false, true);
        }

        /// <summary>
        /// Takes PNG images from glTFast: decodes and shrinks them on a worker thread, so the main thread only uploads
        /// the small copy. Unity decodes a PNG on the main thread, and Dusty's 4096 px PNG stalled one frame for about
        /// 235 ms. A PNG this decoder does not cover goes back to Unity's decoder and the GPU shrink.
        /// </summary>
        internal sealed class PngLoader : ImportAddonInstance, ITextureImageLoader
        {
            private readonly int _maxSide;
            private readonly ModelSwapImportStats _stats;

            public PngLoader(int maxSide, ModelSwapImportStats stats)
            {
                _maxSide = maxSide;
                _stats = stats;
            }

            public override bool SupportsGltfExtension(string extensionName) => false;
            public override void Inject(GltfImportBase gltfImport) { }
            public override void Inject(IInstantiator instantiator) { }
            public override void Dispose() { }

            public bool IsAbleToLoad(TextureBase texture, out int imageIndex)
            {
                imageIndex = -1;
                return false;
            }

            public bool IsAbleToLoad(ReadOnlySpan<byte> data) => PngShrink.IsPng(data);

            public async Task<ImageResult> LoadImage(NativeArray<byte>.ReadOnly data, bool linear, bool readable,
                bool generateMipMaps, CancellationToken cancellationToken)
            {
                byte[] bytes = data.ToArray();
                var clock = Stopwatch.StartNew();
                var image = await Task.Run(() => PngShrink.Decode(bytes, _maxSide), cancellationToken);
                _stats.DecodeMs += clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                Texture2D texture;
                if (image != null)
                {
                    // What Unity's own decoder would have kept: full size, RGBA32.
                    _stats.TextureBytesLoaded += (long)image.SourceWidth * image.SourceHeight * 4;
                    texture = NewTexture("png", image.Width, image.Height, linear);
                    texture.SetPixelData(image.Rgb, 0);
                    Finish(texture);
                }
                else
                {
                    texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear) { name = "png" };
                    texture.LoadImage(bytes, false);
                }
                _stats.WorstTextureMs = Math.Max(_stats.WorstTextureMs, clock.Elapsed.TotalMilliseconds);
                return new ImageResult(texture);
            }
        }
    }

    /// <summary>
    /// A small PNG reader for the texture budget: 8- and 16-bit grey, RGB, palette, grey with alpha and RGBA, not
    /// interlaced. It box-filters rows into the output as it inflates them, so a 4096 px image never sits in memory
    /// whole, and writes rows bottom-up as Unity textures are. Alpha is dropped. Returns null for anything else.
    /// </summary>
    internal static class PngShrink
    {
        internal sealed class Image
        {
            public int Width, Height, SourceWidth, SourceHeight;
            public byte[] Rgb = Array.Empty<byte>();
        }

        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private const int MaxSide = 16384;

        public static bool IsPng(ReadOnlySpan<byte> data) => data.Length > 8 && data.Slice(0, 8).SequenceEqual(Signature);

        public static Image? Decode(byte[] png, int maxSide)
        {
            if (!IsPng(png)) return null;
            int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = 0;
            byte[]? palette = null;
            using var idat = new MemoryStream();
            int at = 8;
            while (at + 12 <= png.Length)
            {
                int length = ReadInt(png, at);
                if (length < 0 || at + 12 + length > png.Length) return null;
                string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
                int body = at + 8;
                if (type == "IHDR" && length >= 13)
                {
                    width = ReadInt(png, body);
                    height = ReadInt(png, body + 4);
                    bitDepth = png[body + 8];
                    colorType = png[body + 9];
                    interlace = png[body + 12];
                }
                else if (type == "PLTE") palette = png.AsSpan(body, length).ToArray();
                else if (type == "IDAT") idat.Write(png, body, length);
                else if (type == "IEND") break;
                at += 12 + length;
            }
            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            bool depthOk = bitDepth == 8 || bitDepth == 16 && colorType != 3;
            if (channels == 0 || !depthOk || interlace != 0 || width <= 0 || height <= 0 || width > MaxSide || height > MaxSide
                || colorType == 3 && palette == null || idat.Length < 2)
                return null;

            int factor = 1;
            while ((width + factor - 1) / factor > maxSide || (height + factor - 1) / factor > maxSide) factor *= 2;
            int outWidth = (width + factor - 1) / factor, outHeight = (height + factor - 1) / factor;
            int bytesPerPixel = channels * bitDepth / 8;
            int stride = width * bytesPerPixel;
            var row = new byte[stride];
            var previous = new byte[stride];
            var sums = new int[outWidth * 3];
            var counts = new int[outWidth];
            var rgb = new byte[outWidth * outHeight * 3];

            idat.Position = 2;
            using var inflate = new DeflateStream(idat, CompressionMode.Decompress);
            for (int y = 0; y < height; y++)
            {
                int filter = inflate.ReadByte();
                if (filter < 0 || !ReadFully(inflate, row)) return null;
                Unfilter(filter, row, previous, bytesPerPixel);
                for (int x = 0; x < width; x++)
                {
                    int p = x * bytesPerPixel, step = bitDepth / 8;
                    int r, g, b;
                    switch (colorType)
                    {
                        case 3:
                            int entry = row[p] * 3;
                            if (entry + 2 >= palette!.Length) return null;
                            r = palette[entry]; g = palette[entry + 1]; b = palette[entry + 2];
                            break;
                        case 0:
                        case 4:
                            r = g = b = row[p];
                            break;
                        default:
                            r = row[p]; g = row[p + step]; b = row[p + 2 * step];
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
                        int n = Math.Max(1, counts[o]), d = outRow * outWidth * 3 + o * 3;
                        rgb[d] = (byte)(sums[o * 3] / n);
                        rgb[d + 1] = (byte)(sums[o * 3 + 1] / n);
                        rgb[d + 2] = (byte)(sums[o * 3 + 2] / n);
                    }
                    Array.Clear(sums, 0, sums.Length);
                    Array.Clear(counts, 0, counts.Length);
                }
                (row, previous) = (previous, row);
            }
            return new Image { Width = outWidth, Height = outHeight, SourceWidth = width, SourceHeight = height, Rgb = rgb };
        }

        private static int ReadInt(byte[] data, int at) => data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3];

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
}
