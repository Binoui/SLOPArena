using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public class StickyFuseTests
{
    private static Hitbox Bomb(ushort fuse = 3) => new()
    {
        OwnerId = ServerSimulation.MayhemHostEntityId, X = 0, Y = 1, Z = 0,
        Radius = .35f, DurationTicks = 20, StickyFuseTicks = fuse,
        Explosion = new ProjectileExplosion { Radius = 2, Damage = 18, DurationTicks = 2,
            StunTicks = 1, Knockback = new() { Profile = KnockbackProfile.Medium } },
    };
    private static SpellResolver.EntityData Body(ulong id, float x = 0) => new()
    { Id = id, Active = true, PosX = x, PosY = 1, EndX = x, EndY = 1, Radius = .4f };

    [Fact]
    public void FirstContactAttachesWithoutDamageAndFollowsBodyCentreUntilFuseBlast()
    {
        var resolver = new SpellResolver(); resolver.Spawn(Bomb());
        Assert.Empty(resolver.Tick(new() { Body(1), Body(2) }));
        var bomb = Assert.Single(resolver.GetActiveHitboxes());
        Assert.Equal(1UL, bomb.AttachedEntityId); Assert.Equal(3, bomb.FuseTicksLeft);
        Assert.Empty(resolver.DrainPendingExplosions());
        resolver.UpdateStickyProjectiles(id => (true, 4f, 2f, 3f));
        bomb = Assert.Single(resolver.GetActiveHitboxes());
        Assert.Equal((4f, 2f, 3f), (bomb.X, bomb.Y, bomb.Z));
        for (int t = 0; t < 2; t++)
        { Assert.Empty(resolver.Tick(new() { Body(2) })); Assert.Empty(resolver.DrainPendingExplosions()); }
        resolver.Tick(new());
        Assert.Empty(resolver.GetActiveHitboxes());
        var explosion = Assert.Single(resolver.DrainPendingExplosions());
        Assert.Equal((4f, 2f, 3f), (explosion.x, explosion.y, explosion.z));
        Assert.Equal(ServerSimulation.MayhemHostEntityId, explosion.ownerId);
    }

    [Fact]
    public void CarrierKoOrBenchExplodesAtItsLastPositionAndCleanupRemovesThePendingBlast()
    {
        var resolver = new SpellResolver(); resolver.Spawn(Bomb()); resolver.Tick(new() { Body(1) });
        resolver.UpdateStickyProjectiles(id => (true, 5f, 3f, 2f));
        resolver.ExplodeAttachedTo(1);
        var blast = Assert.Single(resolver.DrainPendingExplosions());
        Assert.Equal((5f, 3f, 2f), (blast.x, blast.y, blast.z)); Assert.Empty(resolver.GetActiveHitboxes());
        resolver.Spawn(Bomb()); resolver.Tick(new() { Body(1) });
        resolver.UpdateStickyProjectiles(id => (false, 0f, 0f, 0f));
        resolver.RemoveOwnerEffects(ServerSimulation.MayhemHostEntityId);
        Assert.Empty(resolver.DrainPendingExplosions());
    }

    [Fact]
    public void ShieldContactDoesNotAttachAndInvincibilityDoesNotConsumeBomb()
    {
        var resolver = new SpellResolver(); resolver.Spawn(Bomb());
        var target = Body(1); target.InvincibilityTicks = 10;
        Assert.Empty(resolver.Tick(new() { target }));
        Assert.Equal(0UL, Assert.Single(resolver.GetActiveHitboxes()).AttachedEntityId);
        target.InvincibilityTicks = 0; target.ShieldSurface = true;
        Assert.Single(resolver.Tick(new() { target }));
        Assert.Empty(resolver.GetActiveHitboxes()); Assert.Single(resolver.DrainPendingExplosions());
    }

    [Fact]
    public void UncaughtBombLandsExactlyOnItsTelegraphedPointAndTick()
    {
        var arena = MankiMayhemTests.Arena(); var resolver = new SpellResolver();
        var ring = new MayhemLandingRing(0, new(3, 0, -4), 1, MankiMayhemTuning.FlightTicks, 60);
        MankiMayhemRounds.Launch(ring, 0, arena.Arcade!.Mayhem!, resolver);
        for (int tick = 1; tick < MankiMayhemTuning.FlightTicks; tick++)
        {
            resolver.Tick(new()); resolver.CheckGroundCollision(arena);
            Assert.Empty(resolver.DrainPendingExplosions());
        }
        resolver.Tick(new()); resolver.CheckGroundCollision(arena);
        var blast = Assert.Single(resolver.DrainPendingExplosions());
        Assert.Equal(3f, blast.x, 3); Assert.Equal(-4f, blast.z, 3);
        Assert.InRange(blast.y, -.001f, MankiMayhemTuning.BombRadius + .001f);
    }

    [Fact]
    public void FuseBlastUsesOrdinaryCombatAndHitsCarrierAndNearbyFighters()
    {
        var sim = MankiMayhemTests.NewMatch(); MankiMayhemTests.Step(sim);
        for (ulong id = 1; id <= 2; id++)
        { var state = sim.GetState(id); state.PX = id == 1 ? 0 : 1.5f; state.PZ = 0; sim.SetState(id, state); }
        var bomb = Bomb(2); bomb.Y = sim.GetState(1).PY;
        sim.Resolver.Spawn(bomb); MankiMayhemTests.Step(sim);
        Assert.All(new ulong[] { 1, 2 }, id => Assert.Equal(0, sim.GetState(id).DamagePercent));
        var attached = Assert.Single(sim.Resolver.GetActiveHitboxes()); Assert.Equal(1UL, attached.AttachedEntityId);
        MankiMayhemTests.Step(sim, 3);
        Assert.All(new ulong[] { 1, 2 }, id => Assert.Equal(18, sim.GetState(id).DamagePercent));
        Assert.All(sim.LastTickHits, h => Assert.Equal(ServerSimulation.MayhemHostEntityId, h.OwnerEntityId));
    }

    [Fact]
    public void CarrierKoThroughSimulationQueuesBlastBeforeItsRespawn()
    {
        var sim = MankiMayhemTests.NewMatch(); MankiMayhemTests.Step(sim);
        var victim = sim.GetState(1); var bomb = Bomb(); bomb.X = victim.PX; bomb.Y = victim.PY; bomb.Z = victim.PZ;
        sim.Resolver.Spawn(bomb); MankiMayhemTests.Step(sim);
        MankiMayhemTests.Ko(sim, 1); MankiMayhemTests.Step(sim);
        Assert.True(sim.IsEntityInactive(1));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.AttachedEntityId == 1);
        var pending = Assert.Single(sim.Resolver.DrainPendingExplosions());
        Assert.True(pending.y < -90); Assert.Equal(victim.PX, pending.x, 3);
    }
}
