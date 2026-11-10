using System;

namespace SlopArena.Shared.Mayhem;

internal enum SumoTwist : byte { Shrink = 1, Drift = 2 }

public static class SumoTuning
{
    public const float FinalRadius = .45f, DriftDistance = 2f, SlipOvershoot = 2f;
    public const float ShoveDistance = 1.8f;
    public const ushort ShoveTailTicks = 180;
    public static readonly ChannelTiming Timing = new(90, 420);
}

internal sealed class SumoChannel : IMayhemChannel
{
    private readonly ZoneCue _ring;
    private byte _out;

    private SumoChannel(ZoneCue ring) => _ring = ring;

    private static SumoChannel Create(byte lane, uint seed, TwistId twist, MankiMayhemDefinition stage, uint from, uint until)
    {
        var center = new ArcadePoint(MayhemMath.CenterX(stage), stage.DeckTop, MayhemMath.CenterZ(stage));
        float shift = MayhemMath.Signed(MayhemMath.Sample(seed, 0x9100, 0)) < 0 ? -SumoTuning.DriftDistance : SumoTuning.DriftDistance;
        var end = (SumoTwist)twist.Value switch
        {
            SumoTwist.Shrink => center,
            SumoTwist.Drift => new ArcadePoint(center.X + shift, center.Y, center.Z),
        };
        float radius = MathF.Sqrt((stage.MaxX - stage.MinX) * (stage.MaxX - stage.MinX)
            + (stage.MaxZ - stage.MinZ) * (stage.MaxZ - stage.MinZ)) / 2;
        return new(new(lane, 0, MayhemZoneKind.SumoRing, center, radius, from, until, end, SumoTuning.FinalRadius, from));
    }

    internal static SumoChannel Start(ChannelSetup setup)
        => Create(setup.Lane, setup.Seed, setup.Draw.Twist, setup.Stage, setup.PlayStart, setup.Deadline);

    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        if (tick.Moment != ChannelMoment.AfterCombat || tick.Phase != MayhemPhase.Play) return;
        foreach (var fighter in tick.Frame.Fighters)
            if (fighter.Live && !_ring.Contains(fighter.State.PX, fighter.State.PZ, tick.Tick)) _out |= (byte)(1 << fighter.Seat);
    }

    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards) => ChannelContactResult.Absorbed;
    public MayhemJudgement Judge(in ContestantProbe probe) => (_out >> probe.Fighter.Seat & 1) != 0
        ? MayhemJudgement.FailNow(FailCause.OutOfRing, SplatStyle.Fallen) : MayhemJudgement.Ready;

    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var center = _ring.CenterAt(Math.Min(_ring.ActiveUntil - 1, probe.Seen));
        var self = probe.Self.State;
        if (probe.Slip) return new(CpuAction.Move, center.X + _ring.RadiusAt(probe.Seen) + SumoTuning.SlipOvershoot, center.Z);
        if (probe.Frame.FriendlyFire == FriendlyFireMode.Shoves && probe.Seen + SumoTuning.ShoveTailTicks < _ring.ActiveUntil)
        foreach (var rival in probe.Frame.Fighters)
        {
            if (!rival.Live || rival.Seat == probe.Self.Seat) continue;
            if (MayhemMath.DistanceSquared(self.PX, self.PZ, center.X, center.Z)
                >= MayhemMath.DistanceSquared(rival.State.PX, rival.State.PZ, center.X, center.Z)) continue;
            if (MayhemMath.DistanceSquared(self.PX, self.PZ, rival.State.PX, rival.State.PZ) < SumoTuning.ShoveDistance * SumoTuning.ShoveDistance)
                return new(CpuAction.Normal, rival.State.PX, rival.State.PZ, AbilitySlots.Normal1, true);
        }
        return new(CpuAction.Move, center.X, center.Z);
    }

    public ChannelScene Observe() => ChannelScene.Empty with { Zones = new[] { _ring } };
    public void WriteSnapshot(SnapshotWriter w) { w.WriteByte(_ring.Lane); w.WriteByte(_out); }
    internal static SumoChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte(), mask = r.ReadByte();
        if (lane != context.Lane || (mask & ~context.OnAir) != 0) throw SnapshotReader.Malformed("SUMO! state");
        var channel = Create(lane, context.Seed, context.Draw.Twist, context.Stage, context.PlayStart, context.Deadline);
        channel._out = mask;
        return channel;
    }
}
