using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

internal enum BonkTwist : byte { Whack = 1, Decoys = 2 }

public static class BonkTuning
{
    public static readonly ChannelTiming Timing = new(90, 480);
    public const int Waves = 2;
    public const float Radius = .55f, HalfHeight = .9f, Spread = 3f, StandOff = .25f, ArrivalTolerance = .3f;
    public const float WhackMargin = 2f, WhackGap = 3f;
    public const ushort DuckTicks = 12, SlipSwingLead = 1, WhackSwingTicks = 40;
    public const int WhackDraws = 16;
}

internal sealed class BonkChannel : IMayhemChannel
{
    private sealed class Head
    {
        public TargetCue Cue;
        public int Candidate = -1;
        public ulong Activation;
    }
    private readonly ChannelSetup _setup;
    private readonly ulong[] _roster;
    private Head[] _heads = Array.Empty<Head>();
    private int _wave = -1;
    private byte _won, _failed;
    private uint _tick;

    private BonkChannel(ChannelSetup setup, ulong[] roster) { _setup = setup; _roster = roster; }
    internal static BonkChannel Start(ChannelSetup setup)
    {
        int count = 0;
        foreach (var seat in setup.OnAir) count = Math.Max(count, seat.Seat + 1);
        var roster = new ulong[count];
        foreach (var seat in setup.OnAir) roster[seat.Seat] = seat.EntityId;
        return new(setup, roster);
    }
    private (uint From, uint Until) Window(int wave)
    {
        uint length = (_setup.Deadline - _setup.PlayStart) / BonkTuning.Waves;
        uint from = _setup.PlayStart + (uint)wave * length;
        return (from, from + length - BonkTuning.DuckTicks);
    }
    private void Spawn(int wave, MayhemFrame frame)
    {
        int seekers = 0;
        foreach (var fighter in frame.Fighters)
            if (fighter.Live && (_won >> fighter.Seat & 1) == 0) seekers++;
        int real = seekers == 0 ? 0 : Math.Max(1, seekers - 1);
        int count = real + (_setup.Draw.Twist.Value == (byte)BonkTwist.Decoys && real > 0 ? 1 : 0);
        _heads = new Head[count];
        var window = Window(wave);
        float angle = MayhemMath.Unit(MayhemMath.Sample(_setup.Seed, 0x9400, (uint)wave)) * MathF.PI * 2;
        for (int i = 0; i < count; i++)
        {
            float yaw = angle + i * MathF.PI * 2 / Math.Max(1, count);
            var ring = new ArcadePoint(MayhemMath.CenterX(_setup.Stage) + MathF.Sin(yaw) * BonkTuning.Spread,
                _setup.Stage.DeckTop + BonkTuning.HalfHeight, MayhemMath.CenterZ(_setup.Stage) + MathF.Cos(yaw) * BonkTuning.Spread);
            var (kind, center) = (BonkTwist)_setup.Draw.Twist.Value switch
            {
                BonkTwist.Whack => (MayhemTargetKind.CardboardHead, WhackSpot(wave, i, frame, ring)),
                BonkTwist.Decoys => (i == real ? MayhemTargetKind.DecoyHead : MayhemTargetKind.CardboardHead, ring),
            };
            _heads[i] = new Head { Cue = new(_setup.Lane, (ushort)(wave * MayhemEvent.MaxTargets + i), kind,
                MayhemEvent.FirstTargetEntityId + (ulong)i, null, center, BonkTuning.Radius, BonkTuning.HalfHeight, 1, 1,
                window.From, window.Until) };
        }
    }
    /// <summary>A seeded spot anywhere on the deck that every seeker can run to in this wave; the ring when no draw fits.</summary>
    private ArcadePoint WhackSpot(int wave, int index, MayhemFrame frame, ArcadePoint ring)
    {
        var stage = _setup.Stage;
        var window = Window(wave);
        float seconds = (window.Until - window.From - BonkTuning.WhackSwingTicks) / 60f;
        for (uint draw = 0; draw < BonkTuning.WhackDraws; draw++)
        {
            uint ordinal = ((uint)wave * MayhemEvent.MaxTargets + (uint)index) * BonkTuning.WhackDraws + draw;
            float x = stage.MinX + BonkTuning.WhackMargin + MayhemMath.Unit(MayhemMath.Sample(_setup.Seed, 0x9410, ordinal)) * (stage.MaxX - stage.MinX - BonkTuning.WhackMargin * 2);
            float z = stage.MinZ + BonkTuning.WhackMargin + MayhemMath.Unit(MayhemMath.Sample(_setup.Seed, 0x9411, ordinal)) * (stage.MaxZ - stage.MinZ - BonkTuning.WhackMargin * 2);
            if (WhackFits(x, z, index, frame, seconds)) return new(x, ring.Y, z);
        }
        return ring;
    }
    private bool WhackFits(float x, float z, int index, MayhemFrame frame, float seconds)
    {
        for (int i = 0; i < index; i++)
            if (MayhemMath.DistanceSquared(x, z, _heads[i].Cue.Center.X, _heads[i].Cue.Center.Z) < BonkTuning.WhackGap * BonkTuning.WhackGap) return false;
        foreach (var fighter in frame.Fighters)
        {
            float reach = fighter.Definition.Movement.RunSpeed * seconds;
            if (fighter.Live && (_won >> fighter.Seat & 1) == 0
                && MayhemMath.DistanceSquared(x, z, fighter.State.PX, fighter.State.PZ) > reach * reach) return false;
        }
        return true;
    }
    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        _tick = tick.Tick;
        if (tick.Phase != MayhemPhase.Play) return;
        int wave = Math.Min(BonkTuning.Waves - 1, (int)((tick.Tick - _setup.PlayStart) * BonkTuning.Waves / (_setup.Deadline - _setup.PlayStart)));
        if (tick.Moment == ChannelMoment.BeforeActions)
        {
            if (wave != _wave) { _wave = wave; Spawn(wave, tick.Frame); }
            Trim(tick.Frame);
            return;
        }
        foreach (var head in _heads)
        {
            if (head.Candidate < 0) continue;
            int seat = head.Candidate;
            if (head.Cue.Kind != MayhemTargetKind.DecoyHead) _won |= (byte)(1 << seat);
            head.Cue = head.Cue with { HitsLeft = 0 };
            hazards.ObjectHit(MayhemEventKind.TargetBroken, _setup.Lane, head.Cue.EntityId, _roster[seat], head.Activation, 0);
            head.Candidate = -1; head.Activation = 0;
        }
        Trim(tick.Frame);
    }
    private void Trim(MayhemFrame frame)
    {
        int seekers = 0;
        foreach (var fighter in frame.Fighters)
            if (fighter.Live && ((_won | _failed) >> fighter.Seat & 1) == 0) seekers++;
        int remaining = seekers == 0 ? 0 : Math.Max(1, seekers - 1);
        foreach (var head in _heads)
            if (head.Cue.Kind == MayhemTargetKind.CardboardHead && head.Cue.HitsLeft > 0 && remaining-- <= 0)
                head.Cue = head.Cue with { HitsLeft = 0 };
    }
    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards)
    {
        if (contact.Kind != MayhemContactKind.SolidHit || contact.AttackSlot is not (AbilitySlots.Normal1 or AbilitySlots.Normal2 or AbilitySlots.Normal3))
            return ChannelContactResult.Absorbed;
        int seat = Array.IndexOf(_roster, contact.Attacker);
        if (seat < 0) return ChannelContactResult.Ignored;
        foreach (var head in _heads)
        {
            if (head.Cue.EntityId != contact.Target || !head.Cue.ActiveAt(_tick)
                || head.Cue.Kind == MayhemTargetKind.CardboardHead && (_won >> seat & 1) != 0) continue;
            if (head.Cue.Kind == MayhemTargetKind.DecoyHead) _failed |= (byte)(1 << seat);
            if (head.Candidate < 0 || seat < head.Candidate) { head.Candidate = seat; head.Activation = contact.ActivationId; }
        }
        return ChannelContactResult.Absorbed;
    }
    public MayhemJudgement Judge(in ContestantProbe probe) => (_failed >> probe.Fighter.Seat & 1) != 0
        ? MayhemJudgement.FailNow(FailCause.Hazard, SplatStyle.Toasted)
        : (_won >> probe.Fighter.Seat & 1) != 0 ? MayhemJudgement.Ready : MayhemJudgement.Incomplete(FailCause.NoBonk);
    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self.State;
        if ((_won >> probe.Self.Seat & 1) != 0) return new(CpuAction.Brake, self.PX, self.PZ);
        TargetCue? best = null; float distance = float.MaxValue;
        foreach (var head in _heads)
        {
            var cue = head.Cue;
            if (!cue.ActiveAt(probe.Seen) || (!probe.Slip && cue.Kind == MayhemTargetKind.DecoyHead)) continue;
            float d = MayhemMath.DistanceSquared(self.PX, self.PZ, cue.Center.X, cue.Center.Z);
            if (probe.Slip && cue.Kind == MayhemTargetKind.DecoyHead) { best = cue; break; }
            if (d < distance) { best = cue; distance = d; }
        }
        if (best is not { } target) return new(CpuAction.Move, MayhemMath.CenterX(_setup.Stage), MayhemMath.CenterZ(_setup.Stage));
        float stand = target.Radius + probe.Self.Definition.CapsuleRadius + BonkTuning.StandOff;
        float dx = self.PX - target.Center.X, dz = self.PZ - target.Center.Z;
        float length = MathF.Sqrt(dx * dx + dz * dz);
        if (length > stand + BonkTuning.ArrivalTolerance) return new(CpuAction.Move, target.Center.X + dx / length * stand, target.Center.Z + dz / length * stand);
        byte slot = probe.Slip && target.Kind == MayhemTargetKind.CardboardHead && probe.Seen + BonkTuning.SlipSwingLead < target.ActiveUntil ? AbilitySlots.None : AbilitySlots.Normal1;
        return new(CpuAction.Normal, target.Center.X, target.Center.Z, slot, true);
    }
    public ChannelScene Observe()
    {
        var targets = new TargetCue[_heads.Length];
        for (int i = 0; i < targets.Length; i++) targets[i] = _heads[i].Cue;
        return ChannelScene.Empty with { Targets = targets };
    }
    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteByte(_setup.Lane); w.WriteByte((byte)(_wave + 1)); w.WriteUInt(_tick); w.WriteByte(_won); w.WriteByte(_failed);
        w.WriteByte((byte)_heads.Length);
        foreach (var head in _heads)
        {
            w.WriteByte((byte)head.Cue.Kind); w.WriteFloat(head.Cue.Center.X); w.WriteFloat(head.Cue.Center.Z); w.WriteByte(head.Cue.HitsLeft);
            w.WriteByte((byte)(head.Candidate + 1)); w.WriteULong(head.Activation);
        }
    }
    internal static BonkChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte(); int wave = r.ReadByte() - 1; uint tick = r.ReadUInt(); byte won = r.ReadByte(), failed = r.ReadByte(); int count = r.ReadByte();
        if (lane != context.Lane || wave < -1 || wave >= BonkTuning.Waves || count > context.Roster.Count
            || ((won | failed) & ~context.OnAir) != 0 || wave == -1 && count != 0) throw SnapshotReader.Malformed("BONK! state");
        var setup = new ChannelSetup(context.Airing, context.Episode, lane, context.Tier, 0, context.PlayStart, context.Deadline, context.Stage, Array.Empty<ChannelSeat>());
        var roster = new ulong[context.Roster.Count]; for (int i = 0; i < roster.Length; i++) roster[i] = context.Roster[i];
        var channel = new BonkChannel(setup, roster) { _wave = wave, _tick = tick, _won = won, _failed = failed, _heads = new Head[count] };
        var window = wave < 0 ? default : channel.Window(wave);
        for (int i = 0; i < count; i++)
        {
            var kind = (MayhemTargetKind)r.ReadByte(); float x = r.ReadFloat(), z = r.ReadFloat(); byte left = r.ReadByte(); int candidate = r.ReadByte() - 1; ulong activation = r.ReadULong();
            if (kind is not (MayhemTargetKind.CardboardHead or MayhemTargetKind.DecoyHead) || !float.IsFinite(x) || !float.IsFinite(z)
                || !MayhemMath.OnDeck(context.Stage, x, z) || left > 1 || candidate >= roster.Length || candidate >= 0 && (context.OnAir >> candidate & 1) == 0)
                throw SnapshotReader.Malformed("BONK! head");
            channel._heads[i] = new Head { Cue = new(lane, (ushort)(wave * MayhemEvent.MaxTargets + i), kind,
                MayhemEvent.FirstTargetEntityId + (ulong)i, null, new(x, context.Stage.DeckTop + BonkTuning.HalfHeight, z),
                BonkTuning.Radius, BonkTuning.HalfHeight, left, 1, window.From, window.Until), Candidate = candidate, Activation = activation };
        }
        return channel;
    }
}
