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
    /// <summary>Original primitive assets and a dedicated runner; never rewrites the offline/PvP scenes.</summary>
    public static class SlopArenaArcadePhase1Commands
    {
        public const string ScenePath = "Assets/Scenes/Prototypes/ArcadeBallLab.unity";
        private const string StageDirectory = "Assets/Stages/arcade_ball_lab";
        private const string SourcePath = StageDirectory + "/arcade_ball_lab.unity";
        private const string PrefabPath = "Assets/Resources/Stages/arcade_ball_lab.prefab";

        [CliCommand("sloparena.arcade.phase1.prepare", "Create missing original Phase 1 court assets and scene, then validate/bake the unified stage.",
            MainThreadRequired = true, Tags = new[] { "arcade", "authoring/stage" })]
        public static SlopArenaStageBakeResult Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Prepare the Arcade playground in Edit Mode.");
            Directory.CreateDirectory(StageDirectory);
            AssetDatabase.Refresh();
            var original = SceneManager.GetActiveScene();
            try
            {
                Mesh shell = LoadOrCreateMesh(StageDirectory + "/CourtShell.asset", ShellMesh());
                Mesh floor = LoadOrCreateMesh(StageDirectory + "/CourtFloor.asset", FloorMesh());
                Material floorMaterial = LoadOrCreateMaterial(StageDirectory + "/CourtFloor.mat", new Color(.16f, .24f, .34f), false);
                Material lineMaterial = LoadOrCreateMaterial(StageDirectory + "/CourtLines.mat", new Color(.25f, .8f, 1f), true);
                if (!File.Exists(SourcePath)) CreateSource(shell);
                if (!File.Exists(PrefabPath)) CreateVisual(floor, floorMaterial, lineMaterial);
                if (!File.Exists(ScenePath)) CreateBallScene(ScenePath, typeof(ArcadeBallLabMatch));
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
            return new SlopArenaStageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Bake(ArcadeBallLabMatch.ArenaKey);
        }

        [MenuItem("Tools/SlopArena/Arcade/Phase 1/Prepare Test Court")]
        private static void PrepareMenu()
        {
            var result = Prepare();
            if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(x => x.Message)));
            Debug.Log("[Arcade Phase 1] Test court prepared. Open a fighter playground from the Arcade menu.");
        }
        [MenuItem("Tools/SlopArena/Arcade/Phase 1/Open FightGuy")]
        private static void OpenFightGuy() => Open("fightguy");
        [MenuItem("Tools/SlopArena/Arcade/Phase 1/Open Manki")]
        private static void OpenManki() => Open("manki");
        [MenuItem("Tools/SlopArena/Arcade/Phase 1/Open Wibou")]
        private static void OpenWibou() => Open("wibou");
        [MenuItem("Tools/SlopArena/Arcade/Phase 1/Open Bonk")]
        private static void OpenBonk() => Open("bonk");

        [CliCommand("sloparena.arcade.phase1.open", "Open the prepared Phase 1 playground in Edit Mode with a roster fighter selected; press Play to run.",
            MainThreadRequired = true, Tags = new[] { "arcade" })]
        public static object Open([CliArg("fighter", "fightguy, manki, wibou or bonk.")] string fighter = "fightguy")
        {
            return OpenBallScene(ScenePath, ArcadeBallLabMatch.ArenaKey, fighter);
        }

        internal static object OpenBallScene(string scenePath, string stageKey, string fighter)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the playground in Edit Mode.");
            if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save the current scene edits before switching to the playground.");
            var selected = fighter switch
            {
                "fightguy" => CharacterClass.FightGuy, "manki" => CharacterClass.Manki,
                "wibou" => CharacterClass.Wibou, "bonk" => CharacterClass.Bonk,
                _ => throw new ArgumentException("Unknown roster fighter.", nameof(fighter))
            };
            if (!File.Exists(scenePath)) throw new InvalidOperationException("Run phase1.prepare first.");
            MatchConfig.ClearActiveMatch();
            MatchConfig.Mode = GameMode.Arcade;
            MatchConfig.PlayerClass = selected; MatchConfig.PlayerPackageId = fighter;
            MatchConfig.ArenaName = stageKey;
            SessionState.SetString(ArcadeBallLabMatch.FighterSessionKey, selected.ToString());
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            return new { success = true, scene = scenePath, fighter };
        }

        [CliCommand("sloparena.arcade.phase1.run", "Run a bounded semantic input sequence in the live ball playground and return actual Shared contacts; mutates this playground.",
            MainThreadRequired = true, Tags = new[] { "arcade", "simulation" })]
        public static async Task<object> Run(
            [CliArg("steps", "Semantic step JSON using the existing Training sequence format; at most 600 Shared ticks.", Required = true)] string steps,
            [CliArg("reset-ball", "Start this probe with a fresh ball, retaining fighter resources.")] bool resetBall = false)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || Time.timeScale <= 0f)
                throw new InvalidOperationException("Run requires the live, unpaused playground.");
            return await RunBallSteps(Object.FindFirstObjectByType<ArcadeBallLabMatch>(), steps, resetBall);
        }

        internal static async Task<object> RunBallSteps(ArcadeBallMatch match, string steps, bool resetBall = false, bool fromGo = false)
        {
            if (match == null || !match.Ready || match.ProbeInputActive)
                throw new InvalidOperationException("The playground must be ready and have no existing probe input.");
            var parsed = SlopArenaTrainingCommands.ParseSteps(steps);
            foreach (var step in parsed)
                if (step.TargetEntityIdByte != 0 && step.TargetEntityIdByte != ServerSimulation.ArcadeBallTargetInputId)
                    throw new ArgumentException("The playground's only selectable opponent is ball alias 255.");
            if (resetBall) match.Simulation.ResetArcadeBall();
            uint startTick = match.SimulationTick;
            int index = 0, stepTicks = 0, totalTicks = 0;
            var hits = new List<object>();
            var goals = new List<object>();
            var completion = new TaskCompletionSource<bool>();
            bool waitingForGo = fromGo && match.Simulation.Slopball?.Phase == SlopballPhase.Countdown;
            double deadline = EditorApplication.timeSinceStartup + 20d;
            void Apply(bool first)
            {
                short yaw = (short)(match.Simulation.GetState(1).FacingYaw * Mathf.Rad2Deg * 100f);
                match.SetProbeInput(SlopArenaTrainingCommands.BuildInput(parsed[index], first, yaw));
            }
            void Tick(uint tick)
            {
                if (completion.Task.IsCompleted) return;
                foreach (var hit in match.Simulation.LastTickHits)
                    hits.Add(new { tick, owner = hit.OwnerEntityId.ToString(), target = hit.TargetEntityId.ToString(),
                        slot = hit.AttackSlot, damage = hit.Damage, force = hit.ImpactForce, angle = hit.KnockbackAngle,
                        dirX = hit.DirX, dirZ = hit.DirZ, contactTime = hit.ContactTime });
                foreach (var goal in match.Simulation.LastTickSlopballEvents)
                    goals.Add(new { tick, kind = goal.Kind.ToString(), ballId = goal.BallId.ToString(), returns = goal.Returns, time = goal.ContactTime });
                if (waitingForGo)
                {
                    if (match.Simulation.Slopball?.Phase != SlopballPhase.Countdown)
                    { waitingForGo = false; Apply(true); }
                    return;
                }
                totalTicks++; stepTicks++;
                if (match.Simulation.Slopball is SlopballState challenge && challenge.Ended)
                { completion.TrySetResult(true); return; }
                if (stepTicks >= parsed[index].Ticks)
                {
                    index++; stepTicks = 0;
                    if (index == parsed.Count) { completion.TrySetResult(true); return; }
                    Apply(true);
                }
                else Apply(false);
            }
            void Check()
            {
                if (match == null || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    completion.TrySetException(new InvalidOperationException("The live Arcade probe stopped or exceeded its wall deadline."));
            }
            match.ProbeTickCompleted += Tick;
            EditorApplication.update += Check;
            try
            {
                if (waitingForGo) match.SetProbeInput(default(InputState));
                else Apply(true);
                await completion.Task;
                var ball = match.Simulation.ArcadeBall!.Value;
                return new { success = true, startTick, endTick = match.SimulationTick, completedTicks = totalTicks,
                    fighter = MatchConfig.PlayerClass.ToString(), hitCount = hits.Count, hits, goals,
                    challenge = match.Simulation.Slopball.HasValue ? new { phase = match.Simulation.Slopball.Value.Phase.ToString(), returns = match.Simulation.Slopball.Value.Returns } : null,
                    ball = new { id = ball.EntityId.ToString(), x = ball.Position.X, y = ball.Position.Y,
                        z = ball.Position.Z, percent = ball.DamagePercent } };
            }
            finally
            {
                EditorApplication.update -= Check;
                if (match != null) { match.ProbeTickCompleted -= Tick; match.SetProbeInput(null); }
            }
        }

        private static void CreateSource(Mesh mesh)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Stage_" + ArcadeBallLabMatch.ArenaKey);
                var geometry = Child(root.transform, "GameplayGeometry");
                var spawns = Child(root.transform, "SpawnPoints");
                var aids = Child(root.transform, "AuthoringAids");
                var shell = Child(geometry.transform, "CourtShell"); shell.isStatic = true;
                shell.AddComponent<MeshFilter>().sharedMesh = mesh;
                var spawn = Child(spawns.transform, "Spawn_01"); spawn.tag = "SpawnPoint";
                spawn.transform.localPosition = new(0f, .85f, -3f);
                var marker = Child(aids.transform, "BallSpawn"); marker.transform.localPosition = new(0f, 1.7f, -1.2f);
                var authoring = aids.AddComponent<ArcadeStageAuthoring>();
                authoring.BallSpawn = marker.transform; authoring.InitialVelocity = new(1.5f, 4f, 1f);
                EditorSceneManager.SaveScene(scene, SourcePath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static void CreateVisual(Mesh floor, Material floorMaterial, Material lineMaterial)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("ArcadeBallLab Court");
            try
            {
                var surface = Child(root.transform, "CourtFloor");
                surface.AddComponent<MeshFilter>().sharedMesh = floor;
                surface.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;
                Line(root.transform, "Floor outline", lineMaterial, new[]
                {
                    new Vector3(-8, .02f, -10), new Vector3(-8, .02f, 10), new Vector3(8, .02f, 10),
                    new Vector3(8, .02f, -10), new Vector3(-8, .02f, -10)
                });
                foreach (float x in new[] { -8f, 8f })
                foreach (float z in new[] { -10f, 10f })
                    Line(root.transform, "Court corner", lineMaterial, new[] { new Vector3(x, 0, z), new Vector3(x, 8, z) });
                Line(root.transform, "Ceiling outline", lineMaterial, new[]
                {
                    new Vector3(-8, 8, -10), new Vector3(-8, 8, 10), new Vector3(8, 8, 10),
                    new Vector3(8, 8, -10), new Vector3(-8, 8, -10)
                });
                Line(root.transform, "Midcourt cue", lineMaterial, new[] { new Vector3(-8, .025f, 0), new Vector3(8, .025f, 0) });
                var light = Child(root.transform, "LocalLighting").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.2f;
                light.transform.rotation = Quaternion.Euler(45, -35, 0);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        internal static void CreateBallScene(string scenePath, Type matchType)
        {
            if (!AssetDatabase.CopyAsset("Assets/Scenes/Arena_Offline.unity", scenePath))
                throw new InvalidOperationException("Could not copy the normal input/camera/presentation scene for the playground.");
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var training = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<TrainingMatch>(true)).Single();
                var original = new SerializedObject(training);
                var match = (MatchBase)training.gameObject.AddComponent(matchType);
                if (matchType == typeof(SlopballMatch))
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var orbit in root.GetComponentsInChildren<Unity.Cinemachine.CinemachineOrbitalFollow>(true))
                        { orbit.Radius = 8f; orbit.RadialAxis.Value = 1f; }
                var replacement = new SerializedObject(match);
                foreach (string property in new[] { "_playerRenderer", "_inputController", "_cameraMount", "_aimHandler", "_hudManager",
                    "_playerModelPrefab", "_playerAnimConfig", "_playerWeaponConfig", "_crosshairTexture", "_crosshairSize", "_combatFeedback", "_projectileVFX" })
                {
                    var source = original.FindProperty(property);
                    if (source != null && replacement.FindProperty(property) != null) replacement.CopyFromSerializedProperty(source);
                }
                replacement.ApplyModifiedPropertiesWithoutUndo();
                var npc = original.FindProperty("_npcRenderer")?.objectReferenceValue as Component;
                if (npc != null) Object.DestroyImmediate(npc.gameObject);
                match.gameObject.name = matchType.Name;
                Object.DestroyImmediate(training);
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static GameObject Child(Transform parent, string name)
        { var child = new GameObject(name); child.transform.SetParent(parent, false); return child; }
        private static void Line(Transform parent, string name, Material material, Vector3[] points)
        {
            var renderer = Child(parent, name).AddComponent<LineRenderer>();
            renderer.useWorldSpace = false; renderer.sharedMaterial = material;
            renderer.startWidth = renderer.endWidth = .045f;
            renderer.positionCount = points.Length; renderer.SetPositions(points);
        }
        private static Mesh LoadOrCreateMesh(string path, Mesh proposed)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { Object.DestroyImmediate(proposed); return existing; }
            AssetDatabase.CreateAsset(proposed, path); return proposed;
        }
        private static Material LoadOrCreateMaterial(string path, Color color, bool unlit)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color); AssetDatabase.CreateAsset(material, path); return material;
        }
        private static Mesh FloorMesh()
        {
            var vertices = new List<Vector3>(); var indices = new List<int>();
            Quad(vertices, indices, new(-8, 0, -10), new(-8, 0, 10), new(8, 0, 10), new(8, 0, -10));
            return Mesh("Phase 1 court floor", vertices, indices);
        }
        private static Mesh ShellMesh()
        {
            var vertices = new List<Vector3>(); var indices = new List<int>();
            Quad(vertices, indices, new(-8, 0, -10), new(-8, 0, 10), new(8, 0, 10), new(8, 0, -10));
            Quad(vertices, indices, new(-8, 8, -10), new(8, 8, -10), new(8, 8, 10), new(-8, 8, 10));
            Quad(vertices, indices, new(-8, 0, -10), new(-8, 8, -10), new(-8, 8, 10), new(-8, 0, 10));
            Quad(vertices, indices, new(8, 0, -10), new(8, 0, 10), new(8, 8, 10), new(8, 8, -10));
            Quad(vertices, indices, new(-8, 0, -10), new(8, 0, -10), new(8, 8, -10), new(-8, 8, -10));
            Quad(vertices, indices, new(-8, 0, 10), new(-8, 8, 10), new(8, 8, 10), new(8, 0, 10));
            return Mesh("Phase 1 closed court collision", vertices, indices);
        }
        private static void Quad(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count; vertices.AddRange(new[] { a, b, c, d });
            indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        private static Mesh Mesh(string name, List<Vector3> vertices, List<int> indices)
        {
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
#endif
