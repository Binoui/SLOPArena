#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    /// <summary>Editor lifecycle only; normal Shared course inputs and capture own the actual replay.</summary>
    public static class SlopArenaReplayCommands
    {
        private const string SessionKey = "SlopArena.Replay.Active";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private sealed class Saved
        {
            public string session, stage, fighter, output, chunksHash, stageHash, cookedHash, packageHash;
            public SceneSetup[] scenes;
            public string[] selection;
            public bool optionsEnabled, paused, freezeOnStart, tips, gizmos;
            public int options, viewSize;
            public float timeScale;
            public string recordDirectory, arenaName, playerPackageId;
            public GameMode mode;
            public CharacterClass playerClass;
            public int nextChunk;
        }

        [CliCommand("sloparena.replay.prepare", "Validate a course replay, save Editor state, isolate records and open its local course scene.", MainThreadRequired = true, Tags = new[] { "arcade", "replay" })]
        public static object Prepare(
            [CliArg("session", "Runner session ID.", Required = true)] string session,
            [CliArg("stage", "Baked Character Course key.", Required = true)] string stage,
            [CliArg("fighter", "Admitted package ID.", Required = true)] string fighter,
            [CliArg("output", "Fresh runner artifact directory containing chunks.json.", Required = true)] string output)
        {
            if (!Guid.TryParseExact(session, "N", out _) || !Regex.IsMatch(stage ?? "", "^[a-z][a-z0-9_]{0,99}$"))
                throw new ArgumentException("Invalid replay session or stage key.");
            if (!string.IsNullOrEmpty(SessionState.GetString(SessionKey, ""))) throw new InvalidOperationException("An earlier replay still needs restoration.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Replay requires stopped, compiled Edit Mode.");
            var scenes = EditorSceneManager.GetSceneManagerSetup();
            if (scenes.Length == 0 || scenes.Any(x => string.IsNullOrEmpty(x.path))
                || Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Any(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save and name every open scene before replaying.");
            var directory = ArtifactDirectory(output);
            string chunksText = File.ReadAllText(Path.Combine(directory, "chunks.json"));
            var chunks = JArray.Parse(chunksText);
            if (chunks.Count == 0 || chunks.Count > 128) throw new ArgumentException("Replay needs 1–128 bounded chunks.");
            int total = 0;
            foreach (var chunk in chunks) total += SlopArenaTrainingCommands.ParseSteps(chunk.ToString(Formatting.None)).Sum(x => x.Ticks);
            if (total > 36000) throw new ArgumentException("Replay exceeds 36000 ticks.");
            string arenaPath = BakedContentPaths.ResolveArena(stage);
            var arena = arenaPath == null ? (ArenaDefinition?)null : ArenaBinaryFormat.LoadFromFile(arenaPath);
            if (!arena.HasValue || arena.Value.Arcade?.Course == null || arena.Value.SpawnPoints.Length != 1)
                throw new InvalidOperationException("stage must resolve to a baked solo Character Course.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Stages/" + stage + ".prefab") == null
                || AssetDatabase.LoadAssetAtPath<SceneAsset>(SlopArenaCharacterCourseCommands.ScenePath) == null)
                throw new InvalidOperationException("The course scene and stage presentation must already exist.");
            if (!ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var failure) || catalog == null)
                throw new InvalidOperationException("Local content catalog: " + failure);
            var entry = catalog.ResolvePackage(fighter);
            if (entry == null || !arena.Value.Arcade.AllowsPackage(fighter)) throw new ArgumentException("The course does not admit package " + fighter);
            var view = GameView();
            var saved = new Saved
            {
                session = session, stage = stage, fighter = fighter, output = directory, chunksHash = Hash(System.Text.Encoding.UTF8.GetBytes(chunksText)),
                stageHash = Hash(ArenaBinaryFormat.Serialize(arena.Value)), cookedHash = entry.Identity.CookedContentHash, packageHash = entry.Identity.PackageHash,
                scenes = scenes, selection = Selection.objects.Select(x => GlobalObjectId.GetGlobalObjectIdSlow(x).ToString()).ToArray(),
                optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled, options = (int)EditorSettings.enterPlayModeOptions,
                paused = EditorApplication.isPaused, timeScale = Time.timeScale,
                freezeOnStart = CharacterCourseMatch.FreezeOnStartForProbe, tips = CharacterCourseMatch.MovementTipsEnabled,
                recordDirectory = ArcadeRecordsStore.VerificationDirectory, arenaName = MatchConfig.ArenaName,
                playerPackageId = MatchConfig.PlayerPackageId, playerClass = MatchConfig.PlayerClass, mode = MatchConfig.Mode,
                viewSize = (int)view.GetType().GetProperty("selectedSizeIndex", Flags).GetValue(view),
                gizmos = (bool)view.GetType().GetProperty("drawGizmos", Flags).GetValue(view)
            };
            // Save before the first mutation so the declared restore also handles partial setup failure.
            SessionState.SetString(SessionKey, JsonConvert.SerializeObject(saved));
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.isPaused = false; Time.timeScale = 1;
            ArcadeRecordsStore.VerificationDirectory = Path.Combine(directory, "records");
            CharacterCourseMatch.FreezeOnStartForProbe = true; CharacterCourseMatch.MovementTipsEnabled = false;
            MatchConfig.Mode = GameMode.Arcade; MatchConfig.ArenaName = stage; MatchConfig.PlayerPackageId = fighter;
            if (entry.LegacySelector.HasValue) MatchConfig.PlayerClass = entry.LegacySelector.Value;
            EditorSceneManager.OpenScene(SlopArenaCharacterCourseCommands.ScenePath, OpenSceneMode.Single);
            return new { success = true, stage, fighter, requestedTicks = total, recordsDirectory = ArcadeRecordsStore.VerificationDirectory };
        }

        [CliCommand("sloparena.replay.run", "Replay the next validated input chunk through the normal course input driver, freezing at its boundary.", MainThreadRequired = true, Tags = new[] { "arcade", "replay" })]
        public static async Task<object> Run(
            [CliArg("session", "Runner session ID.", Required = true)] string session,
            [CliArg("chunk", "Zero-based next input chunk.", Required = true)] int chunk,
            [CliArg("from-go", "Wait through countdown on the first chunk.")] bool fromGo = false)
        {
            var saved = Read(session);
            if (chunk != saved.nextChunk || fromGo != (chunk == 0)) throw new ArgumentException("Replay chunks must run once in order, with from-go on the first only.");
            double deadline = EditorApplication.timeSinceStartup + 15;
            CharacterCourseMatch match;
            while ((match = Object.FindFirstObjectByType<CharacterCourseMatch>()) == null || !match.Ready)
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new InvalidOperationException("Course did not become ready; inspect the console.");
                await Task.Delay(50);
            }
            if (MatchConfig.ArenaName != saved.stage || MatchConfig.PlayerPackageId != saved.fighter
                || match.Provenance.StageHash != saved.stageHash || match.Provenance.CookedContentHash != saved.cookedHash || match.Provenance.PackageHash != saved.packageHash)
                throw new InvalidOperationException("Live course/content does not match the validated replay.");
            string chunksText = File.ReadAllText(Path.Combine(saved.output, "chunks.json"));
            if (Hash(System.Text.Encoding.UTF8.GetBytes(chunksText)) != saved.chunksHash) throw new InvalidOperationException("Replay inputs changed after validation.");
            var chunks = JArray.Parse(chunksText);
            if (chunk < 0 || chunk >= chunks.Count) throw new ArgumentOutOfRangeException(nameof(chunk));
            object result = match.Simulation.Course.Value.Ended
                ? new { success = true, completedTicks = 0, ended = true, state = SlopArenaCharacterCourseCommands.Status() }
                : await SlopArenaCharacterCourseCommands.Run(chunks[chunk].ToString(Formatting.None), fromGo, true);
            saved.nextChunk++; SessionState.SetString(SessionKey, JsonConvert.SerializeObject(saved));
            return new { success = true, chunk, result, state = SlopArenaCharacterCourseCommands.Status(), provenance = match.Provenance };
        }

        [CliCommand("sloparena.replay.capture", "Verify a frozen course tick, optionally frame its camera and capture a fresh composited Game View.", MainThreadRequired = true, Tags = new[] { "arcade", "replay", "capture" })]
        public static async Task<object> Capture(
            [CliArg("session", "Runner session ID.", Required = true)] string session,
            [CliArg("tick", "Elapsed course ticks after Go.", Required = true)] int tick,
            [CliArg("output", "Fresh PNG below the runner output directory.", Required = true)] string output,
            [CliArg("yaw", "Optional camera yaw in degrees.")] float? yaw = null,
            [CliArg("pitch", "Optional camera pitch in degrees (0–45).")] float? pitch = null)
        {
            var saved = Read(session); var match = Object.FindFirstObjectByType<CharacterCourseMatch>();
            if (match == null || !match.Ready || !match.ProbeFrozen || match.Simulation.Course.Value.ElapsedTicks != (ulong)tick)
                throw new InvalidOperationException("Capture tick was not reached exactly at a frozen replay boundary (the course may have ended earlier).");
            string fullOutput = Path.GetFullPath(Path.Combine(RepositoryRoot(), output));
            if (!fullOutput.StartsWith(saved.output + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException("Capture must stay inside this replay's artifact directory.");
            if (yaw.HasValue != pitch.HasValue || (yaw.HasValue && (float.IsNaN(yaw.Value) || float.IsInfinity(yaw.Value) || yaw < -360 || yaw > 360 || float.IsNaN(pitch.Value) || pitch < 0 || pitch > 45)))
                throw new ArgumentException("Camera needs finite yaw -360..360 and pitch 0..45 together.");
            if (yaw.HasValue)
            {
                var camera = Object.FindFirstObjectByType<CameraMount>();
                if (camera == null) throw new InvalidOperationException("Course camera is unavailable.");
                camera.SetCameraYawDeg(yaw.Value); camera.SetCameraPitchDeg(pitch.Value);
            }
            var capture = await SlopArenaCaptureCommands.Capture(output, "screen");
            if (match.Simulation.Course.Value.ElapsedTicks != (ulong)tick) throw new InvalidOperationException("Simulation drifted while capturing.");
            return new { success = true, tick, capture };
        }

        [CliCommand("sloparena.replay.stop", "Request stopping only this replay; the gateway settles the Play Mode transition before restoration.", MainThreadRequired = true, Tags = new[] { "arcade", "replay" })]
        public static object Stop([CliArg("session", "Runner session ID.", Required = true)] string session)
        {
            if (string.IsNullOrEmpty(SessionState.GetString(SessionKey, ""))) return new { success = true, started = false };
            Read(session);
            EditorApplication.isPlaying = false;
            return new { success = true, stopRequested = true };
        }

        [CliCommand("sloparena.replay.restore", "Restore this stopped replay's saved scenes, selection, Editor options, viewport and records configuration.", MainThreadRequired = true, Tags = new[] { "arcade", "replay" })]
        public static object Restore([CliArg("session", "Runner session ID.", Required = true)] string session)
        {
            if (string.IsNullOrEmpty(SessionState.GetString(SessionKey, ""))) return new { success = true, restored = true, started = false };
            var saved = Read(session);
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
                throw new InvalidOperationException("Stop this replay and settle Play Mode before restoring its scenes.");
            EditorSettings.enterPlayModeOptionsEnabled = saved.optionsEnabled; EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)saved.options;
            ArcadeRecordsStore.VerificationDirectory = saved.recordDirectory;
            CharacterCourseMatch.FreezeOnStartForProbe = saved.freezeOnStart; CharacterCourseMatch.MovementTipsEnabled = saved.tips;
            MatchConfig.Mode = saved.mode; MatchConfig.ArenaName = saved.arenaName; MatchConfig.PlayerPackageId = saved.playerPackageId; MatchConfig.PlayerClass = saved.playerClass;
            Time.timeScale = saved.timeScale; EditorApplication.isPaused = saved.paused;
            EditorSceneManager.RestoreSceneManagerSetup(saved.scenes);
            Selection.objects = saved.selection.Select(x => GlobalObjectId.TryParse(x, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null).Where(x => x != null).ToArray();
            var view = GameView(); var callback = view.GetType().GetMethod("SizeSelectionCallback", Flags);
            callback.Invoke(view, callback.GetParameters().Select(x => x.ParameterType == typeof(int) ? (object)saved.viewSize : null).ToArray());
            view.GetType().GetProperty("drawGizmos", Flags).SetValue(view, saved.gizmos); view.Repaint();
            if ((int)view.GetType().GetProperty("selectedSizeIndex", Flags).GetValue(view) != saved.viewSize) throw new InvalidOperationException("Game View restoration did not settle.");
            SessionState.EraseString(SessionKey);
            return new { success = true, restored = true, scenes = saved.scenes.Select(x => x.path).ToArray(), optionsEnabled = saved.optionsEnabled, options = saved.options, recordDirectory = saved.recordDirectory, viewSize = saved.viewSize, gizmos = saved.gizmos };
        }

        private static Saved Read(string session)
        {
            var saved = JsonConvert.DeserializeObject<Saved>(SessionState.GetString(SessionKey, ""));
            if (saved == null || saved.session != session) throw new InvalidOperationException("No replay session owned by this request.");
            return saved;
        }
        private static EditorWindow GameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            return Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>().FirstOrDefault() ?? throw new InvalidOperationException("Open a Game View before replaying.");
        }
        private static string ArtifactDirectory(string output)
        {
            string[] prefixes = { "tmp/", ".ability-lab-cache/", ".impeccable/", "client/Unity/Temp/" };
            if (string.IsNullOrWhiteSpace(output) || Path.IsPathRooted(output) || output.Contains('\\') || output.Split('/').Any(x => x == ".." || x == ".") || !prefixes.Any(x => output.StartsWith(x, StringComparison.Ordinal)))
                throw new ArgumentException("Replay output must be a repository-relative artifact directory.");
            return Path.GetFullPath(Path.Combine(RepositoryRoot(), output));
        }
        private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        private static string Hash(byte[] bytes)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
#endif
