using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ProjectileEmissionIdentityTests
{
    [Fact]
    public void SameOperationEmissionsRemainDistinctAfterAnEarlierProjectileExpires()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(Projectile(0, 1));
        resolver.Spawn(Projectile(10));
        resolver.Spawn(Projectile(20));
        var original = resolver.GetActiveHitboxes().Select(h => h.EmissionId).ToArray();
        Assert.Equal(3, original.Distinct().Count());
        Assert.DoesNotContain(0UL, original);

        resolver.Tick(new List<SpellResolver.EntityData>());
        Assert.Equal(original.Skip(1), resolver.GetActiveHitboxes().Select(h => h.EmissionId));
        resolver.Spawn(Projectile(30));
        Assert.True(resolver.GetActiveHitboxes().Last().EmissionId > original.Max());
    }

    [Fact]
    public void RestorePreservesSurvivingIdsAndReplaysTheSameFutureEmission()
    {
        var simulation = new ServerSimulation(TestHelpers.TestArena());
        simulation.RegisterEntity(1, TestHelpers.EngineDef, TestHelpers.PlayerState());
        simulation.Resolver.Spawn(Projectile(0));
        var anchor = simulation.CaptureSnapshot();
        ulong survivor = simulation.Resolver.GetActiveHitboxes().Single().EmissionId;
        simulation.Resolver.Spawn(Projectile(10));
        var expected = simulation.CaptureSnapshot();
        var expectedIds = simulation.Resolver.GetActiveHitboxes().Select(h => h.EmissionId).ToArray();

        simulation.RestoreSnapshot(anchor);
        Assert.Equal(survivor, simulation.Resolver.GetActiveHitboxes().Single().EmissionId);
        simulation.Resolver.Spawn(Projectile(10));
        Assert.Equal(expectedIds, simulation.Resolver.GetActiveHitboxes().Select(h => h.EmissionId));
        Assert.True(expected.ContentEquals(simulation.CaptureSnapshot()));
    }

    [Fact]
    public void AnEmptyResolverRetainsItsEmissionHistoryAcrossSnapshots()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(Projectile(0, 1));
        ulong expiredId = resolver.GetActiveHitboxes().Single().EmissionId;
        resolver.Tick(new List<SpellResolver.EntityData>());
        Assert.Empty(resolver.GetActiveHitboxes());
        var writer = new SnapshotWriter();
        resolver.WriteSnapshot(writer, _ => (null, null));
        var restored = new SpellResolver();
        restored.ApplySnapshot(SpellResolver.ReadSnapshot(new SnapshotReader(writer.ToArray()), _ => (null, null)));
        restored.Spawn(Projectile(10));
        Assert.True(restored.GetActiveHitboxes().Single().EmissionId > expiredId);
    }

    [Theory]
    [InlineData(0UL, 1UL, 2UL)]
    [InlineData(1UL, 1UL, 2UL)]
    [InlineData(1UL, 3UL, 2UL)]
    public void InvalidOrDuplicateEmissionIdsAreRejected(ulong first, ulong second, ulong counter)
    {
        var writer = new SnapshotWriter();
        writer.WriteULong(counter);
        writer.WriteInt(2);
        foreach (ulong id in new[] { first, second })
        {
            var hitbox = Projectile(0);
            hitbox.EmissionId = id;
            hitbox.Active = true;
            SnapshotCodecs.Write(writer, hitbox, _ => (null, null));
        }
        writer.WriteInt(0);
        Assert.Throws<InvalidDataException>(() =>
            SpellResolver.ReadSnapshot(new SnapshotReader(writer.ToArray()), _ => (null, null)));
    }

    [Fact]
    public void ExhaustionCannotWrapOrInstallADuplicateIdentity()
    {
        var resolver = new SpellResolver();
        resolver.ApplySnapshot(new SpellResolver.SnapshotData { LastEmissionId = ulong.MaxValue });
        Assert.Throws<OverflowException>(() => resolver.Spawn(Projectile(0)));
        Assert.Empty(resolver.GetActiveHitboxes());
    }

    private static Hitbox Projectile(float x, ushort duration = 20) => new()
    {
        OwnerId = 1, ActivationId = 7, VisualOperationIndex = 3,
        X = x, Y = 20, VX = 1, Radius = 0.1f, DurationTicks = duration,
    };
}
