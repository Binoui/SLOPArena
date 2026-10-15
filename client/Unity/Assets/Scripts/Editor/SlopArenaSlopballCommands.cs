#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SlopArena.Client.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class SlopArenaSlopballCommands
    {
        public const string ScenePath = "Assets/Scenes/Prototypes/Slopball.unity";
        private const string DirectoryPath = "Assets/Stages/slopball";
        private const string SourcePath = DirectoryPath + "/slopball.unity";
        private const string PrefabPath = "Assets/Resources/Stages/slopball.prefab";

        [CliCommand("sloparena.arcade.slopball.prepare", "Create missing original Slopball court assets and prototype, then validate and bake its unified stage.",
            MainThreadRequired = true, Tags = new[] { "arcade", "authoring/stage" })]
        public static SlopArenaStageBakeResult Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Prepare Slopball in Edit Mode.");
            Directory.CreateDirectory(DirectoryPath); AssetDatabase.Refresh();
            var original = SceneManager.GetActiveScene();
            try
            {
                Mesh shell = MeshAsset(DirectoryPath + "/CourtShell.asset", Room());
                Mesh net = MeshAsset(DirectoryPath + "/Net.asset", Net());
                Mesh playerFloor = MeshAsset(DirectoryPath + "/PlayerFloor.asset", Plane(-20, 0));
                Mesh senderFloor = MeshAsset(DirectoryPath + "/SenderFloor.asset", Plane(0, 20));
                Material wall = MaterialAsset("Shell", new Color(.13f, .19f, .26f), false);
                Material player = MaterialAsset("Player", new Color(.12f, .3f, .39f), false);
                Material sender = MaterialAsset("Sender", new Color(.27f, .19f, .34f), false);
                Material line = MaterialAsset("Lines", new Color(.36f, .9f, 1f), true);
                Material tape = MaterialAsset("NetTape", new Color(1f, .85f, .32f), true);
                if (!File.Exists(SourcePath)) CreateSource(shell, net);
                if (!File.Exists(PrefabPath)) CreateVisual(shell, playerFloor, senderFloor, wall, player, sender, line, tape);
                if (!File.Exists(ScenePath)) SlopArenaArcadePhase1Commands.CreateBallScene(ScenePath, typeof(SlopballMatch));
                AssetDatabase.SaveAssets();
            }
            finally { if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original); }
            return new SlopArenaStageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Bake(SlopballMatch.ArenaKey);
        }

        [CliCommand("sloparena.arcade.slopball.retune", "Apply the approved larger court and easier ball profile, rebuild its original presentation, and bake.",
            MainThreadRequired = true, Tags = new[] { "arcade", "authoring/stage" })]
        public static SlopArenaStageBakeResult Retune()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Retune Slopball in Edit Mode.");
            var original = SceneManager.GetActiveScene();
            var source = SceneManager.GetSceneByPath(SourcePath);
            bool opened = !source.IsValid() || !source.isLoaded;
            if (!opened && source.isDirty) throw new InvalidOperationException("Save the Slopball source draft before retuning.");
            try
            {
                var shell = ReplaceMesh(DirectoryPath + "/CourtShell.asset", Room());
                ReplaceMesh(DirectoryPath + "/Net.asset", Net());
                var playerFloor = ReplaceMesh(DirectoryPath + "/PlayerFloor.asset", Plane(-20, 0));
                var senderFloor = ReplaceMesh(DirectoryPath + "/SenderFloor.asset", Plane(0, 20));
                if (opened) source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Additive);
                try
                {
                    var root = source.GetRootGameObjects().Single(x => x.name == "Stage_slopball");
                    var config = root.GetComponentInChildren<ArcadeStageAuthoring>();
                    ApplyShotProfile(config);
                    config.BallSpawn.localPosition = new(0, 2.3f, 6);
                    root.transform.Find("SpawnPoints/Spawn_01").localPosition = new(0, .85f, -6);
                    EditorSceneManager.MarkSceneDirty(source);
                    EditorSceneManager.SaveScene(source);
                }
                finally { if (opened) EditorSceneManager.CloseScene(source, true); }
                CreateVisual(shell, playerFloor, senderFloor,
                    MaterialAsset("Shell", new Color(.13f, .19f, .26f), false),
                    MaterialAsset("Player", new Color(.12f, .3f, .39f), false),
                    MaterialAsset("Sender", new Color(.27f, .19f, .34f), false),
                    MaterialAsset("Lines", new Color(.36f, .9f, 1f), true),
                    MaterialAsset("NetTape", new Color(1f, .85f, .32f), true));
                AssetDatabase.SaveAssets();
            }
            finally { if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original); }
            return new SlopArenaStageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Bake(SlopballMatch.ArenaKey);
        }

        private static void ApplyShotProfile(ArcadeStageAuthoring config)
        {
            config.GameplayRevision = 6;
            config.InitialVelocity = Vector3.zero; config.Radius = .65f; config.Gravity = 6;
            config.Restitution = .8f; config.LaunchScale = 2.4f; config.LinearDrag = .35f;
            config.EasyFlightTicks = 138; config.HardFlightTicks = 108;
            config.FlightJitterTicks = 30; config.LandingHalfWidth = 6;
            config.LandingMinZ = -14; config.LandingMaxZ = -2.5f;
            config.SpawnHalfWidth = 8; config.SpawnHalfDepth = 1;
        }

        private static Mesh ReplaceMesh(string path, Mesh proposed)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(proposed, path); return proposed; }
            // Mesh setters invalidate Unity's cached render buffers as well as the
            // serialized data. CopySerialized alone can leave the old room visible.
            existing.Clear();
            existing.vertices = proposed.vertices;
            existing.triangles = proposed.triangles;
            existing.normals = proposed.normals;
            existing.bounds = proposed.bounds;
            Object.DestroyImmediate(proposed); EditorUtility.SetDirty(existing); return existing;
        }

        [MenuItem("Tools/SlopArena/Arcade/Phase 2/Prepare Slopball")]
        private static void PrepareMenu()
        {
            var result = Prepare();
            if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(x => x.Message)));
        }
        [MenuItem("Tools/SlopArena/Arcade/Phase 2/Open FightGuy")]
        private static void FightGuy() => Open("fightguy");
        [MenuItem("Tools/SlopArena/Arcade/Phase 2/Open Manki")]
        private static void Manki() => Open("manki");
        [MenuItem("Tools/SlopArena/Arcade/Phase 2/Open Wibou")]
        private static void Wibou() => Open("wibou");
        [MenuItem("Tools/SlopArena/Arcade/Phase 2/Open Bonk")]
        private static void Bonk() => Open("bonk");

        [CliCommand("sloparena.arcade.slopball.open", "Open the Slopball prototype with a normal roster fighter; press Play to begin.",
            MainThreadRequired = true, Tags = new[] { "arcade" })]
        public static object Open([CliArg("fighter", "fightguy, manki, wibou or bonk.")] string fighter = "fightguy")
            => SlopArenaArcadePhase1Commands.OpenBallScene(ScenePath, SlopballMatch.ArenaKey, fighter);

        [CliCommand("sloparena.arcade.slopball.run", "Apply bounded normal semantic inputs to live Slopball; reports actual contacts/goals across automatic fresh serves.",
            MainThreadRequired = true, Tags = new[] { "arcade", "simulation" })]
        public static Task<object> Run([CliArg("steps", "Existing Training step JSON, at most 600 Shared ticks.", Required = true)] string steps,
            [CliArg("from-go", "Wait through the real countdown with neutral input before applying steps.")] bool fromGo = false)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || Time.timeScale <= 0f)
                throw new InvalidOperationException("Run requires live unpaused Slopball.");
            var match = Object.FindFirstObjectByType<SlopballMatch>();
            if (match == null || match.Simulation?.Slopball?.Ended == true)
                throw new InvalidOperationException("Retry the ended Slopball attempt before running inputs.");
            return SlopArenaArcadePhase1Commands.RunBallSteps(match, steps, fromGo: fromGo);
        }

        private static void CreateSource(Mesh shell, Mesh net)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Stage_slopball");
                var geometry = Child(root.transform, "GameplayGeometry");
                var shellObject = Child(geometry.transform, "CourtShell"); shellObject.isStatic = true;
                shellObject.AddComponent<MeshFilter>().sharedMesh = shell;
                var netObject = Child(geometry.transform, "Net"); netObject.isStatic = true;
                netObject.AddComponent<MeshFilter>().sharedMesh = net;
                var spawns = Child(root.transform, "SpawnPoints");
                var spawn = Child(spawns.transform, "Spawn_01"); spawn.tag = "SpawnPoint";
                spawn.transform.localPosition = new(0, .85f, -6);
                var aids = Child(root.transform, "AuthoringAids");
                var marker = Child(aids.transform, "BallSpawn"); marker.transform.localPosition = new(0, 2.3f, 6);
                var config = aids.AddComponent<ArcadeStageAuthoring>();
                config.ProfileId = "slopball_v1"; config.Slopball = true; config.BallSpawn = marker.transform;
                ApplyShotProfile(config);
                EditorSceneManager.SaveScene(scene, SourcePath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        private static void CreateVisual(Mesh shell, Mesh playerFloor, Mesh senderFloor,
            Material wall, Material player, Material sender, Material line, Material tape)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Slopball Court");
            try
            {
                Surface(root.transform, "CourtShell", shell, wall);
                Surface(root.transform, "PlayerHalf", playerFloor, player);
                Surface(root.transform, "SenderHalf", senderFloor, sender);
                Line(root.transform, "Floor perimeter", line, new[] { new Vector3(-20, .025f, -20), new(-20, .025f, 20), new(20, .025f, 20), new(20, .025f, -20), new(-20, .025f, -20) }, .04f);
                Line(root.transform, "Net top", tape, new[] { new Vector3(-20, 1.1f, 0), new(20, 1.1f, 0) }, .075f);
                Line(root.transform, "Net foot", tape, new[] { new Vector3(-20, .02f, 0), new(20, .02f, 0) }, .03f);
                for (float x = -20; x <= 20; x += .5f) Line(root.transform, "Net strand", line, new[] { new Vector3(x, 0, 0), new(x, 1.1f, 0) }, .018f);
                for (float y = .25f; y < 1.1f; y += .25f) Line(root.transform, "Net row", line, new[] { new Vector3(-20, y, 0), new(20, y, 0) }, .018f);
                foreach (float x in new[] { -20f, 20f }) foreach (float z in new[] { -20f, 20f })
                    Line(root.transform, "Court corner", line, new[] { new Vector3(x, 0, z), new(x, 20, z) }, .025f);
                var launcher = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                launcher.name = "Ball Sender"; launcher.transform.SetParent(root.transform, false);
                launcher.transform.localPosition = new(0, 1.1f, 6); launcher.transform.localScale = new(.25f, 1.1f, .25f);
                launcher.GetComponent<MeshRenderer>().sharedMaterial = tape; Object.DestroyImmediate(launcher.GetComponent<Collider>());
                var light = Child(root.transform, "LocalLighting").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(40, -25, 0);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                SlopballClubPresentation.Finish();
            }
            finally { Object.DestroyImmediate(root); EditorSceneManager.CloseScene(scene, true); }
        }
        private static GameObject Child(Transform parent, string name)
        { var result = new GameObject(name); result.transform.SetParent(parent, false); return result; }
        private static void Surface(Transform parent, string name, Mesh mesh, Material material)
        { var item = Child(parent, name); item.AddComponent<MeshFilter>().sharedMesh = mesh; item.AddComponent<MeshRenderer>().sharedMaterial = material; }
        private static void Line(Transform parent, string name, Material material, Vector3[] points, float width)
        {
            var line = Child(parent, name).AddComponent<LineRenderer>(); line.useWorldSpace = false;
            line.sharedMaterial = material; line.startWidth = line.endWidth = width; line.positionCount = points.Length; line.SetPositions(points);
        }
        private static Mesh MeshAsset(string path, Mesh proposed)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { Object.DestroyImmediate(proposed); return existing; }
            AssetDatabase.CreateAsset(proposed, path); return proposed;
        }
        private static Material MaterialAsset(string name, Color color, bool unlit)
        {
            string path = DirectoryPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path); if (material != null) return material;
            material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color); AssetDatabase.CreateAsset(material, path); return material;
        }
        private static Mesh Plane(float minZ, float maxZ)
        { var vertices = new List<Vector3>(); var indices = new List<int>(); Quad(vertices, indices, new(-20, .012f, minZ), new(-20, .012f, maxZ), new(20, .012f, maxZ), new(20, .012f, minZ)); return Mesh(vertices, indices); }
        private static Mesh Room()
        {
            var v = new List<Vector3>(); var i = new List<int>();
            Quad(v, i, new(-20, 0, -20), new(-20, 0, 20), new(20, 0, 20), new(20, 0, -20));
            Quad(v, i, new(-20, 20, -20), new(20, 20, -20), new(20, 20, 20), new(-20, 20, 20));
            Quad(v, i, new(-20, 0, -20), new(-20, 20, -20), new(-20, 20, 20), new(-20, 0, 20));
            Quad(v, i, new(20, 0, -20), new(20, 0, 20), new(20, 20, 20), new(20, 20, -20));
            Quad(v, i, new(-20, 0, -20), new(20, 0, -20), new(20, 20, -20), new(-20, 20, -20));
            Quad(v, i, new(-20, 0, 20), new(-20, 20, 20), new(20, 20, 20), new(20, 0, 20));
            return Mesh(v, i);
        }
        private static Mesh Net()
        {
            var v = new List<Vector3>(); var i = new List<int>();
            Quad(v, i, new(-20, 0, -.05f), new(20, 0, -.05f), new(20, 1.1f, -.05f), new(-20, 1.1f, -.05f));
            Quad(v, i, new(-20, 0, .05f), new(-20, 1.1f, .05f), new(20, 1.1f, .05f), new(20, 0, .05f));
            Quad(v, i, new(-20, 1.1f, -.05f), new(20, 1.1f, -.05f), new(20, 1.1f, .05f), new(-20, 1.1f, .05f));
            return Mesh(v, i);
        }
        private static void Quad(List<Vector3> v, List<int> i, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        { int n = v.Count; v.AddRange(new[] { a, b, c, d }); i.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 }); }
        private static Mesh Mesh(List<Vector3> vertices, List<int> indices)
        { var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh; }
    }
}
#endif
