#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SlopArena.Client;
using SlopArena.Client.Camera;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Gameplay-camera views of a PvP stage for art review, through the Training-based
    /// visual baseline scene. Editor lifecycle only, mirroring the course replay commands:
    /// prepare saves Editor state and disables domain reload (this Editor stalls entering
    /// Play Mode with a reload), capture orbits the normal camera and captures the composited
    /// Game View, and stop/restore return the Editor to its saved state.
    /// </summary>
    public static class SlopArenaStageViewCommands
    {
        private const string SessionKey = "SlopArena.StageViews.Active";
        private const string ScenePath = "Assets/Scenes/VisualBaselineCapture.unity";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] ArtifactPrefixes = { "tmp/", ".impeccable/", ".ability-lab-cache/", "client/Unity/Temp/" };
        // Eight-way orbit around the Training fighter at Spawn_01, plus two high views.
        private const string DefaultViews = "north:0:15,northeast:45:20,east:90:15,southeast:135:20,south:180:15,"
            + "southwest:225:20,west:270:15,northwest:315:20,high-northeast:45:30,high-southwest:225:30";

        private sealed class Saved
        {
            public string stage;
            public SceneSetup[] scenes;
            public string[] selection;
            public bool optionsEnabled, paused, gizmos;
            public int options, viewSize;
            public float timeScale;
            public GameMode mode;
        }

        [CliCommand("sloparena.stage.views.prepare", "Save Editor state, disable domain reload and open the Training-based baseline scene on a PvP stage.", MainThreadRequired = true, Tags = new[] { "stage", "capture" })]
        public static object Prepare(
            [CliArg("stage", "Baked PvP stage key with a Resources presentation prefab.", Required = true)] string stage,
            [CliArg("lighting", "scene (default) keeps the baseline scene's key/fill/rim lights, as in Training; pvp disables them in memory, as Arena_PvP has no lights of its own and only the stage's lights and atmosphere remain.")] string lighting = "scene")
        {
            if (!Regex.IsMatch(stage ?? "", "^[a-z][a-z0-9_]{0,99}$")) throw new ArgumentException("Invalid stage key.");
            lighting = string.IsNullOrWhiteSpace(lighting) ? "scene" : lighting.Trim().ToLowerInvariant();
            if (lighting != "scene" && lighting != "pvp") throw new ArgumentException("lighting must be scene or pvp.");
            if (!string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")))
                throw new InvalidOperationException("An earlier stage-views session still needs sloparena.stage.views.restore.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stage views require stopped, compiled Edit Mode.");
            var scenes = EditorSceneManager.GetSceneManagerSetup();
            if (scenes.Length == 0 || scenes.Any(x => string.IsNullOrEmpty(x.path))
                || Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Any(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save and name every open scene before capturing stage views.");
            string arenaPath = BakedContentPaths.ResolveArena(stage);
            var arena = arenaPath == null ? (ArenaDefinition?)null : ArenaBinaryFormat.LoadFromFile(arenaPath);
            if (!arena.HasValue || arena.Value.Purpose != ArenaPurpose.PvP)
                throw new InvalidOperationException("stage must resolve to a baked PvP arena; use the course replay runner for Arcade courses.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Stages/" + stage + ".prefab") == null
                || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new InvalidOperationException("The stage presentation prefab and the visual baseline scene must already exist.");
            var view = GameView();
            var saved = new Saved
            {
                stage = stage, scenes = scenes, selection = Selection.objects.Select(x => GlobalObjectId.GetGlobalObjectIdSlow(x).ToString()).ToArray(),
                optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled, options = (int)EditorSettings.enterPlayModeOptions,
                paused = EditorApplication.isPaused, timeScale = Time.timeScale, mode = MatchConfig.Mode,
                viewSize = (int)view.GetType().GetProperty("selectedSizeIndex", Flags).GetValue(view),
                gizmos = (bool)view.GetType().GetProperty("drawGizmos", Flags).GetValue(view)
            };
            // Save before the first mutation so the declared restore also handles partial setup failure.
            SessionState.SetString(SessionKey, JsonConvert.SerializeObject(saved));
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.isPaused = false; Time.timeScale = 1;
            MatchConfig.Mode = GameMode.Training;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // In-memory only: restore reopens the saved scenes without saving this one.
            var match = Object.FindFirstObjectByType<TrainingMatch>() ?? throw new InvalidOperationException("The baseline scene has no TrainingMatch.");
            var matchObject = new SerializedObject(match);
            matchObject.FindProperty("_arenaNameOverride").stringValue = stage;
            matchObject.ApplyModifiedPropertiesWithoutUndo();
            var controller = Object.FindFirstObjectByType<VisualBaselineCaptureController>();
            if (controller != null)
            {
                var controllerObject = new SerializedObject(controller);
                controllerObject.FindProperty("_runOnStart").boolValue = false;
                controllerObject.ApplyModifiedPropertiesWithoutUndo();
            }
            // Also in memory only. The stage prefab's own lights are spawned in Play Mode and stay on.
            var sceneLights = new List<string>();
            if (lighting == "pvp")
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (light.enabled)
                    {
                        light.enabled = false;
                        sceneLights.Add(light.name);
                    }
            return new { success = true, stage, scene = ScenePath, spawns = arena.Value.SpawnPoints.Length, lighting, disabledSceneLights = sceneLights };
        }

        [CliCommand("sloparena.stage.views.capture", "Orbit the normal gameplay camera around the Training fighter and capture fresh composited Game View PNGs.", MainThreadRequired = true, Tags = new[] { "stage", "capture" })]
        public static async Task<object> Capture(
            [CliArg("output", "Fresh repository-relative directory below tmp/, .impeccable/, .ability-lab-cache/ or client/Unity/Temp/.", Required = true)] string output,
            [CliArg("views", "Comma list of name:yaw:pitch in degrees (yaw 0 looks north, +Z). Defaults to an eight-way orbit plus two high views.")] string views = null)
        {
            var saved = Read();
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode (editor_play) after sloparena.stage.views.prepare.");
            var plan = ParseViews(string.IsNullOrWhiteSpace(views) ? DefaultViews : views);
            output = (output ?? "").TrimEnd('/');
            if (Path.IsPathRooted(output) || output.Contains('\\') || output.Split('/').Any(x => x == "" || x == "." || x == "..")
                || !ArtifactPrefixes.Any(x => (output + "/").StartsWith(x, StringComparison.Ordinal)))
                throw new ArgumentException("output must be a repository-relative directory below " + string.Join(", ", ArtifactPrefixes) + ".");
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", output));
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new ArgumentException("output must be a fresh directory; earlier evidence is never overwritten.");
            // An unrelated Editor error with Error Pause enabled pauses this session's Play Mode;
            // resume it and report how often, so the batch's console check shows the cause.
            int resumed = 0;
            void Resume()
            {
                if (!EditorApplication.isPaused) return;
                EditorApplication.isPaused = false;
                resumed++;
            }
            double deadline = EditorApplication.timeSinceStartup + 20;
            TrainingMatch match;
            while ((match = Object.FindFirstObjectByType<TrainingMatch>()) == null || !match.CaptureSequenceReady)
            {
                Resume();
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    throw new InvalidOperationException("Training did not become ready; inspect the console (is the baked arena valid?).");
                await Task.Delay(50);
            }
            var mount = Object.FindFirstObjectByType<CameraMount>() ?? throw new InvalidOperationException("The gameplay camera is unavailable.");
            var camera = mount.RenderCamera ?? throw new InvalidOperationException("The gameplay camera has no render camera.");
            var captures = new List<object>();
            // This session owns the orbit while rendering. Human input and UI camera
            // freezes must not overwrite a requested pose between Settle and capture.
            bool inputEnabled = mount.enabled;
            mount.enabled = false;
            try
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    mount.SetCameraYawDeg(plan[i].yaw);
                    mount.SetCameraPitchDeg(plan[i].pitch);
                    SlopArenaCaptureResult capture = null;
                    for (int attempt = 0; capture == null; attempt++)
                    {
                        Resume();
                        await Settle(camera.transform);
                        Resume();
                        try { capture = await SlopArenaCaptureCommands.Capture($"{output}/{i + 1:00}-{plan[i].name}.png", "screen"); }
                        catch (InvalidOperationException) when (attempt == 0 && EditorApplication.isPlaying && EditorApplication.isPaused) { }
                    }
                    float yaw = mount.GetCameraYawDeg(), pitch = mount.GetCameraPitchDeg();
                    if (Mathf.Abs(Mathf.DeltaAngle(yaw, plan[i].yaw)) > 0.1f || Mathf.Abs(pitch - plan[i].pitch) > 0.1f)
                        throw new InvalidOperationException($"View '{plan[i].name}' drifted from its requested camera pose; discard this capture pass.");
                    captures.Add(new { view = plan[i].name, yaw, pitch, capture });
                }
            }
            finally { if (mount != null) mount.enabled = inputEnabled; }
            return new { success = true, stage = saved.stage, output, editorPausesResumed = resumed, captures };
        }

        [CliCommand("sloparena.stage.views.stop", "Request stopping Play Mode only for an active stage-views session; the gateway settles the transition before restore.", MainThreadRequired = true, Tags = new[] { "stage", "capture" })]
        public static object Stop()
        {
            if (string.IsNullOrEmpty(SessionState.GetString(SessionKey, ""))) return new { success = true, started = false };
            EditorApplication.isPlaying = false;
            return new { success = true, stopRequested = true };
        }

        [CliCommand("sloparena.stage.views.restore", "Restore the scenes, selection, Play Mode options, match mode and Game View saved by prepare.", MainThreadRequired = true, Tags = new[] { "stage", "capture" })]
        public static object Restore()
        {
            if (string.IsNullOrEmpty(SessionState.GetString(SessionKey, ""))) return new { success = true, restored = true, started = false };
            var saved = Read();
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
                throw new InvalidOperationException("Stop the stage views and settle Play Mode before restoring.");
            EditorSettings.enterPlayModeOptionsEnabled = saved.optionsEnabled; EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)saved.options;
            MatchConfig.Mode = saved.mode; Time.timeScale = saved.timeScale; EditorApplication.isPaused = saved.paused;
            EditorSceneManager.RestoreSceneManagerSetup(saved.scenes);
            Selection.objects = saved.selection.Select(x => GlobalObjectId.TryParse(x, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null).Where(x => x != null).ToArray();
            var view = GameView(); var callback = view.GetType().GetMethod("SizeSelectionCallback", Flags);
            callback.Invoke(view, callback.GetParameters().Select(x => x.ParameterType == typeof(int) ? (object)saved.viewSize : null).ToArray());
            view.GetType().GetProperty("drawGizmos", Flags).SetValue(view, saved.gizmos); view.Repaint();
            SessionState.EraseString(SessionKey);
            return new { success = true, restored = true, scenes = saved.scenes.Select(x => x.path).ToArray(), optionsEnabled = saved.optionsEnabled, options = saved.options, viewSize = saved.viewSize, gizmos = saved.gizmos };
        }

        private static List<(string name, float yaw, float pitch)> ParseViews(string text)
        {
            var plan = new List<(string, float, float)>();
            foreach (string item in text.Split(','))
            {
                var parts = item.Trim().Split(':');
                if (parts.Length != 3 || !Regex.IsMatch(parts[0], "^[a-z0-9-]{1,40}$")
                    || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float yaw)
                    || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pitch)
                    || yaw < -360 || yaw > 360 || pitch < 0 || pitch > 45)
                    throw new ArgumentException($"View '{item}' must be name:yaw:pitch with a lowercase name, yaw -360..360 and pitch 0..45.");
                plan.Add((parts[0], yaw, pitch));
            }
            // Bounded so one command stays well inside a 120 s CLI timeout; split longer passes.
            if (plan.Count > 12 || plan.Select(x => x.Item1).Distinct().Count() != plan.Count)
                throw new ArgumentException("Request at most 12 uniquely named views per capture command.");
            return plan;
        }

        // The orbit applies on the next camera update; wait until the camera holds still (bounded).
        private static async Task Settle(Transform camera)
        {
            Vector3 position = camera.position; Quaternion rotation = camera.rotation;
            double deadline = EditorApplication.timeSinceStartup + 1.0;
            for (int still = 0; still < 4 && EditorApplication.timeSinceStartup < deadline;)
            {
                await Task.Delay(30);
                bool moved = (camera.position - position).sqrMagnitude > 1e-6f || Quaternion.Angle(camera.rotation, rotation) > 0.01f;
                still = moved ? 0 : still + 1;
                position = camera.position; rotation = camera.rotation;
            }
        }

        private static Saved Read() => JsonConvert.DeserializeObject<Saved>(SessionState.GetString(SessionKey, ""))
            ?? throw new InvalidOperationException("No stage-views session; run sloparena.stage.views.prepare first.");

        private static EditorWindow GameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            return Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>().FirstOrDefault() ?? throw new InvalidOperationException("Open a Game View before capturing stage views.");
        }
    }
}
#endif
