using System;
using System.Buffers.Binary;
using System.IO;

namespace SlopArena.Shared
{
    public enum TargetLockMode : byte
    {
        Never = 0,
        Always = 1,
        OnHit = 2,
    }

    /// <summary>
    /// Input state for one tick of simulation.
    /// Pure C# — no Godot types; this is the serialized input payload.
    /// </summary>
    public struct InputState
    {
        public bool Up, Down, DownPressed, Left, Right;
        public bool Jump, Dash, Burst;
        // Dash remains a legacy wire bit; ShieldPressed owns fresh air-dodge input.
        /// <summary>Logical shield/air-dodge control held this tick.</summary>
        public bool ShieldHeld;
        /// <summary>Fresh logical defense press edge; simulation chooses ground shield or air dodge.</summary>
        public bool ShieldPressed;
        /// <summary>Fresh logical grab edge; client modifier/chord handling is already resolved.</summary>
        public bool GrabPressed;
        /// <summary>
        /// True while the jump key is physically held (issue #116 / #106). The sim counts
        /// consecutive held ticks (<c>CharacterState.JumpHeldTicks</c>) and releases within
        /// <c>Simulation.ShortHopWindowTicks</c> produce a reduced short hop.
        /// </summary>
        public bool JumpHeld;
        /// <summary>
        /// LMB facing snap (ADR-0017, issue #126): one-tick edge set on the LMB press.
        /// The sim snaps <c>FacingYaw</c> to the camera azimuth (<c>AimYaw</c>) when the
        /// input gate allows; it does not disengage target lock.
        /// </summary>
        public bool FaceToCamera;
        /// <summary>RMB target-lock toggle edge; simulation owns <c>CharacterState.LockOn</c>.</summary>
        public bool ToggleLock;
        /// <summary>Explicit retarget edge; Shared selects the closest eligible enemy.</summary>
        public bool RetargetPressed;
        /// <summary>Saved automatic target-lock policy for this input tick.</summary>
        public TargetLockMode LockMode;
        public float MoveX, MoveY;
        /// <summary>
        /// 0 = none, 1 = LMB, 2 = RMB, 3 = Q, 4 = E, 5 = R, 6 = F
        /// </summary>
        public byte ActiveSlot;
        /// <summary>True while holding an aim-to-fire ability (RMB charge, Q throw).</summary>
        public bool IsAiming;
        public short FacingYaw;
        /// <summary>Aim yaw in degrees × 100 (short, -18000 to 18000). Sent by client, overrides FacingYaw for combat.</summary>
        public short AimYaw;
        /// <summary>Aim distance in cm (ushort, 0-6500, i.e. 0-65m). Set by client during targeted-aiming state.</summary>
        public ushort AimDistance;
        /// <summary>Aim pitch in degrees × 100 (short, -9000 to 9000). Camera-relative vertical aim.</summary>
        public short AimPitch;

        /// <summary>Client's selected target entity ID (0=none). Computed from screen-center proximity.</summary>
        public byte TargetEntityId;

        /// <summary>Retired local warp metadata retained for recorded-input compatibility; ignored by gameplay.</summary>
        public float WarpTargetX, WarpTargetZ;
        public float WarpSpeed;
        public float WarpAttackRange;

        /// <summary>22 bytes: 21-byte input payload plus the protocol version.</summary>
        /// <remarks>
        /// Flags byte (byte 8): 1=Up, 2=Down, 4=Left, 8=Right, 0x10=Jump, 0x20=legacy Dash,
        /// 0x40=retired Burst (reserved, inert), 0x80=IsAiming.
        /// Flags2 byte (byte 19): 1=JumpHeld, 2=FaceToCamera, 4=ToggleLock,
        /// 8=DownPressed, 0x10=ShieldHeld, 0x20=ShieldPressed, 0x40=GrabPressed,
        /// 0x80=RetargetPressed. Byte 20 is TargetLockMode; byte 21 is the exact
        /// SimulationProtocol version.
        /// </remarks>
        public const int Size = 22;

        /// <summary>
        /// The held part of this input with every one-tick press edge cleared. The GameServer
        /// uses it for a missing tick, the client for catch-up ticks and prediction for
        /// speculative opponent ticks, so all three must stay identical.
        /// </summary>
        public InputState WithoutEdges()
        {
            var held = this;
            held.Jump = false;
            held.DownPressed = false;
            held.ShieldPressed = false;
            held.GrabPressed = false;
            held.FaceToCamera = false;
            held.ToggleLock = false;
            held.RetargetPressed = false;
            held.ActiveSlot = 0;
            return held;
        }

        /// <summary>True when this input carries any one-tick press edge.</summary>
        public bool HasPressEdges => Jump || DownPressed || ShieldPressed || GrabPressed ||
            FaceToCamera || ToggleLock || RetargetPressed || ActiveSlot != 0;

        /// <summary>
        /// This input with the press edges of an <paramref name="earlier"/> input that was never
        /// consumed, so the press is not lost. A slot pressed on this input wins over an earlier
        /// one. The client merges presses between fixed updates and the GameServer merges presses
        /// that arrived after their tick ran; both use this rule.
        /// </summary>
        public InputState WithEdgesFrom(InputState earlier)
        {
            var merged = this;
            merged.Jump |= earlier.Jump;
            merged.DownPressed |= earlier.DownPressed;
            merged.ShieldPressed |= earlier.ShieldPressed;
            merged.GrabPressed |= earlier.GrabPressed;
            merged.FaceToCamera |= earlier.FaceToCamera;
            merged.ToggleLock |= earlier.ToggleLock;
            merged.RetargetPressed |= earlier.RetargetPressed;
            if (merged.ActiveSlot == 0) merged.ActiveSlot = earlier.ActiveSlot;
            return merged;
        }

        public void Write(Span<byte> buf)
        {
            if (buf.Length < Size)
                throw new ArgumentException("Buffer too small", nameof(buf));

            BinaryPrimitives.WriteInt32LittleEndian(buf, BitConverter.SingleToInt32Bits(MoveX));
            BinaryPrimitives.WriteInt32LittleEndian(buf.Slice(4), BitConverter.SingleToInt32Bits(MoveY));
            byte flags = 0;
            if (Up) flags |= 1;
            if (Down) flags |= 2;
            if (Left) flags |= 4;
            if (Right) flags |= 8;
            if (Jump) flags |= 0x10;
            if (Dash) flags |= 0x20;
            if (Burst) flags |= 0x40;
            if (IsAiming) flags |= 0x80;
            buf[8] = flags;
            buf[9] = ActiveSlot;
            BinaryPrimitives.WriteInt16LittleEndian(buf.Slice(10), FacingYaw);
            BinaryPrimitives.WriteInt16LittleEndian(buf.Slice(12), AimYaw);
            BinaryPrimitives.WriteInt16LittleEndian(buf.Slice(14), AimPitch);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(16), AimDistance);
            buf[18] = TargetEntityId;
            byte flags2 = 0;
            if (JumpHeld) flags2 |= 1;
            if (FaceToCamera) flags2 |= 2;
            if (ToggleLock) flags2 |= 4;
            if (ShieldHeld) flags2 |= 0x10;
            if (ShieldPressed) flags2 |= 0x20;
            if (GrabPressed) flags2 |= 0x40;
            if (DownPressed) flags2 |= 8;
            if (RetargetPressed) flags2 |= 0x80;
            buf[19] = flags2;
            buf[20] = (byte)LockMode;
            buf[21] = SimulationProtocol.Version;

        }
        public static InputState Deserialize(ReadOnlySpan<byte> buf)
        {
            if (buf.Length != Size)
                throw new ArgumentException($"Input payload must be exactly {Size} bytes.", nameof(buf));
            if (buf[21] != SimulationProtocol.Version)
                throw new InvalidDataException($"Unsupported input protocol version {buf[21]}.");
            if (buf[20] > (byte)TargetLockMode.OnHit)
                throw new InvalidDataException($"Unsupported target lock mode {buf[20]}.");

            var input = new InputState
            {
                MoveX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf)),
                MoveY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(4))),
                LockMode = (TargetLockMode)buf[20],
            };
            byte flags = buf[8];
            input.Up = (flags & 1) != 0;
            input.Down = (flags & 2) != 0;
            input.Left = (flags & 4) != 0;
            input.Right = (flags & 8) != 0;
            input.Jump = (flags & 0x10) != 0;
            input.Dash = (flags & 0x20) != 0;
            input.Burst = (flags & 0x40) != 0;
            input.IsAiming = (flags & 0x80) != 0;
            input.ActiveSlot = buf[9];
            input.FacingYaw = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(10));
            input.AimYaw = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(12));
            input.AimPitch = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(14));
            input.AimDistance = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(16));
            input.TargetEntityId = buf[18];
            input.JumpHeld = (buf[19] & 1) != 0;
            input.FaceToCamera = (buf[19] & 2) != 0;
            input.ToggleLock = (buf[19] & 4) != 0;
            input.ShieldHeld = (buf[19] & 0x10) != 0;
            input.ShieldPressed = (buf[19] & 0x20) != 0;
            input.GrabPressed = (buf[19] & 0x40) != 0;
            input.DownPressed = (buf[19] & 8) != 0;
            input.RetargetPressed = (buf[19] & 0x80) != 0;
            return input;
        }
    }
}
