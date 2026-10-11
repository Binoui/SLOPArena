using System;
using System.Buffers.Binary;
using System.Linq;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ProjectileVisualPacketTests
{
    [Fact]
    public void RoundTripKeepsThreeSameOriginProjectilesDistinctAndAllowsEmptyDespawn()
    {
        var entries = new[]
        {
            new ProjectileVisualState(1, 17, 2, CharacterClass.Wibou, AbilitySlots.A, false, 0, 1, 2, 0, 0, 20),
            new ProjectileVisualState(1, 17, 3, CharacterClass.Wibou, AbilitySlots.A, false, 0, 1, 2, -5, 0, 19, .75f),
            new ProjectileVisualState(1, 17, 4, CharacterClass.Wibou, AbilitySlots.A, false, 0, 1, 2, 5, 0, 19, 1f),
        };
        var packet = new ProjectileVisualPacket(42, entries);
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);
        Assert.True(ProjectileVisualPacket.TryDeserialize(bytes, out var decoded));
        Assert.Equal((uint)42, decoded.Tick);
        Assert.Equal(entries, decoded.Projectiles);

        var despawn = new ProjectileVisualPacket(43, Array.Empty<ProjectileVisualState>());
        var emptyBytes = new byte[despawn.WireSize];
        despawn.Serialize(emptyBytes);
        Assert.True(ProjectileVisualPacket.TryDeserialize(emptyBytes, out var empty));
        Assert.Empty(empty.Projectiles);
    }

    [Fact]
    public void RejectsTruncationWrongCountVersionAndNonFiniteCoordinates()
    {
        var packet = new ProjectileVisualPacket(7, new[]
        {
            new ProjectileVisualState(1, 2, 3, CharacterClass.Wibou, AbilitySlots.A, true, 0, 1, 2, 0, 0, 20),
        });
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);
        Assert.False(ProjectileVisualPacket.TryDeserialize(bytes.AsSpan(0, bytes.Length - 1), out _));
        Assert.False(ProjectileVisualPacket.TryDeserialize(new byte[bytes.Length + 1], out _));
        var wrongCount = (byte[])bytes.Clone();
        wrongCount[9] = 2;
        Assert.False(ProjectileVisualPacket.TryDeserialize(wrongCount, out _));
        var oldVersion = (byte[])bytes.Clone();
        oldVersion[4] = 0;
        Assert.False(ProjectileVisualPacket.TryDeserialize(oldVersion, out _));
        var nonfinite = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(nonfinite.AsSpan(ProjectileVisualPacket.HeaderSize + 23), unchecked((int)0x7FC00000));
        Assert.False(ProjectileVisualPacket.TryDeserialize(nonfinite, out _));
        var badRadius = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(badRadius.AsSpan(ProjectileVisualPacket.HeaderSize + 47),
            BitConverter.SingleToInt32Bits(-1f));
        Assert.False(ProjectileVisualPacket.TryDeserialize(badRadius, out _));
        BinaryPrimitives.WriteInt32LittleEndian(badRadius.AsSpan(ProjectileVisualPacket.HeaderSize + 47),
            unchecked((int)0x7FC00000));
        Assert.False(ProjectileVisualPacket.TryDeserialize(badRadius, out _));
    }
    [Fact]
    public void WibouQProducesThreeIndividuallyAddressableProjectileVisuals()
    {
        var def = TestHelpers.WibouDef;
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        sim.Tick(new() { { 1, new InputState { ActiveSlot = AbilitySlots.A, IsAiming = true } } });
        TestHelpers.TickN(sim, default, 8);

        var projectiles = sim.Resolver.GetActiveHitboxes();
        Assert.Equal(3, projectiles.Count);
        Assert.Equal(3, projectiles.Select(hitbox => hitbox.VisualOperationIndex).Distinct().Count());
        Assert.All(projectiles, hitbox => Assert.Equal((byte)AbilitySlots.A, hitbox.AttackSlot));
    }

}
