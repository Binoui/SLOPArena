using System.Collections.Generic;
using System.Linq;
using System.Text;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SlopArena.Client.Replays
{
    /// <summary>
    /// Seek hygiene for one Replay scene. Owners reset themselves first; this recalls pools, removes what nobody
    /// owns, and starts what a warmup spawns at its recorded age. The invariant, checked by
    /// sloparena.replays.check --script seek-hygiene: after Seek(B) the Census is the same whatever came before.
    /// </summary>
    public sealed class ReplayEffects
    {
        private readonly HashSet<int> _baseline = new();
        private readonly HashSet<int> _marked = new();
        private Scene _scene;

        /// <summary>Remembers the scene's roots; anything spawned later is a transient the sweep may remove.</summary>
        public void Begin(Scene scene)
        {
            _scene = scene;
            _baseline.Clear();
            foreach (var root in scene.GetRootGameObjects()) _baseline.Add(root.GetInstanceID());
        }

        /// <summary>
        /// Returns pooled effects and removes roots spawned since Begin, except UI, pools and the camera's framing anchor.
        /// Each removal is logged: it names an effect whose owner does not reset itself on a seek yet.
        /// </summary>
        public IReadOnlyList<string> Sweep()
        {
            GraphicHitEffect.RecallAll();
            MovementFeedbackEffect.RecallOneShots();
            var swept = new List<string>();
            var anchor = CameraAnchor();
            foreach (var root in _scene.GetRootGameObjects())
            {
                if (_baseline.Contains(root.GetInstanceID()) || !root.activeSelf || Kept(root, anchor)) continue;
                swept.Add(root.name);
                root.SetActive(false);
                Object.Destroy(root);
            }
            if (swept.Count > 0) Debug.Log($"[ReplayEffects] Seek swept {swept.Count} unowned: {string.Join(", ", swept.Distinct())}");
            // Sounds of the previous epoch stop with it; music lives outside the Replay scene.
            foreach (var source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (source.gameObject.scene == _scene && source.isPlaying) source.Stop();
            return swept;
        }

        /// <summary>Marks what is alive before one warmup present.</summary>
        public void Mark()
        {
            _marked.Clear();
            foreach (var system in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                _marked.Add(system.GetInstanceID());
            foreach (var effect in GraphicHitEffect.Active) _marked.Add(effect.GetInstanceID());
        }

        /// <summary>
        /// Starts what the warmup present spawned at its distance from the target, so it shows as it would have. Its
        /// random seed comes from its place in the scene, so the same tick looks the same after any history.
        /// </summary>
        public void AgeSinceMark(float seconds)
        {
            foreach (var system in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (_marked.Contains(system.GetInstanceID())) continue;
                // Only the top system of a spawned effect: Simulate with children covers the rest once.
                var parent = system.transform.parent != null ? system.transform.parent.GetComponentInParent<ParticleSystem>() : null;
                if (parent != null && !_marked.Contains(parent.GetInstanceID())) continue;
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach (var part in system.GetComponentsInChildren<ParticleSystem>())
                {
                    part.useAutoRandomSeed = false;
                    part.randomSeed = Seed(Path(part.transform));
                }
                system.Simulate(seconds, true, true, true);
                system.Play(true);
                foreach (var animator in system.GetComponentsInChildren<Animator>()) animator.Update(seconds);
                foreach (var trail in system.GetComponentsInChildren<TrailRenderer>()) trail.Clear();
            }
            foreach (var effect in GraphicHitEffect.Active)
                if (!_marked.Contains(effect.GetInstanceID())) effect.AgeBy(seconds);
        }

        private static uint Seed(string path)
        {
            uint hash = 2166136261;
            foreach (char c in path) hash = (hash ^ c) * 16777619;
            return hash;
        }

        /// <summary>
        /// What a viewer would see now, as sorted lines: transient roots, alive particle systems with ages and counts,
        /// hit effects, trail lengths, playing sounds and fighter poses.
        /// </summary>
        public IReadOnlyList<string> Census(IEnumerable<PlayerRenderer> fighters)
        {
            var lines = new List<string>();
            var anchor = CameraAnchor();
            foreach (var root in _scene.GetRootGameObjects())
                if (!_baseline.Contains(root.GetInstanceID()) && root.activeInHierarchy && !Kept(root, anchor))
                    lines.Add("root " + Clean(root.name));
            foreach (var system in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (system.IsAlive(false) && system.particleCount > 0)
                    lines.Add($"particles {Path(system.transform)} t={system.time:0.000} n={system.particleCount}");
            foreach (var effect in GraphicHitEffect.Active)
                lines.Add($"hit {effect.transform.position.x:0.00},{effect.transform.position.y:0.00},{effect.transform.position.z:0.00} age={effect.Age:0.000}");
            foreach (var trail in Object.FindObjectsByType<TrailRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (trail.positionCount > 0)
                    lines.Add($"trail {Path(trail.transform)} points={trail.positionCount}");
            foreach (var source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (source.gameObject.scene == _scene && source.isPlaying)
                    lines.Add($"audio {Path(source.transform)} {(source.clip != null ? source.clip.name : "one-shot")}");
            foreach (var fighter in fighters)
            {
                if (fighter == null) continue;
                var p = fighter.transform.position;
                var line = new StringBuilder($"fighter {fighter.EntityId} at {p.x:0.000},{p.y:0.000},{p.z:0.000}");
                var weapon = fighter.GetComponent<WeaponAttach>();
                if (weapon != null) line.Append($" blade={weapon.TrailSampleCount}");
                var animator = fighter.Animator;
                if (animator != null && animator.isHuman)
                    foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot })
                        if (animator.GetBoneTransform(bone) is { } t)
                            line.Append($" {bone}={t.position.x:0.000},{t.position.y:0.000},{t.position.z:0.000}");
                lines.Add(line.ToString());
            }
            lines.Sort(System.StringComparer.Ordinal);
            return lines;
        }

        // The camera makes its framing anchor when it first switches to an automatic style, which can be after Begin.
        private static Transform? CameraAnchor() => Object.FindFirstObjectByType<CameraMount>()?.FramingAnchor;

        private static bool Kept(GameObject root, Transform? cameraAnchor)
            => root.GetComponent<UIDocument>() != null || root.GetComponent<Canvas>() != null
               || root.GetComponent<EventSystem>() != null || root.GetComponent<GraphicHitEffect>() != null
               || (cameraAnchor != null && root.transform == cameraAnchor);

        private static string Path(Transform transform)
        {
            var parts = new List<string>();
            for (var t = transform; t != null; t = t.parent) parts.Add(Clean(t.name));
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string Clean(string name) => name.Replace("(Clone)", "").Trim();
    }
}
