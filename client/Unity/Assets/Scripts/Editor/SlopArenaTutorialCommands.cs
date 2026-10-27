#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
    /// <summary>Tutorial scene preparation and native probes: open, status, lesson jump and scripted input.</summary>
    public static class SlopArenaTutorialCommands
    {
        public const string ScenePath = "Assets/Scenes/" + TutorialMatch.SceneName + ".unity";

        [MenuItem("Tools/SLOPArena/Tutorial/Prepare Tutorial Scene")]
        private static void PrepareMenu() => Prepare();

        [CliCommand("sloparena.tutorial.prepare", "Create the Tutorial match scene from the offline match scene and enable it in the build.",
            MainThreadRequired = true, Tags = new[] { "tutorial" })]
        public static object Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Prepare the tutorial in stopped Edit Mode with the current scene saved.");
            var original = SceneManager.GetActiveScene();
            bool created = !File.Exists(ScenePath);
            try
            {
                if (created) SlopArenaArcadePhase1Commands.CreateBallScene(ScenePath, typeof(TutorialMatch));
                var scenes = EditorBuildSettings.scenes.ToList();
                int index = scenes.FindIndex(x => x.path == ScenePath);
                if (index < 0) scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); else scenes[index].enabled = true;
                EditorBuildSettings.scenes = scenes.ToArray(); AssetDatabase.SaveAssets();
            }
            finally { if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original); }
            return new { success = true, scene = ScenePath, created, stage = TutorialMatch.Layout.ArenaKey };
        }

        private const string SessionKey = "SlopArena.Tutorial.Session";
        private sealed class Saved
        {
            public SceneSetup[] scenes;
            public bool optionsEnabled, gizmos;
            public int options, viewSize;
            public GameMode mode;
            public CharacterClass playerClass;
            public string playerPackageId, arenaName;
        }

        [CliCommand("sloparena.tutorial.open", "Save Editor state, disable domain reload (this Editor stalls entering Play Mode with one) and open the Tutorial scene for a fighter; then editor_play. Always finish with sloparena.tutorial.restore.",
            MainThreadRequired = true, Tags = new[] { "tutorial" })]
        public static object Open([CliArg("fighter", "Package id: fightguy, manki, wibou or bonk.")] string fighter = "fightguy")
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")))
                throw new InvalidOperationException("An earlier tutorial session still needs sloparena.tutorial.restore.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Open the tutorial in stopped, compiled Edit Mode.");
            var scenes = EditorSceneManager.GetSceneManagerSetup();
            if (scenes.Length == 0 || scenes.Any(x => string.IsNullOrEmpty(x.path))
                || Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save and name every open scene before opening the tutorial.");
            if (!File.Exists(ScenePath)) throw new InvalidOperationException("Run sloparena.tutorial.prepare first.");
            var selected = fighter switch
            {
                "fightguy" => CharacterClass.FightGuy, "manki" => CharacterClass.Manki,
                "wibou" => CharacterClass.Wibou, "bonk" => CharacterClass.Bonk, "bashie" => CharacterClass.Bashie,
                "salamander" => CharacterClass.Salamander,
                _ => throw new ArgumentException("Unknown roster fighter.", nameof(fighter)),
            };
            var view = GameView();
            // Save before the first mutation so restore also undoes a partial setup.
            SessionState.SetString(SessionKey, JsonConvert.SerializeObject(new Saved
            {
                scenes = scenes, optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled,
                viewSize = (int)view.GetType().GetProperty("selectedSizeIndex", Flags).GetValue(view),
                gizmos = (bool)view.GetType().GetProperty("drawGizmos", Flags).GetValue(view),
                options = (int)EditorSettings.enterPlayModeOptions, mode = MatchConfig.Mode,
                playerClass = MatchConfig.PlayerClass, playerPackageId = MatchConfig.PlayerPackageId, arenaName = MatchConfig.ArenaName,
            }));
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            MatchConfig.ClearActiveMatch();
            MatchConfig.Mode = GameMode.Tutorial;
            MatchConfig.PlayerClass = selected;
            MatchConfig.PlayerPackageId = fighter;
            MatchConfig.ArenaName = TutorialMatch.Layout.ArenaKey;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return new { success = true, scene = ScenePath, fighter };
        }

        [CliCommand("sloparena.tutorial.restore", "Restore the scenes, Play Mode options and match selection saved by sloparena.tutorial.open.",
            MainThreadRequired = true, Tags = new[] { "tutorial" })]
        public static object Restore()
        {
            string json = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(json)) return new { success = true, restored = false };
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before restoring.");
            var saved = JsonConvert.DeserializeObject<Saved>(json);
            EditorSettings.enterPlayModeOptionsEnabled = saved.optionsEnabled;
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)saved.options;
            MatchConfig.Mode = saved.mode; MatchConfig.PlayerClass = saved.playerClass;
            MatchConfig.PlayerPackageId = saved.playerPackageId; MatchConfig.ArenaName = saved.arenaName;
            EditorSceneManager.RestoreSceneManagerSetup(saved.scenes);
            var view = GameView(); var callback = view.GetType().GetMethod("SizeSelectionCallback", Flags);
            callback.Invoke(view, callback.GetParameters().Select(x => x.ParameterType == typeof(int) ? (object)saved.viewSize : null).ToArray());
            view.GetType().GetProperty("drawGizmos", Flags).SetValue(view, saved.gizmos); view.Repaint();
            SessionState.EraseString(SessionKey);
            return new { success = true, restored = true, scenes = saved.scenes.Select(x => x.path).ToArray(),
                saved.optionsEnabled, saved.options };
        }

        private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        private static EditorWindow GameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            return Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>().FirstOrDefault()
                ?? throw new InvalidOperationException("Open the Game View first.");
        }

        [CliCommand("sloparena.tutorial.camera", "Native probe: aim the live tutorial's gameplay camera (yaw 0 looks north, +Z; pitch 0..45) and let it settle.",
            MainThreadRequired = true, Tags = new[] { "tutorial", "capture" })]
        public static async Task<object> AimCamera([CliArg("yaw", "Camera yaw in degrees.", Required = true)] float yaw,
            [CliArg("pitch", "Camera pitch in degrees, 0..45.")] float pitch = 20f)
        {
            await ReadyMatch();
            var mount = Object.FindFirstObjectByType<SlopArena.Client.Camera.CameraMount>()
                ?? throw new InvalidOperationException("The tutorial has no camera mount.");
            mount.SetCameraYawDeg(yaw); mount.SetCameraPitchDeg(Mathf.Clamp(pitch, 0f, 45f));
            for (int frame = Time.frameCount + 12; Time.frameCount < frame && EditorApplication.isPlaying;) await Task.Delay(5);
            return new { success = true, yaw = mount.GetCameraYawDeg(), pitch = mount.GetCameraPitchDeg() };
        }

        private static async Task<TutorialMatch> ReadyMatch()
        {
            double deadline = EditorApplication.timeSinceStartup + 30;
            TutorialMatch match;
            while ((match = Object.FindFirstObjectByType<TutorialMatch>()) == null || !match.Ready)
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    throw new InvalidOperationException("No ready tutorial in Play Mode; inspect the console.");
                await Task.Delay(10);
            }
            return match;
        }

        [CliCommand("sloparena.tutorial.status", "Observe the live tutorial: lesson, objectives, player and dummy.",
            MainThreadRequired = true, Tags = new[] { "tutorial", "simulation" })]
        public static object Status()
        {
            var match = Object.FindFirstObjectByType<TutorialMatch>();
            var progress = match?.Progress; var sim = match?.Simulation;
            return new
            {
                success = true, scene = SceneManager.GetActiveScene().path, ready = match?.Ready ?? false,
                tick = match?.SimulationTick, finished = match?.Finished,
                lessonIndex = progress?.LessonIndex, lesson = progress?.Lesson.Id.ToString(),
                lessonComplete = progress?.LessonComplete,
                objectives = progress?.Lesson.Objectives.Select((o, i) => new
                    { id = o.Id.ToString(), o.Goal, amount = progress.Amount(i), complete = progress.IsComplete(i) }).ToArray(),
                player = sim == null ? null : Fighter(sim.GetState(MatchConfig.LocalEntityId)),
                dummy = sim == null ? null : Fighter(sim.GetState(TutorialMatch.DummyEntityId)),
                tutorialCompletedOnce = TutorialMatch.CompletedOnce,
            };
        }

        [CliCommand("sloparena.tutorial.lesson", "Native probe: restart the live tutorial at a lesson index.",
            MainThreadRequired = true, Tags = new[] { "tutorial", "simulation" })]
        public static async Task<object> Lesson([CliArg("index", "Zero-based lesson index.", Required = true)] int index,
            [CliArg("reset-player", "Also return the player to its spawn with no damage.")] bool resetPlayer = false)
        {
            var match = await ReadyMatch();
            match.JumpToLessonForProbe(index, resetPlayer);
            return Status();
        }

        [CliCommand("sloparena.tutorial.place", "Native probe: place the live tutorial's player on a surface (y is the surface height) facing a yaw in degrees.",
            MainThreadRequired = true, Tags = new[] { "tutorial", "simulation" })]
        public static async Task<object> Place([CliArg("x", "World X.", Required = true)] float x, [CliArg("z", "World Z.", Required = true)] float z,
            [CliArg("y", "Surface height under the player.")] float y = 0f, [CliArg("yaw", "Facing in degrees; 0 looks north (+Z).")] float yaw = 0f)
        {
            var match = await ReadyMatch();
            match.PlacePlayerForProbe(x, y, z, yaw * Mathf.Deg2Rad);
            return Status();
        }

        [CliCommand("sloparena.tutorial.run", "Apply bounded normal semantic inputs to the live tutorial and report hits and objective progress.",
            MainThreadRequired = true, Tags = new[] { "tutorial", "simulation" })]
        public static async Task<object> Run([CliArg("steps", "Training step JSON, at most 600 ticks.", Required = true)] string steps)
        {
            var match = await ReadyMatch();
            if (EditorApplication.isPaused || Time.timeScale <= 0 || match.Finished || match.ProbeInputActive)
                throw new InvalidOperationException("Run requires a ready, unpaused, unfinished tutorial without another probe.");
            var parsed = SlopArenaTrainingCommands.ParseSteps(steps);
            if (parsed.Sum(x => x.Ticks) > 600) throw new ArgumentException("Tutorial probes are limited to 600 ticks.");
            int index = 0, stepTicks = 0, totalTicks = 0;
            var hits = new List<object>(); var lessons = new List<string>();
            var completion = new TaskCompletionSource<bool>(); double deadline = EditorApplication.timeSinceStartup + 30;
            void Apply(bool first)
            {
                short yaw = (short)(match.Simulation.GetState(MatchConfig.LocalEntityId).FacingYaw * Mathf.Rad2Deg * 100);
                match.SetProbeInput(SlopArenaTrainingCommands.BuildInput(parsed[index], first, yaw));
            }
            void Tick(uint tick)
            {
                if (completion.Task.IsCompleted) return;
                foreach (var hit in match.Simulation.LastTickHits)
                    hits.Add(new { tick, hit.OwnerEntityId, hit.TargetEntityId, hit.AttackSlot, hit.Airborne, hit.Blocked, hit.Damage });
                string lesson = match.Progress.Lesson.Id + (match.Progress.LessonComplete ? " passed" : "");
                if (lessons.Count == 0 || lessons[lessons.Count - 1] != lesson) lessons.Add(lesson);
                totalTicks++; stepTicks++;
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
                    completion.TrySetException(new InvalidOperationException("Tutorial probe stopped or exceeded its wall deadline."));
            }
            match.ProbeTickCompleted += Tick; EditorApplication.update += Check;
            try
            {
                Apply(true);
                await completion.Task;
                return new { success = true, completedTicks = totalTicks, lessons, hits, state = Status() };
            }
            finally
            {
                EditorApplication.update -= Check;
                if (match != null) { match.ProbeTickCompleted -= Tick; match.SetProbeInput(null); }
            }
        }

        private static object Fighter(CharacterState s) => new
        {
            s.PX, s.PY, s.PZ, s.IsGrounded, state = s.State.ToString(), s.JumpsLeft, s.IsFastFalling,
            s.DamagePercent, s.Deaths, s.HitstunTicks, s.AttackSlot, s.TargetEntityId, s.LockOn,
        };
    }
}
#endif
