using SlopArena.Client.UI;
using System.Collections.Generic;

using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Combat
{
    public enum ImpactTier : byte
    {
        Light,
        Medium,
        Heavy,
        Launch,
        Block,
    }

    /// <summary>
    /// Pooled, single-mesh directional impact shape. Shared tiers communicate gameplay
    /// strength; character-specific particles may be layered over this grammar later.
    /// </summary>
    public sealed class GraphicHitEffect : MonoBehaviour
    {
        private const int InitialPoolSize = 16;
        private static readonly Stack<GraphicHitEffect> Pool = new();
        private static Material _sharedMaterial;

        private readonly List<Vector3> _vertices = new(192);
        private readonly List<Color> _colors = new(192);
        private readonly List<int> _triangles = new(288);
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _properties;
        private float _age;
        private float _holdDuration;
        private float _fadeDuration;
        private float _endScale;
        private float _flashOpacity = 1f;
        private Vector3 _launch;
        private Vector2 _localLaunch;
        private ImpactTier _tier;

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += FaceCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= FaceCamera;

        private void FaceCamera(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (_tier == ImpactTier.Block || camera == null) return;
            transform.rotation = camera.transform.rotation;
            Vector2 direction = LocalLaunch(camera);
            // Radial rays stay fixed on screen; only the directional streak follows the launch.
            if ((direction - _localLaunch).sqrMagnitude > 0.000001f)
            {
                _localLaunch = direction;
                BuildMesh(_tier, direction);
            }
        }

        private Vector2 LocalLaunch(UnityEngine.Camera camera)
        {
            Vector2 direction = camera != null
                ? new Vector2(Vector3.Dot(_launch, camera.transform.right), Vector3.Dot(_launch, camera.transform.up))
                : new Vector2(_launch.x, _launch.y);
            return direction.sqrMagnitude < 0.001f ? Vector2.right : direction.normalized;
        }

        public static void Prewarm()
        {
            EnsureMaterial();
            while (Pool.Count < InitialPoolSize)
            {
                var effect = CreateInstance();
                effect.gameObject.SetActive(false);
                Pool.Push(effect);
            }
        }

        public static void Spawn(in SpellResolver.HitResult hit, ImpactTier tier)
            => Spawn(in hit, tier, Vector3.zero);

        private static void Spawn(in SpellResolver.HitResult hit, ImpactTier tier, Vector3 surfaceNormal)
        {
            GraphicHitEffect effect = null;
            while (Pool.Count > 0 && effect == null)
                effect = Pool.Pop();
            if (effect == null)
                effect = CreateInstance();
            effect.Show(in hit, tier, surfaceNormal);
        }
        /// <summary>Temporary blue guard ripple; no damaging-hit VFX or SFX.</summary>
        public static void SpawnBlock(Vector3 position, Vector3 surfaceNormal)
        {
            var hit = new SpellResolver.HitResult
            {
                HitX = position.x, HitY = position.y, HitZ = position.z,
            };
            Spawn(in hit, ImpactTier.Block, surfaceNormal);
        }

        private static GraphicHitEffect CreateInstance()
        {
            var effectObject = new GameObject("Graphic Hit");
            var filter = effectObject.AddComponent<MeshFilter>();
            var renderer = effectObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = EnsureMaterial();

            var effect = effectObject.AddComponent<GraphicHitEffect>();
            effect._mesh = new Mesh { name = "GraphicHitMesh" };
            effect._mesh.MarkDynamic();
            effect._renderer = renderer;
            effect._properties = new MaterialPropertyBlock();
            filter.sharedMesh = effect._mesh;
            return effect;
        }

        private static Material EnsureMaterial()
        {
            if (_sharedMaterial != null)
                return _sharedMaterial;

            var shader = Shader.Find("Sprites/Default");
            _sharedMaterial = new Material(shader)
            {
                name = "GraphicHitShared",
                hideFlags = HideFlags.HideAndDontSave,
            };
            return _sharedMaterial;
        }

        private void Show(in SpellResolver.HitResult hit, ImpactTier tier, Vector3 surfaceNormal)
        {
            var camera = UnityEngine.Camera.main;
            if (camera == null) camera = Object.FindFirstObjectByType<UnityEngine.Camera>();

            transform.position = new Vector3(hit.HitX, hit.HitY, hit.HitZ);
            transform.rotation = tier == ImpactTier.Block && surfaceNormal.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(surfaceNormal, Mathf.Abs(surfaceNormal.y) > 0.95f
                    ? Vector3.forward : Vector3.up)
                : camera != null ? camera.transform.rotation : Quaternion.identity;
            transform.localScale = Vector3.one * 0.72f;

            _tier = tier;
            _launch = ResolveLaunch(in hit);
            _localLaunch = LocalLaunch(camera);

            _flashOpacity = ClientSettingsService.Instance.ReducedFlashing ? 0.48f : 1f;
            BuildMesh(tier, _localLaunch);
            _age = 0f;
            _holdDuration = hit.HitstopTicks * SlopArena.Shared.Simulation.TickDt;
            _fadeDuration = tier switch
            {
                ImpactTier.Light => 0.08f,
                ImpactTier.Medium => 0.10f,
                ImpactTier.Heavy => 0.13f,
                ImpactTier.Block => 0.12f,
                _ => 0.16f,
            };
            _endScale = tier switch
            {
                ImpactTier.Light => 1.15f,
                ImpactTier.Medium => 1.4f,
                ImpactTier.Heavy => 1.7f,
                ImpactTier.Block => 1.35f,
                _ => 2.05f,
            };

            _properties.SetColor("_Color", new Color(1f, 1f, 1f, _flashOpacity));
            _renderer.SetPropertyBlock(_properties);
            gameObject.SetActive(true);
        }

        private static Vector3 ResolveLaunch(in SpellResolver.HitResult hit)
        {
            float angle = hit.KnockbackAngle * Mathf.Deg2Rad;
            var launch = new Vector3(
                hit.DirX * Mathf.Cos(angle),
                Mathf.Sin(angle),
                hit.DirZ * Mathf.Cos(angle));
            return launch.sqrMagnitude > 0.001f ? launch.normalized : Vector3.forward;
        }

        private void BuildMesh(ImpactTier tier, Vector2 launch)
        {
            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();

            Color core = tier switch
            {
                ImpactTier.Light => new Color(1f, 0.96f, 0.72f, 0.95f),
                ImpactTier.Medium => new Color(1f, 0.76f, 0.24f, 0.98f),
                ImpactTier.Heavy => new Color(1f, 0.33f, 0.08f, 1f),
                ImpactTier.Block => new Color(0.25f, 0.8f, 1f, 0.9f),
                _ => new Color(1f, 0.18f, 0.08f, 1f),
            };
            Color pale = new(1f, 0.98f, 0.88f, 0.92f);
            Vector2 incoming = -launch;

            switch (tier)
            {
                case ImpactTier.Light:
                    AddRing(0.28f, 0.055f, 12, core);
                    AddRadialRays(4, 0.12f, 0.48f, 0.07f, core, 0.25f);
                    AddRay(Vector2.zero, incoming * 0.58f, 0.11f, 0.025f, pale);
                    break;

                case ImpactTier.Medium:
                    AddRing(0.4f, 0.075f, 16, core);
                    AddRadialRays(6, 0.18f, 0.76f, 0.09f, core, 0.16f);
                    AddRay(launch * 0.12f, incoming * 0.94f, 0.17f, 0.035f, pale);
                    break;

                case ImpactTier.Heavy:
                    AddRing(0.52f, 0.095f, 18, core);
                    AddRing(0.24f, 0.12f, 12, pale);
                    AddRadialRays(9, 0.2f, 1.05f, 0.12f, core, 0.1f);
                    AddRay(launch * 0.18f, incoming * 1.35f, 0.24f, 0.045f, pale);
                    break;

                case ImpactTier.Launch:
                    AddRing(0.62f, 0.11f, 20, core);
                    AddRing(0.31f, 0.08f, 14, pale);
                    AddRadialRays(11, 0.25f, 1.25f, 0.12f, core, 0.08f);
                    AddRay(launch * 0.22f, incoming * 1.8f, 0.28f, 0.035f, pale);
                    AddRay(launch.Perpendicular() * 0.16f, incoming * 1.35f + launch.Perpendicular() * 0.35f,
                        0.13f, 0.025f, core);
                    AddRay(-launch.Perpendicular() * 0.16f, incoming * 1.35f - launch.Perpendicular() * 0.35f,
                        0.13f, 0.025f, core);
                    break;
                case ImpactTier.Block:
                    AddRing(0.4f, 0.09f, 18, core);
                    AddRing(0.2f, 0.04f, 14, pale);
                    break;
            }

            _mesh.Clear(false);
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0, false);
            _mesh.RecalculateBounds();
        }

        private void AddRadialRays(int count, float startRadius, float endRadius,
            float width, Color color, float phase)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count + phase;
                Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
                float alternating = i % 2 == 0 ? 1f : 0.76f;
                AddRay(direction * startRadius, direction * endRadius * alternating,
                    width, width * 0.18f, color);
            }
        }

        private void AddRing(float radius, float width, int segments, Color color)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector2 start = new(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius);
                Vector2 end = new(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                AddRay(start, end, width, width, color);
            }
        }

        private void AddRay(Vector2 start, Vector2 end, float startWidth, float endWidth, Color color)
        {
            Vector2 direction = end - start;
            if (direction.sqrMagnitude < 0.0001f)
                return;
            direction.Normalize();
            Vector2 normal = new(-direction.y, direction.x);
            int first = _vertices.Count;

            _vertices.Add(new Vector3(start.x + normal.x * startWidth, start.y + normal.y * startWidth));
            _vertices.Add(new Vector3(start.x - normal.x * startWidth, start.y - normal.y * startWidth));
            _vertices.Add(new Vector3(end.x - normal.x * endWidth, end.y - normal.y * endWidth));
            _vertices.Add(new Vector3(end.x + normal.x * endWidth, end.y + normal.y * endWidth));
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
            _triangles.Add(first);
            _triangles.Add(first + 1);
            _triangles.Add(first + 2);
            _triangles.Add(first);
            _triangles.Add(first + 2);
            _triangles.Add(first + 3);
        }

        private void Update()
        {
#if UNITY_EDITOR
            _age += Time.captureDeltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
#else
            _age += Time.unscaledDeltaTime;
#endif
            if (_age <= _holdDuration)
            {
                float settle = _holdDuration > 0f ? _age / _holdDuration : 1f;
                transform.localScale = Vector3.one * Mathf.Lerp(0.72f, 1f, settle);
                return;
            }

            float t = Mathf.Clamp01((_age - _holdDuration) / _fadeDuration);
            float expansion = 1f - Mathf.Pow(1f - t, 3f);
            transform.localScale = Vector3.one * Mathf.Lerp(1f, _endScale, expansion);
            _properties.SetColor("_Color", new Color(1f, 1f, 1f, (1f - t) * _flashOpacity));
            _renderer.SetPropertyBlock(_properties);

            if (t >= 1f)
                Recycle();
        }

        private void Recycle()
        {
            gameObject.SetActive(false);
            Pool.Push(this);
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }

    internal static class ImpactVectorExtensions
    {
        public static Vector2 Perpendicular(this Vector2 value) => new(-value.y, value.x);
    }
}
