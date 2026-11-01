using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public sealed class NyxPlayNotesTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealFormJumpAndAirJumpHaveLowerApexes(bool saved)
    {
        var content = BuiltInContentResolver.Resolve(CharacterClass.Nyx);
        var def = saved ? content.Definition : TestHelpers.ResolveSourceDef("nyx");
        var sim = FormsFixture.Sim(def, dummy: false);
        sim.RegisterEntity(1, def, sim.GetState(1), content.BakedAnimation);
        float floor = sim.GetState(1).PY;
        FormsFixture.Step(sim, new InputState { Jump = true, JumpHeld = true });
        float apex = floor;
        for (int i = 0; i < 80; i++)
        {
            FormsFixture.Step(sim, new InputState { JumpHeld = true });
            apex = Math.Max(apex, sim.GetState(1).PY);
            if (!sim.GetState(1).IsGrounded && sim.GetState(1).VY <= 0) break;
        }
        float start = sim.GetState(1).PY;
        FormsFixture.Step(sim, new InputState { Jump = true, JumpHeld = true });
        float airApex = start;
        for (int i = 0; i < 60; i++)
        {
            FormsFixture.Step(sim, new InputState { JumpHeld = true });
            airApex = Math.Max(airApex, sim.GetState(1).PY);
            if (sim.GetState(1).VY <= 0) break;
        }
        output.WriteLine($"Nyx saved={saved} jump rise {apex-floor:F4} m; air-jump rise {airApex-start:F4} m");
        Assert.InRange(apex - floor, 2.05f, 2.3f);
        Assert.InRange(airApex - start, 1.3f, 1.5f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RotorFollowsTheRisingFighterAndHitsAboveHer(bool saved)
    {
        var content = BuiltInContentResolver.Resolve(CharacterClass.Nyx);
        var def = saved ? content.Definition : TestHelpers.ResolveSourceDef("nyx");
        var sim = FormsFixture.Sim(def, 1);
        sim.RegisterEntity(1, def, sim.GetState(1), content.BakedAnimation);
        var victim = sim.GetState(100);
        victim.PZ = FormsFixture.Z; victim.PY += 4f; victim.IsGrounded = false;
        sim.SetState(100, victim);
        FormsFixture.Step(sim, new InputState { ActiveSlot = AbilitySlots.E });
        var damage = new System.Collections.Generic.List<float>();
        int tracked = 0;
        float firstY = 0, lastY = 0;
        for (int i = 0; i < 110; i++)
        {
            FormsFixture.Step(sim);
            foreach (var h in sim.LastTickHits.Where(h => h.OwnerEntityId == 1))
            {
                damage.Add(h.Damage);
                output.WriteLine($"Rotor hit at tick {sim.CurrentTick}, damage {h.Damage}, owner Y {sim.GetState(1).PY:F3}");
            }
            foreach (var h in sim.Resolver.GetActiveHitboxes().Where(h => h.OwnerId == 1))
            {
                Assert.True(h.TracksBone);
                var owner = sim.GetState(1);
                var move = def.ForForm(1).GetCookedSlotAbility(AbilitySlots.E, false);
                HitboxGeometry.ResolvePositions(owner, h.SourceEvent, content.BakedAnimation, def.ForForm(1), move, 0,
                    out float x, out float y, out float z, out _, out _, out _);
                Assert.InRange(MathF.Abs(h.Y - y), 0, .001f);
                if (tracked++ == 0) firstY = h.Y;
                lastY = h.Y;
            }
        }
        output.WriteLine($"Tracked rotor samples {tracked}, height {firstY:F3} to {lastY:F3}; contacts {damage.Count}");
        Assert.True(tracked > 15);
        Assert.True(lastY > firstY + 2f);
        Assert.Equal(new[] { 2f, 2f, 2f, 4f }, damage);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RotorEndsOnHitOrFinishesAfterLanding(bool landing, bool saved)
    {
        var content = BuiltInContentResolver.Resolve(CharacterClass.Nyx);
        var def = saved ? content.Definition : TestHelpers.ResolveSourceDef("nyx");
        var sim = FormsFixture.Sim(def, 1, dummy: false);
        sim.RegisterEntity(1, def, sim.GetState(1), content.BakedAnimation);
        FormsFixture.Step(sim, new InputState { ActiveSlot = AbilitySlots.E });
        while (sim.GetState(1).AttackElapsedTicks < 20) FormsFixture.Step(sim);
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
        var state = sim.GetState(1);
        if (landing)
        {
            state.PY = TestHelpers.GroundPY(content.Definition) + .01f;
            state.VY = -5f;
        }
        else
        {
            state.State = ActionState.Hitstun;
            state.HitstunTicks = 30;
        }
        sim.SetState(1, state);
        FormsFixture.Step(sim);
        if (landing)
        {
            Assert.True(sim.GetState(1).IsGrounded);
            Assert.NotNull(sim.GetActiveAbility(1));
            for (int i = 0; i < 70 && sim.GetActiveAbility(1) != null; i++) FormsFixture.Step(sim);
        }
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        output.WriteLine($"Rotor saved={saved} ended after {(landing ? "landing and timeline completion" : "hitstun")}, state {sim.GetState(1).State}");
    }
}
