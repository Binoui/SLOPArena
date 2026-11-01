using System;
using System.Collections.Generic;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CpuCombatPlanTests
{
    private static CharacterDefinition Kit()
    {
        var def = TestHelpers.EngineDef;
        foreach (bool air in new[] { false, true })
            def = TestHelpers.WithMove(def, AbilitySlots.Normal1, air, "Poke", 18, 12,
                hits: new[] { new HitboxEvent { TriggerTick = 2, DurationTicks = 2, Radius = .6f,
                    OffZ = 1.2f, Damage = 3f, FixedHitstunTicks = 60 } });
        return def;
    }

    private static ArenaDefinition BroadFloor()
    {
        var arena = TestHelpers.TestArena();
        arena.MinX = arena.MinZ = -40f; arena.MaxX = arena.MaxZ = 40f;
        arena.Heightmap = new ArenaHeightmap { Width = 81, Height = 81, OriginX = -40f, OriginZ = -40f,
            CellSize = 1f, Data = new float[81 * 81] };
        return arena;
    }

    private sealed class Fight
    {
        public readonly HeuristicBotPolicy Policy = new();
        public readonly BotMemory Memory = new() { Difficulty = CpuDifficulty.Hard };
        public readonly CharacterDefinition Def = Kit();
        public readonly ArenaDefinition Arena = BroadFloor();
        public readonly ServerSimulation Sim;
        public readonly Random Rng;
        private readonly Dictionary<ulong, InputState> _inputs = new();
        public CharacterState Self => Sim.GetState(100);
        public CharacterState Target => Sim.GetState(1);
        public Fight(float gap, int seed = 3, ushort damage = 0, bool airborne = false)
        {
            Rng = new Random(seed); Sim = new ServerSimulation(Arena);
            Sim.RegisterEntity(100, Def, TestHelpers.PlayerState() with { EntityId = 100, PZ = -gap,
                PY = Arena.Heightmap.Sample(0, -gap) + Def.CapsuleHeight * .5f, DamagePercent = damage });
            Sim.RegisterEntity(1, Def, TestHelpers.NpcState() with { EntityId = 1,
                PY = Arena.Heightmap.Sample(0, 0) + Def.CapsuleHeight * .5f });
            if (airborne) Sim.SetState(100, Self with { IsGrounded = false, PY = Self.PY + 3f,
                AirTimeTicks = Def.Movement.FloatWindowTicks });
            for (int i = 0; i <= 12; i++) Policy.ObserveOpponent(Memory, Target, Def);
        }
        public InputState Step(InputState targetInput = default)
        {
            Policy.ObserveOpponent(Memory, Target, Def, activeMove: Sim.GetActiveMove(1)?.Slot);
            var input = Policy.Decide(Self, Def, Rng, Memory, Arena, activeMove: Sim.GetActiveMove(100)?.Slot);
            _inputs[100] = input; _inputs[1] = targetInput; Sim.Tick(_inputs);
            foreach (var hit in Sim.LastTickHits)
                if (hit.OwnerEntityId == 100 && hit.TargetEntityId == 1)
                    Memory.RecordOpponentContact(hit.AttackSlot, hit.Blocked ? CpuContactKind.Block : CpuContactKind.Hit, Target, hit.HitstopTicks);
            return input;
        }
    }

    [Fact]
    public void PersistentApproachSurvivesQuietTicks()
    {
        var fight = new Fight(24);
        fight.Step(); uint serial = fight.Memory.CombatTrace.Serial;
        Assert.Equal(CpuCombatPlanKind.Approach, fight.Memory.CombatTrace.Kind);
        for (int i = 0; i < 45; i++)
        {
            if (i % 10 == 0) fight.Sim.SetState(1, fight.Target with { PX = i % 20 == 0 ? .2f : -.2f, DamagePercent = (ushort)i });
            var input = fight.Step();
            Assert.Equal(serial, fight.Memory.CombatTrace.Serial);
            Assert.True(input.MoveY > 0f);
        }
        Assert.True(fight.Self.PZ > -23f);
        bool accepted = false;
        for (int tick = 0; tick < 180; tick++)
            if (fight.Step().ActiveSlot == AbilitySlots.Normal1)
            { accepted = fight.Self.State == ActionState.Attacking; break; }
        Assert.True(accepted);
    }

    [Fact]
    public void SpaceMakesAGenuineRetreatBeforeWatching()
    {
        var fight = new Fight(.7f, damage: 90);
        float start = fight.Self.PZ; int retreat = 0;
        for (int i = 0; i < 60; i++)
        {
            var input = fight.Step(new InputState { MoveY = i < 18 ? -1f : 0f });
            if (input.MoveY < -.2f) retreat++;
            if (i > 0 && fight.Memory.CombatTrace.Kind != CpuCombatPlanKind.Space) break;
        }
        Assert.True(retreat >= 18, $"retreat ticks {retreat}");
        Assert.True(fight.Self.PZ < start - .5f);
        Assert.True(fight.Target.PZ - fight.Self.PZ >= 2f);
    }

    [Fact]
    public void ARecoveryEpisodeIsRecognizedOnlyOnceIncludingAMiss()
    {
        var def = TestHelpers.WithMove(Kit(), AbilitySlots.Normal2, false, "Whiff", 100, 90,
            hits: new[] { new HitboxEvent { TriggerTick = 1, DurationTicks = 1, Radius = .2f, Damage = 1 } });
        var memory = new BotMemory { Difficulty = CpuDifficulty.Easy }; var policy = new HeuristicBotPolicy();
        var target = TestHelpers.NpcState(0, 1) with { State = ActionState.Attacking, AttackSlot = AbilitySlots.Normal2,
            AttackElapsedTicks = 5, AnimLockTicks = 95 };
        var rng = new MissRandom();
        for (int i = 0; i < 100; i++)
        {
            policy.ObserveOpponent(memory, target, def, activeMove: def.GetCookedSlotAbility(AbilitySlots.Normal2, false));
            policy.Decide(TestHelpers.PlayerState(), def, rng, memory);
        }
        Assert.Equal(1, memory.CombatTrace.RecoveryEvaluations);
    }

    [Fact]
    public void ExpiredPunishIsRejectedRatherThanRefreshedByTheSameEpisode()
    {
        var def = Kit(); var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var policy = new HeuristicBotPolicy(); var rng = new ZeroRandom();
        var target = TestHelpers.NpcState(0, 1) with { LandingLagTicks = 30 };
        for (int i = 0; i <= 12; i++) policy.ObserveOpponent(memory, target, def);
        var locked = TestHelpers.PlayerState() with { State = ActionState.ShieldDrop, StateTicks = 5 };
        policy.Decide(locked, def, rng, memory);
        Assert.Equal(CpuCombatPlanKind.Punish, memory.CombatTrace.Kind);
        for (int i = 0; i < 24; i++)
        {
            policy.ObserveOpponent(memory, target, def);
            Assert.Equal(0, policy.Decide(locked, def, rng, memory).ActiveSlot);
        }
        Assert.Equal(1, memory.CombatTrace.RecoveryEvaluations);
        Assert.NotEqual(CpuCombatPlanKind.Punish, memory.CombatTrace.Kind);
    }

    [Fact]
    public void PressureHasABoundedBudgetDespiteFurtherContacts()
    {
        var fight = new Fight(1f, seed: 1); int max = 0, count = 0; uint serial = 0; bool pressure = false;
        for (int i = 0; i < 450; i++)
        {
            var input = fight.Step(); var trace = fight.Memory.CombatTrace;
            if (trace.Serial != serial) { max = Math.Max(max, count); count = 0; serial = trace.Serial; }
            if (trace.Kind != CpuCombatPlanKind.Pressure) continue;
            pressure = true; if (input.ActiveSlot != 0) count++;
        }
        Assert.True(pressure); Assert.InRange(Math.Max(max, count), 1, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BeelineArrivalsProduceAFollowUpInTheAcceptedContext(bool airborne)
    {
        int arrivals = 0, followups = 0;
        for (int seed = 1; seed <= 32; seed++)
        {
            var fight = new Fight(7, seed, airborne: airborne);
            for (int i = 0; i < 100; i++)
            {
                bool arrival = fight.Self.State == ActionState.HomingDashArrive;
                bool grounded = fight.Self.IsGrounded;
                var input = fight.Step();
                if (!arrival) continue;
                arrivals++;
                if (input.ActiveSlot == 0) continue;
                followups++;
                Assert.Equal(ActionState.Attacking, fight.Self.State);
                Assert.Equal(!grounded, fight.Sim.GetActiveMove(100)!.Value.Slot.IsAir);
            }
        }
        Assert.True(arrivals > 0); Assert.True(followups > 0);
    }

    [Fact]
    public void ARejectedBeelinePressIsNeverReplayed()
    {
        bool exercised = false;
        for (int seed = 1; seed <= 32; seed++)
        {
            var fight = new Fight(7, seed);
            for (int tick = 0; tick < 5; tick++)
            {
                fight.Policy.ObserveOpponent(fight.Memory, fight.Target, fight.Def);
                var input = fight.Policy.Decide(fight.Self, fight.Def, fight.Rng, fight.Memory, fight.Arena);
                if (!input.DashPressed) continue;
                exercised = true;
                // Withhold the press from Shared to model rejection at the adapter boundary.
                fight.Policy.ObserveOpponent(fight.Memory, fight.Target, fight.Def);
                var rejected = fight.Policy.Decide(fight.Self, fight.Def, fight.Rng, fight.Memory, fight.Arena);
                Assert.False(rejected.DashPressed); Assert.Equal(0, rejected.ActiveSlot);
                Assert.Equal(CpuPlanEnd.Rejected, fight.Memory.CombatTrace.LastEnd);
                break;
            }
        }
        Assert.True(exercised);
    }

    [Fact]
    public void ABeelineMissKeepsTheEnginesRecoveryAndDoesNotRestart()
    {
        bool exercised = false;
        for (int seed = 1; seed <= 32 && !exercised; seed++)
        {
            var fight = new Fight(7, seed);
            for (int tick = 0; tick < 6; tick++)
            {
                if (!fight.Step().DashPressed) continue;
                fight.Sim.SetState(1, fight.Target with { PZ = 25f });
                bool miss = false;
                for (int later = 0; later < 70; later++)
                {
                    var before = fight.Self;
                    var input = fight.Step();
                    Assert.False(input.DashPressed);
                    if (before.State != ActionState.HomingDashMiss) continue;
                    miss = true;
                    Assert.Equal(0, input.ActiveSlot); Assert.False(input.Jump);
                }
                Assert.True(miss); exercised = true; break;
            }
        }
        Assert.True(exercised);
    }

    [Fact]
    public void EveryPlanRefusesForwardPursuitOfAnObservedOffstageTarget()
    {
        for (int seed = 1; seed <= 32; seed++)
        {
            var fight = new Fight(7, seed);
            var offstage = fight.Target with { PZ = 45f, IsGrounded = false };
            for (int tick = 0; tick <= 12; tick++) fight.Policy.ObserveOpponent(fight.Memory, offstage, fight.Def);
            for (int tick = 0; tick < 30; tick++)
            {
                fight.Policy.ObserveOpponent(fight.Memory, offstage, fight.Def);
                var input = fight.Policy.Decide(fight.Self, fight.Def, fight.Rng, fight.Memory, fight.Arena);
                Assert.False(input.DashPressed); Assert.False(input.Jump); Assert.True(input.MoveY <= 0f);
            }
        }
    }

    [Fact]
    public void QuietApproachContinuationConsumesNoRandomSamples()
    {
        var fight = new Fight(24);
        var rng = new CountingRandom();
        fight.Policy.Decide(fight.Self, fight.Def, rng, fight.Memory, fight.Arena);
        int count = rng.Samples;
        for (int tick = 0; tick < 20; tick++)
        {
            fight.Policy.ObserveOpponent(fight.Memory, fight.Target, fight.Def);
            fight.Policy.Decide(fight.Self, fight.Def, rng, fight.Memory, fight.Arena);
        }
        Assert.Equal(count, rng.Samples);
    }

    [Theory]
    [InlineData(-4.253f, 11.854f)]
    [InlineData(3.766f, 10.613f)]
    public void BeelineRequiresRealTakeoffMarginAtTheTrainingLedge(float x, float z)
    {
        var arena = CpuMovementTests.Stage("training"); var def = TestHelpers.FightGuyDef;
        var self = TestHelpers.PlayerState(x, z) with { PY = TestHelpers.GroundPY(def) };
        var target = TestHelpers.NpcState(0f, 0f) with { PY = TestHelpers.GroundPY(def) };
        for (int seed = 0; seed < 64; seed++)
        {
            var policy = new HeuristicBotPolicy(); var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            for (int tick = 0; tick <= 12; tick++) policy.ObserveOpponent(memory, target, def);
            var rng = new Random(seed);
            for (int tick = 0; tick < 3; tick++)
            {
                policy.ObserveOpponent(memory, target, def);
                Assert.False(policy.Decide(self, def, rng, memory, arena).DashPressed);
            }
        }
    }

    [Fact]
    public void BeelineRefusesAnArrivalPredictedTowardTheEdge()
    {
        var arena = CpuMovementTests.Stage("training"); var def = TestHelpers.FightGuyDef;
        var self = TestHelpers.PlayerState(.31f, -4.1f) with { PY = TestHelpers.GroundPY(def) };
        var target = TestHelpers.NpcState(.31f, 6.9f) with { PY = TestHelpers.GroundPY(def), VZ = 12f };
        for (int seed = 0; seed < 64; seed++)
        {
            var policy = new HeuristicBotPolicy(); var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            for (int tick = 0; tick <= 12; tick++) policy.ObserveOpponent(memory, target, def);
            var rng = new Random(seed);
            for (int tick = 0; tick < 3; tick++)
            {
                policy.ObserveOpponent(memory, target, def);
                Assert.False(policy.Decide(self, def, rng, memory, arena).DashPressed);
            }
        }
    }

    [Fact]
    public void BeelineChecksTheCommittedTravelWindowBeforeChasingARunner()
    {
        var arena = CpuMovementTests.Stage("training"); var def = TestHelpers.FightGuyDef;
        var self = TestHelpers.PlayerState(-5.920f, .125f) with { PY = TestHelpers.GroundPY(def) };
        var target = TestHelpers.NpcState(-1.75f, 1.32f) with { PY = TestHelpers.GroundPY(def), VZ = 10.4f };
        for (int seed = 0; seed < 64; seed++)
        {
            var policy = new HeuristicBotPolicy(); var memory = new BotMemory { Difficulty = CpuDifficulty.Normal };
            for (int tick = 0; tick <= 18; tick++) policy.ObserveOpponent(memory, target, def);
            var rng = new Random(seed);
            for (int tick = 0; tick < 8; tick++)
            {
                policy.ObserveOpponent(memory, target, def);
                Assert.False(policy.Decide(self, def, rng, memory, arena).DashPressed);
            }
        }
    }

    [Theory]
    [InlineData(CpuDifficulty.Easy, .8, .1, .1)]
    [InlineData(CpuDifficulty.Normal, .4, .3, .3)]
    [InlineData(CpuDifficulty.Hard, .3, .3, .4)]
    public void FirstLegalApproachUsesTheApprovedTierWeights(CpuDifficulty tier, double walk, double jump, double dash)
    {
        var arena = BroadFloor(); var def = Kit(); var policy = new HeuristicBotPolicy();
        var self = TestHelpers.PlayerState(0f, -4f) with { PY = TestHelpers.GroundPY(def) };
        var target = TestHelpers.NpcState() with { PY = TestHelpers.GroundPY(def) };
        var counts = new int[3];
        for (int seed = 0; seed < 1024; seed++)
        {
            var memory = new BotMemory { Difficulty = tier };
            for (int tick = 0; tick <= BotDifficultyProfile.ForDifficulty(tier).ReactionDelayTicks; tick++)
                policy.ObserveOpponent(memory, target, def);
            policy.Decide(self, def, new Random(seed), memory, arena);
            Assert.Equal(CpuCombatPlanKind.Approach, memory.CombatTrace.Kind);
            counts[(int)memory.CombatTrace.Approach]++;
        }
        Assert.InRange(counts[0] / 1024d, walk - .05, walk + .05);
        Assert.InRange(counts[1] / 1024d, jump - .05, jump + .05);
        Assert.InRange(counts[2] / 1024d, dash - .05, dash + .05);
    }

    private sealed class CountingRandom : Random
    {
        public int Samples;
        public override double NextDouble() { Samples++; return .5; }
        public override int Next(int maxValue) { Samples++; return 0; }
        public override int Next() { Samples++; return 0; }
    }

    private sealed class MissRandom : Random { public override double NextDouble() => .999; }
    private sealed class ZeroRandom : Random { public override double NextDouble() => 0; public override int Next(int maxValue) => 0; public override int Next() => 0; }
}
