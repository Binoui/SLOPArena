#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Client;
using SlopArena.Client.Animation;
using SlopArena.Client.Combat;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using static SlopArena.EditorTools.PresentationFx;
using static SlopArena.EditorTools.ToonShapes;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Nyx's combat presentation: a cool motif per move whose shape, direction and reach agree with
    /// the move's hitbox, never the hitbox drawn as a field. Hitbox geometry comes from the compiled
    /// character.json and limb, pole and phone paths from her clips, sampled as the cook bakes poses;
    /// both only size and place the motifs.
    /// </summary>
    public static class NyxPresentationBuilder
    {
        private const string Root = "Assets/CharacterPackages/nyx";
        private const string Prefix = "presentation.nyx.";
        private const float TrailSeconds = .2f;
        private const int TrailSteps = 4;
        // Ticks of swing before a hitbox opens that its trail starts with (NyxAttackCue.SweepTrail.Lead).
        private const int TrailLead = 2;
        // The shock ring texture's dark outer rim, as a fraction of the particle's half size.
        private const float RingOuter = .98f;
        private static PresentationBuilderKit _kit;
        private static Materials _materials;
        private static CharacterDefinition _definition;
        private static BakedAnimationData _baked;
        private static GameObject _poleTrail;
        private static readonly Color ShadowRim = new Color32(28, 14, 43, 255);
        private static readonly Color Violet = new Color32(91, 42, 134, 255);
        private static readonly Color Lavender = new Color32(217, 200, 240, 255);
        private static readonly Color PinkRim = new Color32(87, 13, 52, 255);
        private static readonly Color Pink = new Color32(255, 79, 163, 255);
        private static readonly Dictionary<string, List<CueWindow>> Cues = new(StringComparer.Ordinal);
        private static readonly SortedDictionary<string, object> ReachReport = new(StringComparer.Ordinal);
        private static int _thrustTicks, _rotorStartupTicks;
        private static float _graspRadius, _graspHeight, _crowRadius, _handRadius, _handHeight;
        private static readonly Color Ink = new Color32(46, 20, 72, 255);
        // Cues that run from tick 0 through a move's startup, not over one of its active phases (ADR-0051 section 11).
        private static readonly HashSet<string> StartupCues = new(StringComparer.Ordinal) { Prefix + "cast-aura", Prefix + "ring-light-charge" };
        // Nyx's shadow spells show the cast effect; Bat Scatter and Going Live do not.
        private static readonly string[] CastSlots = { "ground.1", "ground.2", "ground.3", "air.1", "air.2", "air.3", "ground.A", "ground.R" };
        private static string _phoneHand = "bone.right-hand";

        private sealed class Materials
        {
            public Material ShadowBurst, PinkBurst, ShadowBand, PinkBand, Heart, Sparkle, Shard, Feather, Bat;
            public Material ClawBlade, PinkBlade, ShadowWhip, ClawMarks, LightRay, Viewfinder, PoleSmear, ShockRing, ShadowShockRing, Glint, TrailDots;
            public Material InkPool, DropLine;
        }
        private sealed class CueWindow
        {
            public int Tick, Duration;
            public CookedSlotDefinition Move;
            public readonly List<HitWindow> Hitboxes = new();
        }
        private sealed class HitWindow
        {
            public int Tick, End;
            public HitboxEvent Geometry;
            public CharacterDefinition Definition;
            public CookedSlotDefinition Move;
        }
        private readonly struct Blade
        {
            public readonly Func<float, Vector3> At;
            public readonly float Width;
            public readonly Vector3 Hint;
            public Blade(Func<float, Vector3> at, float width, Vector3? hint = null)
            {
                At = at;
                Width = width;
                Hint = hint ?? Vector3.up;
            }
        }
        private sealed class Pose
        {
            public int Tick;
            public readonly Dictionary<string, Vector3> At = new(StringComparer.Ordinal);
        }

        [CliCommand("sloparena.presentation.nyx.build", "Build Nyx's roster particles and trails sized from Shared hitbox geometry and her measured clips, preserving her prop and crow models.",
            MainThreadRequired = true, Tags = new[] { "presentation", "nyx" })]
        public static object Build()
        {
            SynchronizeAttackCues();
            LoadCueData();
            if (MeasureStartupHands()) LoadCueData();
            ReachReport.Clear();
            using var kit = new PresentationBuilderKit("nyx", "Nyx");
            _kit = kit;
            _materials = BuildMaterials();
            _poleTrail = PoleTrailStyle();
            var built = new List<(string id, GameObject prefab)>();
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(Root + "/CharacterAssetCatalog.asset");
            foreach (string id in new[] { "pole-held", "phone-raise", "phone-drop" })
            {
                string semantic = Prefix + "props." + id;
                var binding = catalog.Presentations.Single(x => x.SemanticId == semantic);
                if (binding.Prefab == null) throw new InvalidOperationException("Missing prop " + semantic);
                built.Add((semantic, binding.Prefab));
            }
            void Bind(string id, GameObject prefab) => built.Add((Prefix + id, prefab));
            Bind("shadow-claws", ShadowClaws());
            Bind("rising-hands", GraspingHands());
            Bind("floor-lash", ShadowLash());
            Bind("shadow-ring", ShadowRing());
            Bind("up-claw", UpClaw());
            Bind("spike-fist", ShadowFist());
            Bind("phone-slaps", PhoneSlaps());
            Bind("pole-uppercut", PoleSwing("NyxPoleUppercut", "pole-uppercut", arc: true));
            Bind("stomp-impact", StompKick());
            Bind("spin-arc", SpinningSlap());
            Bind("pole-overhead", PoleSwing("NyxPoleOverhead", "pole-overhead"));
            Bind("dropkick-spike", Dropkick());
            Bind("ring-light", RingLight());
            Bind("going-live", SwapBurst("NyxGoingLive", "going-live", true));
            Bind("logging-off", SwapBurst("NyxLoggingOff", "logging-off", false));
            Bind("selfie-catch", SelfieCatch());
            Bind("rotor-arcs", RingLightRise());
            Bind("bat-scatter", BatScatter());
            Bind("selfie-pop", SelfiePop());
            Bind("selfie-release", SelfieRelease());
            Bind("shadow-hit", Contact("NyxShadowHit", false));
            Bind("pink-hit", Contact("NyxPinkHit", true));
            Bind("crow-pop", CrowPop());
            Bind("crow-launch", CrowLaunch());
            var crow = Crow();
            Bind("crow", crow);
            kit.BindProjectile("ground.A", crow);
            kit.BindProjectile("air.A", crow);
            var drag = GraspDrag();
            Bind("grasp-drag", drag);
            Bind("grasp-catch", GraspCatch(drag));
            Bind("grasp-warning", GraspWarning());
            Bind("grasp-pop", GraspPop());
            Bind("crow-peck", CrowPeck());
            Bind("crow-scatter", CrowScatter());
            Bind("cast-aura", CastAura());
            Bind("ring-light-charge", RingLightCharge());
            return kit.Finish(built, new { reach = ReachReport, toolkit = "PresentationFx + ToonShapes.Bands" });
        }

        private static void SynchronizeAttackCues()
        {
            string path = Root + "/character.json";
            var character = JsonNode.Parse(File.ReadAllText(path));
            bool changed = false;
            foreach (var slot in character["slots"].AsArray().Concat(character["alternateForm"]["slots"].AsArray()))
                foreach (var stage in slot["timeline"]["stages"].AsArray())
                {
                    var operations = stage["operations"].AsArray();
                    var emits = operations.Where(op => (string)op["kind"] == "emitPresentation"
                        && !((string)op["presentationId"]).StartsWith(Prefix + "props.", StringComparison.Ordinal)
                        && !StartupCues.Contains((string)op["presentationId"])).OrderBy(op => (int)op["tick"]).ToArray();
                    foreach (var startup in operations.Where(op => (string)op["kind"] == "emitPresentation"
                                 && StartupCues.Contains((string)op["presentationId"])).ToArray())
                    {
                        int window = (string)startup["presentationId"] == Prefix + "cast-aura" ? LastActiveTick(operations) : FirstActiveTick(operations);
                        if ((int)startup["tick"] != 0) { startup["tick"] = 0; changed = true; }
                        if ((int)startup["placement"]["durationTicks"] != window) { startup["placement"]["durationTicks"] = window; changed = true; }
                    }
                    var groups = operations.Where(op => (string)op["kind"] == "spawnHitbox")
                        .GroupBy(op => (int)op["tick"]).OrderBy(group => group.Key).ToArray();
                    // One cue over every active phase (Ring Light Rise's rotor and its four pulses) stays as authored.
                    bool spansMove = emits.Length == 1 && groups.Length > 1 && (int)emits[0]["tick"] <= groups[0].Key
                        && (int)emits[0]["tick"] + (int)emits[0]["placement"]["durationTicks"]
                            >= groups.Max(group => group.Key + group.Max(op => (int)op["hitbox"]["durationTicks"]));
                    if (!spansMove && groups.Length > 0 && groups.Length != emits.Length)
                        throw new InvalidOperationException("Attack cue count differs from its active phases: " + slot["id"]);
                    for (int phase = 0; !spansMove && phase < groups.Length; phase++)
                    {
                        var group = groups[phase];
                        var emit = emits[phase];
                        int duration = group.Max(op => (int)op["hitbox"]["durationTicks"]);
                        if ((int)emit["tick"] != group.Key) { emit["tick"] = group.Key; changed = true; }
                        if ((int)emit["placement"]["durationTicks"] < duration)
                        { emit["placement"]["durationTicks"] = duration; changed = true; }
                    }
                    foreach (var capability in operations.Where(op => (string)op["kind"] == "startCapability").ToArray())
                    {
                        if ((string)capability["capabilityId"] != "slop.ability.command-grab.v1") continue;
                        var parameters = capability["parameters"];
                        int tick = (int)capability["tick"] + (int)parameters["captureTick"];
                        int duration = (int)parameters["capture"]["durationTicks"];
                        var emit = emits.FirstOrDefault(op => (string)op["presentationId"] == Prefix + "selfie-catch");
                        if (emit == null)
                        {
                            emit = new JsonObject { ["kind"] = "emitPresentation", ["unit"] = "ticks", ["tick"] = tick,
                                ["presentationId"] = Prefix + "selfie-catch", ["placement"] = new JsonObject {
                                    ["attachmentMode"] = "bone", ["boneId"] = "bone.hips", ["durationTicks"] = duration } };
                            int index = 0;
                            while (index < operations.Count && (int)operations[index]["tick"] <= tick) index++;
                            operations.Insert(index, emit); changed = true;
                        }
                        if ((int)emit["tick"] != tick) { emit["tick"] = tick; changed = true; }
                        if ((int)emit["placement"]["durationTicks"] != duration)
                        { emit["placement"]["durationTicks"] = duration; changed = true; }
                    }
                }
            if (!character["presentationIds"].AsArray().Any(id => (string)id == Prefix + "selfie-catch"))
            { character["presentationIds"].AsArray().Add(Prefix + "selfie-catch"); changed = true; }
            if (changed) File.WriteAllText(path, character.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }) + "\n");
        }

        /// <summary>The tick after a move's last active one: hitboxes, the crow's launch, the placed catch's window.</summary>
        private static int LastActiveTick(JsonArray operations) => operations.Max(op => (string)op["kind"] switch
        {
            "spawnHitbox" => (int)op["tick"] + (int)op["hitbox"]["durationTicks"],
            "spawnProjectile" => (int)op["tick"] + 1,
            "startCapability" when op["parameters"]["atTarget"] is { } at => (int)op["tick"] + (int)op["parameters"]["throwTick"]
                + (int)at["warningTicks"] + (int)at["catchTicks"],
            _ => 0,
        });

        private static int FirstActiveTick(JsonArray operations) => operations.Where(op => (string)op["kind"] == "spawnHitbox")
            .Select(op => (int)op["tick"]).DefaultIfEmpty(0).Min();

        /// <summary>
        /// Puts the cast effect on the hand nearer each spell's first contact, and the ring-light charge on the hand
        /// that holds her phone, measured from her clips. True when character.json changed.
        /// </summary>
        private static bool MeasureStartupHands()
        {
            string path = Root + "/character.json";
            var character = JsonNode.Parse(File.ReadAllText(path));
            bool changed = false;
            string Nearer(Pose pose, Vector3 point) => (pose.At["RightHand"] - point).sqrMagnitude <= (pose.At["LeftHand"] - point).sqrMagnitude
                ? "bone.right-hand" : "bone.left-hand";
            void SetBone(JsonNode slot, string id, string bone)
            {
                foreach (var op in slot["timeline"]["stages"].AsArray().SelectMany(stage => stage["operations"].AsArray()))
                    if ((string)op["presentationId"] == Prefix + id && (string)op["placement"]["boneId"] != bone)
                    { op["placement"]["boneId"] = bone; changed = true; }
            }
            foreach (var slot in character["slots"].AsArray().Where(slot => CastSlots.Contains((string)slot["id"])))
            {
                var move = _definition.ForForm(0).CookedSlots.First(m => m.Id == (string)slot["id"]);
                var (tick, point) = FirstContact(move, _definition.ForForm(0));
                SetBone(slot, "cast-aura", Nearer(SampleRig(move, tick, tick + 1).Poses[0], point));
            }
            var ring = character["alternateForm"]["slots"].AsArray().First(slot => (string)slot["id"] == "ground.A");
            var ringMove = _definition.ForForm(1).CookedSlots.First(m => m.Id == "ground.A");
            var start = SampleRig(ringMove, 0, 1).Poses[0];
            _phoneHand = Nearer(start, start.At["phone"]);
            SetBone(ring, "ring-light-charge", _phoneHand);
            if (changed) File.WriteAllText(path, character.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }) + "\n");
            return changed;
        }

        /// <summary>A move's first contact tick and where it happens, in the cue frame: its first hitbox, else its spell's reach.</summary>
        private static (int Tick, Vector3 Point) FirstContact(CookedSlotDefinition move, CharacterDefinition definition)
        {
            foreach (var op in move.Timeline.Stages[0].Operations.OrderBy(op => op.Tick))
                switch (op)
                {
                    case CookedSpawnHitboxOperation hit:
                        return (op.Tick, Resolve(new HitWindow { Tick = op.Tick, End = op.Tick + hit.Hitbox.DurationTicks,
                            Geometry = CookedCharacterRuntimeAdapter.ToHitboxEvent(hit.Hitbox), Definition = definition, Move = move }).Start);
                    case CookedSpawnProjectileOperation projectile:
                        return (op.Tick, new Vector3(projectile.Projectile.LaunchOffsetX, projectile.Projectile.LaunchOffsetY, projectile.Projectile.LaunchOffsetZ));
                    case CookedStartCapabilityOperation { Parameters: CookedTetherCapabilityParameters { AtTarget: { } at } tether }:
                        return (op.Tick + tether.ThrowTick + at.WarningTicks, new Vector3(0f, .4f, 2f));
                }
            return (0, Vector3.forward);
        }

        private static void LoadCueData()
        {
            Cues.Clear();
            _thrustTicks = _rotorStartupTicks = 0;
            _graspRadius = _graspHeight = _crowRadius = _handRadius = _handHeight = 0f;
            var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Root + "/package.json"), File.ReadAllText(Root + "/character.json"));
            if (compiled.HasErrors || compiled.CookedPackage == null)
                throw new InvalidOperationException(string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
            _definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage);
            var resolution = LocalContentResolver.CreateDefault().ResolveCookedPackage("nyx");
            if (!resolution.Success || resolution.Requirement == null) throw new InvalidOperationException("Nyx has no verified pose package.");
            var saved = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(resolution.RootPath, "nyx"), resolution.Requirement);
            if (!saved.IsValid || saved.BakedAnimation == null) throw new InvalidOperationException("Nyx's verified pose package is unavailable.");
            _baked = saved.BakedAnimation;
            CueWindow Add(string id, int tick, int duration, CookedSlotDefinition move)
            {
                if (id == null || !id.StartsWith(Prefix, StringComparison.Ordinal)) return null;
                id = id.Substring(Prefix.Length);
                if (!Cues.TryGetValue(id, out var list)) Cues[id] = list = new List<CueWindow>();
                var cue = new CueWindow { Tick = tick, Duration = duration, Move = move };
                list.Add(cue);
                return cue;
            }
            using var document = JsonDocument.Parse(File.ReadAllText(Root + "/character.json"));
            var character = document.RootElement;
            for (byte form = 0; form <= 1; form++)
            {
                var definition = _definition.ForForm(form);
                var authored = form == 0 ? character.GetProperty("slots") : character.GetProperty("alternateForm").GetProperty("slots");
                var ids = authored.EnumerateArray().Select(slot => slot.GetProperty("id").GetString()).ToHashSet();
                foreach (var move in definition.CookedSlots.Where(move => ids.Contains(move.Id)))
                {
                    var windows = new List<(string Id, CueWindow Cue)>();
                    var hits = new List<HitWindow>();
                    int stageStart = 0;
                    foreach (var stage in move.Timeline.Stages)
                    {
                        foreach (var op in stage.Operations)
                        {
                            int tick = stageStart + op.Tick;
                            if (op is CookedSpawnHitboxOperation hit)
                                hits.Add(new HitWindow { Tick = tick, End = tick + hit.Hitbox.DurationTicks,
                                    Geometry = CookedCharacterRuntimeAdapter.ToHitboxEvent(hit.Hitbox), Definition = definition, Move = move });
                            if (op is CookedSpawnProjectileOperation projectile) _crowRadius = projectile.Projectile.Radius;
                            if (op is CookedStartCapabilityOperation capability)
                                switch (capability.Parameters)
                                {
                                    case CookedThrustCapabilityParameters thrust:
                                        _thrustTicks = thrust.ThrustTicks;
                                        break;
                                    case CookedAimedRecoveryCapabilityParameters recovery:
                                        _rotorStartupTicks = recovery.StartupTicks;
                                        break;
                                    case CookedCommandGrabCapabilityParameters grab:
                                        hits.Add(new HitWindow { Tick = tick + grab.CaptureTick,
                                            End = tick + grab.CaptureTick + grab.Capture.DurationTicks,
                                            Geometry = CookedCharacterRuntimeAdapter.ToHitboxEvent(grab.Capture),
                                            Definition = definition, Move = move });
                                        break;
                                    case CookedTetherCapabilityParameters { AtTarget: { } at } tether:
                                        int warn = tick + tether.ThrowTick;
                                        Add(at.WarningPresentationId, warn, at.WarningTicks, move);
                                        Add(at.CatchPresentationId, warn + at.WarningTicks, at.CatchTicks, move);
                                        _graspRadius = at.Radius;
                                        _graspHeight = at.Height;
                                        if (at.Unmarked is { } hand)
                                        {
                                            Add(hand.PresentationId, tick + hand.RiseTick, hand.ActiveTicks, move);
                                            _handRadius = hand.Radius;
                                            _handHeight = hand.Height;
                                        }
                                        break;
                                }
                            if (op is CookedEmitPresentationOperation emit)
                            {
                                var cue = Add(emit.PresentationId, tick, emit.Placement.DurationTicks, move);
                                if (cue != null) windows.Add((emit.PresentationId.Substring(Prefix.Length), cue));
                            }
                        }
                        stageStart += stage.DurationTicks;
                    }
                    foreach (var hit in hits)
                    {
                        var eligible = windows.Where(w => !w.Id.StartsWith("props.", StringComparison.Ordinal)
                            && !StartupCues.Contains(Prefix + w.Id)).ToArray();
                        if (eligible.Length == 0) throw new InvalidOperationException("No attack cue for " + move.Id);
                        var cue = eligible.OrderBy(w => Mathf.Abs(w.Cue.Tick - hit.Tick)).First().Cue;
                        if (hit.Tick < cue.Tick || hit.End > cue.Tick + cue.Duration)
                            throw new InvalidOperationException($"{move.Name}: hitbox {hit.Tick}-{hit.End} outlives cue {cue.Tick}-{cue.Tick + cue.Duration}; retime its emitPresentation and rebuild.");
                        cue.Hitboxes.Add(hit);
                    }
                }
            }
            foreach (var list in Cues.Values) list.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        }

        private static List<CueWindow> Windows(string id) => Cues.TryGetValue(id, out var list) ? list
            : throw new InvalidOperationException("No cue for " + Prefix + id);

        private static HitWindow FirstHit(string id) => Windows(id).SelectMany(w => w.Hitboxes).First();

        private static int Duration(string id) => Windows(id).Min(w => w.Duration);

        /// <summary>A hitbox at its spawn tick in the cue frame (capsule centre, facing +Z); a sphere has End == Start.</summary>
        private static (Vector3 Start, Vector3 End, float Radius) Resolve(HitWindow hit)
        {
            var state = new CharacterState { AttackElapsedTicks = (ushort)hit.Tick, AnimIndex = 0, IsGrounded = !hit.Move.IsAir };
            HitboxGeometry.ResolvePositions(state, hit.Geometry, _baked, hit.Definition, hit.Move, 0,
                out float x, out float y, out float z, out float ex, out float ey, out float ez);
            var start = new Vector3(x, y, z);
            return (start, hit.Geometry.Shape == HitboxShape.Capsule ? new Vector3(ex, ey, ez) : start, hit.Geometry.Radius);
        }

        private static float CapsuleDistance(Vector3 p, Vector3 a, Vector3 b, float radius)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude - radius;
        }

        /// <summary>How far along a ray from <paramref name="from"/> stays inside the capsule (never less than <paramref name="least"/>).</summary>
        private static float FarInside(Vector3 from, Vector3 direction, Vector3 a, Vector3 b, float radius, float least)
        {
            float far = least, outside = 0f;
            for (float s = least; s < 3f && outside < .25f; s += .01f)
                if (CapsuleDistance(from + direction * s, a, b, radius) <= 0f) { far = s; outside = 0f; }
                else outside += .01f;
            return far;
        }

        /// <summary>
        /// Records how a motif's mesh sits against its hitboxes: the farthest any vertex pokes out of
        /// them, and how far forward the motif and the hitboxes reach. The build returns the table.
        /// </summary>
        private static void Report(string id, Mesh mesh, Vector3 pivot, IEnumerable<(Vector3 A, Vector3 B, float Radius)> hitboxes)
        {
            var shapes = hitboxes.ToArray();
            var points = mesh.vertices.Select(v => v + pivot).ToArray();
            float outside = points.Max(p => shapes.Min(h => CapsuleDistance(p, h.A, h.B, h.Radius)));
            ReachReport[id + "/" + mesh.name] = new
            {
                outside = Math.Round(Math.Max(0f, outside), 3),
                motifFarZ = Math.Round(points.Max(p => p.z), 2),
                hitboxFarZ = Math.Round(shapes.Max(h => Mathf.Max(h.A.z, h.B.z) + h.Radius), 2),
                motifTopY = Math.Round(points.Max(p => p.y), 2),
                hitboxTopY = Math.Round(shapes.Max(h => Mathf.Max(h.A.y, h.B.y) + h.Radius), 2),
            };
        }

        private static Func<float, Vector3> Bezier(Vector3 a, Vector3 b, Vector3 c)
            => t => (1f - t) * (1f - t) * a + 2f * (1f - t) * t * b + t * t * c;

        /// <summary>A point on the floor-level circle at <paramref name="degrees"/> clockwise from +Z, seen from above.</summary>
        private static Vector3 Around(float degrees) => new(Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad));

        /// <summary>
        /// Tapered blades along curves, each drawn as two crossed strips so it reads from every yaw at
        /// the match camera's pitch; U runs root to tip and V across. Vertices are relative to
        /// <paramref name="pivot"/>. A strip spans the curve's tangent and its cross with the blade's hint.
        /// </summary>
        private static Mesh BladeMesh(string name, IEnumerable<Blade> blades, Vector3 pivot = default, bool colored = false)
        {
            const int segments = 24;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            foreach (var blade in blades)
                for (int strip = 0; strip < 2; strip++)
                {
                    int first = vertices.Count;
                    for (int i = 0; i <= segments; i++)
                    {
                        float t = i / (float)segments;
                        var tangent = (blade.At(Mathf.Min(1f, t + .01f)) - blade.At(Mathf.Max(0f, t - .01f))).normalized;
                        var flat = Vector3.Cross(tangent, blade.Hint);
                        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.Cross(tangent, Vector3.forward).normalized;
                        var across = strip == 0 ? flat : Vector3.Cross(tangent, flat).normalized;
                        vertices.Add(blade.At(t) - pivot - across * blade.Width * .5f);
                        vertices.Add(blade.At(t) - pivot + across * blade.Width * .5f);
                        uvs.Add(new Vector2(t, 0f));
                        uvs.Add(new Vector2(t, 1f));
                        if (i == 0) continue;
                        int q = first + (i - 1) * 2;
                        triangles.AddRange(new[] { q, q + 2, q + 1, q + 1, q + 2, q + 3 });
                    }
                }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            if (colored) mesh.SetColors(Enumerable.Repeat((Color32)Color.white, vertices.Count).ToList());
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        /// <summary>One mesh particle for <paramref name="life"/> seconds in the cue frame, scaled per axis around its position.</summary>
        private static ParticleSystem MeshShape(GameObject parent, string name, Material material, Mesh mesh, float life, Vector3 at,
            AnimationCurve x, AnimationCurve y, AnimationCurve z, float fade)
        {
            var ps = Particles(parent, name, material, life, ParticleSystemSimulationSpace.Local);
            ps.transform.localPosition = at;
            Burst(ps, 1);
            var main = ps.main;
            main.startLifetime = life + 1f / 60f;
            main.startSize3D = true;
            main.startSizeX = 1f;
            main.startSizeY = 1f;
            main.startSizeZ = 1f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = true;
            size.x = new ParticleSystem.MinMaxCurve(1f, x);
            size.y = new ParticleSystem.MinMaxCurve(1f, y);
            size.z = new ParticleSystem.MinMaxCurve(1f, z);
            Fade(ps, fade);
            return ps;
        }

        /// <summary>
        /// Full size, with a slight overshoot, by the first attack tick a cue shows (MeshShape lives
        /// <paramref name="life"/> plus a tick), then holds. A hit on the first active tick freezes
        /// the attack clock there for the whole Hitstop, so anything still growing reads stunted.
        /// </summary>
        private static AnimationCurve Snap(float life, float from = .6f)
        {
            float tick = Mathf.Clamp(1f / 60f / (life + 1f / 60f), .001f, .3f);
            return Curve((0f, from), (tick, 1.04f), (2f * tick, 1f), (1f, 1f));
        }

        private static AnimationCurve Flat() => Curve((0f, 1f), (1f, 1f));

        /// <summary>Turns a mesh particle about its own Y axis at <paramref name="radiansPerSecond"/>, scaled by <paramref name="ramp"/>.</summary>
        private static void SpinY(ParticleSystem ps, float radiansPerSecond, AnimationCurve ramp)
        {
            var main = ps.main;
            main.startRotation3D = true;
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            var none = Curve((0f, 0f), (1f, 0f));
            rotation.x = new ParticleSystem.MinMaxCurve(0f, none);
            rotation.y = new ParticleSystem.MinMaxCurve(radiansPerSecond, ramp);
            rotation.z = new ParticleSystem.MinMaxCurve(0f, none);
        }

        private static GameObject Child(GameObject parent, string name, Vector3 at = default)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = at;
            return child;
        }

        /// <summary>An attack-bound root on the attack clock with one beat over the cue's life.</summary>
        private static (GameObject Root, NyxAttackCue Cue, GameObject Content, int Duration) AttackRoot(string name, string id, string content)
        {
            var root = _kit.NewRoot(name, attackBound: true);
            var cue = root.AddComponent<NyxAttackCue>();
            int duration = Duration(id);
            var beat = Child(root, content);
            cue.Beats = new[] { new NyxAttackCue.Beat { Content = beat, EndAfter = duration } };
            return (root, cue, beat, duration);
        }

        // ── Real form ────────────────────────────────────────────────────────────

        /// <summary>
        /// Shadow Flick: three violet claw blades raked from her shoulder to the far end of the hitbox
        /// capsule, with shards torn off around the tips. Blade length, spacing and width come from
        /// the capsule; any blade that would rise out of the capsule is lowered into it. The blades
        /// snap out to full reach in a tick and fade as the hitbox ends.
        /// </summary>
        private static GameObject ShadowClaws()
        {
            var (root, _, rake, duration) = AttackRoot("NyxShadowClaws", "shadow-claws", "Claw rake");
            var (a, b, radius) = Resolve(FirstHit("shadow-claws"));
            float life = duration / 60f;
            var forward = (b - a).normalized;
            var side = Vector3.Cross(Vector3.up, forward).normalized;
            var up = Vector3.Cross(forward, side);
            var blades = new List<Blade>();
            for (int claw = -1; claw <= 1; claw++)
            {
                // Roots bunch at her shoulder; tips fan across the far cap, so the rake spans the capsule's height.
                float lift = (claw * .55f - .2f) * radius, swing = radius * .2f;
                var from = a - forward * radius * .5f + side * radius * .5f + up * radius * (.95f + claw * .25f);
                var to = b - side * swing + up * lift + forward * Mathf.Sqrt(radius * radius - lift * lift - swing * swing) * .97f;
                var bend = Vector3.Lerp(from, to, .5f) + side * radius * .45f + up * radius * .85f;
                float width = radius * (claw == 0 ? .62f : .52f);
                // The top claw rose 0.3 m out of the capsule by her shoulder; lower it until its visible blade
                // (past the texture's faded root) stays inside.
                for (int pass = 0; claw == 1 && pass < 12; pass++)
                {
                    var curve = Bezier(from, bend, to);
                    float over = Enumerable.Range(3, 22).Max(i => CapsuleDistance(curve(i / 24f) + up * width * .42f, a, b, radius));
                    if (over <= .04f) break;
                    from -= up * over * .5f;
                    bend -= up * over;
                }
                blades.Add(new Blade(Bezier(from, bend, to), width));
            }
            var mesh = BladeMesh("NyxShadowClawsMesh", blades);
            Report("shadow-claws", mesh, Vector3.zero, new[] { (a, b, radius) });
            MeshShape(rake, "Claw blades", _materials.ClawBlade, mesh, life, Vector3.zero, Flat(), Flat(),
                Curve((0f, .6f), (.12f, 1.04f), (.3f, 1f), (1f, 1f)), .55f);

            var shards = Particles(rake, "Torn shards", _materials.Shard, life, ParticleSystemSimulationSpace.Local);
            shards.transform.localPosition = b;
            Burst(shards, 5);
            var main = shards.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * .6f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(.16f, .24f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            Sphere(shards, radius * .7f);
            Spin(shards, 540f);
            Fade(shards, .45f);
            InFront(shards);
            return Save(root);
        }

        /// <summary>
        /// Grasping Hands: two clawed shadow hands, one per hitbox sphere, rise from below and spread
        /// their fingers to the top of the sphere, tips curling in over her head. Every finger stays
        /// inside its sphere.
        /// </summary>
        private static GameObject GraspingHands()
        {
            var (root, _, content, duration) = AttackRoot("NyxRisingHands", "rising-hands", "Rising hands");
            float life = duration / 60f;
            var spheres = Windows("rising-hands").SelectMany(w => w.Hitboxes).Select(Resolve).OrderBy(s => s.Start.x).ToArray();
            foreach (var (c, _, r) in spheres)
            {
                float side = Mathf.Sign(c.x);
                string name = side < 0 ? "Left hand" : "Right hand";
                var palm = c + Vector3.down * r * .72f;
                var blades = new List<Blade>();
                for (int finger = 0; finger < 5; finger++)
                {
                    // Finger 0 is the thumb, low on the side nearer her; the others fan up and curl toward her head.
                    float k = finger - 2.2f;
                    bool thumb = finger == 0;
                    var root0 = palm + Vector3.right * (thumb ? -side * r * .4f : (finger - 2.5f) * r * .18f) + Vector3.up * (thumb ? -r * .05f : r * .12f);
                    var tip = c + Vector3.up * r * (thumb ? .25f : .78f - Mathf.Abs(k) * .06f)
                        + Vector3.right * (thumb ? -side * r * .72f : (finger - 2.5f) * r * .22f - side * r * .3f)
                        + Vector3.forward * r * (thumb ? .2f : .1f);
                    var bend = Vector3.Lerp(root0, tip, .5f) + Vector3.right * side * r * .16f + Vector3.forward * r * .15f;
                    float width = r * (thumb ? .26f : .24f);
                    blades.Add(new Blade(InSphere(Bezier(root0, bend, tip), c, r - width * .5f), width, Vector3.forward));
                }
                var mesh = BladeMesh("NyxGraspingHand" + (side < 0 ? "Left" : "Right") + "Mesh", blades, palm);
                Report("rising-hands", mesh, palm, spheres.Select(s => (s.Start, s.End, s.Radius)));
                MeshShape(content, name, _materials.ClawBlade, mesh, life, palm, Snap(life, .8f), Snap(life, .15f), Snap(life, .8f), .6f);
                var shards = Particles(content, name + " shards", _materials.Shard, life, ParticleSystemSimulationSpace.Local);
                shards.transform.localPosition = c + Vector3.up * r * .6f;
                Burst(shards, 4);
                var main = shards.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(life * .5f, life * .8f);
                main.startSize = new ParticleSystem.MinMaxCurve(.13f, .2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 1.6f);
                main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                Sphere(shards, r * .3f);
                Spin(shards, 360f);
                Fade(shards, .5f);
                InFront(shards);
            }
            return Save(root);
        }

        /// <summary>Pulls a curve's points back inside a capsule of <paramref name="radius"/> from <paramref name="a"/> to <paramref name="b"/>.</summary>
        private static Func<float, Vector3> InCapsule(Func<float, Vector3> curve, Vector3 a, Vector3 b, float radius) => t =>
        {
            var p = curve(t);
            Vector3 ab = b - a;
            var axis = a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return axis + Vector3.ClampMagnitude(p - axis, radius);
        };

        /// <summary>Pulls a curve's points back inside a sphere of <paramref name="radius"/> around <paramref name="centre"/>.</summary>
        private static Func<float, Vector3> InSphere(Func<float, Vector3> curve, Vector3 centre, float radius) => t =>
        {
            var offset = curve(t) - centre;
            return centre + Vector3.ClampMagnitude(offset, radius);
        };

        /// <summary>
        /// Shadow Lash: a snaking shadow whip along the floor from her feet to where the capsule sinks
        /// into the floor, riding at half the capsule's height above it, cracking at the tip. It
        /// unrolls to full length in three ticks.
        /// </summary>
        private static GameObject ShadowLash()
        {
            var (root, _, content, duration) = AttackRoot("NyxFloorLash", "floor-lash", "Lash");
            var (a, b, r) = Resolve(FirstHit("floor-lash"));
            float life = duration / 60f, floor = _definition.ModelYOffset;
            var axis = b - a;
            float CapsuleTop(float z) => Mathf.Lerp(a.y, b.y, Mathf.Clamp01((z - a.z) / axis.z)) + r;
            // The far cap's last point at a few centimetres above the floor.
            float dy = floor + .05f - b.y;
            float tipZ = b.z + (dy < r ? Mathf.Sqrt(r * r - dy * dy) : 0f) - .05f;
            var start = new Vector3(0f, floor + .1f, a.z - r * .6f);
            Vector3 At(float t)
            {
                float z = Mathf.Lerp(start.z, tipZ, t);
                float height = Mathf.Max(.07f, (CapsuleTop(z) - floor) * .5f);
                return new Vector3(r * .55f * Mathf.Sin(t * Mathf.PI * 2.2f) * (1f - .45f * t), floor + height, z);
            }
            var mesh = BladeMesh("NyxFloorLashMesh", new[] { new Blade(At, r * 1.25f) }, start);
            Report("floor-lash", mesh, start, new[] { (a, b, r) });
            MeshShape(content, "Shadow whip", _materials.ShadowWhip, mesh, life, start, Flat(), Flat(), Snap(life, .08f), .6f);

            // The crack marks the far end of the reach from the first live tick.
            var crack = Child(content, "Whip crack", At(.97f));
            var star = Pop(crack, "Crack star", _materials.ShadowBurst, .95f, life, ParticleSystemScalingMode.Local);
            star.transform.localPosition = Vector3.up * .3f;
            var shards = Motifs(crack, "Crack shards", _materials.Shard, 6, .22f, life, 2.6f);
            shards.transform.localPosition = Vector3.up * .25f;
            return Save(root);
        }

        /// <summary>
        /// Shadow Ring: three violet crescents spin round her at the hit sphere's equator, their tips
        /// reaching its edge. They snap out in two ticks and turn once over the active window.
        /// </summary>
        private static GameObject ShadowRing()
        {
            var (root, _, content, duration) = AttackRoot("NyxShadowRing", "shadow-ring", "Ring");
            var (c, _, R) = Resolve(FirstHit("shadow-ring"));
            float life = duration / 60f, width = R * .26f;
            var blades = Enumerable.Range(0, 3).Select(i => new Blade(t =>
                Around(i * 120f + t * 105f) * Mathf.Lerp(R * .62f, R - width * .55f, Mathf.Sqrt(t)), width)).ToArray();
            var mesh = BladeMesh("NyxShadowRingMesh", blades);
            Report("shadow-ring", mesh, c, new[] { (c, c, R) });
            var ring = MeshShape(content, "Spinning crescents", _materials.ClawBlade, mesh, life, c, Snap(life), Flat(), Snap(life), .7f);
            SpinY(ring, Mathf.PI * 2f / life, Flat());
            var shards = Particles(content, "Flung shards", _materials.Shard, life, ParticleSystemSimulationSpace.Local);
            shards.transform.localPosition = c;
            Burst(shards, 6);
            var main = shards.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * .6f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(.15f, .22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            FlatCircle(shards, R * .85f);
            Spin(shards, 540f);
            Fade(shards, .5f);
            InFront(shards);
            return Save(root);
        }

        /// <summary>
        /// Up Claw: three claw blades rake up over the hit sphere from its front to its back, the way
        /// her hand sweeps, each inside the sphere and the middle one reaching its top.
        /// </summary>
        private static GameObject UpClaw()
        {
            var (root, _, content, duration) = AttackRoot("NyxUpClaw", "up-claw", "Rake");
            var (c, _, r) = Resolve(FirstHit("up-claw"));
            float life = duration / 60f, width = r * .36f;
            var blades = new List<Blade>();
            for (int claw = -1; claw <= 1; claw++)
            {
                float x = claw * r * .38f, rho = Mathf.Sqrt(r * r - x * x) - width * .5f;
                blades.Add(new Blade(t =>
                {
                    float angle = Mathf.Lerp(-35f, 155f, t) * Mathf.Deg2Rad;
                    return c + new Vector3(x, Mathf.Sin(angle) * rho, Mathf.Cos(angle) * rho);
                }, width, Vector3.right));
            }
            var mesh = BladeMesh("NyxUpClawMesh", blades, c);
            Report("up-claw", mesh, c, new[] { (c, c, r) });
            MeshShape(content, "Rising claws", _materials.ClawBlade, mesh, life, c, Snap(life), Snap(life), Snap(life), .55f);
            var shards = Motifs(content, "Raked shards", _materials.Shard, 5, .2f, life, 1.6f);
            shards.transform.localPosition = c + Vector3.up * r * .7f;
            return Save(root);
        }

        /// <summary>
        /// Shadow Fist: three violet talons drive down through the lower hit sphere to its floor, drawn
        /// toward the camera so her dress cannot hide them, with a burst star, a slam ring and shards
        /// driven down. Every talon stays inside the sphere.
        /// </summary>
        private static GameObject ShadowFist()
        {
            var (root, _, content, duration) = AttackRoot("NyxSpikeFist", "spike-fist", "Fist");
            var (c, _, r) = Resolve(FirstHit("spike-fist"));
            float life = duration / 60f;
            var talons = new List<Blade>();
            for (int i = -1; i <= 1; i++)
            {
                var top = c + new Vector3(i * r * .42f, r * .8f - Mathf.Abs(i) * r * .15f, -r * .1f);
                var tip = c + new Vector3(i * r * .2f, -r * .88f + Mathf.Abs(i) * r * .12f, r * .15f);
                var bend = Vector3.Lerp(top, tip, .5f) + new Vector3(i * r * .18f, 0f, r * .25f);
                float width = r * (i == 0 ? .5f : .42f);
                talons.Add(new Blade(InSphere(Bezier(top, bend, tip), c, r - width * .3f), width, Vector3.forward));
            }
            var mesh = BladeMesh("NyxSpikeFistMesh", talons, c);
            Report("spike-fist", mesh, c, new[] { (c, c, r) });
            var talon = MeshShape(content, "Shadow talons", _materials.ClawBlade, mesh, life, c, Snap(life), Snap(life, .3f), Snap(life), .6f);
            InFront(talon);
            // Low under her, the talons hide behind her dress from the match camera; draw them toward it.
            talon.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, 0f, .5f);
            var slam = Particles(content, "Slam ring", _materials.ShadowShockRing, life, ParticleSystemSimulationSpace.Local);
            slam.transform.localPosition = c + Vector3.down * r * .55f;
            Burst(slam, 1);
            var main = slam.main;
            main.startLifetime = life + 1f / 60f;
            main.startSize = 2f * r * .84f / RingOuter;
            slam.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeOverLife(slam, Snap(life, .5f));
            Fade(slam, .6f);
            var burst = Pop(content, "Knuckle burst", _materials.ShadowBurst, r * .95f, life * .7f, ParticleSystemScalingMode.Local);
            burst.transform.localPosition = c + Vector3.down * r * .62f;
            var shards = Motifs(content, "Fist shards", _materials.Shard, 6, .2f, life, 2.2f);
            shards.transform.localPosition = c + Vector3.down * r * .6f;
            return Save(root);
        }

        /// <summary>
        /// Bat Scatter: she bursts into flapping bats, a swarm circles her body through the thrust and
        /// sheds bats along her path, then converges into a second burst where she re-forms. It has
        /// no hitbox; the swarm is sized to her body.
        /// </summary>
        private static GameObject BatScatter()
        {
            var root = _kit.NewRoot("NyxBatScatter", attackBound: true);
            var cue = root.AddComponent<NyxAttackCue>();
            int thrust = _thrustTicks, reform = Math.Min(Duration("bat-scatter"), thrust + 12);
            var burst = Child(root, "Scatter burst");
            var star = Pop(burst, "Scatter star", _materials.ShadowBurst, 1.4f, .2f, ParticleSystemScalingMode.Local);
            star.transform.localPosition = Vector3.up * .1f;
            var outward = Bats(burst, "Bursting bats", 20, .45f, .4f, 3.2f);
            outward.transform.localPosition = Vector3.up * .1f;

            var swarm = Child(root, "Bat swarm");
            float swarmLife = thrust / 60f;
            var circling = Bats(swarm, "Circling bats", 24, .42f, swarmLife, .4f);
            circling.transform.localPosition = Vector3.up * .1f;
            Sphere(circling, .55f);
            var main = circling.main;
            main.startLifetime = swarmLife;
            var velocity = circling.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(-.4f, .4f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(5f, 7f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            var noise = circling.noise;
            noise.enabled = true;
            noise.strength = .4f;
            noise.frequency = 1.2f;
            var wake = Bats(swarm, "Shed bats", 1, .36f, .45f, .5f);
            main = wake.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;
            var emission = wake.emission;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
            emission.rateOverDistance = 4f;
            Sphere(wake, .35f);

            var reformed = Child(root, "Re-form");
            var converging = Bats(reformed, "Converging bats", 16, .4f, 8f / 60f, 0f);
            converging.transform.localPosition = Vector3.up * .1f;
            Sphere(converging, .95f);
            main = converging.main;
            main.startSpeed = -6f;
            var pop = Pop(reformed, "Re-form star", _materials.ShadowBurst, 1.2f, (reform - thrust) / 60f, ParticleSystemScalingMode.Local);
            pop.transform.localPosition = Vector3.up * .1f;
            var delay = pop.main;
            delay.startDelay = 6f / 60f;
            cue.Beats = new[]
            {
                new NyxAttackCue.Beat { Content = burst, EndAfter = 12 },
                new NyxAttackCue.Beat { Content = swarm, EndAfter = thrust },
                new NyxAttackCue.Beat { Content = reformed, StartAfter = thrust, EndAfter = reform },
            };
            return Save(root);
        }

        /// <summary>Flapping bat billboards from the two-frame bat sheet, bursting out at <paramref name="speed"/>.</summary>
        private static ParticleSystem Bats(GameObject root, string name, int count, float size, float life, float speed)
        {
            var bats = Motifs(root, name, _materials.Bat, count, size, life, speed);
            var rotation = bats.rotationOverLifetime;
            rotation.enabled = false;
            var main = bats.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.25f, .25f);
            SizeOverLife(bats, Curve((0f, .7f), (.15f, 1f), (.8f, 1f), (1f, .4f)));
            FlickerFrames(bats, 2, 1, 16f);
            return bats;
        }

        /// <summary>
        /// Hex Crow launch: a violet ring snaps open where the crow leaves her hand, with a star and a
        /// burst of feathers.
        /// </summary>
        private static GameObject CrowLaunch()
        {
            var (root, _, content, duration) = AttackRoot("NyxCrowLaunch", "crow-launch", "Launch");
            content.transform.localPosition = new Vector3(0, .45f, .6f);
            float life = duration / 60f;
            var ring = Particles(content, "Hex ring", _materials.ShadowShockRing, life);
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = life * .8f;
            main.startSize = .95f;
            SizeOverLife(ring, Curve((0f, .3f), (.2f, 1.1f), (.4f, 1f), (1f, 1.1f)));
            Fade(ring, .5f);
            Pop(content, "Violet launch", _materials.ShadowBurst, .7f, .12f, ParticleSystemScalingMode.Local);
            Motifs(content, "Launch feathers", _materials.Feather, 8, .24f, life, 2.6f);
            return Save(root);
        }

        /// <summary>
        /// The Hex Crow in flight: her retained crow model flapping, with feathers and violet shards
        /// shed along its path.
        /// </summary>
        private static GameObject Crow()
        {
            var root = _kit.NewRoot("NyxHexCrow", attackBound: false);
            string palette = Root + "/Presentation/Source/NyxCrowPalette.png";
            var material = _kit.ParticleMaterial("NyxCrowModel", AssetDatabase.LoadAssetAtPath<Texture2D>(palette), Color.white);
            Transform Model(string name, string mesh)
            {
                var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); part.transform.SetParent(root.transform, false);
                part.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Presentation/" + mesh + "Mesh.asset")
                    ?? throw new InvalidOperationException("Missing retained crow model " + mesh);
                var renderer = part.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                return part.transform;
            }
            Model("Crow body", "NyxCrowBody");
            var wings = root.AddComponent<NyxCrowVisual>();
            wings.LeftWing = Model("Left wing", "NyxCrowWingLeft"); wings.RightWing = Model("Right wing", "NyxCrowWingRight");
            Stream(root, "Feather wake", _materials.Feather, .19f, .28f, 9f);
            Stream(root, "Hex shards", _materials.Shard, _crowRadius * .45f, .3f, 7f);

            // The caw: a violet star and lines bursting from its beak when it marks the spot.
            var caw = Pop(root, "Caw", _materials.ShadowBurst, .75f, .22f, ParticleSystemScalingMode.Local);
            caw.transform.localPosition = new Vector3(0f, .1f, .3f);
            var lines = Particles(caw.gameObject, "Caw lines", _materials.ShadowBand, .3f, ParticleSystemSimulationSpace.Local);
            Burst(lines, 6);
            var main = lines.main;
            main.startLifetime = .26f;
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .16f);
            main.startSpeed = 4.5f;
            var shape = lines.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = .05f;
            Stretch(lines, 0f, 6f);
            Fade(lines, .4f);
            InFront(lines);
            foreach (var system in caw.GetComponentsInChildren<ParticleSystem>(true))
            {
                var settings = system.main;
                settings.playOnAwake = false;
            }
            wings.Caw = caw;

            // Where it will dive: a dotted drop line from the crow to the spot and a ring on it, the size of a contact.
            var line = new GameObject("Drop line", typeof(LineRenderer));
            line.transform.SetParent(root.transform, false);
            var drop = line.GetComponent<LineRenderer>();
            drop.useWorldSpace = true;
            drop.positionCount = 2;
            drop.widthMultiplier = .13f;
            drop.textureMode = LineTextureMode.Stretch;
            drop.alignment = LineAlignment.View;
            drop.sharedMaterial = _materials.DropLine;
            drop.shadowCastingMode = ShadowCastingMode.Off;
            drop.receiveShadows = false;
            drop.enabled = false;
            wings.DropLine = drop;
            float contact = _crowRadius + _definition.CapsuleRadius;
            var spot = StaticMesh(root, "Spot mark", FlatQuad("NyxCrowSpotMesh", contact / RingOuter), _materials.ShadowShockRing, Vector3.zero);
            spot.SetActive(false);
            wings.SpotMark = spot.transform;
            return Save(root);
        }

        /// <summary>A horizontal quad of half size <paramref name="half"/> on the floor plane, UVs 0 to 1.</summary>
        private static Mesh FlatQuad(string name, float half)
        {
            var quad = new Mesh { name = name };
            quad.SetVertices(new List<Vector3> { new(-half, 0f, -half), new(half, 0f, -half), new(-half, 0f, half), new(half, 0f, half) });
            quad.SetColors(Enumerable.Repeat((Color32)Color.white, 4).ToList());
            quad.SetUVs(0, new List<Vector2> { new(0f, 0f), new(1f, 0f), new(0f, 1f), new(1f, 1f) });
            quad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            quad.RecalculateNormals();
            quad.RecalculateBounds();
            return _kit.SaveMesh(quad);
        }

        /// <summary>A peck on the carrier's head: a small violet star, claw scratches and a couple of feathers.</summary>
        private static GameObject CrowPeck()
        {
            var root = _kit.NewRoot("NyxCrowPeck", attackBound: false);
            const ParticleSystemScalingMode scale = ParticleSystemScalingMode.Hierarchy;
            Pop(root, "Peck star", _materials.ShadowBurst, .6f, .13f, scale);
            Pop(root, "Peck scratches", _materials.ClawMarks, .5f, .18f, scale);
            Motifs(root, "Peck feathers", _materials.Feather, 3, .2f, .28f, 1.8f, scale);
            return Save(root);
        }

        /// <summary>The crow leaving without a pop (swatted, missed, broken or expired): a burst of feathers round a violet ring.</summary>
        private static GameObject CrowScatter()
        {
            var root = _kit.NewRoot("NyxCrowScatter", attackBound: false);
            Pop(root, "Scatter star", _materials.ShadowBurst, .7f, .12f, ParticleSystemScalingMode.Local);
            var ring = Particles(root, "Scatter ring", _materials.ShadowShockRing, .3f);
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = .24f;
            main.startSize = 1.1f;
            SizeOverLife(ring, Curve((0f, .4f), (.25f, 1f), (1f, 1.15f)));
            Fade(ring, .4f);
            Motifs(root, "Scattered feathers", _materials.Feather, 14, .3f, .45f, 3.4f);
            Motifs(root, "Scattered shards", _materials.Shard, 5, .16f, .3f, 2.4f);
            return Save(root);
        }

        /// <summary>The crow's pop on her next fake-form hit: a violet star and ring and an explosion of feathers.</summary>
        private static GameObject CrowPop()
        {
            var root = _kit.NewRoot("NyxCrowPop", attackBound: false);
            const ParticleSystemScalingMode scale = ParticleSystemScalingMode.Hierarchy;
            Pop(root, "Pop star", _materials.ShadowBurst, 1.05f, .14f, scale);
            var ring = Particles(root, "Pop ring", _materials.ShadowShockRing, .3f, scaling: scale);
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = .22f;
            main.startSize = 1.3f;
            SizeOverLife(ring, Curve((0f, .35f), (.25f, 1f), (1f, 1.15f)));
            Fade(ring, .4f);
            Motifs(root, "Crow feathers", _materials.Feather, 12, .28f, .3f, 3.6f, scale);
            Motifs(root, "Pop shards", _materials.Shard, 4, .16f, .22f, 2.6f, scale);
            return Save(root);
        }

        /// <summary>
        /// Grasp warning: a violet ring on the floor at the catch's radius with five claw tips that
        /// rise from its edge through the warning, so the target sees where the hand will close.
        /// </summary>
        private static GameObject GraspWarning()
        {
            var root = _kit.NewRoot("NyxGraspWarning", attackBound: false);
            float life = (Duration("grasp-warning") + 1) / 60f, R = _graspRadius;
            var ring = Particles(root, "Warning ring", _materials.ShadowShockRing, life, ParticleSystemSimulationSpace.Local);
            ring.transform.localPosition = Vector3.up * .04f;
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = life;
            main.startSize = 2f * R / RingOuter;
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeOverLife(ring, Curve((0f, .75f), (.15f, 1.03f), (.25f, 1f), (1f, 1f)));
            Fade(ring, .9f);
            var tips = Enumerable.Range(0, 6).Select(i =>
            {
                var at = Around(i * 60f + 30f);
                return new Blade(Bezier(at * R * .92f, at * R * 1f + Vector3.up * .45f, at * R * .68f + Vector3.up * .72f), .3f, Vector3.Cross(Vector3.up, at));
            });
            var mesh = BladeMesh("NyxGraspWarningMesh", tips);
            MeshShape(root, "Rising claw tips", _materials.ClawBlade, mesh, life, Vector3.zero, Flat(),
                Curve((0f, .05f), (.8f, .9f), (1f, 1f)), Flat(), .95f);
            return Save(root);
        }

        /// <summary>
        /// Grasp catch: a giant shadow hand erupts from the warning ring, five clawed fingers rising
        /// round the catch's radius to its full height and curling in over the target.
        /// </summary>
        private static GameObject GraspCatch(GameObject drag)
        {
            var root = _kit.NewRoot("NyxGraspCatch", attackBound: false);
            float life = (Duration("grasp-catch") + 1) / 60f, R = _graspRadius, H = _graspHeight;
            var fingers = Enumerable.Range(0, 5).Select(i =>
            {
                var at = Around(i * 72f + 18f);
                return new Blade(Bezier(at * R * .85f, at * R * 1.02f + Vector3.up * H * .62f, at * R * .28f + Vector3.up * H * .96f),
                    .34f, Vector3.Cross(Vector3.up, at));
            });
            var mesh = BladeMesh("NyxGraspHandMesh", fingers);
            Report("grasp-catch", mesh, Vector3.zero, new[] { (Vector3.up * R, Vector3.up * (H - R), R) });
            MeshShape(root, "Erupting hand", _materials.ClawBlade, mesh, life, Vector3.zero, Flat(),
                Curve((0f, .1f), (.3f, 1.05f), (.45f, 1f), (1f, 1f)), Flat(), .75f);
            var ring = Particles(root, "Catch ring", _materials.ShadowShockRing, life, ParticleSystemSimulationSpace.Local);
            ring.transform.localPosition = Vector3.up * .05f;
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = life;
            main.startSize = 2f * R / RingOuter;
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            Fade(ring, .8f);
            var shards = Motifs(root, "Catch shards", _materials.Shard, 8, .22f, life, 4f);
            shards.transform.localPosition = Vector3.up * .3f;
            root.AddComponent<PlacedTetherVisual>().DragPrefab = drag;
            return Save(root);
        }

        /// <summary>
        /// Grasp drag, carried on the caught target: a shadow hand closed round them, a ring at their
        /// feet, and a tendril on the floor that stretches back to her (PlacedTetherVisual scales its Z),
        /// shedding shards along the path.
        /// </summary>
        private static GameObject GraspDrag()
        {
            var root = _kit.NewRoot("NyxGraspDrag", attackBound: false);
            var fingers = Enumerable.Range(0, 5).Select(i =>
            {
                var at = Around(i * 72f);
                return new Blade(Bezier(at * .5f + Vector3.up * .05f, at * .62f + Vector3.up * .75f, at * .2f + Vector3.up * 1.3f),
                    .24f, Vector3.Cross(Vector3.up, at));
            });
            StaticMesh(root, "Gripping hand", BladeMesh("NyxGraspGripMesh", fingers, colored: true), _materials.ClawBlade, Vector3.zero);
            StaticMesh(root, "Grip ring", FlatQuad("NyxGraspPoolMesh", .8f), _materials.ShadowShockRing, Vector3.up * .03f);
            var tendril = BladeMesh("NyxGraspDragMesh", new[] { new Blade(t => new Vector3(.12f * Mathf.Sin(t * Mathf.PI * 3f) * (1f - t), .06f, t), .3f) }, colored: true);
            var drag = StaticMesh(root, "Drag", tendril, _materials.ClawBlade, Vector3.zero);
            Stream(root, "Dragged shards", _materials.Shard, .2f, .3f, 14f);
            root.AddComponent<PlacedTetherVisual>().Drag = drag.transform;
            return Save(root);
        }

        /// <summary>
        /// Grasp without a crow: a shadow hand bursts up from the floor right in front of her, its five fingers flaring
        /// out over the hand's height as it flings the target up, with a ring at its base and streaks rising from it.
        /// </summary>
        private static GameObject GraspPop()
        {
            var root = _kit.NewRoot("NyxGraspPop", attackBound: false);
            float life = (Duration("grasp-pop") + 4) / 60f, R = _handRadius, H = _handHeight;
            var fingers = Enumerable.Range(0, 5).Select(i =>
            {
                var at = Around(i * 72f + 18f);
                return new Blade(Bezier(at * R * .7f, at * R * .85f + Vector3.up * H * .55f, at * R * 1.05f + Vector3.up * H),
                    .32f, Vector3.Cross(Vector3.up, at));
            });
            var mesh = BladeMesh("NyxGraspPopHandMesh", fingers);
            Report("grasp-pop", mesh, Vector3.zero, new[] { (Vector3.up * R, Vector3.up * (H - R), R) });
            MeshShape(root, "Flinging hand", _materials.ClawBlade, mesh, life, Vector3.zero, Flat(),
                Curve((0f, .85f), (.08f, 1.06f), (.16f, 1f), (1f, 1f)), Flat(), .7f);
            var ring = Particles(root, "Hand ring", _materials.ShadowShockRing, life, ParticleSystemSimulationSpace.Local);
            ring.transform.localPosition = Vector3.up * .05f;
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = life;
            main.startSize = 2f * R / RingOuter;
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            Fade(ring, .7f);
            var streaks = Particles(root, "Rising streaks", _materials.ShadowBand, life, ParticleSystemSimulationSpace.Local);
            Burst(streaks, 10);
            main = streaks.main;
            main.startLifetime = life * .7f;
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .18f);
            main.startSpeed = H / (life * .7f);
            var shape = streaks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 0f;
            shape.radius = R * .8f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Stretch(streaks, 0f, 6f);
            Fade(streaks, .5f);
            var shards = Motifs(root, "Flung shards", _materials.Shard, 8, .22f, life, 4f);
            shards.transform.localPosition = Vector3.up * H * .6f;
            return Save(root);
        }

        private static GameObject StaticMesh(GameObject root, string name, Mesh mesh, Material material, Vector3 at)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = at;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part;
        }

        /// <summary>
        /// Nyx's cast effect (owner's pick "C + A"): on the casting hand, three ink flame tongues inside a broken sigil;
        /// at her feet, an ink pool with three short tendrils; in the air, a flat three-armed ink swirl under her boots.
        /// The concept's compact pool hugs her hem; sized up from it to read at match distance, and the flame tops her
        /// hand by a forearm. Three thin tendrils stay apart so the pool never reads as a wall round her legs.
        /// </summary>
        private static GameObject CastAura()
        {
            var root = _kit.NewRoot("NyxCastAura", attackBound: true);
            var aura = root.AddComponent<NyxCastAura>();
            var flame = Child(root, "Hand flame");
            var tongues = new[] { (0f, .46f), (120f, .34f), (240f, .3f) }.Select(t =>
            {
                var at = Around(t.Item1);
                return new Blade(Bezier(at * .05f, at * .12f + Vector3.up * t.Item2 * .5f, at * .02f + Vector3.up * t.Item2), .17f, Vector3.Cross(Vector3.up, at));
            });
            StaticMesh(flame, "Ink flame", BladeMesh("NyxCastFlameMesh", tongues, colored: true), _materials.ClawBlade, Vector3.zero);
            var sigil = Child(root, "Hand sigil");
            var arcs = Enumerable.Range(0, 3).Select(i => new Blade(t => Around(i * 120f + t * 85f) * .27f, .09f, Vector3.up));
            StaticMesh(sigil, "Broken sigil", BladeMesh("NyxCastSigilMesh", arcs, colored: true), _materials.ClawBlade, Vector3.zero);

            var pool = Child(root, "Ink pool");
            StaticMesh(pool, "Pool", FlatQuad("NyxCastPoolMesh", .62f), _materials.InkPool, Vector3.zero);
            var tendrils = Child(root, "Ink tendrils");
            var strands = new[] { (60f, .5f), (190f, .4f), (300f, .45f) }.Select(t =>
            {
                var at = Around(t.Item1);
                return new Blade(Bezier(at * .46f, at * .5f + Vector3.up * t.Item2 * .6f, at * .36f + Vector3.up * t.Item2), .09f, Vector3.Cross(Vector3.up, at));
            });
            StaticMesh(tendrils, "Tendrils", BladeMesh("NyxCastTendrilMesh", strands, colored: true), _materials.ClawBlade, Vector3.zero);

            var swirl = Child(root, "Ink swirl");
            var spiral = Enumerable.Range(0, 3).Select(i => new Blade(t => Around(i * 120f + t * 150f) * Mathf.Lerp(.15f, .62f, t), .16f, Vector3.up));
            StaticMesh(swirl, "Swirl", BladeMesh("NyxCastSwirlMesh", spiral, colored: true), _materials.ClawBlade, Vector3.zero);

            aura.Flame = flame.transform;
            aura.Sigil = sigil.transform;
            aura.Pool = pool.transform;
            aura.Tendrils = tendrils.transform;
            aura.Swirl = swirl.transform;
            return Save(root);
        }

        /// <summary>
        /// Ring Light's wind-up at her phone: the ring light opens behind it and spins up to full size by the flash,
        /// sparkles stream into the lens faster and faster, and in the last third three glints shoot out along her
        /// facing to the flash's far edge, so the threat and its reach read before it fires.
        /// </summary>
        private static GameObject RingLightCharge()
        {
            var (root, cue, content, duration) = AttackRoot("NyxRingLightCharge", "ring-light-charge", "Charge");
            cue.OriginBoneId = _phoneHand;
            float life = duration / 60f;
            var halo = Particles(content, "Ring light", _materials.ShockRing, life, ParticleSystemSimulationSpace.Local);
            Burst(halo, 1);
            var main = halo.main;
            main.startLifetime = life + 1f / 60f;
            main.startSize = 1.15f;
            SizeOverLife(halo, Curve((0f, .45f), (.2f, .62f), (.85f, 1f), (1f, 1.08f)));
            Spin(halo, 240f);
            InFront(halo);
            var motes = Particles(content, "Charging sparkles", _materials.Sparkle, life, ParticleSystemSimulationSpace.Local);
            main = motes.main;
            main.startLifetime = .22f;
            main.startSize = new ParticleSystem.MinMaxCurve(.14f, .22f);
            main.startSpeed = -4.5f;
            var emission = motes.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(40f, Curve((0f, .4f), (1f, 1f)));
            Sphere(motes, 1f);
            SizeOverLife(motes, Curve((0f, 1f), (1f, .3f)));
            InFront(motes);
            var glint = Particles(content, "Lens glint", _materials.Glint, life, ParticleSystemSimulationSpace.Local);
            glint.emission.SetBursts(new[] { .0f, .35f, .6f, .78f, .9f }.Select(t => new ParticleSystem.Burst(t * life, 1)).ToArray());
            main = glint.main;
            main.startLifetime = .12f;
            main.startSize = .55f;
            SizeOverLife(glint, Curve((0f, .5f), (.3f, 1.1f), (1f, .6f)));
            InFront(glint);
            float reach = Windows("ring-light").SelectMany(w => w.Hitboxes).Select(Resolve).Max(h => h.Start.z + h.Radius);
            var aim = Particles(content, "Aim glints", _materials.Sparkle, life, ParticleSystemSimulationSpace.Local);
            aim.emission.SetBursts(new[] { new ParticleSystem.Burst(life * .6f, 3) });
            main = aim.main;
            main.startLifetime = life * .4f;
            main.startSize = .3f;
            // From her phone, roughly half a metre ahead of her feet, to the flash's far edge.
            main.startSpeed = Mathf.Max(1f, reach - .5f) / (life * .4f);
            var shape = aim.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = .05f;
            Spin(aim, 180f);
            InFront(aim);
            return Save(root);
        }

        // ── Fake form ────────────────────────────────────────────────────────────

        /// <summary>
        /// Phone Slaps: each of the three slaps gets its own beat (by emit tick): a pink swoosh across
        /// its hit sphere the way the slap swings (forehand, backhand, then down), a burst star filling
        /// the sphere and hearts. The third, the launcher, is the biggest.
        /// </summary>
        private static GameObject PhoneSlaps()
        {
            var root = _kit.NewRoot("NyxPhoneSlaps", attackBound: true);
            var cue = root.AddComponent<NyxAttackCue>();
            var beats = new List<NyxAttackCue.Beat>();
            var windows = Windows("phone-slaps");
            for (int index = 0; index < windows.Count; index++)
            {
                var window = windows[index];
                var (c, _, r) = Resolve(window.Hitboxes.Single());
                float life = window.Duration / 60f;
                var content = Child(root, "Slap " + (index + 1));
                beats.Add(new NyxAttackCue.Beat { Content = content, EndAfter = window.Duration, Spawn = window.Tick });
                var swing = index switch { 0 => Vector3.right, 1 => Vector3.left, _ => Vector3.down };
                var bulge = index < 2 ? Vector3.forward : Vector3.forward * .8f + Vector3.up * .2f;
                var from = c - swing * r * 1.0f - Vector3.forward * r * .35f;
                var to = c + swing * r * .95f - Vector3.forward * r * .15f;
                var bend = c + bulge * r * 1.15f;
                float width = r * .75f;
                var mesh = BladeMesh("NyxPhoneSlap" + (index + 1) + "Mesh",
                    new[] { new Blade(InSphere(Bezier(from, bend, to), c, r - width * .3f), width, index < 2 ? Vector3.up : Vector3.right) }, c);
                Report("phone-slaps", mesh, c, new[] { (c, c, r) });
                InFront(MeshShape(content, "Slap swoosh " + (index + 1), _materials.PinkBlade, mesh, life, c, Snap(life), Snap(life), Snap(life), .5f));
                var landing = Vector3.Lerp(c, to, .8f);
                Pop(content, "Slap star " + (index + 1), _materials.PinkBurst, r * (index == 2 ? 1.1f : .85f), life, ParticleSystemScalingMode.Local)
                    .transform.localPosition = landing;
                Motifs(content, "Slap hearts " + (index + 1), _materials.Heart, index == 2 ? 4 : 2, .2f, life, 2.2f).transform.localPosition = landing;
            }
            cue.Beats = beats.ToArray();
            return Save(root);
        }

        /// <summary>
        /// Ring Light Uppercut and Overhead: the roster's SwordSweepTrail in pink and white along the
        /// pole's measured path while the pole is in the hitbox capsule. Along the pole, the band's
        /// outer edge reaches the capsule's far side where the pole passes through it and the ring's
        /// top elsewhere. A swing that falls and rises again (the Overhead's chop, then its lift)
        /// gets one trail per stroke. Star glints mark the start, the bottom of a chop and the crest.
        /// </summary>
        private static GameObject PoleSwing(string name, string id, bool arc = false)
        {
            var hit = FirstHit(id);
            var (a, b, radius) = Resolve(hit);
            var (poses, top, rest) = SampleRig(hit.Move, hit.Tick - TrailLead - 1, hit.End + 1);
            var grips = poses.Select(p => p.At["pole"]).ToArray();
            var axes = poses.Select(p => (p.At["ring"] - p.At["pole"]).normalized).ToArray();
            RepairJerks(grips, axes);
            // Index i is tick hit.Tick - TrailLead + i.
            int sweep = hit.End - hit.Tick + TrailLead;
            grips = Smooth(grips).Skip(1).Take(sweep).ToArray();
            axes = Smooth(axes).Skip(1).Take(sweep).Select(v => v.normalized).ToArray();
            var reach = Enumerable.Range(0, sweep).Select(i => FarInside(grips[i], axes[i], a, b, radius, top)).ToArray();
            var smoothed = reach.Select((r, i) => .25f * reach[Math.Max(0, i - 1)] + .5f * r + .25f * reach[Math.Min(sweep - 1, i + 1)]).ToArray();
            reach = reach.Select((r, i) => Mathf.Max(r, smoothed[i])).ToArray();
            // The trail ends once the pole has left the capsule.
            sweep = Math.Min(sweep, Enumerable.Range(0, sweep).Where(i => reach[i] > top + .02f).DefaultIfEmpty(sweep - 1).Max() + 2);
            var tips = Enumerable.Range(0, sweep).Select(i => grips[i] + axes[i] * reach[i]).ToArray();
            // A slim band, never below the ring: it swells where the capsule reaches past the pole and shrinks to the ring elsewhere.
            var hilts = Enumerable.Range(0, sweep).Select(i => grips[i] + axes[i] * Mathf.Max(rest, reach[i] - .6f * radius)).ToArray();
            int bottom = Enumerable.Range(0, sweep).OrderBy(i => tips[i].y).First();
            bool chop = bottom > 0 && bottom < sweep - 2 && tips[0].y - tips[bottom].y > .3f && tips.Skip(bottom).Max(p => p.y) - tips[bottom].y > .3f;
            var strokes = chop ? new[] { (0, bottom), (bottom, sweep - 1) } : new[] { (0, sweep - 1) };
            int crest = Enumerable.Range(strokes.Last().Item1, strokes.Last().Item2 - strokes.Last().Item1 + 1).OrderByDescending(i => tips[i].y).First();

            var root = _kit.NewRoot(name, attackBound: true);
            var cue = root.AddComponent<NyxAttackCue>();
            cue.Trails = strokes.Select(stroke => new NyxAttackCue.SweepTrail
            {
                Style = _poleTrail, StartAfter = Math.Max(0, stroke.Item1 - TrailLead), Lead = Math.Max(0, TrailLead - stroke.Item1),
                Steps = TrailSteps, Seconds = TrailSeconds,
                Hilts = Spline(hilts.Skip(stroke.Item1).Take(stroke.Item2 - stroke.Item1 + 1).ToArray(), TrailSteps),
                Tips = Spline(tips.Skip(stroke.Item1).Take(stroke.Item2 - stroke.Item1 + 1).ToArray(), TrailSteps),
            }).ToArray();
            int duration = Duration(id);
            var marks = new List<(int Index, string Name, float Size)> { (TrailLead, "Swing glint", .42f) };
            if (chop) marks.Add((bottom, "Chop glint", .55f));
            marks.Add((crest, "Apex glint", .6f));
            var beats = new List<NyxAttackCue.Beat>();
            foreach (var (index, mark, size) in marks)
            {
                var content = Child(root, mark, tips[index]);
                int at = Math.Max(0, index - TrailLead);
                beats.Add(new NyxAttackCue.Beat { Content = content, StartAfter = at, EndAfter = Mathf.Min(duration, at + 10) });
                var star = Particles(content, mark + " star", _materials.Glint, .2f);
                Burst(star, 1);
                var main = star.main;
                main.startLifetime = .16f;
                main.startSize = size;
                main.startRotation = new ParticleSystem.MinMaxCurve(-.3f, .3f);
                SizeOverLife(star, Curve((0f, .3f), (.18f, 1.15f), (.4f, 1f), (1f, .2f)));
                Spin(star, 180f);
                Fade(star, .6f);
                InFront(star);
                if (index != crest) continue;
                var heart = Particles(content, "Apex heart", _materials.Heart, .3f);
                heart.transform.SetLocalPositionAndRotation(Vector3.right * .2f, Quaternion.Euler(-90f, 0f, 0f));
                Burst(heart, 1);
                main = heart.main;
                main.startLifetime = .25f;
                main.startSize = .3f;
                main.startSpeed = .8f;
                main.startRotation = .25f;
                SizeOverLife(heart, Curve((0f, .4f), (.2f, 1.1f), (.4f, 1f), (1f, .7f)));
                Fade(heart, .55f);
                InFront(heart);
            }
            if (arc)
            {
                // The capsule leans forward while the pole goes up and back, so the trail leaves it
                // early; a held arc inside the capsule shows the whole reach of the hit.
                var swing = Child(root, "Uppercut arc");
                beats.Add(new NyxAttackCue.Beat { Content = swing, EndAfter = hit.End - hit.Tick });
                var axis = (b - a).normalized;
                var forward = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
                float width = radius * 1.1f;
                var curve = InCapsule(Bezier(a - axis * radius * .3f, (a + b) * .5f + forward * radius * .7f, b + axis * radius * .7f), a, b, radius - width * .3f);
                var mesh = BladeMesh("NyxPoleUppercutArcMesh", new[] { new Blade(curve, width, Vector3.Cross(axis, forward)) }, a);
                Report(id, mesh, a, new[] { (a, b, radius) });
                float life = (hit.End - hit.Tick) / 60f;
                MeshShape(swing, "Rising arc", _materials.PinkBlade, mesh, life, a, Snap(life), Snap(life), Snap(life), .55f);
            }
            cue.Beats = beats.ToArray();
            ReachReport[id + "/trail"] = new
            {
                hitTicks = $"{hit.Tick}-{hit.End - 1}", strokes = strokes.Select(s => $"{hit.Tick - TrailLead + s.Item1}-{hit.Tick - TrailLead + s.Item2}").ToArray(),
                farthestTipOutside = Math.Round(Math.Max(0f, tips.Max(p => CapsuleDistance(p, a, b, radius))), 3),
            };
            return Save(root);
        }

        /// <summary>
        /// Replaces single-frame jerks in a GenRA clip, where the pole turns more than 50° away from
        /// its neighbours and back, with the neighbours' mean; the trail follows the swing, not the jerk.
        /// </summary>
        private static void RepairJerks(Vector3[] grips, Vector3[] axes)
        {
            for (int pass = 0; pass < axes.Length; pass++)
            {
                int worst = -1;
                float angle = 50f;
                for (int i = 1; i < axes.Length - 1; i++)
                {
                    float off = Vector3.Angle(axes[i], (axes[i - 1] + axes[i + 1]).normalized);
                    if (off > angle) { angle = off; worst = i; }
                }
                if (worst < 0) return;
                axes[worst] = (axes[worst - 1] + axes[worst + 1]).normalized;
                grips[worst] = (grips[worst - 1] + grips[worst + 1]) * .5f;
            }
        }

        /// <summary>
        /// Spinning Slap: a pink trail sweeps a full turn round her on the phone's measured path, from
        /// her hand out to the hit sphere's surface at the phone's height.
        /// </summary>
        private static GameObject SpinningSlap()
        {
            var hit = FirstHit("spin-arc");
            var (c, _, R) = Resolve(hit);
            var (poses, _, _) = SampleRig(hit.Move, hit.Tick - TrailLead, hit.End);
            var hilts = poses.Select(p => p.At["RightHand"]).ToArray();
            var tips = poses.Select(p =>
            {
                var far = p.At["phone.far"];
                var flat = Vector3.ProjectOnPlane(far - c, Vector3.up);
                float dy = far.y - c.y;
                return new Vector3(c.x, far.y, c.z) + flat.normalized * Mathf.Sqrt(Mathf.Max(0f, R * R - dy * dy));
            }).ToArray();
            var root = _kit.NewRoot("NyxSpinArc", attackBound: true);
            var cue = root.AddComponent<NyxAttackCue>();
            cue.Trails = new[] { new NyxAttackCue.SweepTrail { Style = _poleTrail, Lead = TrailLead, Steps = TrailSteps, Seconds = TrailSeconds,
                Hilts = Spline(hilts, TrailSteps), Tips = Spline(tips, TrailSteps) } };
            int duration = Duration("spin-arc"), last = tips.Length - 1 - TrailLead;
            var glint = Child(root, "Spin glint", tips[TrailLead]);
            var heart = Child(root, "Spin heart", tips[tips.Length - 1] + Vector3.up * .2f);
            cue.Beats = new[]
            {
                new NyxAttackCue.Beat { Content = glint, EndAfter = Math.Min(duration, 10) },
                new NyxAttackCue.Beat { Content = heart, StartAfter = last, EndAfter = Math.Min(duration, last + 10) },
            };
            Pop(glint, "Spin star", _materials.Glint, .45f, .16f, ParticleSystemScalingMode.Local);
            Pop(heart, "Spin heart pop", _materials.Heart, .34f, .2f, ParticleSystemScalingMode.Local);
            ReachReport["spin-arc/trail"] = new { hitTicks = $"{hit.Tick}-{hit.End - 1}", tipRadius = Math.Round(tips.Average(p => Vector3.Distance(p, c)), 2), sphere = R };
            return Save(root);
        }

        /// <summary>
        /// Platform Stomp-Kick: a pink blast fans from her boot sole down and forward to the far end
        /// of the low capsule, where a burst star and a heart land; a flash marks the sole.
        /// </summary>
        private static GameObject StompKick()
        {
            var hit = FirstHit("stomp-impact");
            var (root, _, content, duration) = AttackRoot("NyxStompImpact", "stomp-impact", "Kick blast");
            var (a, b, r) = Resolve(hit);
            float life = duration / 60f;
            var pose = SampleRig(hit.Move, hit.Tick, hit.Tick + 1).Poses[0];
            var sole = Vector3.Lerp(pose.At["RightFoot"], pose.At["RightToe_End"], .6f);
            var axis = (b - a).normalized;
            var far = b + axis * r * .55f;
            var rays = new[] { -1, 0, 1 }.Select(k =>
            {
                var end = far + Vector3.right * k * r * .55f + Vector3.up * (Mathf.Abs(k) * r * .25f - r * .1f);
                return new Blade(Bezier(sole, Vector3.Lerp(sole, end, .5f) + Vector3.up * .1f, end), r * 1.4f, Vector3.up);
            });
            var mesh = BladeMesh("NyxStompBlastMesh", rays, sole);
            Report("stomp-impact", mesh, sole, new[] { (a, b, r) });
            InFront(MeshShape(content, "Boot blast", _materials.LightRay, mesh, life, sole, Snap(life, .3f), Snap(life, .3f), Snap(life, .3f), .5f));
            Pop(content, "Sole flash", _materials.PinkBurst, .5f, life * .6f, ParticleSystemScalingMode.Local).transform.localPosition = sole;
            var impact = b + axis * r * .1f;
            Pop(content, "Kick star", _materials.PinkBurst, r * 2f, life, ParticleSystemScalingMode.Local).transform.localPosition = impact;
            Motifs(content, "Kick hearts", _materials.Heart, 3, .22f, life, 2f).transform.localPosition = impact;
            return Save(root);
        }

        /// <summary>
        /// Double Dropkick: each boot's drive into the hit replays as a pink trail at the moment of
        /// impact, and two pink spikes point from the soles down the capsule to its far end and hold
        /// through the active window.
        /// </summary>
        private static GameObject Dropkick()
        {
            var hit = FirstHit("dropkick-spike");
            var (root, cue, content, duration) = AttackRoot("NyxDropkickSpike", "dropkick-spike", "Spikes");
            var (a, b, r) = Resolve(hit);
            float life = duration / 60f;
            var axis = (b - a).normalized;
            const int driveTicks = 3;
            var drive = SampleRig(hit.Move, hit.Tick - driveTicks, hit.Tick + 1).Poses;
            var trails = new List<NyxAttackCue.SweepTrail>();
            var spikes = new List<Blade>();
            var landed = drive.Last();
            var middle = (landed.At["LeftToe_End"] + landed.At["RightToe_End"]) * .5f;
            foreach (string side in new[] { "Left", "Right" })
            {
                var hilts = drive.Select(p => p.At[side + "Foot"]).ToArray();
                var tips = drive.Select(p =>
                {
                    var toe = p.At[side + "Toe_End"];
                    var leg = (toe - p.At[side + "Leg"]).normalized;
                    return toe + leg * Mathf.Max(.12f, FarInside(toe, leg, a, b, r, 0f) * .6f);
                }).ToArray();
                // The drive happens before the hitbox opens: the whole of it is the trail's lead-in.
                trails.Add(new NyxAttackCue.SweepTrail { Style = _poleTrail, Lead = driveTicks, Steps = TrailSteps, Seconds = .16f,
                    Hilts = Spline(hilts, TrailSteps), Tips = Spline(tips, TrailSteps) });
                var sole = landed.At[side + "Toe_End"];
                var end = b + axis * r * .8f + (sole - middle) * .6f;
                spikes.Add(new Blade(Bezier(sole, Vector3.Lerp(sole, end, .5f) - axis * .05f, end), r * .7f, Vector3.Cross(axis, Vector3.right)));
            }
            cue.Trails = trails.ToArray();
            var mesh = BladeMesh("NyxDropkickSpikeMesh", spikes, middle);
            Report("dropkick-spike", mesh, middle, new[] { (a, b, r) });
            MeshShape(content, "Boot spikes", _materials.PinkBlade, mesh, life, middle, Snap(life, .2f), Snap(life, .2f), Snap(life, .2f), .65f);
            var burst = b + axis * r * .5f;
            Pop(content, "Spike star", _materials.PinkBurst, r * 1.3f, life * .6f, ParticleSystemScalingMode.Local).transform.localPosition = burst;
            Motifs(content, "Spike sparkles", _materials.Sparkle, 4, .16f, life, 2.4f).transform.localPosition = burst;
            return Save(root);
        }

        /// <summary>
        /// Ring Light: a fan of pink light rays from her phone out to the far side of the four hit
        /// spheres, a ring-light flash at the phone, and sparkles and hearts along the fan's edge.
        /// </summary>
        private static GameObject RingLight()
        {
            var hits = Windows("ring-light").SelectMany(w => w.Hitboxes).ToArray();
            var (root, _, content, duration) = AttackRoot("NyxRingLight", "ring-light", "Dazzle");
            float life = duration / 60f;
            var spheres = hits.Select(Resolve).ToArray();
            var source = SampleRig(hits[0].Move, hits[0].Tick, hits[0].Tick + 1).Poses[0].At["phone.far"];
            float Exit(Vector3 direction) => spheres.Max(s =>
            {
                var offset = source - s.Start;
                float along = Vector3.Dot(direction, offset), gap = along * along - (offset.sqrMagnitude - s.Radius * s.Radius);
                return gap < 0f ? 0f : -along + Mathf.Sqrt(gap);
            });
            float spread = spheres.Max(s =>
            {
                var flat = Vector3.ProjectOnPlane(s.Start - source, Vector3.up);
                return Mathf.Abs(Mathf.Atan2(flat.x, flat.z)) + Mathf.Asin(Mathf.Clamp01(s.Radius / flat.magnitude));
            }) * .88f;
            float centreY = spheres.Average(s => s.Start.y), centreZ = spheres.Average(s => s.Start.z);
            var rays = new List<Blade>();
            var ends = new List<Vector3>();
            for (int k = 0; k < 7; k++)
            {
                float yaw = Mathf.Lerp(-spread, spread, k / 6f);
                float height = centreY + (k % 3 == 0 ? .3f : k % 3 == 1 ? -.2f : .05f);
                var direction = new Vector3(Mathf.Sin(yaw), (height - source.y) / Mathf.Max(.5f, centreZ - source.z), Mathf.Cos(yaw)).normalized;
                float length = Exit(direction);
                if (length <= .1f) continue;
                var end = source + direction * length;
                ends.Add(end);
                rays.Add(new Blade(Bezier(source, Vector3.Lerp(source, end, .5f), end), .6f, Vector3.up));
            }
            var mesh = BladeMesh("NyxRingLightRaysMesh", rays, source);
            Report("ring-light", mesh, source, spheres.Select(s => (s.Start, s.End, s.Radius)));
            InFront(MeshShape(content, "Light rays", _materials.LightRay, mesh, life, source, Snap(life, .2f), Snap(life, .2f), Snap(life, .2f), .55f));
            Pop(content, "Ring light flash", _materials.ShockRing, 1.3f, life, ParticleSystemScalingMode.Local).transform.localPosition = source;
            Pop(content, "Flash star", _materials.PinkBurst, 1f, life * .7f, ParticleSystemScalingMode.Local).transform.localPosition = source;
            Pop(content, "Lens glint", _materials.Glint, .7f, life, ParticleSystemScalingMode.Local).transform.localPosition = source;
            // Focus lines race out through the fan and stop at its far edge, so the flash shows how far it reaches.
            float far = ends.Count == 0 ? 2f : ends.Average(e => (e - source).magnitude);
            var lines = Particles(content, "Flash lines", _materials.PinkBand, life, ParticleSystemSimulationSpace.Local);
            lines.transform.localPosition = source;
            Burst(lines, 14);
            var lineMain = lines.main;
            lineMain.startLifetime = life;
            lineMain.startSize = new ParticleSystem.MinMaxCurve(.12f, .17f);
            lineMain.startSpeed = far / life;
            var lineShape = lines.shape;
            lineShape.enabled = true;
            lineShape.shapeType = ParticleSystemShapeType.Cone;
            lineShape.angle = spread * Mathf.Rad2Deg;
            lineShape.radius = .05f;
            Stretch(lines, 0f, 7f);
            Fade(lines, .5f);
            InFront(lines);
            for (int i = 0; i < ends.Count; i += 2)
                Pop(content, "Edge " + (i % 4 == 0 ? "heart " : "sparkle ") + i, i % 4 == 0 ? _materials.Heart : _materials.Sparkle,
                    i % 4 == 0 ? .3f : .24f, life, ParticleSystemScalingMode.Local).transform.localPosition = Vector3.Lerp(source, ends[i], .92f);
            return Save(root);
        }

        /// <summary>
        /// Ring Light Rise: the ring light spins over her head like a rotor, three pink blades inside
        /// a pink disc ring that spin up through the startup, with a downdraft once she rises and
        /// sparkles shed along her path. The rotor shares the rising hitbox's centre and reach.
        /// </summary>
        private static GameObject RingLightRise()
        {
            var (root, cue, content, duration) = AttackRoot("NyxRotorArcs", "rotor-arcs", "Rotor");
            float life = duration / 60f, startup = Mathf.Clamp01(_rotorStartupTicks / (float)duration);
            cue.OriginBoneId = "bone.hips";
            var hit = FirstHit("rotor-arcs");
            var hub = new Vector3(hit.Geometry.OffX, hit.Geometry.OffY, hit.Geometry.OffZ);
            float R = hit.Geometry.Radius;
            root.AddComponent<PropVisibilityPresentation>().Configure(1, Array.Empty<string>(),
                new[] { "Nyx_BackpackPole", "Nyx_PoleHeld" });
            var blades = Enumerable.Range(0, 3).Select(i => new Blade(t => Around(i * 120f + t * 80f) * Mathf.Lerp(R * .25f, R * .92f, t), .3f));
            var mesh = BladeMesh("NyxRotorBladesMesh", blades);
            var rotor = MeshShape(content, "Rotor blades", _materials.PinkBlade, mesh, life, hub, Snap(life, .4f), Flat(), Snap(life, .4f), .92f);
            SpinY(rotor, 34f, Curve((0f, .35f), (startup, 1f), (1f, 1f)));
            var disc = Particles(content, "Rotor ring", _materials.ShockRing, life, ParticleSystemSimulationSpace.Local);
            disc.transform.localPosition = hub;
            Burst(disc, 1);
            var main = disc.main;
            main.startLifetime = life + 1f / 60f;
            main.startSize = 2f * R / RingOuter;
            disc.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeOverLife(disc, Curve((0f, .5f), (startup, 1f), (1f, 1f)));
            Fade(disc, .92f);

            var lift = Child(root, "Lift");
            cue.Beats = cue.Beats.Append(new NyxAttackCue.Beat { Content = lift, StartAfter = _rotorStartupTicks, EndAfter = duration }).ToArray();
            float rise = (duration - _rotorStartupTicks) / 60f;
            var draft = Particles(lift, "Downdraft", _materials.PinkBand, rise, ParticleSystemSimulationSpace.Local);
            draft.transform.localPosition = hub + Vector3.down * .15f;
            main = draft.main;
            main.startLifetime = .2f;
            main.startSize = new ParticleSystem.MinMaxCurve(.08f, .12f);
            main.startSpeed = 0f;
            var emission = draft.emission;
            emission.rateOverTime = 45f;
            FlatCircle(draft, R * .75f);
            var velocity = draft.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(-7f, -9f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Stretch(draft, .05f, 4f);
            Fade(draft, .4f);
            var shed = Particles(lift, "Shed sparkles", _materials.Sparkle, rise);
            shed.transform.localPosition = hub;
            main = shed.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.3f, .45f);
            main.startSize = new ParticleSystem.MinMaxCurve(.14f, .22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-.4f, .4f);
            emission = shed.emission;
            emission.rateOverTime = 26f;
            FlatCircle(shed, R * .9f);
            SizeOverLife(shed, Curve((0f, .4f), (.2f, 1.1f), (1f, .3f)));
            Fade(shed, .5f);
            InFront(shed);
            return Save(root);
        }

        /// <summary>Selfie Grab's catch: pink viewfinder brackets snap round the capture sphere, framing whoever she grabs.</summary>
        private static GameObject SelfieCatch()
        {
            var (root, _, content, duration) = AttackRoot("NyxSelfieCatch", "selfie-catch", "Viewfinder");
            var (c, _, r) = Resolve(FirstHit("selfie-catch"));
            float life = duration / 60f;
            var frame = Particles(content, "Viewfinder frame", _materials.Viewfinder, life, ParticleSystemSimulationSpace.Local);
            frame.transform.localPosition = c;
            Burst(frame, 1);
            var main = frame.main;
            main.startLifetime = life + 1f / 60f;
            main.startSize = 2f * r * 1.05f;
            SizeOverLife(frame, Curve((0f, 1.4f), (.3f, .96f), (.45f, 1f), (1f, 1f)));
            Fade(frame, .8f);
            InFront(frame);
            Motifs(content, "Frame sparkles", _materials.Sparkle, 3, .18f, life, 2f).transform.localPosition = c;
            return Save(root);
        }

        /// <summary>The selfie flash at her phone: a white-hot star, an opening shutter ring, hearts and sparkles.</summary>
        private static GameObject SelfiePop()
        {
            var (root, cue, content, duration) = AttackRoot("NyxSelfiePop", "selfie-pop", "Phone flash");
            cue.OriginBoneId = "bone.right-hand";
            float life = duration / 60f;
            content.transform.localPosition = Vector3.up * .22f;
            Pop(content, "Shutter star", _materials.Glint, 1.1f, life * .7f, ParticleSystemScalingMode.Local);
            var ring = Particles(content, "Shutter ring", _materials.ShockRing, life, ParticleSystemSimulationSpace.Local);
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = life;
            main.startSize = 1.1f;
            SizeOverLife(ring, Curve((0f, .3f), (.25f, 1f), (1f, 1.35f)));
            Fade(ring, .45f);
            Motifs(content, "Photo hearts", _materials.Heart, 5, .26f, life, 2.8f);
            Motifs(content, "Photo sparkles", _materials.Sparkle, 6, .18f, life, 3.3f);
            return Save(root);
        }

        /// <summary>Selfie Grab's release: a big pink "like" heart pops over her head as the victim launches.</summary>
        private static GameObject SelfieRelease()
        {
            var root = _kit.NewRoot("NyxSelfieRelease", attackBound: false);
            var anchor = root.AddComponent<SnapToOwnerBone>();
            anchor.BoneId = "bone.head";
            anchor.Offset = Vector3.up * .35f;
            var heart = Particles(root, "Like heart", _materials.Heart, .45f, ParticleSystemSimulationSpace.Local);
            Burst(heart, 1);
            var main = heart.main;
            main.startLifetime = .42f;
            main.startSize = .75f;
            SizeOverLife(heart, Curve((0f, .3f), (.15f, 1.2f), (.3f, 1f), (1f, .9f)));
            var velocity = heart.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(.7f, .7f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Fade(heart, .7f);
            InFront(heart);
            var ring = Particles(root, "Like ring", _materials.ShockRing, .3f, ParticleSystemSimulationSpace.Local);
            Burst(ring, 1);
            main = ring.main;
            main.startLifetime = .25f;
            main.startSize = .9f;
            SizeOverLife(ring, Curve((0f, .3f), (.3f, 1f), (1f, 1.25f)));
            Fade(ring, .4f);
            Motifs(root, "Like sparkles", _materials.Sparkle, 4, .16f, .35f, 2.4f);
            return Save(root);
        }

        // ── Swaps ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Going Live (pink, pulls) and Logging Off (violet, pushes): a shock ring that snaps out to the
        /// hit sphere's equator and holds there while the sphere is live, a floor ring at its
        /// footprint, a comic flash behind her, focus lines and motifs thrown out to the edge: hearts
        /// and sparkles going live, shards logging off. Every radius comes from the sphere.
        /// </summary>
        private static GameObject SwapBurst(string name, string id, bool fake)
        {
            var root = _kit.NewRoot(name, attackBound: true);
            var cue = root.AddComponent<NyxAttackCue>();
            var hit = FirstHit(id);
            var (centre, _, radius) = Resolve(hit);
            int duration = Duration(id);
            float life = duration / 60f, hold = (hit.End - hit.Tick) / (float)duration;
            var burst = Child(root, fake ? "Live burst" : "Offline burst");
            cue.Beats = new[] { new NyxAttackCue.Beat { Content = burst, EndAfter = duration } };
            var ringMaterial = fake ? _materials.ShockRing : _materials.ShadowShockRing;
            Ring(burst, "Shock ring", ringMaterial, centre, radius, life, hold);
            float floor = _definition.ModelYOffset + .03f;
            Ring(burst, "Floor ring", ringMaterial, new Vector3(centre.x, floor, centre.z),
                Mathf.Sqrt(Mathf.Max(0f, radius * radius - (centre.y - floor) * (centre.y - floor))), life * .55f, hold);

            var flash = Particles(burst, "Outfit flash", fake ? _materials.PinkBurst : _materials.ShadowBurst, life);
            flash.transform.localPosition = Vector3.up * .55f;
            Burst(flash, 1);
            var main = flash.main;
            main.startLifetime = life * .55f;
            main.startSize = radius * .9f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.2f, .2f);
            SizeOverLife(flash, Curve((0f, .4f), (.08f, 1.1f), (.2f, 1f), (1f, .6f)));
            Fade(flash, .5f);
            // Behind her from every view, so the swap reads against the burst.
            flash.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, 0f, -.25f);

            float spread = life * .45f;
            var lines = Particles(burst, "Focus lines", fake ? _materials.PinkBand : _materials.ShadowBand, life, ParticleSystemSimulationSpace.Local);
            lines.transform.localPosition = centre;
            Burst(lines, 26);
            main = lines.main;
            main.startLifetime = spread;
            main.startSize = new ParticleSystem.MinMaxCurve(.1f, .15f);
            main.startSpeed = (radius - .7f) / spread;
            FlatCircle(lines, .7f);
            Stretch(lines, 0f, 8f);
            SizeOverLife(lines, Curve((0f, .6f), (.3f, 1f), (1f, .4f)));
            Fade(lines, .4f);

            if (fake)
            {
                Edge(burst, "Live hearts", _materials.Heart, centre, 10, .42f, life, radius * .88f, .25f);
                Edge(burst, "Live sparkles", _materials.Sparkle, centre + Vector3.up * .3f, 8, .28f, life * .8f, radius * .85f, .3f);
            }
            else
            {
                Edge(burst, "Offline shards", _materials.Shard, centre, 14, .46f, life, radius * .88f, .25f);
                Edge(burst, "Offline feathers", _materials.Feather, centre + Vector3.up * .3f, 8, .32f, life * .8f, radius * .85f, .3f);
            }
            return Save(root);
        }

        private static void FlatCircle(ParticleSystem system, float radius)
        {
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);
        }

        /// <summary>A flat ring whose dark outer rim sits at <paramref name="radius"/> once it has snapped out.</summary>
        private static void Ring(GameObject root, string name, Material material, Vector3 at, float radius, float life, float hold)
        {
            var ring = Particles(root, name, material, life, ParticleSystemSimulationSpace.Local);
            ring.transform.localPosition = at;
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = life;
            main.startSize = 2f * radius / RingOuter;
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            // Full size by the first live tick, so a Hitstop freeze shows the true radius.
            SizeOverLife(ring, Curve((0f, .7f), (.06f, 1.04f), (.14f, 1f), (1f, 1f)));
            var color = ring.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 1f), (Mathf.Min(.9f, hold + .1f), 1f), (1f, 0f) });
        }

        /// <summary>
        /// Motifs that pop on a flat band just inside <paramref name="radius"/> once the shock ring has
        /// snapped out, drifting a little outward: they mark the edge without passing it.
        /// </summary>
        private static void Edge(GameObject root, string name, Material material, Vector3 at, int count, float size, float life,
            float radius, float band)
        {
            var ps = Particles(root, name, material, life, ParticleSystemSimulationSpace.Local);
            ps.transform.localPosition = at;
            Burst(ps, count);
            var main = ps.main;
            main.startDelay = 2f / 60f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * .75f, life * .85f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .75f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(-.4f, .4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.1f, .3f);
            FlatCircle(ps, radius);
            var shape = ps.shape;
            shape.radiusThickness = band;
            SizeOverLife(ps, Curve((0f, .3f), (.12f, 1.15f), (.25f, 1f), (1f, .8f)));
            Spin(ps, 60f);
            Fade(ps, .6f);
            InFront(ps);
        }

        // ── Contacts and shared pieces ───────────────────────────────────────────

        /// <summary>
        /// Confirmed hits: a burst star with streaks, plus three claw scratches and shards for her
        /// shadows, or a heart and sparkles for Nyxxie. Contacts scale with damage (Hierarchy).
        /// </summary>
        private static GameObject Contact(string name, bool fake)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            const ParticleSystemScalingMode scale = ParticleSystemScalingMode.Hierarchy;
            Pop(root, "Contact flash", fake ? _materials.PinkBurst : _materials.ShadowBurst, .75f, .13f, scale);
            var lines = Particles(root, "Contact streaks", fake ? _materials.PinkBand : _materials.ShadowBand, .25f, scaling: scale);
            Burst(lines, 6);
            var main = lines.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.08f, .14f);
            main.startSize = .07f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 7f);
            Sphere(lines, .08f);
            Stretch(lines, .025f, 2.6f);
            Fade(lines, .45f);
            InFront(lines);
            if (fake)
            {
                var heart = Pop(root, "Contact heart", _materials.Heart, .34f, .2f, scale);
                heart.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, 0f, .5f);
                Motifs(root, "Contact hearts", _materials.Heart, 2, .16f, .23f, 2.3f, scale);
                Motifs(root, "Contact sparkles", _materials.Sparkle, 3, .14f, .2f, 3f, scale);
            }
            else
            {
                var scratches = Particles(root, "Claw scratches", _materials.ClawMarks, .2f, scaling: scale);
                Burst(scratches, 1);
                main = scratches.main;
                main.startLifetime = .17f;
                main.startSize = .85f;
                main.startRotation = new ParticleSystem.MinMaxCurve(-.2f, .2f);
                SizeOverLife(scratches, Curve((0f, .6f), (.2f, 1.05f), (.35f, 1f), (1f, 1f)));
                Fade(scratches, .5f);
                InFront(scratches);
                scratches.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, 0f, .4f);
                Motifs(root, "Shadow shards", _materials.Shard, 5, .12f, .23f, 2.3f, scale);
            }
            return Save(root);
        }

        private static ParticleSystem Pop(GameObject root, string name, Material material, float size, float life, ParticleSystemScalingMode scaling)
        {
            var pop = Particles(root, name, material, Mathf.Max(.1f, life), scaling: scaling);
            Burst(pop, 1);
            var main = pop.main;
            main.startLifetime = life;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.35f, .35f);
            SizeOverLife(pop, Curve((0f, .65f), (.15f, 1.1f), (.5f, 1f), (1f, .7f)));
            Fade(pop, .6f);
            InFront(pop);
            return pop;
        }

        private static ParticleSystem Motifs(GameObject root, string name, Material material, int count, float size, float life,
            float speed, ParticleSystemScalingMode scaling = ParticleSystemScalingMode.Local)
        {
            var ps = Particles(root, name, material, Mathf.Max(.1f, life), ParticleSystemSimulationSpace.Local, scaling);
            Burst(ps, count);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * .8f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .75f, size);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .6f, speed);
            main.startRotation = new ParticleSystem.MinMaxCurve(-.4f, .4f);
            Sphere(ps, .14f);
            Spin(ps, 90f);
            SizeOverLife(ps, Curve((0, 1), (.5f, .8f), (1, .2f)));
            Fade(ps, .65f);
            InFront(ps);
            return ps;
        }

        private static ParticleSystem Stream(GameObject root, string name, Material material, float size, float life, float perMetre)
        {
            var ps = Particles(root, name, material, 1f);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = life;
            main.startSize = size;
            var emission = ps.emission;
            emission.rateOverDistance = perMetre;
            Sphere(ps, .1f);
            SizeOverLife(ps, Curve((0, 1), (1, .1f)));
            Fade(ps, .5f);
            return ps;
        }

        /// <summary>
        /// The pole's sword-trail style: a hot pink band with a white core and a dark rim that tapers
        /// toward its oldest end, plus a light sprinkle of sparkles and hearts shed along it ("Lines",
        /// emitted by rate; "Dots" would emit five per sub-tick sample). Every Nyxxie swing uses it.
        /// </summary>
        private static GameObject PoleTrailStyle()
        {
            var root = _kit.NewRoot("NyxPoleTrail", attackBound: false);
            var ribbon = root.AddComponent<ParticleSystem>();
            ribbon.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ribbon.main;
            main.duration = .24f;
            main.loop = false;
            main.playOnAwake = false;
            main.startColor = Color.white;
            var emission = ribbon.emission;
            emission.enabled = false;
            var shape = ribbon.shape;
            shape.enabled = false;
            var color = ribbon.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 1f), (.45f, .9f), (1f, 0f) });
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _materials.PoleSmear;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var sprinkle = Particles(root, "Lines", _materials.TrailDots, .25f);
            main = sprinkle.main;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.16f, .26f);
            main.startSize = new ParticleSystem.MinMaxCurve(.1f, .16f);
            main.startSpeed = .5f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.5f, .5f);
            var rate = sprinkle.emission;
            rate.rateOverTime = 120f;
            RandomFrame(sprinkle, 2, 1);
            Spin(sprinkle, 120f);
            SizeOverLife(sprinkle, Curve((0f, .6f), (.2f, 1.1f), (1f, .3f)));
            Fade(sprinkle, .5f);
            return Save(root);
        }

        /// <summary>Catmull-Rom through per-tick points, <paramref name="steps"/> samples per tick, ending on the last point.</summary>
        private static Vector3[] Spline(Vector3[] points, int steps)
        {
            if (points.Length == 1) return points;
            Vector3 At(int i) => points[Mathf.Clamp(i, 0, points.Length - 1)];
            var result = new Vector3[(points.Length - 1) * steps + 1];
            for (int j = 0; j < result.Length; j++)
            {
                int i = Math.Min(j / steps, points.Length - 2);
                float t = j / (float)steps - i;
                Vector3 p0 = At(i - 1), p1 = At(i), p2 = At(i + 1), p3 = At(i + 2);
                result[j] = .5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);
            }
            return result;
        }

        /// <summary>Two passes of a 1-2-1 filter, holding the ends.</summary>
        private static Vector3[] Smooth(Vector3[] points)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var source = points;
                points = source.Select((p, i) => .25f * source[Math.Max(0, i - 1)] + .5f * p + .25f * source[Math.Min(source.Length - 1, i + 1)]).ToArray();
            }
            return points;
        }

        /// <summary>
        /// The rig through ticks [from, to) of a move, sampled as the cook bakes poses (a playable graph
        /// without root motion, the rig at visual scale), in the cue frame: capsule centre, facing +Z.
        /// Points are her Mixamo bones by name ("RightHand"), the pole's grip and ring ("pole", "ring")
        /// and the phone's grip and far end ("phone", "phone.far"). PoleTop and PoleRest are the ring's
        /// outer and inner edges, in metres from the grip.
        /// </summary>
        private static (List<Pose> Poses, float PoleTop, float PoleRest) SampleRig(CookedSlotDefinition move, int from, int to)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(Root + "/CharacterAssetCatalog.asset");
            var clip = move.Clip(0);
            var binding = catalog.Bindings.First(b => b.PoseTrackId == clip.AnimationId || b.SemanticId == clip.AnimationId);
            int frameCount = Mathf.CeilToInt(binding.Clip.length * 60f);
            var rig = UnityEngine.Object.Instantiate(catalog.Rig);
            rig.hideFlags = HideFlags.HideAndDontSave;
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.transform.localScale = Vector3.one * _definition.VisualScale;
            var graph = PlayableGraph.Create("Nyx rig sample");
            try
            {
                (Transform Bone, Vector3 Far, float Radius) Prop(string skinName, string boneName, float farFraction)
                {
                    var skin = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == skinName);
                    var bone = skin.bones.Single(t => t != null && t.name == boneName);
                    var bind = skin.sharedMesh.bindposes[Array.IndexOf(skin.bones, bone)];
                    var local = skin.sharedMesh.vertices.Select(v => bind.MultiplyPoint3x4(v)).ToArray();
                    var extent = local.Aggregate(Vector3.Max) - local.Aggregate(Vector3.Min);
                    var length = extent.x >= extent.y && extent.x >= extent.z ? Vector3.right : extent.y >= extent.z ? Vector3.up : Vector3.forward;
                    float low = local.Min(v => Vector3.Dot(v, length)), high = local.Max(v => Vector3.Dot(v, length));
                    if (Mathf.Abs(low) > Mathf.Abs(high)) { length = -length; (low, high) = (-high, -low); }
                    var head = local.Where(v => Vector3.Dot(v, length) >= Mathf.Lerp(low, high, farFraction)).ToArray();
                    var far = head.Aggregate(Vector3.zero, (sum, v) => sum + v) / head.Length;
                    var axis = far.normalized;
                    return (bone, far, head.Max(v => Vector3.Dot(v, axis)) - far.magnitude);
                }
                // The ring light is the far third of the pole from its grip bone.
                var pole = Prop("Nyx_PoleHeld", "Nyx_PoleHeld", .65f);
                var phone = Prop("Nyx_Phone", "Nyx_PhoneBone", .9f);
                var bones = rig.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("mixamorig:", StringComparison.Ordinal))
                    .GroupBy(t => t.name.Substring("mixamorig:".Length)).ToDictionary(g => g.Key, g => g.First());
                var animator = rig.GetComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.fireEvents = false;
                animator.Rebind();
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "pose", animator);
                var playable = AnimationClipPlayable.Create(graph, binding.Clip);
                playable.SetApplyFootIK(false);
                output.SetSourcePlayable(playable);
                graph.Play();
                var centre = Vector3.down * _definition.ModelYOffset;
                var poses = new List<Pose>();
                for (int tick = from; tick < to; tick++)
                {
                    playable.SetTime(clip.At(Math.Max(0, tick)).FrameAt(frameCount) / 60d);
                    graph.Evaluate(0f);
                    var pose = new Pose { Tick = tick };
                    foreach (var pair in bones) pose.At[pair.Key] = pair.Value.position - centre;
                    pose.At["pole"] = pole.Bone.position - centre;
                    pose.At["ring"] = pole.Bone.TransformPoint(pole.Far) - centre;
                    pose.At["phone"] = phone.Bone.position - centre;
                    pose.At["phone.far"] = phone.Bone.TransformPoint(phone.Far) - centre;
                    poses.Add(pose);
                }
                float scale = (poses[0].At["ring"] - poses[0].At["pole"]).magnitude / pole.Far.magnitude;
                return (poses, (pole.Far.magnitude + pole.Radius) * scale, (pole.Far.magnitude - pole.Radius) * scale);
            }
            finally
            {
                graph.Destroy();
                UnityEngine.Object.DestroyImmediate(rig);
            }
        }

        private static GameObject Save(GameObject root)
        {
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                uint seed = 2166136261;
                foreach (char c in system.name) seed = (seed ^ c) * 16777619;
                system.useAutoRandomSeed = false;
                system.randomSeed = seed;
            }
            return _kit.Save(root);
        }

        private static Materials BuildMaterials()
        {
            var outline = StarOutline(9, .4f, 31);
            var heartPoint = new[] { new Vector2(-.63f, .2f), new Vector2(.63f, .2f), new Vector2(0, -.78f) };
            Material Bake(string name, Func<float, float, Color> pixel, int size = 256) => _kit.ParticleMaterial(name,
                _kit.BakeTexture("Source/" + name, size, size, pixel), Color.white);
            Color Banded(float distance, bool fake) => Bands(distance * 128f, 2.5f, 7f,
                fake ? PinkRim : ShadowRim, fake ? Pink : Violet, fake ? Color.white : Lavender);
            // U runs root to tip, V across the strip; the blade is 80% of the strip at its widest.
            Material BladeTexture(string name, bool fake) => _kit.ParticleMaterial(name, _kit.BakeTexture("Source/" + name, 512, 128, (u, v) =>
            {
                // Thickest a third of the way out, then a long taper to a sharp tip.
                float half = .42f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(u, .6f))), .6f);
                Color color = Bands((Mathf.Abs(v - .5f) - half) * 128f, 8f, 22f, fake ? PinkRim : ShadowRim, fake ? Pink : Violet, fake ? Color.white : Lavender);
                color.a *= Step(0f, .1f, u);
                return color;
            }), Color.white);
            Color RingPixel(float u, float v, bool fake) => Banded((Mathf.Abs(new Vector2(u * 2 - 1, v * 2 - 1).magnitude - (RingOuter - .06f)) - .06f) * 2f, fake);
            var wingsUp = new[] { new Vector2(-.9f,.25f),new Vector2(-.8f,-.3f),new Vector2(-.4f,-.1f),new Vector2(0,-.42f),
                new Vector2(.4f,-.1f),new Vector2(.8f,-.3f),new Vector2(.9f,.25f),new Vector2(.25f,.05f),new Vector2(0,.35f),new Vector2(-.25f,.05f) };
            var wingsDown = new[] { new Vector2(-.8f,-.55f),new Vector2(-.55f,.15f),new Vector2(-.22f,.1f),new Vector2(0,.35f),new Vector2(.22f,.1f),
                new Vector2(.55f,.15f),new Vector2(.8f,-.55f),new Vector2(.48f,-.32f),new Vector2(.22f,-.42f),new Vector2(0,-.28f),new Vector2(-.22f,-.42f),new Vector2(-.48f,-.32f) };
            return new Materials
            {
                ShadowBurst = Bake("NyxRosterShadowBurst", (u, v) => Banded(Polygon(new Vector2(u*2-1, v*2-1), outline), false)),
                PinkBurst = Bake("NyxRosterPinkBurst", (u, v) => Banded(Polygon(new Vector2(u*2-1, v*2-1), outline), true)),
                ShadowBand = Bake("NyxRosterShadowBand", (u, v) => Banded(Lens(new Vector2(u*2-1, v*2-1), .94f, .32f), false)),
                PinkBand = Bake("NyxRosterPinkBand", (u, v) => Banded(Lens(new Vector2(u*2-1, v*2-1), .94f, .32f), true)),
                Heart = Bake("NyxRosterHeart", (u, v) =>
                {
                    var p = new Vector2(u*2-1, v*2-1);
                    float lobes = Mathf.Min((p-new Vector2(-.28f,.28f)).magnitude-.38f, (p-new Vector2(.28f,.28f)).magnitude-.38f);
                    return Banded(Mathf.Min(lobes, Polygon(p, heartPoint)), true);
                }),
                Sparkle = Bake("NyxRosterSparkle", (u, v) => Banded(Rhombus(new Vector2(u*2-1,v*2-1), new Vector2(.52f,.92f)),true)),
                Shard = Bake("NyxRosterShard", (u, v) => Banded(Rhombus(Rotate(new Vector2(u*2-1,v*2-1),18),new Vector2(.27f,.9f)),false)),
                Feather = Bake("NyxRosterFeather", (u, v) => Banded(Lens(Rotate(new Vector2(u*2-1,v*2-1),-28),.85f,.25f),false)),
                // Two frames, wings up then down, for flapping.
                Bat = _kit.ParticleMaterial("NyxRosterBat", _kit.BakeTexture("Source/NyxRosterBat", 512, 256, (u, v) =>
                {
                    bool down = u >= .5f;
                    var p = new Vector2((down ? u - .5f : u) * 4f - 1f, v * 2f - 1f);
                    return Bands(Polygon(p, down ? wingsDown : wingsUp) * 128f, 3f, 28f, ShadowRim, Violet, Lavender);
                }), Color.white),
                ClawBlade = BladeTexture("NyxClawBlade", false),
                PinkBlade = BladeTexture("NyxPinkBlade", true),
                // A wedge of light, narrow at the source (U = 0) and widest at its rounded far end.
                LightRay = _kit.ParticleMaterial("NyxLightRay", _kit.BakeTexture("Source/NyxLightRay", 256, 128, (u, v) =>
                {
                    float half = Mathf.Lerp(.06f, .44f, u);
                    Color color = Bands(Mathf.Max(Mathf.Abs(v - .5f) - half, (u - .97f) * .5f) * 128f, 5f, 16f, PinkRim, Pink, Color.white);
                    color.a *= Step(0f, .08f, u);
                    return color;
                }), Color.white),
                Viewfinder = Bake("NyxViewfinder", (u, v) =>
                {
                    var p = new Vector2(Mathf.Abs(u * 2 - 1), Mathf.Abs(v * 2 - 1));
                    return Banded(Mathf.Min(Box(p - new Vector2(.86f, .62f), new Vector2(.08f, .3f)), Box(p - new Vector2(.62f, .86f), new Vector2(.3f, .08f))), true);
                }),
                // SwordSweepTrail maps U along the sweep (old to new) and V 0 to 0.5 from the inner edge to the tip.
                PoleSmear = _kit.ParticleMaterial("NyxPoleSmear", _kit.BakeTexture("Source/NyxPoleSmear", 64, 128, (u, v) =>
                {
                    if (v >= .5f) return new Color(Pink.r, Pink.g, Pink.b, 0f);
                    // The oldest end of the sweep narrows toward the outer edge.
                    float across = v * 2f, inner = Mathf.Lerp(.7f, .02f, Step(.1f, .35f, u));
                    Color color = Bands(Mathf.Max(inner - across, across - .98f) * 64f, 2.5f, 20f, PinkRim, Pink, Color.white);
                    color.a *= Mathf.Min(Mathf.Clamp01((u - .06f) / .04f), Mathf.Clamp01((.96f - u) / .04f));
                    return color;
                }), Color.white),
                ShockRing = Bake("NyxShockRing", (u, v) => RingPixel(u, v, true), 512),
                ShadowShockRing = Bake("NyxShadowShockRing", (u, v) => RingPixel(u, v, false), 512),
                Glint = Bake("NyxGlint", (u, v) =>
                {
                    var p = new Vector2(u * 2f - 1f, v * 2f - 1f);
                    return Banded(Mathf.Min(Rhombus(p, new Vector2(.2f, .95f)), Rhombus(p, new Vector2(.95f, .2f))), true);
                }),
                TrailDots = _kit.ParticleMaterial("NyxTrailDots", _kit.BakeTexture("Source/NyxTrailDots", 256, 128, (u, v) =>
                {
                    var p = new Vector2((u < .5f ? u * 2f : u * 2f - 1f) * 2f - 1f, v * 2f - 1f);
                    if (u < .5f) return Bands(Mathf.Min(Rhombus(p, new Vector2(.3f, .95f)), Rhombus(p, new Vector2(.95f, .3f))) * 64f, 2f, 4f, PinkRim, Pink, Color.white);
                    float lobes = Mathf.Min((p - new Vector2(-.28f, .28f)).magnitude - .38f, (p - new Vector2(.28f, .28f)).magnitude - .38f);
                    return Bands(Mathf.Min(lobes, Polygon(p, heartPoint)) * 64f, 2f, 5f, PinkRim, Pink, Color.white);
                }), Color.white),
                // A whip: full width from the root to three quarters, then a short taper to its tip.
                ShadowWhip = _kit.ParticleMaterial("NyxShadowWhip", _kit.BakeTexture("Source/NyxShadowWhip", 512, 128, (u, v) =>
                {
                    float half = .4f * (1f - .85f * Step(.72f, 1f, u)) * (.75f + .25f * Step(0f, .15f, u));
                    Color color = Bands((Mathf.Abs(v - .5f) - half) * 128f, 8f, 22f, ShadowRim, Violet, Lavender);
                    color.a *= Step(0f, .05f, u);
                    return color;
                }), Color.white),
                // An ink pool with a wobbly edge: ink rim, violet band, deep ink core with a lavender sheen.
                InkPool = Bake("NyxInkPool", (u, v) =>
                {
                    var p = new Vector2(u * 2f - 1f, v * 2f - 1f);
                    float edge = .8f + .06f * Mathf.Sin(Mathf.Atan2(p.y, p.x) * 7f) + .05f * (ValueNoise(u, v, 6f, 11) - .5f);
                    Color color = Bands((p.magnitude - edge) * 128f, 3f, 12f, ShadowRim, Violet, Ink);
                    float sheen = 1f - Step(.02f, .06f, Mathf.Abs((p - new Vector2(-.18f, .2f)).magnitude - .32f));
                    return Color.Lerp(color, new Color(Lavender.r, Lavender.g, Lavender.b, color.a), sheen * .7f * Step(.1f, .3f, edge - p.magnitude));
                }, 512),
                DropLine = DropLineMaterial(),
                // Three parallel claw scratches, tapered at both ends, for her contacts.
                ClawMarks = Bake("NyxClawMarks", (u, v) =>
                {
                    var p = Rotate(new Vector2(u * 2f - 1f, v * 2f - 1f), 35f);
                    float d = 1f;
                    for (int i = -1; i <= 1; i++)
                        d = Mathf.Min(d, Lens(p - new Vector2(i * .08f, i * .3f), .85f - Mathf.Abs(i) * .12f, .1f));
                    return Banded(d, false);
                }),
            };
        }

        /// <summary>The roster's dotted drop line (ink rim, light core) in lavender, for the crow's committed dive.</summary>
        private static Material DropLineMaterial()
        {
            string path = _kit.Root + "/NyxCrowDropLine.mat";
            var shader = Resources.Load<Shader>("Shaders/FighterDropLine") ?? throw new InvalidOperationException("Missing FighterDropLine shader.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(shader);
            material.shader = shader;
            material.SetColor("_Color", Lavender);
            material.SetFloat("_Airborne", 1f);
            material.SetFloat("_Spacing", 2.2f);
            material.SetFloat("_Core", .32f);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        [CliCommand("sloparena.presentation.nyx.verify-timing", "Verify Nyx's attack cues against current source: nothing outlives its cue, every hitbox opens with something showing, and no coverage field remains.",
            MainThreadRequired = true, Tags = new[] { "presentation", "nyx" })]
        public static object VerifyTiming()
        {
            LoadCueData();
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(Root + "/CharacterAssetCatalog.asset");
            var problems = new List<string>();
            int checkedCues = 0, checkedHitboxes = 0;
            foreach (var binding in catalog.Presentations.Where(b => b.SemanticId.StartsWith(Prefix, StringComparison.Ordinal) && b.Prefab != null))
            {
                string id = binding.SemanticId.Substring(Prefix.Length);
                if (binding.Prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                        .Any(m => m != null && m.name.IndexOf("Field", StringComparison.OrdinalIgnoreCase) >= 0))
                    problems.Add(id + ": draws a coverage field");
                var cue = binding.Prefab.GetComponent<NyxAttackCue>();
                bool hasHits = Cues.TryGetValue(id, out var windows) && windows.Any(w => w.Hitboxes.Count > 0);
                if (cue == null)
                {
                    if (hasHits) problems.Add(id + ": missing attack cue");
                    continue;
                }
                if (windows == null) { problems.Add(id + ": no emit in character.json"); continue; }
                checkedCues++;
                bool variants = cue.Beats.Any(b => b.Spawn >= 0);
                foreach (var window in windows)
                {
                    var beats = cue.Beats.Where(b => b.Spawn < 0 || b.Spawn == window.Tick).ToArray();
                    if (variants && beats.All(b => b.Spawn < 0)) problems.Add($"{id}: no beat for its emit at tick {window.Tick}");
                    if (beats.Any(b => b.EndAfter > window.Duration)) problems.Add($"{id}: a beat outlives its cue at tick {window.Tick}");
                    if (cue.Trails.Any(t => t.StartAfter - t.Lead + (t.Tips.Length - 1) / Math.Max(1, t.Steps) >= window.Duration))
                        problems.Add($"{id}: a trail outlives its cue at tick {window.Tick}");
                    foreach (var hit in window.Hitboxes)
                    {
                        checkedHitboxes++;
                        int start = hit.Tick - window.Tick;
                        if (!beats.Any(b => b.StartAfter <= start && b.EndAfter > start) && !cue.Trails.Any(t => t.StartAfter <= start))
                            problems.Add($"{id}: nothing shows when its hitbox opens at tick {hit.Tick}");
                    }
                }
            }
            using (var document = JsonDocument.Parse(File.ReadAllText(Root + "/character.json")))
            {
                var real = document.RootElement.GetProperty("slots").EnumerateArray().ToArray();
                foreach (string slot in CastSlots)
                {
                    var operations = JsonNode.Parse(real.First(s => s.GetProperty("id").GetString() == slot).GetProperty("timeline")
                        .GetProperty("stages")[0].GetProperty("operations").GetRawText()).AsArray();
                    var aura = operations.FirstOrDefault(op => (string)op["presentationId"] == Prefix + "cast-aura");
                    if (aura == null) problems.Add($"cast-aura: missing on {slot}");
                    else if ((int)aura["tick"] != 0 || (int)aura["placement"]["durationTicks"] != LastActiveTick(operations))
                        problems.Add($"cast-aura: {slot} does not cover startup through its last active tick");
                    checkedCues++;
                }
            }
            if (problems.Count != 0) throw new InvalidOperationException(string.Join("; ", problems));
            return new { success = true, checkedCues, checkedHitboxes };
        }
    }
}
#endif
