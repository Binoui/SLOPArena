using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class MatchResultPacketTests
{
    [Fact]
    public void RoundTrip_CarriesACoopSlopballRunAndStockFightsCarryNone()
    {
        var entries = new[] { new MatchResultEntry(1, 1, 0, 0), new MatchResultEntry(2, 2, 0, 0) };
        var run = new MatchResultPacket(900, sharedVictory: true, entries, new SlopballRunResult(27, 11, 3));
        var buffer = new byte[run.WireSize];
        run.Serialize(buffer);
        Assert.True(MatchResultPacket.TryDeserialize(buffer, out var decoded));
        Assert.Equal(new SlopballRunResult(27, 11, 3), decoded!.Run);
        Assert.True(run.WireSize <= MatchResultPacket.MaxSize);

        var fight = new MatchResultPacket(900, sharedVictory: false, entries);
        var fightBuffer = new byte[fight.WireSize];
        fight.Serialize(fightBuffer);
        Assert.True(MatchResultPacket.TryDeserialize(fightBuffer, out var stock));
        Assert.Null(stock!.Run);
        Assert.Equal(buffer.Length - 10, fightBuffer.Length);
    }

    [Fact]
    public void RoundTrip_PreservesAuthoritativePlacementsAndStats()
    {
        var packet = new MatchResultPacket(
            durationTicks: 4321,
            sharedVictory: false,
            entries: new[]
            {
                new MatchResultEntry(7, placement: 1, kos: 4, falls: 0),
                new MatchResultEntry(3, placement: 2, kos: 2, falls: 2),
                new MatchResultEntry(9, placement: 3, kos: 1, falls: 3),
                new MatchResultEntry(4, placement: 4, kos: 0, falls: 3),
            });
        var buffer = new byte[packet.WireSize];

        packet.Serialize(buffer);

        Assert.True(MatchResultPacket.TryDeserialize(buffer, out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal(4321u, decoded!.DurationTicks);
        Assert.False(decoded.SharedVictory);
        Assert.Equal(4, decoded.Entries.Length);
        Assert.Equal((ulong)7, decoded.Entries[0].EntityId);
        Assert.Equal((byte)1, decoded.Entries[0].Placement);
        Assert.Equal((byte)4, decoded.Entries[0].KOs);
        Assert.Equal((byte)0, decoded.Entries[0].Falls);
        Assert.Equal((ulong)4, decoded.Entries[3].EntityId);
    }

[Theory]
[InlineData(2)]
[InlineData(3)]
[InlineData(4)]
public void RoundTrip_SupportsOnlyRosteredPlayerCount(int playerCount)
{
    var entries = new MatchResultEntry[playerCount];
    for (int i = 0; i < entries.Length; i++)
        entries[i] = new MatchResultEntry((ulong)(i + 1), (byte)(i + 1), 0, (byte)i);

    var packet = new MatchResultPacket(60, false, entries);
    var buffer = new byte[packet.WireSize];
    packet.Serialize(buffer);

    Assert.True(MatchResultPacket.TryDeserialize(buffer, out var decoded));
    Assert.NotNull(decoded);
    Assert.Equal(playerCount, decoded!.Entries.Length);
}

    [Fact]
    public void TryDeserialize_RejectsNonResultDatagram()
    {
        Assert.False(MatchResultPacket.TryDeserialize(new byte[MatchResultPacket.MaxSize], out _));
    }

    [Fact]
    public void Constructor_RejectsEmptyOrOverCapacityResults()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchResultPacket(0, false, System.Array.Empty<MatchResultEntry>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchResultPacket(0, false, new MatchResultEntry[MatchResultPacket.MaxPlayers + 1]));
    }
}
