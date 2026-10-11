using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public class CombatTuningTests
{
    [Fact]
    public void InterleavedMatchesRetainTheirOwnHitAndHitstopResults()
    {
        var baseline = ContactSimulation(TuningProfiles.Get("base"));
        var candidate = ContactSimulation(TuningProfiles.Get("floor30"));
        var baselineAlone = ContactSimulation(baseline.CombatTuning);
        var candidateAlone = ContactSimulation(candidate.CombatTuning);
        var expectedBaseline = TraceHit(baselineAlone);
        var expectedCandidate = TraceHit(candidateAlone);
        SpawnContact(baseline);
        SpawnContact(candidate);

        for (int tick = 0; tick < expectedBaseline.Length; tick++)
        {
            candidate.Tick(new());
            baseline.Tick(new());
            Assert.Equal(expectedBaseline[tick], baseline.GetState(100));
            Assert.Equal(expectedCandidate[tick], candidate.GetState(100));
            if (tick == 0)
            {
                TestHelpers.AssertNear(3.468f, Assert.Single(baseline.LastTickHits).ImpactForce);
                TestHelpers.AssertNear(2.856f, Assert.Single(candidate.LastTickHits).ImpactForce);
                Assert.True(baseline.GetState(100).HitstopTicks > 0);
            }
        }
        Assert.NotEqual(expectedBaseline, expectedCandidate);
        Assert.Same(CombatTuning.Default, new ServerSimulation(TestHelpers.TestArena()).CombatTuning);
    }

    [Theory]
    [InlineData("base", 9, 9)]
    [InlineData("floor30", 25, 10)]
    public void ExplicitLaunchesUseProfileAndFixedLaunchesOmitVelocityScaleAndBonus(
        string profile, ushort scaledStun, ushort fixedStun)
    {
        var tuning = TuningProfiles.Get(profile);
        var state = TestHelpers.PlayerState();
        state.DamagePercent = 4;
        Simulation.ApplyKnockback(ref state, 0, 1, 0, 20, 0, 4, 1, 100, combatTuning: tuning);
        Assert.Equal(scaledStun, state.HitstunTicks);
        TestHelpers.AssertNear(20.4f * tuning.KnockbackScale, state.KVZ);

        Simulation.ApplyKnockback(ref state, 0, 1, 0, 20, 0, 4, 1, 100,
            applyScale: false, combatTuning: tuning);
        Assert.Equal(fixedStun, state.HitstunTicks);
        TestHelpers.AssertNear(20.4f, state.KVZ);

        Simulation.ApplyKnockbackForce(ref state, 0, 1, 0, 20, 1, tuning);
        Assert.Equal(fixedStun, state.HitstunTicks);
        TestHelpers.AssertNear(20f, state.KVZ);
    }

    [Theory]
    [InlineData("base", 18)]
    [InlineData("floor30", 20)]
    public void HitstopQueuedForceHookUsesOwningMatchCoefficient(string profile, ushort expectedStun)
    {
        var sim = ContactSimulation(TuningProfiles.Get(profile));
        sim.ActivateAbility(1, new FixedForceAbility(), 2, sim.GetDefinition(1));
        SpawnContact(sim);
        sim.Tick(new());
        TestHelpers.AssertNear(40f, Assert.Single(sim.LastTickHits).ImpactForce);
        Assert.True(sim.GetState(100).QueuedKBResolvedForce);
        while (sim.GetState(100).HitstopTicks > 0)
            sim.Tick(new());

        Assert.Equal(expectedStun, sim.GetState(100).HitstunTicks);
        TestHelpers.AssertNear(40f, sim.GetState(100).KVZ);
    }

    [Theory]
    [InlineData("base")]
    [InlineData("floor30")]
    public void ThrowReleaseUsesOwningMatchTuning(string profile)
    {
        var tuning = TuningProfiles.Get(profile);
        var sim = ContactSimulation(tuning, targetZ: 0.8f);
        sim.Tick(new() { [1] = new InputState { GrabPressed = true } });
        for (int tick = 0; tick < DefenseConfig.GrabStartupTicks; tick++)
            sim.Tick(new());
        Assert.Equal(ActionState.Grabbed, sim.GetState(100).State);
        for (int tick = 0; tick < DefenseConfig.ThrowReleaseTicks; tick++)
            sim.Tick(new());

        var thrown = sim.GetState(100);
        Assert.Equal(ActionState.Hitstun, thrown.State);
        var expected = TestHelpers.NpcState();
        expected.DamagePercent = 6;
        Simulation.ApplyKnockback(ref expected, 0, 1, 30, 5, 26, 6, 1, 100, combatTuning: tuning);
        TestHelpers.AssertNear(expected.KVZ, thrown.KVZ);
        Assert.Equal(expected.HitstunTicks, thrown.HitstunTicks);
    }

    private static ServerSimulation ContactSimulation(CombatTuning tuning, float targetZ = 3f)
    {
        var def = TestHelpers.EngineDef;
        def.CaptureGeometry = new CookedCaptureGeometry(1.1f, 0.8f, 1.2f, 0.6f,
            new CaptureAnchor(0f, 0.6f, 0.2f), new CaptureAnchor(0f, 0.6f, 0.65f));
        var sim = new ServerSimulation(TestHelpers.TestArena(), combatTuning: tuning);
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.GroundPY(def);
        var defender = TestHelpers.NpcState(z: targetZ);
        defender.PY = attacker.PY;
        sim.RegisterEntity(1, def, attacker);
        sim.RegisterEntity(100, def, defender);
        return sim;
    }

    private static void SpawnContact(ServerSimulation sim)
    {
        var target = sim.GetState(100);
        sim.Resolver.Spawn(new Hitbox
        {
            X = target.PX, Y = target.PY, Z = target.PZ,
            Radius = 0.5f, Damage = 4, BaseKnockback = 20,
            StunTicks = 1, DurationTicks = 1, OwnerId = 1,
        });
    }

    private static CharacterState[] TraceHit(ServerSimulation sim)
    {
        SpawnContact(sim);
        var trace = new CharacterState[32];
        for (int tick = 0; tick < trace.Length; tick++)
        {
            sim.Tick(new());
            trace[tick] = sim.GetState(100);
        }
        return trace;
    }

    private sealed class FixedForceAbility : ServerAbility
    {
        public override void OnStart(ref CharacterState state, CharacterDefinition def)
            => state.State = ActionState.Attacking;
        public override void Tick(ref CharacterState state, ref InputState input, CharacterDefinition def) { }
        public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef, CharacterDefinition targetDef, ref float damage, ref float knockbackForce)
            => knockbackForce = 40;
    }
}
