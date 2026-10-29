using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ZeroLaunchStunTests
{
    [Theory]
    [InlineData((ushort)18, (ushort)30, true)]
    [InlineData((ushort)0, (ushort)30, false)]
    [InlineData((ushort)18, (ushort)0, false)]
    public void FixedStunCanDazzleWithoutDamageOrLaunchButStillNeedsItsStunGate(
        ushort stunGate, ushort fixedStun, bool stuns)
    {
        var sim = FormsFixture.Sim(FormsFixture.Def());
        var target = sim.GetState(FormsFixture.Dummy);
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = FormsFixture.Nyx, X = target.PX, Y = target.PY, Z = target.PZ,
            Shape = HitboxShape.Sphere, Radius = 1, DurationTicks = 1,
            Damage = 0, BaseKnockback = 0, KnockbackGrowth = 0,
            StunTicks = stunGate, FixedHitstunTicks = fixedStun,
        });
        var inputs = new Dictionary<ulong, InputState>();
        sim.Tick(inputs);
        Assert.Single(sim.LastTickHits);
        while (sim.GetState(FormsFixture.Dummy).HitstopTicks > 0) sim.Tick(inputs);
        var after = sim.GetState(FormsFixture.Dummy);
        Assert.Equal(0, after.DamagePercent);
        Assert.Equal((0f, 0f, 0f), (after.KVX, after.KVY, after.KVZ));
        Assert.Equal(stuns, after.State == ActionState.Hitstun);
        Assert.Equal(stuns ? fixedStun : (ushort)0, after.HitstunTicks);
    }
}
