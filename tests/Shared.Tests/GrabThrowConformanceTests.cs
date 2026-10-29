using System;
using System.Collections.Generic;
using Xunit;
using static SlopArena.Shared.Tests.GrabHoldTests;

namespace SlopArena.Shared.Tests;

public sealed class GrabThrowConformanceTests
{
    [Theory]
    [InlineData(false, 0, 0f, 0f)]
    [InlineData(false, 80, 1f, 0f)]
    [InlineData(true, 160, 1f, 0f)]
    [InlineData(true, 80, 0f, -1f)]
    public void ReleaseMatchesRealFormulaContactAtFreezeAndLaunch(bool fling, ushort percent, float diX, float diZ)
    {
        var tuning = new CombatTuning(0.23f, 0.61f, 7f);
        var def = TestHelpers.EngineDef with { Weight = 140 };
        var arena = TestHelpers.TestArena();
        arena.KillTop = 100f;
        var (thrown, _) = Capture(arena: arena, definition: def, tuning: tuning);
        var victim = thrown.GetState(2);
        victim.DamagePercent = percent;
        thrown.SetState(2, victim);
        Tick(thrown, new() { ActiveSlot = AbilitySlots.F, MoveY = fling ? 1f : 0f });
        for (int i = 0; i < 22; i++) Tick(thrown);
        var receipt = Assert.Single(thrown.LastTickHits);
        var ordinary = new ServerSimulation(arena, combatTuning: tuning);
        var owner = TestHelpers.PlayerState(20f, 20f) with { PY = TestHelpers.GroundPY(def) };
        ordinary.RegisterEntity(1, def, owner);
        ordinary.RegisterEntity(2, def, victim with { State = ActionState.Idle, InteractionId = 0,
            InteractionPartnerId = 0, InteractionPhase = 0, StateTicks = 0 });
        ordinary.Resolver.Spawn(new Hitbox { X = victim.PX, Y = victim.PY, Z = victim.PZ, Radius = 0.3f,
            OwnerId = 1, Damage = receipt.Damage, KnockbackAngle = receipt.KnockbackAngle,
            BaseKnockback = receipt.BaseKnockback, KnockbackGrowth = receipt.KnockbackGrowth,
            StunTicks = 1, DurationTicks = 1, FreezesOwner = true });
        Tick(ordinary);
        var contact = Assert.Single(ordinary.LastTickHits);
        Assert.Equal(receipt.HitstopTicks, contact.HitstopTicks);
        Assert.Equal(receipt.Damage, contact.Damage);
        TestHelpers.AssertNear(receipt.ImpactForce, contact.ImpactForce);
        Assert.Equal(thrown.GetState(1).HitstopTicks, ordinary.GetState(1).HitstopTicks);
        var input = new InputState { MoveX = diX, MoveY = diZ };
        float throwX = thrown.GetState(2).PX, throwZ = thrown.GetState(2).PZ;
        float hitX = ordinary.GetState(2).PX, hitZ = ordinary.GetState(2).PZ;
        for (int i = 0; i < receipt.HitstopTicks; i++) { Tick(thrown, victim: input); Tick(ordinary, victim: input); }
        var a = thrown.GetState(2);
        var b = ordinary.GetState(2);
        TestHelpers.AssertNear(a.KVX, b.KVX);
        TestHelpers.AssertNear(a.KVY, b.KVY);
        TestHelpers.AssertNear(a.KVZ, b.KVZ);
        if (!fling && diX == 0 && diZ == 0) Assert.Equal((0f, 0f), (a.KVX, a.KVZ));
        Assert.Equal(b.HitstunTicks, a.HitstunTicks);
        Assert.Equal(b.HitReactionSequence, a.HitReactionSequence);
        TestHelpers.AssertNear(a.PX - throwX, b.PX - hitX);
        TestHelpers.AssertNear(a.PZ - throwZ, b.PZ - hitZ);
        var deadA = a with { PY = 1000f }; var deadB = b with { PY = 1000f };
        thrown.SetState(2, deadA); ordinary.SetState(2, deadB);
        Tick(thrown); Tick(ordinary);
        Assert.Equal(1ul, Assert.Single(thrown.LastTickDeaths).KillerEntityId);
        Assert.Equal(1ul, Assert.Single(ordinary.LastTickDeaths).KillerEntityId);
    }
}
