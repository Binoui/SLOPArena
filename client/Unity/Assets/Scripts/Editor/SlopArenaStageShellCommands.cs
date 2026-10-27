#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SlopArena.Client.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Rebuilds a stage's authoritative collision source scene from its layout
    /// (python3 tools/blender/&lt;stage&gt;/layout.py --unity-shell, see stagekit/shell.py).
    /// Mesh assets keep their GUIDs and GameplayGeometry children are reused, so an
    /// unchanged layout leaves the scene and the next bake as they were. Spawn markers and
    /// the kill plane are verified unless the caller asks to move the spawns. A Character
    /// Course's targets, checkpoints and finish (the shell's optional "course") are written as
    /// empty AuthoringAids markers with the ArcadeStageAuthoring the bake compiles; like the
    /// meshes they always follow the layout. --create builds a new stage's source scene first.
    /// Bake and inspect afterwards; a shell change is a gameplay change.
    /// </summary>
    public static class SlopArenaStageShellCommands
    {
        [CliCommand("sloparena.stage.shell-apply",
            "Rebuild a stage's collision source scene from layout.py --unity-shell; keeps mesh GUIDs and verifies spawns and the kill plane. Bake and inspect afterwards.",
            MainThreadRequired = true, Tags = new[] { "authoring/stage" })]
        public static object Apply(
            [CliArg("stage", "Lowercase snake_case stage key with a source scene Assets/Stages/<stage>/<stage>.unity.", Required = true)] string stage,
            [CliArg("from", "Shell JSON written by layout.py --unity-shell (absolute or repository-relative).", Required = true)] string from,
            [CliArg("spawns", "verify (default) checks the spawn markers and kill plane against the layout; move sets them.")] string spawns = "verify",
            [CliArg("remove", "Comma list of GameplayGeometry children the new shell replaces, such as a previous blockout; any other unexpected child stops the rebuild.")] string remove = null,
            [CliArg("create", "Create the source scene of a new stage (Stage_<key> root, GameplayGeometry, the layout's spawn markers, AuthoringAids/KillPlane) when it does not exist yet.")] bool create = false)
        {
            if (!Regex.IsMatch(stage ?? "", "^[a-z][a-z0-9_]{0,99}$")) throw new ArgumentException("Invalid stage key.");
            if (spawns != "verify" && spawns != "move") throw new ArgumentException("spawns must be verify or move.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Rebuild the shell in stopped Edit Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Open scene " + SceneManager.GetSceneAt(i).path + " has unsaved changes; save it first.");
            string repository = Directory.GetParent(Path.GetFullPath(UnityCharacterAssetCooker.ProjectRoot()))?.Parent?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the repository root.");
            string path = Path.GetFullPath(Path.IsPathRooted(from) ? from : Path.Combine(repository, from));
            if (!File.Exists(path)) throw new InvalidOperationException("Missing shell " + from + "; write it with python3 tools/blender/" + stage + "/layout.py --unity-shell.");
            var shell = JObject.Parse(File.ReadAllText(path));
            if ((int?)shell["format"] != 1 || (string)shell["stage"] != stage)
                throw new InvalidOperationException("The shell must be format 1 for stage '" + stage + "' (tools/blender/stagekit/shell.py).");
            float killY = (float)shell["killY"];
            var removable = new HashSet<string>((remove ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));

            // Mesh assets first: an existing asset is updated in place so references keep its GUID.
            string folder = "Assets/Stages/" + stage;
            string scenePath = folder + "/" + stage + ".unity";
            if (!File.Exists(scenePath) && !create)
                throw new InvalidOperationException("Missing source scene " + scenePath + "; pass --create for a new stage.");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Stages", stage);
            var meshes = new List<(string name, Mesh mesh, string asset)>();
            foreach (var spec in (JArray)shell["meshes"])
            {
                string name = (string)spec["name"], asset = (string)spec["asset"];
                if (!Regex.IsMatch(name ?? "", "^[A-Za-z0-9_]{1,80}$") || !Regex.IsMatch(asset ?? "", "^[A-Za-z0-9_]{1,80}$"))
                    throw new InvalidOperationException("Shell mesh and asset names are plain identifiers: " + name + " / " + asset);
                var vertices = ((JArray)spec["vertices"]).Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])).ToList();
                var triangles = ((JArray)spec["triangles"]).Select(t => (int)t).ToList();
                if (triangles.Count == 0 || triangles.Count % 3 != 0 || triangles.Any(t => t < 0 || t >= vertices.Count))
                    throw new InvalidOperationException("Mesh " + name + " has an invalid triangle list.");
                string assetPath = folder + "/" + asset + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                bool created = mesh == null;
                if (created) mesh = new Mesh();
                mesh.Clear();
                mesh.name = asset;
                mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                if (created) AssetDatabase.CreateAsset(mesh, assetPath);
                else EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssetIfDirty(mesh);
                meshes.Add((name, mesh, assetPath));
            }

            bool sceneCreated = !File.Exists(scenePath);
            if (sceneCreated) CreateSourceScene(stage, scenePath, shell, killY);
            var scene = SceneManager.GetSceneByPath(scenePath);
            bool opened = false;
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                opened = true;
            }
            var removed = new List<string>();
            var markers = new List<object>();
            object course = null, mayhem = null;
            try
            {
                var root = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "Stage_" + stage)
                    ?? throw new InvalidOperationException("Stage_" + stage + " root not found in " + scenePath);
                var geometry = root.transform.Find("GameplayGeometry") ?? throw new InvalidOperationException("GameplayGeometry not found.");
                foreach (var child in geometry.Cast<Transform>().ToArray())
                {
                    if (meshes.Any(m => m.name == child.name)) continue;
                    if (!removable.Contains(child.name))
                        throw new InvalidOperationException("Unexpected GameplayGeometry child " + child.name + "; inspect it, then pass --remove " + child.name + " if the new shell replaces it.");
                    removed.Add(child.name);
                    Object.DestroyImmediate(child.gameObject);
                }
                foreach (var (name, mesh, _) in meshes)
                {
                    var child = geometry.Find(name);
                    if (child == null)
                    {
                        child = new GameObject(name).transform;
                        child.SetParent(geometry, false);
                    }
                    child.localPosition = Vector3.zero;
                    child.localRotation = Quaternion.identity;
                    child.localScale = Vector3.one;
                    // GetComponent returns the Editor's fake null for a missing component, which ?? keeps.
                    (child.TryGetComponent<MeshFilter>(out var filter) ? filter : child.gameObject.AddComponent<MeshFilter>()).sharedMesh = mesh;
                    child.gameObject.isStatic = true;
                }

                var spawnRoot = root.transform.Find("SpawnPoints") ?? throw new InvalidOperationException("SpawnPoints not found.");
                foreach (var spawn in (JArray)shell["spawns"])
                {
                    string name = (string)spawn["name"];
                    var marker = spawnRoot.Find(name) ?? throw new InvalidOperationException("Missing spawn marker " + name);
                    var p = spawn["position"];
                    var want = new Vector3((float)p[0], (float)p[1], (float)p[2]);
                    float yaw = (float)spawn["yaw"];
                    var before = marker.position;
                    if (spawns == "move")
                    {
                        marker.position = want;
                        marker.rotation = Quaternion.Euler(0f, yaw, 0f);
                    }
                    else if ((marker.position - want).magnitude > 1e-3f || Mathf.Abs(Mathf.DeltaAngle(marker.eulerAngles.y, yaw)) > 0.05f)
                        throw new InvalidOperationException(name + " differs from the layout (" + marker.position.ToString("F3") + " yaw " + marker.eulerAngles.y.ToString("F1")
                            + "); pass --spawns move to set it.");
                    markers.Add(new { name, before = new[] { before.x, before.y, before.z }, position = new[] { want.x, want.y, want.z }, yaw });
                }
                var kill = root.transform.Find("AuthoringAids/KillPlane") ?? throw new InvalidOperationException("AuthoringAids/KillPlane marker not found.");
                if (spawns == "move")
                    kill.position = new Vector3(kill.position.x, killY, kill.position.z);
                else if (Mathf.Abs(kill.position.y - killY) > 1e-4f)
                    throw new InvalidOperationException("The KillPlane marker is at " + kill.position.y + ", the layout says " + killY + "; pass --spawns move to set it.");
                if (shell["course"] != null && shell["mayhem"] != null)
                    throw new ArgumentException("A shell cannot define both a course and Mayhem.");
                if (shell["mayhem"] is JObject mayhemSpec)
                    mayhem = ApplyMayhem(kill.parent, mayhemSpec);
                if (shell["course"] is JObject courseSpec)
                    course = ApplyCourse(kill.parent, courseSpec);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Saving " + scenePath + " failed.");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
            return new
            {
                success = true, stage, scene = scenePath, created = sceneCreated, spawns, course, mayhem,
                // Read mesh data here: the result is serialized off the main thread.
                meshes = meshes.Select(m => new { m.name, asset = m.asset, vertices = m.mesh.vertexCount, triangles = m.mesh.triangles.Length / 3,
                    min = new[] { m.mesh.bounds.min.x, m.mesh.bounds.min.y, m.mesh.bounds.min.z },
                    max = new[] { m.mesh.bounds.max.x, m.mesh.bounds.max.y, m.mesh.bounds.max.z } }).ToArray(),
                removed, markers, next = "sloparena.stage.bake, sloparena.stage.inspect, then the stage's arena tests and layout.py's strict check",
            };
        }

        /// <summary>A new stage's empty source scene: the root hierarchy, the layout's spawn markers and the kill plane.</summary>
        private static void CreateSourceScene(string stage, string scenePath, JObject shell, float killY)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = new GameObject("Stage_" + stage);
                SceneManager.MoveGameObjectToScene(root, scene);
                Transform Child(Transform parent, string name)
                {
                    var child = new GameObject(name).transform;
                    child.SetParent(parent, false);
                    return child;
                }
                Child(root.transform, "GameplayGeometry");
                var spawnRoot = Child(root.transform, "SpawnPoints");
                foreach (var spawn in (JArray)shell["spawns"])
                {
                    var marker = Child(spawnRoot, (string)spawn["name"]);
                    marker.gameObject.tag = "SpawnPoint";
                    var p = spawn["position"];
                    marker.SetPositionAndRotation(new Vector3((float)p[0], (float)p[1], (float)p[2]), Quaternion.Euler(0f, (float)spawn["yaw"], 0f));
                }
                Child(Child(root.transform, "AuthoringAids"), "KillPlane").position = new Vector3(0f, killY, 0f);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new InvalidOperationException("Saving " + scenePath + " failed.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Writes a Character Course onto AuthoringAids: the ArcadeStageAuthoring settings and one
        /// empty marker per target, checkpoint sensor, respawn and finish. Markers not in the layout
        /// are removed (the kill plane stays). Compiles the course before the scene is saved.
        /// </summary>
        private static object ApplyCourse(Transform aids, JObject spec)
        {
            // GetComponent returns the Editor's fake null for a missing component, which ?? keeps.
            var config = aids.TryGetComponent<ArcadeStageAuthoring>(out var existing) ? existing : aids.gameObject.AddComponent<ArcadeStageAuthoring>();
            config.MankiMayhem = false;
            config.CharacterCourse = true;
            config.Slopball = false;
            config.BallSpawn = null;
            config.ProfileId = (string)spec["profileId"];
            config.GameplayRevision = (uint)spec["gameplayRevision"];
            config.AllowedPackageIds = ((JArray)spec["allowedPackageIds"]).Select(x => (string)x).ToArray();
            var keep = new HashSet<string> { "KillPlane" };
            var moved = new List<object>();
            Transform Marker(string name, JToken position, float yaw = 0f)
            {
                if (!keep.Add(name)) throw new InvalidOperationException("Duplicate course marker " + name);
                var marker = aids.Find(name);
                if (marker == null)
                {
                    marker = new GameObject(name).transform;
                    marker.SetParent(aids, false);
                }
                var before = marker.position;
                var want = Vector(position);
                marker.SetPositionAndRotation(want, Quaternion.Euler(0f, yaw, 0f));
                if ((before - want).sqrMagnitude > 1e-8f)
                    moved.Add(new { name, before = new[] { before.x, before.y, before.z }, position = new[] { want.x, want.y, want.z } });
                return marker;
            }
            config.Targets = ((JArray)spec["targets"]).Select(t => new ArcadeStageAuthoring.Target
            {
                Id = (string)t["id"], Marker = Marker("Target_" + (string)t["id"], t["position"]), Radius = (float)t["radius"],
                Hits = t["hits"] != null ? (int)t["hits"] : 1,
            }).ToArray();
            config.Checkpoints = ((JArray)spec["checkpoints"]).Select(c => new ArcadeStageAuthoring.Checkpoint
            {
                Id = (string)c["id"], Marker = Marker("Checkpoint_" + (string)c["id"], c["centre"]), HalfSize = Vector(c["halfSize"]),
                Respawn = Marker("Respawn_" + (string)c["id"], c["respawn"], (float)c["yaw"]), RequiredTargetCount = (ushort)(int)c["requiredTargets"],
            }).ToArray();
            config.Finish = Marker("Finish", spec["finish"]["centre"]);
            config.FinishHalfSize = Vector(spec["finish"]["halfSize"]);
            var removed = new List<string>();
            foreach (var child in aids.Cast<Transform>().ToArray())
                if (!keep.Contains(child.name))
                {
                    removed.Add(child.name);
                    Object.DestroyImmediate(child.gameObject);
                }
            EditorUtility.SetDirty(config);
            config.Compile();   // Shared rejects an invalid course before the scene is saved
            return new { config.ProfileId, targets = config.Targets.Length, checkpoints = config.Checkpoints.Length, moved, removed };
        }

        private static object ApplyMayhem(Transform aids, JObject spec)
        {
            var config = aids.TryGetComponent<ArcadeStageAuthoring>(out var existing)
                ? existing : aids.gameObject.AddComponent<ArcadeStageAuthoring>();
            config.MankiMayhem = true; config.CharacterCourse = config.Slopball = false; config.BallSpawn = null;
            config.ProfileId = (string)spec["profileId"]; config.GameplayRevision = (uint)spec["gameplayRevision"];
            config.AllowedPackageIds = Array.Empty<string>(); config.DeckTop = (float)spec["deckTop"];
            Transform Marker(string name, JToken position)
            {
                var marker = aids.Find(name);
                if (marker == null) { marker = new GameObject(name).transform; marker.SetParent(aids, false); }
                marker.position = Vector(position);
                return marker;
            }
            config.HostOrigin = Marker("MayhemHostOrigin", spec["hostOrigin"]);
            config.PlayAreaMin = Marker("MayhemPlayAreaMin", spec["playAreaMin"]);
            config.PlayAreaMax = Marker("MayhemPlayAreaMax", spec["playAreaMax"]);
            EditorUtility.SetDirty(config);
            config.Compile();
            return new { config.ProfileId, config.DeckTop };
        }

        private static Vector3 Vector(JToken value) => new((float)value[0], (float)value[1], (float)value[2]);
    }
}
#endif
