#nullable enable
using System;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Lock-on marks for the homing dash, baked once from signed distances into the shared toon
    /// bands: an ink rim, an Action Yellow body and an optional white core.
    /// </summary>
    internal static class HomingDashSprites
    {
        public static readonly Color Ink = new(27f / 255f, 26f / 255f, 24f / 255f, 1f);
        public static readonly Color ActionYellow = new(1f, 0.8f, 0.133f, 1f);

        private static Texture2D? _dash, _reticle, _chevron, _bang;

        /// <summary>One dash of the dotted line; repeats along U.</summary>
        public static Texture2D Dash => _dash ??= Bake("Homing Dash Line", 64, 16, TextureWrapMode.Repeat, 3f, -1f,
            (x, y) => RoundedBox(x - 20f, y - 8f, 15f, 4f, 3f));

        public static Texture2D Reticle => _reticle ??= Bake("Homing Dash Reticle", 128, 128, TextureWrapMode.Clamp, 4f, -1f,
            (x, y) =>
            {
                float px = x - 64f, py = y - 64f;
                float d = MathF.Abs(MathF.Sqrt(px * px + py * py) - 38f) - 5f;
                for (int i = 0; i < 4; i++)
                {
                    float a = MathF.PI * 0.25f + i * MathF.PI * 0.5f;
                    float cx = MathF.Cos(a), cy = MathF.Sin(a);
                    d = MathF.Min(d, Segment(px, py, cx * 47f, cy * 47f, cx * 58f, cy * 58f) - 5.5f);
                }
                return d;
            });

        /// <summary>A ">" pointing toward +U.</summary>
        public static Texture2D Chevron => _chevron ??= Bake("Homing Dash Chevron", 64, 64, TextureWrapMode.Clamp, 3f, 3.5f,
            (x, y) => MathF.Min(Segment(x, y, 20f, 12f, 42f, 32f), Segment(x, y, 42f, 32f, 20f, 52f)) - 6.5f);

        public static Texture2D Bang => _bang ??= Bake("Homing Dash Bang", 64, 128, TextureWrapMode.Clamp, 4f, 3.5f,
            (x, y) =>
            {
                float bar = Segment(x, y, 32f, 52f, 32f, 104f) - 9f - (y - 52f) * 0.06f;
                float dot = MathF.Sqrt((x - 32f) * (x - 32f) + (y - 26f) * (y - 26f)) - 11f;
                return MathF.Min(bar, dot);
            });

        private static Texture2D Bake(string name, int width, int height, TextureWrapMode wrap, float rim, float core,
            Func<float, float, float> distance)
        {
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float d = distance(x + 0.5f, y + 0.5f);
                    float alpha = Step(rim + 0.7f, rim - 0.7f, d);
                    Color c = Color.Lerp(Ink, ActionYellow, Step(0.7f, -0.7f, d));
                    if (core > 0f) c = Color.Lerp(c, Color.white, Step(-core + 0.7f, -core - 0.7f, d));
                    c.a = alpha;
                    pixels[y * width + x] = c;
                }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = name, wrapMode = wrap, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static float Step(float from, float to, float value)
        {
            float t = Mathf.Clamp01((value - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        private static float Segment(float x, float y, float ax, float ay, float bx, float by)
        {
            float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01((px * dx + py * dy) / (dx * dx + dy * dy));
            float ex = px - dx * t, ey = py - dy * t;
            return MathF.Sqrt(ex * ex + ey * ey);
        }

        private static float RoundedBox(float x, float y, float halfWidth, float halfHeight, float radius)
        {
            float qx = MathF.Abs(x) - halfWidth + radius, qy = MathF.Abs(y) - halfHeight + radius;
            float outside = MathF.Sqrt(MathF.Max(qx, 0f) * MathF.Max(qx, 0f) + MathF.Max(qy, 0f) * MathF.Max(qy, 0f));
            return outside + MathF.Min(MathF.Max(qx, qy), 0f) - radius;
        }
    }
}
