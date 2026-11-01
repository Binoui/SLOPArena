using System.Collections.Generic;
using SlopArena.Client.Entities;
using SlopArena.Client.Simulation;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>Cosmetic rings rebuilt from the current predicted world, including rollback.</summary>
    public sealed class BonkSpecialPresenter
    {
        private readonly Dictionary<(ulong Owner, bool Spot), LineRenderer> _rings = new();
        private readonly HashSet<(ulong Owner, bool Spot)> _visible = new();
        private Material _material;

        public void Present(ISimulationBridge bridge, IReadOnlyDictionary<ulong, PlayerRenderer> fighters)
        {
            _visible.Clear();
            var simulation = bridge switch
            {
                LocalSimulationBridge local => local.InternalSim,
                RollbackSimulationBridge online => online.Simulation,
                _ => null,
            };
            foreach (var pair in fighters)
            {
                if (pair.Value == null) continue;
                // Stage art can sit 5.3 cm above Shared floor; keep the ribbon above it. The radius is the slam
                // sphere's reach at floor height (2 m sphere, 0.35 m under his centre), so dodging the ring dodges the slam.
                if (bridge.GetActiveMove(pair.Key)?.Spot is MoveSpot spot)
                    Ring((pair.Key, true), new Vector3(spot.X, spot.Y + 0.1f, spot.Z), 1.9f);
                if (simulation != null && simulation.TryGetChallengeTarget(pair.Key, out ulong target)
                    && fighters.TryGetValue(target, out var renderer) && renderer != null)
                {
                    var state = bridge.GetState(target);
                    float height = renderer.CharacterDef?.CapsuleHeight ?? 1.7f;
                    // Each challenger owns its own ring, staggered so two marks stay readable.
                    Ring((pair.Key, false), new Vector3(state.PX, state.PY + height * 0.5f + 0.3f + (pair.Key % 4) * 0.12f, state.PZ), 0.22f);
                }
            }
            foreach (var pair in _rings) pair.Value.gameObject.SetActive(_visible.Contains(pair.Key));
        }

        private void Ring((ulong Owner, bool Spot) key, Vector3 position, float radius)
        {
            _visible.Add(key);
            if (!_rings.TryGetValue(key, out var ring))
            {
                if (_material == null)
                {
                    _material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                    _material.SetColor("_BaseColor", new Color(1f, 0.78f, 0.25f, 1f));
                    _material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                }
                ring = new GameObject(key.Spot ? "Bonk Faith spot" : "Bonk Challenge marker").AddComponent<LineRenderer>();
                ring.sharedMaterial = _material;
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.positionCount = 48;
                ring.widthMultiplier = key.Spot ? 0.1f : 0.04f;
                ring.startColor = ring.endColor = new Color(1f, 0.85f, 0.4f, 1f);
                ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.receiveShadows = false;
                for (int i = 0; i < 48; i++)
                {
                    float angle = i * Mathf.PI * 2f / 48;
                    ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                }
                _rings.Add(key, ring);
            }
            ring.transform.position = position;
        }

        public void Clear()
        {
            foreach (var ring in _rings.Values)
                if (ring != null) Object.Destroy(ring.gameObject);
            _rings.Clear();
            if (_material != null) Object.Destroy(_material);
            _material = null;
        }
    }
}
