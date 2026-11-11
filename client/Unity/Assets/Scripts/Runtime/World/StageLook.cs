using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.World
{
    /// <summary>
    /// The look a stage gives a match: the grade of the picture, a quieter background, its own pools of light
    /// with shafts and dust, and how fighters are lit and inked on it. Presentation only. A field left out of a
    /// stage's presentation keeps the value written here, which is the look of a stage without one.
    /// </summary>
    [Serializable]
    public sealed class StageLookSettings
    {
        public GradeLook Grade = new();
        public FarBlurLook FarBlur = new();
        public List<TintLook> Tints = new();
        public List<PoolLook> Pools = new();
        public List<ShaftLook> Shafts = new();
        public DustLook Dust = new();
        public FighterLook Fighters = new();

        /// <summary>The look of a stage that carries none.</summary>
        public static readonly StageLookSettings Default = new();

        /// <summary>Bloom, contrast and colour of the whole picture.</summary>
        [Serializable]
        public sealed class GradeLook
        {
            /// <summary>
            /// Neutral tonemapping rolls highlights off instead of clipping them, which suits a stage built on glow
            /// and darkens a bright one by a few percent. Off keeps the brightness the stage was authored at.
            /// </summary>
            public bool NeutralTonemap;
            public float BloomThreshold = 1f;
            public float BloomIntensity = 0.35f;
            public float BloomScatter = 0.6f;
            public float Contrast = 8f;
            public float Saturation = 6f;
            public Color Filter = Color.white;
            public bool SplitToning;
            public Color SplitShadows = new(0.5f, 0.5f, 0.5f);
            public Color SplitHighlights = new(0.5f, 0.5f, 0.5f);
            public float SplitBalance;
            public float Vignette = 0.14f;
            public Color VignetteColor = new(0.03f, 0.03f, 0.05f);
        }

        /// <summary>A soft blur on scenery beyond the playfield (metres from the camera).</summary>
        [Serializable]
        public sealed class FarBlurLook
        {
            public bool Enabled;
            public float Start = 60f;
            public float End = 200f;
            public float Radius = 1f;
        }

        /// <summary>
        /// A tint on named scenery: an exact object name, or a prefix ending in '*'. It names a renderer or an
        /// object above it, so a hand-built stage can quiet a whole group ("Mountains") in one line.
        /// </summary>
        [Serializable]
        public sealed class TintLook
        {
            public string Objects = "";
            public Color Color = Color.white;
        }

        /// <summary>
        /// A lamp over the playfield, in stage space. It lights the stage's surfaces and fighters through
        /// StageLookLights, not as a URP light, so it shows on every quality level.
        /// </summary>
        [Serializable]
        public sealed class PoolLook
        {
            public Vector3 Position = new(0f, 20f, 0f);
            public Vector3 Direction = Vector3.down;
            public Color Color = Color.white;
            public float Intensity = 100f;
            public float Range = 40f;
            public float Angle = 60f;
            public float InnerAngle = 30f;
        }

        /// <summary>A visible shaft of light in the air, from a lamp down to the floor.</summary>
        [Serializable]
        public sealed class ShaftLook
        {
            public Vector3 Top = new(0f, 20f, 0f);
            public Vector3 Bottom;
            public float TopRadius = 0.7f;
            public float BottomRadius = 4f;
            /// <summary>Alpha is the strength.</summary>
            public Color Color = new(1f, 0.9f, 0.7f, 0.14f);
        }

        /// <summary>Motes drifting through the lamp light. Count 0 is none.</summary>
        [Serializable]
        public sealed class DustLook
        {
            public int Count;
            public Vector3 Center = new(0f, 7f, 0f);
            public Vector3 Size = new(16f, 11f, 16f);
            public Color Color = new(1f, 0.92f, 0.75f, 0.7f);
            public float SizeMin = 0.04f;
            public float SizeMax = 0.09f;
        }

        /// <summary>Edge light in the stage's colours, the ink line and the contact shadow on fighters.</summary>
        [Serializable]
        public sealed class FighterLook
        {
            /// <summary>Take the two kick lights from the stage's key light; false uses the values below.</summary>
            public bool KicksFromKeyLight = true;
            public Color KickA = new(1f, 0.93f, 0.78f);
            public Vector3 KickADirection = new(-0.6f, 0.5f, -0.3f);
            public float KickAStrength = 1.2f;
            public Color KickB = new(0.62f, 0.80f, 1f);
            public Vector3 KickBDirection = new(0.7f, 0.3f, 0.4f);
            public float KickBStrength = 0.9f;
            public float KickPower = 2.6f;
            /// <summary>Extra brightness: 0.05 is five percent.</summary>
            public float Lift = 0.05f;
            /// <summary>Most light a fighter takes from the stage's lamps together. 0 is no cap.</summary>
            public float LampCap;
            /// <summary>Ink line width in pixels at 1080p. 0 is no line.</summary>
            public float Ink = 1.6f;
            public Color InkColor = new(0.09f, 0.094f, 0.078f);
            /// <summary>Strength of the soft shadow under each fighter. 0 is none.</summary>
            public float ContactShadow = 0.3f;
            public Color ContactShadowColor = new(0.05f, 0.05f, 0.08f);
            public float ContactShadowSize = 1.5f;
        }

        /// <summary>The tint of the nearest named object, from a renderer up to (not including) the stage root.</summary>
        public Color? TintFor(Transform renderer, Transform root)
        {
            for (var current = renderer; current != null && current != root; current = current.parent)
            {
                var tint = TintFor(current.name);
                if (tint != null) return tint;
            }
            return null;
        }

        /// <summary>The tint for one object name: an exact name wins, then the longest matching prefix.</summary>
        public Color? TintFor(string name)
        {
            TintLook? best = null;
            foreach (var tint in Tints)
            {
                if (!Matches(tint.Objects, name)) continue;
                if (best == null || Rank(tint.Objects) > Rank(best.Objects)) best = tint;
            }
            return best?.Color;
        }

        public static bool Matches(string pattern, string name)
            => pattern.EndsWith("*", StringComparison.Ordinal)
                ? name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal)
                : name == pattern;

        private static int Rank(string pattern) => pattern.EndsWith("*", StringComparison.Ordinal) ? pattern.Length : int.MaxValue;
    }

    /// <summary>
    /// Carries a stage's <see cref="StageLookSettings"/> on its prefab root, written by sloparena.stage.art-apply
    /// from the "look" of the stage's presentation. MatchVisualStyle reads it when a match starts; the scenery
    /// tints, light shafts and dust belong to the stage itself and are made here.
    /// </summary>
    public sealed class StageLook : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private StageLookSettings _settings = new();

        private readonly List<UnityEngine.Object> _made = new();

        public StageLookSettings Settings => _settings;

        /// <summary>The look of the match being played; the default look outside a match.</summary>
        public static StageLookSettings Active { get; internal set; } = StageLookSettings.Default;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay() => Active = StageLookSettings.Default;

        public void Configure(StageLookSettings settings) => _settings = settings;

        private void Start()
        {
            ApplyTints();
            BuildShafts();
            BuildDust();
        }

        private void OnDestroy()
        {
            foreach (var item in _made)
                if (item != null) Destroy(item);
        }

        // Scenery shares its materials with the playfield, so each tinted renderer gets a property block.
        private void ApplyTints()
        {
            if (_settings.Tints.Count == 0) return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                var tint = _settings.TintFor(renderer.transform, transform);
                if (tint == null) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null || !material.HasProperty(BaseColorId)) continue;
                    var color = material.GetColor(BaseColorId);
                    renderer.GetPropertyBlock(block, i);
                    block.SetColor(BaseColorId, new Color(color.r * tint.Value.r, color.g * tint.Value.g, color.b * tint.Value.b, color.a));
                    renderer.SetPropertyBlock(block, i);
                }
            }
        }

        private void BuildShafts()
        {
            if (_settings.Shafts.Count == 0) return;
            var shader = Resources.Load<Shader>("Shaders/StageLightShaft");
            if (shader == null) return;
            for (int i = 0; i < _settings.Shafts.Count; i++)
            {
                var shaft = _settings.Shafts[i];
                float height = (shaft.Top - shaft.Bottom).magnitude;
                if (height < 0.01f) continue;
                var go = new GameObject("Light Shaft " + (i + 1));
                go.transform.SetParent(transform, false);
                go.transform.localPosition = shaft.Top;
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.down, (shaft.Bottom - shaft.Top).normalized);
                var mesh = ConeMesh(shaft.TopRadius, shaft.BottomRadius, height);
                var material = new Material(shader) { name = "Light Shaft (runtime)" };
                material.SetColor(ColorId, shaft.Color);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _made.Add(mesh);
                _made.Add(material);
            }
        }

        // An open cone along -Y from the lamp: vertex alpha fades it from the lamp (1) to the floor (0).
        private static Mesh ConeMesh(float topRadius, float bottomRadius, float height)
        {
            const int sides = 40;
            var vertices = new List<Vector3>((sides + 1) * 2);
            var normals = new List<Vector3>((sides + 1) * 2);
            var colors = new List<Color>((sides + 1) * 2);
            var triangles = new List<int>(sides * 6);
            for (int i = 0; i <= sides; i++)
            {
                float angle = i / (float)sides * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices.Add(dir * topRadius);
                vertices.Add(dir * bottomRadius + Vector3.down * height);
                var normal = new Vector3(dir.x, (bottomRadius - topRadius) / height, dir.z).normalized;
                normals.Add(normal);
                normals.Add(normal);
                colors.Add(new Color(1f, 1f, 1f, 1f));
                colors.Add(new Color(1f, 1f, 1f, 0f));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2;
                triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1);
                triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 3);
            }
            var mesh = new Mesh { name = "Light Shaft (runtime)" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void BuildDust()
        {
            var dust = _settings.Dust;
            if (dust.Count <= 0) return;
            var shader = Resources.Load<Shader>("Shaders/StageDust");
            if (shader == null) return;
            var go = new GameObject("Lamp Dust");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = dust.Center;
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            const float meanLifetime = 9.5f;
            var main = system.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10f;
            main.maxParticles = Mathf.Max(dust.Count * 2, 16);
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(dust.SizeMin, dust.SizeMax);
            main.startColor = dust.Color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = system.emission;
            emission.rateOverTime = dust.Count / meanLifetime;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = dust.Size;
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.1f;
            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var material = new Material(shader) { name = "Lamp Dust (runtime)" };
            var particles = go.GetComponent<ParticleSystemRenderer>();
            particles.sharedMaterial = material;
            particles.shadowCastingMode = ShadowCastingMode.Off;
            particles.receiveShadows = false;
            _made.Add(material);
            system.Play(true);
        }
    }
}
