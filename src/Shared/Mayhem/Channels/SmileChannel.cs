using System;

namespace SlopArena.Shared.Mayhem;

internal enum SmileTwist : byte { Squeeze = 1, Pan = 2 }

public static class SmileTuning
{
    public const float HalfWidth = 2.75f, HalfDepth = 2.75f, OffsetX = 1.5f, OffsetZ = 0f;
    public const ushort FlashTail = 20;
    public static readonly ChannelTiming Timing = new(90, 420);
}

internal sealed class SmileChannel : IMayhemChannel
{
    private readonly ZoneCue _frame;
    private bool _taken;
    private byte _captured;

    private SmileChannel(ZoneCue frame) => _frame = frame;
    private static SmileChannel Create(byte lane, uint seed, TwistId twist, MankiMayhemDefinition stage, uint from, uint until)
    {
        float sign = MayhemMath.Signed(MayhemMath.Sample(seed, 0x9200, 0)) < 0 ? -1 : 1;
        var end = new ArcadePoint(MayhemMath.CenterX(stage) + sign * SmileTuning.OffsetX, stage.DeckTop,
            MayhemMath.CenterZ(stage) + SmileTuning.OffsetZ);
        var start = (SmileTwist)twist.Value switch
        {
            SmileTwist.Squeeze => end,
            SmileTwist.Pan => new ArcadePoint(end.X - sign * SmileTuning.OffsetX * 2, end.Y, end.Z),
        };
        uint flash = until - SmileTuning.FlashTail;
        return new(new(lane, 0, MayhemZoneKind.SelfieFrame, start, stage.MaxX - stage.MinX, from, flash + 1,
            end, SmileTuning.HalfWidth, flash, stage.MaxZ - stage.MinZ, SmileTuning.HalfDepth));
    }
    internal static SmileChannel Start(ChannelSetup setup)
        => Create(setup.Lane, setup.Seed, setup.Draw.Twist, setup.Stage, setup.PlayStart, setup.Deadline);
    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        if (_taken || tick.Moment != ChannelMoment.AfterCombat || tick.Phase != MayhemPhase.Play || tick.Tick != _frame.ArmedFrom) return;
        _taken = true;
        foreach (var fighter in tick.Frame.Fighters)
            if (fighter.Live && _frame.Contains(fighter.State.PX, fighter.State.PZ, tick.Tick)) _captured |= (byte)(1 << fighter.Seat);
    }
    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards) => ChannelContactResult.Absorbed;
    public MayhemJudgement Judge(in ContestantProbe probe) => !_taken ? MayhemJudgement.Incomplete(FailCause.OutOfFrame)
        : (_captured >> probe.Fighter.Seat & 1) != 0 ? MayhemJudgement.Ready
        : MayhemJudgement.FailNow(FailCause.OutOfFrame, SplatStyle.Dizzy);
    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var center = _frame.CenterAt(probe.Slip ? _frame.ActiveFrom : _frame.ArmedFrom);
        return new(CpuAction.Move, center.X, probe.Slip ? center.Z - SmileTuning.HalfDepth * 2 : center.Z);
    }
    public ChannelScene Observe() => ChannelScene.Empty with { Zones = new[] { _frame } };
    public void WriteSnapshot(SnapshotWriter w) { w.WriteByte(_frame.Lane); w.WriteBool(_taken); w.WriteByte(_captured); }
    internal static SmileChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte(); bool taken = r.ReadBool(); byte mask = r.ReadByte();
        if (lane != context.Lane || (mask & ~context.OnAir) != 0 || !taken && mask != 0) throw SnapshotReader.Malformed("SMILE! state");
        var channel = Create(lane, context.Seed, context.Draw.Twist, context.Stage, context.PlayStart, context.Deadline);
        channel._taken = taken; channel._captured = mask;
        return channel;
    }
}
