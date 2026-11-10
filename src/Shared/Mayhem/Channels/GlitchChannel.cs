using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

internal enum GlitchTwist : byte { Scatter = 1, Sweep = 2, Checker = 3 }

public static class GlitchTuning
{
    public static readonly ChannelTiming Timing = new(90, 480);
    public const int Columns = 4, Rows = 4, Rounds = 3;
    public const ushort LeadIn = 12, ArmedTicks = 12, TailTicks = 24;
    public const float EarlyWeight = 1.3f, LateWeight = .7f;
}

internal sealed class GlitchChannel : IMayhemChannel
{
    private readonly byte _lane;
    private readonly uint _from, _until;
    private readonly ushort _lead;
    private readonly TwistId _twist;
    private readonly MankiMayhemDefinition _stage;
    private readonly ushort _scatterMask;
    private readonly bool _sweepBack;
    private int _round;
    private byte _caught;

    private GlitchChannel(byte lane, uint seed, TwistId twist, MankiMayhemDefinition stage, uint from, uint until, ushort lead)
    {
        _lane = lane; _twist = twist; _stage = stage; _from = from; _until = until; _lead = lead;
        var cells = new int[GlitchTuning.Columns * GlitchTuning.Rows];
        for (int i = 0; i < cells.Length; i++) cells[i] = i;
        MayhemMath.Shuffle(cells, seed, 0x9300);
        for (int i = 0; i < cells.Length / 2; i++) _scatterMask |= (ushort)(1 << cells[i]);
        _sweepBack = MayhemMath.Signed(MayhemMath.Sample(seed, 0x9301, 0)) < 0;
    }
    internal static GlitchChannel Start(ChannelSetup setup) => Start(setup, GlitchTuning.LeadIn);
    internal static GlitchChannel Start(ChannelSetup setup, ushort lead)
        => new(setup.Lane, setup.Seed, setup.Draw.Twist, setup.Stage, setup.PlayStart, setup.Deadline, lead);

    internal (uint Warning, uint Armed, uint End) Window(int round)
    {
        uint start = _from + _lead;
        int budget = (int)(_until - start) - GlitchTuning.TailTicks;
        float weights = GlitchTuning.Rounds * (GlitchTuning.EarlyWeight + GlitchTuning.LateWeight) / 2;
        for (int i = 0; i <= round; i++)
        {
            float weight = GlitchTuning.EarlyWeight + (GlitchTuning.LateWeight - GlitchTuning.EarlyWeight) * i / (GlitchTuning.Rounds - 1);
            uint end = start + (uint)(budget * weight / weights);
            if (i == round) return (start, end - GlitchTuning.ArmedTicks, end);
            start = end;
        }
        throw new ArgumentOutOfRangeException(nameof(round));
    }
    /// <summary>The band crosses once, so every column arms in exactly one round and the gap is always a short hop away.</summary>
    private static int SweepRound(int column) => (column * GlitchTuning.Rounds + GlitchTuning.Rounds / 2) / GlitchTuning.Columns;
    private bool Selected(int cell, int round)
    {
        int x = cell % GlitchTuning.Columns, z = cell / GlitchTuning.Columns;
        return (GlitchTwist)_twist.Value switch
        {
            GlitchTwist.Scatter => ((_scatterMask >> cell & 1) ^ (round & 1)) == 0,
            GlitchTwist.Sweep => SweepRound(_sweepBack ? GlitchTuning.Columns - 1 - x : x) == round,
            GlitchTwist.Checker => (x + z + round) % 2 == 0,
        };
    }
    private ZoneCue Panel(int cell, int round)
    {
        float width = (_stage.MaxX - _stage.MinX) / GlitchTuning.Columns;
        float depth = (_stage.MaxZ - _stage.MinZ) / GlitchTuning.Rows;
        var center = new ArcadePoint(_stage.MinX + (cell % GlitchTuning.Columns + .5f) * width,
            _stage.DeckTop, _stage.MinZ + (cell / GlitchTuning.Columns + .5f) * depth);
        var window = Window(round);
        return new(_lane, (ushort)(round * GlitchTuning.Columns * GlitchTuning.Rows + cell), MayhemZoneKind.GlitchPanel,
            center, width / 2, window.Warning, window.End, center, width / 2, window.Armed, depth / 2, depth / 2);
    }
    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        if (tick.Moment == ChannelMoment.AfterCombat && tick.Phase == MayhemPhase.Play)
            foreach (var fighter in tick.Frame.Fighters)
                if (fighter.Live && Judge(new(fighter, tick.Frame, false)).Kind == JudgementKind.FailNow)
                    _caught |= (byte)(1 << fighter.Seat);
        if (tick.Moment != ChannelMoment.BeforeActions) return;
        while (_round < GlitchTuning.Rounds && tick.Tick >= Window(_round).End) _round++;
    }
    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards) => ChannelContactResult.Absorbed;
    public MayhemJudgement Judge(in ContestantProbe probe)
    {
        if ((_caught >> probe.Fighter.Seat & 1) != 0) return MayhemJudgement.FailNow(FailCause.Glitched, SplatStyle.Fallen);
        if (_round < GlitchTuning.Rounds && probe.Fighter.State.IsGrounded)
        for (int cell = 0; cell < GlitchTuning.Columns * GlitchTuning.Rows; cell++)
        {
            var panel = Panel(cell, _round);
            if (Selected(cell, _round) && panel.ArmedAt(probe.Frame.Tick)
                && panel.Contains(probe.Fighter.State.PX, probe.Fighter.State.PZ, probe.Frame.Tick))
                return MayhemJudgement.FailNow(FailCause.Glitched, SplatStyle.Fallen);
        }
        return MayhemJudgement.Ready;
    }
    internal bool Safe(float x, float z, uint tick)
    {
        if (_round >= GlitchTuning.Rounds || tick >= Window(_round).End) return true;
        for (int cell = 0; cell < GlitchTuning.Columns * GlitchTuning.Rows; cell++)
            if (Selected(cell, _round) && Panel(cell, _round).Contains(x, z, tick)) return false;
        return true;
    }
    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self.State;
        if (_round >= GlitchTuning.Rounds || probe.Seen < Window(_round).Warning) return new(CpuAction.Brake, self.PX, self.PZ);
        if (!probe.Slip && Safe(self.PX, self.PZ, probe.Tick)) return new(CpuAction.Brake, self.PX, self.PZ);
        float best = float.MaxValue; ArcadePoint target = default;
        for (int cell = 0; cell < GlitchTuning.Columns * GlitchTuning.Rows; cell++)
        {
            if (Selected(cell, _round) != probe.Slip) continue;
            var center = Panel(cell, _round).Center;
            float distance = MayhemMath.DistanceSquared(self.PX, self.PZ, center.X, center.Z);
            if (distance < best) { best = distance; target = center; }
        }
        return new(CpuAction.Move, target.X, target.Z);
    }
    public ChannelScene Observe()
    {
        var zones = new List<ZoneCue>();
        if (_round < GlitchTuning.Rounds)
            for (int cell = 0; cell < GlitchTuning.Columns * GlitchTuning.Rows; cell++)
                if (Selected(cell, _round)) zones.Add(Panel(cell, _round));
        return ChannelScene.Empty with { Zones = zones.ToArray() };
    }
    public void WriteSnapshot(SnapshotWriter w)
    { w.WriteByte(_lane); w.WriteUShort(_lead); w.WriteByte((byte)_round); w.WriteByte(_caught); }
    internal static GlitchChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte(); ushort lead = r.ReadUShort(); byte round = r.ReadByte(), caught = r.ReadByte();
        if (lane != context.Lane || lead >= context.Deadline - context.PlayStart - GlitchTuning.TailTicks
            || round > GlitchTuning.Rounds || (caught & ~context.OnAir) != 0) throw SnapshotReader.Malformed("GLITCH! state");
        var channel = new GlitchChannel(lane, context.Seed, context.Draw.Twist, context.Stage, context.PlayStart, context.Deadline, lead);
        channel._round = round; channel._caught = caught;
        return channel;
    }
}
