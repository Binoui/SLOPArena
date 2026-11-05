using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

/// <summary>SPOT! tuning, indexed by speed tier. Provisional.</summary>
public static class SpotTuning
{
    public static readonly float[] Radius = { 1.8f, 1.6f, 1.4f };
    /// <summary>A spotlight's edge keeps this far from every start, so standing still never passes.</summary>
    public const float StartClearance = 1f;
    public const float MinSeparation = 4f;
    public const int Attempts = 48;
    public const int MaxSpots = 3;
}

/// <summary>
/// SPOT!: one spotlight fewer than contestants, placed away from the starts. Each spot belongs to the
/// nearest live contestant inside it, with lower seats winning ties. Shoves are allowed.
/// </summary>
internal sealed class SpotChannel : IMayhemChannel
{
    private readonly byte _lane;
    private readonly float _radius;
    private readonly uint _from, _until;
    private readonly ArcadePoint[] _spots;

    private SpotChannel(byte lane, float radius, uint from, uint until, ArcadePoint[] spots)
    { _lane = lane; _radius = radius; _from = from; _until = until; _spots = spots; }

    internal static SpotChannel Start(ChannelSetup setup)
    {
        var stage = setup.Stage;
        float radius = SpotTuning.Radius[(int)setup.Tier];
        int count = Math.Clamp(setup.OnAir.Count - 1, 1, SpotTuning.MaxSpots);
        var spots = new List<ArcadePoint>(count);
        float margin = radius + MayhemShowTuning.PlacementMargin;
        float maxCapsule = 0f;
        foreach (var seat in setup.OnAir) maxCapsule = MathF.Max(maxCapsule, seat.CapsuleRadius);
        float clear = radius + maxCapsule + SpotTuning.StartClearance;
        for (uint attempt = 0; spots.Count < count && attempt < SpotTuning.Attempts * count; attempt++)
        {
            uint sample = MayhemMath.Sample(setup.Seed, setup.Lane, attempt);
            float x = stage.MinX + margin + MayhemMath.Unit(sample) * (stage.MaxX - stage.MinX - 2 * margin);
            float z = stage.MinZ + margin + MayhemMath.Unit(MayhemMath.Sample(setup.Seed, setup.Lane + 8u, attempt))
                * (stage.MaxZ - stage.MinZ - 2 * margin);
            if (Accept(x, z, spots, setup.OnAir, clear)) spots.Add(new ArcadePoint(x, stage.DeckTop, z));
        }
        for (int i = 0; spots.Count < count; i++)
        {
            float x = MayhemMath.CenterX(stage) + (i % 2 == 0 ? -1 : 1) * (i / 2) * (2 * radius + .5f);
            spots.Add(new ArcadePoint(x, stage.DeckTop, MayhemMath.CenterZ(stage)));
        }
        return new SpotChannel(setup.Lane, radius, setup.CallStart, setup.Deadline, spots.ToArray());
    }

    private static bool Accept(float x, float z, List<ArcadePoint> spots, IReadOnlyList<ChannelSeat> seats, float clear)
    {
        foreach (var seat in seats)
            if (MayhemMath.DistanceSquared(x, z, seat.Start.X, seat.Start.Z) < clear * clear) return false;
        foreach (var spot in spots)
            if (MayhemMath.DistanceSquared(x, z, spot.X, spot.Z) < SpotTuning.MinSeparation * SpotTuning.MinSeparation) return false;
        return true;
    }

    public void Tick(in ChannelTick tick, MayhemHazards hazards) { }

    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards) => ChannelContactResult.Absorbed;

    public MayhemJudgement Judge(in ContestantProbe probe)
    {
        foreach (var spot in _spots)
            if (Holder(spot, probe.Frame) == probe.Fighter.Seat) return MayhemJudgement.Ready;
        return MayhemJudgement.Incomplete(FailCause.OutsideSpot);
    }

    private int Holder(ArcadePoint spot, MayhemFrame frame)
    {
        int holder = -1;
        float best = _radius * _radius;
        foreach (var fighter in frame.Fighters)
        {
            if (!fighter.Live) continue;
            float distance = MayhemMath.DistanceSquared(fighter.State.PX, fighter.State.PZ, spot.X, spot.Z);
            if (distance < best || distance == best && (holder < 0 || fighter.Seat < holder))
            { best = distance; holder = fighter.Seat; }
        }
        return holder;
    }

    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self.State;
        ArcadePoint? best = null, contested = null;
        float bestDistance = float.MaxValue, contestedDistance = float.MaxValue;
        foreach (var spot in _spots)
        {
            float distance = MayhemMath.DistanceSquared(self.PX, self.PZ, spot.X, spot.Z);
            bool rivalWins = false;
            foreach (var rival in probe.Frame.Fighters)
            {
                if (!rival.Live || rival.Seat == probe.Self.Seat) continue;
                float other = MayhemMath.DistanceSquared(rival.State.PX, rival.State.PZ, spot.X, spot.Z);
                if (other < distance || other == distance && rival.Seat < probe.Self.Seat) rivalWins = true;
            }
            if (rivalWins && distance < contestedDistance) { contested = spot; contestedDistance = distance; }
            if (!rivalWins && distance < bestDistance) { best = spot; bestDistance = distance; }
        }
        var target = probe.Slip ? contested ?? best ?? _spots[0] : best ?? contested ?? _spots[0];
        return new(CpuAction.Move, target.X, target.Z);
    }

    public ChannelScene Observe()
    {
        var zones = new ZoneCue[_spots.Length];
        for (int i = 0; i < _spots.Length; i++)
            zones[i] = new ZoneCue(_lane, (ushort)i, MayhemZoneKind.Spotlight, _spots[i], _radius, _from, _until);
        return ChannelScene.Empty with { Zones = zones };
    }

    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteByte(_lane); w.WriteFloat(_radius); w.WriteUInt(_from); w.WriteUInt(_until);
        w.WriteByte((byte)_spots.Length);
        foreach (var spot in _spots) { w.WriteFloat(spot.X); w.WriteFloat(spot.Y); w.WriteFloat(spot.Z); }
    }

    internal static SpotChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte();
        float radius = r.ReadFloat();
        uint from = r.ReadUInt(), until = r.ReadUInt();
        int count = r.ReadByte();
        if (lane > MayhemEvent.MaxLane || !float.IsFinite(radius) || radius <= 0 || count is < 1 or > SpotTuning.MaxSpots
            || until != context.Deadline || from >= until)
            throw SnapshotReader.Malformed("SPOT! state");
        var spots = new ArcadePoint[count];
        for (int i = 0; i < count; i++)
        {
            spots[i] = new ArcadePoint(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
            if (!spots[i].IsFinite || !MayhemMath.OnDeck(context.Stage, spots[i].X, spots[i].Z)) throw SnapshotReader.Malformed("SPOT! spotlight");
        }
        return new SpotChannel(lane, radius, from, until, spots);
    }
}
