using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared.Mayhem;

internal enum GetHimTwist : byte { Slam = 1, Tantrum = 2 }
public static class GetHimTuning
{
    public static readonly ChannelTiming Timing = new(90, 600);
    public const byte Quota = 2;
    public const float Radius = 1.4f, HalfHeight = 1.5f, StandOff = .15f, BombSpread = 2.8f, BlastRadius = 2.4f, RingSpeed = 14f;
    public const ushort WakeTailTicks = 100, FlightTicks = 36, EndMarginTicks = 6;
    public const int BombCount = 5, SlipSwingGapTicks = 150;
    public const float RetreatReach = 8f, ArrivalTolerance = .25f;
}
internal sealed class GetHimChannel : IMayhemChannel
{
    private readonly ChannelSetup _setup;
    private readonly ulong[] _roster;
    private readonly List<ulong>[] _accepted = Enumerable.Range(0, 4).Select(_ => new List<ulong>()).ToArray();
    private readonly BombCue[] _bombs;
    private readonly RingCue[] _rings;
    private uint _tick;
    private uint Wake => _setup.Deadline - GetHimTuning.WakeTailTicks;
    private ArcadePoint Center => new(MayhemMath.CenterX(_setup.Stage), _setup.Stage.DeckTop + GetHimTuning.HalfHeight, MayhemMath.CenterZ(_setup.Stage));
    private GetHimChannel(ChannelSetup setup, ulong[] roster)
    {
        _setup = setup; _roster = roster;
        bool slam = (GetHimTwist)setup.Draw.Twist.Value switch { GetHimTwist.Slam => true, GetHimTwist.Tantrum => false };
        var center = new ArcadePoint(Center.X, setup.Stage.DeckTop + JumpTuning.RingHalfHeight, Center.Z);
        _rings = slam ? new[] { new RingCue(setup.Lane, 0, center, Wake, GetHimTuning.RingSpeed,
            JumpTuning.RingHalfWidth, JumpTuning.RingHalfHeight, MayhemMath.RingTicks(
                MayhemMath.FarthestCorner(setup.Stage, center.X, center.Z) + JumpTuning.RingHalfWidth, GetHimTuning.RingSpeed)) } : Array.Empty<RingCue>();
        _bombs = slam ? Array.Empty<BombCue>() : Enumerable.Range(0, GetHimTuning.BombCount).Select(i => {
            float angle = i * MathF.PI * 2 / GetHimTuning.BombCount;
            return new BombCue(setup.Lane, (ushort)i, false, new(Center.X + MathF.Sin(angle) * GetHimTuning.BombSpread,
                setup.Stage.DeckTop, Center.Z + MathF.Cos(angle) * GetHimTuning.BombSpread), Wake, Wake + GetHimTuning.FlightTicks, GetHimTuning.BlastRadius);
        }).ToArray();
    }
    internal static GetHimChannel Start(ChannelSetup setup)
    {
        var roster = new ulong[setup.OnAir.Count == 0 ? 0 : setup.OnAir.Max(s => s.Seat) + 1];
        foreach (var s in setup.OnAir) roster[s.Seat] = s.EntityId;
        return new(setup, roster);
    }
    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        _tick = tick.Tick;
        if (tick.Moment != ChannelMoment.BeforeActions || tick.Phase != MayhemPhase.Play) return;
        if (tick.Tick + MayhemHazardTuning.WindUpTicks == Wake) hazards.Cue(MayhemEventKind.WindUp, _setup.Lane, 0);
        if (tick.Tick != Wake) return;
        foreach (var r in _rings) hazards.Ring(r.Lane, r.Ordinal, r.Center, r.Speed, r.HalfWidth, r.HalfHeight, r.LifetimeTicks);
        foreach (var b in _bombs) hazards.Bomb(b.Lane, b.Ordinal, b.Landing, GetHimTuning.FlightTicks, b.BlastRadius);
    }
    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards)
    {
        if (contact.Kind == MayhemContactKind.HazardBody && contact.Lane == _setup.Lane)
            return ChannelContactResult.Fail(contact.TargetSeat, FailCause.Hazard, _rings.Length > 0 ? SplatStyle.Flattened : SplatStyle.Toasted);
        if (contact.Kind == MayhemContactKind.BombImpact && contact.Lane == _setup.Lane && contact.Ordinal < _bombs.Length)
            _bombs[contact.Ordinal] = _bombs[contact.Ordinal] with { Landing = contact.Position, ImpactTick = _tick, Exploded = true };
        if (contact.Kind != MayhemContactKind.SolidHit || contact.Target != MayhemEvent.FirstTargetEntityId
            || _tick < _setup.PlayStart || _tick >= Wake || contact.ActivationId == 0
            || contact.AttackSlot is not (AbilitySlots.Normal1 or AbilitySlots.Normal2 or AbilitySlots.Normal3)) return ChannelContactResult.Absorbed;
        int seat = Array.IndexOf(_roster, contact.Attacker);
        if (seat < 0 || _accepted[seat].Count >= GetHimTuning.Quota || _accepted[seat].Contains(contact.ActivationId)) return ChannelContactResult.Absorbed;
        _accepted[seat].Add(contact.ActivationId); _accepted[seat].Sort();
        byte left = (byte)(GetHimTuning.Quota - _accepted[seat].Count);
        hazards.ObjectHit(left == 0 ? MayhemEventKind.TargetBroken : MayhemEventKind.TargetHit, _setup.Lane,
            contact.Target, contact.Attacker, contact.ActivationId, left);
        return ChannelContactResult.Absorbed;
    }
    public MayhemJudgement Judge(in ContestantProbe probe) => _accepted[probe.Fighter.Seat].Count >= GetHimTuning.Quota
        ? MayhemJudgement.Ready : MayhemJudgement.Incomplete(FailCause.HeGotUp);
    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self.State;
        if (_accepted[probe.Self.Seat].Count < GetHimTuning.Quota && probe.Seen < Wake)
        {
            float dx = self.PX - Center.X, dz = self.PZ - Center.Z;
            float distance = MathF.Sqrt(dx * dx + dz * dz), stand = GetHimTuning.Radius + probe.Self.Definition.CapsuleRadius + GetHimTuning.StandOff;
            if (distance > stand + GetHimTuning.ArrivalTolerance) return new(CpuAction.Move, Center.X + dx / distance * stand, Center.Z + dz / distance * stand);
            return new(CpuAction.Normal, Center.X, Center.Z, !probe.Slip || probe.Seen % GetHimTuning.SlipSwingGapTicks == 0 ? AbilitySlots.Normal1 : AbilitySlots.None, true);
        }
        if (_rings.Length > 0)
        {
            float distance = MathF.Sqrt(MayhemMath.DistanceSquared(self.PX, self.PZ, Center.X, Center.Z)) - probe.Self.Definition.CapsuleRadius - JumpTuning.RingHalfWidth;
            float until = Wake + MathF.Max(0, distance) / GetHimTuning.RingSpeed * 60 - probe.Seen;
            if (!probe.Slip && until <= JumpTuning.CpuJumpLeadTicks && until >= 0) return new(CpuAction.Jump, self.PX, self.PZ);
            return new(CpuAction.Brake, self.PX, self.PZ);
        }
        if (probe.Slip) return new(CpuAction.Brake, self.PX, self.PZ);
        float length = MathF.Sqrt(MayhemMath.DistanceSquared(self.PX, self.PZ, Center.X, Center.Z));
        float x = length < .01f ? 1 : (self.PX - Center.X) / length, z = length < .01f ? 0 : (self.PZ - Center.Z) / length;
        var safe = MayhemMath.Clamp(_setup.Stage, Center.X + x * GetHimTuning.RetreatReach, Center.Z + z * GetHimTuning.RetreatReach, MayhemShowTuning.PlacementMargin);
        return new(CpuAction.Move, safe.X, safe.Z);
    }
    public ChannelScene Observe() => ChannelScene.Empty with {
        Targets = new[] { new TargetCue(_setup.Lane, 0, MayhemTargetKind.DizzyHost, MayhemEvent.FirstTargetEntityId, null,
            Center, GetHimTuning.Radius, GetHimTuning.HalfHeight, 1, 1, _setup.PlayStart, Wake) },
        Rings = (RingCue[])_rings.Clone(), Bombs = _bombs.Select(b => b with { Launched = _tick >= Wake }).ToArray(),
        HitProgress = _setup.OnAir.Select(s => new HitProgressCue(s.Seat, (byte)_accepted[s.Seat].Count, GetHimTuning.Quota)).ToArray() };
    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteByte(_setup.Lane); w.WriteUInt(_tick);
        foreach (var hits in _accepted) { w.WriteByte((byte)hits.Count); foreach (ulong id in hits) w.WriteULong(id); }
        foreach (var b in _bombs) { w.WriteBool(b.Exploded); w.WriteFloat(b.Landing.X); w.WriteFloat(b.Landing.Y); w.WriteFloat(b.Landing.Z); w.WriteUInt(b.ImpactTick); }
    }
    internal static GetHimChannel Read(SnapshotReader r, ChannelRestore c)
    {
        byte lane = r.ReadByte(); uint tick = r.ReadUInt();
        if (lane != c.Lane || tick > c.Deadline) throw SnapshotReader.Malformed("GET HIM! state");
        var seats = c.Roster.Select((id, i) => new ChannelSeat((byte)i, id, default, 0, 0)).Where(s => (c.OnAir >> s.Seat & 1) != 0).ToArray();
        var channel = new GetHimChannel(new(c.Airing, c.Episode, c.Lane, c.Tier, 0, c.PlayStart, c.Deadline, c.Stage, seats), c.Roster.ToArray()) { _tick = tick };
        for (int seat = 0; seat < 4; seat++)
        {
            int count = r.ReadByte();
            if (count > GetHimTuning.Quota || count > 0 && (c.OnAir >> seat & 1) == 0) throw SnapshotReader.Malformed("GET HIM! hits");
            ulong previous = 0;
            for (int i = 0; i < count; i++) { ulong id = r.ReadULong(); if (id <= previous) throw SnapshotReader.Malformed("GET HIM! activation"); channel._accepted[seat].Add(id); previous = id; }
        }
        for (int i = 0; i < channel._bombs.Length; i++)
        {
            bool exploded = r.ReadBool(); var landing = new ArcadePoint(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()); uint impact = r.ReadUInt();
            if (!landing.IsFinite || !MayhemMath.OnDeck(c.Stage, landing.X, landing.Z) || impact < channel.Wake || impact >= c.Deadline)
                throw SnapshotReader.Malformed("GET HIM! bomb");
            channel._bombs[i] = channel._bombs[i] with { Exploded = exploded, Landing = landing, ImpactTick = impact };
        }
        return channel;
    }
}
