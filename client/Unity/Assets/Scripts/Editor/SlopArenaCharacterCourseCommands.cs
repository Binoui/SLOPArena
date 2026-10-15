#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class SlopArenaCharacterCourseCommands
    {
        public const string ScenePath = "Assets/Scenes/Arcade_FightGuyCourse.unity";
        private const string AssetDirectory = "Assets/Stages/fightguy_sanctuary";
        private const string SourcePath = AssetDirectory + "/fightguy_sanctuary.unity";
        private const string PrefabPath = "Assets/Resources/Stages/fightguy_sanctuary.prefab";
        private readonly struct Surface
        {
            public readonly string Name;
            public readonly Vector3 Position, Size;
            public readonly int Material;
            public Surface(string name, float x, float top, float z, float width, float depth, int material = 0, float thickness = 1)
            { Name = name; Position = new(x, top - thickness / 2, z); Size = new(width, thickness, depth); Material = material; }
        }
        private static readonly Vector3[] TargetPositions =
        {
            new(-10, 1.3f, -27.5f), new(-17, 1.3f, -29.5f), new(-28, 1.3f, -21), new(-30, 1.3f, 2), new(-26, 1.3f, 2),
            new(-20, 1.3f, 26), new(-15, 1.3f, 27), new(-8, 7.3f, 25), new(-5, 7.3f, 28), new(-5, 7.3f, 24),
            new(15, 7.3f, 19), new(18, 7.3f, 19), new(24, 12.3f, -3), new(28, 12.3f, -3), new(26, 12.3f, -6),
            new(28, 10.9f, -14), new(19, 8.8f, -22), new(27, 6.6f, -28), new(19, 4.3f, -36.5f), new(8, 1.3f, -32),
        };
        private static readonly Vector3[] CheckpointSpawns = { new(-27, .85f, 5), new(-5, 6.85f, 26), new(26, 11.85f, -5) };
        private static readonly Vector3 StartPosition = new(-6, .85f, -32);
        private static readonly Vector3 FinishPosition = new(7, .85f, -32);

        private static List<Surface> Surfaces()
        {
            var result = new List<Surface>
            {
                new("Training Courtyard", -6, 0, -30.5f, 36, 16, thickness: 2),
                new("Broken Bridge Takeoff", -28, 0, -20, 10, 10),
                new("Broken Bridge Landing", -28, 0, 2, 12, 10),
                new("Bridge Detour Pole 1", -35.5f, .25f, -13, 3.5f, 3.5f, 3, 4),
                new("Bridge Detour Pole 2", -41.5f, 1.3f, -4.5f, 3, 3, 3, 5),
                new("Bridge Detour Pole 3", -37, .7f, 2.5f, 3.3f, 3.3f, 3, 4),
                new("West Walk", -24, 0, 12.5f, 8, 9, 1),
                new("West Walk Turn", -20, 0, 20, 8, 4, 1),
                new("Terrace Takeoff", -18, 0, 26, 12, 8),
                new("Rising Terrace 1", -20, 1, 34, 4, 3.5f, 1),
                new("Rising Terrace 2", -13, 3.2f, 36.75f, 3.5f, 3.5f, 1),
                new("Rising Terrace 3", -7, 4.6f, 32.5f, 4, 4, 1),
                new("Upper Terrace Landing", -6, 6, 26, 10, 8),
                new("North Gallery", 3.5f, 6, 26, 7, 5, 1),
                new("North Gallery Turn", 13, 6, 26, 6, 5, 1),
                new("Summit Takeoff", 16, 6, 18, 8, 11),
                new("Summit Detour Island 1", 26, 7.1f, 16, 4, 4),
                new("Summit Detour Island 2", 33, 8.4f, 10, 3, 3),
                new("Summit Detour Island 3", 32, 10.1f, 1.5f, 3.5f, 3.5f),
                new("Summit Landing", 26, 11, -4, 7, 6),
                new("Descent Roof 1", 28, 9.6f, -14, 6, 5, 2),
                new("Descent Roof 2", 19, 7.5f, -22, 5.5f, 4.5f, 2),
                new("Descent Roof 3", 27, 5.3f, -28, 4, 4, 2),
                new("Descent Roof 4", 19, 3, -36.5f, 4.5f, 4, 2),
                // Original, solid landmark massing: visible surfaces also belong to the bake.
                new("Sanctuary Foundation", 0, -1, -1, 16, 16, thickness: 8),
                new("Sanctuary Hall", 0, 6, -1, 10, 10, thickness: 7),
                new("Sanctuary Roof", 0, 7, -1, 14, 13, 2),
                new("Sanctuary Crown", 0, 9, -1, 6, 6, 2, 2),
            };
            return result;
        }

        [MenuItem("Tools/SlopArena/Arcade/Phase 4/Prepare FightGuy Course")]
        private static void PrepareMenu() => Prepare();

        [CliCommand("sloparena.arcade.course.prepare", "Create missing original sanctuary greybox assets, gameplay scene and course markers, then bake the unified stage.",
            MainThreadRequired = true, Tags = new[] { "arcade", "authoring/stage" })]
        public static object Prepare([CliArg("rebuild", "Rebuild this approved generated greybox source and presentation.")] bool rebuild = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Prepare the course in stopped Edit Mode with the current scene saved.");
            var original = SceneManager.GetActiveScene();
            var existingSource = SceneManager.GetSceneByPath(SourcePath);
            if (existingSource.IsValid() && existingSource.isLoaded && existingSource.isDirty)
                throw new InvalidOperationException("Save the course source draft before preparing.");
            Directory.CreateDirectory(AssetDirectory); AssetDatabase.Refresh();
            try
            {
                if (rebuild || !File.Exists(SourcePath)) CreateSource();
                if (rebuild || !File.Exists(PrefabPath)) CreateVisual();
                if (!File.Exists(ScenePath)) SlopArenaArcadePhase1Commands.CreateBallScene(ScenePath, typeof(CharacterCourseMatch));
                var scenes = EditorBuildSettings.scenes.ToList();
                int index = scenes.FindIndex(x => x.path == ScenePath);
                if (index < 0) scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); else scenes[index].enabled = true;
                EditorBuildSettings.scenes = scenes.ToArray(); AssetDatabase.SaveAssets();
            }
            finally { if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original); }
            return new SlopArenaStageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Bake(CharacterCourseMatch.ArenaKey);
        }

        [MenuItem("Tools/SlopArena/Arcade/Phase 5/Apply Wider Course Spacing")]
        private static void RelayoutMenu() => Relayout();

        [CliCommand("sloparena.arcade.course.relayout", "Apply the approved wider course spacing to named source and presentation objects, retain the art sample, and rebake.",
            MainThreadRequired = true, Tags = new[] { "arcade", "authoring/stage" })]
        public static object Relayout()
        {
            var original = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || original.isDirty || !File.Exists(SourcePath) || !File.Exists(PrefabPath))
                throw new InvalidOperationException("Relayout requires the prepared course and a stopped, saved scene.");
            var source = SceneManager.GetSceneByPath(SourcePath); bool opened = !source.IsValid() || !source.isLoaded;
            if (!opened && source.isDirty) throw new InvalidOperationException("Save the course source draft before applying spacing.");
            if (opened) source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(source);
                var root = source.GetRootGameObjects().Single(x => x.name == "Stage_fightguy_sanctuary").transform;
                var geometry = root.Find("GameplayGeometry");
                foreach (var surface in Surfaces())
                {
                    var item = geometry.Find(surface.Name);
                    if (item == null)
                    {
                        item = Cube(geometry, surface.Name, surface.Position, surface.Size, null).transform;
                        Object.DestroyImmediate(item.GetComponent<MeshRenderer>()); item.gameObject.isStatic = true;
                    }
                    item.localPosition = surface.Position; item.localScale = surface.Size;
                }
                root.Find("SpawnPoints/Spawn_01").localPosition = StartPosition;
                var aids = root.Find("AuthoringAids");
                for (int i = 0; i < TargetPositions.Length; i++) aids.Find("Target_" + (i + 1).ToString("00")).localPosition = TargetPositions[i];
                for (int i = 0; i < CheckpointSpawns.Length; i++)
                {
                    aids.Find("Checkpoint_" + (i + 1)).localPosition = CheckpointSpawns[i];
                    aids.Find("Respawn_" + (i + 1)).localPosition = CheckpointSpawns[i];
                }
                aids.Find("Finish").localPosition = FinishPosition;
                EditorSceneManager.SaveScene(source);
            }
            finally
            {
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (opened) EditorSceneManager.CloseScene(source, true);
            }
            bool hasArt;
            var visual = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                hasArt = visual.transform.Find("Cloudtop First Crossing") != null;
                var materials = new[] { "Stone", "Jade", "Clay", "Cedar", "Cue" }
                    .Select(name => AssetDatabase.LoadAssetAtPath<Material>(AssetDirectory + "/" + name + ".mat")).ToArray();
                if (materials.Any(x => x == null)) throw new InvalidOperationException("Missing existing course material.");
                foreach (var surface in Surfaces())
                {
                    var item = visual.transform.Find(surface.Name);
                    if (item == null) item = Cube(visual.transform, surface.Name, surface.Position, surface.Size, materials[surface.Material]).transform;
                    item.localPosition = surface.Position; item.localScale = surface.Size;
                }
                void MoveFrame(string name, Vector3 destination)
                {
                    var parts = visual.transform.Cast<Transform>().Where(x => x.name == name).ToArray();
                    if (parts.Length != 4) throw new InvalidOperationException("Expected four existing frame parts: " + name);
                    var center = parts.Aggregate(Vector3.zero, (p, t) => p + t.localPosition) / parts.Length;
                    foreach (var part in parts) part.localPosition += destination - center;
                }
                for (int i = 0; i < CheckpointSpawns.Length; i++) MoveFrame("Checkpoint " + (i + 1), CheckpointSpawns[i] - Vector3.up * .8f);
                var finishParts = visual.transform.Cast<Transform>().Where(x => x.name == "Finish").ToArray();
                var oldFinish = finishParts.Aggregate(Vector3.zero, (p, t) => p + t.localPosition) / finishParts.Length;
                var finishDelta = new Vector3(FinishPosition.x - oldFinish.x, 0, FinishPosition.z - oldFinish.z);
                MoveFrame("Finish", new(FinishPosition.x, .05f, FinishPosition.z));
                foreach (var part in visual.transform.Cast<Transform>().Where(x => x.name == "Shrine Gate" || x.name == "Shrine Lintel")) part.localPosition += finishDelta;
                foreach (var arrow in visual.transform.Cast<Transform>().Where(x => x.name == "Route Arrow").ToArray()) Object.DestroyImmediate(arrow.gameObject);
                RouteCues(visual.transform, materials[4]);
                PrefabUtility.SaveAsPrefabAsset(visual, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(visual); }
            var bake = new SlopArenaStageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Bake(CharacterCourseMatch.ArenaKey);
            if (!bake.Success) return bake;
            object art = hasArt ? SlopArenaSanctuaryArtCommands.Apply() : null;
            return new { success = true, bake, retainedArtSample = hasArt, art };
        }

        [MenuItem("Tools/SlopArena/Arcade/Phase 4/Open FightGuy Course")]
        private static void OpenMenu() => Open();
        [CliCommand("sloparena.arcade.course.open", "Open the FightGuy sanctuary course; press Play to begin.", MainThreadRequired = true, Tags = new[] { "arcade" })]
        public static object Open() => SlopArenaArcadePhase1Commands.OpenBallScene(ScenePath, CharacterCourseMatch.ArenaKey, "fightguy");

        [CliCommand("sloparena.arcade.course.status", "Observe real course state, targets, checkpoints, fighter and records.", MainThreadRequired = true, Tags = new[] { "arcade", "simulation" })]
        public static object Status()
        {
            var match = Object.FindFirstObjectByType<CharacterCourseMatch>(); var sim = match?.Simulation;
            var state = sim?.Course; var fighter = sim?.GetState(1);
            return new { success = true, scene = SceneManager.GetActiveScene().path, ready = match?.Ready ?? false,
                tick = match?.SimulationTick, phase = state?.Phase.ToString(), countdownTicks = state?.CountdownTicks,
                elapsedTicks = state?.ElapsedTicks, brokenTargets = state?.BrokenTargets, totalTargets = state?.TotalTargets,
                checkpoint = state?.SavedCheckpoint, savedBest = match?.SavedBest, recordUpdate = match?.LastRecordUpdate,
                fighter = fighter.HasValue ? new { fighter.Value.PX, fighter.Value.PY, fighter.Value.PZ,
                    fighter.Value.IsGrounded, fighter.Value.Deaths, cooldownE = fighter.Value.GetCooldown(AbilitySlots.E),
                    cooldownR = fighter.Value.GetCooldown(AbilitySlots.R) } : null,
                recordsFile = ArcadeRecordsStore.Instance.Path, recordFeedback = ArcadeRecordsStore.Instance.Feedback };
        }

        [CliCommand("sloparena.arcade.course.run", "Apply bounded normal semantic inputs to the live course; observe actual target and checkpoint events.",
            MainThreadRequired = true, Tags = new[] { "arcade", "simulation" })]
        public static async Task<object> Run([CliArg("steps", "Training step JSON, at most 600 ticks.", Required = true)] string steps,
            [CliArg("from-go", "Wait through the genuine countdown with neutral input.")] bool fromGo = false,
            [CliArg("freeze-after", "Freeze simulation at the final input boundary; the next run resumes it after installing inputs. Use within one owned batch.")] bool freezeAfter = false)
        {
            var match = Object.FindFirstObjectByType<CharacterCourseMatch>();
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || Time.timeScale <= 0 || match == null || !match.Ready
                || match.Simulation.Course.Value.Ended || match.ProbeInputActive)
                throw new InvalidOperationException("Run requires a ready, unpaused, unfinished course without another probe.");
            var parsed = SlopArenaTrainingCommands.ParseSteps(steps);
            if (parsed.Sum(x => x.Ticks) > 600) throw new ArgumentException("Course probes are limited to 600 ticks.");
            int index = 0, stepTicks = 0, totalTicks = 0; bool waiting = fromGo && match.Simulation.Course.Value.Phase == CharacterCoursePhase.Countdown;
            var events = new List<object>(); var hits = new List<object>();
            var completion = new TaskCompletionSource<bool>(); double deadline = EditorApplication.timeSinceStartup + 30;
            void Complete() { match.ProbeFrozen = freezeAfter; completion.TrySetResult(true); }
            void Apply(bool first)
            { short yaw = (short)(match.Simulation.GetState(1).FacingYaw * Mathf.Rad2Deg * 100); match.SetProbeInput(SlopArenaTrainingCommands.BuildInput(parsed[index], first, yaw)); }
            void Tick(uint tick)
            {
                if (completion.Task.IsCompleted) return;
                foreach (var item in match.Simulation.LastTickCourseEvents) events.Add(new { kind = item.Kind.ToString(), item.Index, item.ElapsedTicks });
                foreach (var hit in match.Simulation.LastTickHits) hits.Add(new { tick, hit.OwnerEntityId, hit.TargetEntityId, hit.Damage, hit.AttackSlot });
                if (waiting) { if (match.Simulation.Course.Value.Phase != CharacterCoursePhase.Countdown) { waiting = false; Apply(true); } return; }
                totalTicks++; stepTicks++;
                if (match.Simulation.Course.Value.Ended) { Complete(); return; }
                if (stepTicks >= parsed[index].Ticks) { index++; stepTicks = 0; if (index == parsed.Count) { Complete(); return; } Apply(true); }
                else Apply(false);
            }
            void Check() { if (match == null || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) completion.TrySetException(new InvalidOperationException("Course probe stopped or exceeded its wall deadline.")); }
            match.ProbeTickCompleted += Tick; EditorApplication.update += Check;
            try
            {
                if (waiting) match.SetProbeInput(default(InputState)); else Apply(true);
                match.ProbeFrozen = false;
                await completion.Task;
                return new { success = true, completedTicks = totalTicks, events, hits, state = Status() };
            }
            finally { EditorApplication.update -= Check; if (match != null) { match.ProbeTickCompleted -= Tick; match.SetProbeInput(null); } }
        }

        private static void CreateSource()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Stage_fightguy_sanctuary");
                var geometry = Child(root.transform, "GameplayGeometry");
                foreach (var surface in Surfaces())
                {
                    var item = Cube(geometry.transform, surface.Name, surface.Position, surface.Size, null);
                    Object.DestroyImmediate(item.GetComponent<MeshRenderer>()); item.isStatic = true;
                }
                var spawns = Child(root.transform, "SpawnPoints");
                var spawn = Marker(spawns.transform, "Spawn_01", StartPosition); spawn.tag = "SpawnPoint";
                var aids = Child(root.transform, "AuthoringAids");
                var config = aids.AddComponent<ArcadeStageAuthoring>(); config.CharacterCourse = true;
                config.ProfileId = "fightguy_sanctuary_v1"; config.AllowedPackageIds = new[] { "fightguy" };
                config.Targets = TargetPositions.Select((p, i) => new ArcadeStageAuthoring.Target
                    { Id = "target_" + (i + 1).ToString("00"), Marker = Marker(aids.transform, "Target_" + (i + 1).ToString("00"), p), Radius = .65f }).ToArray();
                config.Checkpoints = CheckpointSpawns.Select((p, i) => new ArcadeStageAuthoring.Checkpoint
                    { Id = "checkpoint_" + (i + 1), Marker = Marker(aids.transform, "Checkpoint_" + (i + 1), p),
                        Respawn = Marker(aids.transform, "Respawn_" + (i + 1), p), HalfSize = new(3, 2, 1.4f), RequiredTargetCount = (ushort)((i + 1) * 5) }).ToArray();
                config.Finish = Marker(aids.transform, "Finish", FinishPosition); config.FinishHalfSize = new(3, 2, 2);
                EditorSceneManager.SaveScene(scene, SourcePath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        private static void CreateVisual()
        {
            var materials = new[] { Material("Stone", new(.62f, .66f, .62f)), Material("Jade", new(.27f, .5f, .43f)),
                Material("Clay", new(.55f, .29f, .25f)), Material("Cedar", new(.43f, .33f, .24f)), Material("Cue", new(1, .8f, .2f)) };
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var root = new GameObject("FightGuy Sanctuary Greybox");
            try
            {
                foreach (var surface in Surfaces()) Cube(root.transform, surface.Name, surface.Position, surface.Size, materials[surface.Material]);
                for (int i = 0; i < CheckpointSpawns.Length; i++)
                {
                    var p = CheckpointSpawns[i]; p.y -= .8f;
                    Frame(root.transform, "Checkpoint " + (i + 1), p, 6, 2.8f, materials[1]);
                }
                Frame(root.transform, "Finish", new(FinishPosition.x, .05f, FinishPosition.z), 6, 4, materials[4]);
                foreach (float x in new[] { FinishPosition.x - 3.4f, FinishPosition.x + 3.4f })
                    Cube(root.transform, "Shrine Gate", new(x, 2.5f, FinishPosition.z), new(.6f, 5, .6f), materials[2]);
                Cube(root.transform, "Shrine Lintel", new(FinishPosition.x, 5, FinishPosition.z), new(8, .7f, 1.3f), materials[2]);
                RouteCues(root.transform, materials[4]);
                var light = Child(root.transform, "LocalLighting").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = .85f; light.color = new(1, .94f, .82f);
                light.transform.rotation = Quaternion.Euler(50, -25, 0);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); EditorSceneManager.CloseScene(scene, true); }
        }
        private static void RouteCues(Transform parent, Material material)
        {
            Arrow(parent, new(-15, .04f, -29), -50, material);
            Arrow(parent, new(-28, .04f, -16.5f), 0, material);
            Arrow(parent, new(-26, .04f, 5), 20, material);
            Arrow(parent, new(-24, .04f, 14), 25, material);
            Arrow(parent, new(-17, .04f, 26), 90, material);
            Arrow(parent, new(-3, 6.04f, 26), 90, material);
            Arrow(parent, new(5.5f, 6.04f, 26), 90, material);
            Arrow(parent, new(16, 6.04f, 15), 153, material);
            Arrow(parent, new(26, 11.04f, -6), 180, material);
        }
        private static void Arrow(Transform parent, Vector3 p, float yaw, Material material)
        {
            var rotation = Quaternion.Euler(0, yaw, 0);
            var stem = Cube(parent, "Route Arrow", p, new(.16f, .04f, 1.5f), material);
            stem.transform.rotation = rotation;
            foreach (float side in new[] { -1f, 1f })
            {
                var head = Cube(parent, "Route Arrow", p + rotation * new Vector3(side * .3f, 0, .45f), new(.14f, .04f, .85f), material);
                head.transform.rotation = rotation * Quaternion.Euler(0, -side * 45, 0);
            }
        }
        private static void Frame(Transform parent, string name, Vector3 p, float width, float depth, Material material)
        {
            foreach (float x in new[] { -width / 2, width / 2 }) Cube(parent, name, p + new Vector3(x, 0, 0), new(.12f, .06f, depth), material);
            foreach (float z in new[] { -depth / 2, depth / 2 }) Cube(parent, name, p + new Vector3(0, 0, z), new(width, .06f, .12f), material);
        }
        private static GameObject Child(Transform parent, string name)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }
        private static Transform Marker(Transform parent, string name, Vector3 position)
        { var marker = Child(parent, name).transform; marker.position = position; return marker; }
        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = size; Object.DestroyImmediate(go.GetComponent<Collider>());
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
        private static Material Material(string name, Color color)
        {
            string path = AssetDirectory + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path); return material;
        }
    }
}
#endif
