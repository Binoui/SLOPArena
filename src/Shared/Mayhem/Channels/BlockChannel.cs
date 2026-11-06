using System;
using System.Linq;

namespace SlopArena.Shared.Mayhem;

internal enum BlockTwist : byte { Barrage = 1, Waves = 2, BlockOrJump = 3 }
public static class BlockTuning
{
    public static readonly ChannelTiming Timing = new(90, 480);
    public const ushort WarningTicks = 40, FirstBlastTicks = 60, LastMarginTicks = 6, RingSlamTicks = 100;
    public const float RingSpeed = 12f;
    public const int ShieldLeadTicks = 16, ShieldTailTicks = 3;
}
internal sealed class BlockChannel : IMayhemChannel
{
    private readonly ChannelSetup _setup;
    private readonly BombCue[] _blasts;
    private readonly RingCue[] _rings;
    private byte _failed;
    private uint _tick;
    private BlockChannel(ChannelSetup setup)
    {
        _setup = setup;
        uint last = setup.Deadline - BlockTuning.LastMarginTicks;
        uint[] times = (BlockTwist)setup.Draw.Twist.Value switch
        {
            BlockTwist.Barrage => new[] { last },
            BlockTwist.Waves => new[] { setup.PlayStart + BlockTuning.FirstBlastTicks, setup.PlayStart + (setup.Deadline - setup.PlayStart) / 2, last },
            BlockTwist.BlockOrJump => new[] { setup.PlayStart + BlockTuning.FirstBlastTicks, last },
        };
        _blasts = times.Select((t, i) => new BombCue(setup.Lane, (ushort)i, false,
            new(MayhemMath.CenterX(setup.Stage), setup.Stage.DeckTop, MayhemMath.CenterZ(setup.Stage)),
            t - BlockTuning.WarningTicks, t, 0, Kind: MayhemBombKind.Barrage)).ToArray();
        var center = new ArcadePoint(MayhemMath.CenterX(setup.Stage), setup.Stage.DeckTop + JumpTuning.RingHalfHeight, MayhemMath.CenterZ(setup.Stage));
        _rings = setup.Draw.Twist.Value == (byte)BlockTwist.BlockOrJump ? new[] { new RingCue(setup.Lane, 0, center,
            setup.PlayStart + BlockTuning.RingSlamTicks, BlockTuning.RingSpeed, JumpTuning.RingHalfWidth, JumpTuning.RingHalfHeight,
            MayhemMath.RingTicks(MayhemMath.FarthestCorner(setup.Stage, center.X, center.Z) + JumpTuning.RingHalfWidth, BlockTuning.RingSpeed)) } : Array.Empty<RingCue>();
    }
    internal static BlockChannel Start(ChannelSetup setup) => new(setup);
    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        _tick = tick.Tick;
        if (tick.Phase != MayhemPhase.Play) return;
        if (tick.Moment == ChannelMoment.BeforeActions)
        {
            foreach (var ring in _rings)
                if (tick.Tick == ring.SlamTick) hazards.Ring(ring.Lane, ring.Ordinal, ring.Center, ring.Speed, ring.HalfWidth, ring.HalfHeight, ring.LifetimeTicks);
            foreach (var blast in _blasts)
            {
                if (tick.Tick == blast.LaunchTick) hazards.Cue(MayhemEventKind.Launch, blast.Lane, blast.Ordinal);
                if (tick.Tick == blast.ImpactTick) hazards.Cue(MayhemEventKind.Impact, blast.Lane, blast.Ordinal);
            }
        }
        else if (_blasts.Any(b => b.ImpactTick == _tick))
            foreach (var f in tick.Frame.Fighters)
                if (f.Live && f.State.State != ActionState.Shielding) _failed |= (byte)(1 << f.Seat);
    }
    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards) => contact.Kind == MayhemContactKind.HazardBody && contact.Lane == _setup.Lane
        ? ChannelContactResult.Fail(contact.TargetSeat, FailCause.Hazard, SplatStyle.Flattened) : ChannelContactResult.Absorbed;
    public MayhemJudgement Judge(in ContestantProbe probe) => (_failed >> probe.Fighter.Seat & 1) != 0
        ? MayhemJudgement.FailNow(FailCause.Hazard, SplatStyle.Toasted) : MayhemJudgement.Ready;
    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        foreach (var ring in _rings)
        {
            float distance = MathF.Sqrt(MayhemMath.DistanceSquared(probe.Self.State.PX, probe.Self.State.PZ, ring.Center.X, ring.Center.Z))
                - probe.Self.Definition.CapsuleRadius - ring.HalfWidth;
            float until = ring.SlamTick + MathF.Max(0, distance) / ring.Speed * 60 - probe.Seen;
            if (until <= JumpTuning.CpuJumpLeadTicks && until >= 0)
                return probe.Slip ? new(CpuAction.Shield, 0, 0) : new(CpuAction.Jump, probe.Self.State.PX, probe.Self.State.PZ);
        }
        foreach (var blast in _blasts)
            if (probe.Seen + BlockTuning.ShieldLeadTicks >= blast.ImpactTick && probe.Seen <= blast.ImpactTick + BlockTuning.ShieldTailTicks)
                return probe.Slip ? MayhemCpuIntent.Wait : new(CpuAction.Shield, 0, 0);
        return new(CpuAction.Brake, probe.Self.State.PX, probe.Self.State.PZ);
    }
    public ChannelScene Observe() => ChannelScene.Empty with { Rings = (RingCue[])_rings.Clone(),
        Bombs = _blasts.Select(b => b with { Launched = _tick >= b.LaunchTick, Exploded = _tick >= b.ImpactTick }).ToArray() };
    public void WriteSnapshot(SnapshotWriter w) { w.WriteByte(_setup.Lane); w.WriteByte(_failed); w.WriteUInt(_tick); }
    internal static BlockChannel Read(SnapshotReader r, ChannelRestore c)
    {
        byte lane = r.ReadByte(), failed = r.ReadByte(); uint tick = r.ReadUInt();
        if (lane != c.Lane || (failed & ~c.OnAir) != 0 || tick > c.Deadline) throw SnapshotReader.Malformed("BLOCK! state");
        return new(new(c.Airing, c.Episode, c.Lane, c.Tier, 0, c.PlayStart, c.Deadline, c.Stage, Array.Empty<ChannelSeat>())) { _failed = failed, _tick = tick };
    }
}
