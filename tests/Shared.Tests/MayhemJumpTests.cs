using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MayhemJumpTests
{
    [Fact]
    public void FastRingSweepsItsBandOnceAndLeavesTheInteriorEmpty()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(Ring(600));
        var target = new SpellResolver.EntityData { Id = 1, Active = true, PosX = 5, PosY = .4f, Radius = .3f };
        Assert.Single(resolver.Tick(new() { target }));
        Assert.Empty(resolver.Tick(new() { target }));
        target.Id = 2; target.PosX = 2;
        Assert.Empty(resolver.Tick(new() { target }));
    }

    [Fact]
    public void CapsuleAboveTheBandClearsItAndAFlatCapsuleCrossingItGetsHit()
    {
        var target = new SpellResolver.EntityData { Id = 1, Active = true, Shape = HitboxShape.Capsule,
            PosX = 2, EndX = 8, PosY = 2, EndY = 2, Radius = .3f };
        var resolver = new SpellResolver(); resolver.Spawn(Ring(600));
        Assert.Empty(resolver.Tick(new() { target }));
        resolver = new(); resolver.Spawn(Ring(600)); target.PosY = target.EndY = .3f;
        Assert.Single(resolver.Tick(new() { target }));
    }

    internal static Hitbox Ring(float speed = 9) => new()
    {
        OwnerId = ServerSimulation.MayhemHostEntityId, Shape = HitboxShape.Ring, Y = .25f,
        RingSpeed = speed, RingHalfHeight = .25f, RingHalfWidth = .28f,
        Damage = 10, DurationTicks = 150, Unblockable = true, HitsMultipleOpponents = true,
    };

    [Fact]
    public void RingBypassesShieldWhileOrdinaryBombBlastIsBlocked()
    {
        var sim = MankiMayhemTests.NewMatch(); MankiMayhemTests.Step(sim);
        var victim = sim.GetState(1);
        var ring = Ring(600); ring.X = victim.PX - 5; ring.Z = victim.PZ;
        sim.Resolver.Spawn(ring);
        sim.Tick(new() { [1] = new() { ShieldHeld = true, ShieldPressed = true } });
        Assert.Equal(10, sim.GetState(1).DamagePercent);
        Assert.False(Assert.Single(sim.LastTickHits, h => h.TargetEntityId == 1).Blocked);
        sim = MankiMayhemTests.NewMatch(); MankiMayhemTests.Step(sim);
        victim = sim.GetState(1);
        sim.Resolver.Spawn(MankiMayhemTests.Hit(ServerSimulation.MayhemHostEntityId, victim));
        sim.Tick(new() { [1] = new() { ShieldHeld = true, ShieldPressed = true } });
        Assert.Equal(0, sim.GetState(1).DamagePercent);
        Assert.True(Assert.Single(sim.LastTickHits).Blocked);
    }

    [Fact]
    public void LaterJumpRoundsUsePairsAndSlamsEmitAtTheirScheduledTicks()
    {
        var stage = MankiMayhemTests.Arena().Arcade!.Mayhem!;
        var early = MankiMayhemRounds.Jump(0, 1, stage);
        var late = MankiMayhemRounds.Jump((ushort)MankiMayhemRounds.DeckSize, 1, stage);
        Assert.Equal(2, early.Length); Assert.Equal(4, late.Length);
        Assert.Equal((uint)MankiMayhemTuning.SlamPairGapTicks, late[1].Tick - late[0].Tick);
        var sim = JumpMatch(); var schedule = sim.MayhemSlams.ToArray(); var emitted = new List<uint>();
        while (sim.CurrentTick < 2000 && sim.MankiMayhem!.Value.Card == MankiMayhemCard.Jump && sim.MankiMayhem.Value.Phase != MankiMayhemPhase.Score)
        {
            MankiMayhemTests.Step(sim);
            emitted.AddRange(sim.LastTickMayhemEvents.Where(e => e.Kind == MankiMayhemEventKind.Slam).Select(e => e.MatchTick));
        }
        Assert.Equal(schedule.Select(s => s.Tick), emitted);
    }

    internal static ServerSimulation JumpMatch(bool roster = false)
    {
        for (uint seed = 1; ; seed++)
            if (MankiMayhemRounds.CardAt(seed, 0) == MankiMayhemCard.Jump)
            { var sim = MankiMayhemTests.NewMatch(seed, roster: roster); MankiMayhemTests.Step(sim); return sim; }
    }

    [Fact]
    public void FourRosterCpusTimeTheirJumpsAndClearBothRings()
    {
        var sim = JumpMatch(roster: true);
        var classes = new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };
        var jumps = new HashSet<ulong>(); var hits = new List<SpellResolver.HitResult>();
        while (sim.CurrentTick < 2000 && sim.MankiMayhem!.Value.Phase != MankiMayhemPhase.Score)
        {
            var inputs = new Dictionary<ulong, InputState>();
            for (int i = 0; i < 4; i++)
            {
                var threats = new MayhemThreats(sim); ulong id = (ulong)i + 1;
                threats.TryAvoid(sim.GetState(id), TestHelpers.ResolveDef(classes[i]), MankiMayhemTests.Arena(), out var input);
                input.JumpHeld = input.Jump; inputs[id] = input;
                if (input.Jump) jumps.Add(id);
            }
            sim.Tick(inputs); hits.AddRange(sim.LastTickHits);
        }
        Assert.Equal(4, jumps.Count);
        Assert.Empty(hits);
    }
}
