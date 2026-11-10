using System;

namespace SlopArena.Shared.Mayhem;

internal enum JumpTwist : byte { Rings = 1, Crossfire = 2, TallWave = 3 }

/// <summary>JUMP! tuning, indexed by speed tier. Provisional.</summary>
public static class JumpTuning
{
    public static readonly ChannelTiming TallTiming = new(90, 420);
    public static readonly float[] RingSpeed = { 8f, 9.5f, 11f };
    /// <summary>Zero means single slams; otherwise each slam is followed by a second after this gap.</summary>
    public static readonly ushort[] PairGapTicks = { 0, 44, 36 };
    /// <summary>Ticks from one group's last slam to the next group's first, so everyone lands in between.</summary>
    public const ushort GroupGapTicks = 90;
    public const ushort FirstSlamTicks = 40;
    public const ushort EndMarginTicks = 6;
    public const float RingHalfWidth = .28f, RingHalfHeight = .25f;
    public const float CrossfireOffset = 1.2f, TallHalfHeight = 1.4f;
    public const int TallJumpLeadTicks = 48, TallSecondJumpTicks = 32;
    public const int MaxSlams = 8;
    /// <summary>The CPU presses jump this many ticks before the ring reaches its body.</summary>
    public const int CpuJumpLeadTicks = 14;
    public const int CpuHoldTicks = 14;
}

/// <summary>
/// JUMP!: unblockable rings roll out from slam points. Rings uses tiered pairs, Crossfire uses two centres,
/// and Tall wave needs a second jump. Pass: not failed by the deadline.
/// </summary>
internal sealed class JumpChannel : IMayhemChannel
{
    private readonly JumpTwist _twist;
    private readonly byte _lane;
    private readonly SpeedTier _tier;
    private readonly ArcadePoint _center;
    private readonly ushort _lifetime;
    private readonly uint[] _slams;

    private JumpChannel(JumpTwist twist, byte lane, SpeedTier tier, ArcadePoint center, ushort lifetime, uint[] slams)
    { _twist = twist; _lane = lane; _tier = tier; _center = center; _lifetime = lifetime; _slams = slams; }

    private float Height => _twist switch
    {
        JumpTwist.Rings or JumpTwist.Crossfire => JumpTuning.RingHalfHeight,
        JumpTwist.TallWave => JumpTuning.TallHalfHeight,
    };
    private ArcadePoint Center(int ordinal) => _twist switch
    {
        JumpTwist.Rings => _center,
        JumpTwist.Crossfire => new ArcadePoint(_center.X + (ordinal % 2 == 0 ? -1 : 1) * JumpTuning.CrossfireOffset, _center.Y, _center.Z),
        JumpTwist.TallWave => new ArcadePoint(_center.X, _center.Y - JumpTuning.RingHalfHeight + JumpTuning.TallHalfHeight, _center.Z),
    };
    private float Speed => JumpTuning.RingSpeed[(int)_tier];

    internal static JumpChannel Start(ChannelSetup setup) => Start(setup, JumpTuning.FirstSlamTicks);

    internal static JumpChannel Start(ChannelSetup setup, ushort firstSlamTicks)
    {
        var twist = (JumpTwist)setup.Draw.Twist.Value;
        int tier = (int)setup.Tier;
        var stage = setup.Stage;
        var center = new ArcadePoint(MayhemMath.CenterX(stage), stage.DeckTop + JumpTuning.RingHalfHeight, MayhemMath.CenterZ(stage));
        ushort lifetime = Lifetime(stage, new ArcadePoint(center.X + (twist == JumpTwist.Crossfire ? JumpTuning.CrossfireOffset : 0), center.Y, center.Z), JumpTuning.RingSpeed[tier]);
        ushort pair = twist == JumpTwist.Rings ? JumpTuning.PairGapTicks[tier] : (ushort)0;
        long last = (long)setup.Deadline - JumpTuning.EndMarginTicks - lifetime;
        var slams = new System.Collections.Generic.List<uint>();
        for (long at = setup.PlayStart + firstSlamTicks; at <= last && slams.Count < JumpTuning.MaxSlams;)
        {
            slams.Add((uint)at);
            if (twist == JumpTwist.Crossfire) slams.Add((uint)at);
            if (twist == JumpTwist.TallWave) break;
            if (pair > 0 && at + pair <= last) { at += pair; slams.Add((uint)at); }
            at += JumpTuning.GroupGapTicks;
        }
        return new JumpChannel(twist, setup.Lane, setup.Tier, center, lifetime, slams.ToArray());
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
                hazards.Ring(_lane, (ushort)i, Center(i), Speed, JumpTuning.RingHalfWidth, Height, _lifetime);
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
        for (int i = 0; i < _slams.Length; i++)
        {
            var center = Center(i);
            float distance = MathF.Sqrt(MayhemMath.DistanceSquared(self.PX, self.PZ, center.X, center.Z))
                - probe.Self.Definition.CapsuleRadius - JumpTuning.RingHalfWidth;
            float arrival = _slams[i] + MathF.Max(0f, distance) / Speed * 60f;
            if (probe.Slip && _slams[i] == _slams[0] && probe.Seen < arrival + 2) continue;
            float until = arrival - probe.Seen;
            if (_twist == JumpTwist.TallWave)
            {
                if (until <= JumpTuning.TallJumpLeadTicks && until >= -JumpTuning.CpuHoldTicks)
                {
                    ushort token = until <= JumpTuning.TallJumpLeadTicks - JumpTuning.TallSecondJumpTicks ? (ushort)2 : (ushort)1;
                    return new(CpuAction.Jump, self.PX, self.PZ, PressToken: token);
                }
            }
            else if (until <= JumpTuning.CpuJumpLeadTicks && until >= JumpTuning.CpuJumpLeadTicks - JumpTuning.CpuHoldTicks)
                return new(CpuAction.Jump, self.PX, self.PZ);
        }
        return new(CpuAction.Brake, self.PX, self.PZ);
    }

    public ChannelScene Observe()
    {
        var rings = new RingCue[_slams.Length];
        for (int i = 0; i < _slams.Length; i++)
            rings[i] = new RingCue(_lane, (ushort)i, Center(i), _slams[i], Speed, JumpTuning.RingHalfWidth, Height, _lifetime);
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
        if (lane != context.Lane || tier != context.Tier || !center.IsFinite || count > JumpTuning.MaxSlams
            || !MayhemMath.OnDeck(context.Stage, center.X, center.Z) || lifetime == 0)
            throw SnapshotReader.Malformed("JUMP! state");
        var slams = new uint[count];
        for (int i = 0; i < count; i++)
        {
            slams[i] = r.ReadUInt();
            if (slams[i] < context.PlayStart || slams[i] >= context.Deadline) throw SnapshotReader.Malformed("JUMP! slam");
        }
        return new JumpChannel((JumpTwist)context.Draw.Twist.Value, lane, tier, center, lifetime, slams);
    }
}
