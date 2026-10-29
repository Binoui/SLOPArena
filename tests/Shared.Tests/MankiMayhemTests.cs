using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MankiMayhemTests
{
    internal static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    internal static ArenaDefinition Arena()
    {
        var arena = WorldSnapshotTests.LoadArena("manki_mayhem");
        arena.Arcade = ArcadeStageDefinition.ForMayhem("manki_mayhem", 1,
            new(new(0, 6.5f, 41.5f), -12, 12, -10, 10, 0));
        return arena;
    }
    internal static ServerSimulation NewMatch(uint seed = 1337, FriendlyFireMode fire = FriendlyFireMode.Shoves,
        int count = 4, bool reverse = false, bool roster = false)
    {
        var arena = Arena();
        var sim = new ServerSimulation(arena, new MankiMayhemMatchRule(fire));
        var classes = new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };
        foreach (int index in reverse ? Enumerable.Range(0, count).Reverse() : Enumerable.Range(0, count))
        {
            var content = roster ? BuiltInContentResolver.Resolve(classes[index]) : null;
            var def = content?.Definition ?? TestHelpers.EngineDef with
            {
                CaptureGeometry = new CookedCaptureGeometry(1.5f, .8f, 1.2f, .6f,
                    new CaptureAnchor(0, .6f, .2f), new CaptureAnchor(0, .6f, .65f)),
            };
            var spawn = MatchFormat.MankiMayhem.StartFor(arena, index, count);
            var state = TestHelpers.PlayerState(spawn.X, spawn.Z);
            state.EntityId = (ulong)index + 1; state.PY = def.CapsuleHeight * .5f;
            sim.RegisterEntity(state.EntityId, def, state, content?.BakedAnimation);
        }
        sim.BeginMankiMayhem(seed);
        return sim;
    }
    internal static void Step(ServerSimulation sim, int ticks = 1)
    { for (int t = 0; t < ticks; t++) sim.Tick(new()); }
    internal static void Until(ServerSimulation sim, Func<bool> condition, int limit = 18000)
    {
        while (!condition() && limit-- > 0) Step(sim);
        Assert.True(condition(), $"Mayhem stopped at {sim.MankiMayhem}, tick {sim.CurrentTick}.");
    }
    internal static void Ko(ServerSimulation sim, ulong id)
    { var state = sim.GetState(id); state.PY = -100; state.IsGrounded = false; sim.SetState(id, state); }

    [Fact]
    public void FormatAdmitsOnlyMayhemAndUsesFourStartsWithoutChangingTraining()
    {
        var arena = Arena();
        Assert.True(MatchFormats.TryParseWire("manki-mayhem", out var format));
        Assert.Equal(MatchFormat.MankiMayhem, format);
        Assert.Equal("manki-mayhem", format.ToWire());
        Assert.IsType<MankiMayhemMatchRule>(format.CreateRule(3));
        Assert.True(format.Admits(arena)); Assert.False(MatchFormat.Stock.Admits(arena));
        Assert.False(format.Admits(TestHelpers.TestArena()));
        for (int i = 0; i < 4; i++) Assert.Equal(arena.SpawnPoints[i], format.StartFor(arena, i, 4));
        Assert.Null(new ServerSimulation(arena, NoWinMatchRule.Instance).MankiMayhem);
        Assert.Throws<ArgumentException>(() => new ServerSimulation(TestHelpers.TestArena(), new MankiMayhemMatchRule()));
        Assert.Throws<InvalidOperationException>(() => NewMatch(count: 1));
        Assert.Throws<InvalidOperationException>(() => NewMatch(seed: 0));
    }

    [Fact]
    public void PhasesCallPatternSettleScoreBreakNextCallAndFixedMatchEnd()
    {
        var sim = NewMatch(); var phases = new HashSet<MankiMayhemPhase>(); var calls = new List<ushort>();
        int launches = 0;
        while (!sim.EvaluateMatch().IsEnded && sim.CurrentTick < 18000)
        {
            Step(sim); phases.Add(sim.MankiMayhem!.Value.Phase);
            foreach (var e in sim.LastTickMayhemEvents)
            {
                if (e.Kind == MankiMayhemEventKind.Call) calls.Add(e.RoundIndex);
                if (e.Kind == MankiMayhemEventKind.Launch) launches++;
            }
        }
        Assert.Equal(Enum.GetValues<MankiMayhemPhase>().Where(p => p != MankiMayhemPhase.Waiting).Order(), phases.Order());
        Assert.Equal(Enumerable.Range(0, MankiMayhemTuning.DeckPasses * MankiMayhemRounds.DeckSize).Select(i => (ushort)i), calls);
        Assert.True(launches >= calls.Count * 2); Assert.True(sim.EvaluateMatch().IsEnded);
    }

    [Fact]
    public void SeededBagAndAimedRingsAreRepeatableAndEachPassContainsEveryCard()
    {
        var first = NewMatch(41); var same = NewMatch(41, reverse: true); var other = NewMatch(42);
        Step(first); Step(same); Step(other);
        Assert.Equal(first.MayhemLandingRings, same.MayhemLandingRings);
        Assert.NotEqual(first.MankiMayhem!.Value.Seed, other.MankiMayhem!.Value.Seed);
        for (ushort round = 0; round < MankiMayhemTuning.DeckPasses * MankiMayhemRounds.DeckSize; round += (ushort)MankiMayhemRounds.DeckSize)
        {
            Assert.Equal(Enum.GetValues<MankiMayhemCard>().Order(), Enumerable.Range(round, MankiMayhemRounds.DeckSize).Select(i => MankiMayhemRounds.CardAt(41, (ushort)i)).Order());
            Assert.Equal(MankiMayhemRounds.CardAt(41, round), MankiMayhemRounds.CardAt(41, round));
        }
        var aimed = first.MayhemLandingRings.ToArray();
        var moved = first.GetState(1); moved.PX += 3; first.SetState(1, moved); Step(first);
        Assert.Equal(aimed, first.MayhemLandingRings);
        Assert.True(MankiMayhemRounds.CallDuration((ushort)(5 * MankiMayhemRounds.DeckSize)) < MankiMayhemRounds.CallDuration(0));
    }

    [Fact]
    public void BenchAfterNormalRespawnIsUntargetableAndReturnsAtSpawnWithZeroPercent()
    {
        var sim = NewMatch(); Step(sim); Ko(sim, 1); Step(sim);
        var state = sim.GetState(1);
        Assert.True(sim.IsEntityInactive(1)); Assert.Equal(1, state.Deaths); Assert.Equal(0, state.DamagePercent);
        Assert.False(new MankiMayhemMatchRule().IsEliminated(state));
        Assert.False(sim.TryGetCombatTargetPosition(1, out _));
        for (int i = 0; i < 10; i++) sim.Tick(new() { [1] = new() { MoveX = 1, Jump = true, ActiveSlot = AbilitySlots.Slot1 } });
        Assert.Equal((state.PX, state.PY, state.PZ, state.State),
            (sim.GetState(1).PX, sim.GetState(1).PY, sim.GetState(1).PZ, sim.GetState(1).State));
        Assert.DoesNotContain(1UL, sim.LastTickAcceptedActions);
        Assert.DoesNotContain(sim.GetLastEntityData(), e => e.Id == 1);
        Until(sim, () => sim.MankiMayhem!.Value.RoundIndex == 1);
        Assert.False(sim.IsEntityInactive(1)); Assert.Equal(0, sim.GetState(1).DamagePercent);
        Assert.Equal(Arena().SpawnPoints[0].X, sim.GetState(1).PX);
        Assert.Equal(Arena().SpawnPoints[0].Z, sim.GetState(1).PZ);
        Assert.Equal(1, sim.GetState(1).Deaths);
    }

    [Fact]
    public void SurvivorsScoreAndSoleSurvivorGetsOneBonusWithoutKoCredit()
    {
        var sim = NewMatch(); Step(sim); Ko(sim, 1); Ko(sim, 2); Ko(sim, 3); Step(sim);
        Until(sim, () => sim.MankiMayhem!.Value.Phase == MankiMayhemPhase.Score);
        Assert.Equal(0, sim.MayhemPlayers.Single(p => p.EntityId == 1).Score);
        var survivor = sim.MayhemPlayers.Single(p => p.EntityId == 4);
        Assert.Equal(MankiMayhemTuning.SurvivalPoints + MankiMayhemTuning.LastStandingBonus, survivor.Score);
        Assert.Equal(1, survivor.RoundsSurvived); Assert.Equal(1, survivor.Bonuses);
        Assert.All(sim.MayhemPlayers, p => Assert.Equal(0, sim.GetKOs(p.EntityId)));
    }

    [Fact]
    public void LastStandingWaitsForTheCurrentThrowAndAttachedFuseBeforeScoring()
    {
        var sim = NewMatch(41); Step(sim);
        Until(sim, () => sim.LastTickMayhemEvents.Any(e => e.Kind == MankiMayhemEventKind.Launch));
        var ring = sim.MayhemLandingRings[0]; Ko(sim, 2); Ko(sim, 3); Ko(sim, 4); Step(sim);
        Assert.Equal(MankiMayhemPhase.Settle, sim.MankiMayhem!.Value.Phase);
        Assert.Equal(0, sim.MayhemPlayers[0].Score);
        Until(sim, () => sim.MankiMayhem!.Value.Phase == MankiMayhemPhase.Score);
        Assert.True(sim.CurrentTick >= ring.LandingTick);
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == ServerSimulation.MayhemHostEntityId);
    }

    [Fact]
    public void PercentCarriesAndSpawnsResetAtEveryCallAndTiesShareVictory()
    {
        var sim = NewMatch(); Step(sim);
        var state = sim.GetState(1); state.DamagePercent = 73; state.PX = -9; state.PZ = -7; sim.SetState(1, state);
        Until(sim, () => sim.MankiMayhem!.Value.Phase == MankiMayhemPhase.Score);
        ushort percent = sim.GetState(1).DamagePercent;
        Assert.True(percent >= 73);
        Until(sim, () => sim.MankiMayhem!.Value.RoundIndex == 1);
        Assert.Equal(percent, sim.GetState(1).DamagePercent);
        Assert.Equal(Arena().SpawnPoints[0].X, sim.GetState(1).PX);
        Assert.Equal(Arena().SpawnPoints[0].Z, sim.GetState(1).PZ);
        var tie = NewMatch();
        while (!tie.EvaluateMatch().IsEnded && tie.CurrentTick < 18000)
        {
            Step(tie);
            if (tie.LastTickMayhemEvents.Any(e => e.Kind == MankiMayhemEventKind.Call))
                for (ulong id = 1; id <= 4; id++) Ko(tie, id);
        }
        Assert.True(tie.EvaluateMatch().IsSharedVictory); Assert.Equal(0UL, tie.EvaluateMatch().WinnerEntityId);
        Assert.All(tie.MayhemPlayers, p => Assert.Equal(0, p.Score));
    }

    [Fact]
    public void HighestScoreWinsAfterTheFixedNumberOfRounds()
    {
        var sim = NewMatch();
        while (!sim.EvaluateMatch().IsEnded && sim.CurrentTick < 6000)
        {
            Step(sim);
            if (sim.LastTickMayhemEvents.Any(e => e.Kind == MankiMayhemEventKind.Call))
                for (ulong id = 1; id < 4; id++) Ko(sim, id);
        }
        Assert.Equal(new MatchOutcome(true, 4, false), sim.EvaluateMatch());
        Assert.Equal(MankiMayhemTuning.DeckPasses * MankiMayhemRounds.DeckSize * 2, sim.MayhemPlayers[3].Score);
    }

    [Theory]
    [InlineData(FriendlyFireMode.Off)]
    [InlineData(FriendlyFireMode.Shoves)]
    [InlineData(FriendlyFireMode.On)]
    public void FighterHitsUseFriendlyFireButHostAlwaysHitsAndNeverGetsKoCredit(FriendlyFireMode fire)
    {
        var sim = NewMatch(fire: fire); Step(sim);
        var victim = sim.GetState(2); victim.DamagePercent = 80; sim.SetState(2, victim);
        sim.Resolver.Spawn(Hit(1, victim)); Step(sim);
        if (fire == FriendlyFireMode.Off)
        { Assert.Equal(80, sim.GetState(2).DamagePercent); Assert.Empty(sim.LastTickHits); }
        else
        {
            Assert.Equal(fire == FriendlyFireMode.On ? 92 : 80, sim.GetState(2).DamagePercent);
            Assert.Equal(fire == FriendlyFireMode.On ? 12f : 0f, Assert.Single(sim.LastTickHits).Damage);
            Assert.Equal(ServerSimulation.ComputeHitstopTicks(12), sim.GetState(2).HitstopTicks);
            if (fire == FriendlyFireMode.Shoves)
            {
                float expectedForce = (8f + 4f * (80f * .01f + 1f)) * 200f
                    / (TestHelpers.EngineDef.Weight + 100f) * sim.CombatTuning.KnockbackScale;
                Assert.Equal(expectedForce, Assert.Single(sim.LastTickHits).ImpactForce, 3);
            }
            Until(sim, () => sim.GetState(2).HitstunTicks > 0, 20);
            Assert.True(Simulation.HasKnockback(sim.GetState(2)));
            if (fire == FriendlyFireMode.Shoves) Assert.Equal(0, sim.GetState(2).QueuedKBDamage);
        }
        victim = sim.GetState(3);
        sim.Resolver.Spawn(Hit(ServerSimulation.MayhemHostEntityId, victim)); Step(sim);
        Assert.Equal(12, sim.GetState(3).DamagePercent);
        Ko(sim, 3); Step(sim);
        var death = Assert.Single(sim.LastTickDeaths);
        Assert.Equal(ServerSimulation.MayhemHostEntityId, death.LastHitEntityId);
        Assert.Equal(0UL, death.KillerEntityId);
    }

    internal static Hitbox Hit(ulong owner, CharacterState victim) => new()
    {
        OwnerId = owner, X = victim.PX, Y = victim.PY, Z = victim.PZ, Radius = 1,
        Damage = 12, BaseKnockback = 8, KnockbackGrowth = 4, KnockbackAngle = 20, StunTicks = 1,
        DurationTicks = 1, FreezesOwner = true,
    };

    [Theory]
    [InlineData(FriendlyFireMode.Off)]
    [InlineData(FriendlyFireMode.Shoves)]
    [InlineData(FriendlyFireMode.On)]
    public void GrabsAndThrowsRespectFriendlyFire(FriendlyFireMode fire)
    {
        var sim = NewMatch(fire: fire, count: 2); Step(sim);
        var owner = sim.GetState(1); owner.PX = 0; owner.PZ = 0; owner.FacingYaw = 0; sim.SetState(1, owner);
        var victim = sim.GetState(2); victim.PX = 0; victim.PZ = 1; victim.DamagePercent = 70; sim.SetState(2, victim);
        bool captured = false, thrown = false;
        for (int t = 0; t < 90; t++)
        {
            sim.Tick(new() { [1] = new() { GrabPressed = t == 0, AimYaw = 0, FacingYaw = 0,
                ActiveSlot = sim.GetState(1).State == ActionState.Grabbing ? AbilitySlots.Slot1 : (byte)0 } });
            captured |= sim.GetState(2).State == ActionState.Grabbed;
            thrown |= sim.GetState(2).InteractionPhase == (byte)DefenseInteractionPhase.UpTossReleased;
            if (thrown) break;
        }
        Assert.Equal(fire != FriendlyFireMode.Off, captured);
        Assert.Equal(fire != FriendlyFireMode.Off, thrown);
        Assert.Equal(fire == FriendlyFireMode.On ? 74 : 70, sim.GetState(2).DamagePercent);
        if (thrown) Assert.True(sim.GetState(2).QueuedKBStun > 0);
    }

    [Fact]
    public void RoundBoundaryCleansOnlyHostAndRetainsFighterProjectiles()
    {
        var sim = NewMatch();
        Until(sim, () => sim.MankiMayhem!.Value.Phase == MankiMayhemPhase.Score);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, X = 20, Y = 30, Radius = .1f, DurationTicks = 500 });
        Until(sim, () => sim.MankiMayhem!.Value.RoundIndex == 1);
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == ServerSimulation.MayhemHostEntityId);
        Assert.Contains(sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == 1);
    }

    [Fact]
    public void SettleWaitsForLaunchedSurvivorsToRecoverOntoTheDeck()
    {
        var sim = NewMatch(); Step(sim);
        for (ulong id = 1; id < 4; id++) Ko(sim, id);
        var survivor = sim.GetState(4); survivor.PY = 4; survivor.IsGrounded = false; sim.SetState(4, survivor);
        Step(sim);
        Assert.Equal(MankiMayhemPhase.Settle, sim.MankiMayhem!.Value.Phase);
        Assert.Equal(0, sim.MayhemPlayers[3].Score);
        Until(sim, () => sim.MankiMayhem!.Value.Phase == MankiMayhemPhase.Score);
        Assert.True(sim.GetState(4).IsGrounded); Assert.Equal(2, sim.MayhemPlayers[3].Score);
    }

    [Fact]
    public void HostCleanupRemovesPendingBlastsWithoutTouchingFighterProjectiles()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox { OwnerId = ServerSimulation.MayhemHostEntityId, DurationTicks = 1,
            Explosion = new ProjectileExplosion { Radius = 2, DurationTicks = 1 } });
        resolver.Spawn(new Hitbox { OwnerId = 1, DurationTicks = 500 });
        resolver.Tick(new());
        Assert.True(resolver.HasOwnerEffects(ServerSimulation.MayhemHostEntityId));
        resolver.RemoveOwnerEffects(ServerSimulation.MayhemHostEntityId);
        Assert.False(resolver.HasOwnerEffects(ServerSimulation.MayhemHostEntityId));
        Assert.Empty(resolver.DrainPendingExplosions()); Assert.Equal(1UL, Assert.Single(resolver.GetActiveHitboxes()).OwnerId);
    }

    [Fact]
    public void WithdrawnPlayerKeepsScoreAndNeverReturnsOrWins()
    {
        var sim = NewMatch(); Step(sim); sim.WithdrawMayhemPlayer(1);
        Assert.True(sim.IsEntityInactive(1));
        Until(sim, () => sim.MankiMayhem!.Value.RoundIndex == 1);
        Assert.True(sim.IsEntityInactive(1)); Assert.True(sim.MayhemPlayers[0].Withdrawn);
    }

    [Theory]
    [InlineData(41u)] [InlineData(1337u)] [InlineData(2026u)]
    public void FourCpuSeededMatchReachesOutcomeAndCpusLeaveLandingRings(uint seed)
    {
        var arena = Arena(); var sim = NewMatch(seed, roster: true);
        var classes = new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk };
        var defs = classes.Select(TestHelpers.ResolveDef).ToArray();
        var policies = classes.Select(_ => new HeuristicBotPolicy()).ToArray();
        var memories = classes.Select(_ => new BotMemory()).ToArray();
        var rngs = classes.Select((_, i) => new Random((int)seed + i)).ToArray();
        var report = new StringBuilder($"seed {seed}\n"); int escaped = 0, hazards = 0; var escapedBy = new HashSet<ulong>();
        while (!sim.EvaluateMatch().IsEnded && sim.CurrentTick < 20000)
        {
            var inputs = new Dictionary<ulong, InputState>();
            for (int i = 0; i < 4; i++)
            {
                ulong id = (ulong)i + 1;
                if (sim.IsEntityInactive(id)) continue;
                memories[i].ObserveOpponent(sim.GetState((ulong)((i + 1) % 4) + 1));
                inputs[id] = policies[i].Decide(sim.GetState(id), defs[i], rngs[i], memories[i], arena,
                    behavior: CpuBehavior.Avoid, mayhemThreats: new(sim));
            }
            sim.Tick(inputs);
            foreach (var e in sim.LastTickMayhemEvents)
            {
                if (e.Kind == MankiMayhemEventKind.Call) report.AppendLine($"round {e.RoundIndex + 1} {sim.MankiMayhem!.Value.Card}");
                if (e.Kind == MankiMayhemEventKind.Launch)
                {
                    hazards++; var ring = sim.MayhemLandingRings[e.OperationIndex];
                    report.AppendLine($"  hazard {ring.Ordinal} ring ({ring.Position.X:F2}, {ring.Position.Z:F2}) lands {ring.LandingTick}");
                }
                if (e.Kind == MankiMayhemEventKind.Score) report.AppendLine($"  survivor {e.EntityId} +{e.Points}");
            }
            foreach (var ring in sim.MayhemLandingRings.Where(r => r.LandingTick == sim.CurrentTick))
            {
                ulong id = (ulong)(ring.Ordinal % 4) + 1; var s = sim.GetState(id);
                float dx = s.PX - ring.Position.X, dz = s.PZ - ring.Position.Z;
                if (!sim.IsEntityInactive(id) && dx * dx + dz * dz > MankiMayhemTuning.BlastRadius * MankiMayhemTuning.BlastRadius)
                { escaped++; escapedBy.Add(id); }
            }
        }
        foreach (var player in sim.MayhemPlayers)
            report.AppendLine($"fighter {player.EntityId} score {player.Score}, survived {player.RoundsSurvived}, bonuses {player.Bonuses}");
        report.AppendLine($"ticks {sim.CurrentTick}, hazards {hazards}, rings escaped {escaped}, outcome {sim.EvaluateMatch()}");
        string output = Path.Combine(Root, "tmp/manki-mayhem-step45/headless"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, $"seed-{seed}.txt"), report.ToString());
        Assert.True(sim.EvaluateMatch().IsEnded, report.ToString()); Assert.True(escaped >= 4, report.ToString()); Assert.Equal(4, escapedBy.Count);
        Assert.True(hazards >= MankiMayhemTuning.DeckPasses * 2);
    }
}
