using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared.AI;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public class MayhemBreakOutTests
{
    private readonly ITestOutputHelper _output;
    public MayhemBreakOutTests(ITestOutputHelper output) => _output = output;
    internal static ServerSimulation CageMatch(CharacterClass fighter = CharacterClass.FightGuy)
    {
        var arena = MankiMayhemTests.Arena();
        var sim = new ServerSimulation(arena, new MankiMayhemMatchRule(FriendlyFireMode.Off));
        var content = BuiltInContentResolver.Resolve(fighter);
        for (int i = 0; i < 2; i++)
        {
            var start = MatchFormat.MankiMayhem.StartFor(arena, i, 2);
            var state = TestHelpers.PlayerState(start.X, start.Z); state.EntityId = (ulong)i + 1;
            state.PY = content.Definition.CapsuleHeight * .5f;
            sim.RegisterEntity(state.EntityId, content.Definition, state, content.BakedAnimation);
        }
        uint seed = 1;
        while (MankiMayhemRounds.CardAt(seed, 0) != MankiMayhemCard.BreakOut) seed++;
        sim.BeginMankiMayhem(seed); MankiMayhemTests.Step(sim); return sim;
    }

    public static IEnumerable<object[]> Roster() => BuiltInRosterManifestCodec.Load(
        Path.Combine(MankiMayhemTests.Root, "content-cooked/roster/manifest.json")).Entries.Select(e => new object[] { e.Selector });

    [Theory]
    [MemberData(nameof(Roster))]
    public void EveryRosterFighterEscapesWithOrdinaryAttacksBeforeTheBomb(CharacterClass fighter)
    {
        var sim = CageMatch(fighter); var def = BuiltInContentResolver.Resolve(fighter).Definition;
        var exit = sim.MayhemWalls.Single(w => w.CageEntityId == 1 && w.HitBudget == MankiMayhemTuning.CrackedWallHits);
        uint broken = 0, escaped = 0; int attacks = 0;
        uint impact = sim.MayhemLandingRings[0].LandingTick;
        while (sim.CurrentTick < impact)
        {
            new MayhemThreats(sim).TryAvoid(sim.GetState(1), def, MankiMayhemTests.Arena(), out var input);
            sim.Tick(new() { [1] = input });
            if (sim.LastTickAcceptedActions.Contains(1UL)) attacks++;
            if (broken == 0 && sim.MayhemWalls.Any(w => w.EntityId == exit.EntityId && w.HitsLeft == 0)) broken = sim.CurrentTick;
            var state = sim.GetState(1);
            if (state.PZ < exit.CageCenter.Z - MankiMayhemTuning.CageHalfSize - def.CapsuleRadius
                && escaped == 0) escaped = sim.CurrentTick;
            if (state.PZ < sim.MayhemLandingRings[0].Position.Z - MankiMayhemTuning.CageBlastRadius - def.CapsuleRadius) break;
        }
        var final = sim.GetState(1);
        string report = $"{fighter}: wall {broken} ticks, escape {escaped} ticks ({escaped / 60f:F3}s), blast clear {sim.CurrentTick} ticks, deadline {impact}, attacks {attacks}, pos {final.PX:F3},{final.PZ:F3}";
        _output.WriteLine(report);
        Directory.CreateDirectory(Path.Combine(MankiMayhemTests.Root, "tmp/manki-mayhem-step45/escape"));
        File.WriteAllText(Path.Combine(MankiMayhemTests.Root, $"tmp/manki-mayhem-step45/escape/{fighter}.txt"), report);
        Assert.True(broken > 0 && escaped > 0 && sim.CurrentTick < impact, report);
        Assert.Equal(0, final.DamagePercent);
    }

    [Theory]
    [InlineData(0f)] [InlineData(90f)] [InlineData(180f)] [InlineData(270f)] [InlineData(45f)]
    public void AOneTickLaunchAcrossTheWholeDeckStopsAndSlidesAtTheWall(float degrees)
    {
        var solids = new RuntimeSolidSet(); solids.AddCage(1, new(0, 0, 0), 0, 0);
        var def = TestHelpers.EngineDef;
        var previous = TestHelpers.PlayerState(); previous.PY = def.CapsuleHeight * .5f;
        var state = previous; float angle = degrees * MathF.PI / 180;
        state.PX = MathF.Sin(angle) * 50; state.PZ = MathF.Cos(angle) * 50;
        state.VX = state.KVX = (state.PX - previous.PX) * 60;
        state.VZ = state.KVZ = (state.PZ - previous.PZ) * 60;
        solids.Constrain(previous, ref state, def);
        float limit = MankiMayhemTuning.CageHalfSize - MankiMayhemTuning.WallHalfThickness - def.CapsuleRadius;
        Assert.InRange(state.PX, -limit - .01f, limit + .01f);
        Assert.InRange(state.PZ, -limit - .01f, limit + .01f);
        Assert.InRange(MathF.Abs(state.KVX) + MathF.Abs(state.KVZ), 0, .01f);
    }

    [Fact]
    public void FighterSlidesAlongTheFaceAndSpawnOverlapResolvesIntoItsCage()
    {
        var solids = new RuntimeSolidSet(); solids.AddCage(1, new(0, 0, 0), 0, 0);
        var previous = TestHelpers.PlayerState(); previous.PY = TestHelpers.EngineDef.CapsuleHeight * .5f;
        var state = previous; state.PX = .5f; state.PZ = 3;
        solids.Constrain(previous, ref state, TestHelpers.EngineDef);
        Assert.Equal(.5f, state.PX, 3); Assert.True(state.PZ < 1.9f);
        previous.PZ = 1.9f; state = previous;
        solids.Constrain(previous, ref state, TestHelpers.EngineDef);
        Assert.True(state.PZ < previous.PZ);
        previous.PY = state.PY = MankiMayhemTuning.WallHeight + 2;
        previous.PZ = 0; state.PZ = 5;
        solids.Constrain(previous, ref state, TestHelpers.EngineDef);
        Assert.Equal(5, state.PZ);
    }

    [Fact]
    public void MeleeAndProjectileDamageCountsHitsAndTheBlastWrecksRemainingWalls()
    {
        var sim = CageMatch();
        var wall = sim.MayhemWalls[0];
        for (int i = 0; i < wall.HitBudget; i++)
        {
            sim.Resolver.Spawn(new Hitbox { OwnerId = 1, X = wall.Center.X, Y = 1, Z = wall.Center.Z - (i == 0 ? 0 : 2),
                Damage = 1, Radius = .4f, DurationTicks = 1, VZ = i == 0 ? 0 : 120, FreezesOwner = i == 0 });
            MankiMayhemTests.Step(sim);
            var current = sim.MayhemWalls.Single(w => w.EntityId == wall.EntityId);
            Assert.Equal(wall.HitBudget - i - 1, current.HitsLeft);
            Assert.Contains(sim.LastTickMayhemEvents, e => e.EntityId == wall.EntityId && e.Kind ==
                (current.HitsLeft == 0 ? MankiMayhemEventKind.WallBroken : MankiMayhemEventKind.WallHit));
        }
        MankiMayhemTests.Until(sim, () => sim.MayhemWalls.All(w => !w.Blocks));
        Assert.All(sim.MayhemWalls, w => Assert.Equal(0, w.HitsLeft));
        MankiMayhemTests.Until(sim, () => sim.MayhemWalls.Count == 0);
    }

    [Theory]
    [MemberData(nameof(Roster))]
    public void PlainDoubleJumpCannotClearTheWalls(CharacterClass fighter)
    {
        var sim = CageMatch(fighter); var spawn = sim.GetState(1);
        var def = BuiltInContentResolver.Resolve(fighter).Definition;
        bool second = false; float apex = 0;
        for (int tick = 0; tick < 170; tick++)
        {
            var current = sim.GetState(1);
            bool jump = tick == 0 || !second && !current.IsGrounded && current.JumpsLeft == 1 && current.VY <= 0;
            if (jump && tick != 0) second = true;
            sim.Tick(new() { [1] = new() { Jump = jump, JumpHeld = true, MoveY = -1 } });
            current = sim.GetState(1); apex = MathF.Max(apex, current.PY - def.CapsuleHeight * .5f);
            Assert.True(current.PZ > spawn.PZ - MankiMayhemTuning.CageHalfSize, $"{fighter} escaped with double jump, apex {apex}");
        }
        Assert.True(second); _output.WriteLine($"{fighter} double-jump feet apex {apex:F3} m");
    }
    [Fact]
    public void MankiCanUseJetpackAfterHisJumpsToClearAnIntactWall()
    {
        var sim = CageMatch(CharacterClass.Manki);
        var def = BuiltInContentResolver.Resolve(CharacterClass.Manki).Definition;
        var origin = sim.GetState(1); bool second = false, boost = false, crossed = false; float apex = 0;
        for (int tick = 0; tick < 225; tick++)
        {
            var self = sim.GetState(1);
            bool jump = tick == 0 || !second && !self.IsGrounded && self.JumpsLeft == 1 && self.VY <= 0;
            if (jump && tick != 0) second = true;
            bool use = second && !boost && self.JumpsLeft == 0 && self.VY <= 0;
            if (use) boost = true;
            sim.Tick(new() { [1] = new() { Jump = jump || boost && tick % 12 == 0, JumpHeld = true,
                ActiveSlot = use ? AbilitySlots.E : (byte)0, MoveY = boost ? -1f : 0f, FacingYaw = 18000, AimYaw = 18000 } });
            self = sim.GetState(1); apex = MathF.Max(apex, self.PY - def.CapsuleHeight * .5f);
            if (self.PZ < origin.PZ - MankiMayhemTuning.CageHalfSize - def.CapsuleRadius)
            { crossed = true; break; }
        }
        _output.WriteLine($"Manki kit escape {sim.CurrentTick} ticks, feet apex {apex:F3} m");
        Assert.True(crossed, $"kit mobility did not clear the cage, feet apex {apex:F3}");
        Assert.All(sim.MayhemWalls, w => Assert.True(w.Blocks));
    }

    [Theory]
    [MemberData(nameof(Roster))]
    public void StrongestAuthoredRosterNormalAt999PercentCannotLaunchThroughAWall(CharacterClass fighter)
    {
        var sim = CageMatch(fighter); var def = BuiltInContentResolver.Resolve(fighter).Definition;
        var strongest = def.CookedSlots!.SelectMany(s => s.Timeline.Stages).SelectMany(s => s.Operations)
            .OfType<CookedSpawnHitboxOperation>().Select(o => o.Hitbox)
            .OrderByDescending(h => h.BaseKnockback + h.KnockbackGrowth * (10.99f + h.Damage * .01f) + h.Damage * .1f).First();
        var target = sim.GetState(1); target.DamagePercent = 999; sim.SetState(1, target);
        sim.Resolver.Spawn(new Hitbox { OwnerId = ServerSimulation.MayhemHostEntityId,
            X = target.PX - .1f, Y = target.PY, Z = target.PZ, Radius = 1, DurationTicks = 1,
            Damage = strongest.Damage, BaseKnockback = strongest.BaseKnockback,
            KnockbackGrowth = strongest.KnockbackGrowth, KnockbackAngle = 0, StunTicks = 1 });
        MankiMayhemTests.Step(sim);
        var hit = Assert.Single(sim.LastTickHits, h => h.TargetEntityId == 1);
        _output.WriteLine($"{fighter} strongest authored normal at 999%: {hit.ImpactForce:F3} m/s, {hit.ImpactForce / 60:F3} m/tick");
        float limit = MankiMayhemTuning.CageHalfSize - MankiMayhemTuning.WallHalfThickness - def.CapsuleRadius;
        for (int tick = 0; tick < 45; tick++)
        {
            MankiMayhemTests.Step(sim);
            Assert.InRange(sim.GetState(1).PX - target.PX, -.01f, limit + .01f);
        }
    }

}
