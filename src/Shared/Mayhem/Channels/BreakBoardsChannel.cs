using System;

namespace SlopArena.Shared.Mayhem;

/// <summary>BREAK THE BOARDS! tuning, indexed by speed tier. Provisional.</summary>
public static class BreakBoardsTuning
{
    public static readonly byte[] Hits = { 3, 3, 3 };
    /// <summary>The stack stands this far in front of the start, toward the deck centre.</summary>
    public const float Distance = 1.6f;
    public const float Radius = .45f, HalfHeight = .8f;
    public const float CpuStandOff = .3f;
}

/// <summary>
/// BREAK THE BOARDS! (guest FightGuy): a stack of boards in front of every contestant. Only the owner's hits
/// count, one per activation; host blasts never break boards. Pass: your own stack broken by the deadline.
/// </summary>
internal sealed class BreakBoardsChannel : IMayhemChannel
{
    private sealed class Stack
    {
        public byte Seat;
        public ulong Owner;
        public float X, Z;
        public byte Hits, HitsLeft;
        public ulong LastActivation;
    }

    private readonly byte _lane;
    private readonly MankiMayhemDefinition _stage;
    private readonly Stack[] _stacks;

    private BreakBoardsChannel(byte lane, MankiMayhemDefinition stage, Stack[] stacks)
    { _lane = lane; _stage = stage; _stacks = stacks; }

    internal static BreakBoardsChannel Start(ChannelSetup setup)
    {
        var stage = setup.Stage;
        byte hits = BreakBoardsTuning.Hits[(int)setup.Tier];
        var stacks = new Stack[Math.Min(setup.OnAir.Count, MayhemEvent.MaxTargets)];
        for (int i = 0; i < stacks.Length; i++)
        {
            var seat = setup.OnAir[i];
            float yaw = MayhemMath.YawTo(seat.Start.X, seat.Start.Z, MayhemMath.CenterX(stage), MayhemMath.CenterZ(stage));
            stacks[i] = new Stack
            {
                Seat = seat.Seat, Owner = seat.EntityId, Hits = hits, HitsLeft = hits,
                X = seat.Start.X + MathF.Sin(yaw) * BreakBoardsTuning.Distance,
                Z = seat.Start.Z + MathF.Cos(yaw) * BreakBoardsTuning.Distance,
            };
        }
        return new BreakBoardsChannel(setup.Lane, stage, stacks);
    }

    private static ulong TargetId(int index) => MayhemEvent.FirstTargetEntityId + (ulong)index;

    public void Tick(in ChannelTick tick, MayhemHazards hazards) { }

    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards)
    {
        if (contact.Kind != MayhemContactKind.SolidHit) return ChannelContactResult.Absorbed;
        int index = (int)(contact.Target - MayhemEvent.FirstTargetEntityId);
        if (!MayhemEvent.IsTargetId(contact.Target) || index >= _stacks.Length) return ChannelContactResult.Ignored;
        var stack = _stacks[index];
        if (contact.Attacker != stack.Owner || stack.HitsLeft == 0 || contact.ActivationId == stack.LastActivation)
            return ChannelContactResult.Absorbed;
        stack.LastActivation = contact.ActivationId;
        stack.HitsLeft--;
        hazards.ObjectHit(stack.HitsLeft == 0 ? MayhemEventKind.TargetBroken : MayhemEventKind.TargetHit, _lane,
            contact.Target, contact.Attacker, contact.ActivationId, stack.HitsLeft);
        return ChannelContactResult.Absorbed;
    }

    public MayhemJudgement Judge(in ContestantProbe probe)
    {
        foreach (var stack in _stacks)
            if (stack.Seat == probe.Fighter.Seat)
                return stack.HitsLeft == 0 ? MayhemJudgement.Ready : MayhemJudgement.Incomplete(FailCause.BoardsRemain);
        return MayhemJudgement.Ready;
    }

    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self.State;
        foreach (var stack in _stacks)
        {
            if (stack.Seat != probe.Self.Seat) continue;
            if (stack.HitsLeft == 0) return new(CpuAction.Brake, self.PX, self.PZ);
            float yaw = MayhemMath.YawTo(stack.X, stack.Z, self.PX, self.PZ);
            float stand = BreakBoardsTuning.Radius + probe.Self.Definition.CapsuleRadius + BreakBoardsTuning.CpuStandOff;
            float sx = stack.X + MathF.Sin(yaw) * stand, sz = stack.Z + MathF.Cos(yaw) * stand;
            float distance = MathF.Sqrt(MayhemMath.DistanceSquared(self.PX, self.PZ, stack.X, stack.Z));
            if (distance > stand + .3f) return new(CpuAction.Move, sx, sz);
            // A slip must read as a slow player, not a frozen CPU: it swings a third of the play apart, so the
            // last board is usually still up at the gong. Between swings it keeps facing so each swing lands.
            uint gap = Math.Max(1u, (probe.Deadline - probe.PlayStart) / (uint)stack.Hits);
            bool swing = !probe.Slip || probe.Seen > probe.PlayStart && (probe.Seen - probe.PlayStart) % gap == 0;
            return new(CpuAction.Normal, stack.X, stack.Z, swing ? AbilitySlots.Normal1 : AbilitySlots.None, Face: true);
        }
        return MayhemCpuIntent.Wait;
    }

    public ChannelScene Observe()
    {
        var targets = new TargetCue[_stacks.Length];
        for (int i = 0; i < _stacks.Length; i++)
        {
            var stack = _stacks[i];
            targets[i] = new TargetCue(_lane, (ushort)i, MayhemTargetKind.BoardStack, TargetId(i), stack.Seat,
                new ArcadePoint(stack.X, _stage.DeckTop + BreakBoardsTuning.HalfHeight, stack.Z),
                BreakBoardsTuning.Radius, BreakBoardsTuning.HalfHeight, stack.HitsLeft, stack.Hits);
        }
        return ChannelScene.Empty with { Targets = targets };
    }

    public void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteByte(_lane);
        w.WriteByte((byte)_stacks.Length);
        foreach (var stack in _stacks)
        {
            w.WriteByte(stack.Seat); w.WriteULong(stack.Owner); w.WriteFloat(stack.X); w.WriteFloat(stack.Z);
            w.WriteByte(stack.Hits); w.WriteByte(stack.HitsLeft); w.WriteULong(stack.LastActivation);
        }
    }

    internal static BreakBoardsChannel Read(SnapshotReader r, ChannelRestore context)
    {
        byte lane = r.ReadByte();
        int count = r.ReadByte();
        if (lane > MayhemEvent.MaxLane || count > context.Roster.Count || count > MayhemEvent.MaxTargets)
            throw SnapshotReader.Malformed("BREAK THE BOARDS! state");
        byte seen = 0;
        var stacks = new Stack[count];
        for (int i = 0; i < count; i++)
        {
            var stack = stacks[i] = new Stack
            {
                Seat = r.ReadByte(), Owner = r.ReadULong(), X = r.ReadFloat(), Z = r.ReadFloat(),
                Hits = r.ReadByte(), HitsLeft = r.ReadByte(), LastActivation = r.ReadULong(),
            };
            if (stack.Seat >= context.Roster.Count || (context.OnAir >> stack.Seat & 1) == 0 || (seen >> stack.Seat & 1) != 0 || context.Roster[stack.Seat] != stack.Owner || stack.Hits == 0
                || stack.HitsLeft > stack.Hits || !float.IsFinite(stack.X) || !float.IsFinite(stack.Z)
                || !MayhemMath.OnDeck(context.Stage, stack.X, stack.Z))
                throw SnapshotReader.Malformed("BREAK THE BOARDS! stack");
            seen |= (byte)(1 << stack.Seat);
        }
        if (seen != context.OnAir) throw SnapshotReader.Malformed("Missing channel objective");
        return new BreakBoardsChannel(lane, context.Stage, stacks);
    }
}
