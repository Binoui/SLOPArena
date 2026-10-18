using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace SlopArena.Shared
{
    /// <summary>The input the GameServer consumed for one fighter during a frame's tick (InputRelay).</summary>
    public readonly record struct RelayedInput(ulong EntityId, bool HasInput, InputState Input);

    /// <summary>
    /// One tick of authoritative Match state for full-world rollback (ADR-0039): the World
    /// Snapshot after the tick, the input each fighter consumed during it, and the complete
    /// record of presentation events and hits of the tick and the <see cref="EventHistoryTicks"/>
    /// − 1 ticks before it, so a lost frame or two loses neither. Sent unreliably every tick; a
    /// newer frame supersedes a lost one. The body is DEFLATE-compressed when that is smaller.
    /// Decoding rejects malformed input with <see cref="InvalidDataException"/> and never
    /// decompresses beyond <see cref="MaxBodyBytes"/>.
    /// </summary>
    public sealed class WorldFramePacket
    {
        public const uint Magic = 0x4D524657; // "WFRM"
        public const byte FormatVersion = 1;
        public const int HeaderSize = 16;
        public const int MaxBodyBytes = 64 * 1024;
        public const int MaxPresentationEvents = 256;
        public const int MaxHits = 128;
        /// <summary>Ticks whose complete event and hit record each frame carries, ending at its own tick.</summary>
        public const uint EventHistoryTicks = 3;
        private const byte CompressedFlag = 1;

        public WorldFramePacket(MatchState matchState, WorldSnapshot snapshot, IReadOnlyList<RelayedInput> inputs,
            IReadOnlyList<TimelinePresentationEvent> events, IReadOnlyList<SpellResolver.HitResult> hits)
        {
            MatchState = matchState;
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Hits = hits ?? throw new ArgumentNullException(nameof(hits));
            if (events.Count > MaxPresentationEvents)
                throw new ArgumentOutOfRangeException(nameof(events));
            if (hits.Count > MaxHits)
                throw new ArgumentOutOfRangeException(nameof(hits));
        }

        public uint Tick => Snapshot.Tick;
        public MatchState MatchState { get; }
        public WorldSnapshot Snapshot { get; }
        public IReadOnlyList<RelayedInput> Inputs { get; }
        /// <summary>Every presentation event of the ticks <see cref="EventsFromTick"/> to <see cref="Tick"/>.</summary>
        public IReadOnlyList<TimelinePresentationEvent> Events { get; }
        /// <summary>Every hit (blocked or not) of the ticks <see cref="EventsFromTick"/> to <see cref="Tick"/>.</summary>
        public IReadOnlyList<SpellResolver.HitResult> Hits { get; }

        /// <summary>First tick whose complete event and hit record this frame carries.</summary>
        public uint EventsFromTick => EventsFrom(Tick);

        public static uint EventsFrom(uint tick) => tick >= EventHistoryTicks - 1 ? tick - (EventHistoryTicks - 1) : 0;

        /// <summary>True when <paramref name="evt"/> satisfies the per-event wire rules a frame enforces.</summary>
        public static bool CanCarry(in TimelinePresentationEvent evt)
        {
            try
            {
                _ = new PresentationEventPacket(evt.MatchTick, evt.EntityId, evt.OperationIndex, evt.PresentationId,
                    evt.AttackSequence, evt.Source, evt.WorldX, evt.WorldY, evt.WorldZ, evt.WorldYaw, evt.Placement);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Encode the frame. Events must already satisfy <see cref="PresentationEventPacket"/>'s
        /// rules, and events and hits must belong to the frame's event ticks; an invalid one throws
        /// <see cref="ArgumentException"/> like the per-event wire.
        /// </summary>
        public byte[] Serialize()
        {
            var w = new SnapshotWriter(Snapshot.Data.Length + 256);
            w.WriteInt(Snapshot.Data.Length);
            foreach (byte b in Snapshot.Data)
                w.WriteByte(b);
            w.WriteInt(Inputs.Count);
            var inputBytes = new byte[InputState.Size];
            foreach (var relayed in Inputs)
            {
                w.WriteULong(relayed.EntityId);
                w.WriteBool(relayed.HasInput);
                if (!relayed.HasInput)
                    continue;
                relayed.Input.Write(inputBytes);
                foreach (byte b in inputBytes)
                    w.WriteByte(b);
            }
            w.WriteInt(Events.Count);
            foreach (var evt in Events)
            {
                if (evt.MatchTick < EventsFromTick || evt.MatchTick > Tick)
                    throw new ArgumentOutOfRangeException(nameof(Events),
                        $"Event of tick {evt.MatchTick} is outside frame {Tick}'s event ticks.");
                WriteEvent(w, evt);
            }
            w.WriteInt(Hits.Count);
            foreach (var hit in Hits)
            {
                if (hit.MatchTick < EventsFromTick || hit.MatchTick > Tick)
                    throw new ArgumentOutOfRangeException(nameof(Hits),
                        $"Hit of tick {hit.MatchTick} is outside frame {Tick}'s event ticks.");
                WriteHit(w, hit);
            }

            byte[] body = w.ToArray();
            if (body.Length > MaxBodyBytes)
                throw new InvalidOperationException($"World frame body of {body.Length} bytes exceeds {MaxBodyBytes}.");
            byte[] compressed = Deflate(body);
            bool useCompressed = compressed.Length < body.Length;
            byte[] payload = useCompressed ? compressed : body;

            var frame = new byte[HeaderSize + payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, Magic);
            frame[4] = FormatVersion;
            frame[5] = SimulationProtocol.Version;
            frame[6] = useCompressed ? CompressedFlag : (byte)0;
            frame[7] = (byte)MatchState;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8), Tick);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(12), body.Length);
            payload.CopyTo(frame, HeaderSize);
            return frame;
        }

        /// <summary>Tick of an encoded frame, read from the header without decoding the body.</summary>
        public static bool TryPeekTick(ReadOnlySpan<byte> frame, out uint tick)
        {
            tick = 0;
            if (frame.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(frame) != Magic)
                return false;
            tick = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(8));
            return true;
        }

        public static bool TryDeserialize(ReadOnlySpan<byte> frame, out WorldFramePacket? packet)
        {
            try
            {
                packet = Deserialize(frame);
                return true;
            }
            catch (InvalidDataException)
            {
                packet = null;
                return false;
            }
        }

        public static WorldFramePacket Deserialize(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < HeaderSize)
                throw Malformed("truncated header");
            if (BinaryPrimitives.ReadUInt32LittleEndian(frame) != Magic)
                throw Malformed("magic");
            if (frame[4] != FormatVersion)
                throw new InvalidDataException($"World frame format {frame[4]} is not supported.");
            if (frame[5] != SimulationProtocol.Version)
                throw new InvalidDataException($"World frame protocol {frame[5]} is not supported.");
            byte flags = frame[6];
            if ((flags & ~CompressedFlag) != 0)
                throw Malformed("flags");
            var matchState = (MatchState)frame[7];
            if (matchState > MatchState.Ended)
                throw Malformed("match state");
            uint tick = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(8));
            int bodyLength = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(12));
            if (bodyLength < 0 || bodyLength > MaxBodyBytes)
                throw Malformed("body length");

            var payload = frame.Slice(HeaderSize);
            byte[] body;
            if ((flags & CompressedFlag) != 0)
                body = Inflate(payload, bodyLength);
            else if (payload.Length != bodyLength)
                throw Malformed("body length");
            else
                body = payload.ToArray();

            var r = new SnapshotReader(body);
            int snapshotLength = r.ReadCount(1);
            var snapshotBytes = new byte[snapshotLength];
            for (int i = 0; i < snapshotLength; i++)
                snapshotBytes[i] = r.ReadByte();
            var snapshot = new WorldSnapshot(snapshotBytes);
            if (snapshot.Tick != tick)
                throw Malformed("snapshot tick");

            int inputCount = r.ReadCount(9);
            var inputs = new RelayedInput[inputCount];
            var inputBytes = new byte[InputState.Size];
            for (int i = 0; i < inputCount; i++)
            {
                ulong entityId = r.ReadULong();
                if (i > 0 && entityId <= inputs[i - 1].EntityId)
                    throw Malformed("relayed input order");
                bool hasInput = r.ReadBool();
                InputState input = default;
                if (hasInput)
                {
                    for (int b = 0; b < inputBytes.Length; b++)
                        inputBytes[b] = r.ReadByte();
                    input = InputState.Deserialize(inputBytes);
                }
                inputs[i] = new RelayedInput(entityId, hasInput, input);
            }

            int eventCount = r.ReadCount(40);
            if (eventCount > MaxPresentationEvents)
                throw Malformed("presentation event count");
            var events = new TimelinePresentationEvent[eventCount];
            for (int i = 0; i < eventCount; i++)
            {
                events[i] = ReadEvent(r);
                if (events[i].MatchTick < EventsFrom(tick) || events[i].MatchTick > tick)
                    throw Malformed("presentation event tick");
            }

            int hitCount = r.ReadCount(HitBytes);
            if (hitCount > MaxHits)
                throw Malformed("hit count");
            var hits = new SpellResolver.HitResult[hitCount];
            for (int i = 0; i < hitCount; i++)
            {
                hits[i] = ReadHit(r);
                if (hits[i].MatchTick < EventsFrom(tick) || hits[i].MatchTick > tick)
                    throw Malformed("hit tick");
            }
            if (!r.AtEnd)
                throw Malformed("trailing data");
            return new WorldFramePacket(matchState, snapshot, inputs, events, hits);
        }

        private const int HitBytes = 8 + 8 + 1 + 8 + 1 + 1 + 4 + 4 + 1 + 1 + 4 + 4 + 4 + 2 + 2 + 1 + 1 + 12 + 4 + 2 + 1 + 4 + 4;

        private static void WriteHit(SnapshotWriter w, in SpellResolver.HitResult hit)
        {
            w.WriteULong(hit.TargetEntityId);
            w.WriteULong(hit.OwnerEntityId);
            w.WriteByte(hit.AttackSlot);
            w.WriteULong(hit.ActivationId);
            w.WriteByte(hit.AttackSequence);
            w.WriteBool(hit.Airborne);
            w.WriteFloat(hit.Damage);
            w.WriteFloat(hit.DirX);
            w.WriteSByte(hit.KnockbackAngle);
            w.WriteByte((byte)hit.KnockbackDirection);
            w.WriteFloat(hit.DirZ);
            w.WriteFloat(hit.BaseKnockback);
            w.WriteFloat(hit.KnockbackGrowth);
            w.WriteUShort(hit.StunTicks);
            w.WriteUShort(hit.FixedHitstunTicks);
            w.WriteBool(hit.ArmorProtected);
            w.WriteBool(hit.FreezesOwner);
            w.WriteFloat(hit.HitX);
            w.WriteFloat(hit.HitY);
            w.WriteFloat(hit.HitZ);
            w.WriteFloat(hit.ImpactForce);
            w.WriteUShort(hit.HitstopTicks);
            w.WriteBool(hit.Blocked);
            w.WriteUInt(hit.MatchTick);
            w.WriteFloat(hit.ContactTime);
        }

        private static SpellResolver.HitResult ReadHit(SnapshotReader r)
        {
            var hit = new SpellResolver.HitResult
            {
                TargetEntityId = r.ReadULong(),
                OwnerEntityId = r.ReadULong(),
                AttackSlot = r.ReadByte(),
                ActivationId = r.ReadULong(),
                AttackSequence = r.ReadByte(),
                Airborne = r.ReadBool(),
                Damage = r.ReadFloat(),
                DirX = r.ReadFloat(),
                KnockbackAngle = r.ReadSByte(),
                KnockbackDirection = (AuthoringKnockbackDirection)r.ReadByte(),
                DirZ = r.ReadFloat(),
                BaseKnockback = r.ReadFloat(),
                KnockbackGrowth = r.ReadFloat(),
                StunTicks = r.ReadUShort(),
                FixedHitstunTicks = r.ReadUShort(),
                ArmorProtected = r.ReadBool(),
                FreezesOwner = r.ReadBool(),
                HitX = r.ReadFloat(),
                HitY = r.ReadFloat(),
                HitZ = r.ReadFloat(),
                ImpactForce = r.ReadFloat(),
                HitstopTicks = r.ReadUShort(),
                Blocked = r.ReadBool(),
                MatchTick = r.ReadUInt(),
                ContactTime = r.ReadFloat(),
            };
            if (hit.KnockbackDirection > AuthoringKnockbackDirection.TowardOwner)
                throw Malformed("hit knockback direction");
            return hit;
        }

        private static void WriteEvent(SnapshotWriter w, in TimelinePresentationEvent evt)
        {
            // Validate with the per-event wire rules so both encodings accept the same events.
            _ = new PresentationEventPacket(evt.MatchTick, evt.EntityId, evt.OperationIndex, evt.PresentationId,
                evt.AttackSequence, evt.Source, evt.WorldX, evt.WorldY, evt.WorldZ, evt.WorldYaw, evt.Placement);
            w.WriteUInt(evt.MatchTick);
            w.WriteULong(evt.EntityId);
            w.WriteInt(evt.OperationIndex);
            w.WriteString(evt.PresentationId);
            w.WriteByte(evt.AttackSequence);
            w.WriteByte((byte)evt.Source);
            w.WriteFloat(evt.WorldX);
            w.WriteFloat(evt.WorldY);
            w.WriteFloat(evt.WorldZ);
            w.WriteFloat(evt.WorldYaw);
            var placement = evt.Placement;
            w.WriteByte((byte)placement.AttachmentMode);
            w.WriteString(placement.BoneId);
            w.WriteFloat(placement.LocalPositionX);
            w.WriteFloat(placement.LocalPositionY);
            w.WriteFloat(placement.LocalPositionZ);
            w.WriteFloat(placement.LocalRotationX);
            w.WriteFloat(placement.LocalRotationY);
            w.WriteFloat(placement.LocalRotationZ);
            w.WriteFloat(placement.LocalScaleX);
            w.WriteFloat(placement.LocalScaleY);
            w.WriteFloat(placement.LocalScaleZ);
            w.WriteUShort(placement.DurationTicks);
        }

        private static TimelinePresentationEvent ReadEvent(SnapshotReader r)
        {
            uint matchTick = r.ReadUInt();
            ulong entityId = r.ReadULong();
            int operationIndex = r.ReadInt();
            string? presentationId = r.ReadString();
            byte attackSequence = r.ReadByte();
            var source = (PresentationEventSource)r.ReadByte();
            float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat(), yaw = r.ReadFloat();
            var placement = new PresentationPlacement(
                (AuthoringPresentationAttachmentMode)r.ReadByte(), r.ReadString(),
                r.ReadFloat(), r.ReadFloat(), r.ReadFloat(),
                r.ReadFloat(), r.ReadFloat(), r.ReadFloat(),
                r.ReadFloat(), r.ReadFloat(), r.ReadFloat(),
                r.ReadUShort());
            try
            {
                return new PresentationEventPacket(matchTick, entityId, operationIndex,
                    presentationId ?? throw Malformed("presentation id"), attackSequence, source,
                    x, y, z, yaw, placement).ToEvent();
            }
            catch (ArgumentException)
            {
                throw Malformed("presentation event");
            }
        }

        private static byte[] Deflate(byte[] body)
        {
            using var output = new MemoryStream(body.Length / 2 + 16);
            using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
                deflate.Write(body, 0, body.Length);
            return output.ToArray();
        }

        private static byte[] Inflate(ReadOnlySpan<byte> payload, int bodyLength)
        {
            var body = new byte[bodyLength];
            try
            {
                using var input = new MemoryStream(payload.ToArray(), writable: false);
                using var inflate = new DeflateStream(input, CompressionMode.Decompress);
                int read = 0;
                while (read < bodyLength)
                {
                    int n = inflate.Read(body, read, bodyLength - read);
                    if (n == 0) break;
                    read += n;
                }
                // The declared length must match exactly: no short body, no extra output.
                if (read != bodyLength || inflate.ReadByte() != -1)
                    throw Malformed("compressed body length");
            }
            catch (IOException exception)
            {
                throw new InvalidDataException("World frame body is not valid DEFLATE data.", exception);
            }
            return body;
        }

        private static InvalidDataException Malformed(string what) => new($"World frame is malformed: {what}.");
    }
}
