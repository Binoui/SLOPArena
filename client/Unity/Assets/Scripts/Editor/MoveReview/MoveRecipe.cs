#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SlopArena.Client;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace SlopArena.EditorTools.MoveReview
{
    public readonly struct FacingSpec
    {
        public readonly bool FromSpawn;
        public readonly float Degrees;
        public FacingSpec(bool fromSpawn, float degrees) { FromSpawn = fromSpawn; Degrees = degrees; }
    }
    public sealed class ViewSpec
    {
        public string Name { get; }
        public float Yaw { get; }
        public float Pitch { get; }
        internal ViewSpec(string name, float yaw, float pitch)
        {
            Name = name;
            Yaw = yaw;
            Pitch = pitch;
        }
    }
    public sealed class ViewFraming
    {
        public float Distance { get; }
        public float Fov { get; }
        public float Look { get; }
        public float Spread { get; }
        public float GameplayPitch { get; }
        internal ViewFraming(float distance, float fov, float look, float spread, float gameplayPitch)
        {
            Distance = distance;
            Fov = fov;
            Look = look;
            Spread = spread;
            GameplayPitch = gameplayPitch;
        }
    }
    public sealed class DummySpec
    {
        public string Package { get; }
        public float Distance { get; }
        public NpcAiMode Behavior { get; }
        public int Damage { get; }
        public string TimelineJson { get; }
        internal DummySpec(string package, float distance, NpcAiMode behavior, int damage, string timelineJson)
        {
            Package = package;
            Distance = distance;
            Behavior = behavior;
            Damage = damage;
            TimelineJson = timelineJson;
        }
    }
    public enum PhaseAnchor { Frame, Start, Hit, Cue, End }
    public sealed class PhaseSpec
    {
        public string Name { get; }
        public string Text { get; }
        public PhaseAnchor Anchor { get; }
        public int Ordinal { get; }
        public string CueId { get; }
        public int Offset { get; }
        internal PhaseSpec(string name, string text, PhaseAnchor anchor, int ordinal, string cueId, int offset)
        {
            Name = name;
            Text = text;
            Anchor = anchor;
            Ordinal = ordinal;
            CueId = cueId;
            Offset = offset;
        }
    }
    public abstract class PlayerScript { public abstract int Ticks { get; } private protected PlayerScript() { } }
    internal sealed class StepEntry
    {
        public TrainingSequenceStep Step { get; }
        public bool TargetsDummy { get; }
        internal StepEntry(TrainingSequenceStep step, bool targetsDummy)
        {
            Step = step;
            TargetsDummy = targetsDummy;
        }
    }
    public sealed class StepScript : PlayerScript
    {
        internal IReadOnlyList<StepEntry> Steps { get; }
        public override int Ticks { get; }
        internal StepScript(IEnumerable<StepEntry> steps)
        {
            Steps = Array.AsReadOnly(steps.ToArray());
            Ticks = Steps.Sum(s => s.Step.Ticks);
        }
    }
    public sealed class PadScript : PlayerScript
    {
        public IReadOnlyList<GamepadState> Frames { get; }
        public override int Ticks => Frames.Count;
        internal PadScript(GamepadState[] frames) => Frames = Array.AsReadOnly(frames);
    }
    public sealed class MoveSpec
    {
        public string Id { get; }
        public SlotAddress Slot { get; }
        public byte Form { get; }
        public string Label { get; }
        public FacingSpec Facing { get; }
        public DummySpec Dummy { get; }
        public PlayerScript Script { get; }
        public IReadOnlyList<PhaseSpec> Phases { get; }
        internal MoveSpec(string id, SlotAddress slot, byte form, string label, FacingSpec facing, DummySpec dummy, PlayerScript script, IReadOnlyList<PhaseSpec> phases)
        {
            Id = id;
            Slot = slot;
            Form = form;
            Label = label;
            Facing = facing;
            Dummy = dummy;
            Script = script;
            Phases = phases;
        }

        public int Ticks => Script.Ticks;
    }
    public sealed class MoveRecipe
    {
        public string Path { get; }
        public string Sha256 { get; }
        public string Package { get; }
        public string Stage { get; }
        public FacingSpec Facing { get; }
        public DummySpec Dummy { get; }
        public ViewFraming Camera { get; }
        public IReadOnlyList<ViewSpec> Views { get; }
        public FrameSize Size { get; }
        public int Quality { get; }
        public int SettleTicks { get; }
        public IReadOnlyList<PhaseSpec> Phases { get; }
        public IReadOnlyList<MoveSpec> Moves { get; }
        internal MoveRecipe(string path, string sha256, string package, string stage, FacingSpec facing, DummySpec dummy, ViewFraming camera, IReadOnlyList<ViewSpec> views, FrameSize size, int quality, int settleTicks, IReadOnlyList<PhaseSpec> phases, IReadOnlyList<MoveSpec> moves)
        {
            Path = path;
            Sha256 = sha256;
            Package = package;
            Stage = stage;
            Facing = facing;
            Dummy = dummy;
            Camera = camera;
            Views = views;
            Size = size;
            Quality = quality;
            SettleTicks = settleTicks;
            Phases = phases;
            Moves = moves;
        }

        public MoveSpec Move(string id) => Moves.FirstOrDefault(m => m.Id == id)
            ?? throw new ArgumentException($"Unknown move '{id}'. Known ids: {string.Join(", ", Moves.Select(m => m.Id))}.");
        public static readonly IReadOnlyList<ViewSpec> DefaultViews = Array.AsReadOnly(new[]
        {
            new ViewSpec("behind", 0, 35), new ViewSpec("rear-left", 45, 35), new ViewSpec("left", 90, 35),
            new ViewSpec("front-left", 135, 35), new ViewSpec("front", 180, 35), new ViewSpec("front-right", -135, 35),
            new ViewSpec("right", -90, 35), new ViewSpec("rear-right", -45, 35), new ViewSpec("low", 0, 8), new ViewSpec("high", 0, 45)
        });
    }

    public static class MoveRecipeParser
    {
        internal static string Resolve(string path, bool output = false)
        {
            if (string.IsNullOrEmpty(path) || System.IO.Path.IsPathRooted(path) || path.Contains('\\')
                || path.Split('/').Any(s => s is "" or "." or "..")
                || !(path.StartsWith("tmp/", StringComparison.Ordinal) || !output && path.StartsWith("scenarios/moves/", StringComparison.Ordinal)))
                throw new ArgumentException("Use a repository-relative path below tmp/ or scenarios/moves/; outputs must be below tmp/.");
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../..", path));
            for (var item = new FileInfo(full) as FileSystemInfo; item != null; item = item is FileInfo f ? f.Directory : ((DirectoryInfo)item).Parent)
                if ((File.Exists(item.FullName) || Directory.Exists(item.FullName)) && (File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Recipe and output paths must not traverse symbolic links.");
            return full;
        }

        public static MoveRecipe Load(string path)
        {
            string full = Resolve(path);
            return Parse(File.ReadAllText(full), path, Hash(File.ReadAllBytes(full)));
        }

        internal static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        internal static MoveRecipe Parse(string json, string path, string fileHash = null)
        {
            try
            {
                JObject root;
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                {
                    root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new ArgumentException("Unexpected content after the recipe.");
                }
                Only(root, "package", "stage", "facing", "dummy", "camera", "views", "size", "quality", "settle", "phases", "moves");
                string package = Id(root["package"]), stage = Str(root["stage"]);
                if (!Regex.IsMatch(stage, "^[a-z0-9][a-z0-9._-]{0,63}$")) throw Error(root["stage"], "Invalid stage key.");
                FacingSpec facing = Facing(root["facing"]);
                var dummy = Dummy(root["dummy"], new DummySpec("fightguy", 2.5f, NpcAiMode.Idle, 0, null), true);
                var camera = root["camera"] == null ? new JObject() : Obj(root["camera"]);
                Only(camera, "distance", "fov", "look", "spread", "gameplayPitch");
                var framing = new ViewFraming(Num(camera["distance"], 8, 2, 40), Num(camera["fov"], 40, 5, 120),
                    Num(camera["look"], 0.3f, -3, 5), Num(camera["spread"], 0.6f, 0, 3), Num(camera["gameplayPitch"], 35, 0, 45));
                IReadOnlyList<ViewSpec> views = MoveRecipe.DefaultViews;
                if (root["views"] != null)
                {
                    var parsed = Arr(root["views"], 1, CaptureBudget.MaxViews).Select(t =>
                    {
                        var view = Obj(t); Only(view, "name", "yaw", "pitch");
                        return new ViewSpec(Id(view["name"]), Num(view["yaw"], 0, -360, 360), Num(view["pitch"], 35, -10, 89));
                    }).ToArray();
                    if (parsed.Select(v => v.Name).Distinct().Count() != parsed.Length) throw Error(root["views"], "View names must be unique.");
                    views = Array.AsReadOnly(parsed);
                }
                var size = ParseSize(root["size"] == null ? "960x540" : Str(root["size"]));
                int quality = Int(root["quality"], 88, 50, 100), settle = Int(root["settle"], 30, 30, 30);
                var phases = root["phases"] == null ? Phases(JArray.Parse("[{\"name\":\"wind-up\",\"at\":\"hit-6\"},{\"name\":\"peak\",\"at\":\"hit+1\"},{\"name\":\"fade\",\"at\":\"hit+16\"}]")) : Phases(root["phases"]);
                var moves = new List<MoveSpec>();
                foreach (var token in Arr(root["moves"], 1, 256))
                {
                    var item = Obj(token); Only(item, "id", "slot", "form", "label", "facing", "dummy", "input", "pad", "phases");
                    string id = Id(item["id"]), slotId = Str(item["slot"]);
                    if (!CanonicalSlotProjection.TryGet(slotId, out var slot)) throw Error(item["slot"], "Unknown canonical slot.");
                    if ((item["input"] != null) == (item["pad"] != null)) throw Error(item, "Provide exactly one of input or pad.");
                    PlayerScript script;
                    if (item["input"] != null)
                    {
                        var steps = (JArray)Arr(item["input"], 1, 600).DeepClone();
                        bool[] targets = steps.Select(t => t["targetEntityId"]?.Type == JTokenType.String && (string)t["targetEntityId"] == "dummy").ToArray();
                        for (int i = 0; i < steps.Count; i++) if (targets[i]) steps[i]["targetEntityId"] = "0";
                        var parsed = SlopArenaTrainingCommands.ParseSteps(steps.ToString(Formatting.None));
                        script = new StepScript(parsed.Select((s, i) => new StepEntry(s, targets[i])));
                    }
                    else
                    {
                        var patches = SlopArenaGamepadCommands.Parse(item["pad"].ToString(Formatting.None), out int duration);
                        script = new PadScript(SlopArenaGamepadCommands.Expand(patches, duration));
                    }
                    var moveDummy = Dummy(item["dummy"], dummy, false);
                    if (moveDummy.TimelineJson != null && SlopArenaGamepadCommands.ParseTarget(moveDummy.TimelineJson, 0, 0).Length != script.Ticks)
                        throw Error(item, "Dummy timeline ticks must equal player input length.");
                    moves.Add(new MoveSpec(id, slot, (byte)Int(item["form"], 0, 0, 1), item["label"] == null ? null : Str(item["label"]),
                        item["facing"] == null ? facing : Facing(item["facing"]), moveDummy, script,
                        item["phases"] == null ? phases : Phases(item["phases"])));
                }
                if (moves.Select(m => m.Id).Distinct().Count() != moves.Count) throw Error(root["moves"], "Move ids must be unique.");
                return new MoveRecipe(path, fileHash ?? Hash(Encoding.UTF8.GetBytes(json)), package, stage, facing, dummy, framing,
                    views, size, quality, settle, phases, Array.AsReadOnly(moves.ToArray()));
            }
            catch (Exception error) when (error is ArgumentException or JsonException or OverflowException)
            { throw new ArgumentException($"{path}: {error.Message} See scenarios/moves/README.md.", error); }
        }

        internal static FrameSize ParseSize(string text)
        {
            var match = Regex.Match(text, @"^(\d+)x(\d+)$");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int width) || !int.TryParse(match.Groups[2].Value, out int height))
                throw new ArgumentException("size must be even WxH, for example 960x540.");
            var size = new FrameSize(width, height);
            CaptureBudget.Fit(size, 1, 1);
            return size;
        }

        private static DummySpec Dummy(JToken token, DummySpec fallback, bool root)
        {
            if (token == null) return fallback;
            var item = Obj(token);
            Only(item, root ? new[] { "package", "distance", "behavior", "damage", "input" } : new[] { "distance", "behavior", "damage", "input" });
            string behavior = item["behavior"] == null ? fallback.Behavior.ToString() : Str(item["behavior"]);
            if (!Enum.TryParse(behavior, false, out NpcAiMode mode) || !Enum.IsDefined(typeof(NpcAiMode), mode)) throw Error(item, "Unsupported dummy behavior.");
            string timeline = item["input"] == null ? fallback.TimelineJson : Obj(item["input"]).ToString(Formatting.None);
            if (timeline != null) SlopArenaGamepadCommands.ParseTarget(timeline, 0, 0);
            return new DummySpec(item["package"] == null ? fallback.Package : Id(item["package"]), Num(item["distance"], fallback.Distance, 1, 25),
                mode, Int(item["damage"], fallback.Damage, 0, 300), timeline);
        }

        private static FacingSpec Facing(JToken token) => token == null || token.Type == JTokenType.String && (string)token == "spawn"
            ? new FacingSpec(true, 0) : new FacingSpec(false, Num(token, 0, -360, 360));

        private static IReadOnlyList<PhaseSpec> Phases(JToken token)
        {
            var phases = Arr(token, 1, 6).Select(t =>
            {
                var item = Obj(t); Only(item, "name", "at");
                string text = Str(item["at"]);
                var match = Regex.Match(text, @"^(start|end|hit(?:#[1-9]\d*)?|cue:[a-zA-Z0-9._-]+?|\d+)([+-]\d+)?$");
                if (!match.Success) throw Error(item["at"], "Invalid phase anchor.");
                string anchor = match.Groups[1].Value;
                int offset = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
                PhaseAnchor kind = anchor == "start" ? PhaseAnchor.Start : anchor == "end" ? PhaseAnchor.End
                    : anchor.StartsWith("hit") ? PhaseAnchor.Hit : anchor.StartsWith("cue:") ? PhaseAnchor.Cue : PhaseAnchor.Frame;
                int ordinal = kind == PhaseAnchor.Frame ? int.Parse(anchor) : anchor.StartsWith("hit#") ? int.Parse(anchor.Substring(4)) : 1;
                if (Math.Abs((long)offset) > 3600 || ordinal > 3600) throw Error(item["at"], "Phase value exceeds 3600.");
                return new PhaseSpec(Str(item["name"]), text, kind, ordinal, kind == PhaseAnchor.Cue ? anchor.Substring(4) : null, offset);
            }).ToArray();
            if (phases.Select(p => p.Name).Distinct().Count() != phases.Length) throw Error(token, "Phase names must be unique.");
            return Array.AsReadOnly(phases);
        }

        private static void Only(JObject item, params string[] names)
        {
            foreach (var property in item.Properties()) if (!names.Contains(property.Name)) throw Error(property, "Unknown field.");
        }
        private static JObject Obj(JToken token) => token as JObject ?? throw Error(token, "Expected an object.");
        private static JArray Arr(JToken token, int min, int max) => token is JArray array && array.Count >= min && array.Count <= max
            ? array : throw Error(token, $"Expected an array with {min}–{max} entries.");
        private static string Str(JToken token) => token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)token)
            ? (string)token : throw Error(token, "Expected a nonempty string.");
        private static string Id(JToken token)
        {
            string id = Str(token);
            if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9.-]{0,31}$")) throw Error(token, "Expected a stable lowercase id of at most 32 characters.");
            return id;
        }
        private static float Num(JToken token, float fallback, float min, float max)
        {
            if (token == null) return fallback;
            if (token.Type is not (JTokenType.Integer or JTokenType.Float)) throw Error(token, "Expected a number.");
            float value = (float)token;
            if (!float.IsFinite(value) || value < min || value > max) throw Error(token, $"Expected {min}–{max}.");
            return value;
        }
        private static int Int(JToken token, int fallback, int min, int max)
        {
            if (token == null) return fallback;
            if (token.Type != JTokenType.Integer) throw Error(token, "Expected an integer.");
            return (int)Num(token, fallback, min, max);
        }
        private static ArgumentException Error(JToken token, string message) => new($"{token?.Path ?? "recipe"}: {message}");
    }

    internal sealed class Coverage
    {
        public readonly Dictionary<byte, string> FormNames = new();
        public readonly Dictionary<byte, Dictionary<string, object>> Forms = new();
        public readonly Dictionary<string, CookedSlotDefinition> Slots = new();
        public CharacterDefinition Definition;
        public CharacterClass DummyClass;
    }
    internal static class PackageCheck
    {
        public static Coverage Check(MoveRecipe recipe)
        {
            var catalog = ClientSession.MatchContentCatalog;
            // Rebuilding development content destroys the animation catalogs used by the live match.
            if (!Application.isPlaying && !ClientSession.TryBuildLocalMatchCatalog(out catalog, out var error))
                throw new InvalidOperationException("Local catalog: " + error);
            if (catalog == null) throw new InvalidOperationException("Training has no installed match catalog.");
            var entry = catalog.ResolvePackage(recipe.Package) ?? throw new ArgumentException("Recipe package is not admitted: " + recipe.Package);
            var dummy = catalog.ResolvePackage(recipe.Dummy.Package) ?? throw new ArgumentException("Dummy package is not admitted: " + recipe.Dummy.Package);
            var coverage = new Coverage { Definition = entry.Definition, DummyClass = dummy.Definition.Class };
            for (byte form = 0; form <= (entry.Definition.AlternateForm != null ? 1 : 0); form++)
            {
                var def = entry.Definition.ForForm(form);
                coverage.FormNames[form] = def.DisplayName;
                coverage.Forms[form] = new Dictionary<string, object>();
                foreach (var address in CanonicalSlotProjection.All)
                {
                    var slot = def.GetCookedSlotAbility(CanonicalSlotProjection.ToWire(address), address.IsAirborne);
                    coverage.Forms[form][address.Id] = new { aliasOf = slot != null && slot.Id != address.Id ? slot.Id : null,
                        moves = recipe.Moves.Where(m => m.Form == form && m.Slot == address).Select(m => m.Id).ToArray() };
                }
            }
            foreach (var move in recipe.Moves)
            {
                var def = entry.Definition.ForForm(move.Form);
                var slot = def.GetCookedSlotAbility(CanonicalSlotProjection.ToWire(move.Slot), move.Slot.IsAirborne);
                if (def.Form != move.Form || slot == null) throw new ArgumentException($"Move '{move.Id}' is unavailable in form {move.Form}.");
                coverage.Slots[move.Id] = slot;
            }
            return coverage;
        }
    }

    internal static class TakeCompiler
    {
        public static TakePlan Compile(MoveRecipe recipe, MoveSpec move, Arranged arranged, TrainingMatch match,
            string viewFilter, FrameSize size, int quality, string output)
        {
            var views = recipe.Views.ToArray();
            if (viewFilter != null)
            {
                string[] names = viewFilter.Split(',');
                if (names.Distinct().Count() != names.Length || names.Any(n => !views.Any(v => v.Name == n))) throw new ArgumentException("Unknown or duplicate view name.");
                views = views.Where(v => names.Contains(v.Name)).ToArray();
            }
            PlayerDrive drive;
            if (move.Script is StepScript semantic)
            {
                var expanded = semantic.Steps.SelectMany(step => Enumerable.Range(0, step.Step.Ticks).Select(i => (step, i == 0))).ToArray();
                drive = new SemanticDrive { Input = tick =>
                {
                    var (entry, first) = expanded[tick];
                    var input = SlopArenaTrainingCommands.BuildInput(entry.Step, first,
                        SlopArenaTrainingCommands.ToWireYaw(match.GetCaptureState(SlopArena.Client.UI.MatchConfig.LocalEntityId).FacingYaw));
                    float yaw = arranged.FacingRadians;
                    (input.MoveX, input.MoveY) = Rotate(input.MoveX, input.MoveY, yaw);
                    input.Up = input.MoveY > 0.3f; input.Left = input.MoveX < -0.3f; input.Right = input.MoveX > 0.3f;
                    if (entry.Step.FacingYaw.HasValue) input.FacingYaw = AddYaw(input.FacingYaw, yaw);
                    if (entry.Step.AimYaw.HasValue) input.AimYaw = AddYaw(input.AimYaw, yaw);
                    else input.AimYaw = input.FacingYaw;
                    if (entry.TargetsDummy) input.TargetEntityId = (byte)match.SelectedNpcId;
                    return input;
                } };
            }
            else drive = new PadDrive { Frames = ((PadScript)move.Script).Frames };
            var dummy = move.Dummy.TimelineJson == null ? null : SlopArenaGamepadCommands.ParseTarget(move.Dummy.TimelineJson,
                arranged.FacingRadians, match.GetCaptureState(match.SelectedNpcId).FacingYaw);
            return new TakePlan { MoveId = move.Id, Ticks = move.Ticks, SettleTicks = recipe.SettleTicks, Player = drive,
                Dummy = dummy == null ? null : tick => dummy[tick], FacingRadians = arranged.FacingRadians, GameplayPitch = recipe.Camera.GameplayPitch,
                ViewDistance = recipe.Camera.Distance + recipe.Camera.Spread * move.Dummy.Distance, ViewFov = recipe.Camera.Fov,
                ViewLook = recipe.Camera.Look, FrameAhead = move.Dummy.Distance / 2,
                Views = views.Select(v => (v.Name, v.Yaw, v.Pitch)).ToArray(), Size = CaptureBudget.Fit(size, views.Length, move.Ticks),
                Quality = quality, OutputDirectory = output };
        }
        internal static (float X, float Y) Rotate(float x, float y, float yaw)
            => (x * Mathf.Cos(yaw) + y * Mathf.Sin(yaw), -x * Mathf.Sin(yaw) + y * Mathf.Cos(yaw));
        private static short AddYaw(short wire, float yaw) => SlopArenaTrainingCommands.ToWireYaw(wire * 0.01f * Mathf.Deg2Rad + yaw);
    }

    internal sealed class ResolvedPhase
    {
        public string name { get; }
        public string at { get; }
        public int? frame { get; }
        internal ResolvedPhase(string name, string at, int? frame)
        {
            this.name = name;
            this.at = at;
            this.frame = frame;
        }
    }
    internal static class PhaseResolver
    {
        public static TickRecord Start(MoveSpec move, TakeResult take) => take.Records.FirstOrDefault(r =>
            r.Fighter.State == ActionState.Attacking && r.Fighter.Form.Active == move.Form
            && r.Fighter.AttackSlot == CanonicalSlotProjection.ToWire(move.Slot));

        public static IReadOnlyList<ResolvedPhase> Resolve(IReadOnlyList<PhaseSpec> phases, MoveSpec move, TakeResult take, List<string> problems)
        {
            var start = Start(move, take);
            if (start == null) problems.Add("Declared slot never started: " + move.Slot.Id);
            else if (start.GroundedBefore == move.Slot.IsAirborne) problems.Add("Declared slot started in the wrong ground/air state: " + move.Slot.Id);
            var hits = take.Records.SelectMany(r => r.Hits.Where(h => h.OwnerEntityId == r.Fighter.EntityId).Select(_ => r.Frame)).ToArray();
            return phases.Select(phase =>
            {
                int? anchor = phase.Anchor switch
                {
                    PhaseAnchor.Frame => phase.Ordinal,
                    PhaseAnchor.Start => start?.Frame,
                    PhaseAnchor.End => take.Records.Count - 1,
                    PhaseAnchor.Hit => phase.Ordinal <= hits.Length ? hits[phase.Ordinal - 1] : (int?)null,
                    PhaseAnchor.Cue => take.Records.Where(r => r.Cues.Any(c => c.EntityId == r.Fighter.EntityId && c.PresentationId == phase.CueId)).Select(r => (int?)r.Frame).FirstOrDefault(),
                    _ => null
                };
                if (!anchor.HasValue) { problems.Add($"Phase '{phase.Name}' unresolved: {phase.Text}"); return new ResolvedPhase(phase.Name, phase.Text, null); }
                int frame = anchor.Value + phase.Offset;
                int clamped = Mathf.Clamp(frame, 0, take.Records.Count - 1);
                if (clamped != frame) problems.Add($"Phase '{phase.Name}' clamped from {frame} to {clamped}.");
                return new ResolvedPhase(phase.Name, phase.Text, clamped);
            }).ToArray();
        }
    }
}
#endif
