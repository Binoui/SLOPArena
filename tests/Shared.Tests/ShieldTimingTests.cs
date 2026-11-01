using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class ShieldTimingTests
{
    [Theory]
    [InlineData(20, 0, 1f, false)]
    [InlineData(2, 2, 8f, true)]
    public void FirstActionableTickDistinguishesCloseUnsafeAndLateAerialSafe(
        ushort attackerRecovery, ushort landingLag, float incomingDamage, bool attackerFirst)
    {
        var def = TestHelpers.EngineDef;
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var attacker = TestHelpers.PlayerState(x: 10f) with
        {
            PY = TestHelpers.GroundPY(def),
            State = ActionState.Attacking,
            AttackSlot = AbilitySlots.Normal1,
            StateTicks = attackerRecovery,
            AnimLockTicks = attackerRecovery,
            LandingLagTicks = landingLag,
        };
        var defender = TestHelpers.PlayerState(x: 11f) with
        {
            EntityId = 100,
            PY = TestHelpers.GroundPY(def),
            State = ActionState.Shielding,
        };
        sim.RegisterEntity(1, def, attacker);
        sim.RegisterEntity(100, def, defender);
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 1, ActivationId = 1, AttackSlot = AbilitySlots.Normal1,
            ActivationAirborne = landingLag > 0, FreezesOwner = true,
            X = defender.PX, Y = defender.PY, Z = defender.PZ,
            Radius = 0.45f, DurationTicks = 1, Damage = incomingDamage,
        });
        sim.Tick(new Dictionary<ulong, InputState> { [100] = new InputState { ShieldHeld = true } });
        Assert.True(Assert.Single(sim.LastTickHits).Blocked);

        int firstAttacker = -1, firstDefender = -1;
        for (int tick = 1; tick < 70 && (firstAttacker < 0 || firstDefender < 0); tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = new InputState { Jump = firstAttacker < 0, JumpHeld = true },
                [100] = new InputState { Jump = firstDefender < 0, JumpHeld = true, ShieldHeld = true },
            });
            if (firstAttacker < 0 && sim.GetState(1).State == ActionState.JumpSquat)
                firstAttacker = tick;
            if (firstDefender < 0 && sim.GetState(100).State == ActionState.JumpSquat)
                firstDefender = tick;
        }
        Assert.True(firstAttacker > 0 && firstDefender > 0,
            $"no actionable tick: attacker={firstAttacker}, defender={firstDefender}");
        Assert.Equal(attackerFirst, firstAttacker < firstDefender);
    }
}
