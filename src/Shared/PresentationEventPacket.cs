using System;
using System.Buffers.Binary;
using System.Text;

namespace SlopArena.Shared;

public readonly struct PresentationEventPacket
{
    private const uint Magic = 0x53455250;
    public const int Version = 3;
    public const int HeaderSize = 145;
    public const int MaxPresentationIdBytes = 64;
    public const int MaxBoneIdBytes = 64;
    public const int MaxSize = HeaderSize + MaxPresentationIdBytes;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public PresentationEventPacket(
        uint matchTick,
        ulong entityId,
        int operationIndex,
        string presentationId,
        byte attackSequence,
        PresentationEventSource source,
        float worldX,
        float worldY,
        float worldZ,
        float worldYaw,
        PresentationPlacement? placement = null)
    {
        if (operationIndex < 0) throw new ArgumentOutOfRangeException(nameof(operationIndex));
        ValidateSource(source);
        ValidateTransform(worldX, nameof(worldX));
        ValidateTransform(worldY, nameof(worldY));
        ValidateTransform(worldZ, nameof(worldZ));
        ValidateTransform(worldYaw, nameof(worldYaw));
        ValidateId(presentationId);
        MatchTick = matchTick;
        EntityId = entityId;
        OperationIndex = operationIndex;
        PresentationId = presentationId;
        AttackSequence = attackSequence;
        Source = source;
        WorldX = worldX;
        WorldY = worldY;
        WorldZ = worldZ;
        WorldYaw = worldYaw;
        Placement = placement ?? new PresentationPlacement();
        ValidatePlacement(Placement);
    }

    public uint MatchTick { get; }
    public ulong EntityId { get; }
    public int OperationIndex { get; }
    public string PresentationId { get; }
    public byte AttackSequence { get; }
    public PresentationEventSource Source { get; }
    public float WorldX { get; }
    public float WorldY { get; }
    public float WorldZ { get; }
    public float WorldYaw { get; }
    public PresentationPlacement Placement { get; }
    public int WireSize => HeaderSize + Utf8.GetByteCount(PresentationId);

    public TimelinePresentationEvent ToEvent()
        => new(MatchTick, EntityId, OperationIndex, PresentationId, AttackSequence, Source, WorldX, WorldY, WorldZ, WorldYaw)
        {
            Placement = Placement,
        };

    public void Serialize(Span<byte> buffer)
    {
        if (buffer.Length < WireSize) throw new ArgumentException("Buffer too small", nameof(buffer));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, Magic);
        buffer[4] = Version;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(5), MatchTick);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(9), EntityId);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(17), OperationIndex);
        buffer[21] = AttackSequence;
        buffer[22] = (byte)Source;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(23), BitConverter.SingleToInt32Bits(WorldX));
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(27), BitConverter.SingleToInt32Bits(WorldY));
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(31), BitConverter.SingleToInt32Bits(WorldZ));
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(35), BitConverter.SingleToInt32Bits(WorldYaw));
        buffer[40] = (byte)Placement.AttachmentMode;
        int boneLength = Placement.BoneId == null ? 0 : Utf8.GetByteCount(Placement.BoneId);
        buffer[41] = (byte)boneLength;
        if (boneLength > 0) Utf8.GetBytes(Placement.BoneId!, buffer.Slice(42, MaxBoneIdBytes));
        WriteFloat(buffer, 42 + MaxBoneIdBytes, Placement.LocalPositionX);
        WriteFloat(buffer, 46 + MaxBoneIdBytes, Placement.LocalPositionY);
        WriteFloat(buffer, 50 + MaxBoneIdBytes, Placement.LocalPositionZ);
        WriteFloat(buffer, 54 + MaxBoneIdBytes, Placement.LocalRotationX);
        WriteFloat(buffer, 58 + MaxBoneIdBytes, Placement.LocalRotationY);
        WriteFloat(buffer, 62 + MaxBoneIdBytes, Placement.LocalRotationZ);
        WriteFloat(buffer, 66 + MaxBoneIdBytes, Placement.LocalScaleX);
        WriteFloat(buffer, 70 + MaxBoneIdBytes, Placement.LocalScaleY);
        WriteFloat(buffer, 74 + MaxBoneIdBytes, Placement.LocalScaleZ);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(142), Placement.DurationTicks);
        int length = Utf8.GetBytes(PresentationId, buffer.Slice(HeaderSize));
        buffer[HeaderSize - 1] = (byte)length;
        buffer[39] = (byte)length;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> buffer, out PresentationEventPacket? packet)
    {
        packet = null;
        if (buffer.Length < HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(buffer) != Magic
            || buffer[4] != Version)
            return false;

        int length = buffer[HeaderSize - 1];
        int boneLength = buffer[41];
        if (length is < 1 or > MaxPresentationIdBytes ||
            boneLength > MaxBoneIdBytes ||
            buffer[39] != length ||
            buffer.Length != HeaderSize + length)
            return false;
        int operationIndex = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(17));
        if (operationIndex < 0) return false;
        var source = (PresentationEventSource)buffer[22];
        if (!IsKnownSource(source)) return false;
        var mode = (AuthoringPresentationAttachmentMode)buffer[40];
        if (mode != AuthoringPresentationAttachmentMode.World &&
            mode != AuthoringPresentationAttachmentMode.Bone)
            return false;
        float worldX = ReadFloat(buffer, 23);
        float worldY = ReadFloat(buffer, 27);
        float worldZ = ReadFloat(buffer, 31);
        float worldYaw = ReadFloat(buffer, 35);
        if (!IsFinite(worldX) || !IsFinite(worldY) || !IsFinite(worldZ) || !IsFinite(worldYaw)) return false;

        string boneId;
        string presentationId;
        try
        {
            boneId = boneLength == 0 ? "" : Utf8.GetString(buffer.Slice(42, boneLength));
            presentationId = Utf8.GetString(buffer.Slice(HeaderSize, length));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        try
        {
            packet = new PresentationEventPacket(
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(5)),
                BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(9)),
                operationIndex,
                presentationId,
                buffer[21],
                source,
                worldX,
                worldY,
                worldZ,
                worldYaw,
                new PresentationPlacement(
                    mode,
                    string.IsNullOrEmpty(boneId) ? null : boneId,
                    ReadFloat(buffer, 106),
                    ReadFloat(buffer, 110),
                    ReadFloat(buffer, 114),
                    ReadFloat(buffer, 118),
                    ReadFloat(buffer, 122),
                    ReadFloat(buffer, 126),
                    ReadFloat(buffer, 130),
                    ReadFloat(buffer, 134),
                    ReadFloat(buffer, 138),
                    BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(142))));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void WriteFloat(Span<byte> buffer, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset), BitConverter.SingleToInt32Bits(value));

    private static float ReadFloat(ReadOnlySpan<byte> buffer, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset)));

    private static void ValidatePlacement(PresentationPlacement placement)
    {
        if (placement == null ||
            (placement.AttachmentMode != AuthoringPresentationAttachmentMode.World &&
             placement.AttachmentMode != AuthoringPresentationAttachmentMode.Bone))
            throw new ArgumentOutOfRangeException(nameof(placement));
        if (placement.BoneId != null && Utf8.GetByteCount(placement.BoneId) > MaxBoneIdBytes)
            throw new ArgumentOutOfRangeException(nameof(placement.BoneId));
        foreach (float value in new[]
        {
            placement.LocalPositionX, placement.LocalPositionY, placement.LocalPositionZ,
            placement.LocalRotationX, placement.LocalRotationY, placement.LocalRotationZ,
            placement.LocalScaleX, placement.LocalScaleY, placement.LocalScaleZ,
        })
            if (!IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(placement));
        if (placement.LocalScaleX <= 0f || placement.LocalScaleY <= 0f || placement.LocalScaleZ <= 0f ||
            placement.DurationTicks == 0)
            throw new ArgumentOutOfRangeException(nameof(placement));
        if (placement.AttachmentMode == AuthoringPresentationAttachmentMode.Bone &&
            string.IsNullOrEmpty(placement.BoneId))
            throw new ArgumentOutOfRangeException(nameof(placement.BoneId));
    }

    private static void ValidateId(string presentationId)
    {
        if (string.IsNullOrEmpty(presentationId))
            throw new ArgumentException("Presentation ID must not be empty.", nameof(presentationId));
        int byteCount;
        try { byteCount = Utf8.GetByteCount(presentationId); }
        catch (EncoderFallbackException ex) { throw new ArgumentException("Presentation ID must be valid UTF-8.", nameof(presentationId), ex); }
        if (byteCount > MaxPresentationIdBytes)
            throw new ArgumentException("Presentation ID is too long.", nameof(presentationId));
    }

    private static void ValidateSource(PresentationEventSource source)
    {
        if (!IsKnownSource(source)) throw new ArgumentOutOfRangeException(nameof(source));
    }

    private static bool IsKnownSource(PresentationEventSource source)
        => source == PresentationEventSource.Timeline
            || source == PresentationEventSource.CapabilityExplosion
            || source == PresentationEventSource.BlockContact
            || source == PresentationEventSource.HitContact
            || source == PresentationEventSource.CapabilityLaunch
            || source == PresentationEventSource.CapabilityLanding
            || source == PresentationEventSource.ProjectileCatch
            || source == PresentationEventSource.ProjectileSelfHit;

    private static void ValidateTransform(float value, string name)
    {
        if (!IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
