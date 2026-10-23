#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.AerialApproachReport;

internal static partial class Program
{
    private sealed class SlideFollowupRow
    {
        public string Character = "";
        public string FallProfile = "";
        public float SlideDeceleration;
        public string Scenario = "";
        public string Route = "";
        public int Cycle;
        public string Status = "";
        public int Tick;
        public int? TakeoffTick;
        public int? SlideStartTick;
        public int? FirstActiveTick;
        public int? RecoveryEndTick;
        public int? AcceptedFollowupTick;
        public float? TakeoffX, TakeoffZ, TakeoffVX, TakeoffVZ;
        public float? FirstActiveX, FirstActiveZ, FirstActiveVX, FirstActiveVZ;
        public float? RecoveryEndX, RecoveryEndZ, RecoveryEndVX, RecoveryEndVZ;
        public byte JumpsLeft, AirDodgesLeft;
        public ushort Cooldown;
        public ushort AnimLockTicks, LandingLagTicks, AttackElapsedTicks;
        public bool Carry, Settled;
        public string Notes = "";
    }

    private sealed class SlideTargetRow
    {
        public string Character = "";
        public string FallProfile = "";
        public float SlideDeceleration;
        public string AirNormal = "";
        public string TargetCharacter = "";
        public string TargetMode = "";
        public float Distance;
        public float SupportHeight;
        public string Placement = "";
        public float DeclaredTargetX, DeclaredTargetZ;
        public float ActualTargetX, ActualTargetZ;
        public string Status = "";
        public int? TakeoffTick, LandingTick, SlideStartTick, AcceptedFollowupTick;
        public int? FirstActiveTick, RecoveryEndTick, FirstHitTick;
        public bool AerialAccepted, GroundNormalAccepted;
        public int HitCount;
        public float TotalDamage;
        public int IncomingHitCount;
        public float IncomingDamage;
        public string IncomingHitDetails = "";
        public float EndpointDistance, Overshoot;
        public string ContactTimings = "";
        public string HitDetails = "";
        public byte JumpsLeft, AirDodgesLeft;
        public ushort Cooldown, AnimLockTicks, LandingLagTicks;
        public string Notes = "";
    }

    private static object WriteSlideFollowupsAndCombat(
        string directory,
        StreamWriter traces,
        IReadOnlyList<MatchContentEntry> entries,
        DownActionTuning tuning,
        string fallProfile,
        bool assert)
    {
        Directory.CreateDirectory(directory);
        var followups = new List<SlideFollowupRow>();
        var targets = new List<SlideTargetRow>();
        var blocked = new List<string>();
        var combatArenas = new[] { CombatFixtureArena(0), CombatFixtureArena(2) };

        foreach (var entry in entries)
        {
            RequireAdmittedCooked(entry);
            var def = SlideDefinition(entry, fallProfile);
            AddJumpComparison(entry, def, tuning, fallProfile, traces, followups, blocked);
            AddSlideFollowupComparison(entry, def, tuning, fallProfile, traces, followups, blocked);
            AddLandingComparisons(entry, def, tuning, fallProfile, traces, followups, blocked);
            AddRepeatedCycles(entry, def, tuning, fallProfile, traces, followups, blocked, landingSlide: false);
            AddRepeatedCycles(entry, def, tuning, fallProfile, traces, followups, blocked, landingSlide: true);
            AddStationaryAndSlidingNormal(entry, def, tuning, fallProfile, traces, followups, blocked);
            AddGeometryEvidence(entry, def, tuning, fallProfile, traces, followups);

            var aerialSlots = CanonicalAerialSlots().ToArray();
            foreach (float support in new[] { 0f, 2f })
            foreach (bool edge in new[] { false, true })
            foreach (var air in aerialSlots)
            foreach (var mode in new[] { "stationary", "retreat", "ground1-poke" })
            foreach (float distance in TargetDistanceGrid)
            {
                var targetEntry = entries.FirstOrDefault(x => !ReferenceEquals(x, entry)) ?? entry;
                var row = RunCombatRoute(entry, targetEntry, air.Id, air, mode, distance, support, edge,
                    tuning, fallProfile, traces, combatArenas[support == 0 ? 0 : 1], delayed: false, noAerial: false);
                targets.Add(row);
            }

            foreach (var control in new[] { (Name: "delayed", Delay: true, NoAerial: false), (Name: "no-aerial", Delay: false, NoAerial: true) })
            foreach (float support in new[] { 0f, 2f })
            foreach (bool edge in new[] { false, true })
            foreach (string mode in new[] { "stationary", "retreat", "ground1-poke" })
            foreach (float distance in TargetDistanceGrid)
            {
                var targetEntry = entries.FirstOrDefault(x => !ReferenceEquals(x, entry)) ?? entry;
                var row = RunCombatRoute(entry, targetEntry, control.Name, control.NoAerial ? null : aerialSlots[0], mode, distance, support, edge,
                    tuning, fallProfile, traces, combatArenas[support == 0 ? 0 : 1], control.Delay, control.NoAerial);
                targets.Add(row);
            }
        }

        File.WriteAllText(Path.Combine(directory, "slide-followups.csv"), SlideFollowupCsv(followups));
        File.WriteAllText(Path.Combine(directory, "slide-targets.csv"), SlideTargetCsv(targets));

        if (assert)
        {
            if (followups.Count == 0 || targets.Count == 0)
                throw new InvalidDataException("slide self-check: no follow-up or target evidence rows");
            foreach (var row in followups.Where(x => x.Scenario == "repeated-cycles"))
            {
                if (row.Cycle < 1 || row.Cycle > 10)
                    throw new InvalidDataException($"slide self-check: invalid repeated cycle {row.Cycle}");
                if (row.JumpsLeft > entries.Single(x => x.Identity.PackageId == row.Character).Definition.Movement.MaxJumps || row.AirDodgesLeft > 1)
                    throw new InvalidDataException($"slide self-check: resource revival in {row.Character} cycle {row.Cycle}");
            }
            foreach (var row in targets)
            {
                if (row.HitCount < 0 || float.IsNaN(row.TotalDamage) || float.IsInfinity(row.TotalDamage))
                    throw new InvalidDataException($"slide self-check: invalid resolver result for {row.Character}/{row.AirNormal}/{row.TargetMode}");
                if (row.GroundNormalAccepted && row.AcceptedFollowupTick == null)
                    throw new InvalidDataException($"slide self-check: accepted normal lacks tick for {row.Character}/{row.AirNormal}");
            }
        }

        traces.Flush();
        return new Dictionary<string, object?>
        {
            ["fallProfile"] = fallProfile,
            ["slideDecelerationRatio"] = tuning.SlideDecelerationRatio,
            ["braceMultiplier"] = tuning.CrouchLaunchMultiplier,
            ["followupRows"] = followups.Count,
            ["targetRows"] = targets.Count,
            ["acceptedGroundNormals"] = targets.Count(x => x.GroundNormalAccepted),
            ["resolverHitRows"] = targets.Count(x => x.HitCount > 0),
            ["totalResolverHits"] = targets.Sum(x => x.HitCount),
            ["blockedRoutes"] = blocked.Distinct(StringComparer.Ordinal).ToArray(),
            ["coverage"] = new Dictionary<string, object?>
            {
                ["characters"] = entries.Select(x => x.Identity.PackageId).ToArray(),
                ["airNormals"] = CanonicalAerialSlots().Select(x => x.Id).ToArray(),
                ["targetModes"] = new[] { "stationary", "retreat", "ground1-poke" },
                ["distances"] = TargetDistanceGrid,
                ["supportHeights"] = new[] { 0f, 2f },
                ["placements"] = new[] { "center", "near-edge" },
                ["controls"] = new[] { "delayed", "no-aerial" },
                ["combatLabel"] = "finite measured resolver sweep; not balance proof",
            },
        };
    }

    private static void AddJumpComparison(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, List<SlideFollowupRow> rows, List<string> blocked)
    {
        rows.Add(RunJumpRoute(entry, def, tuning, fallProfile, traces, slideJump: false, jumpDelayTicks: 0, blocked));
        rows.Add(RunJumpRoute(entry, def, tuning, fallProfile, traces, slideJump: true, jumpDelayTicks: 0, blocked));
        rows.Add(RunJumpRoute(entry, def, tuning, fallProfile, traces, slideJump: true, jumpDelayTicks: 6, blocked));
    }

    private static SlideFollowupRow RunJumpRoute(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, bool slideJump, int jumpDelayTicks, List<string> blocked)
    {
        const ulong id = 1;
        var sim = new ServerSimulation(SlideArena(0f), downActionTuning: tuning);
        sim.RegisterEntity(id, def, InitialState(def, 0f, 0f, true, 0f), entry.BakedAnimation);
        int? takeoff = null, landing = null, accepted = null;
        CharacterState? takeoffState = null, landingState = null;
        int jumpTick = slideJump ? 19 + jumpDelayTicks : 18;
        string scenario = !slideJump ? "run-jump" : jumpDelayTicks == 0 ? "run-slide-jump" : "run-slide-jump-delayed-6";
        int lastTick = 0;
        for (int tick = 0; tick < 240; tick++)
        {
            var before = sim.GetState(id);
            var input = new InputState
            {
                MoveY = tick < 18 || (!slideJump && tick == 18) ? 1f : 0f,
                Down = slideJump && tick >= 18 && tick < jumpTick,
                DownPressed = slideJump && tick == 18,
                Jump = tick == jumpTick,
                JumpHeld = tick >= jumpTick,
            };

            sim.Tick(new Dictionary<ulong, InputState> { [id] = input });
            var after = sim.GetState(id);
            WriteSlideTrace(traces, $"{entry.Identity.PackageId}/{scenario}", tick, input, after, sim, id,
                input.Jump ? "jump" : input.DownPressed ? "slide-edge" : "move");
            lastTick = tick;
            if (accepted == null && input.Jump && sim.LastTickAcceptedActions.Contains(id))
                accepted = tick;
            if (takeoff == null && before.IsGrounded && !after.IsGrounded)
            {
                takeoff = tick;
                takeoffState = after;
            }
            if (takeoff != null && landing == null && sim.LastTickTouchdowns.Contains(id))
            {
                landing = tick;
                landingState = after;
                break;
            }
        }

        if (takeoff == null) blocked.Add($"{entry.Identity.PackageId}:{scenario}:no-takeoff");
        if (landing == null) blocked.Add($"{entry.Identity.PackageId}:{scenario}:timeout-nonlanding");
        var final = sim.GetState(id);
        return new SlideFollowupRow
        {
            Character = entry.Identity.PackageId, FallProfile = fallProfile, SlideDeceleration = tuning.SlideDecelerationRatio,
            Scenario = scenario, Route = slideJump ? "run→slide→jump" : "run→jump", Cycle = 0,
            Status = takeoff != null && landing != null ? "complete" : "blocked/timeout", Tick = lastTick,
            TakeoffTick = takeoff, AcceptedFollowupTick = accepted,
            TakeoffX = takeoffState?.PX, TakeoffZ = takeoffState?.PZ, TakeoffVX = takeoffState?.VX, TakeoffVZ = takeoffState?.VZ,
            RecoveryEndTick = landing, RecoveryEndX = landingState?.PX, RecoveryEndZ = landingState?.PZ,
            RecoveryEndVX = landingState?.VX, RecoveryEndVZ = landingState?.VZ,
            JumpsLeft = final.JumpsLeft, AirDodgesLeft = final.AirDodgesLeft,
            Cooldown = final.GetCooldown(AbilitySlots.Slot1), AnimLockTicks = final.AnimLockTicks,
            LandingLagTicks = final.LandingLagTicks, AttackElapsedTicks = final.AttackElapsedTicks,
            Carry = final.SlideAttackCarryActive, Settled = final.CrouchSettled,
            Notes = slideJump ? "jump requested on first slide tick; no extra takeoff impulse" : "ordinary grounded jump control",
        };
    }

    private static void AddSlideFollowupComparison(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, List<SlideFollowupRow> rows, List<string> blocked)
    {
        rows.Add(RunGroundNormalFollowup(entry, def, tuning, fallProfile, traces, delayedTicks: 0, blocked));
        rows.Add(RunGroundNormalFollowup(entry, def, tuning, fallProfile, traces, delayedTicks: 6, blocked));
        rows.Add(RunReleaseToSteer(entry, def, tuning, fallProfile, traces, blocked));
    }

    private static SlideFollowupRow RunGroundNormalFollowup(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, int delayedTicks, List<string> blocked)
    {
        const ulong id = 1;
        byte normal = WireSlot("1");
        var sim = new ServerSimulation(SlideArena(0f), downActionTuning: tuning);
        sim.RegisterEntity(id, def, InitialState(def, 0f, 0f, true, 0f), entry.BakedAnimation);
        int? slideTick = null, accepted = null, active = null, recovery = null;
        CharacterState? activeState = null, recoveryState = null;
        ulong activation = 0;
        int lastTick = 0;
        for (int tick = 0; tick < 300; tick++)
        {
            var before = sim.GetState(id);
            bool request = slideTick.HasValue && accepted == null && tick >= slideTick.Value + 1 + delayedTicks;
            var input = tick < 20
                ? SlideInput(0f, 1f, false, false, false, false)
                : tick == 20
                    ? SlideInput(0f, 1f, true, true, false, false)
                    : SlideInput(0f, 0f, true, false, false, false);
            if (request)
            {
                input.ActiveSlot = normal;
                input.Down = true;
            }
            sim.Tick(new Dictionary<ulong, InputState> { [id] = input });
            var after = sim.GetState(id);
            WriteSlideTrace(traces, $"{entry.Identity.PackageId}/ground-normal-delay-{delayedTicks}",
                tick, input, after, sim, id, request ? "ground.1" : input.DownPressed ? "slide-edge" : "hold-down");
            lastTick = tick;
            if (slideTick == null && after.IsGrounded && after.State == ActionState.Sliding)
                slideTick = tick;
            if (request && accepted == null && sim.GetActiveAbility(id) != null && after.AttackSlot == normal)
            {
                accepted = tick;
                activation = sim.GetLastActivationId(id);
            }
            if (accepted != null && active == null && sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == id && x.ActivationId == activation))
            {
                active = tick;
                activeState = after;
            }
            if (accepted != null && recovery == null && sim.GetActiveAbility(id) == null && after.AttackSlot == 0 && tick > accepted)
            {
                recovery = tick;
                recoveryState = after;
                break;
            }
        }
        var final = sim.GetState(id);
        if (slideTick == null) blocked.Add($"{entry.Identity.PackageId}:ground-normal:{delayedTicks}:no-slide");
        if (accepted == null) blocked.Add($"{entry.Identity.PackageId}:ground-normal:{delayedTicks}:blocked");
        return new SlideFollowupRow
        {
            Character = entry.Identity.PackageId, FallProfile = fallProfile, SlideDeceleration = tuning.SlideDecelerationRatio,
            Scenario = delayedTicks == 0 ? "first-legal-ground-normal" : "six-tick-delayed-ground-normal",
            Route = delayedTicks == 0 ? "run→slide→ground.1(first legal)" : "run→slide→ground.1(+6 ticks)",
            Status = accepted != null ? "complete" : "blocked", Tick = lastTick,
            SlideStartTick = slideTick, FirstActiveTick = active, RecoveryEndTick = recovery,
            AcceptedFollowupTick = accepted,
            FirstActiveX = activeState?.PX, FirstActiveZ = activeState?.PZ, FirstActiveVX = activeState?.VX, FirstActiveVZ = activeState?.VZ,
            RecoveryEndX = recoveryState?.PX, RecoveryEndZ = recoveryState?.PZ, RecoveryEndVX = recoveryState?.VX, RecoveryEndVZ = recoveryState?.VZ,
            JumpsLeft = final.JumpsLeft, AirDodgesLeft = final.AirDodgesLeft, Cooldown = final.GetCooldown(normal),
            AnimLockTicks = final.AnimLockTicks, LandingLagTicks = final.LandingLagTicks,
            AttackElapsedTicks = final.AttackElapsedTicks, Carry = final.SlideAttackCarryActive, Settled = final.CrouchSettled,
            Notes = "first active/recovery are observed from the real resolver ability; no timing override",
        };
    }

    private static SlideFollowupRow RunReleaseToSteer(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, List<string> blocked)
    {
        const ulong id = 1;
        var sim = new ServerSimulation(SlideArena(0f), downActionTuning: tuning);
        sim.RegisterEntity(id, def, InitialState(def, 0f, 0f, true, 0f), entry.BakedAnimation);
        int? slideTick = null, releaseTick = null;
        CharacterState? releaseState = null;
        int lastTick = 0;
        for (int tick = 0; tick < 140; tick++)
        {
            var before = sim.GetState(id);
            InputState input = tick < 20
                ? SlideInput(0f, 1f, false, false, false, false)
                : tick == 20
                    ? SlideInput(0f, 1f, true, true, false, false)
                    : slideTick == null
                        ? SlideInput(0f, 0f, true, false, false, false)
                        : tick == slideTick.Value + 1
                            ? SlideInput(1f, 0f, false, false, false, false)
                            : SlideInput(1f, 0f, false, false, false, false);
            sim.Tick(new Dictionary<ulong, InputState> { [id] = input });
            var after = sim.GetState(id);
            WriteSlideTrace(traces, $"{entry.Identity.PackageId}/release-to-steer", tick, input, after, sim, id,
                input.DownPressed ? "slide-edge" : input.Down ? "hold-down" : "release-steer");
            lastTick = tick;
            if (slideTick == null && after.State == ActionState.Sliding) slideTick = tick;
            if (slideTick != null && releaseTick == null && !input.Down)
            {
                releaseTick = tick;
                releaseState = after;
                break;
            }
        }
        if (releaseTick == null) blocked.Add($"{entry.Identity.PackageId}:release-to-steer:no-slide-release");
        var final = sim.GetState(id);
        return new SlideFollowupRow
        {
            Character = entry.Identity.PackageId, FallProfile = fallProfile, SlideDeceleration = tuning.SlideDecelerationRatio,
            Scenario = "release-to-steer", Route = "slide→release→steer", Status = releaseTick != null ? "complete" : "blocked", Tick = lastTick,
            SlideStartTick = slideTick, RecoveryEndTick = releaseTick, AcceptedFollowupTick = releaseTick,
            RecoveryEndX = releaseState?.PX, RecoveryEndZ = releaseState?.PZ, RecoveryEndVX = releaseState?.VX, RecoveryEndVZ = releaseState?.VZ,
            JumpsLeft = final.JumpsLeft, AirDodgesLeft = final.AirDodgesLeft, Cooldown = final.GetCooldown(AbilitySlots.Slot1),
            AnimLockTicks = final.AnimLockTicks, LandingLagTicks = final.LandingLagTicks,
            AttackElapsedTicks = final.AttackElapsedTicks, Carry = final.SlideAttackCarryActive, Settled = final.CrouchSettled,
            Notes = "released Down runs ordinary steering on the release tick",
        };
    }

    private static void AddLandingComparisons(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, List<SlideFollowupRow> rows, List<string> blocked)
    {
        foreach (float support in new[] { 0f, 2f })
        foreach (bool fastFall in new[] { false, true })
            rows.Add(RunLandingRoute(entry, def, tuning, fallProfile, traces, support, fastFall, blocked));
    }

    private static SlideFollowupRow RunLandingRoute(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, float support, bool fastFall, List<string> blocked, bool edge = false)
    {
        const ulong id = 1;
        var sim = new ServerSimulation(SlideArena(support, edge), downActionTuning: tuning);
        var initial = InitialState(def, 0f, 0f, false, 0f);
        initial.PY = support + def.CapsuleHeight * .5f + 2f;
        initial.VY = -2f;
        initial.VZ = def.Movement.RunSpeed * .6f;
        initial.AirTimeTicks = 120;
        sim.RegisterEntity(id, def, initial, entry.BakedAnimation);
        int? landing = null;
        CharacterState? landingState = null;
        int lastTick = 0;
        for (int tick = 0; tick < 240; tick++)
        {
            var before = sim.GetState(id);
            bool press = fastFall && !before.IsGrounded && before.VY < 0f && !before.IsFastFalling;
            var input = SlideInput(0f, 0f, down: true, downPressed: press, jump: false, jumpHeld: false);
            sim.Tick(new Dictionary<ulong, InputState> { [id] = input });
            var after = sim.GetState(id);
            WriteSlideTrace(traces, $"{entry.Identity.PackageId}/landing-{support}-{fastFall}", tick, input, after, sim, id,
                press ? "fast-fall-edge" : "hold-down");
            lastTick = tick;
            if (sim.LastTickTouchdowns.Contains(id))
            {
                landing = tick;
                landingState = after;
                break;
            }
        }
        if (landing == null) blocked.Add($"{entry.Identity.PackageId}:{(fastFall ? "fast-fall" : "ordinary")}:timeout-nonlanding");
        var final = sim.GetState(id);
        return new SlideFollowupRow
        {
            Character = entry.Identity.PackageId, FallProfile = fallProfile, SlideDeceleration = tuning.SlideDecelerationRatio,
            Scenario = fastFall ? "fast-fall-landing" : "ordinary-landing", Route = "air→landing-slide",
            Status = landing != null ? "complete" : "timeout", Tick = lastTick,
            RecoveryEndTick = landing, RecoveryEndX = landingState?.PX, RecoveryEndZ = landingState?.PZ,
            RecoveryEndVX = landingState?.VX, RecoveryEndVZ = landingState?.VZ,
            JumpsLeft = final.JumpsLeft, AirDodgesLeft = final.AirDodgesLeft, Cooldown = final.GetCooldown(AbilitySlots.Slot1),
            AnimLockTicks = final.AnimLockTicks, LandingLagTicks = final.LandingLagTicks,
            AttackElapsedTicks = final.AttackElapsedTicks, Carry = final.SlideAttackCarryActive, Settled = final.CrouchSettled,
            Notes = $"support={support.ToString("0.###", CultureInfo.InvariantCulture)}; declared initial XZ speed=.6 RunSpeed; no added touchdown displacement",
        };
    }

    private static void AddRepeatedCycles(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, List<SlideFollowupRow> rows, List<string> blocked, bool landingSlide)
    {
        const ulong id = 1;
        float support = landingSlide ? 2f : 0f;
        var sim = new ServerSimulation(SlideArena(support), downActionTuning: tuning);
        var initial = InitialState(def, 0f, 0f, true, 0f);
        initial.PY += support;
        initial.SetCooldown(AbilitySlots.Slot1, 90);
        sim.RegisterEntity(id, def, initial, entry.BakedAnimation);
        int cycle = 0, phase = 0, phaseTicks = 0;
        int? slideTick = null, jumpTick = null, takeoffTick = null;
        CharacterState? takeoffState = null;
        bool fastFallSent = false;
        for (int tick = 0; tick < 6000 && cycle < 10; tick++)
        {
            var before = sim.GetState(id);
            float direction = cycle % 2 == 0 ? 1f : -1f;
            var input = new InputState { JumpHeld = phase >= 2 };
            if (phase == 0) input.MoveY = direction;
            else if (phase == 1)
            {
                input.Jump = landingSlide;
                input.JumpHeld = landingSlide;
                input.Down = input.DownPressed = !landingSlide;
            }
            else if (phase == 2 && landingSlide)
            {
                input.Down = fastFallSent;
                if (!before.IsGrounded && before.VY < 0 && !fastFallSent)
                {
                    input.Down = input.DownPressed = true;
                    fastFallSent = true;
                }
            }
            else if (phase == 2 || phase == 3)
            {
                input.Down = true;
                input.Jump = true;
            }
            sim.Tick(new Dictionary<ulong, InputState> { [id] = input });
            var after = sim.GetState(id);
            WriteSlideTrace(traces, $"{entry.Identity.PackageId}/repeated-{(landingSlide ? "landing-slide" : "run-slide")}", tick, input, after, sim, id,
                input.Jump ? "cycle-jump" : input.DownPressed ? "down-edge" : "");
            if (after.JumpsLeft > def.Movement.MaxJumps || after.AirDodgesLeft > 1
                || (!before.IsGrounded && !sim.LastTickTouchdowns.Contains(id) && after.JumpsLeft > before.JumpsLeft)
                || after.GetCooldown(AbilitySlots.Slot1) > before.GetCooldown(AbilitySlots.Slot1))
                throw new InvalidDataException($"Repeated movement revived a resource: {entry.Identity.PackageId}/{cycle}/{tick}");
            if (phase == 0)
            {
                if (++phaseTicks == 18) { phase = 1; phaseTicks = 0; }
            }
            else if (phase == 1)
            {
                if (!landingSlide)
                {
                    if (after.State != ActionState.Sliding) break;
                    slideTick = tick;
                }
                phase = 2;
            }
            else if (phase == 2 && landingSlide)
            {
                if (sim.LastTickTouchdowns.Contains(id))
                {
                    if (after.State != ActionState.Sliding) break;
                    slideTick = tick;
                    phase = 3;
                }
            }
            else if (phase == 2 || phase == 3)
            {
                if (!sim.LastTickAcceptedActions.Contains(id) || !after.JumpFromSlide) break;
                jumpTick = tick;
                phase = 4;
            }
            else
            {
                if (before.IsGrounded && !after.IsGrounded) { takeoffTick = tick; takeoffState = after; }
                if (!sim.LastTickTouchdowns.Contains(id)) continue;
                cycle++;
                rows.Add(new SlideFollowupRow
                {
                    Character = entry.Identity.PackageId, FallProfile = fallProfile,
                    SlideDeceleration = tuning.SlideDecelerationRatio, Scenario = "repeated-cycles",
                    Route = landingSlide ? "run→air→landing-slide→jump" : "run→slide→jump",
                    Cycle = cycle, Status = "complete", Tick = tick, SlideStartTick = slideTick,
                    AcceptedFollowupTick = jumpTick, TakeoffTick = takeoffTick,
                    TakeoffX = takeoffState?.PX, TakeoffZ = takeoffState?.PZ,
                    TakeoffVX = takeoffState?.VX, TakeoffVZ = takeoffState?.VZ,
                    RecoveryEndTick = tick, RecoveryEndX = after.PX, RecoveryEndZ = after.PZ,
                    RecoveryEndVX = after.VX, RecoveryEndVZ = after.VZ,
                    JumpsLeft = after.JumpsLeft, AirDodgesLeft = after.AirDodgesLeft,
                    Cooldown = after.GetCooldown(AbilitySlots.Slot1),
                    Notes = "one simulation, alternating run direction; actual cooldowns checked every tick; no resets between cycles",
                });
                phase = phaseTicks = 0;
                slideTick = jumpTick = takeoffTick = null;
                takeoffState = null;
                fastFallSent = false;
            }
        }
        if (cycle != 10)
            throw new InvalidDataException($"{entry.Identity.PackageId}: repeated {(landingSlide ? "landing" : "run")} cycles completed {cycle}/10");
    }

    private static void AddStationaryAndSlidingNormal(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, List<SlideFollowupRow> rows, List<string> blocked)
    {
        rows.Add(RunNormalActivation(entry, def, tuning, fallProfile, traces, sliding: false, blocked));
        rows.Add(RunNormalActivation(entry, def, tuning, fallProfile, traces, sliding: true, blocked));
    }

    private static SlideFollowupRow RunNormalActivation(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, bool sliding, List<string> blocked)
    {
        const ulong id = 1;
        byte normal = WireSlot("1");
        var sim = new ServerSimulation(SlideArena(0f), downActionTuning: tuning);
        sim.RegisterEntity(id, def, InitialState(def, 0f, 0f, true, 0f), entry.BakedAnimation);
        ulong activation = 0;
        int? accepted = null, active = null, recovery = null;
        CharacterState? acceptedState = null, activeState = null, recoveryState = null;
        for (int tick = 0; tick < 240; tick++)
        {
            var before = sim.GetState(id);
            InputState input;
            if (!sliding)
                input = tick == 0 ? SlideInput(0f, 0f, false, false, false, false, normal) : default;
            else if (tick < 20)
                input = SlideInput(0f, 1f, false, false, false, false);
            else if (tick == 20)
                input = SlideInput(0f, 1f, true, true, false, false);
            else
                input = SlideInput(0f, 0f, true, false, false, false, accepted == null ? normal : (byte)0);
            sim.Tick(new Dictionary<ulong, InputState> { [id] = input });
            var after = sim.GetState(id);
            WriteSlideTrace(traces, $"{entry.Identity.PackageId}/{(sliding ? "sliding-ground-normal" : "stationary-ground-normal")}",
                tick, input, after, sim, id, input.ActiveSlot > 0 ? "ground.1" : input.DownPressed ? "slide-edge" : "neutral");
            if (input.ActiveSlot == normal && accepted == null && sim.GetActiveAbility(id) != null && after.AttackSlot == normal)
            {
                accepted = tick;
                acceptedState = after;
                activation = sim.GetLastActivationId(id);
            }
            if (accepted != null && active == null && sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == id && x.ActivationId == activation))
            {
                active = tick;
                activeState = after;
            }
            if (accepted != null && recovery == null && tick > accepted && sim.GetActiveAbility(id) == null && after.AttackSlot == 0)
            {
                recovery = tick;
                recoveryState = after;
                break;
            }
            _ = before;
        }
        if (accepted == null) blocked.Add($"{entry.Identity.PackageId}:{(sliding ? "sliding" : "stationary")}:ground.1-blocked");
        var final = sim.GetState(id);
        return new SlideFollowupRow
        {
            Character = entry.Identity.PackageId, FallProfile = fallProfile, SlideDeceleration = tuning.SlideDecelerationRatio,
            Scenario = sliding ? "sliding-ground-normal" : "stationary-ground-normal",
            Route = sliding ? "slide→ground.1" : "stationary→ground.1", Status = accepted != null ? "complete" : "blocked",
            Tick = recovery ?? accepted ?? 0, AcceptedFollowupTick = accepted, FirstActiveTick = active, RecoveryEndTick = recovery,
            FirstActiveX = activeState?.PX, FirstActiveZ = activeState?.PZ, FirstActiveVX = activeState?.VX, FirstActiveVZ = activeState?.VZ,
            RecoveryEndX = recoveryState?.PX, RecoveryEndZ = recoveryState?.PZ, RecoveryEndVX = recoveryState?.VX, RecoveryEndVZ = recoveryState?.VZ,
            JumpsLeft = final.JumpsLeft, AirDodgesLeft = final.AirDodgesLeft, Cooldown = final.GetCooldown(normal),
            AnimLockTicks = final.AnimLockTicks, LandingLagTicks = final.LandingLagTicks,
            AttackElapsedTicks = final.AttackElapsedTicks, Carry = final.SlideAttackCarryActive, Settled = final.CrouchSettled,
            Notes = "same canonical opted-in ground.1; stationary and sliding use real activation path",
        };
    }

    private static CollisionTriangle[] ReportFloor(float y, float minX, float maxX, float minZ, float maxZ)
        => new[]
        {
            new CollisionTriangle { AX = minX, AY = y, AZ = minZ, BX = minX, BY = y, BZ = maxZ, CX = maxX, CY = y, CZ = minZ },
            new CollisionTriangle { AX = maxX, AY = y, AZ = maxZ, BX = maxX, BY = y, BZ = minZ, CX = minX, CY = y, CZ = maxZ },
        };

    private static ArenaDefinition CombatFixtureArena(float support)
    {
        var arena = SlideArena(0);
        var triangles = ReportFloor(0, -128, 128, -128, 128).ToList();
        arena.KillHeight = -20;
        if (support > 0)
        {
            triangles.AddRange(ReportFloor(support, -12, 12, -12, 12));
            for (int z = 116; z <= 140; z++)
            for (int x = 116; x <= 140; x++)
                arena.Heightmap.Data[z * 256 + x] = support;
        }
        arena.CollisionTriangles = triangles.ToArray();
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }

    private static void AddGeometryEvidence(MatchContentEntry entry, CharacterDefinition def, DownActionTuning tuning,
        string fallProfile, StreamWriter traces, List<SlideFollowupRow> rows)
    {
        foreach (string fixture in new[] { "wall-tangent", "slope", "step", "walk-off" })
        {
            var arena = CombatFixtureArena(0);
            var triangles = arena.CollisionTriangles.ToList();
            var initial = InitialState(def, 0, 0, true, 0, vx: def.Movement.RunSpeed);
            if (fixture == "wall-tangent")
            {
                initial.PX = 2 - def.CapsuleRadius - .02f;
                initial.VX = initial.VZ = def.Movement.RunSpeed / MathF.Sqrt(2);
                triangles.Add(new CollisionTriangle { AX = 2, AY = 0, AZ = -20, BX = 2, BY = 10, BZ = -20, CX = 2, CY = 0, CZ = 20 });
                triangles.Add(new CollisionTriangle { AX = 2, AY = 0, AZ = 20, BX = 2, BY = 0, BZ = -20, CX = 2, CY = 10, CZ = 20 });
            }
            else if (fixture == "slope")
            {
                triangles.Clear();
                triangles.Add(new CollisionTriangle { AX = -10, AY = 0, AZ = -10, BX = -10, BY = 0, BZ = 10, CX = 10, CY = 2, CZ = -10 });
                triangles.Add(new CollisionTriangle { AX = 10, AY = 2, AZ = 10, BX = 10, BY = 2, BZ = -10, CX = -10, CY = 0, CZ = 10 });
                initial.PY += 1;
                for (int z = 118; z <= 138; z++)
                for (int x = 118; x <= 138; x++) arena.Heightmap.Data[z * 256 + x] = (x - 118) * .1f;
            }
            else if (fixture == "step")
            {
                triangles.AddRange(ReportFloor(.2f, 1, 20, -20, 20));
                initial.PX = .8f;
                for (int z = 108; z <= 148; z++)
                for (int x = 129; x <= 148; x++) arena.Heightmap.Data[z * 256 + x] = .2f;
            }
            else
            {
                arena = CombatFixtureArena(2);
                triangles = arena.CollisionTriangles.ToList();
                initial.PX = 11.95f;
                initial.PY += 2;
            }
            arena.CollisionTriangles = triangles.ToArray();
            arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
            var sim = new ServerSimulation(arena, downActionTuning: tuning);
            sim.RegisterEntity(1, def, initial, entry.BakedAnimation);
            bool leftSupport = false;
            CharacterState final = initial;
            for (int tick = 0; tick < 90; tick++)
            {
                var input = new InputState { Down = true, DownPressed = tick == 0 };
                sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
                final = sim.GetState(1);
                leftSupport |= !final.IsGrounded;
                WriteSlideTrace(traces, $"{entry.Identity.PackageId}/geometry/{fixture}", tick, input, final, sim, 1, tick == 0 ? "slide-edge" : "");
                if (!final.IsGrounded && (final.State is ActionState.Sliding or ActionState.Crouching || final.CrouchSettled || final.IsFastFalling))
                    throw new InvalidDataException($"{entry.Identity.PackageId}/{fixture}: stale low posture or unrequested fast-fall after walk-off");
                if (fixture == "wall-tangent" && final.PX > 2 - def.CapsuleRadius + .03f)
                    throw new InvalidDataException($"{entry.Identity.PackageId}: wall penetration");
            }
            if (fixture == "walk-off" && !leftSupport) throw new InvalidDataException("Walk-off fixture never left support");
            rows.Add(new SlideFollowupRow
            {
                Character = entry.Identity.PackageId, FallProfile = fallProfile, SlideDeceleration = tuning.SlideDecelerationRatio,
                Scenario = "geometry", Route = fixture, Status = "complete", Tick = 89,
                RecoveryEndX = final.PX, RecoveryEndZ = final.PZ, RecoveryEndVX = final.VX, RecoveryEndVZ = final.VZ,
                JumpsLeft = final.JumpsLeft, AirDodgesLeft = final.AirDodgesLeft,
                Notes = $"real triangles and matching heightmap; leftSupport={leftSupport}; finalPY={F(final.PY)}",
            });
        }
    }

    private static SlideTargetRow RunCombatRoute(
        MatchContentEntry attackerEntry, MatchContentEntry targetEntry, string airNormal,
        SlotAddress? airSlot, string targetMode, float distance, float support, bool edge,
        DownActionTuning tuning, string fallProfile, StreamWriter traces, ArenaDefinition arena, bool delayed, bool noAerial)
    {
        const ulong attackerId = 1, targetId = 100;
        var attackerDef = SlideDefinition(attackerEntry, fallProfile);
        var targetDef = SlideDefinition(targetEntry, fallProfile);
        var sim = new ServerSimulation(arena, downActionTuning: tuning);
        float targetZ = edge ? 11f : distance;
        var attacker = InitialState(attackerDef, 0, targetZ - distance, true, 0, vz: attackerDef.Movement.RunSpeed);
        var target = InitialState(targetDef, 0, targetZ, true, MathF.PI);
        attacker.PY += support;
        target.PY += support;
        attacker.State = ActionState.Run;
        sim.RegisterEntity(attackerId, attackerDef, attacker, attackerEntry.BakedAnimation);
        sim.RegisterEntity(targetId, targetDef, target, targetEntry.BakedAnimation);
        string scenario = $"combat/{attackerEntry.Identity.PackageId}/{airNormal}/{targetMode}/{distance}/{support}/{(edge ? "edge" : "center")}";
        int? takeoff = null, landing = null, slideStart = null, accepted = null, active = null, recovery = null, firstHit = null;
        bool aerialAccepted = false, downEdge = false, aerialRequested = false, normalRequested = false;
        ulong activation = 0;
        float totalDamage = 0, incomingDamage = 0;
        int incomingHits = 0;
        var contactTicks = new List<string>();
        var hitDetails = new List<string>();
        var incomingDetails = new List<string>();
        var removed = new List<Hitbox>();
        sim.Resolver.OnHitboxRemoved = (hitbox, _, _, _) => removed.Add(hitbox);
        var lastAttacker = attacker;
        var lastTarget = target;
        for (int tick = 0; tick < 600; tick++)
        {
            var before = sim.GetState(attackerId);
            var input = new InputState
            {
                MoveY = landing.HasValue ? 0 : 1,
                Jump = tick == 0,
                JumpHeld = true,
                Down = downEdge,
            };
            if (!before.IsGrounded && before.VY < 0 && !downEdge)
            {
                input.Down = input.DownPressed = true;
                downEdge = true;
            }
            bool requestAir = !noAerial && !before.IsGrounded && !landing.HasValue
                && takeoff.HasValue && !aerialRequested && tick >= takeoff.Value + (delayed ? 20 : 4);
            if (requestAir)
            {
                input.ActiveSlot = WireSlot(airSlot!.Value.InputLabel);
                aerialRequested = true;
            }
            bool requestNormal = !normalRequested && landing.HasValue && before.IsGrounded
                && (before.State is ActionState.Sliding or ActionState.Crouching)
                && before.AnimLockTicks == 0 && before.LandingLagTicks == 0
                && before.HitstopTicks == 0 && before.HitstunTicks == 0
                && sim.GetActiveAbility(attackerId) == null
                && tick >= landing.Value + (delayed ? 7 : 1);
            if (requestNormal)
            {
                input.ActiveSlot = WireSlot("1");
                normalRequested = true;
            }
            var response = new InputState { FacingYaw = 18000, AimYaw = 18000 };
            if (targetMode == "retreat") response.MoveY = 1;
            if (targetMode == "ground1-poke" && tick == 0) response.ActiveSlot = WireSlot("1");
            removed.Clear();
            sim.Tick(new Dictionary<ulong, InputState> { [attackerId] = input, [targetId] = response });
            var after = sim.GetState(attackerId);
            lastTarget = sim.GetState(targetId);
            WriteSlideTrace(traces, scenario, tick, input, after, sim, attackerId,
                requestNormal ? "ground.1-followup" : requestAir ? airNormal : input.DownPressed ? "fast-fall-edge" : input.Jump ? "full-hop" : "");
            WriteSlideTrace(traces, scenario + "/target", tick, response, lastTarget, sim, targetId,
                response.ActiveSlot != 0 ? "ground.1-poke" : targetMode);
            if (!takeoff.HasValue && before.IsGrounded && !after.IsGrounded) takeoff = tick;
            if (!landing.HasValue && sim.LastTickTouchdowns.Contains(attackerId)) landing = tick;
            if (!slideStart.HasValue && after.State == ActionState.Sliding) slideStart = tick;
            if (aerialRequested && !normalRequested && sim.GetActiveAbility(attackerId) != null
                && after.AttackSlot == WireSlot(airSlot!.Value.InputLabel)) aerialAccepted = true;
            if (normalRequested && !accepted.HasValue && sim.LastTickAcceptedActions.Contains(attackerId)
                && after.AttackSlot == WireSlot("1"))
            {
                accepted = tick;
                activation = sim.GetLastActivationId(attackerId);
            }
            if (accepted.HasValue && !active.HasValue
                && sim.Resolver.GetActiveHitboxes().Concat(removed).Any(x => x.OwnerId == attackerId && x.ActivationId == activation))
                active = tick;
            if (accepted.HasValue && tick > accepted && sim.GetActiveAbility(attackerId) == null && after.AttackSlot == 0)
                recovery ??= tick;
            foreach (var hit in sim.LastTickHits)
            {
                if (hit.OwnerEntityId == targetId && hit.TargetEntityId == attackerId && hit.Damage > 0)
                {
                    incomingHits++;
                    incomingDamage += hit.Damage;
                    incomingDetails.Add($"tick={tick};slot={hit.AttackSlot};activation={hit.ActivationId};damage={F(hit.Damage)};force={F(hit.ImpactForce)};hitstop={hit.HitstopTicks}");
                }
                if (hit.OwnerEntityId != attackerId || hit.TargetEntityId != targetId || hit.Damage <= 0) continue;
                firstHit ??= tick;
                totalDamage += hit.Damage;
                contactTicks.Add($"{tick}:{F(hit.Damage)}@{F(hit.HitX)}/{F(hit.HitY)}/{F(hit.HitZ)}");
                hitDetails.Add($"slot={hit.AttackSlot};activation={hit.ActivationId};damage={F(hit.Damage)};force={F(hit.ImpactForce)};hitstop={hit.HitstopTicks}");
            }
            lastAttacker = after;
            if (recovery.HasValue && tick >= recovery + 8) break;
            if (landing.HasValue && tick > landing + 120 && !accepted.HasValue) break;
        }
        return new SlideTargetRow
        {
            Character = attackerEntry.Identity.PackageId, FallProfile = fallProfile, SlideDeceleration = tuning.SlideDecelerationRatio,
            AirNormal = airNormal, TargetCharacter = targetEntry.Identity.PackageId, TargetMode = targetMode,
            Distance = distance, SupportHeight = support, Placement = edge ? "near-edge" : "center",
            DeclaredTargetX = 0, DeclaredTargetZ = targetZ, ActualTargetX = lastTarget.PX, ActualTargetZ = lastTarget.PZ,
            Status = recovery.HasValue ? "complete" : !landing.HasValue ? "timeout-nonlanding" : "blocked-followup",
            TakeoffTick = takeoff, LandingTick = landing, SlideStartTick = slideStart,
            AcceptedFollowupTick = accepted, FirstActiveTick = active, RecoveryEndTick = recovery, FirstHitTick = firstHit,
            AerialAccepted = aerialAccepted, GroundNormalAccepted = accepted.HasValue,
            HitCount = hitDetails.Count, TotalDamage = totalDamage, IncomingHitCount = incomingHits, IncomingDamage = incomingDamage,
            IncomingHitDetails = string.Join('|', incomingDetails),
            EndpointDistance = MathF.Sqrt(lastAttacker.PX * lastAttacker.PX + (lastAttacker.PZ - targetZ) * (lastAttacker.PZ - targetZ)),
            Overshoot = lastAttacker.PZ - targetZ, ContactTimings = string.Join('|', contactTicks), HitDetails = string.Join('|', hitDetails),
            JumpsLeft = lastAttacker.JumpsLeft, AirDodgesLeft = lastAttacker.AirDodgesLeft,
            Cooldown = lastAttacker.GetCooldown(WireSlot("1")), AnimLockTicks = lastAttacker.AnimLockTicks,
            LandingLagTicks = lastAttacker.LandingLagTicks,
            Notes = "declared running start and target before simulation; positive overshoot is past target along +Z; response damage is real resolver output",
        };
    }

    private static InputState SlideInput(float x, float z, bool down, bool downPressed, bool jump, bool jumpHeld, byte activeSlot = 0)
        => new() { MoveX = x, MoveY = z, Down = down, DownPressed = downPressed, Jump = jump, JumpHeld = jumpHeld, ActiveSlot = activeSlot };

    private static string SlideFollowupCsv(IEnumerable<SlideFollowupRow> rows)
    {
        var sb = new System.Text.StringBuilder("character,fallProfile,slideDeceleration,scenario,route,cycle,status,tick,takeoffTick,firstActiveTick,recoveryEndTick,acceptedFollowupTick,takeoffX,takeoffZ,takeoffVX,takeoffVZ,firstActiveX,firstActiveZ,firstActiveVX,firstActiveVZ,recoveryEndX,recoveryEndZ,recoveryEndVX,recoveryEndVZ,jumpsLeft,airDodgesLeft,cooldown,animLockTicks,landingLagTicks,attackElapsedTicks,carry,settled,notes,slideStartTick\n");
        foreach (var x in rows)
            sb.AppendLine(string.Join(',', Fields(x.Character, x.FallProfile, F(x.SlideDeceleration), x.Scenario, x.Route, x.Cycle, x.Status, x.Tick,
                x.TakeoffTick, x.FirstActiveTick, x.RecoveryEndTick, x.AcceptedFollowupTick,
                F(x.TakeoffX), F(x.TakeoffZ), F(x.TakeoffVX), F(x.TakeoffVZ), F(x.FirstActiveX), F(x.FirstActiveZ), F(x.FirstActiveVX), F(x.FirstActiveVZ),
                F(x.RecoveryEndX), F(x.RecoveryEndZ), F(x.RecoveryEndVX), F(x.RecoveryEndVZ), x.JumpsLeft, x.AirDodgesLeft, x.Cooldown,
                x.AnimLockTicks, x.LandingLagTicks, x.AttackElapsedTicks, x.Carry, x.Settled, x.Notes, x.SlideStartTick)));
        return sb.ToString();
    }

    private static string SlideTargetCsv(IEnumerable<SlideTargetRow> rows)
    {
        var sb = new System.Text.StringBuilder("character,fallProfile,slideDeceleration,airNormal,targetCharacter,targetMode,distance,supportHeight,placement,declaredTargetX,declaredTargetZ,actualTargetX,actualTargetZ,status,takeoffTick,landingTick,slideStartTick,acceptedFollowupTick,firstActiveTick,recoveryEndTick,firstHitTick,aerialAccepted,groundNormalAccepted,hitCount,totalDamage,endpointDistance,overshoot,contactTimings,hitDetails,jumpsLeft,airDodgesLeft,cooldown,animLockTicks,landingLagTicks,notes,incomingHitCount,incomingDamage,incomingHitDetails\n");
        foreach (var x in rows)
            sb.AppendLine(string.Join(',', Fields(x.Character, x.FallProfile, F(x.SlideDeceleration), x.AirNormal, x.TargetCharacter, x.TargetMode,
                F(x.Distance), F(x.SupportHeight), x.Placement, F(x.DeclaredTargetX), F(x.DeclaredTargetZ), F(x.ActualTargetX), F(x.ActualTargetZ), x.Status,
                x.TakeoffTick, x.LandingTick, x.SlideStartTick, x.AcceptedFollowupTick, x.FirstActiveTick, x.RecoveryEndTick, x.FirstHitTick,
                x.AerialAccepted, x.GroundNormalAccepted, x.HitCount, F(x.TotalDamage), F(x.EndpointDistance), F(x.Overshoot), x.ContactTimings, x.HitDetails,
                x.JumpsLeft, x.AirDodgesLeft, x.Cooldown, x.AnimLockTicks, x.LandingLagTicks, x.Notes, x.IncomingHitCount, F(x.IncomingDamage), x.IncomingHitDetails)));
        return sb.ToString();
    }

    private static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string F(float? value) => value.HasValue ? F(value.Value) : "";
}
