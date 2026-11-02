#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// The "flyer burst" played where a fighter crosses a blast line, shared by every fighter and
    /// presentation-only: an Orange cut-paper starburst with an ink rim, a hard ink shadow and an
    /// out-of-register Action Yellow copy, a halftone trail in the fighter's seat colour pointing
    /// back at the fight, and paper scraps. This builds the shapes as a function of the burst's
    /// age; KnockoutBurst.shader draws them as a depth-tested card facing each camera.
    /// </summary>
    internal sealed class KnockoutBurst : MonoBehaviour
    {
        // The shared graphic palette: Action Yellow, Orange and Paper (docs/design/visual-language.md).
        private static readonly Color ActionYellow = new(1f, 0.8f, 0.133f, 1f);
        private static readonly Color Orange = new(0.941f, 0.357f, 0.208f, 1f);
        private static readonly Color Paper = new(0.871f, 0.847f, 0.788f, 1f);

        private const float Lifetime = 0.8f;
        private const float BurstRadius = 1.5f, PeakPop = 1.15f;
        private const float TrailLength = 4.2f, TrailHalfWidth = 0.75f;
        // The burst and scrap quads are padded so the print shadow fits inside them.
        private const float BurstPadding = 1.3f, ScrapPadding = 1.4f;
        private const int ScrapCount = 12;
        private const float ScrapDrag = 3.2f, ScrapGravity = 7f;
        private const float BurstKind = 0f, TrailKind = 1f, ScrapKind = 2f;

        private struct Scrap
        {
            public Vector2 Velocity, Half;   // launch velocity and half size in screen-plane metres
            public float Angle, Spin;
            public Color Color;
        }

        // Checked by value: Play Mode without a domain reload destroys a material made in Edit Mode.
        private static Material? _material;

        private readonly Scrap[] _scraps = new Scrap[ScrapCount];
        private Vector3 _origin;
        private Color _seat;
        private float _age, _angle, _seed;
        private Mesh _mesh = null!;
        private readonly List<Vector3> _origins = new();
        private readonly List<Color> _colors = new();
        private readonly List<Vector2> _corners = new();
        private readonly List<Vector4> _shapes = new();
        private readonly List<Vector4> _data = new();
        private readonly List<Vector2> _offsets = new();
        private readonly List<int> _indices = new();

        /// <summary>Play one burst at the blast-line crossing, accented with the fighter's seat colour.</summary>
        public static void Spawn(Vector3 position, Color seat)
        {
            if (!LoadMaterial()) return;
            new GameObject("Knockout Burst").AddComponent<KnockoutBurst>().Begin(position, seat);
        }

        private void Begin(Vector3 position, Color seat)
        {
            _origin = position;
            _seat = seat;
            var random = new System.Random(Time.frameCount * 7919 + position.GetHashCode());
            float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
            _angle = Range(0f, Mathf.PI * 2f);
            _seed = Range(0f, 100f);
            Color[] fills = { Paper, Paper, ActionYellow, seat };
            for (int i = 0; i < _scraps.Length; i++)
            {
                float direction = (i + Range(-0.5f, 0.5f)) / _scraps.Length * Mathf.PI * 2f;
                _scraps[i] = new Scrap
                {
                    Velocity = new Vector2(Mathf.Cos(direction), Mathf.Sin(direction)) * Range(3f, 10f),
                    Half = new Vector2(Range(0.12f, 0.22f), Range(0.06f, 0.11f)),
                    Angle = Range(0f, Mathf.PI),
                    Spin = Range(-14f, 14f),
                    Color = fills[i % fills.Length],
                };
            }

            _mesh = new Mesh { name = "Knockout Burst" };
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            BuildMesh();
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            if (_age >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }
            BuildMesh();
        }

        private void BuildMesh()
        {
            _origins.Clear(); _colors.Clear(); _corners.Clear(); _shapes.Clear(); _data.Clear(); _offsets.Clear(); _indices.Clear();

            // Trail: grows out over 0.12 s, its dots shrink away after 0.22 s. The shader aims it.
            float grow = Mathf.Clamp01(_age / 0.12f);
            float fade = Mathf.Clamp01((_age - 0.22f) / 0.3f);
            if (fade < 1f)
                Quad(Vector2.zero, new Vector2(TrailLength * 0.5f, TrailHalfWidth), 0f, TrailKind, _seat,
                    new Vector4(grow, fade, 0f, 0f));

            // Scraps fly out against drag and fall, in closed form so the burst is a function of
            // its age alone; they come out from behind the burst and shrink out over their last 0.15 s.
            float decay = (1f - Mathf.Exp(-ScrapDrag * _age)) / ScrapDrag;
            float shrink = Mathf.Clamp01((Lifetime - _age) / 0.15f);
            foreach (var scrap in _scraps)
            {
                Vector2 offset = scrap.Velocity * decay - Vector2.up * (ScrapGravity * (_age - decay) / ScrapDrag);
                Quad(offset, scrap.Half * (ScrapPadding * shrink), scrap.Angle + scrap.Spin * _age, ScrapKind,
                    scrap.Color, Vector4.zero);
            }

            // Burst: pops past full size, settles, holds, then snaps shut.
            float pop = _age < 0.07f ? Mathf.Lerp(0.35f, PeakPop, 1f - (1f - _age / 0.07f) * (1f - _age / 0.07f))
                : _age < 0.16f ? Mathf.Lerp(PeakPop, 1f, (_age - 0.07f) / 0.09f)
                : _age < 0.42f ? 1f
                : 1f - Mathf.Clamp01((_age - 0.42f) / 0.14f) * Mathf.Clamp01((_age - 0.42f) / 0.14f);
            if (pop > 0.01f)
            {
                float half = BurstRadius * BurstPadding * pop;
                Quad(Vector2.zero, new Vector2(half, half), 0f, BurstKind, Orange,
                    new Vector4(_angle + _age * 0.25f, _seed, 0f, 0f));
            }

            _mesh.Clear();
            _mesh.SetVertices(_origins);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _corners);
            _mesh.SetUVs(1, _shapes);
            _mesh.SetUVs(2, _data);
            _mesh.SetUVs(3, _offsets);
            _mesh.SetTriangles(_indices, 0, false);
            // The shader lays the quads out in the screen plane, so the vertices all sit at the origin.
            _mesh.bounds = new Bounds(_origin, Vector3.one * 20f);
        }

        /// <summary>One quad at an offset from the burst centre; the shader draws its kind's shape.</summary>
        private void Quad(Vector2 offset, Vector2 half, float rotation, float kind, Color color, Vector4 data)
        {
            int start = _origins.Count;
            for (int corner = 0; corner < 4; corner++)
            {
                _origins.Add(_origin);
                _colors.Add(color);
                _corners.Add(new Vector2(corner is 1 or 2 ? 1f : 0f, corner >= 2 ? 1f : 0f));
                _shapes.Add(new Vector4(half.x, half.y, rotation, kind));
                _data.Add(data);
                _offsets.Add(offset);
            }
            _indices.Add(start); _indices.Add(start + 1); _indices.Add(start + 2);
            _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 3);
        }

        private static bool LoadMaterial()
        {
            if (_material != null) return true;
            var shader = Resources.Load<Shader>("Shaders/KnockoutBurst");
            if (shader == null)
            {
                Debug.LogWarning("[KnockoutBurst] Resources/Shaders/KnockoutBurst is missing.");
                return false;
            }
            _material = new Material(shader);
            return true;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
