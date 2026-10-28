using System.Collections.Generic;
using UnityEngine;

namespace SlopArena.Client.Camera
{
    /// <summary>
    /// Stage shaders (SlopArenaStageCutaway.hlsl) dither away walls and platforms between the camera
    /// and up to four fighters, so the orbit camera keeps its distance instead of pulling in.
    /// </summary>
    public static class StageSightHoles
    {
        public static float Radius = 1.8f;
        public static float Edge = 0.5f;
        public static float Opacity = 0f;

        private static readonly Vector4[] Targets = new Vector4[4];
        private static readonly int TargetsId = Shader.PropertyToID("_StageSightTargets");
        private static readonly int CountId = Shader.PropertyToID("_StageSightCount");
        private static readonly int ShapeId = Shader.PropertyToID("_StageSightShape");

        // Shared positions are capsule centres; the roster's shortest half height keeps every floor whole.
        private const float FeetBelowCentre = 0.75f;

        /// <summary>Open holes toward the player and the three nearest others (capsule centres).</summary>
        public static void Publish(Vector3 player, IReadOnlyList<FramingSubject> others)
        {
            Targets[0] = new Vector4(player.x, player.y, player.z, player.y - FeetBelowCentre);
            int count = 1;
            for (int i = 0; i < others.Count; i++)
            {
                Vector3 centre = others[i].Position;
                float distance = (centre - player).sqrMagnitude;
                int slot = count;
                while (slot > 1)
                {
                    Vector4 previous = Targets[slot - 1];
                    if ((new Vector3(previous.x, previous.y, previous.z) - player).sqrMagnitude <= distance) break;
                    slot--;
                }
                if (slot >= Targets.Length) continue;
                for (int j = Mathf.Min(count, Targets.Length - 1); j > slot; j--)
                    Targets[j] = Targets[j - 1];
                Targets[slot] = new Vector4(centre.x, centre.y, centre.z, centre.y - FeetBelowCentre);
                count = Mathf.Min(count + 1, Targets.Length);
            }

            Shader.SetGlobalVectorArray(TargetsId, Targets);
            Shader.SetGlobalVector(ShapeId, new Vector4(Radius, Edge, Mathf.Clamp01(Opacity), 0f));
            Shader.SetGlobalFloat(CountId, count);
        }

        public static void Clear() => Shader.SetGlobalFloat(CountId, 0f);

        private static readonly Dictionary<Material, Material> SightLit = new();
        private static Shader? _stageLit;

        /// <summary>Give a stage's URP/Lit materials sight holes; the SLOPArena stage shaders have them built in.</summary>
        public static void Adopt(GameObject stage)
        {
            _stageLit ??= Resources.Load<Shader>("Shaders/SlopArenaStageLit");
            if (_stageLit == null) return;
            foreach (var renderer in stage.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null || source.shader.name != "Universal Render Pipeline/Lit") continue;
                    // Play Mode exit destroys the copies while this cache survives (no domain reload).
                    if (!SightLit.TryGetValue(source, out var copy) || copy == null)
                        SightLit[source] = copy = new Material(source) { shader = _stageLit, name = source.name };
                    materials[i] = copy;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }
    }
}
