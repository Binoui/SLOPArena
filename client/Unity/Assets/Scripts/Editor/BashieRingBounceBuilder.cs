#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SlopArena.Client.Animation;
using SlopArena.Client.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>Rebuilds only the accepted E ring cues and binding, preserving the rest of Bashie's catalog.</summary>
    public static class BashieRingBounceBuilder
    {
        private const string Root = "Assets/CharacterPackages/bashie";
        private const string ClipPath = Root + "/Animations/bashie_e_ring_bounce.anim";

        [CliCommand("sloparena.presentation.bashie-ring.build", "Rebuild the accepted Ring Bounce prop cues and body binding in Edit Mode.", MainThreadRequired = true, Tags = new[] { "presentation", "character" })]
        public static object Build()
        {
            var built = BuildPrefabs();
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(Root + "/CharacterAssetCatalog.asset");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath)
                ?? throw new InvalidOperationException("Promote the accepted Ring Bounce body clip first.");
            catalog.Bindings.Single(b => b.SemanticId == "anim.bashie.ge").Clip = clip;
            var ids = new HashSet<string>(built.Select(p => p.id));
            var presentations = (catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
                .Where(p => !ids.Contains(p.SemanticId)).ToList();
            presentations.AddRange(built.Select(p => new CharacterAssetCatalog.PresentationBinding { SemanticId = p.id, Prefab = p.prefab }));
            catalog.Presentations = presentations.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            return new { success = true, clip = ClipPath, cues = built.Select(p => p.id).ToArray() };
        }

        public static List<(string id, GameObject prefab)> BuildPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Rebuild Ring Bounce in Edit Mode.");
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var staging = JObject.Parse(File.ReadAllText(Path.Combine(repo, "tools/blender/bashie_rig/ring_bounce_staging.json")));
            int launch = (int)staging["launchTick"], duration = (int)staging["durationTicks"];
            int contact = (int?)staging["contactTick"] ?? launch;
            var poses = staging["poses"].Children().OrderBy(p => (int)p["tick"]).ToArray();
            if (poses.Length != duration + 1) throw new InvalidOperationException("Incomplete accepted ring staging.");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Presentation/BashieRing.prefab")
                ?? throw new InvalidOperationException("Promote the accepted ring model first.");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var result = new List<(string id, GameObject prefab)>();
                foreach (bool rebound in new[] { false, true })
                {
                    string name = rebound ? "BashieRingRebound" : "BashieRingBounce";
                    var root = new GameObject(name);
                    SceneManager.MoveGameObjectToScene(root, scene);
                    if (!rebound) root.AddComponent<AttackBoundPresentation>();
                    var ring = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                    ring.name = "Ring";
                    ring.transform.SetParent(root.transform, false);
                    var motion = root.AddComponent<RingBounceProp>();
                    motion.Ring = ring.transform;
                    motion.WorldRebound = rebound;
                    motion.ContactTick = contact;
                    motion.FirstTick = rebound ? launch : 0;
                    var samples = poses.Where(p => (int)p["tick"] >= motion.FirstTick
                        && (rebound || (int)p["tick"] <= launch)).ToArray();
                    Vector3 Vector(JToken p, string field) { var a = p[field]?.ToObject<float[]>(); return a == null ? Vector3.zero : new Vector3(a[0], a[1], a[2]); }
                    motion.Positions = samples.Select(p => Vector(p, "position")).ToArray();
                    motion.ActorOffsets = rebound ? Array.Empty<Vector3>() : samples.Select(p => Vector(p, "actorOffset")).ToArray();
                    motion.Scales = samples.Select(p => Vector(p, "scale")).ToArray();
                    motion.EulerAngles = samples.Select(p => Vector(p, "euler")).ToArray();
                    if (rebound)
                        for (int i = 0; i < motion.Scales.Length; i++)
                            motion.Positions[i] -= Vector(poses[launch], "actorOffset");
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Presentation/" + name + ".prefab");
                    result.Add((rebound ? "presentation.bashie.ring.rebound" : "presentation.bashie.ring.bounce", prefab));
                }
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
#endif
