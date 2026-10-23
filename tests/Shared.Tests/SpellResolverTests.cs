using Xunit;

namespace SlopArena.Shared.Tests;

public class SpellResolverTests
{
    private static SpellResolver.EntityData MakeEntity(ulong id, float x, float y, float z, float radius = 0.5f)
    {
        return new SpellResolver.EntityData
        {
            Id = id,
            PosX = x, PosY = y, PosZ = z,
            Radius = radius,
            Shape = HitboxShape.Sphere,
            EndX = x, EndY = y, EndZ = z,
            Active = true,
        };
    }

    private static Hitbox MakeHitbox(float x, float y, float z, float radius = 0.5f, ulong ownerId = 1)
    {
        return new Hitbox
        {
            X = x, Y = y, Z = z,
            Radius = radius,
            Shape = HitboxShape.Sphere,
            EndX = x, EndY = y, EndZ = z,
            Damage = 10,
            BaseKnockback = 25f,
            KnockbackGrowth = 25f,
            KnockbackAngle = 30,
            StunTicks = 10,
            DurationTicks = 10,
            OwnerId = ownerId,
            Active = true,
            CanHitOwner = false,
        };
    }

    // ── Sphere-sphere collision ──

    [Fact]
    public void Tick_SphereTouchingEntity_ReturnsHit()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(MakeHitbox(0, 0, 0));
        var entities = new List<SpellResolver.EntityData> { MakeEntity(2, 0.6f, 0, 0) };

        var hits = resolver.Tick(entities);

        Assert.Single(hits);
        Assert.Equal(2UL, hits[0].TargetEntityId);
        Assert.Equal(1UL, hits[0].OwnerEntityId);
        Assert.Equal(10, hits[0].Damage);
        Assert.Equal(1f, hits[0].DirX, 4);
        Assert.Equal(0.5f, hits[0].HitX, 4);
        Assert.Equal(0f, hits[0].HitY, 4);
        Assert.Equal(0f, hits[0].HitZ, 4);
    }

    [Fact]
    public void Tick_SphereFarFromEntity_NoHit()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(MakeHitbox(0, 0, 0));
        var entities = new List<SpellResolver.EntityData> { MakeEntity(2, 10, 0, 0) };

        var hits = resolver.Tick(entities);

        Assert.Empty(hits);
    }

    // ── Capsule-sphere collision ──

    [Fact]
    public void Tick_CapsuleHitboxVsSphereEntity_ReturnsHit()
    {
        var resolver = new SpellResolver();
        var capsule = MakeHitbox(0, 0, 0);
        capsule.Shape = HitboxShape.Capsule;
        capsule.EndX = 2; capsule.EndY = 0; capsule.EndZ = 0;
        capsule.Radius = 0.3f;
        resolver.Spawn(capsule);

        // Entity near the capsule segment's midpoint
        var entities = new List<SpellResolver.EntityData> { MakeEntity(2, 1, 0, 0.5f, 0.5f) };

        var hits = resolver.Tick(entities);

        Assert.Single(hits);
        Assert.Equal(2UL, hits[0].TargetEntityId);
        Assert.Equal(1f, hits[0].DirZ, 4);
        Assert.Equal(1f, hits[0].HitX, 4);
        Assert.Equal(0f, hits[0].HitY, 4);
        Assert.Equal(0.3f, hits[0].HitZ, 4);
    }

    [Theory]
    [InlineData(0f, -0.95f)]
    [InlineData(120f, -2f)]
    [InlineData(240f, -2f)]
    public void SphereHitboxContactsFrontOfShieldAtFirstSurface(float speed, float startX)
    {
        var resolver = new SpellResolver();
        var projectile = MakeHitbox(startX, 0f, 0f, radius: 0.1f);
        projectile.VX = speed;
        resolver.Spawn(projectile);
        var shield = MakeEntity(100, 0f, 0f, 0f, radius: 1.05f);
        shield.ShieldSurface = true;

        var hit = Assert.Single(resolver.Tick(new List<SpellResolver.EntityData> { shield }));

        Assert.Equal(100UL, hit.TargetEntityId);
        Assert.Equal(-1.05f, hit.HitX, 3);
        Assert.Equal(0f, hit.HitY, 3);
        Assert.Equal(0f, hit.HitZ, 3);
    }

    [Fact]
    public void CapsuleHitboxContactsShieldSurfaceNotDefenderHurtbox()
    {
        var resolver = new SpellResolver();
        var capsule = MakeHitbox(-1.2f, 0f, 0f, radius: 0.1f);
        capsule.Shape = HitboxShape.Capsule;
        capsule.EndX = -0.8f;
        resolver.Spawn(capsule);
        var shield = MakeEntity(100, 0f, 0f, 0f, radius: 1.05f);
        shield.ShieldSurface = true;

        var hit = Assert.Single(resolver.Tick(new List<SpellResolver.EntityData> { shield }));

        Assert.Equal(-1.05f, hit.HitX, 3);
    }

    [Fact]
    public void ProjectileHitsCloserBodyBeforeOverlappingShieldSurface()
    {
        var resolver = new SpellResolver();
        var projectile = MakeHitbox(1.28f, 0f, 0f, radius: 0.12f);
        projectile.VX = -13.8f;
        resolver.Spawn(projectile);
        var shield = MakeEntity(100, 0f, 0f, 0f, radius: 0.95f);
        shield.ShieldSurface = true;
        var body = MakeEntity(101, 1.2f, 0f, 0f, radius: 0.3f);

        var hit = Assert.Single(resolver.Tick(new List<SpellResolver.EntityData> { shield, body }));

        Assert.Equal(101UL, hit.TargetEntityId);
    }

    [Theory]
    [InlineData(HitboxShape.Sphere, false, false)]
    [InlineData(HitboxShape.Sphere, true, false)]
    [InlineData(HitboxShape.Capsule, false, false)]
    [InlineData(HitboxShape.Capsule, true, false)]
    [InlineData(HitboxShape.Sphere, false, true)]
    [InlineData(HitboxShape.Sphere, true, true)]
    [InlineData(HitboxShape.Capsule, false, true)]
    [InlineData(HitboxShape.Capsule, true, true)]
    public void ProjectileContactsNearBeforeFarRegardlessOfEntityOrder(HitboxShape shape, bool reverse, bool multiple)
    {
        var resolver = new SpellResolver();
        var projectile = MakeHitbox(-2f, .5f, 0f, .1f);
        projectile.VX = 480f;
        projectile.HitsMultipleOpponents = multiple;
        resolver.Spawn(projectile);
        var near = MakeEntity(2, 0, .5f, 0);
        var far = MakeEntity(3, 3, .5f, 0);
        near.Shape = far.Shape = shape;
        if (shape == HitboxShape.Capsule)
        {
            near.PosY = far.PosY = 0;
            near.EndY = far.EndY = 1;
        }
        var entities = reverse
            ? new List<SpellResolver.EntityData> { far, near }
            : new List<SpellResolver.EntityData> { near, far };
        var hits = resolver.Tick(entities);
        Assert.Equal(multiple ? 2 : 1, hits.Count);
        Assert.Equal(2UL, hits[0].TargetEntityId);
        Assert.Equal(.175f, hits[0].ContactTime, 5);
        Assert.Equal(-.5f, hits[0].HitX, 5);
        Assert.Equal(1f, hits[0].DirX, 5);
        if (multiple)
        {
            Assert.Equal(3UL, hits[1].TargetEntityId);
            Assert.True(hits[0].ContactTime < hits[1].ContactTime);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectileCrossingShieldAndBodyChoosesTheShieldFirst(bool reverse)
    {
        var resolver = new SpellResolver();
        var projectile = MakeHitbox(-2, 0, 0, .1f);
        projectile.VX = 240;
        resolver.Spawn(projectile);
        var shield = MakeEntity(2, 0, 0, 0, 1);
        shield.ShieldSurface = true;
        var body = MakeEntity(2, 0, 0, 0, .5f);
        var hit = Assert.Single(resolver.Tick(reverse ? new() { body, shield } : new() { shield, body }));
        Assert.Equal(.225f, hit.ContactTime, 5);
        Assert.Equal(-1f, hit.HitX, 5);
    }

    [Theory]
    [InlineData(HitboxShape.Sphere)]
    [InlineData(HitboxShape.Capsule)]
    public void SweptImpactSuppliesExplosionAndRemovalOrigin(HitboxShape shape)
    {
        var resolver = new SpellResolver();
        var projectile = MakeHitbox(-2, .5f, 0, .1f);
        projectile.VX = 240;
        projectile.Explosion = new ProjectileExplosion { Radius = 2, Damage = 3 };
        float removedX = float.NaN;
        resolver.OnHitboxRemoved = (_, x, _, _) => removedX = x;
        resolver.Spawn(projectile);
        var target = MakeEntity(2, 0, .5f, 0);
        target.Shape = shape;
        if (shape == HitboxShape.Capsule) { target.PosY = 0; target.EndY = 1; }
        Assert.Single(resolver.Tick(new() { target }));
        var explosion = Assert.Single(resolver.DrainPendingExplosions());
        Assert.Equal(-.6f, explosion.x, 5);
        Assert.Equal(.5f, explosion.y, 5);
        Assert.Equal(-.6f, removedX, 5);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, .5f)]
    [InlineData(1f, 1.5f)]
    public void ProjectileSweepsCapsuleCylinderCapsAndDegenerateSegment(float length, float height)
    {
        var resolver = new SpellResolver();
        var projectile = MakeHitbox(-2, height, 0, .1f);
        projectile.VX = 240;
        resolver.Spawn(projectile);
        var capsule = MakeEntity(2, 0, 0, 0);
        capsule.Shape = HitboxShape.Capsule;
        capsule.EndY = length;
        var hit = Assert.Single(resolver.Tick(new() { capsule }));
        Assert.InRange(hit.ContactTime, 0f, 1f);
        Assert.True(hit.HitX < 0);
    }

    [Fact]
    public void SweptProjectileInitialOverlapPrecedesLaterContact()
    {
        var resolver = new SpellResolver();
        var projectile = MakeHitbox(0, 0, 0, .1f);
        projectile.VX = 240;
        resolver.Spawn(projectile);
        var hit = Assert.Single(resolver.Tick(new() { MakeEntity(3, 2, 0, 0), MakeEntity(2, 0, 0, 0) }));
        Assert.Equal(2UL, hit.TargetEntityId);
        Assert.Equal(0, hit.ContactTime);
    }

    [Fact]
    public void EqualTimeProjectileContactsRetainCandidatePriority()
    {
        var resolver = new SpellResolver();
        var projectile = MakeHitbox(-2, 0, 0, .1f);
        projectile.VX = 240;
        resolver.Spawn(projectile);
        var hit = Assert.Single(resolver.Tick(new() { MakeEntity(3, 0, 0, 0), MakeEntity(2, 0, 0, 0) }));
        Assert.Equal(3UL, hit.TargetEntityId);
    }

    [Fact]
    public void MovingMeleeStillUsesItsCurrentOverlap()
    {
        var resolver = new SpellResolver();
        var melee = MakeHitbox(-2, 0, 0, .1f);
        melee.VX = 240;
        melee.CancelWithAbility = true;
        resolver.Spawn(melee);
        Assert.Empty(resolver.Tick(new() { MakeEntity(2, 0, 0, 0) }));
    }

    // ── CanHitOwner ──

    [Fact]
    public void Tick_CanHitOwnerFalse_SkipsOwner()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(MakeHitbox(0, 0, 0, ownerId: 1));
        var entities = new List<SpellResolver.EntityData> { MakeEntity(1, 0.4f, 0, 0) }; // same ID as owner

        var hits = resolver.Tick(entities);

        Assert.Empty(hits);
    }

    [Fact]
    public void Tick_CanHitOwnerTrue_HitsOwner()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(0, 0, 0, ownerId: 1);
        hb.CanHitOwner = true;
        resolver.Spawn(hb);
        var entities = new List<SpellResolver.EntityData> { MakeEntity(1, 0.4f, 0, 0) };

        var hits = resolver.Tick(entities);

        Assert.Single(hits);
    }

    // ── One hit per entity per tick ──

    [Fact]
    public void Tick_TwoHitboxesSameEntity_OneHit()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(MakeHitbox(0, 0, 0));
        resolver.Spawn(MakeHitbox(0.1f, 0, 0)); // second overlapping
        var entities = new List<SpellResolver.EntityData> { MakeEntity(2, 0.5f, 0, 0) };

        var hits = resolver.Tick(entities);

        Assert.Single(hits); // only first hitbox connects
    }

    [Fact]
    public void Tick_TwoHitboxesTwoEntities_BothHit()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(MakeHitbox(0, 0, 0, radius: 5f));
        resolver.Spawn(MakeHitbox(0, 0, 1, radius: 5f));
        var entities = new List<SpellResolver.EntityData>
        {
            MakeEntity(2, 1, 0, 0),
            MakeEntity(3, 0, 0, 1),
        };

        var hits = resolver.Tick(entities);

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.TargetEntityId == 2);
        Assert.Contains(hits, h => h.TargetEntityId == 3);
    }
    [Fact]
    public void Tick_ProjectileExpiresWithExplosion_PendingExplosion()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(10, 5, 0);
        hb.DurationTicks = 30;
        hb.Explosion = new ProjectileExplosion
        {
            Radius = 3, Damage = 25, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 40f, KnockbackGrowth = 40f }, StunTicks = 15, DurationTicks = 5,
        };
        resolver.Spawn(hb);

        // Tick until expiry (no entities to collide with)
        var entities = new List<SpellResolver.EntityData>();
        for (int i = 0; i < 30; i++)
            resolver.Tick(entities);

        var explosions = resolver.DrainPendingExplosions();
        Assert.Single(explosions);
        // Position should be near the last position (pre-deactivation)
        Assert.Equal(10, explosions[0].x, 1);
        Assert.Equal(5, explosions[0].y, 1);
    }

    // ── Gravity on projectile ──

    [Fact]
    public void Tick_GravityOnProjectile_VYDecreases()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(0, 10, 0);
        hb.VY = 5f;
        hb.Gravity = 20f; // m/s²
        hb.DurationTicks = 60;
        resolver.Spawn(hb);
        var entities = new List<SpellResolver.EntityData>();

        resolver.Tick(entities);

        // After one tick (1/60s): VY = 5 - 20 * (1/60) ≈ 4.667
        float expectedVy = 5f - (20f * (1f / 60f));
        var hitboxes = resolver.GetActiveHitboxes();
        var projectile = Assert.Single(hitboxes);
        Assert.Equal(expectedVy, projectile.VY, 4);
    }

    // ── CheckGroundCollision ──

    [Fact]
    public void CheckGroundCollision_ProjectileBelowGround_QueuesExplosion()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(0, -1, 0); // below ground
        hb.Gravity = 20f;
        hb.Explosion = new ProjectileExplosion { Radius = 2, Damage = 15, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 30f, KnockbackGrowth = 30f }, StunTicks = 10, DurationTicks = 5 };
        resolver.Spawn(hb);

        var arena = TestHelpers.TestArena();
        resolver.CheckGroundCollision(arena);

        var explosions = resolver.DrainPendingExplosions();
        Assert.Single(explosions);
        Assert.Equal(0, explosions[0].y, 4); // explosion at floor Y
    }

    [Fact]
    public void CheckGroundCollision_ProjectileAboveGround_NoExplosion()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(0, 5, 0); // above ground
        hb.Gravity = 20f;
        hb.Explosion = new ProjectileExplosion
        {
            Radius = 2, Damage = 15, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 30f, KnockbackGrowth = 30f }, StunTicks = 10, DurationTicks = 5,
        };
        resolver.Spawn(hb);

        var arena = TestHelpers.TestArena();
        resolver.CheckGroundCollision(arena);

        Assert.Single(resolver.GetActiveHitboxes());
        Assert.Empty(resolver.DrainPendingExplosions());
    }

    [Fact]
    public void CheckGroundCollision_NoGravity_NoExplosion()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(0, -1, 0); // below ground
        hb.Gravity = 0;                 // zero gravity = melee hitbox, not a projectile
        hb.Explosion = new ProjectileExplosion
        {
            Radius = 2, Damage = 15, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 30f, KnockbackGrowth = 30f }, StunTicks = 10, DurationTicks = 5,
        };
        resolver.Spawn(hb);
        var arena = TestHelpers.TestArena();
        resolver.CheckGroundCollision(arena);

        Assert.Empty(resolver.DrainPendingExplosions());
    }

    // ── Owner hitstop ──

    [Fact]
    public void Tick_FrozenOwner_PausesItsMeleeWindowsButNotProjectilesOrOtherOwners()
    {
        // ADR-0012: hitstop pauses the attacker's move, so its melee windows wait with it.
        // A launched projectile and another fighter's window keep their own time.
        var resolver = new SpellResolver();
        var window = MakeHitbox(0, 0, 0); window.DurationTicks = 2; window.CancelWithAbility = true;
        var projectile = MakeHitbox(10, 0, 0); projectile.DurationTicks = 2;
        var otherWindow = MakeHitbox(20, 0, 0, ownerId: 3); otherWindow.DurationTicks = 2; otherWindow.CancelWithAbility = true;
        resolver.Spawn(window); resolver.Spawn(projectile); resolver.Spawn(otherWindow);
        var noEntities = new List<SpellResolver.EntityData>();

        for (int i = 0; i < 5; i++)
            resolver.Tick(noEntities, frozenOwners: new HashSet<ulong> { 1 });
        Assert.Equal(0, Assert.Single(resolver.GetActiveHitboxes()).AgeTicks);

        resolver.Tick(noEntities);
        Assert.Equal(1, Assert.Single(resolver.GetActiveHitboxes()).AgeTicks);
        resolver.Tick(noEntities);
        Assert.Empty(resolver.GetActiveHitboxes());
    }
}
