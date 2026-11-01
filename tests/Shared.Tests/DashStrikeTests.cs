using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Abilities;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class DashStrikeTests
{
    private static readonly CharacterDefinition Owner = TestHelpers.ResolveSourceDef("wibou");
    private static readonly CharacterDefinition Body = TestHelpers.EngineDef;
    private static readonly CookedDashStrikeCapabilityParameters Parameters = DashStrikeReach.Parameters(Owner.GetCookedSlotAbility(AbilitySlots.R, false))!;

    private static ServerSimulation Make(float? distance = null, bool air = false, CharacterDefinition? body = null)
    {
        var arena = TestHelpers.TestArena(); arena.Heightmap.OriginX = arena.Heightmap.OriginZ = -100;
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, Owner, TestHelpers.PlayerState() with {
            PY = TestHelpers.GroundPY(Owner) + (air ? 3 : 0), IsGrounded = !air });
        if (distance.HasValue)
            sim.RegisterEntity(2, body ?? Body, TestHelpers.PlayerState(0, distance.Value) with {
                EntityId = 2, PY = TestHelpers.GroundPY(body ?? Body) + (air ? 3 : 0), IsGrounded = !air });
        return sim;
    }
    private static void Step(ServerSimulation sim, InputState input = default, InputState target = default)
        => sim.Tick(new Dictionary<ulong, InputState> { [1] = input, [2] = target });
    private static List<SpellResolver.HitResult> Run(ServerSimulation sim, InputState input = default, InputState target = default)
    {
        var hits = new List<SpellResolver.HitResult>();
        for (int i = 0; i < 110; i++) { Step(sim, i == 0 ? input with { ActiveSlot = AbilitySlots.R } : input, target); hits.AddRange(sim.LastTickHits); }
        return hits.Where(x => x.OwnerEntityId == 1 && x.TargetEntityId == 2).ToList();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PressMovesAndHeldInputCameraAndStickCannotBendTheFixedLine(bool air)
    {
        var plain = Make(air: air); var held = Make(air: air);
        Step(plain, new() { ActiveSlot = AbilitySlots.R });
        Step(held, new() { ActiveSlot = AbilitySlots.R, IsAiming = true, AimYaw = 9000, AimPitch = 6000, MoveX = 1 });
        Assert.InRange(plain.GetState(1).PZ, .46f, .47f);
        for (int i = 0; i < 8; i++)
        {
            Step(plain); Step(held, new() { IsAiming = true, AimYaw = -9000, AimPitch = -6000, MoveX = -1 });
            Assert.Equal(plain.GetState(1).PX, held.GetState(1).PX);
            Assert.Equal(plain.GetState(1).PZ, held.GetState(1).PZ);
        }
        Assert.InRange(held.GetState(1).PZ, 3.499f, 3.501f);
        Assert.InRange(held.GetState(1).PX, -.001f, .001f);
        Assert.Equal((ushort)0, held.GetState(1).ChargeTicks);
    }

    [Fact]
    public void CastUsesSelectedTargetBearingAndLaterTargetChangesCannotBendIt()
    {
        var sim = Make(8);
        sim.RegisterEntity(3, Body, TestHelpers.PlayerState(7, 0) with { EntityId = 3, PY = TestHelpers.GroundPY(Body), IsGrounded = true });
        var s = sim.GetState(1); s.TargetEntityId = 3; sim.SetState(1, s);
        Step(sim, new() { ActiveSlot = AbilitySlots.R, AimYaw = 18000 });
        Assert.InRange(sim.GetState(1).PX, .46f, .47f);
        s = sim.GetState(1); s.TargetEntityId = 2; sim.SetState(1, s);
        for (int i = 0; i < 10; i++) Step(sim);
        Assert.InRange(sim.GetState(1).PX, 3.499f, 3.501f);
        Assert.InRange(sim.GetState(1).PZ, -.001f, .001f);
        var noTarget = Make(); s = noTarget.GetState(1); s.FacingYaw = MathF.PI * .5f; noTarget.SetState(1, s);
        Run(noTarget);
        Assert.InRange(noTarget.GetState(1).PX, 3.499f, 3.501f);
    }

    [Fact]
    public void EveryDistanceTakesOneCutAndTheStandingClassifierAgrees()
    {
        for (float distance = .5f; distance <= 7; distance += .125f)
        {
            var sim = Make(distance);
            var cue = DashStrikeReach.Classify(Parameters, sim.GetState(1), sim.GetState(2), Body);
            var hits = Run(sim);
            Assert.True(hits.Count <= 1, $"{distance}: {hits.Count} cuts");
            var actual = hits.Count == 0 ? DashStrikeContact.None : hits[0].FreezesOwner ? DashStrikeContact.Tip : DashStrikeContact.Light;
            Assert.True(cue == actual, $"{distance}: cue {cue}, actual {actual}");
        }
    }

    [Fact]
    public void TipWinsWhenLightAndTipFirstOverlapOnTheEndpointTick()
    {
        var body = Body with { CapsuleRadius = .01f, CapsuleHeight = .02f, HurtboxRadius = .01f,
            HurtboxCapsules = new[] { new HurtboxCapsule(0, 0, 0, 0, 0, 0, .01f) } };
        var sim = Make(4.05f, body: body);
        var target = sim.GetState(2); target.PY = TestHelpers.GroundPY(Owner); target.IsGrounded = false; sim.SetState(2, target);
        sim.NoGravityEntityId = 2;
        var hit = Assert.Single(Run(sim));
        Assert.True(hit.FreezesOwner); Assert.Equal(13, hit.Damage);
    }

    [Fact]
    public void TinyTargetBetweenTickPositionsIsCut()
    {
        var body = Body with { CapsuleRadius = .01f, CapsuleHeight = .02f, HurtboxRadius = .01f,
            HurtboxCapsules = new[] { new HurtboxCapsule(0, 0, 0, 0, 0, 0, .01f) } };
        var sim = Make(.7f, body: body);
        var target = sim.GetState(2); target.PY = TestHelpers.GroundPY(Owner); target.IsGrounded = false; sim.SetState(2, target);
        sim.NoGravityEntityId = 2;
        var hit = Assert.Single(Run(sim)); Assert.False(hit.FreezesOwner);
    }

    [Fact]
    public void ShieldStopCreatesNoTipAndNoDamage()
    {
        var sim = Make(1.8f);
        var hits = Run(sim, target: new() { ShieldHeld = true });
        Assert.Single(hits); Assert.True(hits[0].Blocked);
        Assert.Equal((ushort)0, sim.GetState(2).DamagePercent);
        Assert.True(sim.GetState(1).PZ < 3.5f);
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), x => x.OwnerId == 1);
    }

    [Fact]
    public void LightCutTurnsTowardVictimAndFirstFreeNormalConnects()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "content-cooked/wibou/poses.bin"))) directory = directory.Parent;
        var baked = BakedAnimationData.LoadFromBin(System.IO.File.ReadAllBytes(System.IO.Path.Combine(directory!.FullName, "content-cooked/wibou/poses.bin")));
        var arena = TestHelpers.TestArena(); arena.Heightmap.OriginX = arena.Heightmap.OriginZ = -100;
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, Owner, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(Owner) }, baked);
        sim.RegisterEntity(2, Body, TestHelpers.PlayerState(0, 2) with { EntityId = 2, PY = TestHelpers.GroundPY(Body) });
        Step(sim, new() { ActiveSlot = AbilitySlots.R });
        for (int i = 0; i < 100 && sim.GetActiveAbility(1) != null; i++) Step(sim);
        Assert.True(MathF.Abs(sim.GetState(1).FacingYaw) > 3f);
        Assert.True(sim.GetState(2).HitstunTicks > 0);
        Step(sim, new() { ActiveSlot = AbilitySlots.Normal1 });
        Assert.Equal(AbilitySlots.Normal1, sim.GetState(1).AttackSlot);
        bool connects = false;
        for (int i = 0; i < 35; i++) { Step(sim, target: new() { ShieldHeld = true }); connects |= sim.LastTickHits.Any(x => x.OwnerEntityId == 1 && !x.Blocked); }
        Assert.True(connects);
    }

    [Theory]
    [InlineData(3)] [InlineData(18)]
    public void WorldSnapshotRestoreMidDashOrRecoveryReproducesEveryTick(int captureTick)
    {
        var continuous = Make(2); Step(continuous, new() { ActiveSlot = AbilitySlots.R });
        for (int i = 1; i < captureTick; i++) Step(continuous);
        var restored = Make(2); restored.RestoreSnapshot(continuous.CaptureSnapshot());
        continuous.ClearPresentationEvents(); restored.ClearPresentationEvents();
        for (int i = 0; i < 65; i++)
        {
            Step(continuous); Step(restored);
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()), $"restore {captureTick}, tick {i}");
            Assert.Equal(continuous.GetPresentationEvents(), restored.GetPresentationEvents());
        }
    }

    [Fact]
    public void TerrainStopNeverPlacesAReplacementTip()
    {
        var arena = TestHelpers.TestArena(); arena.Heightmap.OriginX = arena.Heightmap.OriginZ = -100;
        arena.CollisionTriangles = new[] {
            new CollisionTriangle { AX = -10, AY = 0, AZ = -10, BX = -10, BY = 0, BZ = 10, CX = 10, CY = 0, CZ = -10 },
            new CollisionTriangle { AX = -10, AY = 0, AZ = 10, BX = 10, BY = 0, BZ = 10, CX = 10, CY = 0, CZ = -10 },
            new CollisionTriangle { AX = -10, AY = 0, AZ = 1.8f, BX = -10, BY = 6, BZ = 1.8f, CX = 10, CY = 0, CZ = 1.8f },
            new CollisionTriangle { AX = 10, AY = 6, AZ = 1.8f, BX = 10, BY = 0, BZ = 1.8f, CX = -10, CY = 6, CZ = 1.8f } };
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, Owner, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(Owner) });
        sim.RegisterEntity(2, Body, TestHelpers.PlayerState(0, 4.5f) with { EntityId = 2, PY = TestHelpers.GroundPY(Body) });
        Assert.Empty(Run(sim));
        Assert.InRange(sim.GetState(1).PZ, .5f, 1.8f);
    }

    [Fact]
    public void CpuSpendsReadyRAtTheTipWithoutHoldingAim()
    {
        var sim = Make(5);
        var self = sim.GetState(1); var target = sim.GetState(2);
        var policy = new HeuristicBotPolicy();
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        bool used = false;
        for (int tick = 0; tick < 240; tick++)
        {
            var input = policy.Decide(self, target, Owner, new Random(tick), memory);
            if (input.ActiveSlot == AbilitySlots.R) { Assert.False(input.IsAiming); used = true; break; }
        }
        Assert.True(used);
    }

    [Fact]
    public void OptionalTipPresentationDoesNotEmitAnEmptySemanticId()
    {
        var definition = TestHelpers.ResolveSourceDef("wibou", character =>
        {
            var move = character["slots"]!.AsArray().Single(x => (string?)x!["id"] == "ground.R")!;
            move["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!["tipPresentationId"] = "";
        });
        var arena = TestHelpers.TestArena(); arena.Heightmap.OriginX = arena.Heightmap.OriginZ = -100;
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, definition, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(definition) });
        sim.RegisterEntity(2, Body, TestHelpers.PlayerState(0, 5) with { EntityId = 2, PY = TestHelpers.GroundPY(Body) });
        Assert.True(Assert.Single(Run(sim)).FreezesOwner);
        Assert.DoesNotContain(sim.GetPresentationEvents(), x => string.IsNullOrEmpty(x.PresentationId));
    }

    [Fact]
    public void RetiredCapabilityCannotConstruct()
        => Assert.False(InternalCapabilityRegistry.TryCreate("slop.ability.charged-directional-dash.v1", "1", Parameters, out _));
}
