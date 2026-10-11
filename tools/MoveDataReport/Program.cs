using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlopArena.Shared;

// Issue #149: the tuning A/B diff tool (SlopArena.AbDiffReport) reuses this tool's analysis
// engine (BuildReport / true-combo graph / DI escape) without duplicating it.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SlopArena.AbDiffReport")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Shared.Tests")]

namespace SlopArena.MoveDataReport;

/// <summary>
/// Move data report: runs the real shared sim (ADR-0019 knockback + flight law)
/// for any character's normal hitboxes at several victim damage percents, and prints
/// authored frame data + simulated trajectories + a derived true-combo matrix.
/// Also emits a lossless JSON report and a self-contained HTML visual report
/// (frame-adv heatmap, knockback-shape gallery, KO/blast-clearance).
///
/// Usage: dotnet run --project tools/MoveDataReport -- [character] [--pcts 0,30,60] [--out path]
///        [--json report.json] [--html report.html] [--combos] [--example]
/// Character: fightguy (default) | manki | wibou | bonk.
/// Default markdown output: docs/generated/{character}-move-data.md.
/// Kill % / blast clearance: on a Crossroads-style 60x60 proxy (top +20, sides ±40, bottom -10).
/// </summary>
internal static class Program
{
    internal const int MaxTicks = 2400;

    internal readonly record struct SlotRef(bool Air, int Slot);
    internal sealed record Route(string Label, SlotRef From, int FromHit, SlotRef To, int ToHit);
    internal sealed record HitSpec(SlotRef Slot, AbilitySpec Ability, AttackStage Stage, HitboxEvent Hit, int HitIndex,
        sbyte LaunchAngle, float BaseKb, float GrowthKb, bool Adaptive);
    internal sealed record RunResult(float Kv, ushort Hitstop, ushort Stun, float Rise, float Drift,
        float Apex, int ActionableTick, int? LandedTick);

    // ── JSON / HTML report model ────────────────────────────────────────────
    internal sealed record InitialStateData(float PositionX, float PositionY, float PositionZ,
        bool IsGrounded, string ActionState, float FacingYawDegrees);
    internal sealed record ReportMetadata(string Character, int[] RequestedPercents, string DamagePercentBasis,
        string VictimCharacter, float VictimWeight, InitialStateData VictimInitialState,
        int TickRateHz, string DiMode, string TerminationRule);
    internal sealed record TrajPoint(int Tick, float Height, float Travel, float Vy, float Vx, char Phase,
        float PositionX, float PositionY, float PositionZ, float VelocityX, float VelocityY, float VelocityZ,
        bool InHitstun);
    internal sealed record TrajectoryData(int Pct, float Kv, ushort Hitstop, ushort Stun, int Adv, int? AdvL,
        float Rise, float Drift, float Apex, int? ApexTick, float MaxTravel, int ActionableTick,
        int? HitstunExpiryTick, float? HitstunExpiryPositionX, float? HitstunExpiryPositionY,
        float? HitstunExpiryPositionZ, float? HitstunExpiryHorizontalDistance, float? HitstunExpiryHeight,
        int? LandedTick, string? KillLine, string Termination, int AuthoredAngle,
        float LaunchVelocityX, float LaunchVelocityY, float LaunchVelocityZ,
        float InitialHorizontalVelocity, float InitialVerticalVelocity, TrajPoint[] Points);
    internal sealed record FrameDataData(int Trigger, int ActiveStart, int ActiveEnd, float Damage, int Angle,
        float BaseKb, float Growth, ushort Stun, int Iasa, int LandingLag, int AcBefore, int AcAfter, int Duration,
        string Profile, bool Adaptive);
    internal sealed record ClearanceData(float TopFrac, float SideFrac, string Nearest);
    internal sealed record MoveData(string Label, string Slot, int HitIndex, string Ability, FrameDataData Frame,
        int? KillPct, string? KillLine, ClearanceData Clearance, TrajectoryData[] Trajectories, DiEscapeData[] DiEscape);
    internal sealed record ArenaData(float? KillHeight, float? KillTop, float? KillMinX, float? KillMaxX,
        float? KillMinZ, float? KillMaxZ, string Note);
    internal sealed record ReportData(string Character, string GeneratedAt, ReportMetadata Metadata, int[] Percents,
        ArenaData ReportArena, ArenaData KillArena, MoveData[] Moves,
        ComboStarterData[] TrueCombos, ComboDensityData[] ComboDensity,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReachData? Reach);

    // ── True-combo graph model ───────────────────────────────────────────────
    /// <summary>One starter→follow-up edge at one victim %. Verdict: "true" = the follow-up's damage landed
    /// while the victim was still in hitstun (real sim); "false" = it landed after stun expired (opponent
    /// actionable); "never" = no connect within the cap. Tightness = stun − attacker budget to the
    /// follow-up's first active tick (positive = frame-true by that many ticks, before travel reality).</summary>
    internal sealed record ComboEdgeData(string FollowUp, int Tightness, int Pct, string Verdict, int ConnectTick, int StunLeft);
    internal sealed record ComboStarterData(string Move, string State, ComboEdgeData[] Edges);
    internal sealed record ComboDensityData(int Pct, int Grounded, int Airborne, int Total);

    // ── DI escape-space model ────────────────────────────────────────────────
    internal sealed record DiVariantData(string Direction, float DevDeg, float MaxTravel, float Apex, TrajPoint[] Points);
    internal sealed record DiEscapeData(int Pct, float MaxDevDeg, DiVariantData[] Variants);

    // ── Reach chart model (issue #151) ───────────────────────────────────────
    /// <summary>One world-space capsule sample: the hitbox's start/end endpoints at one active tick, plus radius.</summary>
    internal sealed record ReachCapsuleData(int Tick, float X0, float Y0, float Z0, float X1, float Y1, float Z1, float Radius);
    /// <summary>Forward extent across one height band. MinZ/MaxZ null = the band is unreached.</summary>
    internal sealed record ReachBandData(string Name, float YMin, float YMax, float? MinZ, float? MaxZ);
    /// <summary>One hit's reach summary: per-band extent + per-tick capsules. Reach = max MaxZ over bands, null if all null.</summary>
    internal sealed record ReachHitData(string Label, string Slot, int HitIndex, string Ability,
        float? Reach, ReachBandData[] Bands, ReachCapsuleData[] Capsules);
    /// <summary>A whiff zone: within one height band, nothing covers From–To forward (below the kit's max reach at that height).</summary>
    internal sealed record ReachGapData(string Band, float From, float To, float HMin, float HMax);
    internal sealed record ReachData(float CharHeight, float CapsuleRadius, float CellSize, float GapStep, float ZMin, float ZMax,
        ReachHitData[] Hits, ReachGapData[] Gaps, string[] UncoveredBands); // ZMin/ZMax = common overlay range

    internal static readonly SlotRef G1 = new(false, 1), G2 = new(false, 2), G3 = new(false, 3), G4 = new(false, 4);
    internal static readonly SlotRef A1 = new(true, 1), A2 = new(true, 2), A3 = new(true, 3), A4 = new(true, 4);

    /// <summary>Default combo routes — the designed FightGuy juggle/kill links.</summary>
    internal static readonly Route[] DefaultRoutes =
    {
        new("g3 Uppercut -> a1 Double Punch",      G3, 0, A1, 0),
        new("g3 Uppercut -> a2 Floating Kick",     G3, 0, A2, 0),
        new("g3 Uppercut -> a3 High Kick",         G3, 0, A3, 0),
        new("g3 Uppercut -> g2 Roundhouse",        G3, 0, G2, 0),
        new("a1 Double Punch h1 -> h2",            A1, 0, A1, 1),
        new("a2 Floating Kick sweet -> weak",      A2, 0, A2, 1),
        new("g1 Low Kick -> g2 Roundhouse",        G1, 0, G2, 0),
        new("g4 Tornado -> a1 Double Punch",       G4, 0, A1, 0),
        new("a2 Floating Kick -> land -> g1 jab",  A2, 1, G1, 0),
    };

    /// <summary>Combo routes per character — authored designed juggle/kill links. Non-FightGuy
    /// characters default to empty until their links are designed; the combo sections
    /// auto-omit when a character has no routes.</summary>
    internal static Route[] RoutesFor(string which) => which.ToLowerInvariant() switch
    {
        "fightguy" => DefaultRoutes,
        _ => Array.Empty<Route>(),
    };

    internal static MatchContentEntry ResolveEntry(string which)
    {
        return which.ToLowerInvariant() switch
        {
            "fightguy" => BuiltInContentResolver.Resolve(CharacterClass.FightGuy),
            "manki"    => BuiltInContentResolver.Resolve(CharacterClass.Manki),
            "wibou"    => BuiltInContentResolver.Resolve(CharacterClass.Wibou),
            "bonk"     => BuiltInContentResolver.Resolve(CharacterClass.Bonk),
            var c => throw new ArgumentException(
                $"unknown character: {c} (expected one of: fightguy, manki, wibou, bonk)"),
        };
    }

    internal static CharacterDefinition ResolveCharacter(string which)
        => ResolveEntry(which).Definition;

    internal static int Main(string[] args)
    {
        if (args.Contains("--route-search") || args.Contains("--route-replay"))
            return ComboRouteReport.Run(args);

        if (args.Contains("--coverage"))
            return ContactCoverageReport.Run(args);

        string which = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "fightguy";
        int? trajSlot = ParseTraj(args);
        int[] pcts = ParsePcts(args) ?? (trajSlot.HasValue
            ? new[] { 0, 10, 20, 30, 40, 50, 60 }   // --traj: 10% steps, low-% range
            : new[] { 0, 30, 60, 90, 120 });
        bool example = args.Contains("--example");
        string? outPath = ParseOut(args) ?? $"docs/generated/{which}-move-data.md";

        var entry = ResolveEntry(which);
        var def = entry.Definition;
        var routes = RoutesFor(which);

        var hits = CollectHits(def);
        if (example)
        {
            var representative = hits.FirstOrDefault(h => !h.Slot.Air && h.Slot.Slot == 2 && h.HitIndex == 0)
                ?? hits.FirstOrDefault();
            hits = representative == null ? new List<HitSpec>() : new List<HitSpec> { representative };
        }

        var baked = entry.BakedAnimation;

        // --truecombos: freeform true-combo reachability graph (real sim, per starter × hit state
        // × %, which follow-ups connect while the victim is still in hitstun) + combo density.
        // --di: DI escape-space — each trajectory re-run with the victim holding DI in/away/up/down
        // during hitstun (Simulation.ApplyDirectionalInfluence, 18° cap), with angular deviation.
        bool trueCombos = args.Contains("--truecombos");
        bool di = args.Contains("--di");
        // --reach: authored hitbox reach chart (issue #151) — per-move side-view overlays,
        // per-height-band range ladder, coverage gaps + uncovered bands. Composes with
        // --json/--html and appends a markdown section on the default path.
        bool reach = args.Contains("--reach");

        // --kbm <model>: knockback-tuning lab. Supplies immutable combat settings so the
        // combo matrix + trajectories re-run under a candidate curve. base = shipped
        // (the adopted Melee shape). old = the pre-adoption curve.
        //   old        — pre-adoption (stun 0.5·mag, KV×0.14 — zero true combos)
        //   stunx18    — hitstun ×1.8, travel unchanged            (stun 0.9·mag)
        //   kv70       — travel −50% (KV×0.71), stun unchanged      (scale 0.10)
        //   stun16kv11 — Melee-ish ratio: stun ×1.6 + KV×0.79      (0.8 / 0.11)
        //   floor30    — Melee "+18"-style floor: stun from mag+30  (0.5 / 0.14 / +30)
        //   melee      — Melee shape: stun 0.4×mag, KV×0.19         (0.4 / 0.19 / 0)
        //   melee-hot  — hotter: stun 0.4×mag, KV×0.22              (0.4 / 0.22 / 0)
        //   melee-soft — softer: stun 0.45×mag, KV×0.17             (0.45 / 0.17 / 0)
        // Profile table lives in Shared (TuningProfiles) — shared with AbDiffReport + tests.
        string kbm = ParseArg(args, "--kbm") ?? "base";
        if (!TuningProfiles.TryGet(kbm, out var combatTuning))
        {
            if (kbm != "base")
                Console.Error.WriteLine($"warn: unknown --kbm '{kbm}' (base|old|stunx18|kv70|stun16kv11|floor30) — using base");
        }
        if (kbm != "base")
            Console.Error.WriteLine($"kbm={kbm}: stun={combatTuning.HitstunCoefficient:0.0}×(mag+{combatTuning.HitstunMagnitudeBonus:0}), KV×{combatTuning.KnockbackScale:0.00}");

        // --json / --html: lossless structured report + self-contained visual report.
        // Both come from one richer collection (per-tick arcs + KO analysis); markdown path is untouched.
        string? jsonPath = ParseArg(args, "--json");
        string? htmlPath = ParseArg(args, "--html");
        if (jsonPath != null || htmlPath != null)
        {
            var report = BuildReport(def, hits, pcts, baked, trueCombos, di, reach, combatTuning);
            if (jsonPath != null)
            {
                var dir = Path.GetDirectoryName(jsonPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(jsonPath, ToJson(report));
                Console.Error.WriteLine($"wrote {jsonPath}");
            }
            if (htmlPath != null)
            {
                var dir = Path.GetDirectoryName(htmlPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(htmlPath, ToHtml(report));
                Console.Error.WriteLine($"wrote {htmlPath}");
            }
            return 0;
        }

        // --dll <path>: loads the given SlopArena.Shared.dll file IN ISOLATION (own load
        // context) and runs its Simulation.ApplyKnockback for the g2 formula. Verifies
        // that the DLL file on disk is the build we think it is — the editor can run a
        // stale in-memory copy while the file is fresh.
        string? dllPath = ParseArg(args, "--dll");
        if (dllPath != null)
        {
            ProbeDll(dllPath);
            return 0;
        }

        // --pipe: full-pipeline launch diagnostic — the victim is hit by the REAL hitbox
        // (inputs + baked bones → ResolveHits → queued launch), then the applied KV and
        // travel to landing are printed. Compares the in-game path to the direct
        // ApplyKnockback trajectory rows.
        if (args.Contains("--pipe"))
        {
            PipeLaunch(entry, combatTuning);
            return 0;
        }

        // --shape [step]: the knockback feel surface. For EVERY hitbox (normals + aerials)
        // at each %, prints the real sampled arc — height above ground, horizontal travel,
        // vertical/horizontal velocity, and phase (H=hitstun, F=flight, A=apex, G=landed) —
        // sampled every `step` ticks (default 12 ≈ 0.2s) so the shape reads without per-tick
        // noise. This is the report's combo-free view of how far, how high, and at what angle
        // each hit launches at a given %. Also prints a one-line summary (KV, launch angle,
        // stun in seconds) per block.
        string? shapeStepArg = ParseArg(args, "--shape");
        if (shapeStepArg != null)
        {
            int step = int.TryParse(shapeStepArg, out int s) && s > 0 ? s : 12;
            DumpShapes(def, hits, pcts, step, combatTuning);
            return 0;
        }

        // --traj: raw per-tick launch trace only (no report/doc). Verifies the in-game
        // feel against the sim numbers without the summary tables.
        if (trajSlot.HasValue)
        {
            var h = hits.First(x => x.Slot.Air == false && x.Slot.Slot == trajSlot.Value && x.HitIndex == 0);
            DumpTrajectory(def, h, pcts, combatTuning);
            return 0;
        }

        var runs = new Dictionary<(SlotRef, int, int), RunResult>();
        foreach (var h in hits)
        foreach (var p in pcts)
            runs[(h.Slot, h.HitIndex, p)] = RunTrajectory(def, h, p, combatTuning);

        // Pipeline parity: the game launches through ResolveHits → freeze queue, not a
        // direct ApplyKnockback call. Run BOTH paths and compare — a drift here means the
        // game behaves differently from this report (the 2026-08-14 x87 float bug was
        // exactly this divergence, invisible to .NET: every hit took the unscaled force path).
        var parity = ComputeParity(def, hits, runs, pcts, baked, combatTuning);
        foreach (var p in parity)
            if (!p.Ok)
                Console.Error.WriteLine($"PARITY DIVERGENCE: {p.Label}@{p.Pct}% direct KV {p.DirectKv:F2} vs pipeline {p.PipeKv:F2}");

        if (args.Contains("--parity"))
        {
            PrintParity(parity);
            return 0;
        }
        // Combo matrix + movement probes are opt-in (--combos). They encode designed
        // route *ideas*, not a feel contract — default output is pure knockback data.
        bool combos = args.Contains("--combos");
        var probeRoutes = Array.Empty<Route>();
        var probes = new Dictionary<(SlotRef, SlotRef, int), (ProbeVerdict Verdict, int ConnectTick)>();
        if (combos)
        {
            probeRoutes = routes.Where(r => r.From != r.To).ToArray();
            var starterSpawns = new Dictionary<SlotRef, (float AtkY, float NpcY, float NpcZ)>();
            foreach (var r in probeRoutes)
            {
                if (starterSpawns.ContainsKey(r.From)) continue;
                var spawn = CalibrateStarter(def, r.From, baked, combatTuning);
                if (spawn == null)
                    Console.Error.WriteLine($"probe {r.Label}: no starter placement connects — route will report NO");
                else
                    starterSpawns[r.From] = spawn.Value;
            }
            foreach (var r in probeRoutes)
            foreach (var p in pcts)
            {
                if (!starterSpawns.TryGetValue(r.From, out var spawn))
                    probes[(r.From, r.To, p)] = (ProbeVerdict.None, -1);
                else
                    probes[(r.From, r.To, p)] = RunProbe(def, r, p, baked, spawn, combatTuning);
            }
        }

        (ComboStarterData[], ComboDensityData[])? comboGraph = null;
        if (trueCombos) comboGraph = ComputeTrueComboGraph(def, hits, pcts, baked, combatTuning);
        Dictionary<(SlotRef, int), DiEscapeData[]>? diData = di ? ComputeDiEscape(def, hits, pcts, NoRespawn(BuildArena()), combatTuning) : null;
        string md = BuildMarkdown(def, hits, runs, pcts, routes, probeRoutes, probes, parity, which, combos,
            comboGraph, diData, reach ? BuildReach(def, hits, baked) : null);
        // Isolated recovery-move record (FightGuy E) — attacker self-motion + victim launch,
        // outside the normals machinery (no parity / reach / combo pollution).
        if (RunRecoveryTrace(def, combatTuning) is { } recovery)
        {
            var launchSpec = RecoveryHitSpec(def);
            var launches = pcts.Select(p => (Pct: p, R: RunTrajectory(def, launchSpec, p, combatTuning))).ToList();
            md += BuildRecoveryMarkdown(recovery, launches);
        }
        Console.WriteLine(md);
        if (outPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, md);
            Console.Error.WriteLine($"wrote {outPath}");
        }
        return 0;
    }

    // ── Data collection ──────────────────────────────────────────────────────

    internal static List<HitSpec> CollectHits(CharacterDefinition def)
    {
        var hits = new List<HitSpec>();
        foreach (var (slot, air) in new[] { (G1, false), (G2, false), (G3, false), (G4, false),
                                            (A1, true), (A2, true), (A3, true), (A4, true) })
        {
            AbilitySpec ability = air
                ? slot.Slot switch
                {
                    1 => def.AirSlot1, 2 => def.AirSlot2, 3 => def.AirSlot3, _ => def.AirSlot4,
                }
                : slot.Slot switch
                {
                    1 => def.Slot1, 2 => def.Slot2, 3 => def.Slot3, _ => def.Slot4,
                };
            if (ability == null || ability.Stages.Length == 0) continue;
            var stage = ability.Stages[0];
            if (stage.HitboxEvents == null || stage.HitboxEvents.Length == 0)
            {
                Console.Error.WriteLine($"warn: {ability.Name} has no hitbox events — skipped");
                continue;
            }
            for (int i = 0; i < stage.HitboxEvents.Length; i++)
            {
                var hit = stage.HitboxEvents[i];
                // Every move is included regardless of knockback profile. Resolve the launch triple:
                // Custom/Adaptive carry their own base/growth (and Adaptive's authored angle is used as
                // a documented representative — see the report caveat); named profiles resolve from the
                // profile table.
                var kb = hit.Knockback;
                bool adaptive = kb.Profile == KnockbackProfile.Adaptive;
                sbyte angle; float baseKb, growthKb;
                if (adaptive || kb.Profile == KnockbackProfile.Custom)
                {
                    angle = kb.Angle; baseKb = kb.BaseKnockback; growthKb = kb.KnockbackGrowth;
                }
                else
                {
                    var r = kb.Resolve(); angle = r.angle; baseKb = r.baseKB; growthKb = r.growthKB;
                }
                hits.Add(new HitSpec(slot, ability, stage, hit, i, angle, baseKb, growthKb, adaptive));
            }
        }
        return hits;
    }

    // ── Recovery-move record (FightGuy E — Melee up-B analog) ─────────────────

    internal sealed record RecoveryData(string Name, ushort DurationTicks, ushort TriggerTick, ushort ActiveUntilTick,
        float Damage, sbyte Angle, float BaseKb, float GrowthKb, ushort StunTicks, ushort CooldownTicks,
        float RiseSpeed, ushort RiseTicks, ushort RiseDelay,
        float RiseHeight, float ApexGain, float PeakVY, int RiseWindowTicks, int ActionableTick, int DurationObserved);

    /// <summary>
    /// Records a recovery-designated move (FightGuy E, IsRecoveryMove) through the REAL sim: activates
    /// the slot on a grounded entity and traces the ATTACKER's own rise — the Melee up-B recovery
    /// contract (SET velocity + sustained rise window, then gravity). This self-motion is the one thing
    /// a victim-knockback report can't show, so it lives in an isolated section, NOT in the normal-tier
    /// `hits` list — no parity / reach / combo pollution. Returns null when the kit has no recovery move.
    /// </summary>
    internal static RecoveryData? RunRecoveryTrace(CharacterDefinition def, CombatTuning? combatTuning = null)
    {
        var spec = def.E;
        if (spec == null || !spec.IsRecoveryMove) return null;

        // E = ActiveSlot byte 4 (grounded and air share the cooked slot).
        const byte activeSlot = 4;

        var sim = new ServerSimulation(BuildArena(), combatTuning: combatTuning);
        float groundY = def.CapsuleHeight * 0.5f;
        var state = new CharacterState
        {
            PX = 0f, PY = groundY, PZ = 0f, IsGrounded = true,
            State = ActionState.Idle, FacingYaw = 0f,
        };
        sim.RegisterEntity(1, def, state);

        float riseSpeed = spec.Params != null && spec.Params.TryGetValue("rise_speed", out var rs) ? rs : 11f;
        ushort riseTicks = spec.Params != null && spec.Params.TryGetValue("rise_ticks", out var rt) ? (ushort)rt : (ushort)12;
        ushort riseDelay = spec.Params != null && spec.Params.TryGetValue("rise_delay", out var rd) ? (ushort)rd : (ushort)8;

        float startPy = groundY, maxPy = groundY, peakVY = 0f;
        int riseWindow = 0, actionableTick = -1, durationObserved = 0;
        bool wasAttacking = false;
        for (int t = 0; t < MaxTicks; t++)
        {
            // Press E on tick 0, then release.
            sim.Tick(t == 0 ? new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = activeSlot } }
                            : new Dictionary<ulong, InputState> { [1] = default });
            var s = sim.GetState(1);
            if (s.PY > maxPy) maxPy = s.PY;
            if (s.VY > peakVY) peakVY = s.VY;
            if (s.State == ActionState.Attacking)
            {
                wasAttacking = true;
                durationObserved++;
                // The sustained-rise window: VY held at (or above) rise_speed while locked.
                if (s.VY >= riseSpeed * 0.99f) riseWindow++;
            }
            else if (wasAttacking && actionableTick < 0)
            {
                actionableTick = t; // ability ended → control returned on this tick
            }
        }

        ushort trigger = spec.Stages is { Length: > 0 } && spec.Stages[0].HitboxEvents is { Length: > 0 }
            ? spec.Stages[0].HitboxEvents[0].TriggerTick : (ushort)0;
        var hit = spec.Stages is { Length: > 0 } && spec.Stages[0].HitboxEvents is { Length: > 0 }
            ? spec.Stages[0].HitboxEvents[0] : default;
        ushort duration = spec.Stages is { Length: > 0 } ? spec.Stages[0].DurationTicks : (ushort)0;

        return new RecoveryData(
            spec.Name, duration, trigger, (ushort)(trigger + hit.DurationTicks),
            hit.Damage, hit.Knockback.Angle, hit.Knockback.BaseKnockback, hit.Knockback.KnockbackGrowth,
            hit.StunTicks, spec.CooldownTicks,
            riseSpeed, riseTicks, riseDelay,
            maxPy - startPy, maxPy - startPy, peakVY, riseWindow, actionableTick, durationObserved);
    }

    /// <summary>
    /// E's first hitbox as a synthetic HitSpec so the recovery section can reuse the normal
    /// RunTrajectory machinery (real ApplyKnockback + sim) for the VICTIM launch. Custom
    /// profile → authored angle/base/growth. All three E hitboxes share the same KB data.
    /// SlotRef slot 100 keeps it clear of the normal-tier 1-4 numbering.
    /// </summary>
    internal static HitSpec RecoveryHitSpec(CharacterDefinition def)
    {
        var spec = def.E!;
        var stage = spec.Stages![0];
        var evt = stage.HitboxEvents![0];
        return new HitSpec(new SlotRef(false, 100), spec, stage, evt, 0,
            evt.Knockback.Angle, evt.Knockback.BaseKnockback, evt.Knockback.KnockbackGrowth, false);
    }

    internal static string BuildRecoveryMarkdown(RecoveryData r, List<(int Pct, RunResult R)> launches)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Recovery move — " + r.Name + " (E)");
        sb.AppendLine();
        sb.AppendLine("The attacker's own rise through the REAL sim (grounded cast) — the Melee up-B recovery");
        sb.AppendLine("contract: VY SET to rise_speed, held for rise_ticks after a grounded rise_delay");
        sb.AppendLine("windup (air cast rises immediately), then gravity bounds the arc.");
        sb.AppendLine();
        sb.AppendLine("| rise_speed m/s | rise_ticks | rise_delay | rise height m | apex gain m | peak VY m/s | rise window ticks | actionable tick | duration observed |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        sb.AppendLine($"| {r.RiseSpeed:F0} | {r.RiseTicks} | {r.RiseDelay} | {r.RiseHeight:F2} | {r.ApexGain:F2} | {r.PeakVY:F2} | {r.RiseWindowTicks} | {r.ActionableTick} | {r.DurationObserved} |");
        sb.AppendLine();
        sb.AppendLine("Authored (spec):");
        sb.AppendLine();
        sb.AppendLine("| trigger | active | damage | angle | base KB | growth | stun | duration | cooldown |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        sb.AppendLine($"| {r.TriggerTick} | {r.TriggerTick}-{r.ActiveUntilTick - 1} | {r.Damage:F0} | {r.Angle} | {r.BaseKb:F0} | {r.GrowthKb:F0} | {r.StunTicks} | {r.DurationTicks} | {r.CooldownTicks} |");
        sb.AppendLine();
        sb.AppendLine("Victim launch (real sim, no DI, post-hit %):");
        sb.AppendLine();
        sb.AppendLine("| victim % | KV m/s | hitstop | stun | adv | rise@stun m | drift@stun m | apex m | actionable | landed |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        int advDenominator = r.DurationTicks - r.TriggerTick; // attacker locked until duration ends
        foreach (var (pct, R) in launches)
            sb.AppendLine($"| {pct} | {R.Kv:F1} | {R.Hitstop} | {R.Stun} | {R.Stun - advDenominator} | {R.Rise:F2} | {R.Drift:F2} | {R.Apex:F2} | {R.ActionableTick} | {R.LandedTick?.ToString() ?? "—"} |");
        sb.AppendLine();
        return sb.ToString();
    }

    // ── Reach chart collection (issue #151) ──────────────────────────────────

    /// <summary>Vertical sampling step for the gap scan, metres.</summary>
    internal const float ReachGapStep = 0.1f;
    /// <summary>Gaps narrower than this are below-cell noise and dropped, metres.</summary>
    internal const float ReachMinGapWidth = 0.05f;

    /// <summary>Tool SlotRef (g1–g4 / a1–a4) → GetSlotAbility slot index (Slot1=2, Slot2=6,
    /// Slot3=7, Slot4=8; same indices airborne → AirSlotN), matching CollectHits.</summary>
    internal static byte SlotIndex(SlotRef s) => (byte)(s.Slot switch { 1 => 2, 2 => 6, 3 => 7, _ => 8 });

    /// <summary>Reach height bands: thirds of CapsuleHeight from the feet — low [0, H/3),
    /// mid [H/3, 2H/3), high [2H/3, H + 0.5] (0.5 m headroom so above-head coverage counts as high).</summary>
    internal static (string Name, float YMin, float YMax)[] ReachBandsFor(CharacterDefinition def)
    {
        float h = def.CapsuleHeight;
        return new[] { ("low", 0f, h / 3f), ("mid", h / 3f, 2f * h / 3f), ("high", 2f * h / 3f, h + 0.5f) };
    }

    /// <summary>
    /// Deterministic authored-hitbox reach chart: every collected normal's capsule union per
    /// height band (via <see cref="MoveReach"/> over <see cref="HitboxGeometry.ResolvePositions"/>),
    /// the common overlay range, the kit's whiff zones (per band, per height row: holes in
    /// [0, maxReachAtY] across move envelopes, merged over consecutive rows), and bands no
    /// normal reaches at all.
    /// </summary>
    internal static ReachData BuildReach(CharacterDefinition def, List<HitSpec> hits, BakedAnimationData? baked)
    {
        var sampleCache = new Dictionary<(SlotRef, int), MoveReach.CapsuleSample[]>();
        foreach (var h in hits)
            sampleCache[(h.Slot, h.HitIndex)] = MoveReach.SampleHit(def, h.Hit, SlotIndex(h.Slot), h.Slot.Air,
                h.Ability.AnimationNames, 0, baked);

        // Common overlay range: min/max endpoint Z over all samples, widened to at least [-1, 3].
        float zMin = float.PositiveInfinity, zMax = float.NegativeInfinity;
        foreach (var samples in sampleCache.Values)
        foreach (var c in samples)
        {
            float lo = MathF.Min(c.Z0, c.Z1), hi = MathF.Max(c.Z0, c.Z1);
            if (lo < zMin) zMin = lo;
            if (hi > zMax) zMax = hi;
        }
        zMin = MathF.Min(zMin - 0.2f, -1.0f);
        zMax = MathF.Max(zMax + 0.2f, 3.0f);

        var bands = ReachBandsFor(def);
        var hitData = new List<ReachHitData>();
        foreach (var h in hits)
        {
            var samples = sampleCache[(h.Slot, h.HitIndex)];
            var hitBands = new ReachBandData[bands.Length];
            float? reach = null;
            for (int i = 0; i < bands.Length; i++)
            {
                var ext = MoveReach.BandExtent(samples, bands[i].YMin, bands[i].YMax);
                hitBands[i] = new ReachBandData(bands[i].Name, bands[i].YMin, bands[i].YMax, ext?.MinZ, ext?.MaxZ);
                if (ext != null && (reach == null || ext.Value.MaxZ > reach)) reach = ext.Value.MaxZ;
            }
            hitData.Add(new ReachHitData(Label(h), SlotName(h.Slot), h.HitIndex, h.Ability.Name, reach, hitBands,
                samples.Select(c => new ReachCapsuleData(c.Tick, c.X0, c.Y0, c.Z0, c.X1, c.Y1, c.Z1, c.Radius)).ToArray()));
        }

        // Per-move envelopes: group hit samples by slot (concatenated — ExtentAt unions them).
        var moveUnions = new Dictionary<SlotRef, MoveReach.CapsuleSample[]>();
        foreach (var grp in hits.GroupBy(h => h.Slot))
            moveUnions[grp.Key] = grp.SelectMany(h => sampleCache[(h.Slot, h.HitIndex)]).ToArray();

        // Whiff zones: per band, per y-row, covered intervals across moves clamp to
        // [0, maxReachAtY]; holes in that range are gaps (narrower than ReachMinGapWidth
        // dropped). Consecutive rows whose (From, To) agree within ReachMinGapWidth merge
        // into one gap spanning HMin–HMax.
        var gaps = new List<ReachGapData>();
        var uncovered = new List<string>();
        foreach (var band in bands)
        {
            var rows = new List<(float Y, float From, float To)>();
            bool anyCoverage = false;
            int n = (int)Math.Ceiling((band.YMax - band.YMin) / ReachGapStep);
            for (int i = 0; i <= n; i++)
            {
                float y = MathF.Min(band.YMin + i * ReachGapStep, band.YMax);
                var intervals = new List<(float Min, float Max)>();
                float maxReach = 0f;
                foreach (var union in moveUnions.Values)
                {
                    var ext = MoveReach.ExtentAt(union, y);
                    if (ext != null)
                    {
                        intervals.Add((ext.Value.MinZ, ext.Value.MaxZ));
                        if (ext.Value.MaxZ > maxReach) maxReach = ext.Value.MaxZ;
                    }
                }
                if (maxReach <= 0f) continue;
                anyCoverage = true;
                for (int k = 0; k < intervals.Count; k++)
                {
                    var iv = intervals[k];
                    intervals[k] = (Math.Clamp(iv.Min, 0f, maxReach), Math.Clamp(iv.Max, 0f, maxReach));
                }
                intervals.Sort((a, b) => a.Min.CompareTo(b.Min));
                var merged = new List<(float Min, float Max)>();
                foreach (var iv in intervals)
                {
                    if (merged.Count > 0 && iv.Min <= merged[^1].Max + 1e-4f)
                        merged[^1] = (merged[^1].Min, MathF.Max(merged[^1].Max, iv.Max));
                    else merged.Add(iv);
                }
                float cursor = 0f;
                foreach (var iv in merged)
                {
                    if (iv.Min > cursor + ReachMinGapWidth) rows.Add((y, cursor, iv.Min));
                    if (iv.Max > cursor) cursor = iv.Max;
                }
                if (cursor < maxReach - ReachMinGapWidth) rows.Add((y, cursor, maxReach));
            }
            if (!anyCoverage) { uncovered.Add(band.Name); continue; }
            int ri = 0;
            while (ri < rows.Count)
            {
                int rj = ri;
                while (rj + 1 < rows.Count
                    && MathF.Abs(rows[rj + 1].From - rows[rj].From) <= ReachMinGapWidth
                    && MathF.Abs(rows[rj + 1].To - rows[rj].To) <= ReachMinGapWidth)
                    rj++;
                var first = rows[ri]; var last = rows[rj];
                gaps.Add(new ReachGapData(band.Name, first.From, first.To, first.Y, last.Y + ReachGapStep));
                ri = rj + 1;
            }
        }
        return new ReachData(def.CapsuleHeight, def.CapsuleRadius, MoveReach.CellSize, ReachGapStep,
            zMin, zMax, hitData.ToArray(), gaps.ToArray(), uncovered.ToArray());
    }

    /// <summary>Ladder cell: "0.56" or the null marker ("—" markdown / "&amp;mdash;" HTML) when unreached.</summary>
    internal static string ReachCell(float? v, string nullMark = "—")
        => v == null ? nullMark : F(v.Value);

    /// <summary>
    /// Launch a fresh grounded victim with the hitbox's authored knockback, then step
    /// the real sim until landing. The % column is the victim's damage BEFORE the hit;
    /// the launch itself is computed at pct + damage — the game applies the damage
    /// first, so the queued launch sees the post-hit percent (verified by the pipeline
    /// parity section). No DI/SDI input; hitstop is reported but not simulated.
    /// </summary>
    internal static RunResult RunTrajectory(CharacterDefinition def, HitSpec h, int pct, CombatTuning? combatTuning = null)
    {
        var sim = new ServerSimulation(BuildArena(), combatTuning: combatTuning);
        float groundY = def.CapsuleHeight * 0.5f;
        var state = new CharacterState
        {
            PX = 0f, PY = groundY, PZ = 0f, IsGrounded = true,
            State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)(pct + (int)h.Hit.Damage),
        };
        Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
            h.BaseKb, h.GrowthKb,
            h.Hit.Damage, h.Hit.StunTicks, def.Weight, combatTuning: combatTuning);
        float kv = MathF.Sqrt(state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ);
        ushort stun = state.HitstunTicks;
        ushort hitstop = ServerSimulation.ComputeHitstopTicks(h.Hit.Damage, null);
        sim.RegisterEntity(1, def, state);

        var inputs = new Dictionary<ulong, InputState> { [1] = default };
        float maxPy = state.PY;
        int actionable = -1;
        int? landed = null;
        float rise = 0f, drift = 0f;
        for (int t = 0; t < MaxTicks; t++)
        {
            sim.Tick(inputs);
            var s = sim.GetState(1);
            if (s.PY > maxPy) maxPy = s.PY;
            if (actionable < 0 && s.HitstunTicks == 0)
            {
                actionable = t + 1;
                rise = s.PY - groundY;
                drift = s.PZ;
            }
            if (actionable >= 0 && s.IsGrounded)
            {
                landed = t + 1;
                break;
            }
        }
        return new RunResult(kv, hitstop, stun, rise, drift, maxPy - groundY, actionable, landed);
    }

    /// <summary>
    /// Per-tick launch trace: the same real launch (ApplyKnockback + sim.Tick) as
    /// RunTrajectory, but prints every tick so the shape can be compared to in-game
    /// feel. CSV columns: tick, height above ground (m), horizontal travel (m, +Z),
    /// vertical velocity, horizontal velocity, phase flag (H=hitstun, F=flight,
    /// A=apex tick, G=landed).
    /// </summary>
    internal static void DumpTrajectory(CharacterDefinition def, HitSpec h, int[] pcts, CombatTuning? combatTuning = null)
    {
        foreach (var pct in pcts)
        {
            var sim = new ServerSimulation(BuildArena(), combatTuning: combatTuning);
            float groundY = def.CapsuleHeight * 0.5f;
            var state = new CharacterState
            {
                PX = 0f, PY = groundY, PZ = 0f, IsGrounded = true,
                State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)pct,
            };
            Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
                h.BaseKb, h.GrowthKb,
                h.Hit.Damage, h.Hit.StunTicks, def.Weight, combatTuning: combatTuning);
            sim.RegisterEntity(1, def, state);
            var inputs = new Dictionary<ulong, InputState> { [1] = default };

            Console.WriteLine($"\n=== {Label(h)} at {pct}% ===");
            Console.WriteLine("tick,height(m),travel(m),vY,vX,phase");
            float maxPy = state.PY;
            bool apexMarked = false;
            for (int t = 0; t < MaxTicks; t++)
            {
                sim.Tick(inputs);
                var s = sim.GetState(1);
                if (s.PY > maxPy) maxPy = s.PY;
                else if (!apexMarked && s.HitstunTicks == 0 && !s.IsGrounded)
                {
                    Console.WriteLine($"{t + 1},{s.PY - groundY:F2},{s.PZ:F2},{s.VY:F2},{s.VX:F2},A");
                    apexMarked = true;
                }
                char phase = s.IsGrounded ? 'G'
                    : s.HitstunTicks > 0 ? 'H'
                    : 'F';
                float vy = s.HitstunTicks > 0 ? s.KVY : s.VY;
                float vz = s.HitstunTicks > 0 ? s.KVZ : s.VZ;
                Console.WriteLine($"{t + 1},{s.PY - groundY:F2},{s.PZ:F2},{vy:F2},{vz:F2},{phase}");
                if (phase == 'G') break;
            }
        }
    }

    /// <summary>
    /// Knockback feel surface for every hitbox (normals + aerials) at each %: prints the
    /// real sampled arc — height above ground, horizontal travel, V/H velocity, phase
    /// (H=hitstun, F=flight, A=apex, G=landed) — sampled every `step` ticks (default 12 ≈
    /// 0.2s). Uses the post-hit % (pct + damage) so rows match the Per-hit trajectories.
    /// This is the combo-free view: how far, how high, and at what angle each hit sends.
    /// </summary>
    internal static void DumpShapes(CharacterDefinition def, List<HitSpec> hits, int[] pcts, int step,
        CombatTuning? combatTuning = null)
    {
        foreach (var h in hits)
        foreach (var pct in pcts)
        {
            var sim = new ServerSimulation(BuildArena(), combatTuning: combatTuning);
            float groundY = def.CapsuleHeight * 0.5f;
            var state = new CharacterState
            {
                PX = 0f, PY = groundY, PZ = 0f, IsGrounded = true,
                State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)(pct + (int)h.Hit.Damage),
            };
            Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
                h.BaseKb, h.GrowthKb,
                h.Hit.Damage, h.Hit.StunTicks, def.Weight, combatTuning: combatTuning);
            float kv = MathF.Sqrt(state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ);
            ushort stun = state.HitstunTicks;
            sim.RegisterEntity(1, def, state);
            var inputs = new Dictionary<ulong, InputState> { [1] = default };

            Console.WriteLine($"\n=== {Label(h)} hit {h.HitIndex + 1} at {pct}% ===");
            Console.WriteLine($"launch {kv:F2} m/s  stun {stun} ticks ({stun / 60f:F2}s)  hitstop {ServerSimulation.ComputeHitstopTicks(h.Hit.Damage, null)}");
            Console.WriteLine("tick,height(m),travel(m),vY,vX,phase");
            float maxPy = state.PY;
            bool apexMarked = false;
            int apexTick = -1;
            for (int t = 0; t < MaxTicks; t++)
            {
                sim.Tick(inputs);
                var s = sim.GetState(1);
                // Apex = the tick after height stops increasing (post-hitstun flight).
                if (!apexMarked && t > 0 && s.PY <= maxPy && !s.IsGrounded && s.HitstunTicks == 0)
                {
                    apexMarked = true;
                    apexTick = t + 1;
                }
                if (s.PY > maxPy) maxPy = s.PY;
                if (s.IsGrounded)
                {
                    Console.WriteLine($"{t + 1},{s.PY - groundY:F2},{s.PZ:F2},0.00,0.00,G");
                    break;
                }
                bool atApex = t + 1 == apexTick;
                // Print the apex tick and every `step`-th tick (plus tick 0-ish via the loop start).
                if (!atApex && t % step != 0) continue;
                char phase = s.HitstunTicks > 0 ? 'H' : atApex ? 'A' : 'F';
                float vy = s.HitstunTicks > 0 ? s.KVY : s.VY;
                float vz = s.HitstunTicks > 0 ? s.KVZ : s.VZ;
                Console.WriteLine($"{t + 1},{s.PY - groundY:F2},{s.PZ:F2},{vy:F2},{vz:F2},{phase}");
            }
        }
    }

    internal sealed record ParityResult(string Label, int Pct, float DirectKv, float PipeKv,
        int DirectStun, int PipeStun, float DirectApex, float PipeApex, bool Ok);
    /// <summary>
    /// Runs the REAL launch path (input → hitbox → ResolveHits → freeze queue → queued
    /// launch) for each slot's first hitbox at 0% and the last requested %, and compares
    /// the applied launch against the direct-ApplyKnockback trajectory rows. The game
    /// must match the tool here; divergence means the report doesn't describe the game.
    /// </summary>
    internal static List<ParityResult> ComputeParity(CharacterDefinition def, List<HitSpec> hits,
        Dictionary<(SlotRef, int, int), RunResult> runs, int[] pcts, BakedAnimationData? baked,
        CombatTuning? combatTuning = null)
    {
        var result = new List<ParityResult>();
        foreach (var h in hits.Where(x => x.HitIndex == 0 && !x.Adaptive))
        foreach (var pct in new[] { pcts[0], pcts[^1] })
        {
            var direct = runs[(h.Slot, 0, pct)];
            var spawn = CalibrateStarter(def, h.Slot, baked, combatTuning);
            if (spawn == null)
            {
                result.Add(new ParityResult(Label(h), pct, direct.Kv, float.NaN, direct.Stun, -1, direct.Apex, float.NaN, false));
                continue;
            }
            var pipe = RunPipelineLaunch(def, h.Slot, pct, baked, spawn.Value, combatTuning);
            if (pipe == null)
            {
                result.Add(new ParityResult(Label(h), pct, direct.Kv, float.NaN, direct.Stun, -1, direct.Apex, float.NaN, false));
                continue;
            }
            bool ok = MathF.Abs(pipe.Value.KvMag - direct.Kv) <= 0.01f * direct.Kv + 0.02f
                && pipe.Value.Hitstun == direct.Stun
                && MathF.Abs(pipe.Value.Apex - direct.Apex) <= 0.03f * direct.Apex + 0.05f;
            result.Add(new ParityResult(Label(h), pct, direct.Kv, pipe.Value.KvMag, direct.Stun, pipe.Value.Hitstun,
                direct.Apex, pipe.Value.Apex, ok));
        }
        return result;
    }

    internal static void PrintParity(List<ParityResult> parity)
    {
        Console.WriteLine("pipeline parity (real hit path vs direct formula):");
        foreach (var p in parity)
            Console.WriteLine($"{(p.Ok ? "OK " : "DIVERGE")} {p.Label,-22} {p.Pct,4}%  direct KV {p.DirectKv,6:F2}  pipe KV {p.PipeKv,6:F2}  " +
                $"stun {p.DirectStun,2}/{p.PipeStun,2}  apex {p.DirectApex,5:F1}/{p.PipeApex,5:F1}");
    }

    /// <summary>
    /// Full-pipeline launch: the slot's starter connects through the real hitbox path
    /// (inputs + baked bones → ResolveHits → hitstop queue → queued launch). Returns the
    /// applied KV magnitude, hitstun, hitstop and apex, or null when the starter whiffs.
    /// </summary>
    internal static (float KvMag, int Hitstun, int Hitstop, float Apex)? RunPipelineLaunch(
        CharacterDefinition def, SlotRef slot, int pct, BakedAnimationData? baked, (float AtkY, float NpcY, float NpcZ) spawn,
        CombatTuning? combatTuning = null)
    {
        var sim = new ServerSimulation(BuildArena(), combatTuning: combatTuning);
        bool air = slot.Air;
        var attacker = new CharacterState
        {
            PX = 0f, PY = spawn.AtkY, PZ = 0f, IsGrounded = !air,
            State = ActionState.Idle, FacingYaw = 0f, JumpsLeft = (byte)(air ? 1 : 2),
        };
        var victim = new CharacterState
        {
            PX = 0f, PY = spawn.NpcY, PZ = spawn.NpcZ, IsGrounded = !air,
            State = ActionState.Idle, FacingYaw = MathF.PI, DamagePercent = (ushort)pct,
        };
        sim.RegisterEntity(1, def, attacker, baked);
        sim.RegisterEntity(100, def, victim, baked);
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = SlotByte(slot.Slot) }, [100] = default };
        bool hit = false, launched = false;
        float kvMag = 0f, apex = victim.PY, launchPy = victim.PY;
        int hitstun = 0, hitstop = 0;
        for (int t = 0; t < 400; t++)
        {
            sim.Tick(inputs);
            var v = sim.GetState(100);
            if (!hit && v.DamagePercent > pct) hit = true;
            if (hit && !launched && v.HitstunTicks > 0)
            {
                launched = true;
                kvMag = MathF.Sqrt(v.KVX * v.KVX + v.KVY * v.KVY + v.KVZ * v.KVZ);
                hitstun = v.HitstunTicks;
                hitstop = v.HitstopTicks;
                launchPy = v.PY;
            }
            if (v.PY > apex) apex = v.PY;
            if (launched && v.HitstunTicks == 0 && v.HitstopTicks == 0 && v.IsGrounded)
                return (kvMag, hitstun, hitstop, apex - launchPy); // apex GAIN — comparable to the ground-launch rows
        }
        return null;
    }

    internal static string? ParseArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>
    /// Loads a SlopArena.Shared.dll file in a dedicated context and reflectively invokes
    /// Simulation.ApplyKnockback with the g2 parameters (base 14, growth 26, dmg 8,
    /// angle 28, stun 20, weight 100, 0%). Prints the KV + hitstun the FILE produces —
    /// independent of whatever the Unity editor has loaded.
    /// </summary>
    internal static void ProbeDll(string dllPath)
    {
        var alc = new System.Runtime.Loader.AssemblyLoadContext("probe", isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var simType = asm.GetType("SlopArena.Shared.Simulation")!;
            Console.WriteLine($"file={Path.GetFullPath(dllPath)}");
            Console.WriteLine($"MVID={asm.ManifestModule.ModuleVersionId}");
            var tuningType = asm.GetType("SlopArena.Shared.CombatTuning");
            var defaults = tuningType?.GetField("Default")?.GetValue(null);
            // Older assemblies kept the scale in a static field; the probe can inspect either layout.
            var scale = defaults != null
                ? tuningType!.GetProperty("KnockbackScale")?.GetValue(defaults)
                : simType.GetField("KbScaleFactor")?.GetValue(null);
            var gField = simType.GetField("FlightGravity");
            Console.WriteLine($"KnockbackScale={(scale?.ToString() ?? "MISSING")} " +
                $"FlightGravity={(gField?.GetValue(null)?.ToString() ?? "MISSING")}");
            var stateType = asm.GetType("SlopArena.Shared.CharacterState")!;
            var state = Activator.CreateInstance(stateType)!;
            Set(stateType, state, "PX", 0f); Set(stateType, state, "PY", 0.85f); Set(stateType, state, "PZ", 0f);
            Set(stateType, state, "IsGrounded", true); Set(stateType, state, "DamagePercent", (ushort)0);
            var apply = simType.GetMethods()
                .FirstOrDefault(m => m.Name == "ApplyKnockback" && m.GetParameters().Length is 10 or 11);
            if (apply == null) { Console.WriteLine("ERR: ApplyKnockback not found"); return; }
            Console.WriteLine($"sig={string.Join(", ", apply.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))}");
            var args = new object?[] { state, 0f, 1f, (sbyte)28, 14f, 26f, 8f, (ushort)20, 100f, true }
                .Concat(apply.GetParameters().Skip(10).Select(p => p.DefaultValue)).ToArray();
            apply.Invoke(null, args);
            state = args[0]!;
            float kvx = (float)Get(stateType, state, "KVX")!;
            float kvy = (float)Get(stateType, state, "KVY")!;
            float kvz = (float)Get(stateType, state, "KVZ")!;
            int hstun = Convert.ToInt32(Get(stateType, state, "HitstunTicks"));
            Console.WriteLine($"KV=({kvx:F2},{kvy:F2},{kvz:F2}) mag={MathF.Sqrt(kvx * kvx + kvy * kvy + kvz * kvz):F2} hitstun={hstun}");
        }
        catch (Exception e) { Console.WriteLine($"ERR {e.GetType().Name}: {e.Message}"); }
        finally { alc.Unload(); }
    }

    internal static void Set(Type t, object o, string name, object value)
        => t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!.SetValue(o, value);
    internal static object? Get(Type t, object o, string name)
        => t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!.GetValue(o);


    internal static int? ParseTraj(string[] args)
    {
        int i = Array.IndexOf(args, "--traj");
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int slot) ? slot : null;
    }

    /// <summary>Kit slot 1-4 → ActiveSlot byte (factory indices: key1=3, key2=7, key3=8, key4=9).</summary>
    internal static byte SlotByte(int slot) => (byte)(slot switch { 1 => 3, 2 => 7, 3 => 8, _ => 9 });

    /// <summary>
    /// Full-pipeline launch: the g2 starter connects through the REAL hitbox path
    /// (inputs + baked bones → ResolveHits → hitstop queue → queued launch). Prints the
    /// victim's applied KV + hitstun + travel to landing — the number the GAME produces,
    /// vs the direct-ApplyKnockback trajectory rows.
    internal static void PipeLaunch(MatchContentEntry entry, CombatTuning? combatTuning = null)
    {
        var def = entry.Definition;
        var baked = entry.BakedAnimation;
        float gpy = def.CapsuleHeight * 0.5f;
        foreach (var z in new[] { 0.8f, 1.0f, 1.2f, 1.5f, 2.0f })
        {
            var sim = new ServerSimulation(BuildArena(), combatTuning: combatTuning);
            var atk = new CharacterState
            {
                PX = 0f, PY = gpy, PZ = 0f, IsGrounded = true,
                State = ActionState.Idle, FacingYaw = 0f, JumpsLeft = 2, DamagePercent = 0,
            };
            var vic = new CharacterState
            {
                PX = 0f, PY = gpy, PZ = z, IsGrounded = true,
                State = ActionState.Idle, FacingYaw = MathF.PI, JumpsLeft = 2, DamagePercent = 0,
            };
            sim.RegisterEntity(1, def, atk, baked);
            sim.RegisterEntity(100, def, vic, baked);
            var inputs = new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = SlotByte(2) }, [100] = default };
            bool hit = false, launched = false;
            for (int t = 0; t < 400; t++)
            {
                sim.Tick(inputs);
                var v = sim.GetState(100);
                if (!hit && v.DamagePercent > 0)
                {
                    hit = true;
                    Console.WriteLine($"z={z} HIT t={t} dmg={v.DamagePercent} hstun={v.HitstunTicks} hitstop={v.HitstopTicks}");
                }
                if (hit && !launched && v.HitstunTicks > 0)
                {
                    launched = true;
                    float kv = MathF.Sqrt(v.KVX * v.KVX + v.KVY * v.KVY + v.KVZ * v.KVZ);
                    Console.WriteLine($"z={z} LAUNCH t={t} hstun={v.HitstunTicks} " +
                        $"KV=({v.KVX:F2},{v.KVY:F2},{v.KVZ:F2}) mag={kv:F2}");
                }
                if (launched && v.HitstunTicks == 0 && v.HitstopTicks == 0 && v.IsGrounded)
                {
                    Console.WriteLine($"z={z} LANDED t={t} P=({v.PX:F2},{v.PY:F2},{v.PZ:F2})");
                    return;
                }
            }
            if (!hit) Console.WriteLine($"z={z} no connect");
        }
    }

    /// <summary>
    /// Find a starter placement where the move's hitbox actually connects (bone pose +
    /// radius reach varies per move and changes with retunes — don't trust stale test
    /// positions). Tries a small grid, returns the first working (attackerY, victimY, victimZ).
    /// </summary>
    internal static (float AtkY, float NpcY, float NpcZ)? CalibrateStarter(CharacterDefinition def, SlotRef from,
        BakedAnimationData? baked, CombatTuning? combatTuning = null)
    {
        float gpy = def.CapsuleHeight * 0.5f;
        var candidates = new List<(float, float, float)>();
        if (!from.Air)
        {
            foreach (var z in new[] { 0.8f, 1.0f, 1.2f, 1.5f, 2.0f })
                candidates.Add((gpy, gpy, z));
        }
        else
        {
            foreach (var (ay, ny) in new[] { (2f, 3f), (2.5f, 3.5f), (2f, 3.5f), (2.5f, 3f), (1.5f, 2.5f), (2f, 2.5f) })
            foreach (var z in new[] { 0.8f, 1.0f, 1.2f, 1.5f, 2.0f })
                candidates.Add((ay, ny, z));
        }
        foreach (var c in candidates)
            if (StarterConnects(def, SlotByte(from.Slot), c, baked, from.Air, from.Air, combatTuning)) return c;
        return null;
    }

    internal static bool StarterConnects(CharacterDefinition def, byte slotByte,
        (float AtkY, float NpcY, float NpcZ) p, BakedAnimationData? baked, bool attackerAir, bool victimAir,
        CombatTuning? combatTuning = null)
    {
        var sim = new ServerSimulation(BuildArena(), combatTuning: combatTuning);
        var attacker = new CharacterState
        {
            PX = 0f, PY = p.AtkY, PZ = 0f, IsGrounded = !attackerAir,
            State = ActionState.Idle, FacingYaw = 0f, JumpsLeft = (byte)(attackerAir ? 1 : 2),
        };
        var victim = new CharacterState
        {
            PX = 0f, PY = p.NpcY, PZ = p.NpcZ, IsGrounded = !victimAir,
            State = ActionState.Idle, FacingYaw = MathF.PI,
        };
        sim.RegisterEntity(1, def, attacker, baked);
        sim.RegisterEntity(100, def, victim, baked);
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = slotByte }, [100] = default };
        for (int t = 0; t < 80; t++)
        {
            sim.Tick(inputs);
            if (sim.GetState(100).DamagePercent > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Calibrate a starter placement for one hit state: the victim is either grounded or
    /// airborne at connect (the two rows the true-combo graph distinguishes). The attacker
    /// is airborne exactly when the starter move is an aerial. Tries a small grid per
    /// scenario and returns the first working placement, or null when the move can't reach
    /// that hit state at all.
    /// </summary>
    internal static ProbeSetup? CalibrateScenario(CharacterDefinition def, HitSpec h, bool victimAir,
        BakedAnimationData? baked, CombatTuning? combatTuning = null)
    {
        float gpy = def.CapsuleHeight * 0.5f;
        bool attackerAir = h.Slot.Air;
        var candidates = new List<(float, float, float)>();
        if (!victimAir)
        {
            if (attackerAir)
            {
                // Falling aerials hit grounded victims from a low jump (SHFFL-style): the
                // attacker spawns above ground and descends through the victim's height during
                // the active window. Low altitudes are what make the connect possible.
                foreach (var ay in new[] { gpy + 0.4f, gpy + 0.7f, gpy + 1.0f, gpy + 1.4f, gpy + 1.8f, gpy + 2.2f })
                foreach (var z in new[] { 0.8f, 1.0f, 1.2f, 1.5f })
                    candidates.Add((ay, gpy, z));
            }
            else
            {
                foreach (var z in new[] { 0.8f, 1.0f, 1.2f, 1.5f, 2.0f })
                    candidates.Add((gpy, gpy, z));
            }
        }
        else
        {
            if (attackerAir)
            {
                foreach (var (ay, ny) in new[] { (2f, 3f), (2.5f, 3.5f), (2f, 3.5f), (2.5f, 3f), (1.5f, 2.5f), (2f, 2.5f) })
                foreach (var z in new[] { 0.8f, 1.0f, 1.2f, 1.5f, 2.0f })
                    candidates.Add((ay, ny, z));
            }
            else
            {
                // Ground moves catching a low airborne target (anti-air / juggle start).
                foreach (var ny in new[] { gpy + 0.8f, gpy + 1.2f, gpy + 1.7f, gpy + 2.2f })
                foreach (var z in new[] { 0.8f, 1.0f, 1.2f, 1.5f })
                    candidates.Add((gpy, ny, z));
            }
        }
        foreach (var c in candidates)
            if (StarterConnects(def, SlotByte(h.Slot.Slot), c, baked, attackerAir, victimAir, combatTuning))
                return new ProbeSetup(c.Item1, c.Item2, c.Item3, attackerAir, victimAir);
        return null;
    }

    /// <summary>Probe verdict: Airborne = follow-up connected while the victim was in flight
    /// (a real juggle); Grounded = connected only after the victim landed (neutral hit on a
    /// passive target — opponent is fully actionable); None = never connected.</summary>
    internal enum ProbeVerdict { None, Airborne, Grounded }

    /// <summary>Placement + air state for one starter scenario (auto-calibrated).</summary>
    internal readonly record struct ProbeSetup(float AtkY, float NpcY, float NpcZ, bool AttackerAir, bool VictimAir);

    /// <summary>Raw outcome of one starter→follow-up attempt in the real sim.</summary>
    internal sealed record ProbeOutcome(bool Connected, int ConnectTick, bool VictimAirborne, int VictimStunAtConnect);

    /// <summary>
    /// Shared follow-up chase core: the attacker performs the starter with real inputs (hit must
    /// connect through the actual hit resolution, bone hitboxes included), then plays a greedy
    /// chase policy — run toward the victim, jump when they're above, buffer the follow-up when
    /// in range. Victim is passive (no DI/SDI). Returns the raw outcome; callers map it to their
    /// own verdict (juggle vs ground hit vs combo-true vs combo-false).
    /// </summary>
    internal static ProbeOutcome RunFollowUpSim(CharacterDefinition def, SlotRef from, SlotRef to, int toTriggerTicks,
        int starterIasaTicks, float followUpLunge, int pct, BakedAnimationData? baked, in ProbeSetup setup,
        CombatTuning? combatTuning = null)
    {
        var sim = new ServerSimulation(BuildArena(), combatTuning: combatTuning);

        bool air = setup.AttackerAir;
        var attacker = new CharacterState
        {
            PX = 0f, PY = setup.AtkY, PZ = 0f, IsGrounded = !air,
            State = ActionState.Idle, FacingYaw = 0f, JumpsLeft = (byte)(air ? 1 : 2),
        };
        var victim = new CharacterState
        {
            PX = 0f, PY = setup.NpcY, PZ = setup.NpcZ, IsGrounded = !setup.VictimAir,
            State = ActionState.Idle, FacingYaw = MathF.PI, DamagePercent = (ushort)pct,
        };
        sim.RegisterEntity(1, def, attacker, baked);
        sim.RegisterEntity(100, def, victim, baked);

        var inputs = new Dictionary<ulong, InputState>();
        int starterConnectedAt = -1;
        bool fired = false;
        ushort damageAtFire = 0;

        for (int t = 0; t < MaxTicks; t++)
        {
            var a = sim.GetState(1);
            var v = sim.GetState(100);
            var input = new InputState();

            if (starterConnectedAt < 0)
            {
                if (t == 0) input.ActiveSlot = SlotByte(from.Slot);
            }
            else if (t > starterConnectedAt)
            {
                bool squatting = a.State == ActionState.JumpSquat;
                // Actionable = no locks, AND (idle/run OR the starter's IASA early-out has
                // passed — the game lets an ability input interrupt the recovery from IASA,
                // even while the state machine is still Attacking).
                bool iasa = a.State == ActionState.Attacking && starterIasaTicks > 0
                    && a.AttackElapsedTicks >= starterIasaTicks;
                bool canAct = a.HitstunTicks == 0 && a.HitstopTicks == 0
                    && a.LandingLagTicks == 0 && a.BurstRecoveryTicks == 0
                    && (a.AnimLockTicks == 0 || iasa)
                    && (a.State == ActionState.Idle || a.State == ActionState.Run || iasa);
                if (squatting)
                {
                    // hold through the squat for a full jump
                    input.Jump = true;
                    input.JumpHeld = true;
                }
                else if (canAct)
                {
                    float dx = v.PX - a.PX, dz = v.PZ - a.PZ;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    float dy = v.PY - a.PY;
                    // Aerials have short reach — get in close; ground moves with a lunge stop at
                    // kick distance and let the lunge close the gap. Non-lunge ground moves (most
                    // normals) must get into real hitbox reach (~1.3m) before pressing — a press
                    // from 1.4-1.8 m would whiff-loop forever.
                    float stopDist = to.Air ? 0.6f : (followUpLunge > 0f ? 1.4f : 1.15f);
                    if (dist > stopDist) { input.MoveX = dx / dist; input.MoveY = dz / dist; }

                    // Stop at attack range, not point-blank: ground moves lunge forward, so a
                    // press at 0 m carries the hitbox PAST the target (whiff-lunge loop). A
                    // player stands at kick distance and lets the lunge close the gap.
                    float speed = MathF.Sqrt(a.VX * a.VX + a.VZ * a.VZ);

                    // Press with lead: predict where the victim will be at the follow-up's
                    // trigger tick (constant velocity) and fire when the predicted position
                    // is inside the real reach (~1.3m; bone pose + hitbox radius). The press
                    // repeats every tick in range — the sim gates it, so this is a retry.
                    float pdx = (v.PX + v.VX * toTriggerTicks / 60f) - a.PX;
                    float pdz = (v.PZ + v.VZ * toTriggerTicks / 60f) - a.PZ;
                    float pdy = (v.PY + v.VY * toTriggerTicks / 60f) - a.PY;
                    float pdist = MathF.Sqrt(pdx * pdx + pdz * pdz);
                    if (to.Air)
                    {
                        // jump only when the victim is close to falling into reach
                        if (a.IsGrounded && dy > 1.2f && dy < 8f && a.JumpsLeft > 0 && dist < 7f)
                        { input.Jump = true; input.JumpHeld = true; }
                        else if (!a.IsGrounded && dy > 0.2f && dy < 8f && a.JumpsLeft > 0 && a.VY < -1f)
                        { input.Jump = true; }
                        if (speed < 1f && pdist < 1.1f && pdy > -0.8f && pdy < 2.2f)
                        { input.ActiveSlot = SlotByte(to.Slot); if (!fired) { fired = true; damageAtFire = v.DamagePercent; } }
                    }
                    else
                    {
                        // ground follow-up: stay grounded, hit them on the way down or after landing
                        float pressMax = followUpLunge > 0f ? 1.8f : 1.3f;
                        if (speed < 1f && pdist > 0.9f && pdist < pressMax && pdy > -0.6f && pdy < 1.8f)
                        { input.ActiveSlot = SlotByte(to.Slot); if (!fired) { fired = true; damageAtFire = v.DamagePercent; } }
                    }
                }
            }

            inputs[1] = input;
            inputs[100] = default;
            sim.Tick(inputs);

            var v2 = sim.GetState(100);
            if (starterConnectedAt < 0 && v2.DamagePercent > pct) starterConnectedAt = t;
            if (fired && v2.DamagePercent > damageAtFire)
                return new ProbeOutcome(true, t, !v2.IsGrounded, v2.HitstunTicks);
            if (starterConnectedAt < 0 && t > 500) return new ProbeOutcome(false, -1, false, 0);
        }
        return new ProbeOutcome(false, -1, false, 0);
    }

    /// <summary>
    /// Movement probe: wraps <see cref="RunFollowUpSim"/> and maps the outcome to the juggle
    /// verdict — Airborne = the follow-up connected while the victim was in flight (a real
    /// juggle); Grounded = connected only after the victim landed (neutral hit on a passive
    /// target — opponent is fully actionable); None = never connected.
    /// </summary>
    internal static (ProbeVerdict Verdict, int ConnectTick) RunProbe(CharacterDefinition def, Route r, int pct, BakedAnimationData? baked,
        (float AtkY, float NpcY, float NpcZ) spawn, CombatTuning? combatTuning = null)
    {
        int toTriggerTicks = def.GetSlotAbility(SlotByte(r.To.Slot) - 1, r.To.Air)?.Stages[0]
            .HitboxEvents?[0].TriggerTick ?? 0;
        int starterIasa = def.GetSlotAbility(SlotByte(r.From.Slot) - 1, r.From.Air)?.Stages[0].IasaTicks ?? 0;
        float followUpLunge = def.GetSlotAbility(SlotByte(r.To.Slot) - 1, r.To.Air)?.Stages[0].LungeForce ?? 0f;
        var outcome = RunFollowUpSim(def, r.From, r.To, toTriggerTicks, starterIasa, followUpLunge, pct, baked,
            new ProbeSetup(spawn.AtkY, spawn.NpcY, spawn.NpcZ, r.From.Air, r.From.Air), combatTuning);
        return outcome.Connected
            ? (outcome.VictimAirborne ? ProbeVerdict.Airborne : ProbeVerdict.Grounded, outcome.ConnectTick)
            : (ProbeVerdict.None, -1);
    }

    internal static ArenaDefinition BuildArena()
    {
        const int w = 200, h = 200;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "test",
            DisplayName = "Test Arena",
            KillHeight = -20f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            Heightmap = new ArenaHeightmap
            {
                Data = data, Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginZ = 0f,
            },
        };
    }

    // ── Report ───────────────────────────────────────────────────────────────

    internal static string BuildMarkdown(CharacterDefinition def, List<HitSpec> hits,
        Dictionary<(SlotRef, int, int), RunResult> runs, int[] pcts,
        Route[] routes, Route[] probeRoutes, Dictionary<(SlotRef, SlotRef, int), (ProbeVerdict Verdict, int ConnectTick)> probes,
        List<ParityResult> parity, string which, bool combos,
        (ComboStarterData[] Starters, ComboDensityData[] Density)? trueCombos,
        Dictionary<(SlotRef, int), DiEscapeData[]>? diEscape,
        ReachData? reach = null)
    {
        var sb = new System.Text.StringBuilder();
        string charName = def.DisplayName;
        sb.AppendLine($"# {charName} move data report");
        sb.AppendLine();
        sb.AppendLine($"> Generated by `tools/MoveDataReport` ({DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC) — runs the real shared sim.");
        sb.AppendLine($"> Method: each trajectory row launches a fresh grounded victim at the given % with the hitbox's authored");
        sb.AppendLine("> knockback via `Simulation.ApplyKnockback`, then steps the sim until landing (cap 2400 ticks).");
        sb.AppendLine("> No DI/SDI input. Hitstop is reported (in-code `ComputeHitstopTicks`, ADR-0019: min(12, dmg/3 + 6))");
        sb.AppendLine("> but not simulated — flight starts at launch.");
        sb.AppendLine("> Flight law (current sim): KV holds constant through hitstun (single integration per tick); at stun expiry KV is copied");
        sb.AppendLine($"> to V and the victim enters post-hitstun flight (ADR-0019 §6): gravity {Simulation.FlightGravity:0} m/s2 + horizontal friction {Simulation.FlightFriction:0} until");
        sb.AppendLine("> landing or any action (jump/ability clears the regime). The AirTime float window no longer applies to launches.");
        sb.AppendLine("> Combo matrix uses the no-travel bound: `TA = (IASA - trigger) [+ landing lag] [+ jump squat] + follow-up trigger`;");
        sb.AppendLine("> `T` = TRUE combo (TA < stun at that %), `-` = not true. Travel and hitstop make reality harder than this bound.");
        sb.AppendLine();

        sb.AppendLine("## Frame data (authored)");
        sb.AppendLine();
        sb.AppendLine("| move | hit | trigger | active | dmg | angle | base | growth | stun gate | IASA | landlag | AC bef | AC aft | total |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var h in hits)
        {
            sb.AppendLine($"| {Label(h)}{(h.Adaptive ? " (adaptive)" : "")} | {h.HitIndex + 1} | {h.Hit.TriggerTick} | {h.Hit.TriggerTick}-{h.Hit.TriggerTick + h.Hit.DurationTicks - 1} | " +
                          $"{h.Hit.Damage} | {h.LaunchAngle} | {h.BaseKb} | {h.GrowthKb} | {h.Hit.StunTicks} | " +
                          $"{h.Stage.IasaTicks} | {h.Stage.LandingLagTicks} | {h.Stage.AutoCancelBeforeTicks} | {h.Stage.AutoCancelAfterTicks} | {h.Stage.DurationTicks} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Per-hit trajectories (simulated)");
        sb.AppendLine();
        sb.AppendLine("| move | hit | % | KV m/s | hitstop | stun | adv | advL | rise@stun m | drift@stun m | apex m | actionable tick | landed tick |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var h in hits)
        foreach (var p in pcts)
        {
            var r = runs[(h.Slot, h.HitIndex, p)];
            // Frame advantage on hit (ticks): victim stun minus the attacker's remaining
            // recovery after the hit connects on its first active frame. Hitstop freezes
            // both players so it cancels out. unlock = IASA early-out, or stage end when
            // no IASA is authored (full commitment).
            int unlock = h.Stage.IasaTicks > 0 ? h.Stage.IasaTicks : h.Stage.DurationTicks;
            int recovery = Math.Max(0, unlock - h.Hit.TriggerTick);
            int adv = r.Stun - recovery;
            // advL: landed follow-up after an aerial pays landing lag (unless the landing
            // frame sits in an AC window, or the attacker continues with an aerial at IASA).
            string advL = h.Slot.Air ? (adv - h.Stage.LandingLagTicks).ToString() : "—";
            sb.AppendLine($"| {Label(h)} | {h.HitIndex + 1} | {p} | {r.Kv:0.0} | {r.Hitstop} | {r.Stun} | {adv} | {advL} | " +
                          $"{r.Rise:0.00} | {r.Drift:0.00} | {r.Apex:0.0} | {r.ActionableTick} | {r.LandedTick?.ToString() ?? ">2400"} |");
        }
        sb.AppendLine();
        sb.AppendLine("> **adv** = on-hit frame advantage (ticks): `stun − (IASA − trigger)`; positive = the attacker acts before the");
        sb.AppendLine("> victim leaves hitstun (can press/continue), negative = the victim recovers first. Hitstop freezes both, so it");
        sb.AppendLine("> cancels out. Assumes the hit connects on the first active frame — later connects only improve advantage.");
        sb.AppendLine("> **advL** (aerials): the landed follow-up pays `LandingLagTicks` on top (SHFFL-style); land inside an AC window");
        sb.AppendLine("> (`≤ AC bef` / `≥ AC aft`) or chase with an aerial at IASA and the lag is skipped — the true landed number sits");
        sb.AppendLine("> between `advL` and `adv`. Grounded moves land free (no lag), so `advL` is `—`.");

        sb.AppendLine("## Pipeline parity (real hit path vs direct formula)");
        sb.AppendLine();
        sb.AppendLine("The game launches through ResolveHits → hitstop queue, the rows above through a direct");
        sb.AppendLine("`ApplyKnockback` call. This section runs the REAL path (input → hitbox → resolve → queued launch)");
        sb.AppendLine("for each slot's first hitbox and compares the applied launch to the direct rows. Any `DIVERGE`");
        sb.AppendLine("means the game behaves differently from this report — investigate before trusting feel tuning");
        sb.AppendLine("(the 2026-08-14 x87 float comparison bug showed exactly this: every hit took the unscaled");
        sb.AppendLine("force path while .NET tests were green).");
        sb.AppendLine();
        sb.AppendLine("| move | % | direct KV m/s | pipeline KV m/s | direct stun | pipeline stun | direct apex | pipeline apex | status |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var p in parity)
            sb.AppendLine($"| {p.Label} | {p.Pct} | {p.DirectKv:0.00} | {p.PipeKv:0.00} | {p.DirectStun} | {p.PipeStun} | " +
                          $"{p.DirectApex:0.0} | {p.PipeApex:0.0} | {(p.Ok ? "OK" : "**DIVERGE**")} |");
        sb.AppendLine();

        if (combos)
        {
            sb.AppendLine("## Combo matrix (no-travel bound)");
            sb.AppendLine();
            sb.AppendLine("T = follow-up connects before stun ends (attacker recovers at IASA, jumpsquat when follow-up is aerial,");
            sb.AppendLine("landing lag when the route lands between hits). Most routes SHOULD be `-` at low % and some at all % —");
            sb.AppendLine("reads and movement are what make combos happen (Melee-style), TRUE everywhere = flowchart bait.");
            sb.AppendLine();
            sb.AppendLine("| route (TA ticks) | " + string.Join(" | ", pcts.Select(p => p.ToString())) + " |");
            sb.AppendLine("|---" + string.Join("", pcts.Select(_ => "|---")) + "|");
            foreach (var r in routes)
            {
                int ta = AttackerBudget(def, hits, r);
                string cells = string.Join(" | ", pcts.Select(p =>
                {
                    var stun = runs[(r.From, r.FromHit, p)].Stun;
                    return ta < stun ? "**T**" : "-";
                }));
                sb.AppendLine($"| {r.Label} ({ta}) | {cells} |");
            }
            sb.AppendLine();
        }

        if (combos && probeRoutes.Length > 0)
        {
            sb.AppendLine("## Combo probes (movement chase, real sim)");
            sb.AppendLine();
        sb.AppendLine("T = frame-true (matrix above). C = the chase policy connected while the victim was airborne — a real");
        sb.AppendLine("juggle/read; the victim is likely actionable by then, so these are not true combos. L = connected only");
        sb.AppendLine("after the victim landed (neutral hit on a passive target — opponent fully actionable, so not a combo).");
        sb.AppendLine("- = no connect within 2400 ticks. Policy: run toward the victim, jump when they are above, press the");
        sb.AppendLine("follow-up with a trigger-tick lead when the predicted position is in reach; victim is passive (no DI/SDI).");
        sb.AppendLine("The starter is performed for real (bone hitboxes) with an auto-calibrated placement — a route can fail");
        sb.AppendLine("because the starter itself is hard to land.");
        sb.AppendLine();
        sb.AppendLine("| route | " + string.Join(" | ", pcts.Select(p => p.ToString())) + " |");
        sb.AppendLine("|---" + string.Join("", pcts.Select(_ => "|---")) + "|");
        foreach (var r in probeRoutes)
        {
            int ta = AttackerBudget(def, hits, r);
            string cells = string.Join(" | ", pcts.Select(p =>
            {
                var stun = runs[(r.From, r.FromHit, p)].Stun;
                if (ta < stun) return "**T**";
                var probe = probes[(r.From, r.To, p)];
                return probe.Verdict == ProbeVerdict.Airborne ? "**C**"
                    : probe.Verdict == ProbeVerdict.Grounded ? "L" : "-";
            }));
            sb.AppendLine($"| {r.Label} | {cells} |");
        }
        sb.AppendLine();
        sb.AppendLine("Connect ticks (starter hit -> follow-up damage tick; verdict letter per cell):");
        sb.AppendLine();
        sb.AppendLine("```");
        foreach (var r in probeRoutes)
        {
            var parts = pcts.Select(p =>
            {
                var probe = probes[(r.From, r.To, p)];
                string tag = probe.Verdict switch
                {
                    ProbeVerdict.Airborne => "C",
                    ProbeVerdict.Grounded => "L",
                    _ => "-",
                };
                return probe.Verdict == ProbeVerdict.None ? $"{p}%:-" : $"{p}%:{tag}@{probe.ConnectTick}";
            });
            sb.AppendLine($"{r.Label}: {string.Join("  ", parts)}");
        }
        sb.AppendLine("```");
        sb.AppendLine();
        }

        if (trueCombos is { Starters.Length: > 0 })
        {
            sb.AppendLine("## True-combo reachability (real sim)");
            sb.AppendLine();
            sb.AppendLine("> Method: the starter connects through the REAL hitbox path (auto-calibrated placement per hit");
            sb.AppendLine("> state — grounded / airborne victim), then the attacker plays a greedy chase and presses the");
            sb.AppendLine("> follow-up as soon as actionable + in predicted reach. Cell letters: **T** = the follow-up's");
            sb.AppendLine("> damage landed while the victim was still in hitstun (a true combo); **F** = it landed after");
            sb.AppendLine("> stun expired (opponent fully actionable — not a combo); `-` = never connected within 2400 ticks.");
            sb.AppendLine("> **tight** = window tightness at that % — `sim stun (0.5 × launch speed) − (recovery +");
            sb.AppendLine("> landing lag + jump squat + follow-up trigger)`; positive = frame-true on paper. Travel and");
            sb.AppendLine("> hitstop make reality <= paper. Pure reachability — no scripted routes, no recommendations.");
            sb.AppendLine("> Note: the sim derives hitstun from launch speed; the authored `StunTicks` is a zero/nonzero");
            sb.AppendLine("> gate only — so tightness grows as the victim takes damage.");
            sb.AppendLine();
            foreach (var starter in trueCombos.Value.Starters)
            {
                sb.AppendLine($"### {starter.Move} — {starter.State} hit");
                sb.AppendLine();
                sb.AppendLine("| follow-up | tight@0% | " + string.Join(" | ", pcts.Select(p => p.ToString())) + " |");
                sb.AppendLine("|---|--" + string.Join("", pcts.Select(_ => "|---")) + "|");
                var byFu = starter.Edges.GroupBy(e => e.FollowUp);
                foreach (var grp in byFu)
                {
                    string cells = string.Join(" | ", pcts.Select(p =>
                    {
                        var e = grp.FirstOrDefault(x => x.Pct == p);
                        if (e == null) return "-";
                        return e.Verdict switch
                        {
                            "true" => "**T**",
                            "false" => "F",
                            _ => "-",
                        };
                    }));
                    int tight0 = grp.FirstOrDefault(x => x.Pct == pcts[0])?.Tightness ?? 0;
                    sb.AppendLine($"| {grp.Key} | {tight0} | {cells} |");
                }
                sb.AppendLine();
            }
            sb.AppendLine("Combo density — true links per character (all starters × follow-ups × hit states):");
            sb.AppendLine();
            sb.AppendLine("| % | grounded | airborne | total |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var d in trueCombos.Value.Density)
                sb.AppendLine($"| {d.Pct} | {d.Grounded} | {d.Airborne} | {d.Total} |");
            sb.AppendLine();
        }

        if (diEscape != null)
        {
            sb.AppendLine("## DI escape-space");
            sb.AppendLine();
            sb.AppendLine("> Method: baseline launch + four DI holds applied via `Simulation.ApplyDirectionalInfluence` at");
            sb.AppendLine("> launch (18° cap, Melee sin² curve — perpendicular holds bend most) and held through hitstun.");
            sb.AppendLine("> DI convention (DIX/DIY): in = MoveY −1 (toward the attacker, opposite the launch axis),");
            sb.AppendLine("> away = MoveY +1, up = MoveX +1, down = MoveX −1. Dev = 3D angle between the baseline and DI");
            sb.AppendLine("> launch vectors; **escape magnitude = max dev across holds** — low = DI-resistant (reliable");
            sb.AppendLine("> combo/kill tool), high = DI-bendable (escapable).");
            sb.AppendLine();
            sb.AppendLine("| move | % | in | away | up | down | max dev |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var h in hits)
            {
                if (!diEscape.TryGetValue((h.Slot, h.HitIndex), out var data)) continue;
                foreach (var d in data)
                {
                    sb.AppendLine($"| {Label(h)} | {d.Pct} | {d.Variants[0].DevDeg:0.0}° | {d.Variants[1].DevDeg:0.0}° | " +
                                  $"{d.Variants[2].DevDeg:0.0}° | {d.Variants[3].DevDeg:0.0}° | **{d.MaxDevDeg:0.0}°** |");
                }
            }
            sb.AppendLine();
        }

        if (reach != null)
        {
            sb.AppendLine("## Hitbox reach (authored)");
            sb.AppendLine();
            sb.AppendLine("> Method: geometry = `HitboxGeometry.ResolvePositions` per active tick at the grounded origin frame");
            sb.AppendLine("> (character at origin, yaw 0 → forward is +Z); bands = thirds of `CapsuleHeight` from the feet");
            sb.AppendLine("> (high band +0.5 m headroom); reach = forward (Z) extent of the side-view envelope (X flattened);");
            sb.AppendLine("> baked skeleton used when present, entity-relative fallback otherwise. Authored geometry only");
            sb.AppendLine("> — no buff bonuses. Reach = max forward extent over the bands.");
            sb.AppendLine();
            sb.AppendLine("| move | hit | low m | mid m | high m | reach m |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var hh in reach.Hits.OrderByDescending(x => x.Reach ?? float.NegativeInfinity))
            {
                string label = hits.Count(x => Label(x) == hh.Label) > 1 ? $"{hh.Label} (hit {hh.HitIndex + 1})" : hh.Label;
                sb.AppendLine($"| {label} | {hh.HitIndex + 1} | {ReachCell(hh.Bands[0].MaxZ)} | {ReachCell(hh.Bands[1].MaxZ)} | " +
                              $"{ReachCell(hh.Bands[2].MaxZ)} | **{ReachCell(hh.Reach)}** |");
            }
            sb.AppendLine();
            var bandDefs = ReachBandsFor(def);
            foreach (var g in reach.Gaps)
                sb.AppendLine($"- {g.Band} @ {g.HMin:0.0}–{g.HMax:0.0} m: nothing covers {g.From:0.0}–{g.To:0.0} m");
            foreach (var name in reach.UncoveredBands)
            {
                var b = bandDefs.FirstOrDefault(x => x.Name == name);
                sb.AppendLine($"- {name}: no normal reaches {b.YMin:0.0}–{b.YMax:0.0} m");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"_Weight: {def.Weight}. Jump squat: {def.Movement.JumpSquatTicks} ticks. Dash startup: not authored (0) — revisit later._");
        return sb.ToString();
    }

    /// <summary>Attacker time from hit-1 connect to hit-2 connect, in state-machine ticks (shared hitstop cancels).</summary>
    internal static int AttackerBudget(CharacterDefinition def, List<HitSpec> hits, Route r)
    {
        var from = hits.First(h => h.Slot == r.From && h.HitIndex == r.FromHit);
        var to = hits.First(h => h.Slot == r.To && h.HitIndex == r.ToHit);
        return AttackerBudget(def, from, to);
    }

    internal static int AttackerBudget(CharacterDefinition def, HitSpec from, HitSpec to)
    {
        if (from.Slot == to.Slot && from.HitIndex < to.HitIndex)
            return to.Hit.TriggerTick - from.Hit.TriggerTick; // same-press chain: no recovery between hitboxes
        int unlock = from.Stage.IasaTicks > 0 ? from.Stage.IasaTicks : from.Stage.DurationTicks;
        int recovery = Math.Max(0, unlock - from.Hit.TriggerTick);
        int landLag = from.Slot.Air && !to.Slot.Air ? from.Stage.LandingLagTicks : 0;
        int jumpSquat = to.Slot.Air && !from.Slot.Air ? def.Movement.JumpSquatTicks : 0;
        return recovery + landLag + jumpSquat + to.Hit.TriggerTick;
    }

    /// <summary>Hitstun the sim actually applies for this hit at this victim %, using the selected tuning.
    /// The authored <c>StunTicks</c> is a zero/nonzero gate; the duration derives from
    /// unscaled launch magnitude and the profile's coefficient/bonus.</summary>
    internal static int SimStun(CharacterDefinition def, HitSpec h, int pct, CombatTuning? combatTuning = null)
    {
        var s = new CharacterState { DamagePercent = (ushort)(pct + (int)h.Hit.Damage) };
        Simulation.ApplyKnockback(ref s, 0f, 1f, h.LaunchAngle, h.BaseKb, h.GrowthKb,
            h.Hit.Damage, h.Hit.StunTicks, def.Weight, combatTuning: combatTuning);
        return s.HitstunTicks;
    }

    /// <summary>True-combo window tightness for a starter→follow-up link at a victim %: the sim's
    /// derived stun minus the attacker's total time to the follow-up's first active tick (recovery +
    /// landing lag + jump squat + follow-up trigger). For multihit starters the LAST hitbox is the
    /// effective send — it re-launches the victim, so its stun/trigger define the window. Positive =
    /// the follow-up's hitbox is active before stun ends on paper; the sim verdict in the graph shows
    /// whether travel/range make it real.</summary>
    internal static int TightnessOf(CharacterDefinition def, HitSpec starter, HitSpec followUp, int pct,
        CombatTuning? combatTuning = null)
    {
        var send = starter.Stage.HitboxEvents[^1];
        int unlock = starter.Stage.IasaTicks > 0 ? starter.Stage.IasaTicks : starter.Stage.DurationTicks;
        int recovery = Math.Max(0, unlock - send.TriggerTick);
        if (starter.Slot.Air && !followUp.Slot.Air) recovery += starter.Stage.LandingLagTicks;
        if (!starter.Slot.Air && followUp.Slot.Air) recovery += def.Movement.JumpSquatTicks;
        return SimStun(def, starter, pct, combatTuning) - recovery - followUp.Hit.TriggerTick;
    }

    /// <summary>
    /// Freeform true-combo reachability graph: for every normal (as starter) × hit state
    /// (grounded / airborne victim) × %, the real sim runs the starter then attempts every
    /// other normal as a follow-up (greedy chase, shared <see cref="RunFollowUpSim"/>).
    /// An edge is TRUE iff the follow-up's damage lands while the victim is still in hitstun.
    /// Also aggregates combo density (true links per character per %).
    /// </summary>
    internal static (ComboStarterData[] Starters, ComboDensityData[] Density) ComputeTrueComboGraph(
        CharacterDefinition def, List<HitSpec> hits, int[] pcts, BakedAnimationData? baked,
        CombatTuning? combatTuning = null)
    {
        var normals = hits.Where(x => x.HitIndex == 0).ToList();
        var starters = new List<ComboStarterData>();
        var groundedCount = new int[pcts.Length];
        var airborneCount = new int[pcts.Length];
        foreach (var starter in normals)
        foreach (var (stateName, victimAir) in new[] { ("grounded", false), ("airborne", true) })
        {
            var setup = CalibrateScenario(def, starter, victimAir, baked, combatTuning);
            if (setup == null)
            {
                Console.Error.WriteLine($"truecombos: {Label(starter)} {stateName}: no placement connects — hit state skipped");
                continue;
            }
            var edges = new List<ComboEdgeData>();
            int starterIasa = starter.Stage.IasaTicks;
            foreach (var followUp in normals)
            for (int pi = 0; pi < pcts.Length; pi++)
            {
                var outcome = RunFollowUpSim(def, starter.Slot, followUp.Slot, followUp.Hit.TriggerTick,
                    starterIasa, followUp.Stage.LungeForce, pcts[pi], baked, setup.Value, combatTuning);
                string verdict = !outcome.Connected ? "never"
                    : outcome.VictimStunAtConnect > 0 ? "true" : "false";
                edges.Add(new ComboEdgeData(Label(followUp), TightnessOf(def, starter, followUp, pcts[pi], combatTuning),
                    pcts[pi], verdict, outcome.ConnectTick, outcome.VictimStunAtConnect));
                if (verdict == "true")
                {
                    if (victimAir) airborneCount[pi]++; else groundedCount[pi]++;
                }
            }
            starters.Add(new ComboStarterData(Label(starter), stateName, edges.ToArray()));
        }
        var density = pcts.Select((p, i) => new ComboDensityData(p, groundedCount[i], airborneCount[i], groundedCount[i] + airborneCount[i])).ToArray();
        return (starters.ToArray(), density);
    }

    internal static string Label(HitSpec h)
        => $"{(h.Slot.Air ? "a" : "g")}{h.Slot.Slot} {h.Ability.Name}";

    // ── JSON / HTML report ──────────────────────────────────────────────────

    internal static string SlotName(SlotRef s) => $"{(s.Air ? "a" : "g")}{s.Slot}";

    /// <summary>A spike (negative launch angle) sent from the ground lands instantly and shows no arc.
    /// The report launches spike victims from this altitude (off-stage / airborne scenario) so the
    /// downward send is visible. Kill-% analysis stays grounded (a grounded spike does not KO).</summary>
    internal const float SpikeLaunchAltitude = 3f;

    /// <summary>Kill-% proxy arena: flat 60x60, KillHeight -10 (Crossroads-style). Sides auto-derive
    /// from bounds &plusmn;10 &rarr; &plusmn;40; top auto = floor + 20 = +20.</summary>
    internal static ArenaDefinition BuildKillArena()
    {
        const int w = 60, h = 60;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "kill-proxy",
            DisplayName = "Kill Proxy (Crossroads-style 60x60)",
            KillHeight = -10f,
            MinX = -30f, MaxX = 30f, MinZ = -30f, MaxZ = 30f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            Heightmap = new ArenaHeightmap { Data = data, Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginZ = 0f },
        };
    }

    /// <summary>Clone an arena with blast lines pushed out so the sim never respawns — lets the run
    /// detect a real blast-line crossing itself and keep recording the full arc.</summary>
    internal static ArenaDefinition NoRespawn(ArenaDefinition a)
    {
        a.KillHeight = -1e6f; a.KillTop = 1e6f;
        a.KillMinX = -1e6f; a.KillMaxX = 1e6f; a.KillMinZ = -1e6f; a.KillMaxZ = 1e6f;
        return a;
    }

    internal static string? BlastLine(CharacterState s, in ArenaCollision.BlastLines b)
    {
        if (s.PY < b.KillHeight) return "bottom";
        if (s.PY > b.KillTop) return "top";
        if (s.PX < b.KillMinX || s.PX > b.KillMaxX) return "side";
        if (s.PZ < b.KillMinZ || s.PZ > b.KillMaxZ) return "side";
        return null;
    }

    internal static TrajPoint PointAt(CharacterState s, int tick, float groundY)
    {
        bool inHitstun = s.HitstunTicks > 0;
        float vy = inHitstun ? s.KVY : s.VY;
        float vz = inHitstun ? s.KVZ : s.VZ;
        char phase = s.IsGrounded ? 'G' : inHitstun ? 'H' : 'F';
        return new TrajPoint(tick, s.PY - groundY, s.PZ, vy, vz, phase,
            s.PX, s.PY, s.PZ, inHitstun ? s.KVX : s.VX, vy, vz, inHitstun);
    }

    /// <summary>Launch a grounded victim with the hitbox's knockback, step the sim until it lands OR
    /// crosses a real blast line, recording the per-tick arc. Runs on a no-respawn arena so the flight
    /// is captured in full and the crossing is detected against <paramref name="detect"/>.</summary>
    internal static TrajectoryData RunTrajectoryFull(CharacterDefinition def, HitSpec h, int pct,
        ArenaDefinition phys, in ArenaCollision.BlastLines detect, CombatTuning? combatTuning = null)
    {
        var sim = new ServerSimulation(phys, combatTuning: combatTuning);
        float groundY = def.CapsuleHeight * 0.5f;
        bool spike = h.LaunchAngle < 0;
        float startY = spike ? groundY + SpikeLaunchAltitude : groundY;
        var state = new CharacterState
        {
            PX = 0f, PY = startY, PZ = 0f,
            IsGrounded = !spike,
            State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)(pct + (int)h.Hit.Damage),
        };
        Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
            h.BaseKb, h.GrowthKb,
            h.Hit.Damage, h.Hit.StunTicks, def.Weight, combatTuning: combatTuning);
        float launchX = state.KVX, launchY = state.KVY, launchZ = state.KVZ;
        float kv = MathF.Sqrt(launchX * launchX + launchY * launchY + launchZ * launchZ);
        ushort stun = state.HitstunTicks;
        ushort hitstop = ServerSimulation.ComputeHitstopTicks(h.Hit.Damage, null);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };

        int actionable = -1;
        int? expiryTick = null;
        float? expiryX = null, expiryY = null, expiryZ = null;
        float? expiryDistance = null, expiryHeight = null;
        int? landed = null; string? killLine = null;
        string termination = "simulation-cap";
        var pts = new List<TrajPoint> { PointAt(state, 0, groundY) };
        for (int t = 0; t < MaxTicks; t++)
        {
            sim.Tick(inputs);
            var s = sim.GetState(1);
            killLine = BlastLine(s, detect);
            if (killLine != null)
            {
                pts.Add(PointAt(s, t + 1, groundY));
                termination = "blast-line";
                break;
            }
            if (expiryTick == null && s.HitstunTicks == 0)
            {
                actionable = t + 1;
                expiryTick = actionable;
                expiryX = s.PX; expiryY = s.PY; expiryZ = s.PZ;
                expiryDistance = MathF.Sqrt(s.PX * s.PX + s.PZ * s.PZ);
                expiryHeight = s.PY - groundY;
            }
            if (actionable >= 0 && s.IsGrounded)
            {
                pts.Add(PointAt(s, t + 1, groundY));
                landed = t + 1;
                termination = "landing";
                break;
            }
            pts.Add(PointAt(s, t + 1, groundY));
            if (pts.Count >= 1200)
            {
                termination = "sample-cap";
                break;
            }
        }
        var apex = pts.Count == 0 ? null : pts.OrderByDescending(p => p.Height).First();
        float maxTravel = pts.Count == 0 ? 0f : pts.Max(p => p.Travel);
        int unlock = h.Stage.IasaTicks > 0 ? h.Stage.IasaTicks : h.Stage.DurationTicks;
        int adv = stun - Math.Max(0, unlock - h.Hit.TriggerTick);
        int? advL = h.Slot.Air ? adv - h.Stage.LandingLagTicks : null;
        return new TrajectoryData(pct, kv, hitstop, stun, adv, advL,
            expiryHeight ?? 0f, expiryDistance ?? 0f, apex?.Height ?? 0f, apex?.Tick, maxTravel, actionable,
            expiryTick, expiryX, expiryY, expiryZ, expiryDistance, expiryHeight,
            landed, killLine, termination, h.LaunchAngle, launchX, launchY, launchZ,
            MathF.Sqrt(launchX * launchX + launchZ * launchZ), launchY, pts.ToArray());
    }


    /// <summary>3D angle between two vectors, in degrees.</summary>
    internal static float AngleBetween(float ax, float ay, float az, float bx, float by, float bz)
    {
        float ma = MathF.Sqrt(ax * ax + ay * ay + az * az);
        float mb = MathF.Sqrt(bx * bx + by * by + bz * bz);
        if (ma <= 0.0001f || mb <= 0.0001f) return 0f;
        return MathF.Acos(Math.Clamp((ax * bx + ay * by + az * bz) / (ma * mb), -1f, 1f)) * 180f / MathF.PI;
    }

    /// <summary>The four DI holds relative to the launch: along the launch axis toward the attacker
    /// (in) / with it (away), and the two perpendicular bends (up/down). MoveX/MoveY = the sim's
    /// DI coordinate convention (DIX/DIY, camera-relative). The sim's sin² curve makes the
    /// perpendicular holds bend most; along-axis holds only give the expiry ASDI push.</summary>
    internal static readonly (string Name, float Dx, float Dy)[] DiDirections =
    {
        ("in", 0f, -1f),
        ("away", 0f, 1f),
        ("up", 1f, 0f),
        ("down", -1f, 0f),
    };

    /// <summary>
    /// One DI-variant trajectory: the same launch as the baseline row, but the victim holds
    /// the given DI direction during hitstun. The launch vector is rotated by the sim's real
    /// <see cref="Simulation.ApplyDirectionalInfluence"/> (18° cap, Melee sin² curve) — the
    /// same call the hitstop-freeze boundary runs — then the sim steps with the stick held
    /// through stun (so the expiry ASDI push applies too, like holding in-game). Returns the
    /// arc plus the angular deviation of the launch vector from baseline.
    /// </summary>
    internal static DiVariantData RunTrajectoryWithDi(CharacterDefinition def, HitSpec h, int pct,
        ArenaDefinition phys, in ArenaCollision.BlastLines detect, string dir, float dx, float dy,
        CombatTuning? combatTuning = null)
    {
        var sim = new ServerSimulation(phys, combatTuning: combatTuning);
        float groundY = def.CapsuleHeight * 0.5f;
        bool spike = h.LaunchAngle < 0;
        float startY = spike ? groundY + SpikeLaunchAltitude : groundY;
        var state = new CharacterState
        {
            PX = 0f, PY = startY, PZ = 0f,
            IsGrounded = !spike,
            State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)(pct + (int)h.Hit.Damage),
        };
        Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
            h.BaseKb, h.GrowthKb,
            h.Hit.Damage, h.Hit.StunTicks, def.Weight, combatTuning: combatTuning);
        float kvx0 = state.KVX, kvy0 = state.KVY, kvz0 = state.KVZ;
        state.DIX = dx; state.DIY = dy;
        Simulation.ApplyDirectionalInfluence(ref state);
        float devDeg = AngleBetween(kvx0, kvy0, kvz0, state.KVX, state.KVY, state.KVZ);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>();

        float maxPy = state.PY;
        float maxTravel = 0f;
        var pts = new List<TrajPoint>();
        for (int t = 0; t < MaxTicks; t++)
        {
            var s0 = sim.GetState(1);
            // Hold the stick only while in hitstun — post-stun the victim is passive (baseline behavior).
            inputs[1] = s0.HitstunTicks > 0 ? new InputState { MoveX = dx, MoveY = dy } : default;
            sim.Tick(inputs);
            var s = sim.GetState(1);
            if (BlastLine(s, detect) != null) { pts.Add(PointAt(s, t + 1, groundY)); break; }
            if (s.PY > maxPy) maxPy = s.PY;
            if (s.PZ > maxTravel) maxTravel = s.PZ;
            if (s.IsGrounded) { pts.Add(PointAt(s, t + 1, groundY)); break; }
            pts.Add(PointAt(s, t + 1, groundY));
            if (pts.Count >= 1200) break; // safety cap
        }
        return new DiVariantData(dir, devDeg, maxTravel, maxPy - groundY, pts.ToArray());
    }

    /// <summary>DI escape-space for every move × %: the baseline arc plus the four DI-variant arcs,
    /// with the max launch-vector deviation as the escape magnitude. Per-move results align with
    /// the move's Trajectories array by pct index.</summary>
    internal static DiEscapeData[] ComputeDiEscapeFor(CharacterDefinition def, HitSpec h, int[] pcts,
        ArenaDefinition phys, in ArenaCollision.BlastLines detect, CombatTuning? combatTuning = null)
    {
        var result = new List<DiEscapeData>();
        foreach (var pct in pcts)
        {
            var variants = new List<DiVariantData>();
            foreach (var (name, dx, dy) in DiDirections)
                variants.Add(RunTrajectoryWithDi(def, h, pct, phys, detect, name, dx, dy, combatTuning));
            result.Add(new DiEscapeData(pct, variants.Max(v => v.DevDeg), variants.ToArray()));
        }
        return result.ToArray();
    }

    internal static Dictionary<(SlotRef, int), DiEscapeData[]> ComputeDiEscape(CharacterDefinition def,
        List<HitSpec> hits, int[] pcts, ArenaDefinition phys, CombatTuning? combatTuning = null)
    {
        var detect = ArenaCollision.ResolveBlastLines(in phys);
        return hits.ToDictionary(h => (h.Slot, h.HitIndex), h => ComputeDiEscapeFor(def, h, pcts, phys, detect, combatTuning));
    }

    internal static (bool Killed, string Line) RunKillProbe(CharacterDefinition def, HitSpec h, int pct,
        ArenaDefinition phys, in ArenaCollision.BlastLines detect, CombatTuning? combatTuning = null)
    {
        var sim = new ServerSimulation(phys, combatTuning: combatTuning);
        float groundY = def.CapsuleHeight * 0.5f;
        var state = new CharacterState
        {
            PX = 0f, PY = groundY, PZ = 0f, IsGrounded = true,
            State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)(pct + (int)h.Hit.Damage),
        };
        Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
            h.BaseKb, h.GrowthKb,
            h.Hit.Damage, h.Hit.StunTicks, def.Weight, combatTuning: combatTuning);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };
        for (int t = 0; t < MaxTicks; t++)
        {
            sim.Tick(inputs);
            var s = sim.GetState(1);
            string? line = BlastLine(s, detect);
            if (line != null) return (true, line);
            if (s.IsGrounded) return (false, "");
        }
        return (false, "");
    }

    /// <summary>Lowest victim % (pre-hit) at which the launch crosses a blast line before landing.
    /// Binary search — knockback grows monotonically with damage. null = no KO by 250%.</summary>
    internal static (int Pct, string Line)? KillPctFor(CharacterDefinition def, HitSpec h,
        ArenaDefinition phys, in ArenaCollision.BlastLines detect, CombatTuning? combatTuning = null)
    {
        const int MAX = 250;
        if (!RunKillProbe(def, h, MAX, phys, detect, combatTuning).Killed) return null;
        int lo = 0, hi = MAX, found = MAX; string line = "";
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var r = RunKillProbe(def, h, mid, phys, detect, combatTuning);
            if (r.Killed) { found = mid; line = r.Line; hi = mid - 1; }
            else lo = mid + 1;
        }
        return (found, line);
    }

    internal static FrameDataData BuildFrameData(HitSpec h) => new(
        h.Hit.TriggerTick, h.Hit.TriggerTick, h.Hit.TriggerTick + h.Hit.DurationTicks - 1,
        h.Hit.Damage, h.LaunchAngle, h.BaseKb, h.GrowthKb,
        h.Hit.StunTicks, h.Stage.IasaTicks, h.Stage.LandingLagTicks, h.Stage.AutoCancelBeforeTicks,
        h.Stage.AutoCancelAfterTicks, h.Stage.DurationTicks,
        h.Hit.Knockback.Profile.ToString(), h.Adaptive);

    internal static ReportData BuildReport(CharacterDefinition def, List<HitSpec> hits, int[] pcts,
        BakedAnimationData? baked, bool trueCombos, bool di, bool reach = false, CombatTuning? combatTuning = null)
    {
        var reportArena = BuildArena();
        var reportDetect = ArenaCollision.ResolveBlastLines(in reportArena);
        var reportPhys = NoRespawn(reportArena);

        var killArena = BuildKillArena();
        var killDetect = ArenaCollision.ResolveBlastLines(in killArena);
        var killPhys = NoRespawn(killArena);

        var diEscape = di ? ComputeDiEscape(def, hits, pcts, reportPhys, combatTuning) : null;
        var reachData = reach ? BuildReach(def, hits, baked) : null;
        var (graph, density) = trueCombos
            ? ComputeTrueComboGraph(def, hits, pcts, baked, combatTuning)
            : (Array.Empty<ComboStarterData>(), Array.Empty<ComboDensityData>());

        var moves = new List<MoveData>();
        foreach (var h in hits)
        {
            var trajs = new List<TrajectoryData>();
            foreach (var p in pcts)
                trajs.Add(RunTrajectoryFull(def, h, p, reportPhys, reportDetect, combatTuning));
            var kill = KillPctFor(def, h, killPhys, killDetect, combatTuning);
            moves.Add(new MoveData(Label(h), SlotName(h.Slot), h.HitIndex, h.Ability.Name,
                BuildFrameData(h), kill?.Pct, kill?.Line, ClearanceOf(def, trajs[^1], killDetect), trajs.ToArray(),
                diEscape != null ? diEscape[(h.Slot, h.HitIndex)] : Array.Empty<DiEscapeData>()));
        }
        float groundY = def.CapsuleHeight * 0.5f;
        var metadata = new ReportMetadata(def.DisplayName, pcts,
            "requested bucket is pre-hit damage; launch uses bucket plus this hit's damage",
            def.DisplayName, def.Weight,
            new InitialStateData(0f, groundY, 0f, true, ActionState.Idle.ToString(), 0f),
            60, di ? "neutral baseline plus held-DI variants" : "neutral (no DI/SDI)",
            "first of landing, report-arena blast-line crossing, 1200 sampled points, or 2400 simulation ticks");
        return new ReportData(def.DisplayName, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            metadata, pcts, ToArenaData(reportDetect, "trajectory / landing arena (flat 200x200)"),
            ToArenaData(killDetect, "kill-% proxy (flat 60x60, Crossroads-style, -10)"), moves.ToArray(),
            graph, density, reachData);
    }

    /// <summary>How close the move gets to the top / side blast lines at the highest simulated % —
    /// fraction of the way there (0..1). Always populated even when it never kills, so tuning can
    /// see "up-slash apex is 47% of the way to top blast".</summary>
    internal static ClearanceData ClearanceOf(CharacterDefinition def, TrajectoryData tr, in ArenaCollision.BlastLines kill)
    {
        float groundY = def.CapsuleHeight * 0.5f;
        float topFrac = kill.KillTop - groundY > 0.01f ? Math.Clamp(tr.Apex / (kill.KillTop - groundY), 0f, 1f) : 0f;
        float sideFrac = kill.KillMaxZ - kill.KillMinZ > 0.01f ? Math.Clamp(tr.MaxTravel / kill.KillMaxZ, 0f, 1f) : 0f;
        return new ClearanceData(topFrac, sideFrac, topFrac >= sideFrac ? "top" : "side");
    }

    internal static ArenaData ToArenaData(in ArenaCollision.BlastLines b, string note)
    {
        static float? Inf(float v) => float.IsInfinity(v) ? null : v;
        return new ArenaData(Inf(b.KillHeight), Inf(b.KillTop), Inf(b.KillMinX), Inf(b.KillMaxX),
            Inf(b.KillMinZ), Inf(b.KillMaxZ), note);
    }

    internal static string ToJson(ReportData r) => JsonSerializer.Serialize(r,
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    // ── HTML renderer (self-contained, no external deps) ────────────────────

    internal static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    internal static string Fi(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    internal static string Escape(string s) => System.Net.WebUtility.HtmlEncode(s);
    internal static string Blast(float? v) => v == null ? "&infin;" : F(v.Value);
    internal static string Bar(int pct) => $"<span class=\"barwrap\"><span class=\"barfill\" style=\"width:{pct}%\"></span></span>{pct}%";
    internal static string MoveTag(MoveData m) => m.Frame.Adaptive ? " <span class=\"tag\">adaptive</span>" : "";
    internal static string DisplayLabel(MoveData m, ReportData r) =>
        r.Moves.Count(x => x.Label == m.Label) > 1 ? $"{m.Label} (hit {m.HitIndex + 1})" : m.Label;

    internal static string AdvColor(int adv)
    {
        int a = Math.Clamp(adv, -40, 40);
        int m = (int)(Math.Abs(a) * 5.5f);
        return a >= 0 ? $"rgb({255 - m},255,{255 - m})" : $"rgb(255,{255 - m},{255 - m})";
    }

    internal static readonly (string Name, string Color)[] DiArcColors =
    {
        ("in", "#2ecc71"), ("away", "#e74c3c"), ("up", "#f39c12"), ("down", "#9b59b6"),
    };

    /// <summary>Reach-chart hit colors: one per HitboxEvent within a move (hit 1..5).</summary>
    internal static readonly (string Name, string Color)[] HitColors =
    {
        ("hit 1", "#1a5fd0"), ("hit 2", "#e74c3c"), ("hit 3", "#27ae60"), ("hit 4", "#8e44ad"), ("hit 5", "#e67e22"),
    };

    internal static string ArcSvg(TrajectoryData tr, float maxTravel, float maxHeight)
    {
        const int W = 150, H = 110, PAD = 8;
        float tw = maxTravel > 0.01f ? maxTravel : 1f;
        float th = maxHeight > 0.01f ? maxHeight : 1f;
        float sx = (W - 2 * PAD) / tw, sy = (H - 2 * PAD) / th;
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\" class=\"arc\">");
        sb.Append($"<line x1=\"{PAD}\" y1=\"{H - PAD}\" x2=\"{W - PAD}\" y2=\"{H - PAD}\" stroke=\"#888\" stroke-width=\"1\"/>");
        sb.Append("<polyline fill=\"none\" stroke=\"#1a5fd0\" stroke-width=\"1.6\" points=\"");
        foreach (var p in tr.Points)
            sb.Append($"{Fi(PAD + p.Travel * sx)},{Fi(H - PAD - p.Height * sy)} ");
        sb.Append("\"/>");
        if (tr.Points.Length > 0)
        {
            var apex = tr.Points.OrderByDescending(p => p.Height).First();
            sb.Append($"<circle cx=\"{Fi(PAD + apex.Travel * sx)}\" cy=\"{Fi(H - PAD - apex.Height * sy)}\" r=\"2.5\" fill=\"#d23\"/>");
        }
        if (tr.KillLine != null && tr.Points.Length > 0)
        {
            var last = tr.Points[tr.Points.Length - 1];
            sb.Append($"<line x1=\"{Fi(PAD + last.Travel * sx)}\" y1=\"{PAD}\" x2=\"{Fi(PAD + last.Travel * sx)}\" y2=\"{H - PAD}\" stroke=\"#d23\" stroke-width=\"1\" stroke-dasharray=\"2,2\"/>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Large per-move DI figure: one SVG with a % grid and the baseline + four DI-variant arcs
    /// stacked as per-% groups; a global selector toggles which group is visible. Common character
    /// scale (maxTravel/maxHeight) so all %s share one coordinate system.</summary>
    internal static string DiFigure(MoveData m, ReportData r, float maxTravel, float maxHeight)
    {
        const int W = 640, H = 400, PAD = 26;
        float tw = maxTravel > 0.01f ? maxTravel : 1f;
        float th = maxHeight > 0.01f ? maxHeight : 1f;
        float sx = (W - 2 * PAD) / tw, sy = (H - 2 * PAD) / th;
        var sb = new StringBuilder();
        sb.Append($"<figure class=\"di-fig\"><div class=\"di-title\">{Escape(DisplayLabel(m, r))}{MoveTag(m)}</div>");
        sb.Append($"<svg width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\" class=\"arc di\">");
        // grid: horizontal every 1 m of height, vertical every 2 m of travel, with m-tick labels
        for (float h = 0; h <= th + 0.001f; h += 1f)
        {
            float y = H - PAD - h * sy;
            sb.Append($"<line x1=\"{PAD}\" y1=\"{Fi(y)}\" x2=\"{W - PAD}\" y2=\"{Fi(y)}\" stroke=\"#e3e6ea\" stroke-width=\"1\"/>");
            sb.Append($"<text x=\"{PAD - 5}\" y=\"{Fi(y + 3)}\" font-size=\"10\" fill=\"#999\" text-anchor=\"end\">{Fi(h)}</text>");
        }
        for (float t = 0; t <= tw + 0.001f; t += 2f)
        {
            float x = PAD + t * sx;
            sb.Append($"<line x1=\"{Fi(x)}\" y1=\"{PAD}\" x2=\"{Fi(x)}\" y2=\"{H - PAD}\" stroke=\"#e3e6ea\" stroke-width=\"1\"/>");
            sb.Append($"<text x=\"{Fi(x)}\" y=\"{H - PAD + 14}\" font-size=\"10\" fill=\"#999\" text-anchor=\"middle\">{Fi(t)}</text>");
        }
        sb.Append($"<text x=\"{W - PAD}\" y=\"{H - 4}\" font-size=\"10\" fill=\"#666\" text-anchor=\"end\">travel (m)</text>");
        sb.Append($"<text x=\"8\" y=\"{PAD - 8}\" font-size=\"10\" fill=\"#666\">height (m)</text>");
        sb.Append($"<line x1=\"{PAD}\" y1=\"{H - PAD}\" x2=\"{W - PAD}\" y2=\"{H - PAD}\" stroke=\"#666\" stroke-width=\"1.4\"/>");
        // one group per % — the selector shows exactly one at a time
        for (int i = 0; i < m.Trajectories.Length; i++)
        {
            var tr = m.Trajectories[i];
            var diData = i < m.DiEscape.Length ? m.DiEscape[i] : null;
            sb.Append($"<g class=\"pct\" data-pct=\"{tr.Pct}\">");
            if (diData != null)
            {
                for (int v = 0; v < diData.Variants.Length && v < DiArcColors.Length; v++)
                {
                    sb.Append($"<polyline fill=\"none\" stroke=\"{DiArcColors[v].Color}\" stroke-width=\"1.6\" opacity=\"0.9\" points=\"");
                    foreach (var p in diData.Variants[v].Points)
                        sb.Append($"{Fi(PAD + p.Travel * sx)},{Fi(H - PAD - p.Height * sy)} ");
                    sb.Append("\"/>");
                }
            }
            sb.Append("<polyline fill=\"none\" stroke=\"#1a5fd0\" stroke-width=\"2.2\" points=\"");
            foreach (var p in tr.Points)
                sb.Append($"{Fi(PAD + p.Travel * sx)},{Fi(H - PAD - p.Height * sy)} ");
            sb.Append("\"/>");
            if (tr.Points.Length > 0)
            {
                var apex = tr.Points.OrderByDescending(p => p.Height).First();
                sb.Append($"<circle cx=\"{Fi(PAD + apex.Travel * sx)}\" cy=\"{Fi(H - PAD - apex.Height * sy)}\" r=\"3\" fill=\"#d23\"/>");
            }
            if (tr.KillLine != null && tr.Points.Length > 0)
            {
                var last = tr.Points[tr.Points.Length - 1];
                sb.Append($"<line x1=\"{Fi(PAD + last.Travel * sx)}\" y1=\"{PAD}\" x2=\"{Fi(PAD + last.Travel * sx)}\" y2=\"{H - PAD}\" stroke=\"#d23\" stroke-width=\"1.2\" stroke-dasharray=\"3,3\"/>");
            }
            sb.Append("</g>");
        }
        sb.Append("</svg>");
        sb.Append("<figcaption>");
        for (int i = 0; i < m.Trajectories.Length; i++)
        {
            var tr = m.Trajectories[i];
            var diData = i < m.DiEscape.Length ? m.DiEscape[i] : null;
            sb.Append($"<span class=\"pct\" data-pct=\"{tr.Pct}\">{tr.Pct}% &middot; KV {F(tr.Kv)} &middot; apex {F(tr.Apex)}m &middot; stun {tr.Stun}" +
                (diData != null ? $" &middot; <b>DI {F(diData.MaxDevDeg)}&deg;</b>" : "") + "</span>");
        }
        sb.Append("</figcaption></figure>");
        return sb.ToString();
    }

    /// <summary>
    /// Side-view reach overlay for one hit: the character silhouette at the origin plus every
    /// active-tick capsule drawn as a thick round-cap line (opacity 0.18 — overlap darkens, so
    /// the union reads). Common character scale: width/height from the reach ZMin/ZMax and
    /// CharHeight + 0.6 at 60 px/m; grid every 0.5 m.
    /// </summary>
    internal static string ReachSvg(ReachData reach, ReachHitData hit)
    {
        const float PxPerM = 60f;
        float w = (reach.ZMax - reach.ZMin) * PxPerM;
        float h = (reach.CharHeight + 0.6f) * PxPerM;
        float top = reach.CharHeight + 0.6f;
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{Fi(w)}\" height=\"{Fi(h)}\" viewBox=\"0 0 {Fi(w)} {Fi(h)}\" class=\"reach\">");
        for (float z = MathF.Ceiling(reach.ZMin / 0.5f) * 0.5f; z <= reach.ZMax + 0.001f; z += 0.5f)
            sb.Append($"<line x1=\"{Fi((z - reach.ZMin) * PxPerM)}\" y1=\"0\" x2=\"{Fi((z - reach.ZMin) * PxPerM)}\" y2=\"{Fi(h)}\" stroke=\"#e3e6ea\" stroke-width=\"1\"/>");
        for (float y = 0f; y <= top + 0.001f; y += 0.5f)
            sb.Append($"<line x1=\"0\" y1=\"{Fi((top - y) * PxPerM)}\" x2=\"{Fi(w)}\" y2=\"{Fi((top - y) * PxPerM)}\" stroke=\"#e3e6ea\" stroke-width=\"1\"/>");
        // Character silhouette: capsule-width rounded rect from feet (y=0) to head (y=CharHeight), centered at z=0.
        float cr = reach.CapsuleRadius * PxPerM;
        float cx = (0f - reach.ZMin) * PxPerM;
        float feetY = top * PxPerM;
        sb.Append($"<rect x=\"{Fi(cx - cr)}\" y=\"{Fi(feetY - reach.CharHeight * PxPerM)}\" width=\"{Fi(2 * cr)}\" " +
                  $"height=\"{Fi(reach.CharHeight * PxPerM)}\" rx=\"{Fi(cr)}\" fill=\"none\" stroke=\"#999\" stroke-width=\"1.2\"/>");
        string color = HitColors[hit.HitIndex % HitColors.Length].Color;
        foreach (var c in hit.Capsules)
        {
            float x0 = (c.Z0 - reach.ZMin) * PxPerM, y0 = (top - c.Y0) * PxPerM;
            float x1 = (c.Z1 - reach.ZMin) * PxPerM, y1 = (top - c.Y1) * PxPerM;
            sb.Append($"<line x1=\"{Fi(x0)}\" y1=\"{Fi(y0)}\" x2=\"{Fi(x1)}\" y2=\"{Fi(y1)}\" stroke=\"{color}\" " +
                      $"stroke-width=\"{Fi(c.Radius * 2f * PxPerM)}\" stroke-linecap=\"round\" opacity=\"0.18\"/>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    internal static string ToHtml(ReportData r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{Escape(r.Character)} move data</title><style>");
        sb.AppendLine("body{font-family:system-ui,sans-serif;margin:24px;color:#1c1e21;}h1{font-size:22px;}h2{margin-top:32px;border-bottom:1px solid #ddd;padding-bottom:4px;}.meta{color:#666;font-size:13px;margin-bottom:16px;max-width:880px;line-height:1.4;}");
        sb.AppendLine("table.heat{border-collapse:collapse;font-size:13px;}table.heat td,table.heat th{border:1px solid #ccc;padding:3px 7px;text-align:center;}table.heat td.move,table.heat th.move{text-align:left;white-space:nowrap;}table.heat th{cursor:pointer;user-select:none;background:#f2f3f5;}table.heat td.kill{min-width:56px;}");
        sb.AppendLine(".legend{font-size:12px;color:#444;margin:6px 0 18px;max-width:880px;}.arcs{display:flex;flex-wrap:wrap;gap:12px;margin:6px 0 20px;}.move{margin-bottom:20px;}figure{margin:0;text-align:center;font-size:11px;color:#555;width:150px;}svg.arc{border:1px solid #eee;background:#fafbfc;display:block;}");
        sb.AppendLine(".killbadge{font-size:11px;padding:1px 6px;border-radius:9px;background:#fdecea;color:#c0392b;white-space:nowrap;}");
        sb.AppendLine(".barwrap{width:84px;background:#eee;border-radius:6px;height:10px;display:inline-block;vertical-align:middle;margin-right:5px;overflow:hidden;}.barfill{display:block;height:10px;border-radius:6px;background:#e74c3c;}td.clr{font-size:11px;color:#555;white-space:nowrap;}");
        sb.AppendLine(".tag{font-size:10px;color:#8a6d1a;background:#fcf3d4;padding:0 5px;border-radius:8px;margin-left:5px;font-weight:normal;}.note{background:#fff7e6;border-left:3px solid #e6a23c;padding:6px 10px;font-size:12px;color:#6b5b1f;margin:8px 0 14px;max-width:880px;}");
        sb.AppendLine(".di-figs{display:grid;grid-template-columns:repeat(auto-fill,minmax(480px,1fr));gap:20px 28px;align-items:start;}.di-fig{min-width:0;width:auto;text-align:left;}svg.di{width:100%;height:auto;display:block;background:#fafbfc;}.di-title{font-size:13px;font-weight:600;margin-bottom:4px;}.di-fig figcaption{font-size:11px;color:#555;margin-top:4px;text-align:left;}select{margin-left:6px;font-size:13px;}.swatch{display:inline-block;width:12px;height:5px;border-radius:2px;margin:0 4px 0 10px;vertical-align:middle;}");
        sb.AppendLine("</style></head><body>");

        var meta = r.Metadata;
        sb.AppendLine($"<h1>{Escape(r.Character)} move data</h1>");
        sb.AppendLine($"<div class=\"meta\">Generated {Escape(r.GeneratedAt)} &middot; real ServerSimulation (ADR-0019). " +
            $"Victim: {Escape(meta.VictimCharacter)}, weight {F(meta.VictimWeight)}, initial state " +
            $"({F(meta.VictimInitialState.PositionX)}, {F(meta.VictimInitialState.PositionY)}, {F(meta.VictimInitialState.PositionZ)}), " +
            $"{Escape(meta.VictimInitialState.ActionState)}, grounded {meta.VictimInitialState.IsGrounded}. " +
            $"Buckets: {string.Join(", ", meta.RequestedPercents)}% pre-hit; tick rate {meta.TickRateHz} Hz; " +
            $"DI: {Escape(meta.DiMode)}.<br>" +
            $"Trajectories: {Escape(r.ReportArena.Note)}. Termination: {Escape(meta.TerminationRule)}. " +
            $"Kill %: {Escape(r.KillArena.Note)} &mdash; top {Blast(r.KillArena.KillTop)}, side &plusmn;{Blast(r.KillArena.KillMaxX)}, " +
            $"bottom {Blast(r.KillArena.KillHeight)}. Center launch, victim passive.</div>");

        // A — frame-advantage heatmap
        sb.AppendLine("<h2>Frame advantage</h2>");
        if (r.Moves.Any(m => m.Frame.Adaptive))
            sb.AppendLine("<div class=\"note\">Moves tagged <b>adaptive</b> use the Melee-361 auto-angle (the real launch angle varies with hit position); their trajectory here uses the <b>authored angle as a representative</b>. Frame data (active / duration / IASA / stun) is exact regardless.</div>");
        sb.AppendLine("<div class=\"legend\">Cells = on-hit frame advantage in ticks (stun &minus; recovery). Green = attacker acts first (+), red = victim recovers first (&minus;). Click a column header to sort; hover a cell for KV / stun / apex.</div>");
        sb.AppendLine("<table id=\"adv\" class=\"heat\"><thead><tr><th class=\"move\" onclick=\"sortT(0,false)\">move</th>");
        for (int i = 0; i < r.Percents.Length; i++) sb.AppendLine($"<th onclick=\"sortT({i + 1},true)\">{r.Percents[i]}%</th>");
        sb.AppendLine($"<th class=\"kill\" onclick=\"sortT({r.Percents.Length + 1},true)\">kill%</th></tr></thead><tbody>");
        foreach (var m in r.Moves)
        {
            sb.AppendLine($"<tr><td class=\"move\">{Escape(DisplayLabel(m, r))}{MoveTag(m)}</td>");
            foreach (var tr in m.Trajectories)
                sb.AppendLine($"<td style=\"background:{AdvColor(tr.Adv)}\" title=\"KV {F(tr.Kv)} m/s &middot; stun {tr.Stun} &middot; apex {F(tr.Apex)} m &middot; hitstop {tr.Hitstop}\">{tr.Adv}</td>");
            sb.AppendLine($"<td class=\"kill\">{(m.KillPct != null ? $"<span class=\"killbadge\">{m.KillPct}% {Escape(m.KillLine!)}</span>" : "&mdash;")}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");

        // B — knockback shape gallery (COMMON scale across the whole character so absolute sizes compare)
        float gMaxTravel = 1f, gMaxHeight = 1f;
        foreach (var m in r.Moves)
            foreach (var tr in m.Trajectories)
            {
                if (tr.MaxTravel > gMaxTravel) gMaxTravel = tr.MaxTravel;
                if (tr.Apex > gMaxHeight) gMaxHeight = tr.Apex;
            }
        sb.AppendLine("<h2>Knockback shape</h2>");
        sb.AppendLine($"<div class=\"legend\">Arc = height (m) vs horizontal travel (m). <b>Common scale across the character</b> — axes span {F(gMaxTravel)} m wide &times; {F(gMaxHeight)} m tall, so absolute size is comparable move-to-move. Red dot = apex; red dashed line = blast crossing (KO). <b>Spike moves (negative angle) launch from {F(SpikeLaunchAltitude)} m up</b> (off-stage scenario) so the downward send is visible.</div>");
        foreach (var m in r.Moves)
        {
            sb.AppendLine($"<div class=\"move\"><strong>{Escape(DisplayLabel(m, r))}{MoveTag(m)}</strong><div class=\"arcs\">");
            for (int i = 0; i < m.Trajectories.Length; i++)
            {
                var tr = m.Trajectories[i];
                sb.AppendLine($"<figure>{ArcSvg(tr, gMaxTravel, gMaxHeight)}<figcaption>{tr.Pct}% &middot; KV {F(tr.Kv)} &middot; apex {F(tr.Apex)}m &middot; stun {tr.Stun}</figcaption></figure>");
            }
            sb.AppendLine("</div></div>");
        }

        // B2 — DI escape-space (interactive: global % selector, one large figure per move)
        bool hasDi = r.Moves.Any(m => m.DiEscape.Length > 0);
        if (hasDi)
        {
            sb.AppendLine("<h2>DI escape-space</h2>");
            sb.AppendLine("<div class=\"legend\">The baseline launch plus the same launch with the victim holding one DI direction through hitstun — " +
                "<span class=\"swatch\" style=\"background:#1a5fd0\"></span>baseline " +
                "<span class=\"swatch\" style=\"background:#2ecc71\"></span>in (MoveY &minus;1, toward attacker) " +
                "<span class=\"swatch\" style=\"background:#e74c3c\"></span>away (MoveY +1) " +
                "<span class=\"swatch\" style=\"background:#f39c12\"></span>up (MoveX +1) " +
                "<span class=\"swatch\" style=\"background:#9b59b6\"></span>down (MoveX &minus;1) — " +
                "rotated by the sim's real <code>ApplyDirectionalInfluence</code> (18&deg; cap, Melee sin&sup2; curve; perpendicular holds bend most; in/away on horizontal sends only push via ASDI). <b>Escape magnitude = max launch-vector deviation</b> in the caption: low = DI-resistant (reliable combo/kill tool), high = DI-bendable (escapable). Common scale across the character; pick the victim % above each figure's grid.</div>");
            sb.AppendLine("<div class=\"legend\"><label for=\"diPct\"><b>Victim %:</b></label> <select id=\"diPct\" onchange=\"diShow()\">");
            foreach (var p in r.Percents)
                sb.AppendLine($"<option value=\"{p}\"{(p == r.Percents[0] ? " selected" : "")}>{p}%</option>");
            sb.AppendLine("</select></div>");
            sb.AppendLine("<div class=\"di-figs\">");
            foreach (var m in r.Moves)
                if (m.DiEscape.Length > 0)
                    sb.AppendLine(DiFigure(m, r, gMaxTravel, gMaxHeight));
            sb.AppendLine("</div>");
        }

        // C — KO + blast clearance
        sb.AppendLine("<h2>KO &amp; blast clearance</h2>");
        sb.AppendLine("<div class=\"legend\">kill % = lowest victim % at which the launch crosses a blast line before landing (&mdash; = no KO by 250%). Clearance = fraction of the way to the top / side blast line at the highest simulated %, so a move that can't kill still shows how close it gets.</div>");
        sb.AppendLine("<table class=\"heat\"><thead><tr><th class=\"move\">move</th><th>kill %</th><th>top</th><th>side</th></tr></thead><tbody>");
        foreach (var m in r.Moves)
        {
            int tp = (int)Math.Round(m.Clearance.TopFrac * 100), sp = (int)Math.Round(m.Clearance.SideFrac * 100);
            sb.AppendLine($"<tr><td class=\"move\">{Escape(DisplayLabel(m, r))}{MoveTag(m)}</td>");
            sb.AppendLine($"<td>{(m.KillPct != null ? $"{m.KillPct}% {Escape(m.KillLine!)}" : "&mdash;")}</td>");
            sb.AppendLine($"<td class=\"clr\">{Bar(tp)}</td><td class=\"clr\">{Bar(sp)}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");

        // C2 — authored hitbox reach (issue #151): side-view overlays + range ladder + gaps
        if (r.Reach != null)
        {
            sb.AppendLine("<h2>Authored hitbox reach</h2>");
            sb.AppendLine("<div class=\"legend\">Side-view overlay of the authored hitbox volumes across the active frames, resolved with the real <code>HitboxGeometry.ResolvePositions</code> at the grounded origin frame (yaw 0 &rarr; forward = +Z). Character silhouette = capsule (2&times; radius wide). Colored capsule lines per hit, one per active tick (opacity 0.18 &mdash; overlap darkens, so the union reads). Bands = thirds of capsule height from the feet (high +0.5 m headroom); <b>reach = max forward (Z) extent over the bands</b>. Gaps = per-height whiff zones below the kit's max reach at that height; uncovered bands = no normal reaches them.</div>");
            sb.AppendLine("<div class=\"legend\">" + string.Join("", HitColors.Select(c =>
                $"<span class=\"swatch\" style=\"background:{c.Color}\"></span>{c.Name} ")) + "</div>");
            // Band definitions (thirds of capsule height) are identical across hits — take them from the first.
            var bandDefs = r.Reach.Hits.FirstOrDefault()?.Bands
                .Select(b => (b.Name, b.YMin, b.YMax)).ToArray() ?? Array.Empty<(string, float, float)>();
            for (int i = 0; i < r.Moves.Length && i < r.Reach.Hits.Length; i++)
            {
                var m = r.Moves[i];
                var rh = r.Reach.Hits[i];
                string label = r.Moves.Count(x => x.Label == rh.Label) > 1 ? $"{rh.Label} (hit {rh.HitIndex + 1})" : rh.Label;
                sb.AppendLine($"<h3>{Escape(label)}{MoveTag(m)}</h3>");
                sb.AppendLine(ReachSvg(r.Reach, rh));
                sb.AppendLine($"<div class=\"legend\">reach {(rh.Reach != null ? $"{F(rh.Reach.Value)} m" : "no band coverage")}</div>");
            }
            sb.AppendLine("<h3>Range ladder</h3>");
            sb.AppendLine("<table class=\"heat\"><thead><tr><th class=\"move\">move</th><th>hit</th><th>low m</th><th>mid m</th><th>high m</th><th>reach m</th></tr></thead><tbody>");
            foreach (var rh in r.Reach.Hits.OrderByDescending(x => x.Reach ?? float.NegativeInfinity))
            {
                string label = r.Moves.Count(x => x.Label == rh.Label) > 1 ? $"{rh.Label} (hit {rh.HitIndex + 1})" : rh.Label;
                sb.AppendLine($"<tr><td class=\"move\">{Escape(label)}</td><td>{rh.HitIndex + 1}</td>" +
                    $"<td>{ReachCell(rh.Bands[0].MaxZ, "&mdash;")}</td><td>{ReachCell(rh.Bands[1].MaxZ, "&mdash;")}</td>" +
                    $"<td>{ReachCell(rh.Bands[2].MaxZ, "&mdash;")}</td><td><b>{ReachCell(rh.Reach, "&mdash;")}</b></td></tr>");
            }
            sb.AppendLine("</tbody></table>");
            sb.AppendLine("<div class=\"legend\"><b>Coverage gaps</b></div>");
            sb.AppendLine("<ul>");
            foreach (var g in r.Reach.Gaps)
                sb.AppendLine($"<li>{Escape(g.Band)} @ {g.HMin:0.0}&ndash;{g.HMax:0.0} m: nothing covers {g.From:0.0}&ndash;{g.To:0.0} m</li>");
            foreach (var name in r.Reach.UncoveredBands)
            {
                var b = bandDefs.FirstOrDefault(x => x.Name == name);
                sb.AppendLine($"<li>no normal reaches the {Escape(name)} band" +
                    (b.Name != null ? $" ({b.YMin:0.00}&ndash;{b.YMax:0.00} m)" : "") + "</li>");
            }
            sb.AppendLine("</ul>");
        }

        // D — true-combo reachability (real sim) + combo density
        if (r.TrueCombos.Length > 0)
        {
            sb.AppendLine("<h2>True-combo reachability</h2>");
            sb.AppendLine("<div class=\"legend\">Pure reachability (no scripted routes, no recommendations): for each normal &times; hit state &times; %, the starter connects through the real hit path, then the attacker plays a greedy chase and presses each follow-up as soon as actionable + in reach. Cell: <b>T</b> = follow-up landed while the victim was still in hitstun (true combo) &middot; <b>F</b> = landed after stun expired (opponent actionable) &middot; &ndash; = never connected. <b>tight</b> = window tightness per % — <code>sim stun (0.5 &times; launch speed) &minus; (recovery + landing lag + jump squat + follow-up trigger)</code>; positive = frame-true on paper (travel + hitstop make reality &le; paper). The sim derives hitstun from launch speed; the authored <code>StunTicks</code> is a zero/nonzero gate only, so tightness grows as the victim takes damage. Hover a cell for connect tick / stun left / tightness.</div>");
            sb.AppendLine("<div class=\"legend\"><b>Combo density</b> — total true links (all starters &times; follow-ups &times; hit states) per victim %: a tuning target for too-combo-heavy vs too-sparse. No threshold is hardcoded; the number is yours to read.</div>");
            sb.AppendLine("<table class=\"heat\"><thead><tr><th class=\"move\">%</th><th>grounded</th><th>airborne</th><th>total</th></tr></thead><tbody>");
            foreach (var d in r.ComboDensity)
                sb.AppendLine($"<tr><td class=\"move\">{d.Pct}%</td><td>{d.Grounded}</td><td>{d.Airborne}</td><td><b>{d.Total}</b></td></tr>");
            sb.AppendLine("</tbody></table>");
            foreach (var starter in r.TrueCombos)
            {
                sb.AppendLine($"<h3>{Escape(starter.Move)} &mdash; {Escape(starter.State)} hit</h3>");
                sb.AppendLine("<table class=\"heat\"><thead><tr><th class=\"move\">follow-up</th><th>tight@0%</th>");
                for (int i = 0; i < r.Percents.Length; i++) sb.AppendLine($"<th>{r.Percents[i]}%</th>");
                sb.AppendLine("</tr></thead><tbody>");
                foreach (var grp in starter.Edges.GroupBy(e => e.FollowUp))
                {
                    int tight0 = grp.FirstOrDefault(x => x.Pct == r.Percents[0])?.Tightness ?? 0;
                    sb.AppendLine($"<tr><td class=\"move\">{Escape(grp.Key)}</td><td>{tight0}</td>");
                    foreach (var p in r.Percents)
                    {
                        var e = grp.FirstOrDefault(x => x.Pct == p);
                        string cell = e == null ? "<td>&ndash;</td>"
                            : e.Verdict == "true" ? $"<td style=\"background:#d9f2d9\" title=\"connect t={e.ConnectTick} &middot; stun left {e.StunLeft} &middot; tight {e.Tightness}\">T</td>"
                            : e.Verdict == "false" ? $"<td style=\"background:#fdecea\" title=\"connect t={e.ConnectTick} after stun &middot; tight {e.Tightness}\">F</td>"
                            : $"<td style=\"background:#f2f3f5\" title=\"tight {e.Tightness}\">&ndash;</td>";
                        sb.AppendLine(cell);
                    }
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
        }

        sb.AppendLine("<script>function sortT(n,nums){var tb=document.getElementById('adv').tBodies[0],rows=[].slice.call(tb.rows);rows.sort(function(a,b){var av=a.cells[n].textContent,bv=b.cells[n].textContent;if(nums){av=parseInt(av)||-9999;bv=parseInt(bv)||-9999;return av-bv;}return av.localeCompare(bv);});rows.forEach(function(r){tb.appendChild(r);});}function diShow(){var p=document.getElementById('diPct').value;document.querySelectorAll('.di-fig').forEach(function(f){[].forEach.call(f.querySelectorAll('.pct'),function(g){g.style.display=g.getAttribute('data-pct')===p?'':'none';});});}diShow();</script>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // ── CLI ──────────────────────────────────────────────────────────────────

    internal static int[]? ParsePcts(string[] args)
    {
        int i = Array.IndexOf(args, "--pcts");
        if (i < 0 || i + 1 >= args.Length) return null;
        return args[i + 1].Split(',').Select(int.Parse).ToArray();
    }

    internal static string? ParseOut(string[] args)
    {
        int i = Array.IndexOf(args, "--out");
        if (i < 0 || i + 1 >= args.Length) return null;
        return args[i + 1];
    }
}
