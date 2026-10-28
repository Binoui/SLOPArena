using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class TimelineRuntimeTests
{
    [Fact]
    public void ANewCapabilityWithoutAClipSelectionKeepsItsAuthoredLaterStagePose()
    {
        var definition = TestHelpers.ResolveSourceDef("manki", source =>
        {
            var move = source["slots"]!.AsArray().Single(slot => slot!["id"]!.GetValue<string>() == "ground.E")!;
            var stages = move["timeline"]!["stages"]!.AsArray();
            var preparation = stages[0]!.DeepClone().AsObject();
            preparation["durationTicks"] = 4;
            preparation["iasaTicks"] = 0;
            preparation["landingLagTicks"] = 0;
            preparation["autoCancelBeforeTicks"] = 0;
            preparation["autoCancelAfterTicks"] = 0;
            preparation["animationIds"] = new System.Text.Json.Nodes.JsonArray("anim.manki.idle");
            preparation["operations"] = new System.Text.Json.Nodes.JsonArray();
            stages.Insert(0, preparation);
        });
        var sim = TestHelpers.MakeSim();
        var initial = TestHelpers.PlayerState(20, 10) with { PY = TestHelpers.GroundPY(definition) };
        sim.RegisterEntity(1, definition, initial);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.E } });
        TestHelpers.TickDefault(sim, 4);
        var active = Assert.IsType<ActiveMoveView>(sim.GetActiveMove(1));
        Assert.Equal(1, active.StageIndex);
        Assert.Equal((byte)1, sim.GetState(1).AnimIndex);
        Assert.Equal((byte)1, active.ClipIndex);
        Assert.Equal("anim.manki.ge", active.Pose.AnimationId);
        var restored = TestHelpers.MakeSim();
        restored.RegisterEntity(1, definition, initial);
        restored.RestoreSnapshot(sim.CaptureSnapshot());
        Assert.Equal(active, restored.GetActiveMove(1));
        for (int tick = 0; tick < 20; tick++)
        {
            TestHelpers.TickDefault(sim, 1);
            TestHelpers.TickDefault(restored, 1);
            Assert.True(sim.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StageAfterPhaseClipsUsesItsOwnClipWithoutChangingIasaClock(bool startsCapability)
    {
        CookedTimelineOperation[] operations = startsCapability
            ? new CookedTimelineOperation[]
            {
                new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                    "slop.internal.fightguy.cyclone-kick.v1", "1",
                    new CookedCycloneKickCapabilityParameters(0, 1, 3, 3, 1, 1, 1, 7, 15, 8, 5, 6, 1, 1)),
            }
            : Array.Empty<CookedTimelineOperation>();
        var slot = new CookedSlotDefinition(0, "ground.1", false, "Phase stages", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(4, 0, 0, 0, 0, new[] { "first", "first.phase" }, operations),
                new CookedStage(12, 6, 0, 0, 0, new[] { "second" }, Array.Empty<CookedTimelineOperation>()),
            }));
        var definition = TestHelpers.EngineDef with { CookedSlots = new[] { slot } };
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState(10, 10); state.PY = TestHelpers.GroundPY(definition);
        sim.RegisterEntity(1, definition, state);
        for (int tick = 0; tick < 11; tick++)
        {
            sim.Tick(new() { [1] = new InputState { ActiveSlot = tick == 0 ? AbilitySlots.Slot1 : (byte)0 } });
            var active = sim.GetActiveMove(1)!.Value;
            if (active.StageIndex != 1) continue;
            state = sim.GetState(1);
            Assert.Equal((byte)1, state.ComboStage);
            Assert.Equal((byte)2, active.ClipIndex);
            Assert.Equal((byte)2, state.AnimIndex);
            Assert.Equal("second", active.Pose.AnimationId);
            Assert.False(active.Pose.IsPhaseClip);
            Assert.Equal(Math.Max(0, state.AttackElapsedTicks - 4), active.Pose.ElapsedTicks);
            Assert.Equal(state.AttackElapsedTicks >= 10, Simulation.IsIasaUnlocked(in state, active));
        }
        Assert.Equal(1, sim.GetActiveMove(1)!.Value.StageIndex);
    }

    [Fact]
    public void SameTickOperationsPreserveOrderAndCompleteImmediately()
    {
        var hitbox = Hitbox(2, duration: 3);
        var slot = Slot(10,
            new CookedSetVelocityOperation(0, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Absolute, 1f, 2f, 3f),
            new CookedSetVelocityOperation(0, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Additive, .5f, 0f, 0f),
            new CookedSpawnHitboxOperation(0, AuthoringUnit.Meters, hitbox),
            new CookedSpawnHitboxOperation(0, AuthoringUnit.Meters, hitbox),
            new CookedEmitPresentationOperation(0, AuthoringUnit.Ticks, "presentation.hit", 4),
            new CookedCompleteTimelineOperation(0, AuthoringUnit.Ticks),
            new CookedSetVelocityOperation(0, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Absolute, 99f, 99f, 99f));
        var (sim, def) = Create(slot);

        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        var state = sim.GetState(1);

        Assert.Equal(ActionState.Idle, state.State);
        Assert.Equal((byte)0, state.AttackSlot);
        TestHelpers.AssertNear(1.5f, state.VX);
        TestHelpers.AssertNear(2f, state.VY);
        TestHelpers.AssertNear(3f, state.VZ);
        Assert.Equal(2, sim.Resolver.GetActiveHitboxes().Count);
        var presentation = Assert.Single(sim.GetPresentationEvents());
        Assert.Equal(0u, presentation.MatchTick);
        Assert.Equal(1ul, presentation.EntityId);
        Assert.Equal(4, presentation.OperationIndex);
        Assert.Equal(new PresentationEventKey(0, 1, 1, PresentationEventSource.Timeline, 4), presentation.Key);
        Assert.Equal("presentation.hit", presentation.PresentationId);

        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Equal(2, sim.Resolver.GetActiveHitboxes().Count);
    }

    [Fact]
    public void ForwardLungeCapturesFacingAndStopsAfterAuthoredDuration()
    {
        var slot = Slot(10,
            new CookedForwardLungeOperation(2, AuthoringUnit.MetersPerSecond, 12f, 3));
        var (sim, def) = Create(slot, TestHelpers.PlayerState() with { FacingYaw = MathF.PI / 2f });
        var inputs = new Dictionary<ulong, InputState> { [1] = default };

        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        sim.TickAbilities(inputs);
        sim.TickAbilities(inputs);
        var state = sim.GetState(1);
        TestHelpers.AssertNear(12f, state.VX);
        TestHelpers.AssertNear(0f, state.VZ);

        state.FacingYaw = 0f;
        sim.SetState(1, state);
        sim.TickAbilities(inputs);
        sim.TickAbilities(inputs);
        state = sim.GetState(1);
        TestHelpers.AssertNear(12f, state.VX);
        TestHelpers.AssertNear(0f, state.VZ);

        // Tick 5: lunge duration (3 ticks) has completed; horizontal velocity stops.
        sim.TickAbilities(inputs);
        state = sim.GetState(1);
        TestHelpers.AssertNear(0f, state.VX);
        TestHelpers.AssertNear(0f, state.VZ);
    }

    [Theory]
    [InlineData(0f, 1.3f, 0f, true)]
    [InlineData(0f, 10f, 0f, false)]
    [InlineData(0f, -1.3f, 0f, false)]
    [InlineData(2f, 1.3f, 0f, false)]
    [InlineData(0f, 1.3f, 3f, false)]
    public void RangeAwareLungeBrakesOnlyForReachableForwardHurtboxes(
        float x, float z, float height, bool brakes)
    {
        var slot = BrakingSlot();
        var (sim, def) = Create(slot);
        sim.RegisterEntity(100, def, TestHelpers.NpcState(x, z)
            with { PY = sim.GetState(1).PY + height });
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 0, def);

        TestHelpers.AssertNear(brakes ? 0f : 12f, sim.GetState(1).VZ);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        for (int tick = 0; tick < 5; tick++)
            sim.TickAbilities(new());
        Assert.Single(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void RangeAwareLungeBrakesOnApproachWithoutRestartingOrAdvancingSwings()
    {
        var slot = BrakingSlot();
        var (sim, def) = Create(slot);
        sim.RegisterEntity(100, def, TestHelpers.NpcState(0f, 2.5f));
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 0, def);
        Assert.True(sim.GetState(1).VZ > 0f);
        sim.SetState(1, sim.GetState(1) with { PZ = 1.2f, VY = 3f });
        sim.TickAbilities(new());
        TestHelpers.AssertNear(0f, sim.GetState(1).VZ);
        TestHelpers.AssertNear(3f, sim.GetState(1).VY);
        sim.SetState(100, sim.GetState(100) with { PZ = 10f });
        for (int tick = 1; tick < 4; tick++)
        {
            sim.TickAbilities(new());
            TestHelpers.AssertNear(0f, sim.GetState(1).VZ);
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }
        sim.TickAbilities(new());
        Assert.Single(sim.Resolver.GetActiveHitboxes());
        sim.SetState(1, sim.GetState(1) with { State = ActionState.Hitstun, VZ = -4f });
        sim.TickAbilities(new());
        Assert.Null(sim.GetActiveAbility(1));
        TestHelpers.AssertNear(-4f, sim.GetState(1).VZ);
    }

    [Theory]
    [InlineData(1.3f, false)]
    [InlineData(2.5f, false)]
    [InlineData(1.3f, true)]
    [InlineData(2.5f, true)]
    public void RangeAwareLungeStopsBeforePassingTargetAndFirstSwingConnects(float distance, bool airborne)
    {
        var slot = BrakingSlot();
        float height = airborne ? 20f : .75f;
        var (sim, def) = Create(slot, TestHelpers.PlayerState() with { PY = height, IsGrounded = !airborne });


        def = def with { Movement = def.Movement with { Gravity = 0f } };
        sim.NoGravityEntityId = 1;
        sim.RegisterEntity(100, def, TestHelpers.NpcState(0f, distance)
            with { PY = height, IsGrounded = !airborne });
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        var inputs = new Dictionary<ulong, InputState> { [1] = default, [100] = default };
        bool connected = false;
        for (int tick = 0; tick < 12; tick++)
        {
            sim.Tick(inputs);
            connected |= sim.LastTickHits.Any(hit => hit.OwnerEntityId == 1 && hit.TargetEntityId == 100);
            Assert.True(sim.GetState(1).PZ < sim.GetState(100).PZ);
        }
        Assert.True(connected);
        Assert.True(sim.GetState(100).DamagePercent > 0);
    }

    private static CookedSlotDefinition BrakingSlot()
        => Slot(20,
            new CookedForwardLungeOperation(0, AuthoringUnit.MetersPerSecond, 12f, 10, true),
            new CookedSpawnHitboxOperation(5, AuthoringUnit.Meters,
                new CookedHitbox(AuthoringHitboxShape.Sphere, .4f,
                    0f, 0f, 1f, 0f, 0f, 0f, null, null,
                    3f, 20f, 2f, 4f, 5, 2, true, 0)));

    [Fact]
    public void AerialAuthoringSuspendsAmbientGravityButPreservesAuthoredVelocity()
    {
        var slot = new CookedSlotDefinition(7, "air.1", true, "Test", "Test", "icon.test",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(100, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<CookedTimelineOperation>()),
            }));
        var initial = TestHelpers.PlayerState() with { PY = 20f, IsGrounded = false };
        var (sim, def) = Create(slot, initial);
        sim.NoGravityEntityId = 1;
        sim.RegisterEntity(2, def, initial with { EntityId = 2, PX = 10f });
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        var inputs = new Dictionary<ulong, InputState> { [1] = default, [2] = default };

        for (int tick = 0; tick < 44; tick++)
            sim.Tick(inputs);
        TestHelpers.AssertNear(20f, sim.GetState(1).PY);
        TestHelpers.AssertNear(0f, sim.GetState(1).VY);
        Assert.True(sim.GetState(2).PY < 20f);
        Assert.True(sim.GetState(2).VY < 0f);

        var launch = Slot(4,
            new CookedSetVelocityOperation(0, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Absolute, 0f, 3f, 0f));
        sim.ActivateAbility(1, new CookedTimelineAbility(launch), 2, def);
        for (int tick = 0; tick < 5; tick++)
            sim.Tick(inputs);
        TestHelpers.AssertNear(3f, sim.GetState(1).VY);
        Assert.True(sim.GetState(1).PY > 20f);

        sim.NoGravityEntityId = null;
        sim.Tick(inputs);
        Assert.True(sim.GetState(1).VY < 3f);
    }

    [Fact]
    public void GravityWindowScalesAirGravityForItsDurationThenRestoresIt()
    {
        var slot = Slot(6, new CookedGravityWindowOperation(0, AuthoringUnit.Normalized, 0.5f, 2));
        var initial = TestHelpers.PlayerState() with
        {
            PY = 20f,
            VY = 0f,
            AirTimeTicks = 100,
            IsGrounded = false,
        };
        var (sim, def) = Create(slot, initial);
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 0, def);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };

        sim.Tick(inputs);
        var state = sim.GetState(1);
        TestHelpers.AssertNear(-def.Movement.Gravity * Simulation.TickDt * 0.5f, state.VY);

        sim.Tick(inputs);
        state = sim.GetState(1);
        TestHelpers.AssertNear(-def.Movement.Gravity * Simulation.TickDt, state.VY);

        sim.Tick(inputs);
        state = sim.GetState(1);
        TestHelpers.AssertNear(-def.Movement.Gravity * Simulation.TickDt * 2f, state.VY);
    }

    [Fact]
    public void GravityWindowDoesNotOwnVerticalMotionAndFastFallOverridesIt()
    {
        var slot = Slot(6, new CookedGravityWindowOperation(0, AuthoringUnit.Normalized, 0.5f, 3));
        var initial = TestHelpers.PlayerState() with
        {
            PY = 20f,
            VY = -1f,
            AirTimeTicks = 100,
            IsGrounded = false,
        };
        var (sim, def) = Create(slot, initial);
        var ability = new CookedTimelineAbility(slot);
        sim.ActivateAbility(1, ability, 0, def);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new InputState { DownPressed = true },
        });

        Assert.False(ability.OwnsVerticalMotion);
        Assert.True(sim.GetState(1).IsFastFalling);
        TestHelpers.AssertNear(-def.Movement.FastFallSpeed, sim.GetState(1).VY);
    }

    [Fact]
    public void GravityWindowIsRemovedImmediatelyWhenItsAbilityIsCancelled()
    {
        var slot = Slot(20, new CookedGravityWindowOperation(0, AuthoringUnit.Normalized, 0.5f, 10));
        var initial = TestHelpers.PlayerState() with
        {
            PY = 20f,
            VY = 0f,
            AirTimeTicks = 100,
            IsGrounded = false,
        };
        var (sim, def) = Create(slot, initial);
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 0, def);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };
        sim.Tick(inputs);
        var state = sim.GetState(1);
        TestHelpers.AssertNear(-def.Movement.Gravity * Simulation.TickDt * 0.5f, state.VY);

        state.State = ActionState.Idle;
        state.AttackSlot = 0;
        sim.SetState(1, state);
        sim.TickAbilities(inputs);
        Assert.Null(sim.GetActiveAbility(1));

        sim.Tick(inputs);
        state = sim.GetState(1);
        TestHelpers.AssertNear(-def.Movement.Gravity * Simulation.TickDt * 1.5f, state.VY);
    }

    [Fact]
    public void LatestGravityWindowReplacesAnOverlappingWindow()
    {
        var slot = Slot(8,
            new CookedGravityWindowOperation(0, AuthoringUnit.Normalized, 0.5f, 6),
            new CookedGravityWindowOperation(2, AuthoringUnit.Normalized, 0.25f, 2));
        var initial = TestHelpers.PlayerState() with
        {
            PY = 20f,
            VY = 0f,
            AirTimeTicks = 100,
            IsGrounded = false,
        };
        var (sim, def) = Create(slot, initial);
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 0, def);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };
        float gravityPerTick = def.Movement.Gravity * Simulation.TickDt;
        float elapsedGravity = 0f;
        foreach (float scale in new[] { 0.5f, 0.5f, 0.25f, 0.25f, 1f })
        {
            sim.Tick(inputs);
            elapsedGravity += scale;
            TestHelpers.AssertNear(-gravityPerTick * elapsedGravity, sim.GetState(1).VY);
        }
    }

    [Fact]
    public void TimelineOwnershipClearsLateVerticalWritesButNotHorizontalLungeFastFall()
    {
        var verticalSlot = Slot(6,
            new CookedSetVelocityOperation(1, AuthoringUnit.MetersPerSecond, AuthoringVelocityMode.Absolute, 2f, 3f, 4f));
        var initial = TestHelpers.PlayerState() with { IsFastFalling = true, SlideAttackCarryActive = true };
        var (sim, def) = Create(verticalSlot, initial);
        sim.ActivateAbility(1, new CookedTimelineAbility(verticalSlot), 2, def);
        sim.TickAbilities(new Dictionary<ulong, InputState> { [1] = default });
        var state = sim.GetState(1);
        Assert.False(state.IsFastFalling);
        Assert.False(state.SlideAttackCarryActive);

        var horizontalSlot = Slot(6,
            new CookedForwardLungeOperation(1, AuthoringUnit.MetersPerSecond, 8f, 2));
        initial = TestHelpers.PlayerState() with { IsFastFalling = true, SlideAttackCarryActive = true };
        (sim, def) = Create(horizontalSlot, initial);
        var horizontalAbility = new CookedTimelineAbility(horizontalSlot);
        sim.ActivateAbility(1, horizontalAbility, 2, def);
        sim.TickAbilities(new Dictionary<ulong, InputState> { [1] = default });
        state = sim.GetState(1);
        Assert.True(state.IsFastFalling);
        Assert.False(state.SlideAttackCarryActive);
        Assert.False(horizontalAbility.OwnsVerticalMotion);
    }

    [Fact]
    public void LateVerticalCapabilityEntryClearsFastFallAndSlideCarry()
    {
        var slot = Slot(6, new CookedStartCapabilityOperation(1, AuthoringUnit.Ticks,
            "slop.internal.fightguy.cyclone-kick.v1", "1",
            new CookedCycloneKickCapabilityParameters(8.5f, 1, 3, 3, 1, 1, 1, 7, 15, 8, 5, 6, 1, 1)));
        var initial = TestHelpers.PlayerState() with { IsFastFalling = true, SlideAttackCarryActive = true };
        var (sim, def) = Create(slot, initial);
        var ability = new CookedTimelineAbility(slot);
        sim.ActivateAbility(1, ability, 2, def);
        sim.TickAbilities(new Dictionary<ulong, InputState> { [1] = default });
        var state = sim.GetState(1);
        Assert.False(state.IsFastFalling);
        Assert.False(state.SlideAttackCarryActive);
        Assert.True(ability.OwnsVerticalMotion);
    }

    [Fact]
    public void OutOfOrderCookedOperationsAreSortedAndExecutedInChronologicalTickOrder()
    {
        var manifest = File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/fightguy/package.json"));
        var character = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(RepoFile("client/Unity/Assets/CharacterPackages/fightguy/character.json")))!.AsObject();
        character["slots"]![0]!["allowSlideCarry"] = false;
        var stage = (System.Text.Json.Nodes.JsonObject)character["slots"]![0]!["timeline"]!["stages"]![0]!;
        stage["operations"] = new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject
            {
                ["kind"] = "spawnHitbox", ["tick"] = 4, ["unit"] = "meters",
                ["hitbox"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["shape"] = "sphere", ["radius"] = 0.5, ["offsetX"] = 0, ["offsetY"] = 0, ["offsetZ"] = 0,
                    ["endOffsetX"] = 0, ["endOffsetY"] = 0, ["endOffsetZ"] = 0, ["startBoneId"] = null, ["endBoneId"] = null,
                    ["damage"] = 10, ["angle"] = 45, ["baseKnockback"] = 5, ["knockbackGrowth"] = 20, ["stunTicks"] = 10,
                    ["durationTicks"] = 2, ["interruptible"] = false, ["hitGroup"] = 0, ["knockbackDirection"] = "awayFromOwner"
                }
            },
            new System.Text.Json.Nodes.JsonObject
            {
                ["kind"] = "forwardLunge", ["tick"] = 2, ["unit"] = "metersPerSecond", ["speed"] = 10, ["durationTicks"] = 2
            }
        };

        var compileResult = CharacterPackageCompiler.Compile(manifest, character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compileResult.CookedPackage != null, string.Join("; ", compileResult.Diagnostics.Select(d => $"{d.Code}: {d.Message} ({d.Path})")));
        var cookedSlot = compileResult.CookedPackage!.Definition.Slots[0];
        Assert.Equal(2, cookedSlot.Timeline.Stages[0].Operations.Count);
        Assert.Equal(2, cookedSlot.Timeline.Stages[0].Operations[0].Tick);
        Assert.IsType<CookedForwardLungeOperation>(cookedSlot.Timeline.Stages[0].Operations[0]);
        Assert.Equal(4, cookedSlot.Timeline.Stages[0].Operations[1].Tick);
        Assert.IsType<CookedSpawnHitboxOperation>(cookedSlot.Timeline.Stages[0].Operations[1]);

        var (sim, def) = Create(cookedSlot, TestHelpers.PlayerState() with { FacingYaw = MathF.PI / 2f });
        var inputs = new Dictionary<ulong, InputState> { [1] = default };

        sim.ActivateAbility(1, new CookedTimelineAbility(cookedSlot), 0, def);
        sim.TickAbilities(inputs); // tick 1
        sim.TickAbilities(inputs); // tick 2: lunge begins
        var state = sim.GetState(1);
        TestHelpers.AssertNear(10f, state.VX);

        sim.TickAbilities(inputs); // tick 3: lunge tick 2
        sim.TickAbilities(inputs); // tick 4: lunge completes (stops), hitbox triggers
        state = sim.GetState(1);
        TestHelpers.AssertNear(0f, state.VX);
        Assert.Single(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void ProjectileUsesAimDirectionAndMaxFlightLifetime()
    {
        var slot = Slot(5, new CookedSpawnProjectileOperation(0, AuthoringUnit.Meters,
            new CookedProjectile(0f, 1f, 0f, 12f, 2f, .5f, 4f, 30, 3f, 4f, 6, 7)));
        var (sim, def) = Create(slot, TestHelpers.PlayerState() with { AimYaw = MathF.PI / 2f, AimPitch = 0f });

        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        var projectile = Assert.Single(sim.Resolver.GetActiveHitboxes());

        TestHelpers.AssertNear(12f, projectile.VX);
        TestHelpers.AssertNear(0f, projectile.VY);
        TestHelpers.AssertNear(0f, projectile.VZ);
        Assert.Equal((ushort)7, projectile.DurationTicks);
        TestHelpers.AssertNear(1f, projectile.Y - TestHelpers.PlayerState().PY);
    }

    [Fact]
    public void AimStateTransitionsAndMultiStageAdvanceUseCookedTiming()
    {
        var slot = new CookedSlotDefinition(0, "ground.1", false, "Test", "Test", "icon.test",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.GroundCursor, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(2, 0, 0, 0, 0, new[] { "anim.one" }, new CookedTimelineOperation[]
                {
                    new CookedSetAimStateOperation(0, AuthoringUnit.Ticks, AuthoringAimMode.GroundCursor),
                }),
                new CookedStage(2, 0, 0, 0, 0, new[] { "anim.two" }, new CookedTimelineOperation[]
                {
                    new CookedSetAimStateOperation(0, AuthoringUnit.Ticks, AuthoringAimMode.None),
                }),
            }));
        var (sim, def) = Create(slot);
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Equal((byte)0, sim.GetState(1).ComboStage);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Equal((byte)1, sim.GetState(1).ComboStage);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void CycloneStopsPropulsionAtCapabilityDurationWithoutEndingRecovery()
    {
        var slot = Slot(8, new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
            "slop.internal.fightguy.cyclone-kick.v1", "1",
            new CookedCycloneKickCapabilityParameters(8.5f, 1, 3, 3, 1, 1, 1, 7, 15, 8, 5, 6, 1, 1)));
        var (sim, def) = Create(slot);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        for (var tick = 0; tick < 2; tick++)
            sim.TickAbilities(inputs);
        TestHelpers.AssertNear(8.5f, sim.GetState(1).VZ);

        var state = sim.GetState(1);
        state.VY = -2f;
        sim.SetState(1, state);
        sim.TickAbilities(inputs);
        state = sim.GetState(1);
        TestHelpers.AssertNear(0f, state.VX);
        TestHelpers.AssertNear(0f, state.VZ);
        TestHelpers.AssertNear(-2f, state.VY);
        Assert.Equal(ActionState.Attacking, state.State);

        // After braking once, the expired capability no longer owns velocity.
        state.VX = 3f;
        state.VZ = 4f;
        sim.SetState(1, state);
        sim.TickAbilities(inputs);
        state = sim.GetState(1);
        TestHelpers.AssertNear(3f, state.VX);
        TestHelpers.AssertNear(4f, state.VZ);
        TestHelpers.AssertNear(-2f, state.VY);
        for (var tick = 4; tick < 8; tick++)
            sim.TickAbilities(inputs);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void CycloneInterruptionDoesNotBrakeIncomingKnockback()
    {
        var slot = Slot(8, new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
            "slop.internal.fightguy.cyclone-kick.v1", "1",
            new CookedCycloneKickCapabilityParameters(8.5f, 1, 3, 3, 1, 1, 1, 7, 15, 8, 5, 6, 1, 1)));
        var (sim, def) = Create(slot);
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        var state = sim.GetState(1);
        state.State = ActionState.Hitstun;
        state.VX = -4f;
        state.VY = 6f;
        state.VZ = -5f;
        sim.SetState(1, state);
        sim.TickAbilities(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Null(sim.GetActiveAbility(1));
        state = sim.GetState(1);
        TestHelpers.AssertNear(-4f, state.VX);
        TestHelpers.AssertNear(6f, state.VY);
        TestHelpers.AssertNear(-5f, state.VZ);
    }

    [Fact]
    public void InternalCapabilityRegistryAdmitsOnlyExactTypedEntries()
    {
        var cases = new (string Id, CookedCapabilityParameters Parameters, Type Type)[]
        {
            ("slop.internal.fightguy.rising-dragon.v1", new CookedRisingDragonCapabilityParameters(11, 12, 8), typeof(FightGuyRisingKick)),
            ("slop.internal.fightguy.cyclone-kick.v1", new CookedCycloneKickCapabilityParameters(17, 6, 34, 40, 1, 1, 1, 7, 15, 8, 5, 6, 1, 1), typeof(FightGuyCycloneKick)),
        };
        foreach (var item in cases)
        {
            Assert.True(InternalCapabilityRegistry.TryCreate(item.Id, "1", item.Parameters, out var capability));
            Assert.IsType(item.Type, capability);
        }

        Assert.False(InternalCapabilityRegistry.TryCreate(cases[0].Id, "2", cases[0].Parameters, out _));
        Assert.False(InternalCapabilityRegistry.TryCreate("slop.internal.fightguy.unknown.v1", "1", cases[0].Parameters, out _));
        Assert.False(InternalCapabilityRegistry.TryCreate(cases[0].Id, "1", cases[1].Parameters, out _));
    }

    [Fact]
    public void CapabilityCancellationClearsAimWithoutClearingVelocity()
    {
        var slot = Slot(10,
            new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                "slop.internal.manki.round-bomb.v2", "1",
                new CookedMankiRoundBombCapabilityParameters(10, 12f, 30f, 30f, .6f, 6f, 22, 90, 30, 4f, 2.3f, 5f, 9f, 15, 4, 45)),
            new CookedSetAimStateOperation(0, AuthoringUnit.Ticks, AuthoringAimMode.CameraForward3D));
        var initial = TestHelpers.PlayerState() with { PY = 100f, IsGrounded = false, VX = 4f, VZ = 5f };
        var (sim, def) = Create(slot, initial);
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        var state = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, state.State);
        state.State = ActionState.Hitstun;
        sim.SetState(1, state);
        sim.TickAbilities(new Dictionary<ulong, InputState> { [1] = default });

        Assert.Null(sim.GetActiveAbility(1));
        state = sim.GetState(1);
        Assert.False(state.IsAiming);
        TestHelpers.AssertNear(4f, state.VX);
        TestHelpers.AssertNear(5f, state.VZ);
    }
    [Fact]
    public void InterruptionAndRemovalCancelWithoutNaturalEnd()
    {
        var sim = TestHelpers.MakeSim();
        var def = TestHelpers.EngineDef;
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        var probe = new ProbeAbility();
        sim.ActivateAbility(1, probe, 0, def);
        var state = sim.GetState(1);
        state.State = ActionState.Hitstun;
        sim.SetState(1, state);
        sim.TickAbilities(new Dictionary<ulong, InputState> { [1] = default });

        Assert.Equal(1, probe.CancelCount);
        Assert.Equal(0, probe.EndCount);
        Assert.Null(sim.GetActiveAbility(1));

        var removalSim = TestHelpers.MakeSim();
        removalSim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        var removalProbe = new ProbeAbility();
        removalSim.ActivateAbility(1, removalProbe, 0, def);
        removalSim.RemoveEntity(1);
        Assert.Equal(1, removalProbe.CancelCount);
        Assert.Equal(0, removalProbe.EndCount);
    }

    private sealed class ProbeAbility : ServerAbility
    {
        public int CancelCount;
        public int EndCount;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
            => s.State = ActionState.Attacking;

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def) { }
        public override void OnEnd(ref CharacterState s) => EndCount++;
        public override void OnCancel(ref CharacterState s) => CancelCount++;
    }


    [Fact]
    public void FightGuyCookedResolutionCoversSevenWireNormalAndSpecialSlots()
    {
        var def = TestHelpers.FightGuyDef;
        var expectedGround = new[] { "ground.1", "ground.E", "ground.R", "ground.F", "ground.3", "ground.4", "ground.A" };
        var wireSlots = new byte[] { 3, 4, 5, 6, 8, 9, 11 };
        for (var i = 0; i < wireSlots.Length; i++)
        {
            Assert.Equal(expectedGround[i], def.GetCookedSlotAbility(wireSlots[i], false)!.Id);
            Assert.Equal("air." + expectedGround[i].Substring(expectedGround[i].IndexOf('.') + 1), def.GetCookedSlotAbility(wireSlots[i], true)!.Id);
        }
        Assert.Null(def.GetCookedSlotAbility(1, false));
        Assert.Null(def.GetCookedSlotAbility(AbilitySlots.Slot2, false));
        Assert.Null(def.GetCookedSlotAbility(10, false));
    }



    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupCorrectionUsesCurrentSelectionAndLockNeverBypassesRate(bool locked)
    {
        var slot = CorrectionSlot(6);
        var initial = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.EngineDef.CapsuleHeight * .5f,
            LockOn = locked,
            TargetEntityId = 100,
        };
        var (sim, def) = Create(slot, initial);
        sim.RegisterEntity(100, def, initial with { EntityId = 100, PX = -1.5f, PZ = 2.5f, LockOn = false });
        sim.RegisterEntity(101, def, initial with { EntityId = 101, PX = 1.5f, PZ = 2.5f, LockOn = false });
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Slot1, TargetEntityId = 101 } });
        var state = sim.GetState(1);
        Assert.Equal(locked ? 100UL : 101UL, state.AttackCorrectionTargetId);
        TestHelpers.AssertNear((locked ? -1f : 1f) * MathF.PI / 30f, state.FacingYaw, 1e-5f);
        Assert.True(state.AttackCorrectionActive);
    }

    [Theory]
    [InlineData("fightguy", false)]
    [InlineData("fightguy", true)]
    [InlineData("manki", false)]
    [InlineData("manki", true)]
    [InlineData("bonk", false)]
    [InlineData("bonk", true)]
    [InlineData("wibou", false)]
    [InlineData("wibou", true)]
    public void AuthoredNormalsHonorSideAndRearTargetsWithoutExceedingStartupTurn(string package, bool locked)
    {
        string directory = RepoFile($"client/Unity/Assets/CharacterPackages/{package}");
        var compiled = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(directory, "package.json")),
            File.ReadAllText(Path.Combine(directory, "character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var def = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage!);
        var directions = new[] { (X: 2.5f, Z: 0f), (X: -2.5f, Z: 0f), (X: 0f, Z: -2.5f) };
        foreach (var slot in def.CookedSlots!.Where(slot => slot.Ordinal % CanonicalSlotProjection.VariantSlotCount < 3))
        foreach (var direction in directions)
        {
            var profile = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                .OfType<CookedStartupAimCorrectionOperation>().Single();
            var initial = TestHelpers.PlayerState() with
            {
                PX = 0f, PY = def.CapsuleHeight * .5f, PZ = 0f, FacingYaw = 0f,
                IsGrounded = !slot.IsAir, LockOn = locked, TargetEntityId = locked ? 100UL : 0UL,
            };
            var sim = TestHelpers.MakeSim();
            sim.RegisterEntity(1, def, initial);
            sim.RegisterEntity(100, def, TestHelpers.NpcState(direction.X, direction.Z)
                with { PY = initial.PY });
            sim.RegisterEntity(101, def, TestHelpers.NpcState(0f, 1f) with { PY = initial.PY });
            byte wireSlot = CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[slot.Ordinal]);
            sim.ActivateAbility(1, new CookedTimelineAbility(slot), wireSlot, def,
                activationInput: new InputState { TargetEntityId = locked ? (byte)101 : (byte)100 });
            Assert.Equal(100UL, sim.GetState(1).AttackCorrectionTargetId);

            float step = profile.YawDegreesPerSecond * MathF.PI / 180f / 60f;
            float limit = profile.MaxYawDegrees * MathF.PI / 180f;
            float previousYaw = 0f;
            for (int tick = 1; tick < profile.EndTick; tick++)
            {
                sim.TickAbilities(new());
                var state = sim.GetState(1);
                Assert.Equal(100UL, state.AttackCorrectionTargetId);
                Assert.InRange(MathF.Abs(state.FacingYaw - previousYaw), 0f, step + 1e-5f);
                Assert.InRange(MathF.Abs(state.FacingYaw), 0f, limit + 1e-5f);
                previousYaw = state.FacingYaw;
            }
            float expected = MathF.Min(limit, step * (profile.EndTick - 1));
            TestHelpers.AssertNear(direction.X < 0f ? -expected : expected, previousYaw, 1e-5f);
            sim.TickAbilities(new());
            Assert.False(sim.GetState(1).AttackCorrectionActive);
            Assert.Equal(previousYaw, sim.GetState(1).FacingYaw);
        }
    }

    [Fact]
    public void StartupCorrectionFiltersAcquisitionAndDoesNotAcquireAfterAnEmptyStart()
    {
        var slot = CorrectionSlot(6);
        var (sim, def) = Create(slot);
        sim.RegisterEntity(100, def, TestHelpers.NpcState(0f, -2f));
        sim.RegisterEntity(101, def, TestHelpers.NpcState(3f, 0f));
        sim.RegisterEntity(102, def, TestHelpers.NpcState(0f, 5f));
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def,
            activationInput: new InputState { TargetEntityId = 100 });
        Assert.Equal(0UL, sim.GetState(1).AttackCorrectionTargetId);
        sim.RegisterEntity(103, def, TestHelpers.NpcState(1f, 3f));
        sim.TickAbilities(new());
        Assert.Equal(0f, sim.GetState(1).FacingYaw);
        Assert.False(sim.GetState(1).AttackCorrectionActive);
    }

    [Fact]
    public void StartupCorrectionCommitsBeforeActiveAndDoesNotReopenBetweenHits()
    {
        var slot = CorrectionSlot(4);
        var (sim, def) = Create(slot);
        sim.RegisterEntity(100, def, TestHelpers.NpcState(1.5f, 2.5f));
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        for (int i = 0; i < 3; i++) sim.TickAbilities(new());
        var beforeActive = sim.GetState(1);
        TestHelpers.AssertNear(MathF.PI / 10f, beforeActive.FacingYaw, 1e-5f);
        var crossed = sim.GetState(100) with { PX = -1.5f };
        sim.SetState(100, crossed);
        sim.RegisterEntity(101, def, TestHelpers.NpcState(-.5f, 2f));
        for (int i = 0; i < 5; i++)
        {
            sim.TickAbilities(new() { [1] = new InputState { TargetEntityId = 101, ToggleLock = true } });
            Assert.Equal(beforeActive.FacingYaw, sim.GetState(1).FacingYaw);
            Assert.Equal(100UL, sim.GetState(1).AttackCorrectionTargetId);
            Assert.False(sim.GetState(1).AttackCorrectionActive);
        }
        Assert.Equal(2, sim.Resolver.GetActiveHitboxes().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupCorrectionInvalidationIsPermanentEvenIfTargetReturns(bool respawned)
    {
        var slot = CorrectionSlot(10);
        var (sim, def) = Create(slot);
        sim.RegisterEntity(100, def, TestHelpers.NpcState(1f, 3f));
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        sim.TickAbilities(new());
        float yaw = sim.GetState(1).FacingYaw;
        var target = sim.GetState(100);
        sim.SetState(100, target with { PX = respawned ? target.PX : 10f, Deaths = (byte)(respawned ? 1 : 0) });
        sim.TickAbilities(new());
        sim.SetState(100, target with { PX = -1f });
        sim.TickAbilities(new());
        Assert.False(sim.GetState(1).AttackCorrectionActive);
        Assert.Equal(yaw, sim.GetState(1).FacingYaw);
        Assert.Equal(100UL, sim.GetState(1).AttackCorrectionTargetId);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void StartupCorrectionUsesSizeScaledTorsoRatherThanTargetHip(float targetHeight)
    {
        var slot = CorrectionSlot(8, pitch: 15f);
        var initial = TestHelpers.PlayerState() with { IsGrounded = false, PY = 3f };
        var (sim, def) = Create(slot, initial);
        var targetDef = TestHelpers.EngineDef;
        targetDef = targetDef with { CapsuleHeight = targetHeight };
        targetDef = targetDef with { HipHeight = targetHeight / 2f };
        sim.RegisterEntity(100, targetDef, initial with { EntityId = 100, PZ = 2.5f });
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        for (int tick = 0; tick < 4; tick++) sim.TickAbilities(new());

        TestHelpers.AssertNear(MathF.Atan2(targetHeight * .25f, 2.5f),
            sim.GetState(1).AttackPosePitch, 1e-5f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelayedCorrectionCapturesAtActivationAndCannotReacquireAfterInvalidation(bool leavesRange)
    {
        var slot = Slot(12,
            new CookedStartupAimCorrectionOperation(3, AuthoringUnit.Ticks, 5, 4f, 60f, 45f, 0f, 360f, 0f),
            new CookedSpawnHitboxOperation(5, AuthoringUnit.Meters, Hitbox(5, 1)));
        var (sim, def) = Create(slot);
        var target = TestHelpers.NpcState(1f, 2.5f);
        sim.RegisterEntity(100, def, target);
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        Assert.Equal(100UL, sim.GetState(1).AttackCorrectionTargetId);
        Assert.True(sim.GetState(1).AttackCorrectionOwned);
        sim.RegisterEntity(101, def, TestHelpers.NpcState(-1f, 2f));
        sim.SetState(1, sim.GetState(1) with { LockOn = true, TargetEntityId = 101 });
        if (leavesRange) sim.SetState(100, target with { PX = 10f });
        sim.TickAbilities(new());
        sim.SetState(100, target);
        sim.TickAbilities(new());
        Assert.Equal(0f, sim.GetState(1).FacingYaw);
        sim.TickAbilities(new());
        Assert.Equal(100UL, sim.GetState(1).AttackCorrectionTargetId);
        TestHelpers.AssertNear(leavesRange ? 0f : MathF.PI / 30f, sim.GetState(1).FacingYaw, 1e-5f);
        sim.TickAbilities(new());
        float committed = sim.GetState(1).FacingYaw;
        sim.TickAbilities(new());
        Assert.False(sim.GetState(1).AttackCorrectionActive);
        Assert.Equal(committed, sim.GetState(1).FacingYaw);
    }

    [Fact]
    public void StartupCorrectionCapsDeflectionAndNeverSwitchesToACloserTarget()
    {
        var slot = CorrectionSlot(18);
        var (sim, def) = Create(slot);
        sim.RegisterEntity(100, def, TestHelpers.NpcState(1.5f, 2.5f));
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        sim.SetState(100, sim.GetState(100) with { PX = 3f, PZ = .2f });
        sim.RegisterEntity(101, def, TestHelpers.NpcState(-1f, 2f));
        for (int i = 0; i < 12; i++) sim.TickAbilities(new() { [1] = new InputState { TargetEntityId = 101 } });
        TestHelpers.AssertNear(MathF.PI / 4f, sim.GetState(1).FacingYaw, 1e-5f);
        Assert.Equal(100UL, sim.GetState(1).AttackCorrectionTargetId);
    }

    [Fact]
    public void StartupCorrectionFreezesWithHitstopAndClearsOnInterruptionAndRestart()
    {
        var slot = CorrectionSlot(8, pitch: 15f);
        var initial = TestHelpers.PlayerState() with { IsGrounded = false, PY = 3f, VX = 2f, VY = 1f };
        var (sim, def) = Create(slot, initial);
        sim.RegisterEntity(100, def, initial with { EntityId = 100, PX = 1f, PY = 4f, PZ = 2.5f });
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        sim.TickAbilities(new());
        var corrected = sim.GetState(1);
        TestHelpers.AssertNear(MathF.PI / 60f, corrected.AttackPosePitch, 1e-5f);
        Assert.Equal(initial.VX, corrected.VX);
        Assert.Equal(initial.VY, corrected.VY);
        sim.SetState(1, corrected with { HitstopTicks = 2 });
        sim.TickAbilities(new());
        Assert.Equal(corrected.FacingYaw, sim.GetState(1).FacingYaw);
        Assert.Equal(corrected.AttackPosePitch, sim.GetState(1).AttackPosePitch);
        sim.SetState(1, sim.GetState(1) with { HitstopTicks = 0, State = ActionState.Hitstun });
        sim.TickAbilities(new());
        Assert.Equal(0f, sim.GetState(1).AttackPosePitch);
        Assert.False(sim.GetState(1).AttackCorrectionOwned);
        Assert.Equal(0UL, sim.GetState(1).AttackCorrectionTargetId);
        sim.SetState(1, initial with { FacingYaw = 0f });
        sim.SetState(100, sim.GetState(100) with { PX = -1f });
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        sim.TickAbilities(new());
        Assert.True(sim.GetState(1).FacingYaw < 0f);
        Assert.Equal(100UL, sim.GetState(1).AttackCorrectionTargetId);
    }

    [Fact]
    public void StartupCorrectionSupportsEarlierSpecialLaunchAndProjectileIgnoresLaterCameraAim()
    {
        var projectile = new CookedProjectile(0f, 1f, 0f, 30f, 0f, .2f, 3f, 20f, 2f, 4f, 5, 30);
        var timeline = Slot(12,
            new CookedStartupAimCorrectionOperation(0, AuthoringUnit.Ticks, 3, 4f, 60f, 45f, 0f, 360f, 0f),
            new CookedForwardLungeOperation(3, AuthoringUnit.MetersPerSecond, 6f, 2),
            new CookedSpawnProjectileOperation(5, AuthoringUnit.Meters, projectile));
        var slot = new CookedSlotDefinition(3, "ground.A", false, "Launch", "Launch", "icon.test",
            AuthoringAbilityBehavior.AimedProjectile, AuthoringAimMode.CameraForward3D, 0, false, false, timeline.Timeline);
        var (sim, def) = Create(slot);
        sim.RegisterEntity(100, def, TestHelpers.NpcState(1.5f, 2.5f));
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), (byte)(AbilitySlots.A - 1), def);
        for (int i = 0; i < 3; i++) sim.TickAbilities(new());
        var launch = sim.GetState(1);
        TestHelpers.AssertNear(MathF.PI / 15f, launch.FacingYaw, 1e-5f);
        TestHelpers.AssertNear(6f * MathF.Sin(launch.FacingYaw), launch.VX, 1e-5f);
        sim.SetState(1, launch with { AimYaw = -MathF.PI / 2f });
        sim.SetState(100, sim.GetState(100) with { PX = -1.5f });
        sim.TickAbilities(new());
        sim.TickAbilities(new());
        var fired = Assert.Single(sim.Resolver.GetActiveHitboxes());
        TestHelpers.AssertNear(30f * MathF.Sin(launch.FacingYaw), fired.VX, 1e-5f);
        TestHelpers.AssertNear(30f * MathF.Cos(launch.FacingYaw), fired.VZ, 1e-5f);
    }

    [Theory]
    [InlineData("manki", "F")]
    [InlineData("fightguy", "R")]
    [InlineData("fightguy", "F")]
    [InlineData("wibou", "F")]
    [InlineData("bonk", "A")]
    [InlineData("bonk", "R")]
    public void AuthoredSpecialsCorrectStartupAndCommitGroundAndAir(string package, string label)
    {
        var def = CompileSourceDefinition(package);
        foreach (bool airborne in new[] { false, true })
        foreach (bool locked in new[] { false, true })
        {
            var slot = def.CookedSlots!.Single(s => s.Id == $"{(airborne ? "air" : "ground")}.{label}");
            var initial = TestHelpers.PlayerState() with
            {
                PY = airborne ? 20f : def.CapsuleHeight * .5f, IsGrounded = !airborne,
                LockOn = locked, TargetEntityId = locked ? 100UL : 0UL,
            };
            var sim = TestHelpers.MakeSim();
            sim.NoGravityEntityId = 1;
            sim.RegisterEntity(1, def, initial);
            var targetDef = TestHelpers.EngineDef;
            targetDef = targetDef with { Movement = targetDef.Movement with { Gravity = 0f } };
            sim.RegisterEntity(100, targetDef, TestHelpers.NpcState(2.5f, 0f)
                with { PY = initial.PY, IsGrounded = !airborne });
            byte wireSlot = CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All.First(address => address.InputLabel == label));
            // Derive the cutoff from actual attack/launch timing, never the correction profile.
            int commitmentTick = slot.Timeline.Stages[0].Operations.Select(operation => operation switch
            {
                CookedSpawnHitboxOperation hit => (int)hit.Tick,
                CookedSpawnProjectileOperation throwOperation => (int)throwOperation.Tick,
                CookedForwardLungeOperation lunge => (int)lunge.Tick,
                CookedStartCapabilityOperation { Parameters: CookedMankiAerosolInfernoCapabilityParameters flame }
                    => flame.FireTriggerTick,
                CookedStartCapabilityOperation { Parameters: CookedCycloneKickCapabilityParameters cyclone }
                    => cyclone.WindupTicks + 1,
                // The Joust launches into its gallop on the tick the capability starts.
                CookedStartCapabilityOperation { Parameters: CookedSteeredChargeCapabilityParameters } charge
                    => (int)charge.Tick,
                _ => int.MaxValue,
            }).Min();
            Assert.True(commitmentTick < 1000, $"{package} {label} has no attack or launch this test knows");
            bool aimedHold = package == "manki";
            sim.Tick(new() { [1] = new InputState
                { ActiveSlot = wireSlot, IsAiming = aimedHold, TargetEntityId = 100 } });
            var inputs = new Dictionary<ulong, InputState>
                { [1] = new() { TargetEntityId = 100, AimYaw = -9000 } };
            if (aimedHold)
            {
                // A tap queues release through any authored minimum charge.
                do
                {
                    sim.Tick(new() { [1] = new InputState { TargetEntityId = 100 } });
                } while (sim.GetState(1).State == ActionState.Aiming);
            }
            var profile = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                .OfType<CookedStartupAimCorrectionOperation>().Single();
            float step = profile.YawDegreesPerSecond * MathF.PI / 180f / 60f;
            float limit = profile.MaxYawDegrees * MathF.PI / 180f;
            float previousYaw = sim.GetState(1).FacingYaw;
            TestHelpers.AssertNear(step, previousYaw);
            for (int tick = aimedHold ? 1 : 2; tick < commitmentTick; tick++)
            {
                sim.Tick(inputs);
                var state = sim.GetState(1);
                Assert.InRange(state.FacingYaw - previousYaw, 0f, step + 1e-5f);
                Assert.InRange(state.FacingYaw, 0f, limit + 1e-5f);
                previousYaw = state.FacingYaw;
            }
            float expectedYaw = MathF.Min(limit,
                step * (aimedHold ? commitmentTick : commitmentTick - 1));
            TestHelpers.AssertNear(expectedYaw, previousYaw);
            // Crossing at the first active/launch tick must not permit one last correction.
            sim.SetState(100, sim.GetState(100) with { PX = -2.5f });
            sim.Tick(inputs);
            TestHelpers.AssertNear(previousYaw, sim.GetState(1).FacingYaw);
            Assert.False(sim.GetState(1).AttackCorrectionActive);
            if (package == "bonk" && label != "A")  // his Q throws the pot lid; the others launch him
            {
                var launched = sim.GetState(1);
                Assert.True(launched.VX * launched.VX + launched.VZ * launched.VZ > 0f);
                TestHelpers.AssertNear(previousYaw, MathF.Atan2(launched.VX, launched.VZ));
            }
            else
                Assert.Contains(sim.Resolver.GetActiveHitboxes(), hit => hit.OwnerId == 1);
            for (int tick = 0; tick < 8; tick++)
            {
                sim.Tick(inputs);
                TestHelpers.AssertNear(previousYaw, sim.GetState(1).FacingYaw);
                Assert.False(sim.GetState(1).AttackCorrectionActive);
            }
        }
    }

    [Theory]
    [InlineData("manki", "F", "valid")]
    [InlineData("manki", "F", "invalidated")]
    [InlineData("manki", "F", "empty")]
    public void AimedSpecialReleaseCorrectsFinalManualFacingWithoutReacquiring(
        string package, string label, string targetScenario)
    {
        var def = CompileSourceDefinition(package);
        var slot = def.CookedSlots!.Single(s => s.Id == $"ground.{label}");
        var initial = TestHelpers.PlayerState() with { PY = def.CapsuleHeight * .5f };
        var (sim, _) = Create(slot, initial);
        sim.RegisterEntity(1, def, initial);
        sim.RegisterEntity(100, def, TestHelpers.NpcState(targetScenario == "empty" ? 10f : 2.5f, 0f)
            with { PY = initial.PY });
        byte wireSlot = label == "R" ? AbilitySlots.R : AbilitySlots.F;
        sim.ActivateAbility(1, new CookedTimelineAbility(slot),
            (byte)(wireSlot - 1), def, activationInput: new InputState { TargetEntityId = 100 });
        var hold = new Dictionary<ulong, InputState> { [1] = new() { IsAiming = true } };
        const float manualYaw = MathF.PI / 3f;
        sim.SetState(1, sim.GetState(1) with { AimYaw = manualYaw });
        for (int tick = 0; tick < 24; tick++)
        {
            if (targetScenario == "invalidated" && tick == 2)
                sim.SetState(100, sim.GetState(100) with { PX = 10f });
            if (targetScenario != "valid" && tick == 3)
                sim.SetState(100, sim.GetState(100) with { PX = 2.5f });
            sim.TickAbilities(hold);
            TestHelpers.AssertNear(manualYaw, sim.GetState(1).FacingYaw);
            Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
        }
        sim.TickAbilities(new() { [1] = default });
        var profile = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<CookedStartupAimCorrectionOperation>().Single();
        float step = profile.YawDegreesPerSecond * MathF.PI / 180f / 60f;
        float expected = manualYaw + (targetScenario == "valid" ? step : 0f);
        TestHelpers.AssertNear(expected, sim.GetState(1).FacingYaw);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.Equal(targetScenario == "empty" ? 0UL : 100UL, sim.GetState(1).AttackCorrectionTargetId);
        if (targetScenario != "valid")
            Assert.False(sim.GetState(1).AttackCorrectionActive);
        sim.SetState(1, sim.GetState(1) with { State = ActionState.Hitstun });
        sim.TickAbilities(new());
        Assert.False(sim.GetState(1).AttackCorrectionOwned);
        Assert.Equal(0UL, sim.GetState(1).AttackCorrectionTargetId);
    }

    [Fact]
    public void AttackerHitstopDoesNotShortenItsOtherLiveWindows()
    {
        // At tick 2 a window 1 m to the right connects and freezes both fighters, while a
        // 3-tick window 5 m to the left touches nobody. The freeze pauses the attacker's
        // timeline (ADR-0012), so the second window still closes on the same attack tick.
        var slot = Slot(20,
            new CookedSpawnHitboxOperation(2, AuthoringUnit.Meters, Hitbox(2, 1)),
            new CookedSpawnHitboxOperation(2, AuthoringUnit.Meters, Hitbox(2, 3) with { OffsetX = -5f }));

        float groundY = TestHelpers.GroundPY(TestHelpers.EngineDef);

        (ushort ClosedAt, bool Froze) Run(bool withTarget)
        {
            var (sim, def) = Create(slot, TestHelpers.PlayerState() with { PY = groundY });
            if (withTarget)
                sim.RegisterEntity(100, def, TestHelpers.NpcState(1f, 0f) with { PY = groundY });
            sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
            bool seen = false, froze = false;
            for (int tick = 0; tick < 40; tick++)
            {
                sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
                froze |= sim.GetState(1).HitstopTicks > 0;
                bool live = sim.Resolver.GetActiveHitboxes().Any(hitbox => hitbox.X < -2f);
                if (seen && !live) return (sim.GetState(1).AttackElapsedTicks, froze);
                seen |= live;
            }
            throw new InvalidOperationException("The left window never closed.");
        }

        var alone = Run(withTarget: false);
        var frozen = Run(withTarget: true);
        Assert.False(alone.Froze);
        Assert.True(frozen.Froze);
        Assert.Equal(alone.ClosedAt, frozen.ClosedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MobileAimWalksAtItsSpeedScaleAndStopsWhenTheAimEnds(bool airborne)
    {
        // A mobile aim state on ticks 0-19 walks at 40% of run (or air) speed under a held
        // stick. When the timeline ends the aim, the walking stops instead of carrying into
        // the rest of the move, which has no friction.
        var timeline = new CookedTimeline(new[]
        {
            new CookedStage(40, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
            {
                new CookedSetAimStateOperation(0, AuthoringUnit.Ticks, AuthoringAimMode.CameraForward3D),
                new CookedSetAimStateOperation(20, AuthoringUnit.Ticks, AuthoringAimMode.None),
            }),
        });
        var slot = new CookedSlotDefinition(airborne ? 7 : 0, airborne ? "air.1" : "ground.1", airborne, "Test", "Test",
            "icon.test", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false, timeline,
            aimMovement: AuthoringAimMovementMode.Mobile, aimMovementSpeedScale: 0.4f);
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[airborne ? 7 : 0] = slot;
        def = def with { CookedSlots = slots };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def) + (airborne ? 50f : 0f), IsGrounded = !airborne,
        });
        sim.ActivateAbility(1, new CookedTimelineAbility(slot), 2, def);
        var stick = new Dictionary<ulong, InputState> { [1] = new InputState { MoveX = 1f } };
        float cap = 0.4f * (airborne ? def.Movement.AirSpeedMax : def.Movement.RunSpeed);

        float fastest = 0f;
        while (sim.GetState(1).State == ActionState.Aiming)
        {
            sim.Tick(stick);
            var walking = sim.GetState(1);
            if (walking.State == ActionState.Aiming) fastest = MathF.Max(fastest, MathF.Abs(walking.VX));
        }
        Assert.InRange(fastest, 0.5f * cap, cap + 1e-4f);
        if (!airborne) TestHelpers.AssertNear(cap, fastest, 1e-4f); // the ground ramp completes inside the aim

        var stopped = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, stopped.State);
        for (int tick = 0; tick < 10; tick++) sim.Tick(stick);
        Assert.Equal(0f, sim.GetState(1).VX);
        TestHelpers.AssertNear(stopped.PX, sim.GetState(1).PX, 1e-5f);
    }

    private static CharacterDefinition CompileSourceDefinition(string package)
    {
        string directory = RepoFile($"client/Unity/Assets/CharacterPackages/{package}");
        var compiled = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(directory, "package.json")),
            File.ReadAllText(Path.Combine(directory, "character.json")),
            CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        return CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage!);
    }

    private static CookedSlotDefinition CorrectionSlot(ushort endTick, float pitch = 0f)
        => Slot((ushort)(endTick + 10),
            new CookedStartupAimCorrectionOperation(0, AuthoringUnit.Ticks, endTick, 4f, 60f, 45f, pitch, 360f, 180f),
            new CookedSpawnHitboxOperation(endTick, AuthoringUnit.Meters, Hitbox(endTick, 1)),
            new CookedSpawnHitboxOperation((ushort)(endTick + 3), AuthoringUnit.Meters, Hitbox((ushort)(endTick + 3), 1)));

    private static CookedSlotDefinition Slot(ushort duration, params CookedTimelineOperation[] operations)
        => new(0, "ground.1", false, "Test", "Test", "icon.test", AuthoringAbilityBehavior.MeleeCombo,
            AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[] { new CookedStage(duration, 0, 0, 0, 0, Array.Empty<string>(), operations) }));

    private static CookedHitbox Hitbox(ushort tick, ushort duration)
        => new(AuthoringHitboxShape.Sphere, .5f, 1f, 0f, 0f, 0f, 0f, 0f, null, null, 3f, 20f, 2f, 4f, 5, duration, true, 0);

    private static (ServerSimulation Sim, CharacterDefinition Def) Create(CookedSlotDefinition slot, CharacterState? state = null)
    {
        var def = TestHelpers.EngineDef;
        def = def with { CookedSlots = new[] { slot } };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, state ?? TestHelpers.PlayerState());
        return (sim, def);
    }
    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
