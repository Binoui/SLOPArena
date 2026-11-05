using System;

namespace SlopArena.Shared.Mayhem;

/// <summary>JUMP! tuning, indexed by speed tier. Provisional.</summary>
public static class JumpTuning
{
    public static readonly float[] RingSpeed = { 8f, 9.5f, 11f };
    /// <summary>Zero means single slams; otherwise each slam is followed by a second after this gap.</summary>
    public static readonly ushort[] PairGapTicks = { 0, 44, 36 };
    /// <summary>Ticks from one group's last slam to the next group's first, so everyone lands in between.</summary>
    public const ushort GroupGapTicks = 90;
    public const ushort FirstSlamTicks = 40;
    public const ushort EndMarginTicks = 6;
    public const float RingHalfWidth = .28f, RingHalfHeight = .25f;
    public const int MaxSlams = 8;
    /// <summary>The CPU presses jump this many ticks before the ring reaches its body.</summary>
    public const int CpuJumpLeadTicks = 14;
    public const int CpuHoldTicks = 14;
}

/// <summary>
/// JUMP!: Manki slams the deck centre and unblockable rings roll out; a ring catches grounded fighters only.
/// Later tiers slam in pairs. Pass: not failed by the deadline.
/// </summary>
internal sealed class JumpChannel : IMayhemChannel
{
    private readonly byte _lane;
    private readonly SpeedTier _tier;
    private readonly ArcadePoint _center;
    private readonly ushort _lifetime;
    private readonly uint[] _slams;

    private JumpChannel(byte lane, SpeedTier tier, ArcadePoint center, ushort lifetime, uint[] slams)
    { _lane = lane; _tier = tier; _center = center; _lifetime = lifetime; _slams = slams; }

    private float Speed => JumpTuning.RingSpeed[(int)_tier];

    internal static JumpChannel Start(ChannelSetup setup)
    {
        int tier = (int)setup.Tier;
        var stage = setup.Stage;
        var center = new ArcadePoint(MayhemMath.CenterX(stage), stage.DeckTop + JumpTuning.RingHalfHeight, MayhemMath.CenterZ(stage));
        ushort lifetime = Lifetime(stage, center, JumpTuning.RingSpeed[tier]);
        ushort pair = JumpTuning.PairGapTicks[tier];
        long last = (long)setup.Deadline - JumpTuning.EndMarginTicks - lifetime;
        var slams = new System.Collections.Generic.List<uint>();
        for (long at = setup.PlayStart + JumpTuning.FirstSlamTicks; at <= last && slams.Count < JumpTuning.MaxSlams;)
        {
            slams.Add((uint)at);
            if (pair > 0 && at + pair <= last) { at += pair; slams.Add((uint)at); }
            at += JumpTuning.GroupGapTicks;
        }
        return new JumpChannel(setup.Lane, setup.Tier, center, lifetime, slams.ToArray());
    }

    private static ushort Lifetime(MankiMayhemDefinition stage, ArcadePoint center, float speed)
        => MayhemMath.RingTicks(MayhemMath.FarthestCorner(stage, center.X, center.Z) + JumpTuning.RingHalfWidth, speed);

    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        if (tick.Moment != ChannelMoment.BeforeActions) return;
        for (int i = 0; i < _slams.Length; i++)
        {
            if (tick.Tick + MayhemHazardTuning.WindUpTicks == _slams[i]) hazards.Cue(MayhemEventKind.WindUp, _lane, (ushort)i);
            if (tick.Phase == MayhemPhase.Play && tick.Tick == _slams[i])
                hazards.Ring(_lane, (ushort)i, _center, Speed, JumpTuning.RingHalfWidth, JumpTuning.RingHalfHeight, _lifetime);
        }
    }

    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards)
        => contact.Kind == MayhemContactKind.HazardBody && contact.Lane == _lane
            ? ChannelContactResult.Fail(contact.TargetSeat, FailCause.Hazard, SplatStyle.Flattened)
            : ChannelContactResult.Absorbed;

    public MayhemJudgement Judge(in ContestantProbe probe) => MayhemJudgement.Ready;

    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self.State;
        float distance = MathF.Sqrt(MayhemMath.DistanceSquared(self.PX, self.PZ, _center.X, _center.Z))
            - probe.Self.Definition.CapsuleRadius - JumpTuning.RingHalfWidth;
        foreach (uint slam in _slams)
        {
            if (probe.Slip && slam == _slams[0] && probe.Seen < slam + MathF.Max(0f, distance) / Speed * 60f + 2) continue;
            float arrival = slam + MathF.Max(0f, distance) / Speed * 60f;
            float until = arrival - probe.Seen;
            if (until <= JumpTuning.CpuJumpLeadTicks && until >= JumpTuning.CpuJumpLeadTicks - JumpTuning.CpuHoldTicks)
                return new(CpuAction.Jump, self.PX, self.PZ);
        }
        return new(CpuAction.Brake, self.PX, self.PZ);
    }

    public ChannelScene Observe()
    {
        var rings = new RingCue[_slams.Length];
        for (int i = 0; i < _slams.Length; i++)
            rings[i] = new RingCue(_lane, (ushort)i, _center, _slams[i], Speed, JumpTuning.RingHalfWidth, JumpTuning.RingHalfHeight, _lifetime);
        return ChannelScene.Empty with { Rings = rings };
    }

    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteByte(_lane); w.WriteByte((byte)_tier);
        w.WriteFloat(_center.X); w.WriteFloat(_center.Y); w.WriteFloat(_center.Z); w.WriteUShort(_lifetime);
        w.WriteByte((byte)_slams.Length);
        foreach (uint slam in _slams) w.WriteUInt(slam);
    }

    internal static JumpChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte();
        var tier = (SpeedTier)r.ReadByte();
        var center = new ArcadePoint(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
        ushort lifetime = r.ReadUShort();
        int count = r.ReadByte();
        if (lane > MayhemEvent.MaxLane || tier > SpeedTier.Three || !center.IsFinite || count > JumpTuning.MaxSlams
            || !MayhemMath.OnDeck(context.Stage, center.X, center.Z) || lifetime == 0)
            throw SnapshotReader.Malformed("JUMP! state");
        var slams = new uint[count];
        for (int i = 0; i < count; i++)
        {
            slams[i] = r.ReadUInt();
            if (slams[i] < context.PlayStart || slams[i] >= context.Deadline) throw SnapshotReader.Malformed("JUMP! slam");
        }
        return new JumpChannel(lane, tier, center, lifetime, slams);
    }
}
