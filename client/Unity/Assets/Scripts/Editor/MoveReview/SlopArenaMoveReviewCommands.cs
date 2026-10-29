#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SlopArena.Client.Camera;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools.MoveReview
{
    [InitializeOnLoad]
    public static class SlopArenaMoveReviewCommands
    {
        private static MoveRecipe _prepared;
        static SlopArenaMoveReviewCommands()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () => TakeSession.EndIfActive();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) TakeSession.EndIfActive();
            };
        }

        [CliCommand("sloparena.moves.prepare", "Validate a move recipe, write plan.json and prepare disposable Training.", MainThreadRequired = true, Tags = new[] { "capture", "moves" })]
        public static object Prepare(
            [CliArg("recipe", "Repository-relative recipe below scenarios/moves/ or tmp/.", Required = true)] string recipe,
            [CliArg("output", "Run directory below tmp/.", Required = true)] string output,
            [CliArg("moves", "Comma-separated move ids to include in plan.json; omitted means all.")] string moves = null,
            [CliArg("views", "Comma-separated view subset, also supplied to take.")] string views = null,
            [CliArg("size", "Requested even WxH, also supplied to take.")] string size = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Prepare requires stopped Edit Mode.");
            var parsed = MoveRecipeParser.Load(recipe);
            var coverage = PackageCheck.Check(parsed);
            var selected = moves == null ? parsed.Moves.ToArray() : moves.Split(',').Select(parsed.Move).ToArray();
            if (selected.Select(m => m.Id).Distinct().Count() != selected.Length) throw new ArgumentException("Duplicate selected move.");
            var viewNames = views == null ? parsed.Views.Select(v => v.Name).ToArray() : views.Split(',');
            if (viewNames.Length == 0 || viewNames.Distinct().Count() != viewNames.Length || viewNames.Any(n => !parsed.Views.Any(v => v.Name == n)))
                throw new ArgumentException("Unknown or duplicate view name.");
            var requestedSize = size == null ? parsed.Size : MoveRecipeParser.ParseSize(size);
            string directory = MoveRecipeParser.Resolve(output, true);
            if (File.Exists(Path.Combine(directory, "plan.json"))) throw new ArgumentException("The run already has plan.json; use a fresh run.");
            var plan = new { format = 1, package = parsed.Package, recipe, recipeSha256 = parsed.Sha256,
                formNames = coverage.FormNames, coverage = coverage.Forms,
                moves = selected.Select(m => new { id = m.Id, slot = m.Slot.Id, form = m.Form,
                    label = m.Label ?? coverage.Slots[m.Id].Name, ticks = m.Ticks,
                    size = CaptureBudget.Fit(requestedSize, viewNames.Length, m.Ticks).ToArray() }) };
            SlopArenaPhotoCommands.Prepare(parsed.Stage, "scene", parsed.Package, true, parsed.Dummy.Package);
            Directory.CreateDirectory(directory);
            Write(Path.Combine(directory, "plan.json"), plan);
            _prepared = parsed;
            return new { success = true, output, moves = selected.Length, recipeSha256 = parsed.Sha256 };
        }

        [CliCommand("sloparena.moves.take", "Arrange, settle and capture one move from every requested view in one Training take.", MainThreadRequired = true, Tags = new[] { "capture", "moves" })]
        public static async Task<object> Take(
            [CliArg("recipe", "Repository-relative move recipe.", Required = true)] string recipe,
            [CliArg("move", "Recipe move id.", Required = true)] string move,
            [CliArg("output", "Fresh take directory below tmp/.", Required = true)] string output,
            [CliArg("views", "Comma-separated subset of recipe views.")] string views = null,
            [CliArg("size", "Even requested WxH; fitted to the per-take pixel budget.")] string size = null,
            [CliArg("quality", "JPG quality 50–100.")] int? quality = null)
        {
            var parsed = _prepared != null && _prepared.Path == recipe ? _prepared : MoveRecipeParser.Load(recipe);
            if (MoveRecipeParser.Hash(File.ReadAllBytes(MoveRecipeParser.Resolve(recipe))) != parsed.Sha256)
                throw new InvalidOperationException("The recipe changed after preparation; prepare a fresh run.");
            var spec = parsed.Move(move);
            var coverage = PackageCheck.Check(parsed);
            string directory = MoveRecipeParser.Resolve(output, true);
            if (Directory.Exists(directory)) throw new ArgumentException("The take output must be a fresh directory.");
            int jpgQuality = quality ?? parsed.Quality;
            if (jpgQuality < 50 || jpgQuality > 100) throw new ArgumentException("quality must be 50–100.");
            var requestedSize = size == null ? parsed.Size : MoveRecipeParser.ParseSize(size);
            var match = await Ready();
            if (TakeSession.IsActive || SlopArenaPhotoCommands.LiveActive || SlopArenaGamepadCommands.Active || VirtualPadTimeline.Active)
                throw new InvalidOperationException("Another input/capture session is active.");
            if (match.CaptureSimulation.GetDefinition(MatchConfig.LocalEntityId).Class != coverage.Definition.Class
                || match.CaptureSimulation.GetDefinition(match.SelectedNpcId).Class != ResolveDummy(parsed.Dummy.Package))
                throw new InvalidOperationException("The Training fighter or selected dummy does not match the recipe.");
            var mount = Object.FindFirstObjectByType<CameraMount>();
            if (mount == null || !mount.isActiveAndEnabled || mount.RenderCamera == null) throw new InvalidOperationException("An enabled gameplay CameraMount is required.");
            try
            {
                TakeSession.Begin(match, mount);
                var arranged = TrainingArrangement.Apply(match, mount, new Arrangement(spec.Facing.Degrees * Mathf.Deg2Rad,
                    spec.Facing.FromSpawn, spec.Form, spec.Dummy.Distance, spec.Dummy.Behavior, spec.Dummy.Damage));
                var plan = TakeCompiler.Compile(parsed, spec, arranged, match, views, requestedSize, jpgQuality, directory);
                var rig = MoveTakeRig.Begin(match, mount, mount.RenderCamera, plan);
                TakeSession.Rig = rig;
                double deadline = EditorApplication.timeSinceStartup + 170;
                while (!rig.Done)
                {
                    if (EditorApplication.isPaused) { EditorApplication.isPaused = false; rig.Result.EditorPausesResumed++; }
                    if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                        throw new TimeoutException("Move take stopped or exceeded 170 seconds.");
                    await Task.Delay(20);
                }
                var result = rig.Result;
                if (!result.Success) throw new InvalidOperationException(result.Failure);
                if (result.Records.Count != spec.Ticks || result.FramesWritten != spec.Ticks * plan.Views.Count)
                    throw new InvalidOperationException("The take is incomplete.");
                var problems = new List<string>();
                var phases = PhaseResolver.Resolve(spec.Phases, spec, result, problems);
                var start = PhaseResolver.Start(spec, result);
                var hits = result.Records.SelectMany(r => r.Hits.Where(h => h.OwnerEntityId == r.Fighter.EntityId)
                    .Select(h => new { frame = r.Frame, damage = h.Damage, blocked = h.Blocked, target = h.TargetEntityId.ToString() })).ToArray();
                var receipt = new { format = 1, recipe, recipeSha256 = parsed.Sha256, move, package = parsed.Package,
                    form = spec.Form, formName = coverage.FormNames[spec.Form], slot = spec.Slot.Id,
                    label = spec.Label ?? coverage.Slots[spec.Id].Name, stage = parsed.Stage,
                    dummy = new { package = spec.Dummy.Package, distance = spec.Dummy.Distance, behavior = spec.Dummy.Behavior.ToString(), damage = spec.Dummy.Damage },
                    facing = arranged.FacingRadians * Mathf.Rad2Deg, requestedSize = requestedSize.ToArray(), size = plan.Size.ToArray(),
                    quality = jpgQuality, ticks = spec.Ticks, settle = plan.SettleTicks,
                    views = plan.Views.Select(v => new { name = v.Name, yaw = v.Yaw, pitch = v.Pitch }),
                    start = start == null ? null : new { frame = start.Frame, grounded = start.GroundedBefore, observedSlot = start.ObservedSlot },
                    hits, phases, framesWritten = result.FramesWritten, editorPausesResumed = result.EditorPausesResumed, problems };
                if (start == null || start.GroundedBefore == spec.Slot.IsAirborne)
                    throw new InvalidOperationException(string.Join(" ", problems));
                Write(Path.Combine(directory, "take.json"), receipt);
                return new { success = true, move, ticks = spec.Ticks, views = plan.Views.Count, frames = result.FramesWritten, problems };
            }
            finally { TakeSession.EndIfActive(); }
        }

        private static CharacterClass ResolveDummy(string package)
        {
            if (!SlopArena.Client.ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var error)) throw new InvalidOperationException(error);
            return catalog.ResolvePackage(package)?.Definition.Class ?? throw new ArgumentException("Missing dummy package.");
        }
        internal static async Task<TrainingMatch> Ready()
        {
            double deadline = EditorApplication.timeSinceStartup + 20;
            TrainingMatch match;
            while ((match = Object.FindFirstObjectByType<TrainingMatch>()) == null || !match.CaptureSequenceReady)
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new InvalidOperationException("No ready, unpaused Training duel.");
                await Task.Delay(30);
            }
            return match;
        }
        internal static void Write(string path, object value)
        {
            string temporary = path + ".writing";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented) + "\n");
            File.Move(temporary, path);
        }

        [CliCommand("sloparena.moves.abort", "Restore an active move take, including after reload. Safe to repeat.", MainThreadRequired = true, Tags = new[] { "capture", "moves" })]
        public static object Abort() => new { success = true, restored = TakeSession.EndIfActive() };

        [CliCommand("sloparena.moves.test", "Check move recipe parsing, rotation, budget and phase resolution without advancing gameplay.", MainThreadRequired = true, Tags = new[] { "moves", "testing" })]
        public static object Test()
        {
            const string valid = "{\"package\":\"nyx\",\"stage\":\"slop_pit\",\"moves\":[{\"id\":\"test\",\"slot\":\"ground.1\",\"input\":[{\"ticks\":1,\"slot\":\"1\"},{\"ticks\":89}]}]}";
            var parsed = MoveRecipeParser.Parse(valid, "self-test");
            if (parsed.Views.Count != 10 || parsed.Moves[0].Ticks != 90 || parsed.Moves[0].Script is not StepScript)
                throw new InvalidOperationException("Valid recipe did not normalize correctly.");
            var invalid = new[] { valid.Replace("\"input\":", "\"pad\":{\"frames\":1,\"events\":[]},\"input\":"),
                valid.Replace("ground.1", "ground.2"), valid.Replace("\"ticks\":89", "\"ticks\":600"),
                valid.Replace("\"package\":\"nyx\"", "\"package\":\"nyx\",\"package\":\"nyx\""),
                valid.Replace("\"id\":\"test\"", "\"id\":\"../bad\""), valid.Replace("\"slot\":\"1\"", "\"slot\":1"),
                valid.Replace("\"stage\":\"slop_pit\"", "\"stage\":\"slop_pit\",\"unknown\":true"),
                valid.Replace("\"input\":[", "\"phases\":[{\"name\":\"bad\",\"at\":\"wat\"}],\"input\":[") };
            foreach (string json in invalid)
            {
                bool rejected = false;
                try { MoveRecipeParser.Parse(json, "self-test"); } catch (ArgumentException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Accepted invalid recipe: " + json);
            }
            var rotated = TakeCompiler.Rotate(0, 1, Mathf.PI / 2);
            if (Mathf.Abs(rotated.X - 1) > 0.0001f || Mathf.Abs(rotated.Y) > 0.0001f) throw new InvalidOperationException("Facing rotation failed.");
            var fit = CaptureBudget.Fit(new FrameSize(960, 540), 10, 600);
            if (fit.Width != 640 || fit.Height != 360) throw new InvalidOperationException("Worst-case budget fit failed.");
            var take = new TakeResult();
            for (int i = 0; i < 20; i++) take.Records.Add(new TickRecord { Frame = i, GroundedBefore = true,
                Fighter = new CharacterState { EntityId = 1, State = i < 2 ? ActionState.Idle : ActionState.Attacking, IsGrounded = true, AttackSlot = AbilitySlots.Slot1 },
                Hits = i == 7 ? new[] { new SpellResolver.HitResult { OwnerEntityId = 1 } } : Array.Empty<SpellResolver.HitResult>(), Cues = Array.Empty<TimelinePresentationEvent>() });
            var problems = new List<string>();
            var phases = PhaseResolver.Resolve(parsed.Phases, parsed.Moves[0], take, problems);
            if (phases[0].frame != 1 || phases[1].frame != 8 || phases[2].frame != 19 || problems.Count != 1)
                throw new InvalidOperationException("Phase anchors or clamping diagnostics failed.");
            var nyx = MoveRecipeParser.Load("scenarios/moves/nyx.json");
            PackageCheck.Check(nyx);
            return new { success = true, rejected = invalid.Length, nyxMoves = nyx.Moves.Count, maxViews = CaptureBudget.MaxViews, worstCaseSize = fit.ToArray() };
        }
    }

    internal static class TakeSession
    {
        private const string Key = "SlopArena.MoveReview.Active";
        internal static MoveTakeRig Rig;
        public static bool IsActive => !string.IsNullOrEmpty(SessionState.GetString(Key, ""));
        private sealed class Saved
        {
            public float Capture, Fixed, Yaw, Pitch;
            public int Quality, Match, Mount;
            public bool Interpolation;
        }
        public static void Begin(TrainingMatch match, CameraMount mount)
        {
            if (IsActive) throw new InvalidOperationException("A move take needs restoration.");
            SessionState.SetString(Key, JsonConvert.SerializeObject(new Saved { Capture = Time.captureDeltaTime,
                Fixed = Time.fixedDeltaTime, Quality = QualitySettings.GetQualityLevel(), Match = match.GetInstanceID(),
                Mount = mount.GetInstanceID(), Yaw = mount.GetCameraYawDeg(), Pitch = mount.GetCameraPitchDeg(), Interpolation = match.RenderInterpolationEnabled }));
            Time.captureFramerate = 60; Time.fixedDeltaTime = SlopArena.Shared.Simulation.TickDt;
            int ultra = Array.IndexOf(QualitySettings.names, "Ultra");
            if (ultra >= 0) QualitySettings.SetQualityLevel(ultra, true);
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>().FirstOrDefault()?.Focus();
        }
        public static bool EndIfActive()
        {
            if (Rig != null) Object.DestroyImmediate(Rig.gameObject);
            Rig = null;
            string json = SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(json)) return false;
            var saved = JsonConvert.DeserializeObject<Saved>(json);
            Time.captureDeltaTime = saved.Capture; Time.fixedDeltaTime = saved.Fixed;
            QualitySettings.SetQualityLevel(saved.Quality, true);
            if (EditorUtility.InstanceIDToObject(saved.Match) is TrainingMatch match) match.SetRenderInterpolation(saved.Interpolation);
            if (EditorUtility.InstanceIDToObject(saved.Mount) is CameraMount mount)
            { mount.SetCameraYawDeg(saved.Yaw); mount.SetCameraPitchDeg(saved.Pitch); }
            SessionState.EraseString(Key);
            return true;
        }
    }
}
#endif
