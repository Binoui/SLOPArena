using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class SlotAddressTests
{
    [Fact]
    public void CanonicalMovesKeepHistoricalWireValuesAndReservedSlotsStayEmpty()
    {
        byte[] historical = { 3, 7, 8, 9, 11, 4, 5, 6 };
        foreach (var address in CanonicalSlotProjection.All)
        {
            byte wire = historical[address.Ordinal % 8];
            Assert.Equal(wire, CanonicalSlotProjection.ToWire(address));
            Assert.True(CanonicalSlotProjection.TryGet(wire, address.IsAirborne, out var decoded));
            Assert.Equal(address, decoded);
        }
        foreach (byte reserved in new byte[] { 0, 1, 2, 10, 12, 255 })
        {
            Assert.False(CanonicalSlotProjection.TryGet(reserved, false, out _));
            Assert.False(CanonicalSlotProjection.TryGet(reserved, true, out _));
        }
    }

    [Fact]
    public void AllHasExactCanonicalOrderAndMetadata()
    {
        var expectedLabels = new[] { "1", "2", "3", "4", "A", "E", "R", "F" };

        Assert.Equal(16, CanonicalSlotProjection.All.Count);
        for (var ordinal = 0; ordinal < CanonicalSlotProjection.All.Count; ordinal++)
        {
            var address = CanonicalSlotProjection.All[ordinal];
            var isAirborne = ordinal >= 8;
            var label = expectedLabels[ordinal % 8];
            Assert.Equal($"{(isAirborne ? "air" : "ground")}.{label}", address.Id);
            Assert.Equal(isAirborne, address.IsAirborne);
            Assert.Equal(label, address.InputLabel);
            Assert.Equal((byte)ordinal, address.Ordinal);
        }
    }

    [Theory]
    [InlineData("ground.1", false, "1", 0)]
    [InlineData("air.F", true, "F", 15)]
    public void TryGetByCanonicalIdReturnsAddress(string id, bool isAirborne, string label, byte ordinal)
    {
        Assert.True(CanonicalSlotProjection.TryGet(id, out var address));
        Assert.Equal(new SlotAddress(id, isAirborne, label, ordinal), address);
    }

    [Theory]
    [InlineData(false, "1", "ground.1", 0)]
    [InlineData(true, "F", "air.F", 15)]
    public void TryGetByInputReturnsAddress(bool isAirborne, string label, string id, byte ordinal)
    {
        Assert.True(CanonicalSlotProjection.TryGet(isAirborne, label, out var address));
        Assert.Equal(new SlotAddress(id, isAirborne, label, ordinal), address);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownCanonicalIdFailsWithDefault(string? id)
    {
        Assert.False(CanonicalSlotProjection.TryGet(id!, out var address));
        Assert.Equal(default, address);
    }

    [Theory]
    [InlineData(false, "unknown")]
    [InlineData(true, "")]
    [InlineData(false, null)]
    public void UnknownInputFailsWithDefault(bool isAirborne, string? label)
    {
        Assert.False(CanonicalSlotProjection.TryGet(isAirborne, label!, out var address));
        Assert.Equal(default, address);
    }

    [Fact]
    public void AllCannotBeMutatedThroughCollectionInterface()
    {
        var list = Assert.IsAssignableFrom<IList<SlotAddress>>(CanonicalSlotProjection.All);
        var original = CanonicalSlotProjection.All[0];

        Assert.Throws<NotSupportedException>(() => list[0] = new SlotAddress("bad", true, "bad", 255));
        Assert.Equal(original, CanonicalSlotProjection.All[0]);
        Assert.Equal(16, CanonicalSlotProjection.All.Count);
        Assert.Equal(16, CanonicalSlotProjection.All.Select(slot => slot.Ordinal).Distinct().Count());
    }
}
