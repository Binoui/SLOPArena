using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

internal enum BreakOutTwist : byte { CrackedWall = 1, AnyWall = 2, DoubleCage = 3 }

/// <summary>BREAK OUT! tuning. Cage geometry lives in <see cref="RuntimeSolidTuning"/>. Provisional.</summary>
public static class BreakOutTuning
{
    public static readonly ChannelTiming DoubleCageTiming = new(90, 480);
    /// <summary>The cage bomb's flight, by tier; it lands just before the deadline.</summary>
    public static readonly ushort[] FlightTicks = { 80, 70, 60 };
    public const float BlastRadius = 3.6f;
    public const ushort EndMarginTicks = 6;
    public const float OuterHalfSize = 3.1f, SideBombOffset = 1.9f;
    public const int MaxCages = 4;
    /// <summary>How far in front of the cracked wall the CPU stands to swing.</summary>
    public const float CpuStandOff = .35f;
    public const float CpuClearance = .9f;
    /// <summary>A cracked side's way out keeps this far inside the deck edge.</summary>
    public const float EscapeEdgeMargin = 1.5f;
}

/// <summary>
/// BREAK OUT!: a cage rises around every contestant and a bomb is lobbed at each cage. The cracked wall (facing
/// the deck centre) breaks in two hits of any normal, one per activation. Pass: outside your cage and unhit.
/// </summary>
internal sealed class BreakOutChannel : IMayhemChannel
{
    private sealed class Cage
    {
        public byte Seat;
        public ulong EntityId;
        public float X, Z;
        public byte CrackedSide;
        public bool Launched;
        public bool Exploded;
        public uint ImpactTick;
        public ArcadePoint Impact;
    }

    private const int AcceptedPerWall = RuntimeSolidTuning.WallHits;

    private readonly BreakOutTwist _twist;
    private readonly byte _lane;
    private readonly SpeedTier _tier;
    private readonly uint _launchTick;
    private readonly MankiMayhemDefinition _stage;
    private readonly Cage[] _cages;
    private readonly RuntimeSolidSet _walls;
    /// <summary>Activations already counted per wall slot, so one swing counts once however many hitboxes it has.</summary>
    private readonly ulong[] _accepted;

    private BreakOutChannel(BreakOutTwist twist, byte lane, SpeedTier tier, uint launchTick, MankiMayhemDefinition stage, Cage[] cages,
        RuntimeSolidSet walls, ulong[] accepted)
    {
        _twist = twist; _lane = lane; _tier = tier; _launchTick = launchTick; _stage = stage; _cages = cages; _walls = walls; _accepted = accepted;
    }

    private ushort Flight => BreakOutTuning.FlightTicks[(int)_tier];

    internal static BreakOutChannel Start(ChannelSetup setup)
    {
        var twist = (BreakOutTwist)setup.Draw.Twist.Value;
        var stage = setup.Stage;
        float cx = MayhemMath.CenterX(stage), cz = MayhemMath.CenterZ(stage);
        var cages = new Cage[Math.Min(setup.OnAir.Count, BreakOutTuning.MaxCages)];
        var walls = new RuntimeSolidSet();
        for (int i = 0; i < cages.Length; i++)
        {
            var seat = setup.OnAir[i];
            byte cracked = CrackedSide(stage, seat, setup.OnAir);
            cages[i] = new Cage { Seat = seat.Seat, EntityId = seat.EntityId, X = seat.Start.X, Z = seat.Start.Z, CrackedSide = cracked };
            walls.AddCage(seat.EntityId, new ArcadePoint(seat.Start.X, stage.DeckTop, seat.Start.Z), stage.DeckTop, i, cracked, allCracked: twist == BreakOutTwist.AnyWall);
            if (twist == BreakOutTwist.DoubleCage)
                walls.AddCage(seat.EntityId, new ArcadePoint(seat.Start.X, stage.DeckTop, seat.Start.Z), stage.DeckTop,
                    i + BreakOutTuning.MaxCages, cracked, BreakOutTuning.OuterHalfSize);
        }
        ushort flight = BreakOutTuning.FlightTicks[(int)setup.Tier];
        uint launch = setup.Deadline - BreakOutTuning.EndMarginTicks - MayhemHazardTuning.BlastTicks - 2u - flight;
        return new BreakOutChannel(twist, setup.Lane, setup.Tier, Math.Max(setup.PlayStart, launch), stage, cages, walls,
            new ulong[RuntimeSolidSet.MaximumCount * AcceptedPerWall]);
    }

    internal uint ImpactTick => _launchTick + Flight;

    /// <summary>The side whose way out lands farthest from every other cage, keeping well inside the deck.</summary>
    private static byte CrackedSide(MankiMayhemDefinition stage, ChannelSeat seat, IReadOnlyList<ChannelSeat> all)
    {
        float reach = EscapeReach(seat.CapsuleRadius);
        byte best = 0;
        float bestScore = float.NegativeInfinity;
        for (byte side = 0; side < 4; side++)
        {
            var (dx, dz) = RuntimeSolidSet.SideDirection(side);
            float x = seat.Start.X + dx * reach, z = seat.Start.Z + dz * reach;
            float score = MayhemMath.OnDeck(stage, x, z, BreakOutTuning.EscapeEdgeMargin) ? float.MaxValue : -1000f;
            foreach (var other in all)
                if (other.Seat != seat.Seat)
                    score = MathF.Min(score, MathF.Sqrt(MayhemMath.DistanceSquared(x, z, other.Start.X, other.Start.Z)));
            if (score == float.MaxValue) score = 1000f;
            if (score > bestScore) { bestScore = score; best = side; }
        }
        return best;
    }

    private static float EscapeReach(float capsuleRadius) => BreakOutTuning.BlastRadius + capsuleRadius + BreakOutTuning.CpuClearance;

    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        if (tick.Moment != ChannelMoment.BeforeActions) return;
        _walls.Step();
        if (tick.Tick + MayhemHazardTuning.WindUpTicks == _launchTick)
            for (int i = 0; i < _cages.Length; i++) hazards.Cue(MayhemEventKind.WindUp, _lane, (ushort)i);
        if (tick.Phase != MayhemPhase.Play || tick.Tick != _launchTick) return;
        for (int i = 0; i < _cages.Length; i++)
        {
            var cage = _cages[i];
            if (cage.Launched) continue;
            cage.Launched = true;
            hazards.Bomb(_lane, (ushort)i, BombLanding(cage), Flight, BreakOutTuning.BlastRadius);
        }
    }

    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards)
    {
        switch (contact.Kind)
        {
            case MayhemContactKind.HazardBody:
                return contact.Lane == _lane
                    ? ChannelContactResult.Fail(contact.TargetSeat, FailCause.Hazard, SplatStyle.Toasted)
                    : ChannelContactResult.Absorbed;
            case MayhemContactKind.BombImpact:
                if (contact.Lane != _lane || contact.Ordinal >= _cages.Length) return ChannelContactResult.Absorbed;
                var cage = _cages[contact.Ordinal];
                if (!cage.Launched || cage.Exploded) return ChannelContactResult.Absorbed;
                cage.Exploded = true; cage.ImpactTick = hazards.Tick; cage.Impact = contact.Position;
                hazards.Cue(MayhemEventKind.Impact, _lane, contact.Ordinal);
                return ChannelContactResult.Absorbed;
            case MayhemContactKind.HostBlast:
                if (_walls.Hit(contact.Target, wreck: true, out _))
                    hazards.ObjectHit(MayhemEventKind.SolidBroken, _lane, contact.Target, contact.Attacker, contact.ActivationId, 0);
                return ChannelContactResult.Absorbed;
            case MayhemContactKind.SolidHit:
                if (!RuntimeSolidSet.IsWallId(contact.Target) || !_walls.Contains(contact.Target)) return ChannelContactResult.Ignored;
                int slot = (int)(contact.Target - RuntimeSolidSet.FirstEntityId) * AcceptedPerWall;
                int free = -1;
                for (int i = 0; i < AcceptedPerWall; i++)
                {
                    if (_accepted[slot + i] == contact.ActivationId) return ChannelContactResult.Absorbed;
                    if (free < 0 && _accepted[slot + i] == 0) free = i;
                }
                if (free < 0 || !_walls.Hit(contact.Target, wreck: false, out var wall)) return ChannelContactResult.Absorbed;
                _accepted[slot + free] = contact.ActivationId;
                hazards.ObjectHit(wall.HitsLeft == 0 ? MayhemEventKind.SolidBroken : MayhemEventKind.SolidHit, _lane,
                    contact.Target, contact.Attacker, contact.ActivationId, wall.HitsLeft);
                return ChannelContactResult.Absorbed;
        }
        return ChannelContactResult.Ignored;
    }

    public MayhemJudgement Judge(in ContestantProbe probe)
    {
        var cage = CageOf(probe.Fighter.Seat);
        if (cage == null) return MayhemJudgement.Ready;
        return Inside(cage, probe.Fighter.State.PX, probe.Fighter.State.PZ)
            ? MayhemJudgement.Incomplete(FailCause.StillCaged) : MayhemJudgement.Ready;
    }

    private float CageSize => _twist switch
    {
        BreakOutTwist.CrackedWall or BreakOutTwist.AnyWall => RuntimeSolidTuning.CageHalfSize,
        BreakOutTwist.DoubleCage => BreakOutTuning.OuterHalfSize,
    };
    private bool Inside(Cage cage, float x, float z)
        => MathF.Abs(x - cage.X) < CageSize && MathF.Abs(z - cage.Z) < CageSize;
    private ArcadePoint BombLanding(Cage cage)
    {
        var (dx, dz) = RuntimeSolidSet.SideDirection(cage.CrackedSide);
        float offset = _twist == BreakOutTwist.AnyWall ? BreakOutTuning.SideBombOffset : 0;
        return new(cage.X - dx * offset, _stage.DeckTop, cage.Z - dz * offset);
    }

    private Cage? CageOf(byte seat)
    {
        foreach (var cage in _cages) if (cage.Seat == seat) return cage;
        return null;
    }

    /// <summary>Where the contestant is safe from its own cage bomb: out through the cracked side, past the blast.</summary>
    internal (float X, float Z) EscapePoint(byte seat, float capsuleRadius)
    {
        var cage = CageOf(seat)!;
        var (dx, dz) = RuntimeSolidSet.SideDirection(cage.CrackedSide);
        float reach = EscapeReach(capsuleRadius);
        var point = MayhemMath.Clamp(_stage, cage.X + dx * reach, cage.Z + dz * reach, MayhemShowTuning.PlacementMargin);
        return (point.X, point.Z);
    }

    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self.State;
        var cage = CageOf(probe.Self.Seat);
        if (cage == null) return MayhemCpuIntent.Wait;
        byte side = probe.Slip && probe.Seen < probe.Deadline - 35 ? (byte)((cage.CrackedSide + 1) % 4) : cage.CrackedSide;
        var (dx, dz) = RuntimeSolidSet.SideDirection(side);
        if (Inside(cage, self.PX, self.PZ))
        {
            int cageIndex = Array.IndexOf(_cages, cage);
            ulong wallId = WallId(side, cageIndex);
            RuntimeSolid? blocking = null;
            foreach (var wall in _walls.Walls) if (wall.EntityId == wallId && wall.Blocks) blocking = wall;
            if (blocking == null && _twist == BreakOutTwist.DoubleCage)
                foreach (var wall in _walls.Walls)
                    if (wall.EntityId == WallId(side, cageIndex + BreakOutTuning.MaxCages) && wall.Blocks) blocking = wall;
            if (blocking is { } wallToBreak)
            {
                float wallSize = MathF.Max(MathF.Abs(wallToBreak.Center.X - cage.X), MathF.Abs(wallToBreak.Center.Z - cage.Z));
                float stand = wallSize - RuntimeSolidTuning.WallHalfThickness
                    - probe.Self.Definition.CapsuleRadius - BreakOutTuning.CpuStandOff;
                float sx = cage.X + dx * stand, sz = cage.Z + dz * stand;
                float off = MathF.Sqrt(MayhemMath.DistanceSquared(self.PX, self.PZ, sx, sz));
                return off > .3f
                    ? new(CpuAction.Move, sx, sz)
                    : new(CpuAction.Normal, wallToBreak.Center.X, wallToBreak.Center.Z,
                        AbilitySlots.Normal1, Face: true);
            }
        }
        var (ex, ez) = EscapePoint(probe.Self.Seat, probe.Self.Definition.CapsuleRadius);
        float distance = MathF.Sqrt(MayhemMath.DistanceSquared(self.PX, self.PZ, ex, ez));
        return distance > .4f ? new(CpuAction.Move, ex, ez) : new(CpuAction.Brake, self.PX, self.PZ);
    }

    private static ulong WallId(int side, int cageIndex) => RuntimeSolidSet.FirstEntityId + (ulong)(cageIndex * 4 + side);

    public ChannelScene Observe()
    {
        var bombs = new BombCue[_cages.Length];
        for (int i = 0; i < _cages.Length; i++)
            bombs[i] = new BombCue(_lane, (ushort)i, _cages[i].Launched, _cages[i].Exploded ? _cages[i].Impact : BombLanding(_cages[i]),
                _launchTick, _cages[i].Exploded ? _cages[i].ImpactTick : ImpactTick, BreakOutTuning.BlastRadius, _cages[i].Exploded);
        return ChannelScene.Empty with { Bombs = bombs, Solids = _walls.Walls };
    }

    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteByte(_lane); w.WriteByte((byte)_tier); w.WriteUInt(_launchTick);
        w.WriteByte((byte)_cages.Length);
        foreach (var cage in _cages)
        {
            w.WriteByte(cage.Seat); w.WriteULong(cage.EntityId); w.WriteFloat(cage.X); w.WriteFloat(cage.Z);
            w.WriteByte(cage.CrackedSide); w.WriteBool(cage.Launched);
            w.WriteBool(cage.Exploded); w.WriteUInt(cage.ImpactTick);
            w.WriteFloat(cage.Impact.X); w.WriteFloat(cage.Impact.Y); w.WriteFloat(cage.Impact.Z);
        }
        _walls.WriteSnapshot(w);
        int used = _accepted.Length;
        while (used > 0 && _accepted[used - 1] == 0) used--;
        w.WriteByte((byte)used);
        for (int i = 0; i < used; i++) w.WriteULong(_accepted[i]);
    }

    internal static BreakOutChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte();
        var tier = (SpeedTier)r.ReadByte();
        uint launch = r.ReadUInt();
        int count = r.ReadByte();
        if (lane != context.Lane || tier != context.Tier || count > BreakOutTuning.MaxCages || count > context.Roster.Count
            || launch < context.PlayStart || launch >= context.Deadline)
            throw SnapshotReader.Malformed("BREAK OUT! state");
        byte seen = 0;
        var cages = new Cage[count];
        for (int i = 0; i < count; i++)
        {
            var cage = cages[i] = new Cage
            {
                Seat = r.ReadByte(), EntityId = r.ReadULong(), X = r.ReadFloat(), Z = r.ReadFloat(),
                CrackedSide = r.ReadByte(), Launched = r.ReadBool(),
                Exploded = r.ReadBool(), ImpactTick = r.ReadUInt(), Impact = new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()),
            };
            if (cage.Seat >= context.Roster.Count || (context.OnAir >> cage.Seat & 1) == 0 || (seen >> cage.Seat & 1) != 0 || context.Roster[cage.Seat] != cage.EntityId || cage.CrackedSide > 3
                || !cage.Impact.IsFinite || cage.Exploded && (!cage.Launched || cage.ImpactTick < launch || cage.ImpactTick >= context.Deadline)
                || !cage.Exploded && (cage.ImpactTick != 0 || (cage.Impact.X != 0 || cage.Impact.Y != 0 || cage.Impact.Z != 0))
                || !float.IsFinite(cage.X) || !float.IsFinite(cage.Z) || !MayhemMath.OnDeck(context.Stage, cage.X, cage.Z))
                throw SnapshotReader.Malformed("BREAK OUT! cage");
            seen |= (byte)(1 << cage.Seat);
        }
        if (seen != context.OnAir) throw SnapshotReader.Malformed("Missing channel objective");
        var walls = new RuntimeSolidSet();
        walls.Apply(RuntimeSolidSet.ReadSnapshot(r, context.Roster));
        int used = r.ReadByte();
        var accepted = new ulong[RuntimeSolidSet.MaximumCount * AcceptedPerWall];
        if (used > accepted.Length) throw SnapshotReader.Malformed("BREAK OUT! hits");
        for (int i = 0; i < used; i++) accepted[i] = r.ReadULong();
        return new BreakOutChannel((BreakOutTwist)context.Draw.Twist.Value, lane, tier, launch, context.Stage, cages, walls, accepted);
    }
}
