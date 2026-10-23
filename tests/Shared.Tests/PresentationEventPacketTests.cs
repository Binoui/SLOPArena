using System;
using System.Buffers.Binary;
using System.Text;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class PresentationEventPacketTests
{
    private static PresentationEventPacket Packet(uint tick = 42, ulong entity = 7, int operation = 11, string id = "presentation.cyclone-kick.start")
        => new(tick, entity, operation, id, 3, PresentationEventSource.CapabilityExplosion, 1.25f, -2.5f, 3.75f, 0.5f);

    [Fact]
    public void RoundTripPreservesEventIdentityAndTransform()
    {
        var packet = Packet();
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);

        Assert.Equal(PresentationEventPacket.HeaderSize + Encoding.UTF8.GetByteCount(packet.PresentationId), packet.WireSize);
        Assert.True(PresentationEventPacket.TryDeserialize(bytes, out var decoded));
        Assert.Equal(packet.ToEvent(), decoded!.Value.ToEvent());
        Assert.Equal(3, decoded.Value.AttackSequence);
        Assert.Equal(PresentationEventSource.CapabilityExplosion, decoded.Value.Source);
        Assert.Equal(1.25f, decoded.Value.WorldX);
        Assert.Equal(-2.5f, decoded.Value.WorldY);
        Assert.Equal(3.75f, decoded.Value.WorldZ);
        Assert.Equal(0.5f, decoded.Value.WorldYaw);
    }

    [Fact]
    public void InvalidDatagramsAreRejected()
    {
        var packet = Packet(1, 2, 3, "presentation.hit");
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);

        Assert.False(PresentationEventPacket.TryDeserialize(bytes.AsSpan(0, bytes.Length - 1), out _));
        var trailing = new byte[bytes.Length + 1];
        Array.Copy(bytes, trailing, bytes.Length);
        Assert.False(PresentationEventPacket.TryDeserialize(trailing, out _));

        var wrongMagic = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongMagic, 0);
        Assert.False(PresentationEventPacket.TryDeserialize(wrongMagic, out _));

        var oldVersion = (byte[])bytes.Clone();
        oldVersion[4] = 1;
        Assert.False(PresentationEventPacket.TryDeserialize(oldVersion, out _));

        var negativeIndex = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(negativeIndex.AsSpan(17), -1);
        Assert.False(PresentationEventPacket.TryDeserialize(negativeIndex, out _));

        var unknownSource = (byte[])bytes.Clone();
        unknownSource[22] = 255;
        Assert.False(PresentationEventPacket.TryDeserialize(unknownSource, out _));

        var nonfiniteTransform = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(nonfiniteTransform.AsSpan(23), unchecked((int)0x7FC00000));
        Assert.False(PresentationEventPacket.TryDeserialize(nonfiniteTransform, out _));

        var invalidUtf8 = (byte[])bytes.Clone();
        invalidUtf8[40] = 0xFF;
        Assert.False(PresentationEventPacket.TryDeserialize(invalidUtf8, out _));

        var zeroLength = (byte[])bytes.Clone();
        zeroLength[39] = 0;
        Assert.False(PresentationEventPacket.TryDeserialize(zeroLength, out _));

        var overlong = new byte[PresentationEventPacket.MaxSize + 1];
        Array.Copy(bytes, overlong, bytes.Length);
        overlong[39] = 65;
        Assert.False(PresentationEventPacket.TryDeserialize(overlong, out _));
    }

    [Theory]
    [InlineData(PresentationEventSource.CapabilityLaunch)]
    [InlineData(PresentationEventSource.CapabilityLanding)]
    public void LeapPhaseEventsSurviveNetworkTransport(PresentationEventSource source)
    {
        var packet = new PresentationEventPacket(83, 7, 0, "presentation.bonk.slam.landing",
            3, source, 1.25f, 0f, 4f, 0.5f);
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);

        Assert.True(PresentationEventPacket.TryDeserialize(bytes, out var decoded));
        Assert.Equal(packet.ToEvent(), decoded!.Value.ToEvent());
    }

}
