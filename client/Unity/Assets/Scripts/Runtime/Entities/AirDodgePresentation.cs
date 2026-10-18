#nullable enable
using System.Collections.Generic;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// The "print slip" air dodge, shared by every fighter and presentation-only. On the push-off
    /// two cut-paper swooshes curl behind the launch point and a paper "fwip" plays. While the
    /// dodge is invulnerable the fighter is printed out of register: Action Yellow and orange
    /// copies of the model sit behind it on screen with flat printed motion bars, and they snap
    /// back the tick it can be hit again. It follows the state the renderer applies
    /// (AirDodgeMovement, DashDirX/Z, InvincibilityTicks) and never drives gameplay.
    /// </summary>
    [DefaultExecutionOrder(950)] // after FighterHitFeel deforms the model
    public sealed class AirDodgePresentation : MonoBehaviour
    {
        // The shared graphic palette: Action Yellow, Orange and Paper (docs/design/visual-language.md).
        private static readonly Color ActionYellow = new(1f, 0.8f, 0.133f, 1f);
        private static readonly Color Orange = new(0.949f, 0.42f, 0.114f, 1f);
        private static readonly Color Paper = new(0.871f, 0.847f, 0.788f, 1f);

        private const float Tick = SlopArena.Shared.Simulation.TickDt;
        // Misprint copies: layer k sits k steps back along the dodge, at a fixed on-screen length.
        private const float SlipStep = 0.075f;
        private const float SlipDepthPush = 0.45f;
        private const float SlipSettleTicks = 4f;   // full slip, then 75% until the window ends
        private const float SnapBackTicks = 2f;
        // Push-off swooshes: two crescents of different sizes behind the launch point.
        private const float SwooshTicks = 7f;
        private static readonly (float size, float back)[] Swooshes = { (1.25f, 0.42f), (0.8f, 0.18f) };
        // Printed bars, behind the body at fractions of the capsule height above the feet.
        private const float BarGrowTicks = 2f, BarFadeTicks = 5f;
        private const float BarThickness = 0.085f, BarGap = 0.42f;
        private static readonly float[] BarHeights = { 0.74f, 0.52f, 0.3f };

        private static Shader? _printShader, _slipShader;
        private static AudioClip? _fwip;
        private static bool _loaded;

        private PlayerRenderer _renderer = null!;
        private Transform? _model;
        private Material? _printMaterial;
        private readonly Material?[] _layers = new Material?[2];
        private readonly List<(Renderer source, Renderer[] copies)> _slips = new();
        private readonly HashSet<Renderer> _copies = new();
        private Mesh? _mesh;
        private readonly List<Vector3> _centers = new();
        private readonly List<Color> _colors = new();
        private readonly List<Vector2> _corners = new();
        private readonly List<Vector4> _shapes = new();
        private readonly List<Vector3> _axes = new();
        private readonly List<int> _indices = new();

        private bool _moving, _invulnerable, _active;
        private byte _deaths;
        // Applied simulation ticks since the push-off, so captures that step one tick per
        // frame time the effect like live play; the frame fraction smooths it in between.
        private int _ticks, _endTick = int.MaxValue;
        private float _tickTime;
        private Vector3 _direction, _launch, _lastCenter;
        private bool _hasLastCenter;
        private readonly float[] _barLengths = new float[3];
        private int _dodges;

        public void Init(PlayerRenderer renderer)
        {
            _renderer = renderer;
            LoadShared();
        }

        /// <summary>Use the model the renderer just loaded; copies are rebuilt on the next dodge.</summary>
        public void AttachModel(Transform model)
        {
            Clear();
            _model = model;
        }

        /// <summary>Call once per applied simulation tick, after the renderer has applied the state.</summary>
        public void OnState(in CharacterState state)
        {
            var center = new Vector3(state.PX, state.PY, state.PZ);
            if (state.Deaths != _deaths)
            {
                _deaths = state.Deaths;
                Clear();
                _hasLastCenter = false;
            }
            bool moving = state.State == ActionState.AirDodgeMovement;
            if (moving && !_moving)
                Begin(state, center);
            else if (_active)
                _ticks++;
            _tickTime = Time.time;
            _moving = moving;
            bool invulnerable = moving && state.InvincibilityTicks > 0;
            if (_invulnerable && !invulnerable)
                _endTick = _ticks;
            _invulnerable = invulnerable;
            _lastCenter = center;
            _hasLastCenter = true;
        }

        /// <summary>A fresh simulation (Arcade retry) or a hidden fighter: drop everything at once.</summary>
        public void Clear()
        {
            _moving = _invulnerable = _active = false;
            _endTick = int.MaxValue;
            SetCopies(false);
        }

        private void Begin(in CharacterState state, Vector3 center)
        {
            _direction = new Vector3(state.DashDirX, 0f, state.DashDirZ);
            if (_direction.sqrMagnitude < 1e-6f)
                _direction = transform.forward;
            _direction.Normalize();
            // The press tick already moved; the swooshes mark where the dodge left from.
            _launch = _hasLastCenter && (center - _lastCenter).sqrMagnitude < 4f ? _lastCenter : center;
            _ticks = 0;
            _endTick = int.MaxValue;
            _active = true;
            var random = new System.Random(unchecked(++_dodges * 7919 + (int)_renderer.EntityId));
            for (int i = 0; i < _barLengths.Length; i++)
                _barLengths[i] = Mathf.Lerp(0.55f, 1.05f, (float)random.NextDouble());
            EnsureCopies();
            if (_fwip != null)
                _renderer.PlayOneShot(_fwip, 0.9f);
        }

        private void LateUpdate()
        {
            if (!_active) return;
            float ticks = _ticks + Mathf.Clamp01((Time.time - _tickTime) / Tick);
            float sinceEnd = _invulnerable || _endTick == int.MaxValue ? -1f : ticks - _endTick;
            Vector3 center = transform.position - Vector3.up * _renderer.ModelYOffset;

            // Misprint copies: full slip, then 75%, snapping back once the dodge can be hit.
            float slip = _invulnerable
                ? (ticks < SlipSettleTicks ? 1f : 0.75f)
                : sinceEnd < SnapBackTicks ? 0.75f * (1f - sinceEnd / SnapBackTicks) : 0f;
            UpdateCopies(center, slip);

            // Bars grow in, hold while untouchable, then fade and shrink.
            float bars = Mathf.Clamp01(ticks / BarGrowTicks)
                * (_invulnerable ? 1f : Mathf.Clamp01(1f - sinceEnd / BarFadeTicks));
            float swoosh = ticks < SwooshTicks ? 1f - ticks / SwooshTicks : 0f;
            BuildMesh(center, bars, ticks, swoosh);
            if (_indices.Count > 0 && _printMaterial != null && _mesh != null)
                Graphics.DrawMesh(_mesh, Matrix4x4.identity, _printMaterial, gameObject.layer);
            if (bars <= 0f && swoosh <= 0f && slip <= 0f && !_moving)
                _active = false;
        }

        // ── Misprint copies ─────────────────────────────────────────────────────

        private void UpdateCopies(Vector3 center, float slip)
        {
            bool show = slip > 0f;
            SetCopies(show);
            if (!show) return;
            for (int layer = 0; layer < _layers.Length; layer++)
            {
                var material = _layers[layer];
                if (material == null) continue;
                // Layer 0 is the orange copy one step back, layer 1 the yellow copy two steps back.
                material.SetVector(AnchorId, center);
                material.SetVector(SlipId, -_direction * (SlipStep * (layer + 1) * slip));
            }
        }

        private void SetCopies(bool visible)
        {
            foreach (var (source, copies) in _slips)
                foreach (var copy in copies)
                    if (copy != null) copy.enabled = visible && source != null && source.enabled && source.gameObject.activeInHierarchy;
        }

        private void EnsureCopies()
        {
            if (_model == null || _slipShader == null) return;
            if (_layers[0] == null)
            {
                _layers[0] = NewSlipMaterial(Orange, -20);
                _layers[1] = NewSlipMaterial(ActionYellow, -21);
            }
            _slips.RemoveAll(slip => slip.source == null);
            _copies.RemoveWhere(copy => copy == null);
            foreach (var renderer in _model.GetComponentsInChildren<Renderer>())
            {
                if (_copies.Contains(renderer) || _slips.Exists(slip => slip.source == renderer)) continue;
                // Body parts and held props only: no particles, trails or transparent effects.
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                    _slips.Add((renderer, new[] { CopySkin(skin, _layers[1]!, "Yellow"), CopySkin(skin, _layers[0]!, "Orange") }));
                else if (renderer is MeshRenderer mesh && mesh.sharedMaterial != null && mesh.sharedMaterial.renderQueue < 2500
                         && mesh.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                    _slips.Add((renderer, new[] { CopyMesh(mesh, filter.sharedMesh, _layers[1]!, "Yellow"), CopyMesh(mesh, filter.sharedMesh, _layers[0]!, "Orange") }));
            }
        }

        private Renderer CopySkin(SkinnedMeshRenderer source, Material material, string label)
        {
            var copy = NewCopyObject(source, label).AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = source.sharedMesh;
            copy.bones = source.bones;
            copy.rootBone = source.rootBone;
            copy.localBounds = source.localBounds;
            copy.updateWhenOffscreen = source.updateWhenOffscreen;
            copy.quality = source.quality;
            return Finish(copy, material, source.sharedMesh.subMeshCount);
        }

        private Renderer CopyMesh(MeshRenderer source, Mesh mesh, Material material, string label)
        {
            var copyObject = NewCopyObject(source, label);
            copyObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            return Finish(copyObject.AddComponent<MeshRenderer>(), material, mesh.subMeshCount);
        }

        private static GameObject NewCopyObject(Renderer source, string label)
        {
            var copy = new GameObject($"{source.name} Print Slip {label}");
            copy.transform.SetParent(source.transform, false);
            copy.layer = source.gameObject.layer;
            return copy;
        }

        private Renderer Finish(Renderer copy, Material material, int subMeshes)
        {
            var materials = new Material[Mathf.Max(1, subMeshes)];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            copy.sharedMaterials = materials;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off;
            copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            copy.enabled = false;
            _copies.Add(copy);
            return copy;
        }

        private static Material NewSlipMaterial(Color color, int queueOffset)
        {
            var material = new Material(_slipShader) { renderQueue = (int)RenderQueue.Transparent + queueOffset };
            material.SetColor(ColorId, color);
            material.SetFloat(DepthPushId, SlipDepthPush);
            return material;
        }

        // ── Bars and swooshes ───────────────────────────────────────────────────

        private void BuildMesh(Vector3 center, float bars, float ticks, float swoosh)
        {
            _centers.Clear(); _colors.Clear(); _corners.Clear(); _shapes.Clear(); _axes.Clear(); _indices.Clear();
            float height = Mathf.Max(0.5f, _renderer.CapsuleHeight);
            if (bars > 0f)
            {
                Vector3 feet = center - Vector3.up * (height * 0.5f);
                for (int i = 0; i < BarHeights.Length; i++)
                {
                    float length = _barLengths[i] * bars;
                    if (length < 0.02f) continue;
                    // The bar's front end sits just behind the body; it reaches back from there.
                    Vector3 barCenter = feet + Vector3.up * (height * BarHeights[i])
                        - _direction * (BarGap + length * 0.5f);
                    Quad(barCenter, length, BarThickness, 0f, new Color(ActionYellow.r, ActionYellow.g, ActionYellow.b, bars));
                }
            }
            if (swoosh > 0f)
            {
                // Pop slightly larger, settle, then fade over the last half.
                float t = ticks / SwooshTicks;
                float scale = t < 0.2f ? Mathf.Lerp(0.6f, 1.08f, t / 0.2f) : Mathf.Lerp(1.08f, 0.96f, (t - 0.2f) / 0.8f);
                float alpha = Mathf.Clamp01(swoosh * 2f);
                foreach (var (size, back) in Swooshes)
                {
                    float s = size * height * 0.55f * scale;
                    Quad(_launch - _direction * back, s, s, 1f, new Color(Paper.r, Paper.g, Paper.b, alpha));
                }
            }
            if (_indices.Count == 0) return;
            _mesh ??= new Mesh { name = "Air Dodge Print" };
            _mesh.Clear();
            _mesh.SetVertices(_centers);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _corners);
            _mesh.SetUVs(1, _shapes);
            _mesh.SetUVs(2, _axes);
            _mesh.SetTriangles(_indices, 0, false);
            // The shader moves the corners away from the shared centre; never cull it.
            _mesh.bounds = new Bounds(center, Vector3.one * 40f);
        }

        /// <summary>One shape as two quads: its ink print shadow first, then the shape.</summary>
        private void Quad(Vector3 position, float length, float thickness, float shape, Color color)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int start = _centers.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    _centers.Add(position);
                    _colors.Add(color);
                    _corners.Add(new Vector2(corner is 1 or 2 ? 1f : 0f, corner >= 2 ? 1f : 0f));
                    _shapes.Add(new Vector4(length, thickness, shape, pass == 0 ? 1f : 0f));
                    _axes.Add(_direction);
                }
                _indices.Add(start); _indices.Add(start + 1); _indices.Add(start + 2);
                _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 3);
            }
        }

        // ── Shared resources ────────────────────────────────────────────────────

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int AnchorId = Shader.PropertyToID("_Anchor");
        private static readonly int SlipId = Shader.PropertyToID("_Slip");
        private static readonly int DepthPushId = Shader.PropertyToID("_DepthPush");

        private void LoadShared()
        {
            if (!_loaded)
            {
                _loaded = true;
                _printShader = Resources.Load<Shader>("Shaders/AirDodgePrint");
                _slipShader = Resources.Load<Shader>("Shaders/AirDodgePrintSlip");
                _fwip = Resources.Load<AudioClip>("Audio/SFX/AirDodge/air_dodge_fwip");
                if (_printShader == null || _slipShader == null)
                    Debug.LogWarning("[AirDodgePresentation] Air dodge print shaders are missing from Resources/Shaders.");
            }
            if (_printShader != null && _printMaterial == null)
                _printMaterial = new Material(_printShader);
        }

        private void OnDisable() => Clear();

        private void OnDestroy()
        {
            Clear();
            if (_mesh != null) Destroy(_mesh);
            if (_printMaterial != null) Destroy(_printMaterial);
            foreach (var material in _layers)
                if (material != null) Destroy(material);
        }
    }
}
