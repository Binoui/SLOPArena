using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared.Mayhem;

internal enum HotPotatoTwist : byte { OnePotato = 1, TwoPotatoes = 2 }
public static class HotPotatoTuning
{
    public static readonly ChannelTiming Timing = new(90, 360);
    public const ushort EndMarginTicks = 6, ReturnLockTicks = 60, BangGraceTicks = 60;
    public const float TouchTolerance = .025f, RunReach = 5f, GiverPenalty = 10000f;
}

internal sealed class HotPotatoChannel : IMayhemChannel
{
    private readonly ChannelSetup _setup;
    private readonly int[] _holders, _givers;
    private readonly uint[] _lockEnds;
    private ushort _touches, _hits;
    private byte _failed;
    private HotPotatoChannel(ChannelSetup setup, int[] holders, int[] givers, uint[] lockEnds)
    { _setup = setup; _holders = holders; _givers = givers; _lockEnds = lockEnds; }
    internal static HotPotatoChannel Start(ChannelSetup setup)
    {
        int count = (HotPotatoTwist)setup.Draw.Twist.Value switch
        { HotPotatoTwist.OnePotato => 1, HotPotatoTwist.TwoPotatoes => setup.OnAir.Count >= 3 ? 2 : 1 };
        var seats = setup.OnAir.Select(s => (int)s.Seat).ToArray();
        MayhemMath.Shuffle(seats, setup.Seed, 0xA200);
        int potatoes = Math.Min(count, seats.Length);
        return new(setup, seats.Take(potatoes).ToArray(), Enumerable.Repeat(-1, potatoes).ToArray(), new uint[potatoes]);
    }
    private uint Fuse => _setup.Deadline - HotPotatoTuning.EndMarginTicks;
    private static ushort Edge(int a, int b) => (ushort)(1 << (Math.Min(a, b) * 4 + Math.Max(a, b)));
    private static bool Touch(in MayhemFighterFact a, in MayhemFighterFact b)
    {
        float radius = a.Definition.CapsuleRadius + b.Definition.CapsuleRadius + HotPotatoTuning.TouchTolerance;
        float dy = MathF.Max(0, MathF.Abs(a.State.PY - b.State.PY)
            - (a.Definition.CapsuleHeight + b.Definition.CapsuleHeight) * .5f
            + a.Definition.CapsuleRadius + b.Definition.CapsuleRadius);
        return MayhemMath.DistanceSquared(a.State.PX, a.State.PZ, b.State.PX, b.State.PZ) + dy * dy <= radius * radius;
    }
    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        if (tick.Moment == ChannelMoment.BeforeActions) { _hits = 0; return; }
        if (tick.Phase != MayhemPhase.Play || tick.Tick > Fuse) return;
        var fighters = tick.Frame.Fighters;
        bool Live(int seat) => seat >= 0 && seat < fighters.Length && fighters[seat].Live;
        var starting = (int[])_holders.Clone();
        int live = fighters.Count(f => f.Live);
        bool Surplus() => live < 3 && _holders.Count(h => h >= 0) > 1;
        bool late = tick.Tick + HotPotatoTuning.BangGraceTicks >= Fuse;
        for (int p = 0; p < _holders.Length; p++)
        {
            if (tick.Tick >= _lockEnds[p]) _givers[p] = -1;
            if (starting[p] < 0 || Live(starting[p])) continue;
            _givers[p] = -1;
            if (late || live < 2 || Surplus()) { _holders[p] = -1; continue; }
            int nearest = -1; float distance = float.MaxValue;
            foreach (var f in fighters)
            {
                if (!f.Live || Array.IndexOf(starting, (int)f.Seat) >= 0 || Array.IndexOf(_holders, (int)f.Seat) >= 0) continue;
                float d = MayhemMath.DistanceSquared(f.State.PX, f.State.PZ, fighters[starting[p]].State.PX, fighters[starting[p]].State.PZ);
                if (d < distance) { distance = d; nearest = f.Seat; }
            }
            _holders[p] = nearest;
        }
        if (Surplus()) _holders[^1] = -1;
        ushort touches = 0;
        for (int a = 0; a < fighters.Length; a++)
        for (int b = a + 1; b < fighters.Length; b++)
            if (fighters[a].Live && fighters[b].Live && Touch(fighters[a], fighters[b])) touches |= Edge(a, b);
        ushort fresh = (ushort)(touches & ~_touches);
        if (tick.Tick < Fuse)
        {
            for (int p = 0; p < starting.Length; p++)
            {
                int holder = starting[p];
                if (_holders[p] < 0 || !Live(holder)) continue;
                foreach (var f in fighters)
                {
                    int other = f.Seat;
                    if (!f.Live || other == holder || other == _givers[p] || Array.IndexOf(starting, other) >= 0
                        || Array.IndexOf(_holders, other) >= 0
                        || (fresh & Edge(holder, other)) == 0 && (_hits >> (holder * 4 + other) & 1) == 0) continue;
                    _holders[p] = other; _givers[p] = holder; _lockEnds[p] = tick.Tick + HotPotatoTuning.ReturnLockTicks; break;
                }
            }
        }
        else
            foreach (int holder in _holders) if (Live(holder)) _failed |= (byte)(1 << holder);
        _touches = touches;
    }
    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards)
    {
        if (contact.Kind == MayhemContactKind.FighterHit)
        {
            int a = -1, b = -1;
            foreach (var s in _setup.OnAir) { if (s.EntityId == contact.Attacker) a = s.Seat; if (s.EntityId == contact.Target) b = s.Seat; }
            if (a >= 0 && b >= 0 && a != b) _hits |= (ushort)(1 << (a * 4 + b));
        }
        return ChannelContactResult.Absorbed;
    }
    public MayhemJudgement Judge(in ContestantProbe probe) => (_failed >> probe.Fighter.Seat & 1) != 0
        ? MayhemJudgement.FailNow(FailCause.Hazard, SplatStyle.Toasted) : MayhemJudgement.Ready;
    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self;
        int potato = Array.IndexOf(_holders, (int)self.Seat);
        MayhemFighterFact? nearest = null; float distance = float.MaxValue;
        foreach (var f in probe.Frame.Fighters)
        {
            if (!f.Live || f.Seat == self.Seat) continue;
            bool holder = Array.IndexOf(_holders, (int)f.Seat) >= 0;
            if (potato >= 0 ? holder : !holder) continue;
            float d = MayhemMath.DistanceSquared(self.State.PX, self.State.PZ, f.State.PX, f.State.PZ);
            // A locked giver is tailed only when nobody else can take the bomb, so a two-seat chase resumes at once.
            if (potato >= 0 && !probe.Slip && f.Seat == _givers[potato]) d += HotPotatoTuning.GiverPenalty;
            if (d < distance) { nearest = f; distance = d; }
        }
        if (nearest is not { } rival) return new(CpuAction.Brake, self.State.PX, self.State.PZ);
        if (potato >= 0 && !probe.Slip) return new(CpuAction.Move, rival.State.PX, rival.State.PZ);
        float dx = self.State.PX - rival.State.PX, dz = self.State.PZ - rival.State.PZ;
        float length = MathF.Max(.01f, MathF.Sqrt(dx * dx + dz * dz));
        var destination = MayhemMath.Clamp(_setup.Stage, self.State.PX + dx / length * HotPotatoTuning.RunReach,
            self.State.PZ + dz / length * HotPotatoTuning.RunReach, MayhemShowTuning.PlacementMargin);
        if (probe.Slip) destination = new(rival.State.PX, _setup.Stage.DeckTop, rival.State.PZ);
        return new(CpuAction.Move, destination.X, destination.Z);
    }
    public ChannelScene Observe() => ChannelScene.Empty with { Carries = _holders.Select((s, i) => (s, i))
        .Where(x => x.s >= 0).Select(x => new CarryCue(_setup.Lane, (ushort)x.i, (byte)x.s, Fuse,
            _givers[x.i] < 0 ? null : (byte)_givers[x.i])).ToArray() };
    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteByte(_setup.Lane); w.WriteByte(_failed); w.WriteUShort(_touches); w.WriteByte((byte)_holders.Length);
        for (int p = 0; p < _holders.Length; p++) { w.WriteByte((byte)(_holders[p] + 1)); w.WriteByte((byte)(_givers[p] + 1)); w.WriteUInt(_lockEnds[p]); }
    }
    internal static HotPotatoChannel Read(SnapshotReader r, ChannelRestore c)
    {
        byte lane = r.ReadByte(), failed = r.ReadByte(); ushort touches = r.ReadUShort(); int count = r.ReadByte();
        int max = c.Draw.Twist.Value == (byte)HotPotatoTwist.TwoPotatoes ? 2 : 1;
        if (lane != c.Lane || (failed & ~c.OnAir) != 0 || count > max || (touches & ~0x08CE) != 0) throw SnapshotReader.Malformed("HOT POTATO! state");
        var holders = new int[count]; var givers = new int[count]; var lockEnds = new uint[count];
        for (int p = 0; p < count; p++)
        {
            holders[p] = r.ReadByte() - 1; givers[p] = r.ReadByte() - 1; lockEnds[p] = r.ReadUInt();
            if (lockEnds[p] > c.Deadline + HotPotatoTuning.ReturnLockTicks || holders[p] >= c.Roster.Count || givers[p] >= c.Roster.Count || holders[p] >= 0 && (c.OnAir >> holders[p] & 1) == 0
                || givers[p] >= 0 && (c.OnAir >> givers[p] & 1) == 0 || holders[p] >= 0 && Array.IndexOf(holders, holders[p], 0, p) >= 0)
                throw SnapshotReader.Malformed("HOT POTATO! holder");
        }
        var seats = c.Roster.Select((id, i) => new ChannelSeat((byte)i, id, default, 0, 0)).Where(s => (c.OnAir >> s.Seat & 1) != 0).ToArray();
        return new(new(c.Airing, c.Episode, c.Lane, c.Tier, 0, c.PlayStart, c.Deadline, c.Stage, seats), holders, givers, lockEnds)
            { _failed = failed, _touches = touches };
    }
}
