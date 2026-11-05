using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;
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
    public void ShieldContactDoesNotAttachAndInvincibilityStopsOnlyFighterBombs()
    {
        var resolver = new SpellResolver(); var fighterBomb = Bomb(); fighterBomb.OwnerId = 7; resolver.Spawn(fighterBomb);
        var target = Body(1); target.InvincibilityTicks = 10;
        Assert.Empty(resolver.Tick(new() { target }));
        Assert.Equal(0UL, Assert.Single(resolver.GetActiveHitboxes()).AttachedEntityId);
        var host = new SpellResolver(); host.Spawn(Bomb());
        host.Tick(new() { target });
        Assert.Equal(1UL, Assert.Single(host.GetActiveHitboxes()).AttachedEntityId);
        resolver = new SpellResolver(); resolver.Spawn(Bomb());
        target.InvincibilityTicks = 0; target.ShieldSurface = true;
        Assert.Single(resolver.Tick(new() { target }));
        Assert.Empty(resolver.GetActiveHitboxes()); Assert.Single(resolver.DrainPendingExplosions());
    }

    [Fact]
    public void MayhemImpactBombLandsExactlyOnItsTelegraphedPointAndTick()
    {
        var arena = MayhemWorld.Arena(); var resolver = new SpellResolver();
        var hazards = new MayhemHazards(new List<MayhemEvent>(), ServerSimulation.MayhemHostEntityId, arena.Arcade!.Mayhem!);
        hazards.Bind(resolver, 1, 0);
        const ushort flight = 56;
        hazards.Bomb(0, 0, new ArcadePoint(3, 0, -4), flight, 2f);
        var bomb = Assert.Single(resolver.GetActiveHitboxes());
        Assert.True(bomb.ContactExplosion); Assert.Equal(0, bomb.StickyFuseTicks);
        for (int tick = 1; tick < flight; tick++)
        {
            resolver.Tick(new()); resolver.CheckGroundCollision(arena);
            Assert.Empty(resolver.DrainPendingExplosions());
        }
        resolver.Tick(new()); resolver.CheckGroundCollision(arena);
        var blast = Assert.Single(resolver.DrainPendingExplosions());
        Assert.Equal(3f, blast.x, 2); Assert.Equal(-4f, blast.z, 2);
        Assert.InRange(blast.y, -.001f, MayhemHazardTuning.BombRadius + .001f);
        Assert.True(MayhemHazards.TryDecode(blast.activationId, out int episode, out byte lane, out ushort ordinal));
        Assert.Equal((0, (byte)0, (ushort)0), (episode, lane, ordinal));
    }
}
