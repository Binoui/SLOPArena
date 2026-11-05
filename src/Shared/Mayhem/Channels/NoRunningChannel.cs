using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

/// <summary>NO RUNNING! tuning, indexed by speed tier. Provisional.</summary>
public static class NoRunningTuning
{
    /// <summary>Whistle to watch: long enough for every kit to stop from a run.</summary>
    public static readonly ushort[] WarningTicks = { 45, 38, 32 };
    public static readonly ushort[] WatchTicks = { 70, 60, 54 };
    public static readonly ushort[] CalmTicks = { 50, 40, 32 };
    public const ushort CalmJitterTicks = 24;
    public const ushort FirstWarningTicks = 40;
    public const ushort EndMarginTicks = 6;
    /// <summary>Horizontal hip travel per tick tolerated while watched: numerical jitter, not visible running.</summary>
    public const float Tolerance = .02f;
    /// <summary>A shoved fighter keeps sliding briefly after hitstun; that slide is exempt too.</summary>
    public const ushort ShoveGraceTicks = 12;
    public const int MaxWatches = 8;
    public const float CpuWanderReach = 2.5f;
}

/// <summary>
/// NO RUNNING! (guest Bashie): she whistles, turns and watches. Moving horizontally beyond the tolerance while
/// she watches fails (WRITTEN UP); displacement in hitstun, hitstop or knockback is exempt, so a shove never
/// fails anyone. Pass: never caught.
/// </summary>
internal sealed class NoRunningChannel : IMayhemChannel
{
    private readonly byte _lane;
    private readonly uint _seed;
    private readonly WatchCue[] _watches;
    private readonly MankiMayhemDefinition _stage;
    private readonly uint[] _exemptUntil;
    // Per-tick facts: captured before actions and compared after combat in the same tick, never carried across a snapshot.
    private readonly float[] _beforeX = new float[4], _beforeZ = new float[4];
    private readonly bool[] _moved = new bool[4];

    private NoRunningChannel(byte lane, uint seed, WatchCue[] watches, MankiMayhemDefinition stage, uint[] exemptUntil)
    { _lane = lane; _seed = seed; _watches = watches; _stage = stage; _exemptUntil = exemptUntil; }

    internal static NoRunningChannel Start(ChannelSetup setup)
    {
        int tier = (int)setup.Tier;
        var watches = new List<WatchCue>();
        uint warning = setup.PlayStart + NoRunningTuning.FirstWarningTicks;
        for (int i = 0; i < NoRunningTuning.MaxWatches; i++)
        {
            uint start = warning + NoRunningTuning.WarningTicks[tier], end = start + NoRunningTuning.WatchTicks[tier];
            if (end + NoRunningTuning.EndMarginTicks > setup.Deadline) break;
            watches.Add(new WatchCue(setup.Lane, (ushort)i, warning, start, end));
            warning = end + NoRunningTuning.CalmTicks[tier]
                + MayhemMath.Sample(setup.Seed, setup.Lane, (uint)i) % (NoRunningTuning.CalmJitterTicks + 1u);
        }
        return new NoRunningChannel(setup.Lane, setup.Seed, watches.ToArray(), setup.Stage, new uint[4]);
    }

    private bool Watched(uint tick)
    {
        foreach (var watch in _watches) if (tick >= watch.StartTick && tick < watch.EndTick) return true;
        return false;
    }

    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        var fighters = tick.Frame.Fighters;
        if (tick.Moment == ChannelMoment.BeforeActions)
        {
            foreach (var watch in _watches)
            {
                if (tick.Tick == watch.WarningTick) hazards.Cue(MayhemEventKind.WatchWarning, _lane, watch.Ordinal);
                if (tick.Tick == watch.StartTick) hazards.Cue(MayhemEventKind.WatchStart, _lane, watch.Ordinal);
                if (tick.Tick == watch.EndTick) hazards.Cue(MayhemEventKind.WatchEnd, _lane, watch.Ordinal);
            }
            for (int seat = 0; seat < fighters.Length; seat++)
            { _beforeX[seat] = fighters[seat].State.PX; _beforeZ[seat] = fighters[seat].State.PZ; _moved[seat] = false; }
            return;
        }
        bool watched = tick.Phase == MayhemPhase.Play && Watched(tick.Tick);
        for (int seat = 0; seat < fighters.Length; seat++)
        {
            var state = fighters[seat].State;
            if (state.HitstunTicks > 0 || state.HitstopTicks > 0 || Simulation.HasKnockback(state))
                _exemptUntil[seat] = tick.Tick + NoRunningTuning.ShoveGraceTicks;
            _moved[seat] = watched && fighters[seat].Live && tick.Tick > _exemptUntil[seat]
                && MayhemMath.DistanceSquared(state.PX, state.PZ, _beforeX[seat], _beforeZ[seat])
                    > NoRunningTuning.Tolerance * NoRunningTuning.Tolerance;
        }
    }

    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards) => ChannelContactResult.Absorbed;

    public MayhemJudgement Judge(in ContestantProbe probe)
        => !probe.AtDeadline && _moved[probe.Fighter.Seat] ? MayhemJudgement.FailNow(FailCause.Caught, SplatStyle.Dizzy) : MayhemJudgement.Ready;

    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self.State;
        if (probe.Slip && _watches.Length > 0 && probe.Seen >= _watches[0].WarningTick && probe.Seen < _watches[0].StartTick + 8)
            return new(CpuAction.Move, self.PX < MayhemMath.CenterX(_stage) ? _stage.MaxX - 2 : _stage.MinX + 2, self.PZ);
        foreach (var watch in _watches)
            if (probe.Seen + 6 >= watch.WarningTick && probe.Tick < watch.EndTick + 2) return new(CpuAction.Brake, self.PX, self.PZ);
        int window = 0;
        while (window < _watches.Length && probe.Tick >= _watches[window].EndTick) window++;
        uint sample = MayhemMath.Sample(_seed, 16u + probe.Self.Seat, (uint)window);
        var point = MayhemMath.Clamp(_stage, MayhemMath.CenterX(_stage) + MayhemMath.Signed(sample) * NoRunningTuning.CpuWanderReach * 2f,
            MayhemMath.CenterZ(_stage) + MayhemMath.Signed(sample >> 12 | sample << 20) * NoRunningTuning.CpuWanderReach * 2f,
            MayhemShowTuning.PlacementMargin + 1f);
        return new(CpuAction.Move, point.X, point.Z);
    }

    public ChannelScene Observe() => ChannelScene.Empty with { Watches = (WatchCue[])_watches.Clone() };

    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteByte(_lane); w.WriteUInt(_seed);
        w.WriteByte((byte)_watches.Length);
        foreach (var watch in _watches) { w.WriteUInt(watch.WarningTick); w.WriteUInt(watch.StartTick); w.WriteUInt(watch.EndTick); }
        foreach (uint until in _exemptUntil) w.WriteUInt(until);
    }

    internal static NoRunningChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte();
        uint seed = r.ReadUInt();
        int count = r.ReadByte();
        if (lane > MayhemEvent.MaxLane || count > NoRunningTuning.MaxWatches) throw SnapshotReader.Malformed("NO RUNNING! state");
        var watches = new WatchCue[count];
        for (int i = 0; i < count; i++)
        {
            watches[i] = new WatchCue(lane, (ushort)i, r.ReadUInt(), r.ReadUInt(), r.ReadUInt());
            var watch = watches[i];
            if (watch.WarningTick < context.PlayStart || watch.StartTick <= watch.WarningTick || watch.EndTick <= watch.StartTick
                || watch.EndTick > context.Deadline || i > 0 && watch.WarningTick < watches[i - 1].EndTick)
                throw SnapshotReader.Malformed("NO RUNNING! watch");
        }
        var exemptUntil = new uint[4];
        for (int seat = 0; seat < exemptUntil.Length; seat++)
        {
            exemptUntil[seat] = r.ReadUInt();
            if (exemptUntil[seat] > context.Deadline + NoRunningTuning.ShoveGraceTicks) throw SnapshotReader.Malformed("NO RUNNING! grace");
        }
        return new NoRunningChannel(lane, seed, watches, context.Stage, exemptUntil);
    }
}
