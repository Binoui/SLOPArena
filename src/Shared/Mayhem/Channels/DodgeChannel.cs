using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

internal enum DodgeTwist : byte { Aimed = 1, BombRain = 2, BigOne = 3 }

/// <summary>DODGE! tuning, indexed by speed tier. Provisional.</summary>
public static class DodgeTuning
{
    public static readonly ushort[] FlightTicks = { 66, 56, 46 };
    public static readonly ushort[] GapTicks = { 34, 26, 20 };
    public static readonly byte[] BombsPerContestant = { 2, 3, 4 };
    public const ushort FirstLaunchTicks = 12;
    /// <summary>Ticks between the last blast's end and the deadline.</summary>
    public const ushort EndMarginTicks = 6;
    public const float BlastRadius = 2.2f, AimJitter = .8f;
    public const float BigRadius = 8f, RainSpacing = 6f, RainRadius = 4.6f;
    /// <summary>Ticks between rain rows at most; a later first launch, as in a remix, packs the rows closer to fit them all.</summary>
    public const ushort RainRowGap = 80;
    public const int RainRows = 3, RainColumns = 5;
    public const int MaxBombs = 32;
    public const float CpuClearance = .8f;
}

/// <summary>
/// DODGE!: impact bombs aimed near contestants; a ring shows each landing from launch. A bomb that touches a
/// fighter or the deck explodes, and its blast fails everyone it reaches. Pass: not failed by the deadline.
/// </summary>
internal sealed class DodgeChannel : IMayhemChannel
{
    private struct Bomb
    {
        public byte TargetSeat;
        public uint LaunchTick;
        public BombPhase Phase;
        public uint ImpactTick;
        public float Y;
        public float X, Z;
    }

    private readonly DodgeTwist _twist;
    private readonly byte _lane;
    private readonly SpeedTier _tier;
    private readonly uint _seed;
    private readonly MankiMayhemDefinition _stage;
    private readonly Bomb[] _bombs;

    private DodgeChannel(DodgeTwist twist, byte lane, SpeedTier tier, uint seed, MankiMayhemDefinition stage, Bomb[] bombs)
    { _twist = twist; _lane = lane; _tier = tier; _seed = seed; _stage = stage; _bombs = bombs; }

    private float Radius => _twist switch
    {
        DodgeTwist.Aimed => DodgeTuning.BlastRadius,
        DodgeTwist.BombRain => DodgeTuning.RainRadius,
        DodgeTwist.BigOne => DodgeTuning.BigRadius,
    };

    private ushort Flight => DodgeTuning.FlightTicks[(int)_tier];

    internal static DodgeChannel Start(ChannelSetup setup) => Start(setup, DodgeTuning.FirstLaunchTicks);

    /// <summary>A volley paced to the window: the last blast ends <see cref="DodgeTuning.EndMarginTicks"/> before the deadline.</summary>
    internal static DodgeChannel Start(ChannelSetup setup, ushort firstLaunchTicks)
    {
        var twist = (DodgeTwist)setup.Draw.Twist.Value;
        int tier = (int)setup.Tier;
        ushort flight = DodgeTuning.FlightTicks[tier], gap = DodgeTuning.GapTicks[tier];
        uint first = setup.PlayStart + firstLaunchTicks;
        uint lastImpactAllowed = setup.Deadline - DodgeTuning.EndMarginTicks - MayhemHazardTuning.BlastTicks - 2;
        int desired = Math.Min(DodgeTuning.MaxBombs, setup.OnAir.Count * DodgeTuning.BombsPerContestant[tier]);
        var bombs = new List<Bomb>(desired);
        if (twist != DodgeTwist.Aimed)
        {
            int rows = twist switch
            {
                DodgeTwist.Aimed => 0,
                DodgeTwist.BombRain => DodgeTuning.RainRows,
                DodgeTwist.BigOne => 1,
            };
            int gapColumn = MayhemMath.Sample(setup.Seed, 0x8100, 0) % 2 == 0 ? 1 : DodgeTuning.RainColumns - 2;
            int window = (int)lastImpactAllowed - (int)(first + flight);
            int rowTicks = rows > 1 ? Math.Clamp(window / (rows - 1), 0, DodgeTuning.RainRowGap) : DodgeTuning.RainRowGap;
            for (int row = 0; row < rows; row++)
            {
                int rowGap = gapColumn + (gapColumn == 1 ? row : -row);
                uint launch = first + (uint)(row * rowTicks);
                if (launch + flight > lastImpactAllowed) break;
                for (int column = 0; column < (twist == DodgeTwist.BigOne ? 1 : DodgeTuning.RainColumns); column++)
                {
                    if (twist == DodgeTwist.BombRain && column == rowGap) continue;
                    var point = new ArcadePoint(
                        MayhemMath.CenterX(setup.Stage) + (twist == DodgeTwist.BigOne ? 0 : (column - DodgeTuning.RainColumns / 2) * DodgeTuning.RainSpacing),
                        setup.Stage.DeckTop,
                        twist == DodgeTwist.BigOne ? MayhemMath.CenterZ(setup.Stage) : setup.Stage.MinZ + (row + .5f) * (setup.Stage.MaxZ - setup.Stage.MinZ) / rows);
                    bombs.Add(new Bomb { TargetSeat = setup.OnAir.Count == 0 ? (byte)0 : setup.OnAir[bombs.Count % setup.OnAir.Count].Seat,
                        LaunchTick = launch, X = point.X, Z = point.Z });
                }
            }
        }
        for (int i = 0; twist == DodgeTwist.Aimed && i < desired && setup.OnAir.Count > 0; i++)
        {
            uint launch = first + (uint)(i * gap);
            if (launch + flight > lastImpactAllowed) break;
            bombs.Add(new Bomb { TargetSeat = setup.OnAir[i % setup.OnAir.Count].Seat, LaunchTick = launch });
        }
        return new DodgeChannel(twist, setup.Lane, setup.Tier, setup.Seed, setup.Stage, bombs.ToArray());
    }

    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        if (tick.Moment != ChannelMoment.BeforeActions) return;
        for (int i = 0; i < _bombs.Length; i++)
        {
            ref var bomb = ref _bombs[i];
            if (tick.Tick + MayhemHazardTuning.WindUpTicks == bomb.LaunchTick) hazards.Cue(MayhemEventKind.WindUp, _lane, (ushort)i);
            if (tick.Phase != MayhemPhase.Play || bomb.Phase != BombPhase.Pending || tick.Tick != bomb.LaunchTick) continue;
            var target = tick.Frame.Fighters[bomb.TargetSeat].State;
            uint sample = MayhemMath.Sample(_seed, _lane, (uint)i);
            var aimed = MayhemMath.Clamp(_stage, target.PX + MayhemMath.Signed(sample) * DodgeTuning.AimJitter,
                target.PZ + MayhemMath.Signed(sample >> 8 | sample << 24) * DodgeTuning.AimJitter, MayhemShowTuning.PlacementMargin);
            var landing = _twist switch
            {
                DodgeTwist.Aimed => aimed,
                DodgeTwist.BombRain or DodgeTwist.BigOne => new ArcadePoint(bomb.X, _stage.DeckTop, bomb.Z),
            };
            bomb.Phase = BombPhase.InFlight; bomb.X = landing.X; bomb.Z = landing.Z;
            hazards.Bomb(_lane, (ushort)i, landing, Flight, Radius);
        }
    }

    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards)
    {
        if (contact.Lane != _lane) return ChannelContactResult.Absorbed;
        if (contact.Kind == MayhemContactKind.BombImpact && contact.Ordinal < _bombs.Length)
        {
            ref var bomb = ref _bombs[contact.Ordinal];
            if (bomb.Phase != BombPhase.InFlight) return ChannelContactResult.Absorbed;
            bomb.Phase = BombPhase.Exploded; bomb.ImpactTick = hazards.Tick;
            bomb.X = contact.Position.X; bomb.Y = contact.Position.Y; bomb.Z = contact.Position.Z;
            hazards.Cue(MayhemEventKind.Impact, _lane, contact.Ordinal);
        }
        return contact.Kind == MayhemContactKind.HazardBody
            ? ChannelContactResult.Fail(contact.TargetSeat, FailCause.Hazard, SplatStyle.Toasted)
            : ChannelContactResult.Absorbed;
    }

    public MayhemJudgement Judge(in ContestantProbe probe) => MayhemJudgement.Ready;

    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self.State;
        if (probe.Slip)
            foreach (var bomb in _bombs)
                if (_twist != DodgeTwist.Aimed || bomb.TargetSeat == probe.Self.Seat)
                {
                    if (probe.Seen <= bomb.LaunchTick + Flight + 2) return new(CpuAction.Brake, self.PX, self.PZ);
                    break;
                }
        var dangers = Dangers(probe.Tick, probe.Self.Definition.CapsuleRadius);
        return Avoid(_stage, self, dangers);
    }

    /// <summary>Move to the safest nearby point while inside a danger, else stand still.</summary>
    internal static MayhemCpuIntent Avoid(MankiMayhemDefinition stage, in CharacterState self, List<MayhemDanger> dangers)
    {
        if (!MayhemMath.Exposed(self.PX, self.PZ, dangers)) return new(CpuAction.Brake, self.PX, self.PZ);
        var safe = MayhemMath.SafestPoint(stage, self.PX, self.PZ, 1.2f, MayhemShowTuning.PlacementMargin, dangers);
        return new(CpuAction.Move, safe.X, safe.Z);
    }

    /// <summary>Blast footprints through the end of each bomb's actual explosion.</summary>
    internal List<MayhemDanger> Dangers(uint tick, float capsuleRadius)
    {
        var dangers = new List<MayhemDanger>();
        float radius = Radius + capsuleRadius + DodgeTuning.CpuClearance;
        foreach (var bomb in _bombs)
            if (bomb.Phase != BombPhase.Pending && tick <= (bomb.Phase == BombPhase.Exploded ? bomb.ImpactTick : bomb.LaunchTick + Flight) + MayhemHazardTuning.BlastTicks + 1)
                dangers.Add(new MayhemDanger(bomb.X, bomb.Z, radius));
        return dangers;
    }

    public ChannelScene Observe()
    {
        var cues = new BombCue[_bombs.Length];
        for (int i = 0; i < _bombs.Length; i++)
        {
            var bomb = _bombs[i];
            cues[i] = new BombCue(_lane, (ushort)i, bomb.Phase != BombPhase.Pending, new ArcadePoint(bomb.X, bomb.Phase == BombPhase.Exploded ? bomb.Y : _stage.DeckTop, bomb.Z),
                bomb.LaunchTick, bomb.Phase == BombPhase.Exploded ? bomb.ImpactTick : bomb.LaunchTick + Flight, Radius, bomb.Phase == BombPhase.Exploded);
        }
        return ChannelScene.Empty with { Bombs = cues };
    }

    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteByte(_lane); w.WriteByte((byte)_tier); w.WriteUInt(_seed);
        w.WriteByte((byte)_bombs.Length);
        foreach (var bomb in _bombs)
        {
            w.WriteByte(bomb.TargetSeat); w.WriteUInt(bomb.LaunchTick); w.WriteByte((byte)bomb.Phase); w.WriteUInt(bomb.ImpactTick); w.WriteFloat(bomb.Y);
            w.WriteFloat(bomb.X); w.WriteFloat(bomb.Z);
        }
    }

    internal static DodgeChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte();
        var tier = (SpeedTier)r.ReadByte();
        uint seed = r.ReadUInt();
        int count = r.ReadByte();
        if (lane != context.Lane || tier != context.Tier || seed != context.Seed || count > DodgeTuning.MaxBombs)
            throw SnapshotReader.Malformed("DODGE! state");
        var bombs = new Bomb[count];
        for (int i = 0; i < count; i++)
        {
            bombs[i] = new Bomb { TargetSeat = r.ReadByte(), LaunchTick = r.ReadUInt(), Phase = (BombPhase)r.ReadByte(), ImpactTick = r.ReadUInt(), Y = r.ReadFloat(), X = r.ReadFloat(), Z = r.ReadFloat() };
            var bomb = bombs[i];
            if (bomb.TargetSeat >= context.Roster.Count || bomb.LaunchTick < context.PlayStart || bomb.LaunchTick >= context.Deadline
                || !float.IsFinite(bomb.X) || !float.IsFinite(bomb.Y) || !float.IsFinite(bomb.Z)
                || bomb.Phase > MayhemEnumLimits.Bomb
                || (bomb.Phase == BombPhase.Exploded ? bomb.ImpactTick < bomb.LaunchTick || bomb.ImpactTick >= context.Deadline : bomb.ImpactTick != 0 || bomb.Y != 0)
                || bomb.Phase == BombPhase.InFlight && !MayhemMath.OnDeck(context.Stage, bomb.X, bomb.Z)
                || (DodgeTwist)context.Draw.Twist.Value == DodgeTwist.Aimed && bomb.Phase == BombPhase.Pending && (bomb.X != 0 || bomb.Z != 0))
                throw SnapshotReader.Malformed("DODGE! bomb");
        }
        return new DodgeChannel((DodgeTwist)context.Draw.Twist.Value, lane, tier, seed, context.Stage, bombs);
    }
}
