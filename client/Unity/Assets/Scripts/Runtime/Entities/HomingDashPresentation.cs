#nullable enable
using System.Collections.Generic;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// The dasher's half of the homing dash lock-on look (ADR-0042, concept C), shared by every
    /// fighter: an Action Yellow outline while the dash floats and flies, and fading yellow
    /// afterimages left along the dash. The line, chevrons, reticle and "!" belong to
    /// <c>HomingDashLockOn</c>. Presentation only; it follows the applied state.
    /// </summary>
    public sealed class HomingDashPresentation : MonoBehaviour
    {
        private static readonly Color ActionYellow = new(1f, 0.8f, 0.133f, 1f);
        private const float OutlineWidth = 0.03f, OutlineInSeconds = 0.08f;
        // Afterimages count applied simulation ticks, so captures stepping one tick per frame match live play.
        // A fresh afterimage hides until the body has left it, so it never tints the fighter.
        private const int GhostEveryTicks = 3, GhostLifeTicks = 10, GhostHiddenTicks = 2;
        private const float GhostAlpha = 0.42f, Tick = SlopArena.Shared.Simulation.TickDt;
        private const int MaxGhosts = GhostLifeTicks / GhostEveryTicks + 1;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int WidthId = Shader.PropertyToID("_Width");
        private static Material? _outlineMaterial, _ghostMaterial;
        // tools/sfx/homing_dash_clips.py: lock as the reticle lands, whoosh as the dash flies, arrive with the "!".
        private static AudioClip? _lockSound, _whooshSound, _arriveSound;
        private PlayerRenderer? _renderer;

        private Transform? _model;
        private readonly List<Renderer> _outlines = new();
        private readonly List<SkinnedMeshRenderer> _skins = new();
        private readonly List<(GameObject root, MeshRenderer[] parts, Mesh[] meshes, int born)> _ghosts = new();
        private readonly Stack<(GameObject root, MeshRenderer[] parts, Mesh[] meshes)> _pool = new();
        private MaterialPropertyBlock? _block;
        private bool _dashing, _travelling;
        private float _dashStart, _tickTime;
        private int _ticks;
        private byte _deaths;

        public void Init(PlayerRenderer renderer)
        {
            _renderer = renderer;
            _block ??= new MaterialPropertyBlock();
            _lockSound ??= Resources.Load<AudioClip>("Audio/SFX/HomingDash/homing_dash_lock");
            _whooshSound ??= Resources.Load<AudioClip>("Audio/SFX/HomingDash/homing_dash_whoosh");
            _arriveSound ??= Resources.Load<AudioClip>("Audio/SFX/HomingDash/homing_dash_arrive");
            if (_outlineMaterial == null)
            {
                var outline = Resources.Load<Shader>("Shaders/HomingDashOutline");
                var sprite = Resources.Load<Shader>("Shaders/HomingDashSprite");
                if (outline == null || sprite == null)
                {
                    Debug.LogWarning("[HomingDashPresentation] Homing dash shaders are missing from Resources/Shaders.");
                    return;
                }
                _outlineMaterial = new Material(outline) { hideFlags = HideFlags.DontSave };
                _outlineMaterial.SetColor(ColorId, ActionYellow);
                _ghostMaterial = new Material(sprite) { hideFlags = HideFlags.DontSave, renderQueue = (int)RenderQueue.Transparent - 5 };
            }
        }

        /// <summary>Use the model the renderer just loaded; outline copies are rebuilt on the next dash.</summary>
        public void AttachModel(Transform model)
        {
            Clear();
            foreach (var outline in _outlines)
                if (outline != null) Destroy(outline.gameObject);
            _outlines.Clear();
            _skins.Clear();
            foreach (var ghost in _pool) if (ghost.root != null) Destroy(ghost.root);
            _pool.Clear();
            _model = model;
        }

        /// <summary>Call once per applied simulation tick, after the renderer has applied the state.</summary>
        public void OnState(in CharacterState state)
        {
            if (state.Deaths != _deaths)
            {
                _deaths = state.Deaths;
                Clear();
            }
            bool dashing = state.State is ActionState.HomingDashFloat or ActionState.HomingDash;
            if (dashing && !_dashing)
            {
                EnsureOutlines();
                _dashStart = Time.time;
                Play(_lockSound);
            }
            bool travelling = state.State == ActionState.HomingDash;
            if (travelling && !_travelling) Play(_whooshSound);
            if (_travelling && state.State == ActionState.HomingDashArrive) Play(_arriveSound);
            _travelling = travelling;
            _dashing = dashing;
            SetOutlines(dashing);
            _ticks++;
            _tickTime = Time.time;
            for (int i = _ghosts.Count - 1; i >= 0; i--)
                if (_ticks - _ghosts[i].born >= GhostLifeTicks) Release(i);
            if (_travelling && _ticks % GhostEveryTicks == 0 && _ghosts.Count < MaxGhosts)
                SpawnGhost();
        }

        private void Play(AudioClip? clip)
        {
            if (clip != null && _renderer != null) _renderer.PlayOneShot(clip, 0.9f);
        }

        public void Clear()
        {
            _dashing = _travelling = false;
            SetOutlines(false);
            for (int i = _ghosts.Count - 1; i >= 0; i--) Release(i);
        }

        private void LateUpdate()
        {
            if (_dashing && _block != null)
            {
                float width = OutlineWidth * Mathf.Clamp01((Time.time - _dashStart) / OutlineInSeconds);
                _block.Clear();
                _block.SetFloat(WidthId, width);
                foreach (var outline in _outlines)
                    if (outline != null) outline.SetPropertyBlock(_block);
            }
            float fraction = Mathf.Clamp01((Time.time - _tickTime) / Tick);
            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                int ticks = _ticks - _ghosts[i].born;
                float age = (ticks + fraction) / GhostLifeTicks;
                _block!.Clear();
                var c = ActionYellow;
                c.a = ticks < GhostHiddenTicks ? 0f : GhostAlpha * Mathf.Clamp01(1f - age);
                _block.SetColor(ColorId, c);
                foreach (var part in _ghosts[i].parts) part.SetPropertyBlock(_block);
            }
        }

        private void EnsureOutlines()
        {
            if (_model == null || _outlineMaterial == null || _outlines.Count > 0) return;
            foreach (var skin in _model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin.sharedMesh == null) continue;
                _skins.Add(skin);
                var copy = new GameObject($"{skin.name} Homing Dash Outline");
                copy.transform.SetParent(skin.transform, false);
                copy.layer = skin.gameObject.layer;
                var outline = copy.AddComponent<SkinnedMeshRenderer>();
                outline.sharedMesh = skin.sharedMesh;
                outline.bones = skin.bones;
                outline.rootBone = skin.rootBone;
                outline.localBounds = skin.localBounds;
                outline.updateWhenOffscreen = skin.updateWhenOffscreen;
                var materials = new Material[Mathf.Max(1, skin.sharedMesh.subMeshCount)];
                for (int i = 0; i < materials.Length; i++) materials[i] = _outlineMaterial;
                outline.sharedMaterials = materials;
                outline.shadowCastingMode = ShadowCastingMode.Off;
                outline.receiveShadows = false;
                outline.enabled = false;
                _outlines.Add(outline);
            }
        }

        private void SetOutlines(bool visible)
        {
            for (int i = 0; i < _outlines.Count; i++)
                if (_outlines[i] != null)
                    _outlines[i].enabled = visible && i < _skins.Count && _skins[i] != null && _skins[i].enabled
                        && _skins[i].gameObject.activeInHierarchy;
        }

        private void SpawnGhost()
        {
            if (_skins.Count == 0 || _ghostMaterial == null) return;
            var ghost = _pool.Count > 0 ? _pool.Pop() : NewGhost();
            ghost.root.SetActive(true);
            for (int i = 0; i < ghost.parts.Length && i < _skins.Count; i++)
            {
                var skin = _skins[i];
                if (skin == null) continue;
                skin.BakeMesh(ghost.meshes[i], false);
                var part = ghost.parts[i].transform;
                part.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                part.localScale = Vector3.one;
            }
            _ghosts.Add((ghost.root, ghost.parts, ghost.meshes, _ticks));
        }

        private (GameObject root, MeshRenderer[] parts, Mesh[] meshes) NewGhost()
        {
            var root = new GameObject("Homing Dash Afterimage") { layer = _skins[0].gameObject.layer };
            var parts = new MeshRenderer[_skins.Count];
            var meshes = new Mesh[_skins.Count];
            for (int i = 0; i < _skins.Count; i++)
            {
                var part = new GameObject("Part") { layer = root.layer };
                part.transform.SetParent(root.transform, false);
                meshes[i] = new Mesh { name = "Homing Dash Afterimage" };
                part.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var renderer = part.AddComponent<MeshRenderer>();
                int subMeshes = _skins[i] != null && _skins[i].sharedMesh != null ? _skins[i].sharedMesh.subMeshCount : 1;
                var materials = new Material[Mathf.Max(1, subMeshes)];
                for (int m = 0; m < materials.Length; m++) materials[m] = _ghostMaterial!;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                parts[i] = renderer;
            }
            return (root, parts, meshes);
        }

        private void Release(int index)
        {
            var ghost = _ghosts[index];
            _ghosts.RemoveAt(index);
            if (ghost.root == null) return;
            ghost.root.SetActive(false);
            _pool.Push((ghost.root, ghost.parts, ghost.meshes));
        }

        private void OnDestroy()
        {
            foreach (var ghost in _ghosts) if (ghost.root != null) Destroy(ghost.root);
            foreach (var ghost in _pool) if (ghost.root != null) Destroy(ghost.root);
        }
    }
}
