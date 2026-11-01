using System.Collections.Generic;
using UnityEngine;

namespace SlopArena.Client.Camera
{
    /// <summary>
    /// Stage shaders (SlopArenaStageCutaway.hlsl) ghost walls between the camera and up to four fighters,
    /// so the orbit camera keeps its distance instead of pulling in. Walkable tops within jump height stay solid.
    /// </summary>
    public static class StageSightHoles
    {
        // Defaults: a tall oval of the same width all along the sight line, the wall ghosted at 35% with a seat-colour
        // edge, opened only while the fighter is blocked (2026-11-01 owner pick D, with FighterXRay silhouettes).
        public static float Radius = 1.6f;
        public static float Edge = 0.15f;
        public static float Opacity = 0.35f;
        /// <summary>0 = cone narrowing toward the camera; 1 = the same width all along the sight line.</summary>
        public static float Tube = 1f;
        /// <summary>Vertical radius over horizontal radius (1 = circle, above 1 = upright oval).</summary>
        public static float Stretch = 1.4f;
        /// <summary>Seat-colour band along the hole edge: width in metres and strength 0..1.</summary>
        public static float RimWidth = 0.12f;
        public static float RimStrength = 1f;
        /// <summary>Seat-colour tint of the ghosted wall inside the hole, 0..1.</summary>
        public static float Tint = 0.25f;
        /// <summary>Open a fighter's hole only while stage geometry blocks its sight line, easing in and out.</summary>
        public static bool OnlyWhenBlocked = true;
        /// <summary>Tops (upward faces seen from above) up to this height above a fighter's feet stay solid, so walkable
        /// platforms never vanish; fighters behind them show as FighterXRay silhouettes. Taller tops are cut. 0 = off.</summary>
        public static float KeepTopsBelow = 4f;
        public static float FadeInSeconds = 0.15f;
        public static float FadeOutSeconds = 0.3f;

        private static readonly Vector4[] Targets = new Vector4[4];
        private static readonly Vector4[] Colours = new Vector4[4];
        private static readonly ulong[] Ids = new ulong[4];
        private static readonly Dictionary<ulong, float> Amounts = new();
        private static readonly int TargetsId = Shader.PropertyToID("_StageSightTargets");
        private static readonly int ColoursId = Shader.PropertyToID("_StageSightColors");
        private static readonly int AmountsId = Shader.PropertyToID("_StageSightAmounts");
        private static readonly int CountId = Shader.PropertyToID("_StageSightCount");
        private static readonly int ShapeId = Shader.PropertyToID("_StageSightShape");
        private static readonly int StyleId = Shader.PropertyToID("_StageSightStyle");
        private static readonly int KeepTopsId = Shader.PropertyToID("_StageSightKeepTops");

        // Shared positions are capsule centres; the roster's shortest half height keeps every floor whole.
        private const float FeetBelowCentre = 0.75f;

        /// <summary>Open holes toward the player and the three nearest others (capsule centres).</summary>
        public static void Publish(ulong playerId, Vector3 player, Color playerColour, IReadOnlyList<FramingSubject> others,
            Vector3 camera, float deltaTime)
        {
            Targets[0] = new Vector4(player.x, player.y, player.z, player.y - FeetBelowCentre);
            Colours[0] = playerColour;
            Ids[0] = playerId;
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
                {
                    Targets[j] = Targets[j - 1];
                    Colours[j] = Colours[j - 1];
                    Ids[j] = Ids[j - 1];
                }
                Targets[slot] = new Vector4(centre.x, centre.y, centre.z, centre.y - FeetBelowCentre);
                Colours[slot] = others[i].Colour;
                Ids[slot] = others[i].Id;
                count = Mathf.Min(count + 1, Targets.Length);
            }

            var amounts = Vector4.zero;
            for (int i = 0; i < count; i++)
            {
                float goal = !OnlyWhenBlocked || Blocked(Targets[i], camera) ? 1f : 0f;
                Amounts.TryGetValue(Ids[i], out float amount);
                float seconds = goal > amount ? FadeInSeconds : FadeOutSeconds;
                amount = seconds <= 0f ? goal : Mathf.MoveTowards(amount, goal, deltaTime / seconds);
                Amounts[Ids[i]] = amount;
                amounts[i] = amount;
            }

            Shader.SetGlobalVectorArray(TargetsId, Targets);
            Shader.SetGlobalVectorArray(ColoursId, Colours);
            Shader.SetGlobalVector(AmountsId, amounts);
            Shader.SetGlobalVector(ShapeId, new Vector4(Radius, Edge, Mathf.Clamp01(Opacity), Mathf.Clamp01(Tube)));
            Shader.SetGlobalVector(StyleId, new Vector4(Stretch, RimWidth, Mathf.Clamp01(RimStrength), Mathf.Clamp01(Tint)));
            Shader.SetGlobalFloat(KeepTopsId, KeepTopsBelow);
            Shader.SetGlobalFloat(CountId, count);
        }

        // Head, body and both sides: a fighter counts as blocked when stage geometry hides any of them.
        private static bool Blocked(Vector4 target, Vector3 camera)
        {
            var centre = new Vector3(target.x, target.y, target.z);
            Vector3 toCamera = camera - centre;
            Vector3 side = Vector3.Cross(Vector3.up, toCamera).normalized * 0.4f;
            return Ray(centre) || Ray(centre + Vector3.up * 0.6f) || Ray(centre + side) || Ray(centre - side);

            bool Ray(Vector3 from)
            {
                Vector3 ray = camera - from;
                return Physics.Raycast(from, ray.normalized, ray.magnitude,
                    Physics.IgnoreRaycastLayer, QueryTriggerInteraction.Ignore);
            }
        }

        public static void Clear()
        {
            Shader.SetGlobalFloat(CountId, 0f);
            Amounts.Clear();
        }

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
