#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Addons;
using TextureBase = GLTFast.Schema.TextureBase;
using SlopArena.Shared;
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
        /// Takes PNG images from glTFast: decodes and shrinks them on a worker thread with Shared's
        /// <see cref="PngShrink"/>, so the main thread only uploads the small copy. Unity decodes a PNG on the main
        /// thread, and Dusty's 4096 px PNG stalled one frame for about 235 ms. A valid PNG kind the reader does not
        /// decode (interlaced, packed samples) goes to Unity's decoder and the GPU shrink; a malformed one refuses the
        /// file. Every texture it makes belongs to the import, so a failed or dropped import frees it.
        /// </summary>
        internal sealed class PngLoader : ImportAddonInstance, ITextureImageLoader
        {
            private readonly int _maxSide;
            private readonly ImportedSwapModel _model;

            public PngLoader(int maxSide, ImportedSwapModel model)
            {
                _maxSide = maxSide;
                _model = model;
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
                var stats = _model.Stats;
                var clock = Stopwatch.StartNew();
                var decoded = await Task.Run(() => PngShrink.Decode(bytes, _maxSide, WorkshopGlbLimits.Default));
                stats.DecodeMs += clock.Elapsed.TotalMilliseconds;
                // The import may have failed or been dropped while the worker decoded.
                if (_model.IsDisposed || cancellationToken.IsCancellationRequested) return ImageResult.Null;
                if (decoded.Image == null && !decoded.Unsupported)
                {
                    _model.ImageRefusal ??= $"{decoded.Code}: {decoded.Message}";
                    return ImageResult.Null;
                }
                clock.Restart();
                Texture2D texture;
                if (decoded.Image is { } image)
                {
                    // What Unity's own decoder would have kept: full size, RGBA32.
                    stats.TextureBytesLoaded += (long)image.SourceWidth * image.SourceHeight * 4;
                    texture = NewTexture("png", image.Width, image.Height, linear);
                    texture.SetPixelData(image.Rgb, 0);
                    Finish(texture);
                }
                else
                {
                    texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear) { name = "png" };
                    if (!texture.LoadImage(bytes, false))
                    {
                        UnityEngine.Object.Destroy(texture);
                        _model.ImageRefusal ??= $"{decoded.Code}: Unity could not decode it either.";
                        return ImageResult.Null;
                    }
                }
                _model.Textures.Add(texture);
                stats.WorstTextureMs = Math.Max(stats.WorstTextureMs, clock.Elapsed.TotalMilliseconds);
                return new ImageResult(texture);
            }
        }
    }
}
