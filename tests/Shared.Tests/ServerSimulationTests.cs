using SlopArena.Shared.Abilities;

using Xunit;

namespace SlopArena.Shared.Tests;

public class ServerSimulationTests
{
    private static CharacterDefinition MakeTestDef()
    {
        return new CharacterDefinition
        {
            Class = CharacterClass.Manki,
            Movement = new MovementStats
            {
                RunSpeed = 5f,
                RunAccelerationA = 20f,
                RunAccelerationB = 12f,
                DashSpeed = 15f,
                AirSpeedMax = 5f,
                AirAccelStick = 3f,
                AirAccelBase = 1f,
                JumpForce = 10f,
                ShortHopForce = 6f,
                AirJumpVMultiplier = 0.8f,
                AirDodgeSpeed = 11f,
                AirJumpHMultiplier = 0.85f,
                Gravity = 20f,
                AirFloatGravity = 6f,
                DashDurationTicks = 15,
                DashCooldownTicks = 30,
                GroundFriction = 0.5f,
                AirFriction = 0.1f,
                MaxFallSpeed = 20f,
                FastFallSpeed = 24f,
                MaxJumps = 2,
                JumpSquatTicks = 3,
            },
            CapsuleRadius = 0.3f,
            CapsuleHeight = 1.5f,
            ShieldRadius = 0.95f,
            HurtboxRadius = 0.4f,
            CaptureGeometry = new CookedCaptureGeometry(1.1f, 0.8f, 1.2f, 0.6f,
                new CaptureAnchor(0f, 0.6f, 0.2f), new CaptureAnchor(0f, 0.6f, 0.65f)),
            // Full-body capsule so entities appear in the hurtbox list — lets the
            // elimination tests assert untargetability meaningfully.
            HurtboxCapsules = new[] { new HurtboxCapsule(0, -0.65f, 0, 0, 0.65f, 0, 0.3f) },
            HurtboxBoneDefs = null,
            BakedDataPath = "",
        };
    }

    private static ArenaDefinition MakeTestArena()
    {
        return new ArenaDefinition
        {
            Name = "test",
            DisplayName = "Test Arena",
            KillHeight = -20f,
            SpawnPoints = new[]
            {
                new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 },
            },
        };
    }

    private static CharacterState MakeIdleState(ulong entityId = 1)
    {
        return new CharacterState
        {
            EntityId = entityId,
            PX = 0, PY = 0, PZ = 0,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = 2,
            AirDodgesLeft = 1,
            FacingYaw = 0,
        };
    }

    // ── Void death ──

    [Fact]
    public void Tick_EntityBelowKillHeight_RespawnsWithDeathCount()
    {
        var arena = TestHelpers.TestArena();
        var sim = new ServerSimulation(arena);
        var state = MakeIdleState(1);
        state.PZ = -1f; // off the 200x200 heightmap grid → no floor → falls into the void
        state.PY = -30f; // below KillHeight (-20); only dies because PZ=-1 keeps it off-floor
        sim.RegisterEntity(1, MakeTestDef(), state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        Assert.Equal(arena.SpawnPoints[0].X, result.PX);
        Assert.Equal(arena.SpawnPoints[0].Y, result.PY);
        Assert.Equal(arena.SpawnPoints[0].Z, result.PZ);
        Assert.Equal(1, result.Deaths);
        Assert.Equal(0u, result.DamagePercent);
    }

    [Fact]
    public void Tick_BelowKillHeight_RespawnsAtAssignedPosition_WithInvincibility()
    {
        // Respawn honors the per-entity respawn position (MatchInstance distributes
        // spawn points) and grants brief invincibility (issue #37).
        var arena = TestHelpers.TestArena();
        var sim = new ServerSimulation(arena);
        var state = MakeIdleState(1);
        state.PZ = -1f; // off the 200x200 heightmap grid → no floor → falls into the void
        state.PY = -30f; // below KillHeight (-20); only dies because PZ=-1 keeps it off-floor
        state.AirDodgesLeft = 0;
        sim.RegisterEntity(1, MakeTestDef(), state);
        sim.SetRespawnPosition(1, 12f, 3f, -7f);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        Assert.Equal(12f, result.PX);
        Assert.Equal(3f, result.PY);
        Assert.Equal(-7f, result.PZ);
        Assert.Equal(1, result.Deaths);
        Assert.Equal(0u, result.DamagePercent);
        Assert.Equal((ushort)60, result.InvincibilityTicks); // 1s at 60Hz
        Assert.Equal((byte)1, result.AirDodgesLeft);
    }

    [Fact]
    public void Tick_NoRespawnPosition_FallsBackDistributedByEntityIndex()
    {
        // Two spawn points: entity 2 dies → respawns at SpawnPoints[1], not
        // everyone stacking on SpawnPoints[0] (issue #37).
        var arena = TestHelpers.TestArena();
        arena.SpawnPoints = new[]
        {
            new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 },
            new SpawnPoint { X = 10, Y = 0, Z = 10, Yaw = 1.5f },
        };
        var sim = new ServerSimulation(arena);
        var state = MakeIdleState(2);
        state.PZ = -1f; // off the heightmap grid → no floor → falls into the void
        state.PY = -30f; // below KillHeight (-20)
        sim.RegisterEntity(2, MakeTestDef(), state);

        sim.Tick(new Dictionary<ulong, InputState> { { 2, default } });

        var result = sim.GetState(2);
        Assert.Equal(10f, result.PX);
        Assert.Equal(10f, result.PZ);
        Assert.Equal(1.5f, result.FacingYaw);
    }

    [Fact]
    public void Tick_DeathAtMaxDeaths_EliminatesAndFreezes()
    {
        // Losing the last stock eliminates the player: no respawn, frozen at the
        // spawn point, excluded from hurtboxes (untargetable) — issue #37.
        var arena = TestHelpers.TestArena();
        var sim = new ServerSimulation(arena, new StockMatchRule(3));
        var state = MakeIdleState(1);
        state.Deaths = 2; // on last stock
        state.PZ = -1f; // off the heightmap grid → no floor → falls into the void
        state.PY = -30f; // below KillHeight (-20)
        sim.RegisterEntity(1, MakeTestDef(), state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var afterDeath = sim.GetState(1);
        Assert.Equal(3, afterDeath.Deaths); // eliminated
        Assert.Equal(0u, afterDeath.DamagePercent);
        Assert.Equal(0, afterDeath.InvincibilityTicks); // no grace for spectators

        // Frozen: repeated ticks must not move it or change deaths.
        var frozenPos = (afterDeath.PX, afterDeath.PY, afterDeath.PZ);
        for (int i = 0; i < 30; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
        var later = sim.GetState(1);
        Assert.Equal(frozenPos, (later.PX, later.PY, later.PZ));
        Assert.Equal(3, later.Deaths);

        // Untargetable: not present in the last hurtbox list.
        bool inHurtboxes = false;
        foreach (var e in sim.GetLastEntityData())
            if (e.Id == 1) inHurtboxes = true;
        Assert.False(inHurtboxes);
    }

    [Fact]
    public void Tick_NoWinRule_RespawnsForever_NeverEliminates()
    {
        // Training mode (NoWinMatchRule): deaths keep counting and the entity
        // keeps respawning — no freeze at any stock threshold (issue #37 follow-up).
        var arena = TestHelpers.TestArena();
        var sim = new ServerSimulation(arena, NoWinMatchRule.Instance);
        var state = MakeIdleState(1);
        state.PZ = -1f; // off the heightmap grid → no floor → falls into the void
        state.PY = -30f; // below KillHeight (-20)
        sim.RegisterEntity(1, MakeTestDef(), state);

        // Kill the entity 6 times — past the stock-mode threshold of 3.
        // Each pass re-parks it off-grid (the respawn lands grounded on-stage), so it
        // always falls into the void instead of being force-snapped back to the floor.
        for (int i = 0; i < 6; i++)
        {
            var s = sim.GetState(1);
            s.PX = 0f; s.PZ = -1f; s.PY = -30f;
            sim.SetState(1, s);
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
        }

        var result = sim.GetState(1);
        Assert.Equal(6, result.Deaths); // kept counting, never eliminated
        Assert.Equal(0u, result.DamagePercent);

        // Still a hurtbox target — untargetability only applies to eliminated entities.
        bool inHurtboxes = false;
        foreach (var e in sim.GetLastEntityData())
            if (e.Id == 1) inHurtboxes = true;
        Assert.True(inHurtboxes);
    }



    [Fact]
    public void Tick_NoInput_StatePreserved()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        var initialState = MakeIdleState(1);
        sim.RegisterEntity(1, MakeTestDef(), initialState);

        // 10 ticks with no input
        for (int i = 0; i < 10; i++)
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        // State should still be Idle, position unchanged
        Assert.Equal(ActionState.Idle, result.State);
        Assert.Equal(0f, result.PX);
        Assert.Equal(0f, result.PZ);
    }

    // ── Multiple entities / pushboxes ──

    [Fact]
    public void Tick_TwoEntitiesIdle_NeitherChanges()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, MakeTestDef(), MakeIdleState(1));
        sim.RegisterEntity(2, MakeTestDef(), MakeIdleState(2));

        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, default },
            { 2, default },
        });

        var s1 = sim.GetState(1);
        var s2 = sim.GetState(2);
        Assert.Equal(ActionState.Idle, s1.State);
        Assert.Equal(ActionState.Idle, s2.State);

        // Idle entities do not self-move, but overlapping pushboxes separate symmetrically.
        Assert.True(s1.PX < 0f);
        Assert.True(s2.PX > 0f);
        Assert.InRange(s1.PZ, -0.00001f, 0.00001f);
        Assert.InRange(s2.PZ, -0.00001f, 0.00001f);
    }

    // ── GetState/SetState round-trip ──

    [Fact]
    public void SetState_ThenGetState_ReturnsValue()
    {
        var sim = new ServerSimulation(MakeTestArena());
        sim.RegisterEntity(1, MakeTestDef(), MakeIdleState(1));

        var modified = MakeIdleState(1);
        modified.PX = 12.5f;
        modified.DamagePercent = 50;
        sim.SetState(1, modified);

        var result = sim.GetState(1);
        Assert.Equal(12.5f, result.PX);
        Assert.Equal(50u, result.DamagePercent);
    }

    // ── GetLastEntityData after Tick ──

    [Fact]
    public void Tick_EntityRegistered_GetLastEntityDataReturnsList()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, MakeTestDef(), MakeIdleState(1));

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var data = sim.GetLastEntityData();
        Assert.NotNull(data);
        // With no HurtboxCapsules or BakedAnimationData, list may be empty
        // But the assignment should not throw
    }

    // ── Q ability self-hit ──

    [Fact]
    public void Tick_MankiQ_EntityIdSetOnRegister()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        var def = BuiltInContentResolver.Resolve(CharacterClass.Manki).Definition;
        var state = MakeIdleState(1);
        sim.RegisterEntity(1, def, state);

        Assert.Equal((ulong)1, sim.GetState(1).EntityId);
    }

    [Fact]
    public void Tick_MankiQ_DoesNotHitOwner()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        var def = BuiltInContentResolver.Resolve(CharacterClass.Manki).Definition;

        var pState = MakeIdleState(1);
        sim.RegisterEntity(1, def, pState);

        var nState = MakeIdleState(100);
        nState.PX = 3f;
        sim.RegisterEntity(100, def, nState);

        for (int i = 0; i < 20; i++)
        {
            var input = new Dictionary<ulong, InputState>
            {
                { 1, i == 0 ? new InputState { ActiveSlot = 3 } : default },
                { 100, default },
            };
            sim.Tick(input);
        }

        var playerAfter = sim.GetState(1);
        Assert.Equal(0u, playerAfter.DamagePercent);
    }

    [Fact]
    public void Tick_MankiQ_HoldThenThrow_EndsInIdle()
    {
        var arena = MakeTestArena();
        var sim = new ServerSimulation(arena);
        var def = BuiltInContentResolver.Resolve(CharacterClass.Manki).Definition;
        var state = MakeIdleState(1);
        sim.RegisterEntity(1, def, state);

        // Tick 0: press A (Round Bomb)
        sim.Tick(new Dictionary<ulong, InputState>
            { { 1, new InputState { ActiveSlot = AbilitySlots.A } } });
        var t0 = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, t0.State);

        for (int i = 1; i < 75; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });
            var s = sim.GetState(1);
            // Q: 8-tick aim hold (Aiming) + 60-tick throw phase (Attacking) = ends at tick 68.
            // Without aim held, the release fires as soon as the 8-tick lock expires (tick 8).
            bool expectedAiming = i < 8;
            bool expectedAttacking = i >= 8 && i < 68;
            Assert.True((s.State == ActionState.Aiming) == expectedAiming,
                $"tick {i}: expected {(expectedAiming ? "Aiming" : "not-Aiming")} but got {s.State}");
            Assert.True((s.State == ActionState.Attacking) == expectedAttacking,
                $"tick {i}: expected {(expectedAttacking ? "Attacking" : "not-Attacking")} but got {s.State}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Soft-lock Targeting ──
    // ═══════════════════════════════════════════════════════════════
    //
    // ProcessTargetLock() reads state.State/AttackSlot and input.TargetEntityId
    // to set state.TargetEntityId each tick. Tests use Manki LMB (stage 1:
    // UseTargetLock=true, WarpRange=6, AttackRange=4, RotateTowardTarget=true).

    [Fact]
    public void TargetEntityId_ZeroWhenNoEnemyInRange()
    {
        var sim = TestHelpers.MakeSim(MakeTestArena());
        var def = TestHelpers.EngineDef;
        sim.RegisterEntity(1, def, MakeIdleState(1));
        var npc = MakeIdleState(100);
        npc.PZ = 25f; // beyond 20m search range
        sim.RegisterEntity(100, def, npc);

        sim.Tick(new() { { 1, default }, { 100, default } });

        var state = sim.GetState(1);
        Assert.Equal(0ul, state.TargetEntityId);
    }







    // ── Target lock rotation (3-zone) ──


    // ── Whiff commitment (ADR-0015): warp gone, attack at range is a commitment ──

    // ── Training no-cooldown flag (issue #187): opt-in, PvP default null ──

    private static CharacterDefinition CooldownDef(ushort cooldown = 120, ushort iasa = 0)
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        foreach (var (ordinal, id, air, ticks) in new[]
        {
            (0, "ground.1", false, cooldown), (1, "ground.2", false, (ushort)0),
            (8, "air.1", true, cooldown), (9, "air.2", true, (ushort)0),
        })
            slots[ordinal] = new CookedSlotDefinition(
                ordinal, id, air, "Cooldown fixture", "", "",
                AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, ticks, false, false,
                new CookedTimeline(new[]
                {
                    new CookedStage(20, iasa, 0, 0, 0,
                        Array.Empty<string>(), Array.Empty<CookedTimelineOperation>()),
                }));
        def = def with { CookedSlots = slots };
        return def;
    }

    private static ServerSimulation IasaCooldownSim(
        bool airborne = false, ushort cooldown = 120, ulong? noCooldownsEntity = null)
    {
        var def = CooldownDef(cooldown, iasa: 4);
        var sim = TestHelpers.MakeSim();
        sim.NoCooldownsEntityId = noCooldownsEntity;
        var state = TestHelpers.PlayerState();
        state.PY = airborne ? 100 : TestHelpers.GroundPY(def);
        state.IsGrounded = !airborne;
        sim.RegisterEntity(1, def, state);
        sim.Tick(new() { [1] = TestHelpers.Input(activeSlot: AbilitySlots.Slot1) });
        for (int tick = 1; tick < 4; tick++)
            sim.Tick(new() { [1] = default });
        return sim;
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, 2ul)]
    public void Cooldown_IasaSelfRecast_RejectedWithoutCancelling(
        bool airborne, ulong? noCooldownsEntity)
    {
        var sim = IasaCooldownSim(airborne, noCooldownsEntity: noCooldownsEntity);
        var activation = sim.GetLastActivationId(1);
        var before = sim.GetState(1);
        Assert.Equal((ushort)0, before.GetCooldown(AbilitySlots.Slot1));

        sim.Tick(new() { [1] = TestHelpers.Input(activeSlot: AbilitySlots.Slot1) });

        var after = sim.GetState(1);
        Assert.Equal(activation, sim.GetLastActivationId(1));
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal(AbilitySlots.Slot1, after.AttackSlot);
        Assert.Equal(before.AttackElapsedTicks + 1, after.AttackElapsedTicks);
        Assert.Equal((ushort)0, after.GetCooldown(AbilitySlots.Slot1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cooldown_IasaDifferentSlot_AppliesOutgoingCooldown(bool airborne)
    {
        var sim = IasaCooldownSim(airborne);
        var activation = sim.GetLastActivationId(1);

        sim.Tick(new() { [1] = TestHelpers.Input(activeSlot: AbilitySlots.Slot2) });

        Assert.NotEqual(activation, sim.GetLastActivationId(1));
        Assert.Equal(AbilitySlots.Slot2, sim.GetState(1).AttackSlot);
        Assert.Equal((ushort)119, sim.GetState(1).GetCooldown(AbilitySlots.Slot1));
    }

    [Theory]
    [InlineData(false, 0, null)]
    [InlineData(true, 0, null)]
    [InlineData(false, 120, 1ul)]
    [InlineData(true, 120, 1ul)]
    public void Cooldown_IasaSelfRecast_AllowedWithoutCooldown(
        bool airborne, ushort cooldown, ulong? noCooldownsEntity)
    {
        var sim = IasaCooldownSim(airborne, cooldown, noCooldownsEntity);
        var activation = sim.GetLastActivationId(1);

        sim.Tick(new() { [1] = TestHelpers.Input(activeSlot: AbilitySlots.Slot1) });

        Assert.NotEqual(activation, sim.GetLastActivationId(1));
        Assert.Equal(AbilitySlots.Slot1, sim.GetState(1).AttackSlot);
        Assert.Equal((ushort)0, sim.GetState(1).GetCooldown(AbilitySlots.Slot1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cooldown_CompletionAndExpiry_GatesBothVariants(bool airborne)
    {
        var sim = IasaCooldownSim(airborne);
        var activation = sim.GetLastActivationId(1);
        for (int ticks = 0; ticks < 20 && sim.GetState(1).AttackSlot != 0; ticks++)
            sim.Tick(new() { [1] = default });
        Assert.Equal((byte)0, sim.GetState(1).AttackSlot);
        Assert.Equal((ushort)120, sim.GetState(1).GetCooldown(AbilitySlots.Slot1));

        var state = sim.GetState(1);
        // Change ground/air variant while retaining the input-slot timer.
        state.IsGrounded = airborne;
        state.PY = airborne ? TestHelpers.GroundPY(CooldownDef()) : 100;
        state.VY = 0;
        sim.SetState(1, state);
        for (int remaining = 120; remaining > 0; remaining--)
        {
            Assert.Equal((ushort)remaining, sim.GetState(1).GetCooldown(AbilitySlots.Slot1));
            sim.Tick(new() { [1] = TestHelpers.Input(activeSlot: AbilitySlots.Slot1) });
            Assert.Equal(activation, sim.GetLastActivationId(1));
        }
        Assert.Equal((ushort)0, sim.GetState(1).GetCooldown(AbilitySlots.Slot1));
        sim.Tick(new() { [1] = TestHelpers.Input(activeSlot: AbilitySlots.Slot1) });
        Assert.NotEqual(activation, sim.GetLastActivationId(1));
        Assert.Equal(AbilitySlots.Slot1, sim.GetState(1).AttackSlot);
    }

    [Fact]
    public void NoCooldowns_DefaultNull_CooldownApplies()
    {
        var sim = TestHelpers.MakeSim();
        var def = CooldownDef();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);
        // Flag defaults to null — PvP path unchanged.

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.Slot1) } });
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, default } });

        ushort cd = sim.GetState(1).GetCooldown(AbilitySlots.Slot1);
        Assert.True(cd > 0, $"expected cooldown after the fixture move completed, got {cd}");
    }

    [Fact]
    public void NoCooldowns_EntityIdSet_CooldownStaysZeroAndMoveRecasts()
    {
        var sim = TestHelpers.MakeSim();
        var def = CooldownDef();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);
        sim.NoCooldownsEntityId = 1;

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.Slot1) } });
        for (int i = 0; i < 20; i++)
            sim.Tick(new() { { 1, default } });

        Assert.Equal((ushort)0, sim.GetState(1).GetCooldown(AbilitySlots.Slot1));
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);

        // Recast: a second press in the next tick must start the ability.
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.Slot1) } });
        var after = sim.GetState(1);
        Assert.NotEqual(ActionState.Idle, after.State);
        Assert.NotEqual((byte)0, after.AttackSlot);
    }
    [Fact]
    public void NoCooldowns_EntityIdSet_SkipsActivateFallbackCooldown()
    {
        // A move whose timeline completes on start (state stays Idle) takes the
        // ActivateAbility fallback cooldown write — the no-cooldown flag must skip
        // that write too (issue #187).
        var slot = new CookedSlotDefinition(
            ordinal: 0,
            id: "test.instant",
            isAir: false,
            name: "Test Instant",
            description: "",
            iconId: "icon.test",
            behavior: AuthoringAbilityBehavior.MeleeCombo,
            aimMode: AuthoringAimMode.None,
            cooldownTicks: 60,
            isRecoveryMove: false,
            preserveMomentumOnStart: false,
            timeline: new CookedTimeline(new[]
            {
                new CookedStage(
                    0, 0, 0, 0, 0, Array.Empty<string>(),
                    new[] { new CookedCompleteTimelineOperation(0, AuthoringUnit.Ticks) }),
            }));
        var def = new CharacterDefinition
        {
            Class = CharacterClass.Manki,
            DisplayName = "Cooked Test",
            CapsuleHeight = 1.7f,
            CapsuleRadius = .35f,
            Movement = TestHelpers.EngineDef.Movement,
            CookedSlots = new[] { slot },
            HurtboxCapsules = Array.Empty<HurtboxCapsule>(),
        };

        // Control: flag null → fallback write applies the cooldown.
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState());
        var ability = new SlopArena.Shared.Abilities.CookedTimelineAbility(slot);
        ability.Cooldown = slot.CooldownTicks;
        sim.ActivateAbility(1, ability, 2, def);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State); // exercised the fallback path
        Assert.True(sim.GetState(1).GetCooldown(AbilitySlots.Slot1) > 0);

        // Flag set → fallback write skipped.
        var sim2 = TestHelpers.MakeSim();
        sim2.RegisterEntity(1, def, TestHelpers.PlayerState());
        sim2.NoCooldownsEntityId = 1;
        var ability2 = new SlopArena.Shared.Abilities.CookedTimelineAbility(slot);
        ability2.Cooldown = slot.CooldownTicks;
        sim2.ActivateAbility(1, ability2, 2, def);
        Assert.Equal((ushort)0, sim2.GetState(1).GetCooldown(AbilitySlots.Slot1));
    }
    private static CharacterState GroundedState(ulong id, CharacterDefinition def, float z = 0f)
    {
        var state = MakeIdleState(id);
        state.PY = TestHelpers.GroundPY(def);
        state.PZ = z;
        return state;
    }

    private sealed class CancelMutatingAbility : ServerAbility
    {
        public bool Cancelled { get; private set; }

        public override void OnStart(ref CharacterState state, CharacterDefinition def)
        {
            state.State = ActionState.Attacking;
            state.AttackSlot = 1;
        }

        public override void Tick(ref CharacterState state, ref InputState input, CharacterDefinition def) { }

        public override void OnCancel(ref CharacterState state)
        {
            Cancelled = true;
            state.State = ActionState.Idle;
            state.AttackSlot = 0;
        }
    }

    [Fact]
    public void Shielding_GrabAndJumpPrecedence_AndHeldShieldNeverCreatesAirDodge()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = GroundedState(1, def);
        state.VX = 3f;
        state.VZ = -2f;
        sim.RegisterEntity(1, def, state);
        var input = new Dictionary<ulong, InputState>
        {
            [1] = new() { ShieldHeld = true, ShieldPressed = true },
        };

        sim.Tick(input);
        var shielding = sim.GetState(1);
        Assert.Equal(ActionState.Shielding, shielding.State);
        Assert.Equal(0f, shielding.VX);
        Assert.Equal(0f, shielding.VZ);

        input[1] = new InputState { Jump = true, JumpHeld = true, ShieldHeld = true };
        sim.Tick(input);
        Assert.Equal(ActionState.JumpSquat, sim.GetState(1).State);

        input[1] = new InputState { JumpHeld = true, ShieldHeld = true };
        for (int i = 0; i < 15; i++)
            sim.Tick(input);

        var airborne = sim.GetState(1);
        Assert.False(airborne.IsGrounded);
        Assert.Equal((byte)1, airborne.AirDodgesLeft);
        Assert.NotEqual(ActionState.AirDodgeMovement, airborne.State);
    }

    [Fact]
    public void GrabPressWinsJumpAndShield_WhileAirborneGrabIsRejectedWithoutDodging()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var grounded = GroundedState(1, def);
        sim.RegisterEntity(1, def, grounded);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true, Jump = true, ShieldHeld = true, ShieldPressed = true },
        });
        var attempt = sim.GetState(1);
        Assert.Equal(ActionState.GrabAttempt, attempt.State);
        Assert.Equal((byte)2, attempt.JumpsLeft);
        Assert.Equal((byte)DefenseInteractionPhase.Attempt, attempt.InteractionPhase);
        Assert.Equal(0u, attempt.InteractionTick);

        var airborneSim = new ServerSimulation(TestHelpers.TestArena());
        var airborne = GroundedState(1, def);
        airborne.PY += 3f;
        airborne.IsGrounded = false;
        airborneSim.RegisterEntity(1, def, airborne);
        airborneSim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true, Jump = true, ShieldHeld = true, ShieldPressed = true },
        });
        var rejected = airborneSim.GetState(1);
        Assert.Equal(ActionState.Idle, rejected.State);
        Assert.Equal((byte)2, rejected.JumpsLeft);
        Assert.Equal((byte)1, rejected.AirDodgesLeft);
    }

    [Fact]
    public void AirDodge_RequiresFreshAirPress_CapturesFacingAndConsumesOneUse()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = GroundedState(1, def);
        state.PY += 5f;
        state.IsGrounded = false;
        state.FacingYaw = MathF.PI * 0.5f;
        state.VX = 20f;
        state.VZ = -4f;
        sim.RegisterEntity(1, def, state);

        var heldShield = new Dictionary<ulong, InputState>
        {
            [1] = new() { ShieldPressed = true, ShieldHeld = true },
        };
        sim.Tick(heldShield);
        var movement = sim.GetState(1);
        Assert.Equal(ActionState.AirDodgeMovement, movement.State);
        Assert.Equal((byte)0, movement.AirDodgesLeft);
        Assert.Equal(DefenseConfig.AirDodgeInvulnerabilityTicks, movement.InvincibilityTicks);
        float speed = def.Movement.AirDodgeSpeed * DefenseConfig.AirDodgeLaunchScale;
        TestHelpers.AssertNear(speed, movement.VX);
        TestHelpers.AssertNear(0f, movement.VZ);
        Assert.Equal((ushort)DefenseConfig.AirDodgeMovementTicks, movement.StateTicks);

        heldShield[1] = new InputState { ShieldHeld = true, MoveY = -1f, FaceToCamera = true, AimYaw = 18000 };
        for (int i = 1; i < DefenseConfig.AirDodgeMovementTicks; i++)
        {
            sim.Tick(heldShield);
            speed *= DefenseConfig.AirDodgeSpeedRetention;
            var phase = sim.GetState(1);
            Assert.Equal(ActionState.AirDodgeMovement, phase.State);
            Assert.Equal((ushort)(DefenseConfig.AirDodgeMovementTicks - i), phase.StateTicks);
            Assert.Equal(i < DefenseConfig.AirDodgeInvulnerabilityTicks,
                phase.InvincibilityTicks > 0);
            TestHelpers.AssertNear(speed, phase.VX);
            TestHelpers.AssertNear(0f, phase.VZ);
            TestHelpers.AssertNear(MathF.PI * 0.5f, phase.FacingYaw);
        }
        sim.Tick(heldShield);
        var recovery = sim.GetState(1);
        Assert.Equal(ActionState.AirDodgeRecovery, recovery.State);
        Assert.Equal(DefenseConfig.AirDodgeRecoveryTicks, recovery.AirDodgeRecoveryTicks);
        // Recovery glides on the burst's exit speed; the held stick does not steer it.
        TestHelpers.AssertNear(speed - def.Movement.AirFriction * Simulation.TickDt, recovery.VX);
        TestHelpers.AssertNear(0f, recovery.VZ);
        Assert.Equal((byte)0, recovery.AirDodgesLeft);
    }

    [Fact]
    public void AirDodgeFollowsTheStickWithoutTurningTheFighter()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = GroundedState(1, def);
        state.PX = state.PZ = 50f;
        state.PY += 5f;
        state.IsGrounded = false;
        state.VZ = def.Movement.AirSpeedMax;
        sim.RegisterEntity(1, def, state);

        // Facing +Z while drifting forward, the stick pushes back and to the side.
        sim.Tick(new() { [1] = new() { ShieldPressed = true, ShieldHeld = true, MoveX = 1f, MoveY = -1f } });
        var dodge = sim.GetState(1);
        float diagonal = MathF.Sqrt(0.5f);
        float speed = def.Movement.AirDodgeSpeed * DefenseConfig.AirDodgeLaunchScale;
        Assert.Equal(ActionState.AirDodgeMovement, dodge.State);
        TestHelpers.AssertNear(diagonal, dodge.DashDirX);
        TestHelpers.AssertNear(-diagonal, dodge.DashDirZ);
        TestHelpers.AssertNear(speed * diagonal, dodge.VX);
        TestHelpers.AssertNear(-speed * diagonal, dodge.VZ);

        // Later stick input neither steers the dodge nor turns the fighter.
        int commitment = DefenseConfig.AirDodgeMovementTicks + DefenseConfig.AirDodgeRecoveryTicks;
        for (int tick = 1; tick < commitment; tick++)
            sim.Tick(new() { [1] = new() { MoveY = 1f } });
        var end = sim.GetState(1);
        Assert.Equal(ActionState.AirDodgeRecovery, end.State);
        Assert.Equal(0f, end.FacingYaw);
        TestHelpers.AssertNear(end.PX - state.PX, state.PZ - end.PZ, 0.0001f);
        Assert.True(end.PX - state.PX > 2f);
        Assert.True(end.VX > 0f && end.VZ < 0f);
        sim.Tick(new() { [1] = new() { MoveY = 1f } });
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void AirDodgeFallsNormallyAndRecoveryRejectsActionsWithoutBuffering()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = GroundedState(1, def);
        state.IsGrounded = false;
        state.PY += 30f;
        state.VY = -1f;
        state.VZ = 8f;
        state.IsFastFalling = true;
        state.SlideAttackCarryActive = true;
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { ShieldPressed = true, ShieldHeld = true, DownPressed = true },
        };
        sim.Tick(inputs);
        var start = sim.GetState(1);
        Assert.False(start.IsFastFalling);
        Assert.False(start.SlideAttackCarryActive);
        Assert.True(start.VY < state.VY);
        Assert.Equal(def.Movement.AirDodgeSpeed * DefenseConfig.AirDodgeLaunchScale, start.VZ);
        float previousVZ = start.VZ;
        for (int tick = 1; tick < DefenseConfig.AirDodgeMovementTicks
            + DefenseConfig.AirDodgeRecoveryTicks; tick++)
        {
            inputs[1] = new InputState
            {
                ShieldPressed = true, ShieldHeld = tick % 2 == 0,
                Jump = tick % 2 == 0,
                ActiveSlot = tick % 2 == 0 ? (byte)0 : AbilitySlots.Slot1,
                DownPressed = true, MoveY = -1f, FaceToCamera = true, AimYaw = 18000,
            };
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.Equal((byte)0, current.AirDodgesLeft);
            Assert.Equal((byte)0, current.AttackSlot);
            Assert.Equal((byte)0, current.BufferedSlot);
            Assert.False(current.IsFastFalling);
            Assert.Equal((byte)2, current.JumpsLeft);
            Assert.True(current.VY < start.VY);
            Assert.Equal(tick < DefenseConfig.AirDodgeMovementTicks
                ? ActionState.AirDodgeMovement : ActionState.AirDodgeRecovery, current.State);
            if (tick >= DefenseConfig.AirDodgeMovementTicks)
            {
                // The glide ignores the held stick and only loses air friction.
                Assert.Equal(0f, current.VX);
                TestHelpers.AssertNear(previousVZ - def.Movement.AirFriction * Simulation.TickDt, current.VZ);
            }
            previousVZ = current.VZ;
        }
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ShieldHeld = true } });
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.False(sim.GetState(1).IsFastFalling);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { DownPressed = true } });
        Assert.True(sim.GetState(1).IsFastFalling);
    }

    [Fact]
    public void LegacyDashInputCannotStartAirDodgeOrOldBurst()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = GroundedState(1, def);
        state.IsGrounded = false;
        state.PY += 10f;
        sim.RegisterEntity(1, def, state);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = TestHelpers.ReservedInput(0x20, new() { MoveY = -1f }) });
        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)1, after.AirDodgesLeft);
        Assert.True(MathF.Sqrt(after.VX * after.VX + after.VZ * after.VZ)
            < def.Movement.DashSpeed);
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy, 6.666667f, 11f, 3.025755f)]
    [InlineData(CharacterClass.Manki, 6f, 11f, 3.025755f)]
    [InlineData(CharacterClass.Wibou, 5.866667f, 12.1f, 3.328333f)]
    [InlineData(CharacterClass.Bonk, 6.666667f, 11f, 3.025755f)]
    public void CookedAirDodgeBurstIsShorterThanOldAirDash(
        CharacterClass character, float oldTravel, float dodgeSpeed, float burstTravel)
    {
        var def = BuiltInContentResolver.Resolve(character).Definition;
        Assert.Equal(dodgeSpeed, def.Movement.AirDodgeSpeed);
        var state = GroundedState(1, def);
        state.IsGrounded = false;
        state.PY += 30f;
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, state);
        sim.Tick(new() { [1] = new() { ShieldPressed = true } });
        TestHelpers.AssertNear(dodgeSpeed * DefenseConfig.AirDodgeLaunchScale, sim.GetState(1).VZ);
        for (int tick = 1; tick < DefenseConfig.AirDodgeMovementTicks; tick++)
            sim.Tick(new() { [1] = default });
        var burstEnd = sim.GetState(1);
        Assert.Equal(ActionState.AirDodgeMovement, burstEnd.State);
        TestHelpers.AssertNear(burstTravel, burstEnd.PZ, 0.001f);
        Assert.True(burstEnd.PZ < oldTravel);
        sim.Tick(new() { [1] = default });
        var glide = sim.GetState(1);
        Assert.Equal(ActionState.AirDodgeRecovery, glide.State);
        Assert.True(glide.PZ > burstEnd.PZ);
    }

    [Fact]
    public void VulnerableMovementTailCanBeHitWithoutRefillingDodge()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var dodger = GroundedState(1, def);
        dodger.IsGrounded = false;
        dodger.PY += 30f;
        sim.RegisterEntity(1, def, dodger);
        var attacker = GroundedState(2, def);
        attacker.PX = 20f;
        sim.RegisterEntity(2, def, attacker);
        sim.Tick(new() { [1] = new() { ShieldPressed = true }, [2] = default });
        for (int i = 1; i < DefenseConfig.AirDodgeInvulnerabilityTicks; i++)
            sim.Tick(new() { [1] = default, [2] = default });
        Assert.Equal((ushort)1, sim.GetState(1).InvincibilityTicks);
        var before = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            X = before.PX, Y = before.PY, Z = before.PZ,
            EndX = before.PX, EndY = before.PY, EndZ = before.PZ,
            Radius = 0.8f, Shape = HitboxShape.Sphere,
            Damage = 4f, BaseKnockback = 8f, KnockbackAngle = 30,
            StunTicks = 20, DurationTicks = 1, OwnerId = 2, ActivationId = 99,
        });
        sim.Tick(new() { [1] = new() { ShieldPressed = true }, [2] = default });
        var hit = sim.GetState(1);
        Assert.Equal(0, hit.InvincibilityTicks);
        Assert.Equal(4, hit.DamagePercent);
        Assert.Equal((byte)0, hit.AirDodgesLeft);
        for (int i = 0; i < 12; i++)
            sim.Tick(new() { [1] = new() { ShieldPressed = true }, [2] = default });
        Assert.Equal((byte)0, sim.GetState(1).AirDodgesLeft);
        Assert.False(sim.GetState(1).IsGrounded);
    }

    [Fact]
    public void WallContactDoesNotRestoreSpentAirDodge()
    {
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = -2f, AY = -2f, AZ = 0.5f,
                BX = 2f, BY = -2f, BZ = 0.5f, CX = -2f, CY = 3f, CZ = 0.5f },
            new CollisionTriangle { AX = 2f, AY = -2f, AZ = 0.5f,
                BX = 2f, BY = 3f, BZ = 0.5f, CX = -2f, CY = 3f, CZ = 0.5f },
        };
        var def = MakeTestDef();
        var sim = new ServerSimulation(arena);
        var state = GroundedState(1, def);
        state.PY += 1f;
        state.IsGrounded = false;
        state.AirDodgesLeft = 0;
        state.VZ = 11f;
        sim.RegisterEntity(1, def, state);
        for (int i = 0; i < 4; i++)
            sim.Tick(new() { [1] = new() { ShieldPressed = true } });
        var atWall = sim.GetState(1);
        Assert.True(atWall.PZ < 0.5f);
        Assert.False(atWall.IsGrounded);
        Assert.Equal((byte)0, atWall.AirDodgesLeft);
        Assert.NotEqual(ActionState.AirDodgeMovement, atWall.State);
    }


    [Fact]
    public void LandingDuringMovementCapsRemainingCommitment()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = GroundedState(1, def);
        state.IsGrounded = false;
        state.PY += 0.3f;
        state.VY = -2f;
        sim.RegisterEntity(1, def, state);
        var held = new Dictionary<ulong, InputState>
        {
            [1] = new() { ShieldPressed = true, ShieldHeld = true },
        };
        sim.Tick(held);
        Assert.Equal(ActionState.AirDodgeMovement, sim.GetState(1).State);
        held[1] = new InputState { ShieldHeld = true, DownPressed = true, GrabPressed = true };
        int elapsed = 0;
        while (!sim.GetState(1).IsGrounded && elapsed < DefenseConfig.AirDodgeMovementTicks)
        {
            sim.Tick(held);
            elapsed++;
        }
        var landed = sim.GetState(1);
        Assert.True(landed.IsGrounded);
        Assert.InRange(elapsed, 1, DefenseConfig.AirDodgeMovementTicks - 1);
        Assert.Equal(ActionState.AirDodgeRecovery, landed.State);
        Assert.True(DefenseConfig.AirDodgeMovementTicks - elapsed + DefenseConfig.AirDodgeRecoveryTicks
            > DefenseConfig.AirDodgeLandingTicks);
        Assert.Equal(DefenseConfig.AirDodgeLandingTicks, landed.AirDodgeRecoveryTicks);
        Assert.Equal(0f, landed.VX);
        Assert.Equal(0f, landed.VZ);
        Assert.Equal((byte)1, landed.AirDodgesLeft);
        Assert.Equal((ushort)0, landed.InvincibilityTicks);
        int remaining = landed.AirDodgeRecoveryTicks;
        for (int i = 1; i < remaining; i++)
        {
            sim.Tick(held);
            Assert.Equal(ActionState.AirDodgeRecovery, sim.GetState(1).State);
            Assert.Equal((byte)0, sim.GetState(1).AttackSlot);
        }
        sim.Tick(new() { [1] = new() { ShieldHeld = true } });
        Assert.Equal(ActionState.Shielding, sim.GetState(1).State);
    }


    [Fact]
    public void AirDodgeOnLandingBoundaryDoesNotAlsoEnterGroundShield()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = GroundedState(1, def);
        state.PY += 0.005f;
        state.IsGrounded = false;
        state.VY = -1f;
        sim.RegisterEntity(1, def, state);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new() { ShieldHeld = true, ShieldPressed = true },
        });

        var landed = sim.GetState(1);
        Assert.True(landed.IsGrounded);
        Assert.Equal(ActionState.AirDodgeRecovery, landed.State);
        Assert.NotEqual(ActionState.Shielding, landed.State);
        Assert.Equal((byte)1, landed.AirDodgesLeft);
        Assert.Equal(DefenseConfig.AirDodgeLandingTicks, landed.AirDodgeRecoveryTicks);
    }

    [Fact]
    public void LandingLateInRecoveryKeepsTheShorterRemainder()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = GroundedState(1, def);
        state.PY += 0.005f;
        state.IsGrounded = false;
        state.VY = -1f;
        state.VZ = 4f;
        state.State = ActionState.AirDodgeRecovery;
        state.StateTicks = state.AirDodgeRecoveryTicks = 4;
        state.AirDodgesLeft = 0;
        sim.RegisterEntity(1, def, state);

        sim.Tick(new() { [1] = default });
        var landed = sim.GetState(1);
        Assert.True(landed.IsGrounded);
        Assert.Equal(ActionState.AirDodgeRecovery, landed.State);
        Assert.Equal((ushort)3, landed.AirDodgeRecoveryTicks);
        Assert.Equal(0f, landed.VZ);
        for (int tick = 0; tick < 3; tick++)
            sim.Tick(new() { [1] = default });
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void ShieldRelease_RequiresFullDropBeforeReentry()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));

        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new() { ShieldHeld = true, ShieldPressed = true },
        });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Equal(ActionState.ShieldDrop, sim.GetState(1).State);
        Assert.Equal(DefenseConfig.ShieldDropTicks, sim.GetState(1).ShieldDropTicks);

        var rehold = new Dictionary<ulong, InputState>
        {
            [1] = new() { ShieldHeld = true, Jump = true },
        };
        for (int i = 0; i < DefenseConfig.ShieldDropTicks - 1; i++)
        {
            sim.Tick(rehold);
            Assert.Equal(ActionState.ShieldDrop, sim.GetState(1).State);
        }

        rehold[1] = new InputState { ShieldHeld = true };
        sim.Tick(rehold);
        Assert.Equal(ActionState.Shielding, sim.GetState(1).State);
    }

    [Fact]
    public void GrabWhiffCompletesAuthoredAnimationAfterContactWindow()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { GrabPressed = true } };
        sim.Tick(inputs);
        Assert.Equal((ushort)(DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks
            + DefenseConfig.GrabWhiffRecoveryTicks), sim.GetState(1).StateTicks);
        inputs[1] = new InputState { Jump = true, ActiveSlot = AbilitySlots.Slot1 };
        for (int i = 0; i < 10; i++)
        {
            sim.Tick(inputs);
            Assert.Equal(ActionState.GrabAttempt, sim.GetState(1).State);
        }
        Assert.Equal(DefenseConfig.GrabWhiffRecoveryTicks, sim.GetState(1).StateTicks);
        for (int i = 0; i < DefenseConfig.GrabWhiffRecoveryTicks - 1; i++) sim.Tick(inputs);
        Assert.Equal(ActionState.GrabAttempt, sim.GetState(1).State);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.NotEqual(ActionState.GrabAttempt, sim.GetState(1).State);
    }

    [Fact]
    public void AirborneVictimCaptureDoesNotRefillSpentAirDodge()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));
        var victim = GroundedState(2, def, z: 0.7f);
        victim.PY += 0.4f;
        victim.IsGrounded = false;
        victim.AirDodgesLeft = 0;
        sim.RegisterEntity(2, def, victim);
        sim.Tick(new() { [1] = new() { GrabPressed = true }, [2] = default });
        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++)
            sim.Tick(new() { [1] = default, [2] = default });
        Assert.Equal(ActionState.Grabbed, sim.GetState(2).State);
        Assert.Equal((byte)0, sim.GetState(2).AirDodgesLeft);
    }

    [Fact]
    public void GrabAttempt_CapturesAndReleasesBothParticipantsOnce()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));
        sim.RegisterEntity(2, def, GroundedState(2, def, z: 1f));
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true },
            [2] = default,
        };
        sim.Tick(inputs);
        inputs[1] = default;

        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++)
            sim.Tick(inputs);

        var attacker = sim.GetState(1);
        var victim = sim.GetState(2);
        Assert.Equal(ActionState.Grabbing, attacker.State);
        Assert.Equal(ActionState.Grabbed, victim.State);
        Assert.NotEqual(0ul, attacker.InteractionId);
        Assert.Equal(attacker.InteractionId, victim.InteractionId);
        Assert.Equal(2ul, attacker.InteractionPartnerId);
        Assert.Equal(1ul, victim.InteractionPartnerId);
        ulong interactionId = attacker.InteractionId;
        Assert.NotEqual(0u, attacker.InteractionTick);
        Assert.Equal((byte)DefenseInteractionPhase.Captured, attacker.InteractionPhase);
        Assert.Equal((byte)DefenseInteractionPhase.Captured, victim.InteractionPhase);
        var capturedPositions = (attacker.PX, attacker.PY, attacker.PZ, victim.PX, victim.PY, victim.PZ);


        for (int i = 0; i < DefenseConfig.GrabHoldTicks + DefenseConfig.ThrowReleaseTicks - 1; i++)
        {
            sim.Tick(inputs);
            if (i == DefenseConfig.GrabHoldTicks - 1)
                Assert.Equal((byte)DefenseInteractionPhase.Throwing, sim.GetState(1).InteractionPhase);
            Assert.Equal(interactionId, sim.GetState(1).InteractionId);
            Assert.Equal(interactionId, sim.GetState(2).InteractionId);
        }
        sim.Tick(inputs);
        Assert.Equal(capturedPositions,
            (sim.GetState(1).PX, sim.GetState(1).PY, sim.GetState(1).PZ,
                sim.GetState(2).PX, sim.GetState(2).PY, sim.GetState(2).PZ));

        attacker = sim.GetState(1);
        victim = sim.GetState(2);
        Assert.Equal(ActionState.Throwing, attacker.State);
        Assert.Equal(ActionState.Thrown, victim.State);
        Assert.Equal(6, victim.DamagePercent);
        Assert.Equal(0ul, attacker.InteractionId);
        Assert.Equal(0ul, victim.InteractionId);
        Assert.True(victim.KVZ > 0f);
        Assert.True(victim.HitstunTicks > 0);
        sim.Tick(inputs);
        Assert.Equal(6, sim.GetState(2).DamagePercent);
        Assert.Equal(interactionId, attacker.LastTerminalInteractionId);
        Assert.Equal(interactionId, victim.LastTerminalInteractionId);
        Assert.True(attacker.AnimLockTicks > 0);
    }

    [Fact]
    public void ReleaseTickDamageBreaksCaptureBeforeAutomaticThrow()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));
        sim.RegisterEntity(2, def, GroundedState(2, def, z: 1f));
        var hitter = GroundedState(3, def);
        hitter.PX = 20f;
        sim.RegisterEntity(3, def, hitter);
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true }, [2] = default, [3] = default,
        };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks + DefenseConfig.GrabHoldTicks
            + DefenseConfig.ThrowReleaseTicks - 1; i++)
            sim.Tick(inputs);
        var victim = sim.GetState(2);
        Assert.Equal(ActionState.Grabbed, victim.State);
        sim.Resolver.Spawn(new Hitbox
        {
            X = victim.PX, Y = victim.PY, Z = victim.PZ,
            EndX = victim.PX, EndY = victim.PY, EndZ = victim.PZ,
            Radius = 0.2f, Shape = HitboxShape.Sphere,
            Damage = 4f, BaseKnockback = 8f, KnockbackAngle = 45,
            StunTicks = 20, DurationTicks = 1, OwnerId = 3, ActivationId = 42,
        });
        sim.Tick(inputs);
        Assert.Equal(4, sim.GetState(2).DamagePercent);
        Assert.Equal(0ul, sim.GetState(1).InteractionId);
        Assert.Equal(0ul, sim.GetState(2).InteractionId);
        sim.Tick(inputs);
        Assert.Equal(4, sim.GetState(2).DamagePercent);
    }

    [Fact]
    public void SimultaneousReciprocalGrabsClashWithoutRegistrationOrderAdvantage()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var first = GroundedState(1, def);
        var second = GroundedState(2, def, z: 1f);
        second.FacingYaw = MathF.PI;
        sim.RegisterEntity(1, def, first);
        sim.RegisterEntity(2, def, second);
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true },
            [2] = new() { GrabPressed = true },
        };
        sim.Tick(inputs);
        inputs[1] = default;
        inputs[2] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++)
            sim.Tick(inputs);

        var firstAfter = sim.GetState(1);
        var secondAfter = sim.GetState(2);
        Assert.Equal(ActionState.GrabAttempt, firstAfter.State);
        Assert.Equal(ActionState.GrabAttempt, secondAfter.State);
        Assert.Equal(DefenseConfig.GrabClashRecoveryTicks, firstAfter.StateTicks);
        Assert.Equal(DefenseConfig.GrabClashRecoveryTicks, secondAfter.StateTicks);
        Assert.Equal(0ul, firstAfter.InteractionId);
        Assert.Equal(0ul, secondAfter.InteractionId);
    }

    [Fact]
    public void ShieldBubbleDoesNotExtendGrabTargetGeometry()
    {
        var grabberDef = MakeTestDef();
        var defenderDef = TestHelpers.CloneDef(grabberDef);
        defenderDef = defenderDef with { HurtboxCapsules = new[] { new HurtboxCapsule(0f, -0.45f, 0f, 0f, 0.45f, 0f, 0.1f) } };
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, grabberDef, GroundedState(1, grabberDef));
        sim.RegisterEntity(2, defenderDef, GroundedState(2, defenderDef, z: 1.32f));
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true },
            [2] = new() { ShieldHeld = true },
        };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++)
            sim.Tick(inputs);

        Assert.Equal(ActionState.Shielding, sim.GetState(2).State);
        Assert.Equal(0ul, sim.GetState(2).InteractionId);
        Assert.Equal(ActionState.GrabAttempt, sim.GetState(1).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompetingGrabsChooseNearestContactRegardlessOfRegistrationOrder(bool reverse)
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var far = GroundedState(1, def, z: -0.95f);
        var near = GroundedState(2, def, z: 0.7f);
        near.FacingYaw = MathF.PI;
        var victim = GroundedState(3, def);
        if (reverse)
        {
            sim.RegisterEntity(3, def, victim);
            sim.RegisterEntity(2, def, near);
            sim.RegisterEntity(1, def, far);
        }
        else
        {
            sim.RegisterEntity(1, def, far);
            sim.RegisterEntity(2, def, near);
            sim.RegisterEntity(3, def, victim);
        }
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true },
            [2] = new() { GrabPressed = true },
            [3] = default,
        };
        sim.Tick(inputs);
        inputs[1] = inputs[2] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++) sim.Tick(inputs);
        Assert.Equal(2ul, sim.GetState(3).InteractionPartnerId);
        Assert.Equal(ActionState.Grabbing, sim.GetState(2).State);
        Assert.Equal(ActionState.GrabAttempt, sim.GetState(1).State);
    }

    [Fact]
    public void GrabDoesNotPullOpponentThroughSolidWall()
    {
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = -2f, AY = -2f, AZ = 0.5f,
                BX = 2f, BY = -2f, BZ = 0.5f, CX = -2f, CY = 3f, CZ = 0.5f },
            new CollisionTriangle { AX = 2f, AY = -2f, AZ = 0.5f,
                BX = 2f, BY = 3f, BZ = 0.5f, CX = -2f, CY = 3f, CZ = 0.5f },
        };
        var def = MakeTestDef();
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, def, GroundedState(1, def));
        sim.RegisterEntity(2, def, GroundedState(2, def, z: 1f));
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { GrabPressed = true }, [2] = default };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks; i++)
            sim.Tick(inputs);
        Assert.Equal(0ul, sim.GetState(2).InteractionId);
        Assert.Equal(0, sim.GetState(2).DamagePercent);
    }

    [Theory]
    [InlineData(-0.8f)]
    [InlineData(2f)]
    public void FrozenForwardGrabWhiffsBehindOrOutsideShortReach(float targetZ)
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));
        sim.RegisterEntity(2, def, GroundedState(2, def, z: targetZ));
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { GrabPressed = true }, [2] = default };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks; i++)
            sim.Tick(inputs);
        Assert.Equal(0ul, sim.GetState(2).InteractionId);
        Assert.Equal(0, sim.GetState(2).DamagePercent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagingHitOnActiveGrabberWinsBeforeCapture(bool reverse)
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var grabber = GroundedState(1, def);
        var victim = GroundedState(2, def, z: 1f);
        var hitter = GroundedState(3, def);
        hitter.PX = 20f;
        if (reverse)
        {
            sim.RegisterEntity(3, def, hitter);
            sim.RegisterEntity(2, def, victim);
            sim.RegisterEntity(1, def, grabber);
        }
        else
        {
            sim.RegisterEntity(1, def, grabber);
            sim.RegisterEntity(2, def, victim);
            sim.RegisterEntity(3, def, hitter);
        }
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true }, [2] = default, [3] = default,
        };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks - 1; i++) sim.Tick(inputs);
        var position = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            X = position.PX, Y = position.PY, Z = position.PZ,
            EndX = position.PX, EndY = position.PY, EndZ = position.PZ,
            Radius = 0.2f, Shape = HitboxShape.Sphere,
            Damage = 5f, BaseKnockback = 8f, KnockbackAngle = 30,
            StunTicks = 20, DurationTicks = 1, OwnerId = 3, ActivationId = 88,
        });
        sim.Tick(inputs);
        Assert.Equal(5, sim.GetState(1).DamagePercent);
        Assert.Equal(0ul, sim.GetState(2).InteractionId);
    }

    [Theory]
    [InlineData(0.4f, true)]
    [InlineData(3f, false)]
    public void ShortGrabOnlyCatchesLowAirHurtboxOverlap(float height, bool caught)
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));
        var airborne = GroundedState(2, def, z: 0.7f);
        airborne.PY += height;
        airborne.IsGrounded = false;
        sim.RegisterEntity(2, def, airborne);
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { GrabPressed = true }, [2] = default };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++) sim.Tick(inputs);
        Assert.Equal(caught, sim.GetState(2).State == ActionState.Grabbed);
    }

    [Fact]
    public void CloseUnsafeLandingCanBeGrabbedDuringRemainingLag()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));
        var landing = GroundedState(2, def, z: 0.7f);
        landing.LandingLagTicks = 18;
        sim.RegisterEntity(2, def, landing);
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true },
            [2] = new() { ShieldHeld = true, GrabPressed = true },
        };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++) sim.Tick(inputs);
        Assert.Equal(ActionState.Grabbed, sim.GetState(2).State);
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void CookedRosterCaptureGeometryReachesCloseFrontTarget(CharacterClass character)
    {
        var content = BuiltInContentResolver.Resolve(character);
        var def = content.Definition;
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def), content.Baked);
        sim.RegisterEntity(2, def, GroundedState(2, def, z: 0.7f), content.Baked);
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { GrabPressed = true }, [2] = default };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++) sim.Tick(inputs);
        Assert.True(sim.GetState(2).State == ActionState.Grabbed,
            $"attacker={sim.GetState(1).State} ticks={sim.GetState(1).StateTicks} " +
            $"victim={sim.GetState(2).State} pos={sim.GetState(2).PZ} " +
            $"geometry={def.CaptureGeometry} baked={content.Baked != null} " +
            $"hurtboxes={sim.GetLastEntityData().Count}");
    }

    [Fact]
    public void LocalPredictionModeDoesNotCommitCoupledCapture()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena())
        {
            PredictCoupledInteractions = false,
        };
        sim.RegisterEntity(1, def, GroundedState(1, def));
        sim.RegisterEntity(2, def, GroundedState(2, def, z: 1f));
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true },
            [2] = default,
        };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++)
            sim.Tick(inputs);

        Assert.Equal(ActionState.GrabAttempt, sim.GetState(1).State);
        Assert.Equal(0ul, sim.GetState(1).InteractionId);
        Assert.Equal(ActionState.Idle, sim.GetState(2).State);
    }

    [Fact]
    public void DeathDuringCaptureTerminatesSurvivorAndPreservesRespawnBarrier()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));
        sim.RegisterEntity(2, def, GroundedState(2, def, z: 1f));
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true },
            [2] = default,
        };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++)
            sim.Tick(inputs);
        ulong interactionId = sim.GetState(1).InteractionId;

        var dying = sim.GetState(2);
        dying.PY = -30f;
        sim.SetState(2, dying);
        sim.Tick(inputs);

        var survivor = sim.GetState(1);
        var respawned = sim.GetState(2);
        Assert.Equal(ActionState.Idle, survivor.State);
        Assert.Equal(0ul, survivor.InteractionId);
        Assert.Equal(interactionId, survivor.LastTerminalInteractionId);
        Assert.Equal(ActionState.Idle, respawned.State);
        Assert.Equal(0ul, respawned.InteractionId);
        Assert.Equal(interactionId, respawned.LastTerminalInteractionId);
        Assert.Equal((byte)1, respawned.Deaths);
    }

    [Fact]
    public void AcceptedExternalHitInterruptsBothSidesOfCapture()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));
        sim.RegisterEntity(2, def, GroundedState(2, def, z: 1f));
        var hitter = GroundedState(3, def);
        hitter.PX = 20f;
        sim.RegisterEntity(3, def, hitter);
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true },
            [2] = default,
            [3] = default,
        };
        sim.Tick(inputs);
        inputs[1] = default;
        for (int i = 0; i < DefenseConfig.GrabStartupTicks; i++)
            sim.Tick(inputs);
        ulong interactionId = sim.GetState(1).InteractionId;

        var victim = sim.GetState(2);
        sim.Resolver.Spawn(new Hitbox
        {
            X = victim.PX, Y = victim.PY, Z = victim.PZ,
            EndX = victim.PX, EndY = victim.PY, EndZ = victim.PZ,
            Radius = 0.1f,
            Shape = HitboxShape.Sphere,
            Damage = 5f,
            BaseKnockback = 10f,
            KnockbackAngle = 45,
            StunTicks = 20,
            DurationTicks = 30,
            OwnerId = 3,
            ActivationId = 99,
        });
        sim.Tick(inputs);

        var attacker = sim.GetState(1);
        victim = sim.GetState(2);
        Assert.Equal(0ul, attacker.InteractionId);
        Assert.Equal(0ul, victim.InteractionId);
        Assert.Equal(interactionId, attacker.LastTerminalInteractionId);
        Assert.Equal(interactionId, victim.LastTerminalInteractionId);
        Assert.NotEqual(ActionState.Grabbed, victim.State);
        Assert.True(victim.HitstopTicks > 0);
    }

    [Fact]
    public void RestoreSnapshot_DiscardsOldRuntimeAndPreservesBlockStun()
    {
        var def = MakeTestDef();
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, GroundedState(1, def));
        var target = GroundedState(2, def, z: 1f);
        sim.RegisterEntity(2, def, target);
        var ability = new CancelMutatingAbility();
        sim.ActivateAbility(1, ability, 0, def);
        var active = sim.GetActiveAbility(1);
        Assert.NotNull(active);
        sim.Resolver.Spawn(new Hitbox
        {
            X = target.PX, Y = target.PY, Z = target.PZ,
            EndX = target.PX, EndY = target.PY, EndZ = target.PZ,
            Radius = 0.1f,
            Shape = HitboxShape.Sphere,
            Damage = 5f,
            DurationTicks = 30,
            OwnerId = 1,
            ActivationId = active!.ActivationId,
        });

        var authoritative = sim.GetState(1);
        authoritative.State = ActionState.Idle;
        authoritative.AttackSlot = 0;
        authoritative.AnimLockTicks = 0;
        authoritative.BlockStunTicks = 5;
        authoritative.BlockHitstopKind = (byte)DefenseBlockHitstopKind.ShieldContact;
        var authority = new ServerSimulation(TestHelpers.TestArena());
        authority.RegisterEntity(1, def, authoritative);
        authority.RegisterEntity(2, def, sim.GetState(2));
        var snapshot = authority.CaptureSnapshot();
        sim.RestoreSnapshot(snapshot);

        Assert.Equal(authoritative, sim.GetState(1));
        Assert.Equal(snapshot.Data, sim.CaptureSnapshot().Data);
        Assert.False(ability.Cancelled);
        Assert.Null(sim.GetActiveAbility(1));
        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new() { GrabPressed = true, Jump = true, ShieldHeld = true, ShieldPressed = true },
            [2] = default,
        });
        var after = sim.GetState(1);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((ushort)4, after.BlockStunTicks);
        Assert.Equal((ushort)0, sim.GetState(2).DamagePercent);
    }
}
