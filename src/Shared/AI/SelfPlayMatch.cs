using System;
using System.Collections.Generic;

namespace SlopArena.Shared.AI;

/// <summary>
/// Runs one deterministic bot-vs-bot match on the real <see cref="ServerSimulation"/>
/// (issue #148). Both entities are driven by <see cref="HeuristicBotPolicy"/> with the same
/// seeded RNG stream; the match terminates via a <see cref="StockMatchRule"/> winner or a
/// deterministic tick cap. Same seed + def + arena → bit-identical match.
/// </summary>
public static class SelfPlayMatch
{
    /// <summary>Safety cap (3 min at 60Hz). A match hitting this is reported as a draw, not a crash.</summary>
    public const int DefaultMaxTicks = 10800;

    /// <summary>Entity IDs for the two bots — player id 1 and the NPC id 100 (existing convention).</summary>
    public const ulong EntityA = 1;
    public const ulong EntityB = 100;

    /// <param name="def">Both bots use the same character definition.</param>
    /// <param name="arena">A bounded arena (KO-able) so the stock rule can end the match.</param>
    /// <param name="baked">Optional baked skeleton data (for BoneName hitboxes); null skips bone-attached hitboxes.</param>
    /// <param name="seed">Determinism seed — same seed reproduces the same match.</param>
    /// <param name="maxTicks">Tick cap; a match reaching it is <see cref="MatchRecord.TimedOut"/>.</param>
    /// <param name="stocks">Stocks per bot for the <see cref="StockMatchRule"/>.</param>
    /// <param name="difficulty">Shared named CPU difficulty for both bots.</param>
    /// <param name="combatTuning">Immutable combat settings for this match; null uses shipped defaults.</param>
    public static MatchRecord Run(CharacterDefinition def, ArenaDefinition arena, int seed,
        BakedAnimationData? baked = null, int maxTicks = DefaultMaxTicks, int stocks = 3,
        CpuDifficulty difficulty = CpuDifficulty.Normal, CombatTuning? combatTuning = null)
    {
        var rule = new StockMatchRule((byte)stocks);
        var sim = new ServerSimulation(arena, rule, combatTuning: combatTuning);

        float gpy = def.CapsuleHeight * 0.5f;
        RegisterBot(sim, def, EntityA, -12f, gpy, baked);
        RegisterBot(sim, def, EntityB, 12f, gpy, baked);

        var rng = new Random(seed);
        var memA = new BotMemory();
        var memB = new BotMemory();
        memA.Difficulty = BotDifficultyProfile.Normalize(difficulty);
        memB.Difficulty = BotDifficultyProfile.Normalize(difficulty);
        var policy = new HeuristicBotPolicy();
        var recorder = new MatchRecorder();
        var inputs = new Dictionary<ulong, InputState>();
        int tick = 0;
        byte deathsA = 0;
        byte deathsB = 0;

        for (; tick < maxTicks; tick++)
        {
            var sA = sim.GetState(EntityA);
            var sB = sim.GetState(EntityB);
            policy.ObserveOpponent(memA, sB, sim.GetDefinition(EntityB), baked);
            policy.ObserveOpponent(memB, sA, sim.GetDefinition(EntityA), baked);
            memA.ChooseOpponent(sA);
            memB.ChooseOpponent(sB);
            inputs[EntityA] = policy.Decide(sA, sim.GetDefinition(EntityA), rng, memA, arena, baked,
                activeMove: sim.GetActiveMove(EntityA)?.Slot, canSwapCancel: sim.CanSwapCancel(EntityA));
            inputs[EntityB] = policy.Decide(sB, sim.GetDefinition(EntityB), rng, memB, arena, baked,
                activeMove: sim.GetActiveMove(EntityB)?.Slot, canSwapCancel: sim.CanSwapCancel(EntityB));
            recorder.RecordInputs(tick, inputs);

            recorder.RecordPresses(sim, tick, inputs, def); // swings from the pre-tick presses
            sim.Tick(inputs);
            recorder.RecordTick(sim, tick, inputs, def);     // hits + positions + close swings

            var postA = sim.GetState(EntityA);
            var postB = sim.GetState(EntityB);
            bool respawnedA = postA.Deaths > deathsA;
            bool respawnedB = postB.Deaths > deathsB;
            if (respawnedA)
            {
                deathsA = postA.Deaths;
                memA.Reset();
                memA.Difficulty = BotDifficultyProfile.Normalize(difficulty);
            }
            if (respawnedB)
            {
                deathsB = postB.Deaths;
                memB.Reset();
                memB.Difficulty = BotDifficultyProfile.Normalize(difficulty);
            }

            foreach (var hit in sim.LastTickHits)
            {
                if (hit.Blocked) continue;
                if (!respawnedA && hit.OwnerEntityId == EntityA)
                    memA.RecordOpponentHit(hit.AttackSlot, !postB.IsGrounded, postB,
                        postB.HitstunTicks, hit.HitstopTicks);
                if (!respawnedB && hit.OwnerEntityId == EntityB)
                    memB.RecordOpponentHit(hit.AttackSlot, !postA.IsGrounded, postA,
                        postA.HitstunTicks, hit.HitstopTicks);
            }

            var outcome = rule.Evaluate(sim.GetAllStates());
            if (outcome.IsEnded)
                return Finalize(recorder, sim, tick + 1, seed, outcome, timedOut: false);
        }
        // Tick cap reached — draw.
        return Finalize(recorder, sim, tick, seed, default, timedOut: true);
    }

    private static void RegisterBot(ServerSimulation sim, CharacterDefinition def, ulong id, float x, float py, BakedAnimationData? baked)
    {
        var state = new CharacterState
        {
            EntityId = id,
            PX = x, PY = py, PZ = 0f,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps,
            AirDodgesLeft = 1,
            FacingYaw = id == EntityA ? 0f : MathF.PI, // A faces +Z, B faces −Z
            MatchState = MatchState.Playing,
        };
        sim.RegisterEntity(id, def, state, baked);
        sim.SetRespawnPosition(id, x, py, 0f, state.FacingYaw);
    }

    private static MatchRecord Finalize(MatchRecorder recorder, ServerSimulation sim, int tick,
        int seed, MatchOutcome outcome, bool timedOut)
    {
        var record = recorder.Finish(tick, seed, outcome);
        record.TimedOut = timedOut;
        var sA = sim.GetState(EntityA);
        var sB = sim.GetState(EntityB);
        record.Entity1Deaths = sA.Deaths;
        record.Entity2Deaths = sB.Deaths;
        record.Entity1Damage = sA.DamagePercent;
        record.Entity2Damage = sB.DamagePercent;
        return record;
    }
}
