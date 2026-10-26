#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SlopArena.Client;
using SlopArena.Client.Camera;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Cinemachine;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Deterministic hit-feel review captures: the same duel is replayed once per strength preset
    /// and every tick is written from a close and a wide side camera. Layered on the stage-view
    /// save/restore lifecycle; nothing is saved to scenes or assets.
    /// </summary>
    public static class SlopArenaHitFeelReviewCommands
    {
        private const string SessionKey = "SlopArena.HitFeelReview.Active";
        private static readonly string[] ArtifactPrefixes = { "tmp/", ".impeccable/", ".ability-lab-cache/", "client/Unity/Temp/" };
        private static readonly string[] Layers = { "shake", "flash", "squash", "lean", "trail" };

        private sealed class Saved { public string stage = ""; public int captureFramerate; }

        [CliCommand("sloparena.hitfeel.prepare", "Prepare an in-memory two-fighter hit-feel review on a PvP stage using the stage-view save/restore lifecycle.", MainThreadRequired = true, Tags = new[] { "capture", "presentation" })]
        public static object Prepare([CliArg("stage", "Baked PvP stage key with a Resources presentation prefab.")] string stage = "after_hours")
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Hit-feel review preparation requires stopped, compiled Edit Mode.");
            if (!string.IsNullOrEmpty(SessionState.GetString(SessionKey, ""))
                || !string.IsNullOrEmpty(SessionState.GetString("SlopArena.StageViews.Active", ""))
                || !string.IsNullOrEmpty(SessionState.GetString("SlopArena.CpuShowcase.Active", "")))
                throw new InvalidOperationException("A stage-view, showcase or hit-feel session still needs restoration.");
            // Save before the first mutation so the declared restore also handles partial setup failure.
            SessionState.SetString(SessionKey, JsonConvert.SerializeObject(new Saved { stage = stage, captureFramerate = Time.captureFramerate }));
            SlopArenaStageViewCommands.Prepare(stage);
            var camera = Object.FindObjectsByType<CameraMount>(FindObjectsSortMode.None)
                .Select(mount => mount.RenderCamera).FirstOrDefault(value => value != null)
                ?? throw new InvalidOperationException("The baseline scene has no gameplay render camera.");
            // In memory only: Training never starts, so the review owns fighters, stage and camera.
            var training = Object.FindFirstObjectByType<TrainingMatch>()
                ?? throw new InvalidOperationException("The baseline scene has no TrainingMatch.");
            training.enabled = false;
            foreach (var renderer in Object.FindObjectsByType<PlayerRenderer>(FindObjectsSortMode.None))
                Object.DestroyImmediate(renderer.gameObject);
            foreach (var input in Object.FindObjectsByType<InputController>(FindObjectsSortMode.None)) input.enabled = false;
            foreach (var mount in Object.FindObjectsByType<CameraMount>(FindObjectsSortMode.None)) mount.enabled = false;
            foreach (var brain in Object.FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None)) brain.enabled = false;
            foreach (var document in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) document.enabled = false;
            camera.tag = "MainCamera";
            return new { success = true, stage, camera = camera.name };
        }

        [CliCommand("sloparena.hitfeel.capture", "Replay one duel per strength preset in Play Mode and write every tick from close and wide side cameras as JPGs.", MainThreadRequired = true, Tags = new[] { "capture", "presentation" })]
        public static async Task<object> Capture(
            [CliArg("output", "Fresh repository-relative directory below tmp/, .impeccable/, .ability-lab-cache/ or client/Unity/Temp/.", Required = true)] string output,
            [CliArg("attacker", "Roster package ID of the attacker.")] string attacker = "fightguy",
            [CliArg("victim", "Roster package ID of the idle victim.")] string victim = "manki",
            [CliArg("slot", "Canonical label: 1, 3, 4, A, E, R or F.")] string slot = "4",
            [CliArg("distance", "Metres between the fighters, 0.4–4.")] float distance = 1.2f,
            [CliArg("damage", "Victim starting percent, 0–300.")] int damage = 0,
            [CliArg("ticks", "Ticks captured after the press, 10–240.")] int ticks = 90,
            [CliArg("presets", "Comma list of name:scale, at most 5, e.g. off:0,low:0.6,mid:1,high:1.6.")] string presets = "off:0,low:0.6,mid:1,high:1.6",
            [CliArg("layers", "Layers the presets scale: all, or a comma list of shake, flash, squash, lean, trail. Others stay off.")] string layers = "all",
            [CliArg("width", "Frame width, 320–1920.")] int width = 960,
            [CliArg("height", "Frame height, 180–1080.")] int height = 540,
            [CliArg("track", "Let the wide camera follow both fighters each tick, for leaps and launches.")] bool track = false,
            [CliArg("hold", "Ticks to keep aiming after the press, 0–120; charges aim-hold specials such as Ki Shot.")] int hold = 0,
            [CliArg("orbit", "Degrees both cameras orbit around the fighters, -180–180: 0 is the side view, 90 looks from behind the attacker like the gameplay camera.")] float orbit = 0f,
            [CliArg("shield", "The victim holds its shield the whole duel, to capture a move's blocked path.")] bool shield = false,
            [CliArg("airborne", "Jump twelve ticks before pressing the move, using normal simulation input.")] bool airborne = false,
            [CliArg("victim-airborne", "The victim also jumps twelve ticks before the attack, for aerial contact review.")] bool victimAirborne = false)
        {
            var saved = Read();
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Enter Play Mode (editor_play) after sloparena.hitfeel.prepare.");
            if (distance < 0.4f || distance > 4f || damage < 0 || damage > 300 || ticks < 10 || ticks > 240 || hold < 0 || hold > 120
                || orbit < -180f || orbit > 180f
                || width < 320 || width > 1920 || height < 180 || height > 1080 || width % 2 != 0 || height % 2 != 0)
                throw new ArgumentException("distance, damage, ticks, hold, orbit or frame size is out of range (even frame sizes only).");
            byte slotWire = SlopArenaTrainingCommands.MapSlot(slot);
            var plan = ParsePresets(presets);
            var selected = ParseLayers(layers);
            output = (output ?? "").TrimEnd('/');
            if (Path.IsPathRooted(output) || output.Contains('\\') || output.Split('/').Any(x => x == "" || x == "." || x == "..")
                || !ArtifactPrefixes.Any(x => (output + "/").StartsWith(x, StringComparison.Ordinal)))
                throw new ArgumentException("output must be a repository-relative directory below " + string.Join(", ", ArtifactPrefixes) + ".");
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", output));
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new ArgumentException("output must be a fresh directory; earlier evidence is never overwritten.");
            string arenaPath = BakedContentPaths.ResolveArena(saved.stage);
            var loaded = arenaPath == null ? null : ArenaBinaryFormat.LoadFromFile(arenaPath);
            if (loaded is not ArenaDefinition arena || arena.Purpose != ArenaPurpose.PvP)
                throw new InvalidOperationException("The prepared stage must resolve to a baked PvP arena.");

            // Fresh cameras copy the gameplay camera's look; its own rig keeps driving the original.
            var gameplay = Object.FindObjectsByType<CameraMount>(FindObjectsSortMode.None)
                .Select(mount => mount.RenderCamera).FirstOrDefault(value => value != null)
                ?? throw new InvalidOperationException("The review scene has no render camera.");
            var close = ReviewCamera("Hit Feel Close Camera (temporary)", gameplay);
            var wide = ReviewCamera("Hit Feel Wide Camera (temporary)", gameplay);
            var closeTarget = new RenderTexture(width, height, 24) { antiAliasing = 4, name = "Hit Feel Close" };
            var wideTarget = new RenderTexture(width, height, 24) { antiAliasing = 4, name = "Hit Feel Wide" };
            bool gameplayEnabled = gameplay.enabled;
            string gameplayTag = gameplay.tag;
            int previousRate = Time.captureFramerate;
            int resumed = 0;
            var runs = new List<object>();
            try
            {
                // Camera-facing effects (hit sparks) orient to Camera.main.
                gameplay.enabled = false;
                gameplay.tag = "Untagged";
                close.tag = "MainCamera";
                close.targetTexture = closeTarget;
                wide.targetTexture = wideTarget;
                // End-of-frame captures need a repainting Game View, as for showcase recording.
                var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                Resources.FindObjectsOfTypeAll(viewType).OfType<EditorWindow>().FirstOrDefault()?.Focus();
                // One tick per rendered frame, independent of Editor frame pacing.
                Time.captureFramerate = 60;
                foreach (var (name, scale) in plan)
                {
                    var host = new GameObject("Hit Feel Review (temporary)");
                    host.SetActive(false);
                    var review = host.AddComponent<SlopArenaHitFeelReview>();
                    review.Arena = arena;
                    review.AttackerPackage = attacker;
                    review.VictimPackage = victim;
                    review.SlotWire = slotWire;
                    review.Distance = distance;
                    review.VictimDamage = (ushort)damage;
                    review.CaptureTicks = ticks;
                    review.TrackWide = track;
                    review.HoldTicks = hold;
                    review.CameraOrbitDegrees = orbit;
                    review.VictimShield = shield;
                    review.Airborne = airborne;
                    review.VictimAirborne = victimAirborne;
                    review.Strength = Scaled(selected, scale);
                    review.CloseCamera = close;
                    review.WideCamera = wide;
                    review.CloseTarget = closeTarget;
                    review.WideTarget = wideTarget;
                    review.OutputDirectory = Path.Combine(directory, name);
                    host.SetActive(true);
                    double deadline = EditorApplication.timeSinceStartup + 60;
                    while (!review.Done && review.Failure == null)
                    {
                        if (EditorApplication.isPaused)
                        {
                            EditorApplication.isPaused = false;
                            resumed++;
                        }
                        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                            throw new InvalidOperationException($"Preset '{name}' stopped or timed out; inspect the console.");
                        await Task.Delay(30);
                    }
                    runs.Add(new { preset = name, scale, result = review.Inspect() });
                    string? failure = review.Failure;
                    Object.Destroy(host);
                    await Task.Delay(100);
                    if (failure != null)
                        throw new InvalidOperationException($"Preset '{name}' failed: {failure}");
                }
            }
            finally
            {
                Time.captureFramerate = previousRate;
                if (gameplay != null)
                {
                    gameplay.enabled = gameplayEnabled;
                    gameplay.tag = gameplayTag;
                }
                Object.Destroy(close.gameObject);
                Object.Destroy(wide.gameObject);
                closeTarget.Release();
                wideTarget.Release();
                Object.Destroy(closeTarget);
                Object.Destroy(wideTarget);
            }
            return new
            {
                success = true, stage = saved.stage, output, attacker, victim, slot, distance, damage, ticks, track, hold, shield, airborne, victimAirborne,
                layers = selected, editorPausesResumed = resumed, runs
            };
        }

        [CliCommand("sloparena.hitfeel.stop", "Stop Play Mode only for an active hit-feel review session.", MainThreadRequired = true, Tags = new[] { "capture", "presentation" })]
        public static object Stop()
        {
            if (string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")))
                return new { success = true, started = false };
            return SlopArenaStageViewCommands.Stop();
        }

        [CliCommand("sloparena.hitfeel.restore", "Restore capture timing and the scenes/view saved by prepare, after stopping Play Mode.", MainThreadRequired = true, Tags = new[] { "capture", "presentation" })]
        public static object Restore()
        {
            string text = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(text)) return new { success = true, restored = true, started = false };
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop and settle Play Mode before restoring a hit-feel review.");
            var saved = JsonConvert.DeserializeObject<Saved>(text) ?? new Saved();
            Time.captureFramerate = saved.captureFramerate;
            var result = SlopArenaStageViewCommands.Restore();
            SessionState.EraseString(SessionKey);
            return result;
        }

        private static UnityEngine.Camera ReviewCamera(string name, UnityEngine.Camera source)
        {
            var camera = new GameObject(name).AddComponent<UnityEngine.Camera>();
            camera.CopyFrom(source);
            var data = camera.GetUniversalAdditionalCameraData();
            var sourceData = source.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = sourceData.renderPostProcessing;
            data.antialiasing = sourceData.antialiasing;
            camera.enabled = true;
            return camera;
        }

        private static Saved Read() => JsonConvert.DeserializeObject<Saved>(SessionState.GetString(SessionKey, ""))
            ?? throw new InvalidOperationException("No hit-feel session; run sloparena.hitfeel.prepare first.");

        private static List<(string name, float scale)> ParsePresets(string text)
        {
            var plan = new List<(string, float)>();
            foreach (string item in (text ?? "").Split(','))
            {
                var parts = item.Trim().Split(':');
                if (parts.Length != 2 || !Regex.IsMatch(parts[0], "^[a-z0-9-]{1,24}$")
                    || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale)
                    || scale < 0f || scale > 3f)
                    throw new ArgumentException($"Preset '{item}' must be name:scale with a lowercase name and scale 0–3.");
                plan.Add((parts[0], scale));
            }
            if (plan.Count == 0 || plan.Count > 5 || plan.Select(x => x.Item1).Distinct().Count() != plan.Count)
                throw new ArgumentException("Request one to five uniquely named presets.");
            return plan;
        }

        private static string[] ParseLayers(string text)
        {
            string value = string.IsNullOrWhiteSpace(text) ? "all" : text.Trim().ToLowerInvariant();
            if (value == "all") return Layers;
            var layers = value.Split(',').Select(x => x.Trim()).Distinct().ToArray();
            if (layers.Length == 0 || layers.Any(x => !Layers.Contains(x)))
                throw new ArgumentException("layers must be all or a comma list of " + string.Join(", ", Layers) + ".");
            return layers;
        }

        private static HitFeelStrength Scaled(string[] layers, float scale) => new()
        {
            Shake = layers.Contains("shake") ? scale : 0f,
            Flash = layers.Contains("flash") ? scale : 0f,
            Squash = layers.Contains("squash") ? scale : 0f,
            Lean = layers.Contains("lean") ? scale : 0f,
            Trail = layers.Contains("trail") ? scale : 0f,
        };
    }
}
#endif
