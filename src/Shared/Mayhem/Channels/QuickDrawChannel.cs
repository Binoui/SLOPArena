using System;
using System.Linq;

namespace SlopArena.Shared.Mayhem;

internal enum QuickDrawTwist : byte { Showdown = 1, FakeOut = 2, Duel = 3 }
public static class QuickDrawTuning
{
    public static readonly ChannelTiming Timing = new(90, 300);
    public const ushort FirstFakeTicks = 40, FakeDurationTicks = 24, DrawJitterTicks = 40, DrawLeadTicks = 120, SecondFakeTicks = 80, SlipEarlyTicks = 60;
}

internal sealed class QuickDrawChannel : IMayhemChannel
{
    private readonly ChannelSetup _setup;
    private readonly WatchCue[] _watches;
    private readonly uint[] _first = new uint[4];
    private readonly ushort[] _before = new ushort[4];
    private byte _misfire;
    private uint DrawTick => _watches[^1].StartTick;
    private QuickDrawChannel(ChannelSetup setup)
    {
        _setup = setup;
        uint draw = setup.PlayStart + QuickDrawTuning.DrawLeadTicks
            + MayhemMath.Sample(setup.Seed, 0xA100, 0) % QuickDrawTuning.DrawJitterTicks;
        bool fake = (QuickDrawTwist)setup.Draw.Twist.Value switch
        { QuickDrawTwist.Showdown or QuickDrawTwist.Duel => false, QuickDrawTwist.FakeOut => true };
        _watches = fake ? new[] {
            new WatchCue(setup.Lane, 0, setup.PlayStart + QuickDrawTuning.FirstFakeTicks, setup.PlayStart + QuickDrawTuning.FirstFakeTicks,
                setup.PlayStart + QuickDrawTuning.FirstFakeTicks + QuickDrawTuning.FakeDurationTicks, MayhemWatchKind.FakeDraw, "mayhem.draw.drawer"),
            new WatchCue(setup.Lane, 1, setup.PlayStart + QuickDrawTuning.SecondFakeTicks, setup.PlayStart + QuickDrawTuning.SecondFakeTicks, setup.PlayStart + QuickDrawTuning.SecondFakeTicks + QuickDrawTuning.FakeDurationTicks, MayhemWatchKind.FakeDraw, "mayhem.draw.drawl"),
            new WatchCue(setup.Lane, 2, draw, draw, setup.Deadline, MayhemWatchKind.Draw, "mayhem.draw.draw") }
            : new[] { new WatchCue(setup.Lane, 0, draw, draw, setup.Deadline, MayhemWatchKind.Draw, "mayhem.draw.draw") };
    }
    internal static QuickDrawChannel Start(ChannelSetup setup) => new(setup);
    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        foreach (var f in tick.Frame.Fighters)
        {
            int seat = f.Seat;
            if (tick.Moment == ChannelMoment.BeforeActions) { _before[seat] = f.State.AttackSequence; continue; }
            if (tick.Phase != MayhemPhase.Play || !f.Live || _first[seat] != 0 || (_misfire >> seat & 1) != 0
                || f.State.AttackSequence == _before[seat]) continue;
            if (tick.Tick < DrawTick) _misfire |= (byte)(1 << seat);
            else _first[seat] = tick.Tick;
        }
    }
    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards) => ChannelContactResult.Absorbed;
    public MayhemJudgement Judge(in ContestantProbe probe)
    {
        int seat = probe.Fighter.Seat;
        if ((_misfire >> seat & 1) != 0) return MayhemJudgement.FailNow(FailCause.Misfire, SplatStyle.Dizzy);
        if (_first[seat] == 0) return MayhemJudgement.Incomplete(FailCause.TooSlow);
        if (probe.AtDeadline && _setup.Draw.Twist.Value == (byte)QuickDrawTwist.Duel)
        {
            foreach (var f in probe.Frame.Fighters)
                if (f.Live && _first[f.Seat] == 0) return MayhemJudgement.Ready;
            uint max = _first.Max();
            if (_first[seat] == max && _first.Any(tick => tick != 0 && tick < max)) return MayhemJudgement.Incomplete(FailCause.TooSlow);
        }
        return MayhemJudgement.Ready;
    }
    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        if (_first[probe.Self.Seat] != 0 || (_misfire >> probe.Self.Seat & 1) != 0) return MayhemCpuIntent.Wait;
        // The adapter delays this intent. Only a word already on screen can trigger a reaction.
        bool swing = probe.Slip ? probe.Tick >= _watches[0].StartTick - (_watches.Length == 1 ? (uint)QuickDrawTuning.SlipEarlyTicks : 0u) : probe.Tick >= DrawTick;
        return swing ? new(CpuAction.Normal, probe.Self.State.PX, probe.Self.State.PZ + 1, AbilitySlots.Normal1) : MayhemCpuIntent.Wait;
    }
    public ChannelScene Observe() => ChannelScene.Empty with { Watches = (WatchCue[])_watches.Clone() };
    public void WriteSnapshot(SnapshotWriter w)
    { w.WriteByte(_setup.Lane); w.WriteByte(_misfire); foreach (uint tick in _first) w.WriteUInt(tick); }
    internal static QuickDrawChannel Read(SnapshotReader r, ChannelRestore c)
    {
        if (r.ReadByte() != c.Lane) throw SnapshotReader.Malformed("QUICK DRAW! lane");
        var channel = Start(new(c.Airing, c.Episode, c.Lane, c.Tier, 0, c.PlayStart, c.Deadline, c.Stage, Array.Empty<ChannelSeat>()));
        channel._misfire = r.ReadByte();
        if ((channel._misfire & ~c.OnAir) != 0) throw SnapshotReader.Malformed("QUICK DRAW! mask");
        for (int i = 0; i < 4; i++)
        {
            uint at = r.ReadUInt();
            if (at != 0 && (i >= c.Roster.Count || (c.OnAir >> i & 1) == 0 || at < channel.DrawTick || at >= c.Deadline
                || (channel._misfire >> i & 1) != 0)) throw SnapshotReader.Malformed("QUICK DRAW! swing");
            channel._first[i] = at;
        }
        return channel;
    }
}
