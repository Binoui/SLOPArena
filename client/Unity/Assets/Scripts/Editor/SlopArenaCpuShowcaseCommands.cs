#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SlopArena.Client.Camera;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.World;
using SlopArena.Shared.AI;
using Unity.Cinemachine;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>Bounded, in-memory capture commands layered on the existing stage-view lifecycle.</summary>
    public static class SlopArenaCpuShowcaseCommands
    {
        private const string SessionKey = "SlopArena.CpuShowcase.Active";

        [CliCommand("sloparena.showcase.prepare", "Prepare an in-memory four-character CPU filming session using the stage-view save/restore lifecycle.", MainThreadRequired = true, Tags = new[] { "capture", "showcase" })]
        public static object Prepare(
            [CliArg("stage", "Baked four-spawn PvP stage key.")] string stage = "after_hours",
            [CliArg("seed", "Repeatable CPU RNG seed.")] int seed = 7,
            [CliArg("difficulty", "Easy, Normal or Hard.")] string difficulty = "Hard",
            [CliArg("spawn-scale", "1 uses authored spawns; 0.25–1 gathers the cast towards the flat After Hours center for filming.")] float spawnScale = 1f,
            [CliArg("reveal", "Open the recording on the fight-night billboard and move down into the fight over four seconds.")] bool reveal = false,
            [CliArg("captions", "Preview the approved four white text beats, starting seven seconds into the recording.")] bool captions = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Showcase preparation requires stopped, compiled Edit Mode.");
            if (float.IsNaN(spawnScale) || spawnScale < .25f || spawnScale > 1f || spawnScale != 1f && stage != "after_hours")
                throw new ArgumentException("spawn-scale must be 0.25–1 and a compact start is supported only on After Hours.");
            if (!Enum.TryParse<CpuDifficulty>(difficulty, true, out var tier) || !Enum.IsDefined(typeof(CpuDifficulty), tier))
                throw new ArgumentException("Unknown CPU difficulty.");
            if (!string.IsNullOrEmpty(SessionState.GetString(SessionKey, ""))
                || !string.IsNullOrEmpty(SessionState.GetString("SlopArena.StageViews.Active", "")))
                throw new InvalidOperationException("A stage-view or showcase session still needs restoration.");
            var saved = new { fixedDeltaTime = Time.fixedDeltaTime, captureFramerate = Time.captureFramerate };
            SessionState.SetString(SessionKey, JsonConvert.SerializeObject(saved));
            SlopArenaStageViewCommands.Prepare(stage);
            var camera = Object.FindObjectsByType<CameraMount>(FindObjectsSortMode.None)
                .Select(mount => mount.RenderCamera).FirstOrDefault(value => value != null)
                ?? throw new InvalidOperationException("The baseline scene has no gameplay render camera.");
            var training = Object.FindFirstObjectByType<TrainingMatch>();
            training.enabled = false;
            foreach (var renderer in Object.FindObjectsByType<PlayerRenderer>(FindObjectsSortMode.None))
                Object.DestroyImmediate(renderer.gameObject);
            foreach (var input in Object.FindObjectsByType<InputController>(FindObjectsSortMode.None)) input.enabled = false;
            foreach (var mount in Object.FindObjectsByType<CameraMount>(FindObjectsSortMode.None)) mount.enabled = false;
            foreach (var brain in Object.FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None)) brain.enabled = false;
            foreach (var document in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) document.enabled = false;
            camera.tag = "MainCamera";
            var showcase = new GameObject("CPU Showcase (temporary)").AddComponent<SlopArenaCpuShowcase>()
                ?? throw new InvalidOperationException("Could not attach the Editor-only showcase component.");
            showcase.Stage = stage; showcase.Seed = seed; showcase.Difficulty = tier; showcase.FilmCamera = camera;
            showcase.SpawnScale = spawnScale;
            showcase.FilmReveal = reveal; showcase.FilmCaptions = captions;
            if (captions)
                showcase.FilmFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/ArchivoBlack-Regular.ttf");
            Time.fixedDeltaTime = SlopArena.Shared.Simulation.TickDt;
            return new { success = true, stage, seed, spawnScale, reveal, captions, difficulty = tier.ToString(), packages = SlopArenaCpuShowcase.Packages };
        }

        [CliCommand("sloparena.showcase.run", "Run a bounded number of real Shared ticks, then freeze the four CPU fighters.", MainThreadRequired = true, Tags = new[] { "capture", "showcase" })]
        public static async Task<object> Run([CliArg("ticks", "Number of 60 Hz simulation ticks (1–3600).")] int ticks = 180)
        {
            if (ticks < 1 || ticks > 3600) throw new ArgumentOutOfRangeException(nameof(ticks));
            var showcase = await Ready();
            showcase.Run((uint)ticks);
            double deadline = EditorApplication.timeSinceStartup + ticks / 10.0 + 15;
            while (!showcase.Frozen)
            {
                EnsureRunning(showcase, deadline);
                await Task.Delay(30);
            }
            return showcase.Inspect();
        }

        [CliCommand("sloparena.showcase.record", "Record a silent, frame-paced 30 fps gameplay MP4 using an already installed ffmpeg. Does not install tools.", MainThreadRequired = true, Tags = new[] { "capture", "showcase" })]
        public static async Task<object> Record(
            [CliArg("output", "Fresh repository-relative MP4 below tmp/.", Required = true)] string output,
            [CliArg("seconds", "Duration, 1–30 seconds.")] int seconds = 12)
        {
            if (seconds < 1 || seconds > 30) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (string.IsNullOrWhiteSpace(output) || !output.StartsWith("tmp/", StringComparison.Ordinal)
                || output.Contains('\\') || output.Contains('"') || !output.EndsWith(".mp4", StringComparison.Ordinal)
                || output.Split('/').Any(x => x == "" || x == "." || x == ".."))
                throw new ArgumentException("output must be a fresh repository-relative MP4 below tmp/.");
            var showcase = await Ready();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", output));
            var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Resources.FindObjectsOfTypeAll(viewType).OfType<EditorWindow>().First().Focus();
            showcase.Record(path, seconds);
            try
            {
                double deadline = EditorApplication.timeSinceStartup + seconds * 8 + 30;
                while (showcase.Recording)
                {
                    EnsureRunning(showcase, deadline);
                    await Task.Delay(30);
                }
                if (showcase.Failure != null) throw new InvalidOperationException(showcase.Failure);
                return new { success = true, output, seconds, fps = 30, audio = false, frames = showcase.RecordedFrames, state = showcase.Inspect() };
            }
            finally { showcase.FinishRecording(); }
        }

        [CliCommand("sloparena.showcase.stop", "Stop Play Mode only for this showcase's active capture session.", MainThreadRequired = true, Tags = new[] { "capture", "showcase" })]
        public static object Stop()
        {
            if (string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")))
                return new { success = true, started = false };
            return SlopArenaStageViewCommands.Stop();
        }

        [CliCommand("sloparena.showcase.restore", "Restore showcase timing and the scenes/view saved by stage-view prepare, after stopping Play Mode.", MainThreadRequired = true, Tags = new[] { "capture", "showcase" })]
        public static object Restore()
        {
            string saved = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(saved)) return new { success = true, restored = true, started = false };
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop and settle Play Mode before restoring a showcase.");
            var timing = JsonConvert.DeserializeObject<Timing>(saved);
            Time.fixedDeltaTime = timing.fixedDeltaTime; Time.captureFramerate = timing.captureFramerate;
            var result = SlopArenaStageViewCommands.Restore();
            SessionState.EraseString(SessionKey);
            return result;
        }

        private sealed class Timing { public float fixedDeltaTime; public int captureFramerate; }

        private static async Task<SlopArenaCpuShowcase> Ready()
        {
            if (string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")))
                throw new InvalidOperationException("Prepare a showcase first.");
            double deadline = EditorApplication.timeSinceStartup + 20;
            while (true)
            {
                var showcase = Object.FindFirstObjectByType<SlopArenaCpuShowcase>();
                if (showcase != null && showcase.Ready) return showcase;
                EnsureRunning(showcase, deadline);
                await Task.Delay(30);
            }
        }

        private static void EnsureRunning(SlopArenaCpuShowcase showcase, double deadline)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.timeSinceStartup > deadline
                || showcase != null && showcase.Failure != null)
                throw new InvalidOperationException(showcase?.Failure ?? "The showcase stopped, paused or timed out; inspect the console.");
        }
    }
}
#endif
