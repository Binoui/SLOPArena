#if UNITY_EDITOR
using UnityEngine;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Signed-distance shapes and toon banding for textures baked by
    /// <see cref="PresentationBuilderKit.BakeTexture"/>. Shapes take a point in −1..1 and
    /// return a distance that is negative inside. Import with <c>using static</c>.
    /// </summary>
    public static class ToonShapes
    {
        /// <summary>
        /// Toon bands from a signed distance in texels: a dark rim at the edge, then the accent,
        /// then the core, each boundary anti-aliased over one texel. A dark rim keeps a shape
        /// readable on bright stages and a white core catches bloom on dark ones.
        /// </summary>
        public static Color Bands(float distance, float rimWidth, float accentWidth, Color rim, Color accent, Color core)
        {
            float inAccent = Mathf.Clamp01(0.5f - distance - rimWidth);
            float inCore = Mathf.Clamp01(0.5f - distance - rimWidth - accentWidth);
            Color color = Color.Lerp(Color.Lerp(rim, accent, inAccent), core, inCore);
            color.a = Mathf.Clamp01(0.5f - distance);
            return color;
        }

        /// <summary>
        /// Shader-style smoothstep: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>.
        /// Unity's Mathf.SmoothStep(a, b, t) is different: it eases from a to b as t goes 0 to 1.
        /// </summary>
        public static float Step(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Distance from the texture centre in UV space: 0 at the centre, 1 at the edge midpoints.</summary>
        public static float Radius(float u, float v) => new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;

        /// <summary>White with an alpha, for textures tinted by particle colour.</summary>
        public static Color Alpha(float a) => new(1f, 1f, 1f, Mathf.Clamp01(a));

        public static Vector2 Rotate(Vector2 p, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            return new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
        }

        public static float Box(Vector2 p, Vector2 half)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x, Mathf.Abs(p.y) - half.y);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        /// <summary>Signed distance to a rhombus with half-diagonals b (Inigo Quilez).</summary>
        public static float Rhombus(Vector2 p, Vector2 b)
        {
            p = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            float h = Mathf.Clamp((b.x * (b.x - 2f * p.x) - b.y * (b.y - 2f * p.y)) / Vector2.Dot(b, b), -1f, 1f);
            float d = (p - 0.5f * new Vector2(b.x * (1f - h), b.y * (1f + h))).magnitude;
            return d * Mathf.Sign(p.x * b.y + p.y * b.x - b.x * b.y);
        }

        /// <summary>A horizontal lens: the intersection of two circles meeting at ±halfLength.</summary>
        public static float Lens(Vector2 p, float halfLength, float halfWidth)
        {
            float radius = (halfLength * halfLength + halfWidth * halfWidth) / (2f * halfWidth);
            var offset = new Vector2(0f, radius - halfWidth);
            return Mathf.Max((p - offset).magnitude, (p + offset).magnitude) - radius;
        }

        /// <summary>Signed distance to a simple polygon (Inigo Quilez).</summary>
        public static float Polygon(Vector2 p, Vector2[] vertices)
        {
            float distance = Vector2.Dot(p - vertices[0], p - vertices[0]);
            float sign = 1f;
            for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i, i++)
            {
                Vector2 edge = vertices[j] - vertices[i], w = p - vertices[i];
                Vector2 b = w - edge * Mathf.Clamp01(Vector2.Dot(w, edge) / Vector2.Dot(edge, edge));
                distance = Mathf.Min(distance, Vector2.Dot(b, b));
                bool above = p.y >= vertices[i].y, below = p.y < vertices[j].y, left = edge.x * w.y > edge.y * w.x;
                if ((above && below && left) || (!above && !below && !left)) sign = -sign;
            }
            return sign * Mathf.Sqrt(distance);
        }

        /// <summary>Smooth value noise in 0..1 at <paramref name="frequency"/> cells per unit; the same on every rebuild.</summary>
        public static float ValueNoise(float u, float v, float frequency, int seed)
        {
            float x = u * frequency, y = v * frequency;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x0 + 1, y0, seed), tx);
            float b = Mathf.Lerp(Hash(x0, y0 + 1, seed), Hash(x0 + 1, y0 + 1, seed), tx);
            return Mathf.Lerp(a, b, ty);
        }

        /// <summary>Stable integer hash in 0..1.</summary>
        public static float Hash(int x, int y, int seed = 0)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>
        /// A comic impact-star outline with uneven spikes, for <see cref="Polygon"/>. The seed
        /// keeps the shape identical on every rebuild.
        /// </summary>
        public static Vector2[] StarOutline(int spikes, float inner, int seed)
        {
            var random = new System.Random(seed);
            var points = new Vector2[spikes * 2];
            for (int i = 0; i < spikes; i++)
            {
                float angle = (i + (float)random.NextDouble() * 0.3f - 0.15f) / spikes * Mathf.PI * 2f;
                float outer = Mathf.Lerp(0.68f, 0.96f, (float)random.NextDouble());
                float valley = angle + Mathf.PI / spikes;
                float depth = inner * Mathf.Lerp(0.9f, 1.1f, (float)random.NextDouble());
                points[i * 2] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * outer;
                points[i * 2 + 1] = new Vector2(Mathf.Cos(valley), Mathf.Sin(valley)) * depth;
            }
            return points;
        }
    }

    /// <summary>
    /// A signed distance field from a grayscale mask PNG (white inside), for baking glyphs and
    /// logos with <see cref="ToonShapes.Bands"/>. Distances are in mask pixels, negative inside.
    /// Exact Euclidean transform (Felzenszwalb and Huttenlocher), sampled bilinearly.
    /// </summary>
    public sealed class MaskField
    {
        private readonly float[] _distance;
        public int Width { get; }
        public int Height { get; }

        public MaskField(string pngPath)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(System.IO.File.ReadAllBytes(pngPath)))
                throw new System.InvalidOperationException($"Could not read mask {pngPath}.");
            Width = texture.width;
            Height = texture.height;
            var pixels = texture.GetPixels();
            Object.DestroyImmediate(texture);
            var inside = new bool[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                inside[i] = pixels[i].grayscale >= 0.5f;
            double[] outside = SquaredDistance(inside, true), within = SquaredDistance(inside, false);
            _distance = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                _distance[i] = (float)(System.Math.Sqrt(outside[i]) - System.Math.Sqrt(within[i]));
        }

        /// <summary>Distance at UV (0..1, v up), in mask pixels.</summary>
        public float Sample(float u, float v)
        {
            float x = Mathf.Clamp(u * Width - 0.5f, 0f, Width - 1.001f);
            float y = Mathf.Clamp(v * Height - 0.5f, 0f, Height - 1.001f);
            int x0 = (int)x, y0 = (int)y;
            float tx = x - x0, ty = y - y0;
            float a = Mathf.Lerp(_distance[y0 * Width + x0], _distance[y0 * Width + x0 + 1], tx);
            float b = Mathf.Lerp(_distance[(y0 + 1) * Width + x0], _distance[(y0 + 1) * Width + x0 + 1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        /// <summary>Squared distance to the nearest pixel whose inside flag equals <paramref name="target"/>.</summary>
        private double[] SquaredDistance(bool[] inside, bool target)
        {
            const double far = 1e20;
            var grid = new double[inside.Length];
            for (int i = 0; i < inside.Length; i++)
                grid[i] = inside[i] == target ? 0 : far;
            int n = Mathf.Max(Width, Height);
            var f = new double[n];
            var d = new double[n];
            var z = new double[n + 1];
            var v = new int[n];
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++) f[y] = grid[y * Width + x];
                Transform(f, d, v, z, Height);
                for (int y = 0; y < Height; y++) grid[y * Width + x] = d[y];
            }
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++) f[x] = grid[y * Width + x];
                Transform(f, d, v, z, Width);
                for (int x = 0; x < Width; x++) grid[y * Width + x] = d[x];
            }
            return grid;
        }

        private static void Transform(double[] f, double[] d, int[] v, double[] z, int n)
        {
            int k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s = (f[q] + (double)q * q - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = (f[q] + (double)q * q - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                d[q] = (double)(q - v[k]) * (q - v[k]) + f[v[k]];
            }
        }
    }
}
#endif
