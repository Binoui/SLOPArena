using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;
using SlopArena.Shared.AI;

namespace SlopArena.CpuFeelBench;

internal static partial class Program
{
    private static MatchContentEntry WhiffFighter(ushort iasa)
    {
        var source = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var slots = source.Definition.CookedSlots!.ToArray();
        CanonicalSlotProjection.TryGet(AbilitySlots.Normal1, false, out var address);
        var hit = new CookedHitbox(AuthoringHitboxShape.Sphere, 0.35f, 0f, 0f, 0.8f,
            0f, 0f, 0f, null, null, 5f, 45f, 2f, 0f, 18, 2, true, 0, default);
        slots[address.Ordinal] = new CookedSlotDefinition(address.Ordinal, address.Id, false,
            "Bench whiff", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[] { new CookedStage(90, iasa, 0, 0, 0, Array.Empty<string>(),
                new CookedTimelineOperation[] { new CookedSpawnHitboxOperation(8, AuthoringUnit.Meters, hit) }) }));
        return new MatchContentEntry(source.Handle, source.LegacySelector, source.Identity, source.DisplayName,
            source.Definition with { CookedSlots = slots }, source.Baked);
    }

    private static MatchContentEntry ContactFighter(ushort hitstun)
    {
        var source = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var slots = source.Definition.CookedSlots!.ToArray();
        foreach (bool air in new[] { false, true })
        {
            CanonicalSlotProjection.TryGet(AbilitySlots.Normal1, air, out var address);
            var hit = new CookedHitbox(AuthoringHitboxShape.Sphere, .6f, 0, .5f, 1.2f,
                0, 0, 0, null, null, 3f, 45f, 0f, 0f, 0, 2, true, 0, default, hitstun);
            slots[address.Ordinal] = new CookedSlotDefinition(address.Ordinal, address.Id, air,
                "Bench confirm window", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
                new CookedTimeline(new[] { new CookedStage(18, 12, 0, 0, 0, Array.Empty<string>(),
                    new CookedTimelineOperation[] { new CookedSpawnHitboxOperation(2, AuthoringUnit.Ticks, hit) }) }));
        }
        return new MatchContentEntry(source.Handle, source.LegacySelector, source.Identity, source.DisplayName,
            source.Definition with { CookedSlots = slots }, source.Baked);
    }

    private static int ActionWait(in CharacterState target, CookedSlotDefinition? active)
    {
        if (target.HitstopTicks > 0) return target.HitstopTicks;
        if (target.LandingLagTicks > 0) return target.LandingLagTicks;
        if (target.State != ActionState.Attacking || active == null) return 0;
        int stage = Math.Min(target.ComboStage, active.Timeline.Stages.Count - 1);
        int offset = 0;
        for (int i = 0; i < stage; i++) offset += active.Timeline.Stages[i].DurationTicks;
        var current = active.Timeline.Stages[stage];
        int end = offset + (current.IasaTicks > 0 ? current.IasaTicks : current.DurationTicks);
        return Math.Max(0, Math.Min(target.AnimLockTicks, end - target.AttackElapsedTicks));
    }

    private static void CombatBench(string scenario)
    {
        Line(); Line($"## Combat plans: {scenario}");
        Line("Paired seeds 1..32 for plans/approaches, 1..8 otherwise. 900 ticks per trial; identical scripts for all tiers.");
        Line("Whiff/threat use a declared 90-tick move fixture. Pressure uses a 2-tick poke, 12-tick IASA and 42/13-tick fixed Hitstun. Gap uses the admitted Log Off rim/stump spot.");
        Line("Trace is explanatory; inputs, accepted states, resolver contacts and damage supply behavior counts. Legacy policy has trace serial 0.");
        Line("Roster identities: " + string.Join(", ", Roster().Select(c => BuiltInContentResolver.Resolve(c).Identity.ToString())));
        Line();
        Line("| Tier | Fighter | Stage/script | Trials | Actions | Hits | Blocks | True contacts | Walk ticks | Retreat ticks | Jumps | Dashes/arrivals/followups/misses | Shield edges | Openings eligible/expired | Recovery contacts/off-window attacks | Plans/median duration/replacements | Max pressure actions | Damage taken | Deaths |");
        Line("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var fighters = scenario == "tiers" ? new[] { CharacterClass.Manki, CharacterClass.FightGuy, CharacterClass.Wibou, CharacterClass.Bonk }
            : new[] { CharacterClass.FightGuy };
        var stages = scenario == "plans" ? new[] { "training", "log_off", "hamster_habitat" } : new[] { "training" };
        var scripts = scenario switch
        {
            "plans" => new[] { "idle", "lateral", "closing", "feint" },
            "whiff" => new[] { "long-whiff", "expired-whiff", "far-whiff", "landing", "idle" },
            "approaches" => new[] { "3m", "6m", "10m", "edge", "gap", "offstage" },
            "defend" => new[] { "threat", "air-threat" },
            "pressure" => new[] { "hit-block-jab", "short-confirm" },
            _ => new[] { "long-whiff", "threat", "hit-block-jab", "counter" },
        };
        if (_stage != null) stages = stages.Where(s => s == _stage).ToArray();
        if (_script != null) scripts = scripts.Where(s => s == _script).ToArray();
        foreach (var tier in new[] { CpuDifficulty.Easy, CpuDifficulty.Normal, CpuDifficulty.Hard })
        foreach (var cls in fighters)
        foreach (string stage in stages)
        foreach (string script in scripts)
        {
            string actualStage = script == "gap" ? "log_off" : stage;
            var arena = LoadArena(actualStage);
            var fighter = BuiltInContentResolver.Resolve(cls);
            var opponent = script.Contains("whiff") || script.Contains("threat") ? WhiffFighter(script == "expired-whiff" ? (ushort)18 : (ushort)70)
                : BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
            if (scenario == "pressure") fighter = ContactFighter(script == "short-confirm" ? (ushort)13 : (ushort)42);
            int seeds = scenario is "plans" or "approaches" ? 32 : 8;
            int trials = 0, actions = 0, hits = 0, blocks = 0, trueContacts = 0, walk = 0, retreat = 0,
                jumps = 0, dashes = 0, arrivals = 0, followups = 0, misses = 0, shields = 0, openings = 0,
                expired = 0, recoveryHits = 0, falseAttacks = 0, starts = 0, replacements = 0, pressureMax = 0, deaths = 0;
            float damage = 0f;
            var durations = new List<int>();
            for (int seed = _seed ?? 1; seed <= (_seed ?? seeds); seed++)
            {
                float separation = script == "3m" ? 3f : script == "10m" ? 10f : script == "far-whiff" ? 23f : 6f;
                float targetZ = script == "edge" ? arena.MaxZ - 1f : script == "far-whiff" ? 10f : 0f;
                float x = (arena.MinX + arena.MaxX) * 0.5f;
                float targetX = x;
                var selfStart = Spawn(CpuId, fighter.Definition, arena, x, targetZ - separation, 0f);
                if (script == "gap")
                {
                    x = -17.5f; targetX = -7f; targetZ = 5f;
                    selfStart = Spawn(CpuId, fighter.Definition, arena, x, -2.88f, 0f);
                }
                if (scenario == "pressure")
                    foreach (byte slot in new[] { AbilitySlots.Normal2, AbilitySlots.Normal3, AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F })
                        selfStart.SetCooldown(slot, ushort.MaxValue);
                if (script == "air-threat") { selfStart.PY += 2f; selfStart.IsGrounded = false; }
                var duel = new Duel(arena, fighter, opponent, tier, seed, selfStart,
                    Spawn(TargetId, opponent.Definition, arena, targetX, targetZ, MathF.PI));
                duel.Settle(6);
                if (duel.Self.Deaths > 0 || duel.Other.Deaths > 0) continue;
                trials++;
                uint serial = 0; int started = 0, pressureActions = 0;
                bool shielding = false, recovering = false, arrived = false;
                ActionState previous = duel.Self.State;
                for (int tick = 0; tick < 900; tick++)
                {
                    var self = duel.Self; var target = duel.Other;
                    int phase = tick % 173;
                    bool attackScript = script.Contains("whiff") || script.Contains("threat") || script is "hit-block-jab" or "counter";
                    var targetInput = new InputState
                    {
                        ActiveSlot = attackScript && phase == 23 ? AbilitySlots.Normal1 : (byte)0,
                        FaceToCamera = true, AimYaw = 18000,
                        MoveX = script == "lateral" ? (tick / 47 % 2 == 0 ? 1f : -1f) : 0f,
                        MoveY = script == "closing" ? -1f : script == "feint" && phase < 7 ? 0.2f : script == "offstage" && tick > 90 ? 1f : 0f,
                        ShieldHeld = script is "hit-block-jab" or "short-confirm" or "counter" && phase is >= 60 and < 120,
                    };
                    if (script == "landing" && phase == 23)
                        duel.Sim.SetState(TargetId, target with { LandingLagTicks = 45 });
                    int wait = ActionWait(target, duel.Sim.GetActiveMove(TargetId)?.Slot);
                    bool recovery = wait > 0 && (target.LandingLagTicks > 0 || target.AttackElapsedTicks > 10);
                    if (recovery && !recovering)
                    {
                        if (wait > BotDifficultyProfile.ForDifficulty(tier).ReactionDelayTicks + 6 && Horizontal(self, target) < 8f) openings++;
                        else expired++;
                    }
                    recovering = recovery;
                    duel.Step(targetInput, record: true);
                    var input = duel.CpuInput; var post = duel.Self; var trace = duel.Trace;
                    if (trace.Serial != serial) pressureActions = 0;
                    if (input.MoveX != 0f || input.MoveY != 0f)
                    {
                        walk++;
                        float toward = input.MoveX * (target.PX - self.PX) + input.MoveY * (target.PZ - self.PZ);
                        if (toward < -0.1f) retreat++;
                    }
                    if (input.Jump) jumps++;
                    if (input.DashPressed) dashes++;
                    if (input.ShieldHeld && !shielding) shields++;
                    shielding = input.ShieldHeld;
                    if (post.State == ActionState.HomingDashArrive && previous != post.State) { arrivals++; arrived = true; }
                    if (post.State == ActionState.HomingDashMiss && previous != post.State) misses++;
                    bool accepted = post.State == ActionState.Attacking && (self.State != ActionState.Attacking
                        || input.ActiveSlot != 0 && post.AttackElapsedTicks < self.AttackElapsedTicks);
                    if (accepted)
                    {
                        actions++;
                        if (arrived) { followups++; arrived = false; }
                        if (!recovery) falseAttacks++;
                        if (trace.Kind == CpuCombatPlanKind.Pressure) pressureActions++;
                        pressureMax = Math.Max(pressureMax, pressureActions);
                    }
                    foreach (var hit in duel.Sim.LastTickHits)
                    {
                        if (hit.OwnerEntityId != CpuId || hit.TargetEntityId != TargetId) continue;
                        if (hit.Blocked) blocks++;
                        else { hits++; if (target.HitstunTicks > 0) trueContacts++; if (recovery) recoveryHits++; }
                    }
                    if (trace.Serial != serial)
                    {
                        if (serial != 0) durations.Add(tick - started);
                        if (serial != 0 && trace.LastEnd == CpuPlanEnd.None) replacements++;
                        starts++; serial = trace.Serial; started = tick;
                    }
                    damage += Math.Max(0, post.DamagePercent - self.DamagePercent);
                    deaths += Math.Max(0, post.Deaths - self.Deaths);
                    if (_traceDeaths && post.Deaths > self.Deaths)
                        Console.Error.WriteLine($"COMBAT DEATH {tier} {actualStage}/{script} seed={seed}");
                    previous = post.State;
                    if (post.Deaths > 0 || post.DamagePercent > 80)
                        duel.ResetSelf(selfStart);
                    if (duel.Other.Deaths > 0 || duel.Other.DamagePercent > 80)
                        duel.Sim.SetState(TargetId, Spawn(TargetId, opponent.Definition, arena, targetX, targetZ, MathF.PI));
                }
            }
            Line($"| {tier} | {cls} | {actualStage}/{script} | {trials}/{(_seed.HasValue ? 1 : seeds)} | {actions} | {hits} | {blocks} | {trueContacts} | {walk} | {retreat} | {jumps} | {dashes}/{arrivals}/{followups}/{misses} | {shields} | {openings}/{expired} | {recoveryHits}/{falseAttacks} | {starts}/{Median(durations)}/{replacements} | {pressureMax} | {damage:F0} | {deaths} |");
        }
    }
}
