#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SlopArena.Client;
using SlopArena.Client.Camera;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Cinemachine;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Store-screenshot photo shoots: a JSON spec places up to four fighters on a PvP stage, scripts
    /// their inputs per tick and frames free cameras; the shoot writes every requested tick, plus an
    /// optional transparent HUD layer. Layered on the stage-view save/restore lifecycle; nothing is
    /// saved to scenes or assets. tools/photo_shoot.py turns the frames into sheets and final PNGs.
    /// </summary>
    public static class SlopArenaPhotoCommands
    {
        private const string SessionKey = "SlopArena.Photo.Active";
        private static readonly string[] ArtifactPrefixes = { "tmp/", ".impeccable/", ".ability-lab-cache/", "client/Unity/Temp/" };

        private sealed class Saved { public string stage = ""; public int captureFramerate; public bool training; public CharacterClass playerClass; public string packageId = ""; public CharacterClass opponentClass; }

        [CliCommand("sloparena.photo.prepare", "Prepare an in-memory photo shoot on a PvP stage, real Training, or the live Slopball scene for sloparena.photo.live, using the stage-view save/restore lifecycle.", MainThreadRequired = true, Tags = new[] { "capture", "photo" })]
        public static object Prepare(
            [CliArg("stage", "Baked PvP stage key with a Resources presentation prefab, or slopball.", Required = true)] string stage,
            [CliArg("lighting", "scene (default, as Training and Solo) or pvp (the stage's own lights only).")] string lighting = "scene",
            [CliArg("fighter", "Training or Slopball fighter package ID, default fightguy.")] string fighter = "fightguy",
            [CliArg("training", "Run real Training instead of the staged photo shoot on a PvP stage.")] bool training = false,
            [CliArg("opponent", "Training CPU fighter, default fightguy.")] string opponent = "fightguy")
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Photo preparation requires stopped, compiled Edit Mode.");
            if (new[] { SessionKey, "SlopArena.StageViews.Active", "SlopArena.CpuShowcase.Active", "SlopArena.HitFeelReview.Active" }
                .Any(key => !string.IsNullOrEmpty(SessionState.GetString(key, ""))))
                throw new InvalidOperationException("A stage-view, showcase, hit-feel or photo session still needs restoration.");
            CharacterClass selected = fighter switch
            {
                "fightguy" => CharacterClass.FightGuy, "manki" => CharacterClass.Manki,
                "wibou" => CharacterClass.Wibou, "bonk" => CharacterClass.Bonk, "bashie" => CharacterClass.Bashie,
                "salamander" => CharacterClass.Salamander,
                _ => throw new ArgumentException("fighter must be fightguy, manki, wibou, bonk or bashie.")
            };
            if (!Enum.TryParse(opponent, true, out CharacterClass opponentClass) || opponentClass == CharacterClass.None
                || !Enum.GetNames(typeof(CharacterClass)).Any(name => string.Equals(name, opponent, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("opponent must be a fighter name.");
            // Save before the first mutation so the declared restore also handles partial setup failure.
            SessionState.SetString(SessionKey, JsonConvert.SerializeObject(new Saved { stage = stage, captureFramerate = Time.captureFramerate,
                training = training, playerClass = MatchConfig.PlayerClass, packageId = MatchConfig.PlayerPackageId, opponentClass = MatchConfig.Seat(2).Fighter }));
            SlopArenaStageViewCommands.Prepare(stage, lighting);
            if (stage == SlopballMatch.ArenaKey)
            {
                // The stage-view restore puts back the fighter it saved before this in-memory choice.
                MatchConfig.PlayerClass = selected;
                MatchConfig.PlayerPackageId = fighter;
                SessionState.SetString(ArcadeBallMatch.FighterSessionKey, selected.ToString());
                return new { success = true, stage, fighter };
            }
            if (training)
            {
                MatchConfig.Seat(2).Fighter = opponentClass;
                MatchConfig.PlayerClass = selected;
                MatchConfig.PlayerPackageId = fighter;
                var match = Object.FindFirstObjectByType<TrainingMatch>();
                var serialized = new SerializedObject(match);
                serialized.FindProperty("_playerClassOverride").enumValueIndex = Array.IndexOf(Enum.GetValues(typeof(CharacterClass)), selected);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return new { success = true, stage, fighter, training, opponent };
            }
            var camera = GameplayCamera();
            // In memory only: Training never starts, so the shoot owns fighters, stage, camera and HUD.
            var trainingMatch = Object.FindFirstObjectByType<TrainingMatch>()
                ?? throw new InvalidOperationException("The baseline scene has no TrainingMatch.");
            trainingMatch.enabled = false;
            foreach (var renderer in Object.FindObjectsByType<PlayerRenderer>(FindObjectsSortMode.None))
                Object.DestroyImmediate(renderer.gameObject);
            foreach (var input in Object.FindObjectsByType<InputController>(FindObjectsSortMode.None)) input.enabled = false;
            foreach (var mount in Object.FindObjectsByType<CameraMount>(FindObjectsSortMode.None)) mount.enabled = false;
            foreach (var brain in Object.FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None)) brain.enabled = false;
            foreach (var document in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) document.enabled = false;
            camera.tag = "MainCamera";
            return new { success = true, stage, lighting, camera = camera.name };
        }

        [CliCommand("sloparena.photo.shoot", "Run one photo-shoot spec in Play Mode and write each requested tick from its cameras as JPGs (and a transparent HUD layer as PNGs).", MainThreadRequired = true, Tags = new[] { "capture", "photo" })]
        public static async Task<object> Shoot(
            [CliArg("spec", "Repository-relative JSON spec below tmp/ or scenarios/store/ (see docs/steam/store-screenshots.md).", Required = true)] string spec,
            [CliArg("output", "Fresh repository-relative directory below tmp/, .impeccable/, .ability-lab-cache/ or client/Unity/Temp/.", Required = true)] string output)
        {
            var saved = JsonConvert.DeserializeObject<Saved>(SessionState.GetString(SessionKey, ""))
                ?? throw new InvalidOperationException("No photo session; run sloparena.photo.prepare first.");
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Enter Play Mode (editor_play) after sloparena.photo.prepare.");
            // Specs are only read, so the tracked shot recipes may be used in place.
            string specPath = Resolve(spec, "spec", directory: false, extraPrefix: "scenarios/store/");
            string directory = Resolve(output, "output", directory: true);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new ArgumentException("output must be a fresh directory; earlier evidence is never overwritten.");
            var plan = Parse(File.ReadAllText(specPath), saved.stage);
            string arenaPath = BakedContentPaths.ResolveArena(saved.stage);
            var loaded = arenaPath == null ? null : ArenaBinaryFormat.LoadFromFile(arenaPath);
            if (loaded is not ArenaDefinition arena || arena.Purpose != ArenaPurpose.PvP)
                throw new InvalidOperationException("The prepared stage must resolve to a baked PvP arena.");

            var gameplay = GameplayCamera();
            bool gameplayEnabled = gameplay.enabled;
            string gameplayTag = gameplay.tag;
            int previousRate = Time.captureFramerate;
            int previousQuality = QualitySettings.GetQualityLevel();
            int ultra = Array.IndexOf(QualitySettings.names, "Ultra");
            int resumed = 0;
            GameObject? host = null;
            try
            {
                // Store shots use the best preset whatever the local client settings chose.
                if (ultra >= 0) QualitySettings.SetQualityLevel(ultra, true);
                gameplay.enabled = false;
                gameplay.tag = "Untagged";
                // End-of-frame captures need a repainting Game View, as for the hit-feel review.
                var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                Resources.FindObjectsOfTypeAll(viewType).OfType<EditorWindow>().FirstOrDefault()?.Focus();
                // One tick per rendered frame, independent of Editor frame pacing.
                Time.captureFramerate = 60;
                host = new GameObject("Photo Shoot (temporary)");
                host.SetActive(false);
                var shoot = host.AddComponent<SlopArenaPhotoShoot>();
                shoot.Arena = arena;
                shoot.Template = gameplay;
                shoot.OutputDirectory = directory;
                plan(shoot);
                host.SetActive(true);
                double deadline = EditorApplication.timeSinceStartup + 100;
                while (!shoot.Done && shoot.Failure == null)
                {
                    if (EditorApplication.isPaused)
                    {
                        EditorApplication.isPaused = false;
                        resumed++;
                    }
                    if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                        throw new InvalidOperationException($"The shoot stopped or timed out at tick {shoot.Tick}; inspect the console.");
                    await Task.Delay(30);
                }
                if (shoot.Failure != null)
                    throw new InvalidOperationException("The shoot failed: " + shoot.Failure);
                var survey = shoot.Survey;
                File.WriteAllText(Path.Combine(directory, "survey.json"), JsonConvert.SerializeObject(survey, Formatting.Indented));
                return new
                {
                    success = true, stage = saved.stage, spec, output, quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                    editorPausesResumed = resumed, result = shoot.Inspect(), survey
                };
            }
            finally
            {
                if (host != null) Object.Destroy(host);
                Time.captureFramerate = previousRate;
                QualitySettings.SetQualityLevel(previousQuality, true);
                if (gameplay != null)
                {
                    gameplay.enabled = gameplayEnabled;
                    gameplay.tag = gameplayTag;
                }
            }
        }

        [CliCommand("sloparena.photo.live", "Run scripted Slopball steps in the live Arcade scene (after sloparena.photo.prepare --stage slopball and editor_play) and write each requested tick of the composited Game View, HUD included, chat hidden.", MainThreadRequired = true, Tags = new[] { "capture", "photo" })]
        public static async Task<object> Live(
            [CliArg("output", "Fresh repository-relative directory below tmp/, .impeccable/, .ability-lab-cache/ or client/Unity/Temp/.", Required = true)] string output,
            [CliArg("steps", "Training step JSON for the fighter, at most 600 ticks (or use --intercept).")] string? steps = null,
            [CliArg("from-go", "Wait through the real countdown with neutral input before applying steps.")] bool fromGo = true,
            [CliArg("from", "First captured tick after Go.")] int from = 0,
            [CliArg("to", "Last captured tick after Go.")] int to = 120,
            [CliArg("step", "Capture every Nth tick.")] int step = 1,
            [CliArg("quality", "JPG quality, 50–100.")] int quality = 92,
            [CliArg("yaw", "Hold the gameplay orbit at this yaw in degrees; ignored with --camera.")] float? yaw = null,
            [CliArg("pitch", "Hold the gameplay orbit at this pitch in degrees; ignored with --camera.")] float? pitch = null,
            [CliArg("camera", "Free camera JSON: {\"pos\":[x,y,z],\"look\":[x,y,z],\"follow\":\"ball|fighter\",\"lookOffset\":[..],\"move\":bool,\"ease\":0–1,\"fov\":deg}.")] string? camera = null,
            [CliArg("intercept", "Instead of steps, fire a projectile special at the live serve: JSON {\"slot\":\"R\",\"speed\":40,\"gravity\":15,\"delay\":6,\"spawnY\":1,\"min\":3,\"max\":4.5,\"yawOffset\":-20,\"pitch\":8,\"before\":10,\"after\":50}; the capture window follows the predicted impact.")] string? intercept = null)
        {
            if (string.IsNullOrWhiteSpace(steps) == string.IsNullOrWhiteSpace(intercept))
                throw new ArgumentException("Pass either --steps or --intercept.");
            var match = await ReadyMatch<ArcadeBallMatch>(m => m.Ready);
            var live = BeginLive(match, null, output, from, to, step, quality, yaw, pitch, camera);
            try
            {
                object run = string.IsNullOrWhiteSpace(intercept)
                    ? await SlopArenaArcadePhase1Commands.RunBallSteps(match, steps!, fromGo: fromGo)
                    : await RunIntercept(match, live, JsonConvert.DeserializeObject<InterceptPlan>(intercept!) ?? new InterceptPlan());
                var end = await EndLive(30);
                return new { success = true, run, capture = end };
            }
            finally { if (_live != null) await EndLive(0); }
        }

        /// <summary>
        /// A projectile special fired at the live ball. The fighter first walks to a spot offset from where the
        /// serve will descend through the target height, then fires so the shot meets the ball; aim, timing and
        /// the capture window come from the ball's predicted free flight.
        /// </summary>
        private sealed class InterceptPlan
        {
            public string slot = "R";
            public float speed = 40f, gravity = 15f, spawnY = 1f, min = 3f, max = 4.5f, yawOffset = -20f, pitch = 8f;
            public float approachX = 0f, approachZ = -4.5f, xMin = -8f, xMax = 8f, zMin = -14f, zMax = -2f;
            public float? cameraYaw;
            public int delay = 8, hold = 1, before = 10, after = 50;
        }

        private static async Task<object> RunIntercept(ArcadeBallMatch match, SlopArenaPhotoLive live, InterceptPlan plan)
        {
            string? arenaPath = BakedContentPaths.ResolveArena(SlopballMatch.ArenaKey);
            var ballDefinition = (arenaPath == null ? null : ArenaBinaryFormat.LoadFromFile(arenaPath))?.Arcade?.Ball
                ?? throw new InvalidOperationException("The Slopball arena has no ball definition.");
            float retention = 1f / (1f + ballDefinition.LinearDrag * SlopArena.Shared.Simulation.TickDt);
            byte slotWire = SlopArenaTrainingCommands.MapSlot(plan.slot);
            const float dt = SlopArena.Shared.Simulation.TickDt;
            var hits = new List<object>();
            var goals = new List<object>();
            var completion = new TaskCompletionSource<bool>();
            bool started = false, arrived = false;
            uint first = 0;
            int pressAt = -1, impactAt = -1, arrivedAt = -1;
            Vector2? spot = null;
            short aimYaw = 0, aimPitch = 0;
            object? chosen = null;
            if (live.Free == null && plan.cameraYaw.HasValue) { live.HoldYaw = plan.cameraYaw; live.HoldPitch = plan.pitch; }

            List<Vector3> Predict(ArcadeBallState ball)
            {
                // Integrate the ball exactly as Shared does in free flight; collisions are ignored.
                var path = new List<Vector3>();
                Vector3 p = new(ball.Position.X, ball.Position.Y, ball.Position.Z), v = new(ball.Velocity.X, ball.Velocity.Y, ball.Velocity.Z);
                for (int k = 0; k < 300 && p.y > -1f; k++)
                {
                    v.x *= retention; v.z *= retention;
                    v.y = v.y * retention - ballDefinition.Gravity * dt;
                    p += v * dt;
                    path.Add(p);
                }
                return path;
            }

            void Tick(uint tick)
            {
                var sim = match.Simulation;
                if (sim == null || completion.Task.IsCompleted) return;
                if (!started)
                {
                    if (sim.Slopball is SlopballState countdown && countdown.Phase == SlopballPhase.Countdown) return;
                    started = true;
                    first = tick;
                }
                int elapsed = (int)(tick - first);
                foreach (var hit in sim.LastTickHits)
                    hits.Add(new { tick = elapsed, target = hit.TargetEntityId.ToString(), slot = hit.AttackSlot, damage = hit.Damage, force = hit.ImpactForce });
                foreach (var goal in sim.LastTickSlopballEvents)
                    goals.Add(new { tick = elapsed, kind = goal.Kind.ToString(), returns = goal.Returns });
                var self = sim.GetState(1);
                bool flying = sim.ArcadeBall is ArcadeBallState inFlight && inFlight.Active && inFlight.HitstopTicks == 0;
                if (spot == null && flying)
                {
                    var path = Predict(sim.ArcadeBall!.Value);
                    float mid = (plan.min + plan.max) * 0.5f;
                    for (int k = 1; k < path.Count; k++)
                        if (path[k].y < path[k - 1].y && path[k].y <= mid)
                        {
                            spot = new Vector2(Mathf.Clamp(path[k].x + plan.approachX, plan.xMin, plan.xMax),
                                Mathf.Clamp(path[k].z + plan.approachZ, plan.zMin, plan.zMax));
                            break;
                        }
                }
                var input = new InputState { FacingYaw = WireYaw(self.FacingYaw), AimYaw = WireYaw(self.FacingYaw) };
                // Shared ignores Arcade input after Go until it has seen one neutral frame.
                if (elapsed < 2)
                {
                    match.SetProbeInput(input);
                    return;
                }
                if (spot.HasValue && !arrived)
                {
                    var delta = spot.Value - new Vector2(self.PX, self.PZ);
                    if (delta.magnitude > 0.3f)
                    {
                        var direction = delta.normalized;
                        input.MoveX = direction.x; input.MoveY = direction.y;
                        input.FacingYaw = input.AimYaw = (short)Mathf.RoundToInt(Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg * 100f);
                    }
                    else if (self.IsGrounded && Mathf.Abs(self.VX) + Mathf.Abs(self.VZ) < 0.05f)
                    {
                        arrived = true;
                        arrivedAt = elapsed;
                    }
                }
                if (arrived && pressAt < 0 && flying)
                {
                    var path = Predict(sim.ArcadeBall!.Value);
                    Vector3 origin = new(self.PX, self.PY + plan.spawnY, self.PZ);
                    float bestScore = float.MaxValue;
                    for (int lead = 1; lead < path.Count; lead++)
                        for (int k = lead + plan.delay + 1; k < Math.Min(path.Count, lead + plan.delay + 40); k++)
                        {
                            Vector3 b = path[k - 1];
                            bool descending = k >= 2 && path[k - 1].y < path[k - 2].y;
                            if (!descending || b.y < plan.min || b.y > plan.max) continue;
                            float t = (k - lead - plan.delay) * dt;
                            Vector3 need = (b - origin + new Vector3(0f, 0.5f * plan.gravity * t * t, 0f)) / t;
                            float error = Mathf.Abs(need.magnitude - plan.speed);
                            if (error > 1.2f) continue;
                            float score = error + Mathf.Abs(b.y - (plan.min + plan.max) * 0.5f);
                            if (score >= bestScore) continue;
                            bestScore = score;
                            pressAt = elapsed + lead;
                            impactAt = elapsed + k;
                            float yawDeg = Mathf.Atan2(need.x, need.z) * Mathf.Rad2Deg;
                            float pitchDeg = Mathf.Asin(Mathf.Clamp(need.y / need.magnitude, -1f, 1f)) * Mathf.Rad2Deg;
                            aimYaw = (short)Mathf.RoundToInt(yawDeg * 100f);
                            aimPitch = (short)Mathf.RoundToInt(pitchDeg * 100f);
                            chosen = new { pressAt, impactAt, arrivedAt, yaw = yawDeg, pitch = pitchDeg, impact = new[] { b.x, b.y, b.z },
                                fighter = new[] { self.PX, self.PY, self.PZ }, speedError = error };
                        }
                    if (pressAt >= 0)
                    {
                        live.From = Math.Max(0, impactAt - plan.before);
                        live.To = impactAt + plan.after;
                        if (live.Free == null && !plan.cameraYaw.HasValue)
                        {
                            live.HoldYaw = aimYaw / 100f + plan.yawOffset;
                            live.HoldPitch = plan.pitch;
                        }
                    }
                }
                // The probe input set now is consumed by the next tick.
                if (pressAt >= 0)
                {
                    int next = elapsed + 1;
                    input.FacingYaw = aimYaw; input.AimYaw = aimYaw; input.AimPitch = aimPitch;
                    if (next == pressAt) { input.ActiveSlot = slotWire; input.FaceToCamera = true; }
                    input.IsAiming = next >= pressAt && next < pressAt + plan.hold;
                    if (elapsed >= live.To) completion.TrySetResult(true);
                }
                match.SetProbeInput(input);
                if (elapsed > 500) completion.TrySetResult(false);
            }

            double deadline = EditorApplication.timeSinceStartup + 60;
            void Check()
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    completion.TrySetException(new InvalidOperationException("The intercept stopped or exceeded its wall deadline."));
            }
            match.SetProbeInput(default(InputState));
            match.ProbeTickCompleted += Tick;
            EditorApplication.update += Check;
            try
            {
                bool planned = await completion.Task;
                return new { success = planned, spot = spot.HasValue ? new[] { spot.Value.x, spot.Value.y } : null, chosen, hits, goals,
                    returns = match.Simulation?.Slopball is SlopballState state ? state.Returns : 0,
                    presentation = (match as SlopballMatch)?.PresentationDiagnostics };
            }
            finally
            {
                EditorApplication.update -= Check;
                match.ProbeTickCompleted -= Tick;
                match.SetProbeInput(null);
            }
        }

        private static short WireYaw(float radians)
            => (short)Mathf.Clamp(Mathf.RoundToInt(Mathf.DeltaAngle(0f, radians * Mathf.Rad2Deg) * 100f), -18000, 18000);

        [CliCommand("sloparena.photo.live-begin", "Start capturing ticks of live Slopball, Character Course, Tutorial or Training gameplay (for example around replay or tutorial input runs); finish with sloparena.photo.live-end.", MainThreadRequired = true, Tags = new[] { "capture", "photo" })]
        public static async Task<object> LiveBegin(
            [CliArg("output", "Fresh repository-relative directory below tmp/, .impeccable/, .ability-lab-cache/ or client/Unity/Temp/.", Required = true)] string output,
            [CliArg("from", "First captured tick after Go (course elapsed ticks).")] int from = 0,
            [CliArg("to", "Last captured tick after Go.")] int to = 120,
            [CliArg("step", "Capture every Nth tick.")] int step = 1,
            [CliArg("quality", "JPG quality, 50–100.")] int quality = 92,
            [CliArg("yaw", "Hold the gameplay orbit at this yaw in degrees; ignored with --camera.")] float? yaw = null,
            [CliArg("pitch", "Hold the gameplay orbit at this pitch in degrees; ignored with --camera.")] float? pitch = null,
            [CliArg("camera", "Free camera JSON, as for sloparena.photo.live.")] string? camera = null)
        {
            if (to > 36000) throw new ArgumentException("to must be at most 36000.");
            double deadline = EditorApplication.timeSinceStartup + 20;
            while (true)
            {
                var ball = Object.FindFirstObjectByType<ArcadeBallMatch>();
                var course = Object.FindFirstObjectByType<CharacterCourseMatch>();
                var tutorial = Object.FindFirstObjectByType<TutorialMatch>();
                var training = Object.FindFirstObjectByType<TrainingMatch>();
                if (ball != null && ball.Ready || course != null && course.Ready || tutorial != null && tutorial.Ready || training != null && training.CaptureSequenceReady)
                {
                    var live = BeginLive(ball != null && ball.Ready ? ball : null, course != null && course.Ready ? course : null,
                        output, from, to, step, quality, yaw, pitch, camera, tutorial != null && tutorial.Ready ? tutorial : null,
                        training != null && training.CaptureSequenceReady ? training : null);
                    return new { success = true, output, from, to, step, cameraStyle = live.Mount?.Style.ToString(),
                        cameraYaw = live.Mount?.GetCameraYawDeg(), cameraPitch = live.Mount?.GetCameraPitchDeg(),
                        freeCamera = live.Free != null, scene = ball != null && ball.Ready ? "slopball" : course != null && course.Ready ? "course" : tutorial != null && tutorial.Ready ? "tutorial" : "training" };
                }
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    throw new InvalidOperationException("No ready Slopball, Character Course, Tutorial or Training match; enter Play Mode in its scene first.");
                await Task.Delay(50);
            }
        }

        [CliCommand("sloparena.photo.live-end", "Finish a live capture: wait briefly for its last tick, write states.jsonl and restore timing, quality and camera. Safe to call when none is active.", MainThreadRequired = true, Tags = new[] { "capture", "photo" })]
        public static async Task<object> LiveEnd([CliArg("wait", "Seconds to wait for the capture window to complete, 0–60.")] int wait = 10)
        {
            if (wait < 0 || wait > 60) throw new ArgumentException("wait must be 0–60 seconds.");
            if (_live == null) return new { success = true, started = false };
            return await EndLive(wait);
        }

        private sealed class LiveSession
        {
            public SlopArenaPhotoLive Live = null!;
            public GameObject Host = null!;
            public CameraMount? Mount;
            public UnityEngine.Camera? Render;
            public CinemachineBrain[] Brains = Array.Empty<CinemachineBrain>();
            public bool MountEnabled;
            public float Fov, FixedDeltaTime;
            public int CaptureRate, Quality;
            public string Output = "";
        }

        private static LiveSession? _live;

        private static async Task<T> ReadyMatch<T>(Func<T, bool> ready) where T : Object
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode in the match scene first.");
            double deadline = EditorApplication.timeSinceStartup + 20;
            T? match;
            while ((match = Object.FindFirstObjectByType<T>()) == null || !ready(match))
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                    throw new InvalidOperationException(typeof(T).Name + " did not become ready; inspect the console.");
                await Task.Delay(50);
            }
            return match;
        }

        private static SlopArenaPhotoLive BeginLive(ArcadeBallMatch? ball, CharacterCourseMatch? course, string output,
            int from, int to, int step, int quality, float? yaw, float? pitch, string? camera, TutorialMatch? tutorial = null, TrainingMatch? training = null)
        {
            if (_live != null) throw new InvalidOperationException("A live capture is already running; call sloparena.photo.live-end.");
            if (from < 0 || to < from || step < 1 || step > 600 || quality < 50 || quality > 100)
                throw new ArgumentException("Use 0 ≤ from ≤ to, step 1–600 and quality 50–100.");
            string directory = Resolve(output, "output", directory: true);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new ArgumentException("output must be a fresh directory; earlier evidence is never overwritten.");
            PhotoCamera? free = null;
            string follow = "";
            if (!string.IsNullOrWhiteSpace(camera))
            {
                var item = JObject.Parse(camera!);
                Only(item, "camera", "pos", "look", "follow", "lookOffset", "move", "ease", "fov", "roll", "up");
                follow = (string?)item["follow"] ?? "";
                if (follow is not ("" or "ball" or "fighter")) throw new ArgumentException("camera.follow must be ball or fighter.");
                free = new PhotoCamera
                {
                    Name = "live", Position = Vec(item, "pos", Vector3.zero), LookAt = Vec(item, "look", Vector3.zero),
                    LookOffset = Vec(item, "lookOffset", Vector3.zero), Move = (bool?)item["move"] ?? false,
                    Ease = Float(item, "ease", 1f, 0.01f, 1f), Fov = Float(item, "fov", 40f, 5f, 120f),
                    Roll = Float(item, "roll", 0f, -45f, 45f), Up = Vec(item, "up", Vector3.up)
                };
            }
            var mount = Object.FindFirstObjectByType<CameraMount>();
            var render = mount != null ? mount.RenderCamera : UnityEngine.Camera.main;
            if (render == null) throw new InvalidOperationException("The match has no render camera.");
            var session = new LiveSession
            {
                Mount = mount, Render = render, MountEnabled = mount != null && mount.enabled, Fov = render.fieldOfView,
                FixedDeltaTime = Time.fixedDeltaTime, CaptureRate = Time.captureFramerate, Quality = QualitySettings.GetQualityLevel(),
                Brains = Object.FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None).Where(x => x.enabled).ToArray(),
                Output = output
            };
            _live = session;
            int ultra = Array.IndexOf(QualitySettings.names, "Ultra");
            if (ultra >= 0) QualitySettings.SetQualityLevel(ultra, true);
            if (free != null)
            {
                if (mount != null) mount.enabled = false;
                foreach (var brain in session.Brains) brain.enabled = false;
            }
            var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Resources.FindObjectsOfTypeAll(viewType).OfType<EditorWindow>().FirstOrDefault()?.Focus();
            // One tick per rendered frame, independent of Editor frame pacing.
            Time.fixedDeltaTime = SlopArena.Shared.Simulation.TickDt;
            Time.captureFramerate = 60;
            SlopArenaPhotoLive.HideChat();
            session.Host = new GameObject("Photo Live (temporary)");
            session.Host.SetActive(false);
            var live = session.Host.AddComponent<SlopArenaPhotoLive>();
            live.Ball = ball; live.Course = course; live.Tutorial = tutorial; live.Training = training; live.RenderCamera = render; live.Mount = mount;
            live.Free = free; live.FreeFollow = follow; live.HoldYaw = free == null ? yaw : null; live.HoldPitch = free == null ? pitch : null;
            live.From = from; live.To = to; live.Step = step; live.Quality = quality; live.OutputDirectory = directory;
            session.Live = live;
            session.Host.SetActive(true);
            return live;
        }

        private static async Task<object> EndLive(int waitSeconds)
        {
            var session = _live;
            if (session == null) return new { success = true, started = false };
            double deadline = EditorApplication.timeSinceStartup + waitSeconds;
            while (session.Live != null && !session.Live.Done && session.Live.Failure == null
                && EditorApplication.isPlaying && EditorApplication.timeSinceStartup < deadline)
                await Task.Delay(30);
            _live = null;
            try
            {
                if (session.Live == null)
                    return new { success = false, failure = "The live capture component was destroyed (Play Mode stopped?).", output = session.Output };
                session.Live.WriteStates();
                return new { success = session.Live.Failure == null, failure = session.Live.Failure, output = session.Output,
                    frames = session.Live.FramesWritten, elapsed = session.Live.Elapsed, done = session.Live.Done,
                    screen = $"{Screen.width}x{Screen.height}" };
            }
            finally
            {
                if (session.Host != null) Object.Destroy(session.Host);
                Time.captureFramerate = session.CaptureRate;
                Time.fixedDeltaTime = session.FixedDeltaTime;
                QualitySettings.SetQualityLevel(session.Quality, true);
                if (session.Render != null) session.Render.fieldOfView = session.Fov;
                if (session.Mount != null) session.Mount.enabled = session.MountEnabled;
                foreach (var brain in session.Brains) if (brain != null) brain.enabled = true;
            }
        }

        [CliCommand("sloparena.photo.stop", "Stop Play Mode only for an active photo session.", MainThreadRequired = true, Tags = new[] { "capture", "photo" })]
        public static object Stop()
        {
            if (string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")))
                return new { success = true, started = false };
            return SlopArenaStageViewCommands.Stop();
        }

        [CliCommand("sloparena.photo.restore", "Restore capture timing and the scenes/view saved by prepare, after stopping Play Mode.", MainThreadRequired = true, Tags = new[] { "capture", "photo" })]
        public static object Restore()
        {
            string text = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(text)) return new { success = true, restored = true, started = false };
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop and settle Play Mode before restoring a photo session.");
            var saved = JsonConvert.DeserializeObject<Saved>(text) ?? new Saved();
            Time.captureFramerate = saved.captureFramerate;
            if (saved.training)
            {
                MatchConfig.PlayerClass = saved.playerClass;
                MatchConfig.PlayerPackageId = saved.packageId;
                MatchConfig.Seat(2).Fighter = saved.opponentClass;
            }
            var result = SlopArenaStageViewCommands.Restore();
            SessionState.EraseString(SessionKey);
            return result;
        }

        private static UnityEngine.Camera GameplayCamera()
            => Object.FindObjectsByType<CameraMount>(FindObjectsSortMode.None)
                .Select(mount => mount.RenderCamera).FirstOrDefault(value => value != null)
                ?? throw new InvalidOperationException("The capture scene has no gameplay render camera.");

        private static string Resolve(string path, string name, bool directory, string? extraPrefix = null)
        {
            path = (path ?? "").Trim().TrimEnd('/');
            var prefixes = extraPrefix == null ? ArtifactPrefixes : ArtifactPrefixes.Append(extraPrefix).ToArray();
            if (Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(x => x == "" || x == "." || x == "..")
                || !prefixes.Any(x => (path + "/").StartsWith(x, StringComparison.Ordinal)))
                throw new ArgumentException($"{name} must be a repository-relative path below " + string.Join(", ", prefixes) + ".");
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", path));
            if (!directory && !File.Exists(full)) throw new ArgumentException($"{name} '{path}' does not exist.");
            return full;
        }

        // ---- spec parsing ---------------------------------------------------------------

        private static Action<SlopArenaPhotoShoot> Parse(string json, string stage)
        {
            JObject root;
            try { root = JObject.Parse(json); }
            catch (JsonException error) { throw new ArgumentException("spec is not valid JSON: " + error.Message); }
            Only(root, "spec", "stage", "capture", "hud", "landmarks", "fighters", "cameras", "notes", "pick");
            string specStage = (string?)root["stage"] ?? "";
            if (specStage != stage)
                throw new ArgumentException($"The spec is for stage '{specStage}' but the session prepared '{stage}'.");

            var capture = root["capture"] as JObject ?? throw new ArgumentException("spec.capture is required.");
            Only(capture, "capture", "from", "to", "step", "width", "height", "quality");
            int from = Int(capture, "from", 0, 0, 900), to = Int(capture, "to", 120, 0, 900);
            int step = Int(capture, "step", 1, 1, 60), width = Int(capture, "width", 960, 64, 3840);
            int height = Int(capture, "height", 540, 64, 2160), quality = Int(capture, "quality", 90, 50, 100);
            if (from > to || width % 2 != 0 || height % 2 != 0)
                throw new ArgumentException("capture needs from ≤ to and even frame sizes.");

            var fighters = new List<PhotoFighter>();
            foreach (var token in root["fighters"] as JArray ?? new JArray())
            {
                var item = token as JObject ?? throw new ArgumentException("Every fighter must be an object.");
                Only(item, "fighter", "id", "package", "feet", "grounded", "yaw", "damage", "deaths", "target", "aimDistance", "pitch", "input");
                var fighter = new PhotoFighter
                {
                    Id = (ulong)Int(item, "id", 0, 1, 4),
                    Package = (string?)item["package"] ?? throw new ArgumentException("Every fighter needs a package."),
                    Feet = Vec(item, "feet", Vector3.zero),
                    Grounded = (bool?)item["grounded"] ?? true,
                    YawDegrees = Float(item, "yaw", 0f, -360f, 360f),
                    Damage = (ushort)Int(item, "damage", 0, 0, 999),
                    Deaths = (byte)Int(item, "deaths", 0, 0, 9),
                    Target = (ulong)Int(item, "target", 0, 0, 4),
                    AimDistance = Float(item, "aimDistance", -1f, -1f, 65f),
                    AimPitchDegrees = Float(item, "pitch", 0f, -90f, 90f)
                };
                foreach (var stepToken in item["input"] as JArray ?? new JArray())
                    fighter.Inputs.Add(ParseInput(stepToken as JObject ?? throw new ArgumentException("Every input must be an object.")));
                fighters.Add(fighter);
            }
            if (fighters.Count > 4 || fighters.Select(f => f.Id).Distinct().Count() != fighters.Count
                || fighters.Any(f => f.Target == f.Id || f.Target != 0 && fighters.All(o => o.Id != f.Target)))
                throw new ArgumentException("Use up to four fighters with unique ids 1–4; targets must name another fighter.");

            var cameras = new List<PhotoCamera>();
            foreach (var token in root["cameras"] as JArray ?? new JArray())
            {
                var item = token as JObject ?? throw new ArgumentException("Every camera must be an object.");
                Only(item, "camera", "name", "pos", "look", "follow", "lookOffset", "move", "spread", "ease", "fov", "ortho", "roll", "far", "up");
                var camera = new PhotoCamera
                {
                    Name = (string?)item["name"] ?? "",
                    Position = Vec(item, "pos", Vector3.zero),
                    LookAt = Vec(item, "look", Vector3.zero),
                    // One fighter id, or a list whose midpoint the camera follows.
                    Follow = (item["follow"] is JArray ids ? ids.Select(id => (int)id) : new[] { Int(item, "follow", 0, 0, 4) })
                        .Where(id => id != 0).Select(id => (ulong)id).ToArray(),
                    Spread = Float(item, "spread", 0f, 0f, 5f),
                    LookOffset = Vec(item, "lookOffset", Vector3.zero),
                    Move = (bool?)item["move"] ?? false,
                    Ease = Float(item, "ease", 1f, 0.01f, 1f),
                    Fov = Float(item, "fov", 40f, 5f, 120f),
                    Ortho = Float(item, "ortho", 0f, 0f, 500f),
                    Roll = Float(item, "roll", 0f, -45f, 45f),
                    Far = Float(item, "far", 0f, 0f, 5000f),
                    Up = Vec(item, "up", Vector3.up)
                };
                if (!Regex.IsMatch(camera.Name, "^[a-z0-9-]{1,24}$") || camera.Name == "hud")
                    throw new ArgumentException($"Camera name '{camera.Name}' must be 1–24 lowercase letters, digits or dashes (not 'hud').");
                if (camera.Follow.Any(id => fighters.All(f => f.Id != id)))
                    throw new ArgumentException($"Camera '{camera.Name}' follows a fighter that is not in the shoot.");
                cameras.Add(camera);
            }
            if (cameras.Count == 0 || cameras.Count > 9 || cameras.Select(c => c.Name).Distinct().Count() != cameras.Count)
                throw new ArgumentException("Request one to nine uniquely named cameras.");

            bool hud = root["hud"] is JObject;
            ulong hudLocal = 1;
            int hudStocks = 3;
            bool hudGamepad = false;
            if (root["hud"] is JObject hudObject)
            {
                Only(hudObject, "hud", "local", "stocks", "prompts");
                hudLocal = (ulong)Int(hudObject, "local", 1, 1, 4);
                hudStocks = Int(hudObject, "stocks", 3, 0, 9);
                string prompts = (string?)hudObject["prompts"] ?? "keyboard";
                if (prompts is not ("keyboard" or "gamepad"))
                    throw new ArgumentException("hud.prompts must be keyboard or gamepad.");
                hudGamepad = prompts == "gamepad";
                if (fighters.All(f => f.Id != hudLocal))
                    throw new ArgumentException("hud.local must name a fighter in the shoot.");
            }
            string[] landmarks = (root["landmarks"] as JArray ?? new JArray()).Select(x => (string?)x ?? "").Where(x => x.Length > 0).ToArray();

            // Keep one command well inside the CLI timeout: bound the pixels written.
            long frames = (to - from) / step + 1;
            double megapixels = frames * (cameras.Count + (hud ? 2 : 0)) * (double)width * height / 1e6;
            if (megapixels > 1400)
                throw new ArgumentException($"The shoot would write {megapixels:0} megapixels; narrow the window, lower the size or use fewer cameras (limit 1400).");

            return shoot =>
            {
                shoot.Fighters = fighters;
                shoot.Cameras = cameras;
                shoot.From = from; shoot.To = to; shoot.Step = step;
                shoot.Width = width; shoot.Height = height; shoot.Quality = quality;
                shoot.Hud = hud; shoot.HudLocal = hudLocal; shoot.HudStocks = hudStocks; shoot.HudGamepad = hudGamepad;
                shoot.Landmarks = landmarks;
            };
        }

        private static PhotoInput ParseInput(JObject item)
        {
            Only(item, "input", "at", "from", "to", "press", "aim", "jump", "down", "shield", "grab", "dash", "move", "face");
            int at = Int(item, "at", -1, -1, 900);
            int from = at >= 0 ? at : Int(item, "from", -1, 0, 900);
            int to = Int(item, "to", from, 0, 900);
            if (from < 0 || to < from)
                throw new ArgumentException("Every input needs at, or from (and optionally to ≥ from).");
            string? press = (string?)item["press"];
            return new PhotoInput
            {
                From = from, To = to,
                Slot = press == null ? AbilitySlots.None : SlopArenaTrainingCommands.MapSlot(press),
                Aim = (bool?)item["aim"] ?? false,
                Jump = (bool?)item["jump"] ?? false,
                Down = (bool?)item["down"] ?? false,
                Shield = (bool?)item["shield"] ?? false,
                Grab = (bool?)item["grab"] ?? false,
                Dash = (bool?)item["dash"] ?? false,
                MoveYaw = item["move"] == null ? (float?)null : Float(item, "move", 0f, -360f, 360f),
                FaceYaw = item["face"] == null ? (float?)null : Float(item, "face", 0f, -360f, 360f)
            };
        }

        private static void Only(JObject item, string what, params string[] names)
        {
            foreach (var property in item.Properties())
                if (!names.Contains(property.Name))
                    throw new ArgumentException($"Unknown {what} field '{property.Name}'.");
        }

        private static int Int(JObject item, string name, int fallback, int min, int max)
        {
            var token = item[name];
            if (token == null) return fallback;
            if (token.Type != JTokenType.Integer) throw new ArgumentException($"'{name}' must be an integer.");
            int value = token.Value<int>();
            if (value < min || value > max) throw new ArgumentException($"'{name}' must be between {min} and {max}.");
            return value;
        }

        private static float Float(JObject item, string name, float fallback, float min, float max)
        {
            var token = item[name];
            if (token == null) return fallback;
            if (token.Type is not (JTokenType.Integer or JTokenType.Float)) throw new ArgumentException($"'{name}' must be a number.");
            float value = token.Value<float>();
            if (float.IsNaN(value) || value < min || value > max) throw new ArgumentException($"'{name}' must be between {min} and {max}.");
            return value;
        }

        private static Vector3 Vec(JObject item, string name, Vector3 fallback)
        {
            var token = item[name];
            if (token == null) return fallback;
            if (token is not JArray array || array.Count != 3 || array.Any(x => x.Type is not (JTokenType.Integer or JTokenType.Float)))
                throw new ArgumentException($"'{name}' must be an [x, y, z] array.");
            var value = new Vector3(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>());
            if (float.IsNaN(value.x + value.y + value.z) || value.magnitude > 10000f)
                throw new ArgumentException($"'{name}' is out of range.");
            return value;
        }
    }
}
#endif
