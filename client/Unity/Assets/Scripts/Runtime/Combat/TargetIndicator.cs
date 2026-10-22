using System;
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Small broken floor rings and contact shadows, one per player entity. Pinned to the
    /// arena heightmap so position stays readable while airborne; hidden without floor.
    /// Airborne fighters also get a dotted orange drop line from their feet down to the ring,
    /// with dots at a fixed world spacing so a higher jump shows more of them.
    /// The local fighter has a yellow notch. Selection and
    /// lock feedback moved to TargetLockIndicator (targeting pass 1). The launch
    /// trajectory remains editor gizmos only, never gameplay.
    /// </summary>
    public class TargetIndicator : MonoBehaviour
    {
        private readonly struct Ring
        {
            public readonly PlayerRenderer Renderer;
            public readonly ulong EntityId;
            public readonly Transform Transform;
            public readonly MeshRenderer Mesh;
            public readonly LineRenderer DropLine;
            public readonly Color Accent;
            public readonly Color CampColor;

            public Ring(PlayerRenderer renderer, Transform transform, MeshRenderer mesh,
                LineRenderer dropLine, Color accent, Color campColor)
            {
                Renderer = renderer;
                EntityId = renderer != null ? renderer.EntityId : 0;
                Transform = transform;
                Mesh = mesh;
                DropLine = dropLine;
                Accent = accent;
                CampColor = campColor;
            }
        }
        private const float RingHeightAboveFloor = 0.025f;
        private static readonly Color Paper = new Color(240f / 255f, 238f / 255f, 232f / 255f);
        private static readonly Color LocalAccent = new Color(1f, 204f / 255f, 34f / 255f);
        private static readonly Color DropLineOrange = new Color(1f, 176f / 255f, 59f / 255f);
        private static readonly int AirborneProperty = Shader.PropertyToID("_Airborne");
        private static readonly int AccentProperty = Shader.PropertyToID("_Accent");
        private static readonly int CampColorProperty = Shader.PropertyToID("_CampColor");
        private static readonly int LengthInWidthsProperty = Shader.PropertyToID("_LengthInWidths");
        private static readonly int SpacingProperty = Shader.PropertyToID("_Spacing");
        private static readonly int CoreProperty = Shader.PropertyToID("_Core");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        [Header("Drop Line")]
        [SerializeField, Min(0.005f)] private float _dropLineWidth = 0.05f;
        [Tooltip("Smallest dot diameter on screen, so distant fighters keep a readable line.")]
        [SerializeField, Min(0f)] private float _dropLineMinPixels = 4f;
        [Tooltip("Dot spacing in line widths.")]
        [SerializeField, Min(1.05f)] private float _dropLineSpacing = 2f;
        [Tooltip("Light core radius in line widths; the rest of the dot is a dark rim.")]
        [SerializeField, Range(0f, 0.5f)] private float _dropLineCore = 0.3f;
        [SerializeField] private Color _dropLineColor = DropLineOrange;
        /// <summary>Min knockback speed that counts as a launch (State == Hitstun).</summary>
        private const float LaunchSpeedThreshold = 0.05f;
        /// <summary>Max ticks to forward-simulate a launch (mirrors AbilityLab, ~40s).</summary>
        private const int MaxTrajectoryTicks = 2400;

        private Ring[] _rings = Array.Empty<Ring>();
        private Func<ulong, CharacterState> _getState;
        private ArenaHeightmap _heightmap;
        private ArenaDefinition _arena;
        private Material _markerMaterial;
        private MaterialPropertyBlock _markerProperties;
        private Material _dropLineMaterial;
        private MaterialPropertyBlock _dropLineProperties;
        private UnityEngine.Camera _camera;

        // Per-ring indicator state, indexed identically to _rings.
        private List<(Vector3 pos, char phase)>[] _arcPoints = Array.Empty<List<(Vector3 pos, char phase)>>();
        private bool[] _wasLaunched = Array.Empty<bool>();
        private bool[] _arcActive = Array.Empty<bool>();

        public void Init(Func<ulong, CharacterState> getState, PlayerRenderer[] renderers,
            ArenaDefinition arena, ulong localEntityId = 0, UnityEngine.Camera renderCamera = null,
            Func<ulong, SlopballCamp> campOf = null)
        {
            _getState = getState;
            _heightmap = arena.Heightmap;
            _arena = arena;
            _camera = renderCamera;
            _markerMaterial = new Material(Resources.Load<Shader>("Shaders/FighterGroundMarker"));
            _markerProperties = new MaterialPropertyBlock();
            _dropLineMaterial = new Material(Resources.Load<Shader>("Shaders/FighterDropLine"));
            _dropLineProperties = new MaterialPropertyBlock();

            _rings = new Ring[renderers?.Length ?? 0];
            _arcPoints = new List<(Vector3, char)>[_rings.Length];
            _wasLaunched = new bool[_rings.Length];
            _arcActive = new bool[_rings.Length];

            for (int i = 0; i < _rings.Length; i++)
            {
                var renderer = renderers[i];
                var ringGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ringGO.name = "FighterGroundMarker";
                ringGO.transform.SetParent(transform, false);
                ringGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ringGO.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
                ringGO.transform.localPosition = Vector3.zero;
                Destroy(ringGO.GetComponent<MeshCollider>());
                var mr = ringGO.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _markerMaterial;
                mr.receiveShadows = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Color accent = renderer != null && renderer.EntityId == localEntityId ? LocalAccent : Paper;
                var camp = renderer != null ? campOf?.Invoke(renderer.EntityId) ?? SlopballCamp.None : SlopballCamp.None;
                Color campColor = camp == SlopballCamp.None ? Color.clear : camp == SlopballCamp.Teal
                    ? new Color(.22f, .9f, .85f) : new Color(1f, .55f, .35f);
                if (camp != SlopballCamp.None && renderer.EntityId != localEntityId) accent = campColor;
                _rings[i] = new Ring(renderer, ringGO.transform, mr, CreateDropLine(), accent, campColor);
                ringGO.SetActive(false);
            }
        }

        private LineRenderer CreateDropLine()
        {
            var lineGO = new GameObject("FighterDropLine");
            lineGO.transform.SetParent(transform, false);
            var line = lineGO.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 0;
            line.sharedMaterial = _dropLineMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private void LateUpdate()
        {
            if (_getState == null || _rings.Length == 0) return;

            for (int i = 0; i < _rings.Length; i++)
            {
                var ring = _rings[i];
                var renderer = ring.Renderer;
                if (renderer == null)
                {
                    ring.Transform.gameObject.SetActive(false);
                    ring.DropLine.enabled = false;
                    _arcActive[i] = false;
                    continue;
                }

                Vector3 pos = renderer.transform.position;
                float floorY = _heightmap.Sample(pos.x, pos.z);
                // Renderer roots are at capsule centre, not at the fighter's feet.
                float heightAboveFloor = pos.y - floorY - renderer.CapsuleHeight * 0.5f;
                if (float.IsNaN(floorY) || float.IsInfinity(floorY)
                    || floorY <= float.MinValue / 2f || heightAboveFloor < -0.1f)
                {
                    // No supporting floor beneath this fighter, including below a platform.
                    ring.Transform.gameObject.SetActive(false);
                    ring.DropLine.enabled = false;
                    _arcActive[i] = false;
                    continue;
                }

                ring.Transform.gameObject.SetActive(true);
                ring.Transform.position = new Vector3(pos.x, floorY + RingHeightAboveFloor, pos.z);
                var state = _getState(ring.EntityId);
                float airborne = state.IsGrounded ? 0f
                    : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.75f, heightAboveFloor));
                _markerProperties.SetFloat(AirborneProperty, airborne);
                _markerProperties.SetColor(AccentProperty, ring.Accent);
                _markerProperties.SetColor(CampColorProperty, ring.CampColor);
                ring.Mesh.SetPropertyBlock(_markerProperties);
                UpdateDropLine(ring, ring.Transform.position, floorY + heightAboveFloor, airborne);
                UpdateLaunchArc(i, ring, state);
            }
        }

        private void OnDestroy()
        {
            if (_markerMaterial != null)
                Destroy(_markerMaterial);
            if (_dropLineMaterial != null)
                Destroy(_dropLineMaterial);
        }

        private void UpdateDropLine(in Ring ring, Vector3 bottom, float feetY, float airborne)
        {
            var line = ring.DropLine;
            float length = feetY - bottom.y;
            if (airborne <= 0.01f || length <= 0.05f)
            {
                line.enabled = false;
                return;
            }

            var top = new Vector3(bottom.x, feetY, bottom.z);
            float width = _dropLineWidth;
            if (_camera == null) _camera = UnityEngine.Camera.main;
            if (_camera != null && !_camera.orthographic)
            {
                float distance = Vector3.Distance(_camera.transform.position, (bottom + top) * 0.5f);
                float metresPerPixel = 2f * distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                    / Mathf.Max(1, _camera.pixelHeight);
                width = Mathf.Max(width, _dropLineMinPixels * metresPerPixel);
            }

            // Floor first: the shader counts dots up from the ring.
            line.SetPosition(0, bottom);
            line.SetPosition(1, top);
            line.startWidth = line.endWidth = width;
            _dropLineProperties.SetFloat(AirborneProperty, airborne);
            _dropLineProperties.SetFloat(LengthInWidthsProperty, length / width);
            _dropLineProperties.SetFloat(SpacingProperty, _dropLineSpacing);
            _dropLineProperties.SetFloat(CoreProperty, _dropLineCore);
            _dropLineProperties.SetColor(ColorProperty, _dropLineColor);
            line.SetPropertyBlock(_dropLineProperties);
            line.enabled = true;
        }


        // ── A: launch trajectory arc ──

        private void UpdateLaunchArc(int i, in Ring ring, in CharacterState state)
        {
            float kvMag = Mathf.Sqrt(
                state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ);
            bool inLaunch = state.State == ActionState.Hitstun && kvMag > LaunchSpeedThreshold;

            if (inLaunch && !_wasLaunched[i] && ring.Renderer.CharacterDef != null)
            {
                _wasLaunched[i] = true;
                var arc = PredictFlight(state, ring.Renderer.CharacterDef);
                _arcPoints[i] = arc;
                _arcActive[i] = arc.Count > 1;
            }
            else if (!inLaunch)
            {
                _wasLaunched[i] = false;
            }

            if (_arcActive[i] && state.IsGrounded)
                _arcActive[i] = false;
        }

        private void OnDrawGizmos()
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                if (!_arcActive[i] || _arcPoints[i] == null) continue;

                var arc = _arcPoints[i];
                for (int j = 1; j < arc.Count; j++)
                {
                    Gizmos.color = PhaseColor(arc[j].phase);
                    Gizmos.DrawLine(arc[j - 1].pos, arc[j].pos);
                }
            }
        }

        /// <summary>
        /// Forward-simulate the victim's launch to landing through the REAL flight law
        /// (a ServerSimulation over the live arena, seeded with the victim's current
        /// post-launch state). Mirrors AbilityLab.ResolveTrajectory but seeds existing
        /// knockback velocity instead of re-applying the hit.
        /// </summary>
        private List<(Vector3 pos, char phase)> PredictFlight(in CharacterState victim, CharacterDefinition def)
        {
            var sim = new ServerSimulation(_arena);
            sim.RegisterEntity(victim.EntityId, def, victim, baked: null);
            var inputs = new Dictionary<ulong, InputState> { [victim.EntityId] = default };

            var arc = new List<(Vector3 pos, char phase)>
            {
                (new Vector3(victim.PX, victim.PY, victim.PZ), 'H'),
            };
            float maxPy = victim.PY;
            bool apexMarked = false;
            for (int t = 0; t < MaxTrajectoryTicks; t++)
            {
                sim.Tick(inputs);
                var s = sim.GetState(victim.EntityId);
                bool atApex = !apexMarked && s.PY <= maxPy && t > 0 && !s.IsGrounded && s.HitstunTicks == 0;
                if (s.PY > maxPy) maxPy = s.PY;
                else if (atApex) apexMarked = true;
                char phase = s.IsGrounded ? 'G' : s.HitstunTicks > 0 ? 'H' : atApex ? 'A' : 'F';
                arc.Add((new Vector3(s.PX, s.PY, s.PZ), phase));
                if (s.IsGrounded) break;
            }
            return arc;
        }
        // ── Helpers ──

        private static Color PhaseColor(char phase) => phase switch
        {
            'H' => new Color(0f, 1f, 1f, 0.6f),
            'F' => new Color(0.2f, 0.5f, 1f, 0.6f),
            'A' => new Color(1f, 1f, 1f, 0.7f),
            _   => new Color(1f, 0.2f, 0.1f, 0.6f),
        };
    }
}
