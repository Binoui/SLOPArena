#nullable enable
using System;
using System.Collections.Generic;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// The homing dash's lock-on read (ADR-0042, concept C), shown to every player: during the
    /// float a dotted line runs from the dasher to its target and a reticle lands on the target;
    /// chevrons race along the line through the dash while the reticle tightens; on arrival the
    /// reticle snaps shut under a "!" stamp. A miss lets the reticle drop away. Presentation
    /// only: it follows the applied states and never decides anything.
    /// </summary>
    public sealed class HomingDashLockOn : MonoBehaviour
    {
        private const float LineWidth = 0.075f, DashLength = 0.34f, LineScrollSpeed = 2.2f;
        private const float ReticleSize = 1.1f, ReticlePopSeconds = 0.12f, ReticleSpin = 70f;
        private const float ChevronSize = 0.32f, ChevronSpeed = 1.6f;
        private const int Chevrons = 3;
        private const float BangSize = 0.5f, BangPopSeconds = 0.1f, BangHoldSeconds = 0.45f, BangFadeSeconds = 0.2f;
        private const float SnapSeconds = 0.08f, ReticleFadeSeconds = 0.25f, MissFadeSeconds = 0.15f;

        private sealed class Lock
        {
            public PlayerRenderer Dasher = null!;
            public ulong Target;
            public ActionState Previous;
            public float FloatStart = -1f, Arrived = -1f, Ended = -1f;
            public float StartDistance;
            public bool Missed;
            public LineRenderer Line = null!;
            public Material LineMaterial = null!;
            public Renderer Reticle = null!, Bang = null!;
            public readonly Renderer[] Chevrons = new Renderer[HomingDashLockOn.Chevrons];
            public Vector3 LastTargetPoint;
        }

        private readonly List<Lock> _locks = new();
        private readonly Dictionary<ulong, PlayerRenderer> _byEntity = new();
        private Func<ulong, CharacterState> _getState = null!;
        private Func<ulong, Vector3?>? _targetPosition;
        private UnityEngine.Camera? _camera;
        private Shader? _shader;
        private MaterialPropertyBlock? _block;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public void Init(Func<ulong, CharacterState> getState, PlayerRenderer[] renderers,
            Func<ulong, Vector3?>? targetPosition = null, UnityEngine.Camera? renderCamera = null)
        {
            _getState = getState;
            _targetPosition = targetPosition;
            _camera = renderCamera;
            _shader = Resources.Load<Shader>("Shaders/HomingDashSprite");
            _block = new MaterialPropertyBlock();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                _byEntity[renderer.EntityId] = renderer;
                _locks.Add(Build(renderer));
            }
        }

        private Lock Build(PlayerRenderer dasher)
        {
            var root = new GameObject($"Homing Dash Lock-On {dasher.EntityId}").transform;
            root.SetParent(transform, false);
            var lineObject = new GameObject("Line");
            lineObject.transform.SetParent(root, false);
            var line = lineObject.AddComponent<LineRenderer>();
            var lineMaterial = Material(HomingDashSprites.Dash, CompareFunction.LessEqual, 0);
            lineMaterial.mainTextureScale = new Vector2(1f, 1f);
            line.sharedMaterial = lineMaterial;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Tile;
            line.widthMultiplier = LineWidth;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            var lockOn = new Lock { Dasher = dasher, Line = line, LineMaterial = lineMaterial };
            lockOn.Reticle = Quad(root, "Reticle", Material(HomingDashSprites.Reticle, CompareFunction.Always, 2));
            lockOn.Bang = Quad(root, "Bang", Material(HomingDashSprites.Bang, CompareFunction.Always, 3));
            var chevron = Material(HomingDashSprites.Chevron, CompareFunction.Always, 1);
            for (int i = 0; i < Chevrons; i++) lockOn.Chevrons[i] = Quad(root, $"Chevron {i}", chevron);
            return lockOn;
        }

        private Material Material(Texture2D texture, CompareFunction zTest, int queueOffset)
        {
            var material = new Material(_shader) { mainTexture = texture, renderQueue = (int)RenderQueue.Transparent + 40 + queueOffset };
            material.SetFloat("_ZTest", (float)zTest);
            return material;
        }

        private static Renderer Quad(Transform parent, string name, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return renderer;
        }

        private void LateUpdate()
        {
            var camera = _camera != null ? _camera : UnityEngine.Camera.main;
            if (camera == null) return;
            float now = Time.time;
            foreach (var lockOn in _locks)
            {
                if (lockOn.Dasher == null) continue;
                var state = _getState(lockOn.Dasher.EntityId);
                Advance(lockOn, state, now);
                Draw(lockOn, state, camera, now);
                lockOn.Previous = state.State;
            }
        }

        private void Advance(Lock lockOn, in CharacterState state, float now)
        {
            bool dashing = state.State is ActionState.HomingDashFloat or ActionState.HomingDash;
            bool wasDashing = lockOn.Previous is ActionState.HomingDashFloat or ActionState.HomingDash;
            if (dashing && !wasDashing)
            {
                lockOn.FloatStart = now;
                lockOn.Arrived = lockOn.Ended = -1f;
                lockOn.Missed = false;
                lockOn.Target = state.HomingDashTargetId;
                lockOn.StartDistance = 0f;
            }
            if (dashing && state.HomingDashTargetId != 0) lockOn.Target = state.HomingDashTargetId;
            if (!dashing && wasDashing)
            {
                lockOn.Ended = now;
                if (state.State == ActionState.HomingDashArrive) lockOn.Arrived = now;
                else lockOn.Missed = true;
            }
        }

        private void Draw(Lock lockOn, in CharacterState state, UnityEngine.Camera camera, float now)
        {
            bool dashing = state.State is ActionState.HomingDashFloat or ActionState.HomingDash;
            bool hasTarget = lockOn.Target != 0 && TryTargetPoint(lockOn.Target, out lockOn.LastTargetPoint);
            Vector3 from = Chest(lockOn.Dasher), to = lockOn.LastTargetPoint;
            float sinceStart = now - lockOn.FloatStart;
            var face = camera.transform.rotation;

            // Dotted line and chevrons, only while the dash is live and has a target.
            bool line = dashing && hasTarget;
            lockOn.Line.enabled = line;
            if (line)
            {
                float distance = Vector3.Distance(from, to);
                if (lockOn.StartDistance <= 0f) lockOn.StartDistance = distance;
                Vector3 dir = (to - from) / Mathf.Max(distance, 1e-3f);
                Vector3 start = from + dir * 0.35f, end = to - dir * ReticleSize * 0.45f;
                lockOn.Line.SetPosition(0, start);
                lockOn.Line.SetPosition(1, end);
                lockOn.LineMaterial.mainTextureScale = new Vector2(1f / DashLength / LineWidth * LineWidth, 1f);
                lockOn.LineMaterial.mainTextureOffset = new Vector2(-sinceStart * LineScrollSpeed, 0f);
                lockOn.LineMaterial.SetColor(ColorId, new Color(1f, 1f, 1f, Mathf.Clamp01(sinceStart / 0.08f)));
            }
            bool chevrons = line && state.State == ActionState.HomingDash;
            Vector2 screenDir = Vector2.right;
            if (chevrons)
            {
                Vector3 a = camera.WorldToScreenPoint(from), b = camera.WorldToScreenPoint(to);
                screenDir = ((Vector2)(b - a)).normalized;
            }
            float angle = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg;
            for (int i = 0; i < Chevrons; i++)
            {
                var chevron = lockOn.Chevrons[i];
                chevron.enabled = chevrons;
                if (!chevrons) continue;
                float t = Mathf.Repeat((float)i / Chevrons + sinceStart * ChevronSpeed, 1f);
                Vector3 start = from + (to - from).normalized * 0.45f, end = to - (to - from).normalized * ReticleSize * 0.5f;
                chevron.transform.SetPositionAndRotation(Vector3.Lerp(start, end, t), face * Quaternion.Euler(0f, 0f, angle));
                chevron.transform.localScale = Vector3.one * ChevronSize;
                Tint(chevron, Mathf.Clamp01(Mathf.Min(t, 1f - t) * 6f));
            }

            // Reticle: pops on during the float, tightens through the dash, snaps shut on arrival.
            float reticleScale, reticleAlpha;
            if (dashing && hasTarget)
            {
                float pop = Mathf.Clamp01(sinceStart / ReticlePopSeconds);
                float closing = lockOn.StartDistance > 0f
                    ? 1f - Mathf.Clamp01(Vector3.Distance(from, to) / lockOn.StartDistance) : 0f;
                reticleScale = Mathf.Lerp(1.6f, 1f, pop) * Mathf.Lerp(1f, 0.75f, state.State == ActionState.HomingDash ? closing : 0f);
                reticleAlpha = pop;
            }
            else if (lockOn.Arrived >= 0f)
            {
                float t = now - lockOn.Arrived;
                reticleScale = Mathf.Lerp(0.75f, 0.5f, Mathf.Clamp01(t / SnapSeconds)) * (1f + 0.15f * Mathf.Max(0f, 1f - t / 0.12f));
                reticleAlpha = 1f - Mathf.Clamp01((t - BangHoldSeconds * 0.5f) / ReticleFadeSeconds);
            }
            else if (lockOn.Missed && lockOn.Ended >= 0f)
            {
                float t = now - lockOn.Ended;
                reticleScale = 0.9f + t;
                reticleAlpha = 1f - Mathf.Clamp01(t / MissFadeSeconds);
            }
            else reticleScale = reticleAlpha = 0f;
            bool reticle = reticleAlpha > 0.01f && lockOn.Target != 0;
            lockOn.Reticle.enabled = reticle;
            if (reticle)
            {
                lockOn.Reticle.transform.SetPositionAndRotation(to, face * Quaternion.Euler(0f, 0f, -sinceStart * ReticleSpin));
                lockOn.Reticle.transform.localScale = Vector3.one * ReticleSize * reticleScale;
                Tint(lockOn.Reticle, reticleAlpha);
            }

            // "!" stamp above the target after an arrival.
            float sinceArrive = lockOn.Arrived >= 0f ? now - lockOn.Arrived : -1f;
            bool bang = sinceArrive >= 0f && sinceArrive < BangHoldSeconds + BangFadeSeconds;
            lockOn.Bang.enabled = bang;
            if (bang)
            {
                float pop = Mathf.Clamp01(sinceArrive / BangPopSeconds);
                float scale = pop < 1f ? Mathf.Lerp(0.2f, 1.3f, pop) : Mathf.Lerp(1.3f, 1f, Mathf.Clamp01((sinceArrive - BangPopSeconds) / 0.08f));
                float height = _byEntity.TryGetValue(lockOn.Target, out var target) ? target.CapsuleHeight : 1.7f;
                lockOn.Bang.transform.SetPositionAndRotation(to + camera.transform.up * (height * 0.6f), face * Quaternion.Euler(0f, 0f, 8f));
                lockOn.Bang.transform.localScale = new Vector3(BangSize, BangSize * 2f, 1f) * scale;
                Tint(lockOn.Bang, 1f - Mathf.Clamp01((sinceArrive - BangHoldSeconds) / BangFadeSeconds));
            }
        }

        private bool TryTargetPoint(ulong id, out Vector3 point)
        {
            if (_byEntity.TryGetValue(id, out var renderer) && renderer != null)
            {
                point = Chest(renderer);
                return true;
            }
            var position = _targetPosition?.Invoke(id);
            point = position ?? default;
            return position.HasValue;
        }

        /// <summary>A point a little above the presented capsule centre.</summary>
        private static Vector3 Chest(PlayerRenderer renderer)
            => renderer.transform.position + Vector3.up * (renderer.CapsuleHeight * 0.15f - renderer.ModelYOffset);

        private void Tint(Renderer renderer, float alpha)
        {
            _block!.SetColor(ColorId, new Color(1f, 1f, 1f, alpha));
            renderer.SetPropertyBlock(_block);
        }
    }
}
