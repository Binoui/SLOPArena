using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// slop.ability.steered-charge.v1, Sir Bonk's joust: a steerable gallop with a lance that ends on its
/// first hit, skids and turns round on a miss, a shield or a wall, and strides on thin air before a drop.
/// </summary>
public sealed class SteeredChargeTests
{
    private const float Speed = 12f;
    private const ushort StartTick = 10;
    internal static readonly string[] Clips = { "anim.r", "anim.r-air", "anim.r-gallop", "anim.r-hit", "anim.r-miss" };
    private const byte AirClip = 1, GallopClip = 2, HitClip = 3, MissClip = 4;

    internal static CookedSteeredChargeCapabilityParameters Parameters { get; } = new(
        Speed, 180f, 60, true,
        new CookedHitbox(AuthoringHitboxShape.Sphere, .6f, 0f, 0f, 1f, 0f, 0f, 0f, null, null,
            10f, 40f, 8f, 30f, 20, 1, true, 0),
        20, 30, 12, true, 15, 10,
        GallopAnimationId: "anim.r-gallop", GallopLoopTicks: 24,
        HitEndingAnimationId: "anim.r-hit", MissEndingAnimationId: "anim.r-miss", AirEndingAnimationId: "anim.r-air");

    private static readonly CharacterDefinition Definition = CreateDefinition();
    private static readonly CharacterDefinition TargetDef = TestHelpers.EngineDef;

    [Fact]
    public void GallopRunsAtItsSpeedThenSkidsAndTurnsRoundWhenItRunsOut()
    {
        var sim = MakeSim();
        StartGallop(sim);
        float startZ = sim.GetState(1).PZ;
        Assert.Equal(GallopClip, sim.GetState(1).ComboStage);
        AssertPhase(sim, GallopClip);

        for (int i = 0; i < 30; i++)
            Tick(sim, default);
        Assert.InRange(sim.GetState(1).PZ - startZ, Speed * 30 / 60f - .05f, Speed * 30 / 60f + .05f);
        Assert.InRange(sim.GetState(1).PX, -.001f, .001f);

        bool skidded = false;
        float lastSpeed = Speed;
        int ticks = 0;
        for (; ticks < 200 && sim.GetActiveAbility(1) != null; ticks++)
        {
            Tick(sim, default);
            var state = sim.GetState(1);
            if (state.ComboStage != MissClip) continue;
            skidded = true;
            AssertPhase(sim, MissClip);
            Assert.True(state.VZ <= lastSpeed + .001f, "the skid only slows down");
            lastSpeed = state.VZ;
        }

        Assert.True(skidded);
        var done = sim.GetState(1);
        Assert.Equal(ActionState.Idle, done.State);
        Assert.InRange(MathF.Abs(done.FacingYaw), MathF.PI - .001f, MathF.PI + .001f);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void StickSteersTheGallopWithinItsTurnRate()
    {
        var sim = MakeSim();
        StartGallop(sim);
        var right = new InputState { MoveX = 1f };
        for (int i = 0; i < 15; i++)
            Tick(sim, right);
        // 180 degrees a second is 3 degrees a tick: 15 ticks turn 45 degrees toward +X.
        Assert.InRange(sim.GetState(1).FacingYaw, MathF.PI / 4f - .06f, MathF.PI / 4f + .06f);

        for (int i = 0; i < 30; i++)
            Tick(sim, right);
        var state = sim.GetState(1);
        Assert.InRange(state.FacingYaw, MathF.PI / 2f - .001f, MathF.PI / 2f + .001f);
        Assert.InRange(MathF.Sqrt(state.VX * state.VX + state.VZ * state.VZ), Speed - .001f, Speed + .001f);
    }

    [Fact]
    public void FirstFighterTheLanceHitsEndsTheGallopWithTheHitEnding()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 6f);
        StartGallop(sim);

        int hits = 0;
        bool hitEnding = false, launched = false;
        for (int i = 0; i < 200 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default);
            hits += sim.LastTickHits.Count(hit => hit.TargetEntityId == 2 && !hit.Blocked);
            var victim = sim.GetState(2);
            launched |= victim.State == ActionState.Hitstun || Simulation.HasKnockback(victim) || victim.HitstopTicks > 0;
            var state = sim.GetState(1);
            if (state.ComboStage == HitClip)
            {
                hitEnding = true;
                AssertPhase(sim, HitClip);
                Assert.Equal(0f, state.VX);
                Assert.Equal(0f, state.VZ);
            }
        }

        Assert.Equal(1, hits);
        Assert.True(hitEnding);
        var target = sim.GetState(2);
        Assert.Equal((ushort)10, target.DamagePercent);
        Assert.True(launched, "the unhorsed fighter is launched");
        Assert.True(sim.GetState(1).PZ < 6f, "the galloper stops at the fighter he unhorses");
        Assert.InRange(sim.GetState(1).FacingYaw, -.001f, .001f);
    }

    [Fact]
    public void ShieldEndsTheGallopWithTheSkid()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 6f, shield: true);
        StartGallop(sim, shieldHeld: true);

        bool blocked = false, skidded = false;
        for (int i = 0; i < 200 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default, shieldHeld: true);
            blocked |= sim.LastTickHits.Any(hit => hit.TargetEntityId == 2 && hit.Blocked);
            skidded |= sim.GetState(1).ComboStage == MissClip;
            Assert.NotEqual(HitClip, sim.GetState(1).ComboStage);
        }

        Assert.True(blocked);
        Assert.True(skidded);
        Assert.Equal((ushort)0, sim.GetState(2).DamagePercent);
    }

    [Fact]
    public void WallEndsTheGallopWithTheSkid()
    {
        var sim = MakeSim(ArenaWithWall(4f));
        StartGallop(sim);

        int gallopTicks = 0;
        bool skidded = false;
        for (int i = 0; i < 200 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default);
            if (sim.GetState(1).ComboStage == GallopClip) gallopTicks++;
            skidded |= sim.GetState(1).ComboStage == MissClip;
        }

        Assert.True(skidded);
        Assert.True(gallopTicks < 30, $"the wall ends the gallop early ({gallopTicks} ticks)");
        Assert.True(sim.GetState(1).PZ < 4f);
    }

    [Fact]
    public void ArmourAbsorbsHitsWhileGalloping()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, -8f);
        StartGallop(sim);
        var galloper = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            X = galloper.PX, Y = galloper.PY, Z = galloper.PZ, Radius = 1f, Shape = HitboxShape.Sphere,
            Damage = 7f, BaseKnockback = 20f, KnockbackGrowth = 50f, KnockbackAngle = 45, StunTicks = 20,
            DurationTicks = 1, OwnerId = 2, ActivationId = 999,
        });
        Tick(sim, default);

        Assert.Contains(sim.LastTickHits, hit => hit.TargetEntityId == 1 && hit.ArmorProtected);
        var state = sim.GetState(1);
        Assert.Equal((ushort)7, state.DamagePercent);
        Assert.Equal(ActionState.Attacking, state.State);
        Assert.NotNull(sim.GetActiveAbility(1));
    }

    [Fact]
    public void AirChargeStridesLevelThenLooksDownAndDrops()
    {
        var sim = MakeSim(airborneAt: 5f);
        StartGallop(sim);
        float strideY = sim.GetState(1).PY;

        int strides = 0, lookDown = 0;
        for (int i = 0; i < 100 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default);
            var state = sim.GetState(1);
            if (sim.GetActiveAbility(1) == null) break;
            Assert.InRange(state.PY, strideY - .001f, strideY + .001f);
            if (state.ComboStage == AirClip)
            {
                AssertPhase(sim, AirClip);
                lookDown++;
                Assert.Equal(0f, state.VZ);
            }
            else
            {
                strides++;
                Assert.InRange(state.VZ, Speed - .001f, Speed + .001f);
            }
        }

        Assert.Null(sim.GetActiveAbility(1));
        Assert.InRange(strides, 14, 17);
        Assert.InRange(lookDown, 9, 11);
        Tick(sim, default);
        Tick(sim, default);
        var falling = sim.GetState(1);
        Assert.False(falling.IsGrounded);
        Assert.True(falling.PY < strideY, "after the look-down he falls");
        Assert.NotEqual(ActionState.Attacking, falling.State);
    }

    [Fact]
    public void GallopingOffALedgeStridesOnThinAirAndDrops()
    {
        var sim = MakeSim(LedgeArena(edgeZ: 8));
        StartGallop(sim);

        bool lookedDown = false, skidded = false;
        for (int i = 0; i < 200 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default);
            lookedDown |= sim.GetState(1).ComboStage == AirClip;
            skidded |= sim.GetState(1).ComboStage == MissClip;
        }

        Assert.True(lookedDown);
        Assert.False(skidded);
        var state = sim.GetState(1);
        Assert.False(state.IsGrounded);
        Assert.True(state.PZ > 8f);
    }

    [Fact]
    public void InterruptionRemovesTheLance()
    {
        var sim = MakeSim();
        StartGallop(sim);
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());

        var hit = sim.GetState(1);
        hit.State = ActionState.Hitstun;
        hit.HitstunTicks = 20;
        sim.SetState(1, hit);
        Tick(sim, default);

        Assert.Null(sim.GetActiveAbility(1));
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    private static ServerSimulation MakeSim(ArenaDefinition? arena = null, float? airborneAt = null)
    {
        var sim = new ServerSimulation(arena ?? TestHelpers.TestArena());
        var state = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(Definition) };
        if (airborneAt.HasValue)
            state = state with { PY = airborneAt.Value, IsGrounded = false };
        sim.RegisterEntity(1, Definition, state);
        return sim;
    }

    private static void AssertPhase(ServerSimulation simulation, byte clip)
    {
        Assert.Equal(clip, simulation.GetState(1).AnimIndex);
        var move = Assert.IsType<ActiveMoveView>(simulation.GetActiveMove(1));
        Assert.Equal(clip, move.ClipIndex);
        Assert.Equal(Clips[clip], move.Pose.AnimationId);
    }

    private static void RegisterTarget(ServerSimulation sim, float x, float z, bool shield = false)
        => sim.RegisterEntity(2, TargetDef, TestHelpers.PlayerState(x, z) with
        {
            EntityId = 2,
            PY = TestHelpers.GroundPY(TargetDef),
            State = shield ? ActionState.Shielding : ActionState.Idle,
        });

    /// <summary>Press R and tick through the startup until the gallop is moving.</summary>
    private static void StartGallop(ServerSimulation sim, bool shieldHeld = false)
    {
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.R }, shieldHeld);
        for (int i = 0; i < StartTick + 2 && sim.GetState(1).VZ <= 0f; i++)
            Tick(sim, default, shieldHeld);
        Assert.True(sim.GetState(1).VZ > 0f, "the gallop starts after the rear-up");
    }

    private static void Tick(ServerSimulation sim, InputState attacker, bool shieldHeld = false)
    {
        var inputs = new Dictionary<ulong, InputState> { [1] = attacker };
        if (shieldHeld)
            inputs[2] = new InputState { ShieldHeld = true };
        sim.Tick(inputs);
    }

    internal static CookedSlotDefinition Slot(int ordinal, string id, bool air) => new(
        ordinal, id, air, "Joust fixture", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
        0, false, false,
        new CookedTimeline(new[]
        {
            new CookedStage(30, 0, 0, 0, 0, Clips, new CookedTimelineOperation[]
            {
                new CookedStartCapabilityOperation(StartTick, AuthoringUnit.Ticks,
                    CharacterPackageCompiler.SteeredChargeCapabilityId,
                    CharacterPackageCompiler.SteeredChargeCapabilityVersion, Parameters),
            }),
        }));

    private static CharacterDefinition CreateDefinition()
    {
        var definition = TestHelpers.EngineDef;
        var slots = definition.CookedSlots!.ToArray();
        slots[5] = Slot(5, "ground.R", false);
        slots[12] = Slot(12, "air.R", true);
        definition = definition with { CookedSlots = slots };
        return definition;
    }

    /// <summary>A floor at y = 0 that ends at <paramref name="edgeZ"/>, with nothing beyond.</summary>
    private static ArenaDefinition LedgeArena(int edgeZ)
    {
        var arena = TestHelpers.TestArena();
        var data = arena.Heightmap.Data;
        for (int z = edgeZ; z < arena.Heightmap.Height; z++)
            for (int x = 0; x < arena.Heightmap.Width; x++)
                data[z * arena.Heightmap.Width + x] = float.MinValue;
        return arena;
    }

    private static ArenaDefinition ArenaWithWall(float wallZ)
    {
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = -10f, AY = 0f, AZ = -10f, BX = -10f, BY = 0f, BZ = 10f, CX = 10f, CY = 0f, CZ = -10f },
            new CollisionTriangle { AX = -10f, AY = 0f, AZ = 10f, BX = 10f, BY = 0f, BZ = 10f, CX = 10f, CY = 0f, CZ = -10f },
            new CollisionTriangle { AX = -10f, AY = 0f, AZ = wallZ, BX = -10f, BY = 3f, BZ = wallZ, CX = 10f, CY = 0f, CZ = wallZ },
            new CollisionTriangle { AX = 10f, AY = 3f, AZ = wallZ, BX = 10f, BY = 0f, BZ = wallZ, CX = -10f, CY = 3f, CZ = wallZ },
        };
        arena.MinX = arena.MinZ = -10f;
        arena.MaxX = arena.MaxZ = 10f;
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }
}
