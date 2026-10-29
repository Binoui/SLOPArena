#nullable enable
using System.Collections.Generic;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.World
{
    public sealed class HeldCutPresentation : MonoBehaviour
    {
        private static Material? _rim, _edge, _scan, _pixel;
        private readonly List<(SkinnedMeshRenderer source, Renderer[] copies)> _skins = new();
        private bool _held;
        private byte _deaths;

        public void Apply(PlayerRenderer owner, in CharacterState state)
        {
            bool held = state.State == ActionState.CutHeld && state.HitstopTicks > 0;
            if (held && !_held) EnsureSkins(owner);
            if (_held && !held && state.Deaths == _deaths && state.InvincibilityTicks == 0)
                Shatter();
            _held = held;
            _deaths = state.Deaths;
            foreach (var (source, copies) in _skins)
                foreach (var copy in copies)
                    if (copy != null) copy.enabled = held && source != null && source.enabled;
        }

        private void EnsureSkins(PlayerRenderer owner)
        {
            if (_rim == null)
            {
                var shader = Resources.Load<Shader>("Shaders/WibouHeldCut");
                if (shader == null) return;
                _rim = Make(shader, new Color(8f / 255f, 48f / 255f, 18f / 255f), 0.036f, 0, CullMode.Front);
                _edge = Make(shader, new Color(145f / 255f, 1f, 105f / 255f), 0.022f, 0, CullMode.Front);
                _scan = Make(shader, new Color(145f / 255f, 1f, 105f / 255f, 0.32f), 0.003f, 1, CullMode.Back);
                _pixel = Make(shader, Color.white, 0f, 2, CullMode.Off);
                _rim.renderQueue = 2992;
                _edge.renderQueue = 2993;
                _scan.renderQueue = 2994;
            }
            _skins.RemoveAll(pair => pair.source == null);
            foreach (var source in owner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (source.sharedMesh == null || source.sharedMaterial == null
                    || source.sharedMaterial.shader.name != "SLOPArena/GraphicCharacter"
                    || _skins.Exists(pair => pair.source == source)) continue;
                var copies = new Renderer[3];
                var materials = new[] { _rim, _edge, _scan };
                for (int i = 0; i < copies.Length; i++)
                {
                    var root = new GameObject($"Held cut layer {i}") { layer = source.gameObject.layer };
                    root.transform.SetParent(source.transform, false);
                    var copy = root.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = source.sharedMesh;
                    copy.bones = source.bones;
                    copy.rootBone = source.rootBone;
                    copy.localBounds = source.localBounds;
                    var slots = new Material[source.sharedMesh.subMeshCount];
                    for (int j = 0; j < slots.Length; j++) slots[j] = materials[i]!;
                    copy.sharedMaterials = slots;
                    copy.shadowCastingMode = ShadowCastingMode.Off;
                    copy.receiveShadows = false;
                    copy.enabled = false;
                    copies[i] = copy;
                }
                _skins.Add((source, copies));
            }
        }

        private static Material Make(Shader shader, Color color, float width, float mode, CullMode cull)
        {
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.SetColor("_Color", color);
            material.SetFloat("_Width", width);
            material.SetFloat("_Mode", mode);
            material.SetFloat("_Cull", (int)cull);
            return material;
        }

        private void Shatter()
        {
            if (_pixel == null) return;
            var root = new GameObject("Held cut pixel shatter");
            var particles = root.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false;
            main.startLifetime = 0.38f;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 180;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = new Gradient { alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) } };
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _pixel;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.sortingFudge = -12f;
            var mesh = new Mesh();
            foreach (var (source, _) in _skins)
            {
                if (source == null || !source.enabled) continue;
                source.BakeMesh(mesh);
                var vertices = mesh.vertices;
                int step = Mathf.Max(1, vertices.Length / 90);
                for (int i = 0; i < vertices.Length; i += step)
                {
                    Vector3 position = source.transform.TransformPoint(vertices[i]);
                    Vector3 direction = (position - transform.position).normalized;
                    particles.Emit(new ParticleSystem.EmitParams { position = position,
                        velocity = direction * (1.8f + i % 7 * 0.15f), startSize = 0.05f + i % 5 * 0.012f }, 1);
                }
            }
            Destroy(mesh);
            particles.Play();
            Destroy(root, 0.6f);
        }

        private void OnDisable() => Clear();

        public void Clear()
        {
            _held = false;
            foreach (var (_, copies) in _skins)
                foreach (var copy in copies) if (copy != null) copy.enabled = false;
        }
    }
}
