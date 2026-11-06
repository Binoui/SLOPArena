using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

internal enum SpotTwist : byte { Still = 1, Drifting = 2, Musical = 3 }

/// <summary>SPOT! tuning, indexed by speed tier. Provisional.</summary>
public static class SpotTuning
{
    public static readonly float[] Radius = { 1.8f, 1.6f, 1.4f };
    /// <summary>A spotlight's edge keeps this far from every start, so standing still never passes.</summary>
    public const float StartClearance = 1f;
    public const float MinSeparation = 4f;
    public const float DriftDistance = 3f;
    public const int MusicalRounds = 3;
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
    private readonly ZoneCue[] _zones;

    private SpotChannel(byte lane, float radius, uint from, uint until, ZoneCue[] zones)
    { _lane = lane; _radius = radius; _from = from; _until = until; _zones = zones; }

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
        var twist = (SpotTwist)setup.Draw.Twist.Value;
        int rounds = twist switch { SpotTwist.Still or SpotTwist.Drifting => 1, SpotTwist.Musical => SpotTuning.MusicalRounds };
        var zones = new ZoneCue[spots.Count * rounds];
        float shift = MayhemMath.Signed(MayhemMath.Sample(setup.Seed, 0x8400, 0)) < 0 ? -SpotTuning.DriftDistance : SpotTuning.DriftDistance;
        for (int round = 0; round < rounds; round++)
        for (int i = 0; i < spots.Count; i++)
        {
            var start = spots[i];
            var end = twist switch
            {
                SpotTwist.Still => start,
                SpotTwist.Drifting => MayhemMath.Clamp(stage, start.X + shift, start.Z, margin),
                SpotTwist.Musical => MayhemMath.Clamp(stage, start.X + (round == 0 ? 0 : round == 1 ? shift : -shift), start.Z, margin),
            };
            uint from = round == 0 ? setup.CallStart : setup.PlayStart + (uint)((setup.Deadline - setup.PlayStart) * round / rounds);
            uint until = round == rounds - 1 ? setup.Deadline : setup.PlayStart + (uint)((setup.Deadline - setup.PlayStart) * (round + 1) / rounds);
            zones[round * spots.Count + i] = new ZoneCue(setup.Lane, (ushort)(round * spots.Count + i), MayhemZoneKind.Spotlight,
                twist == SpotTwist.Musical ? end : start, radius, from, until, end, radius,
                twist == SpotTwist.Musical && round != rounds - 1 ? until : from);
        }
        return new SpotChannel(setup.Lane, radius, setup.CallStart, setup.Deadline, zones);
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
        foreach (var zone in _zones)
            if (zone.ArmedAt(probe.AtDeadline ? _until - 1 : probe.Frame.Tick) && Holder(zone, probe.Frame, probe.AtDeadline ? _until - 1 : probe.Frame.Tick) == probe.Fighter.Seat) return MayhemJudgement.Ready;
        return MayhemJudgement.Incomplete(FailCause.OutsideSpot);
    }

    private int Holder(ZoneCue zone, MayhemFrame frame, uint tick)
    {
        var spot = zone.CenterAt(tick);
        int holder = -1;
        float best = zone.RadiusAt(tick) * zone.RadiusAt(tick);
        foreach (var fighter in frame.Fighters)
        {
            if (!fighter.Live) continue;
            if (!zone.Contains(fighter.State.PX, fighter.State.PZ, tick)) continue;
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
        foreach (var zone in _zones)
        {
            uint seen = Math.Min(_until - 1, probe.Seen);
            if (!zone.ActiveAt(seen)) continue;
            var spot = zone.CenterAt(seen);
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
        var target = probe.Slip ? contested ?? best ?? _zones[0].CenterAt(probe.Seen) : best ?? contested ?? _zones[0].CenterAt(probe.Seen);
        return new(CpuAction.Move, target.X, target.Z);
    }

    public ChannelScene Observe() => ChannelScene.Empty with { Zones = (ZoneCue[])_zones.Clone() };

    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteByte(_lane); w.WriteFloat(_radius); w.WriteUInt(_from); w.WriteUInt(_until);
        w.WriteByte((byte)_zones.Length);
        foreach (var zone in _zones)
        {
            w.WriteFloat(zone.Center.X); w.WriteFloat(zone.Center.Y); w.WriteFloat(zone.Center.Z);
            w.WriteFloat(zone.EndCenter.X); w.WriteFloat(zone.EndCenter.Y); w.WriteFloat(zone.EndCenter.Z);
            w.WriteFloat(zone.EndRadius); w.WriteUInt(zone.ActiveFrom); w.WriteUInt(zone.ActiveUntil); w.WriteUInt(zone.ArmedFrom);
        }
    }

    internal static SpotChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte();
        float radius = r.ReadFloat();
        uint from = r.ReadUInt(), until = r.ReadUInt();
        int count = r.ReadByte();
        if (lane != context.Lane || !float.IsFinite(radius) || radius <= 0 || count < 1 || count > SpotTuning.MaxSpots * SpotTuning.MusicalRounds
            || until != context.Deadline || from >= until) throw SnapshotReader.Malformed("SPOT! state");
        var zones = new ZoneCue[count];
        for (int i = 0; i < count; i++)
        {
            var center = new ArcadePoint(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
            var end = new ArcadePoint(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
            float endRadius = r.ReadFloat();
            uint activeFrom = r.ReadUInt(), activeUntil = r.ReadUInt(), armed = r.ReadUInt();
            if (!center.IsFinite || !end.IsFinite || !MayhemMath.OnDeck(context.Stage, center.X, center.Z)
                || !MayhemMath.OnDeck(context.Stage, end.X, end.Z) || !float.IsFinite(endRadius) || endRadius <= 0
                || activeFrom < from || activeUntil > until || activeUntil <= activeFrom || armed < activeFrom || armed > activeUntil)
                throw SnapshotReader.Malformed("SPOT! spotlight");
            zones[i] = new ZoneCue(lane, (ushort)i, MayhemZoneKind.Spotlight, center, radius, activeFrom, activeUntil, end, endRadius, armed);
        }
        return new SpotChannel(lane, radius, from, until, zones);
    }
}
