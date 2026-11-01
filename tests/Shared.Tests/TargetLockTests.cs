using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Explicit and automatic target locking are resolved in Shared. Locks retain a valid
/// target within 20m; user retarget selects the nearest eligible enemy. LMB facing snap
/// never exits lock, and owner death resets the lock.
/// </summary>
public class TargetLockTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.EngineDef;
    private static float Gpy => Def.CapsuleHeight * 0.5f;

    /// <summary>
    /// Test arena whose spawn point sits at feet-on-floor (Y = capsule half), so a
    /// void-death respawn lands grounded instead of sinking below the floor surface
    /// (TestArena's default spawn Y=0 is below groundY = 0.75 for this def).
    /// </summary>
    private static ArenaDefinition DeathArena()
    {
        var arena = TestHelpers.TestArena();
        arena.SpawnPoints = new[] { new SpawnPoint { X = 0, Y = Gpy, Z = 0, Yaw = 0 } };
        return arena;
    }

    // ────────────────────────── Golden and behavioral scenarios ──────────────────────────

    [Fact]
    public void LockOn_Disengages_OutOfRange_AndMovementContinues()
    {
        // The 20m boundary is behavioral: once the selected target exceeds it, LockOn
        // clears while ordinary ground movement continues.
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        Assert.True(sim.GetState(1).LockOn, "lock should engage while target is in range");

        bool disengaged = false;
        float disengagedX = 0f;
        for (int tick = 1; tick < 200; tick++)
        {
            sim.Tick(new() { { 1, new InputState { MoveX = 1f } } });
            var state = sim.GetState(1);
            if (!disengaged && !state.LockOn)
            {
                disengaged = true;
                disengagedX = state.PX;
                Assert.True(state.PX * state.PX + 9f > 400f,
                    $"lock cleared before the target exceeded 20m: x={state.PX:F3}");
            }
        }

        var final = sim.GetState(1);
        Assert.True(disengaged, "lock must clear after the target exceeds lock range");
        Assert.False(final.LockOn);
        Assert.True(final.PX > disengagedX + 0.1f,
            $"movement must continue after disengagement: crossing={disengagedX:F3}, final={final.PX:F3}");
    }


    [Fact]
    public void LockOn_LmbSnapKeepsTargetIdentity()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f });
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } });

        var state = sim.GetState(1);
        Assert.True(state.LockOn);
        Assert.Equal(100UL, state.TargetEntityId);
        TestHelpers.AssertNear(MathF.PI, state.FacingYaw, 1e-4f);
    }

    [Fact]
    public void LockOn_DeathRetainsRespawnedTarget()
    {
        var sim = TestHelpers.MakeSim(DeathArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState(0f, 5f) with { PY = Gpy });
        sim.RegisterEntity(100, Def,
            TestHelpers.NpcState(0f, -1f) with { PY = -25f, IsGrounded = false });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        for (int tick = 0; tick < 120; tick++)
            sim.Tick(new() { [1] = default });

        var state = sim.GetState(1);
        Assert.True(state.LockOn);
        Assert.Equal(100UL, state.TargetEntityId);
        Assert.True(sim.GetState(100).Deaths > 0);
    }

    [Fact]
    public void AutoLock_AlwaysAcquiresAndToggleOffStaysSuppressedUntilReenabled()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 4f) with { PY = Gpy });
        sim.RegisterEntity(101, Def, TestHelpers.NpcState(0f, 2f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { LockMode = TargetLockMode.Always, TargetEntityId = 100 } } });
        Assert.True(sim.GetState(1).LockOn);
        Assert.Equal(100UL, sim.GetState(1).TargetEntityId);

        sim.Tick(new() { { 1, new InputState { LockMode = TargetLockMode.Always, TargetEntityId = 101, ToggleLock = true } } });
        Assert.False(sim.GetState(1).LockOn);
        Assert.True(sim.GetState(1).AutoLockSuppressed);

        sim.Tick(new() { { 1, new InputState { LockMode = TargetLockMode.Always } } });
        Assert.False(sim.GetState(1).LockOn);

        sim.Tick(new() { { 1, new InputState { LockMode = TargetLockMode.Always, TargetEntityId = 101, ToggleLock = true } } });
        Assert.True(sim.GetState(1).LockOn);
        Assert.False(sim.GetState(1).AutoLockSuppressed);
        Assert.Equal(101UL, sim.GetState(1).TargetEntityId);
    }

    [Fact]
    public void NeverModeIsManualAndRetargetChoosesClosestEligibleEnemy()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 4f) with { PY = Gpy });
        sim.RegisterEntity(101, Def, TestHelpers.NpcState(0f, 2f) with { PY = Gpy });

        sim.Tick(new() { { 1, default } });
        Assert.False(sim.GetState(1).LockOn);

        sim.Tick(new() { { 1, new InputState { ToggleLock = true, TargetEntityId = 100 } } });
        Assert.True(sim.GetState(1).LockOn);
        Assert.Equal(100UL, sim.GetState(1).TargetEntityId);

        sim.Tick(new() { { 1, new InputState { TargetEntityId = 101 } } });
        Assert.Equal(100UL, sim.GetState(1).TargetEntityId);

        sim.Tick(new() { { 1, new InputState { RetargetPressed = true, TargetEntityId = 100 } } });
        Assert.True(sim.GetState(1).LockOn);
        Assert.Equal(101UL, sim.GetState(1).TargetEntityId);
    }
    [Fact]
    public void ClientTargetSelection_IsValidatedBeforeLockAcquisition()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 2f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true, TargetEntityId = 1 } } });

        Assert.True(sim.GetState(1).LockOn);
        Assert.Equal(100UL, sim.GetState(1).TargetEntityId);
    }


    private static void SpawnContact(ServerSimulation sim, in CharacterState defender)
    {
        sim.Resolver.Spawn(new Hitbox
        {
            X = defender.PX,
            Y = defender.PY,
            Z = defender.PZ,
            EndX = defender.PX,
            EndY = defender.PY,
            EndZ = defender.PZ,
            Radius = 0.45f,
            Shape = HitboxShape.Sphere,
            Damage = 5f,
            BaseKnockback = 2f,
            KnockbackGrowth = 3f,
            StunTicks = 20,
            DurationTicks = 20,
            OwnerId = 1,
            ActivationId = 17,
            AttackSlot = 1,
        });
    }

    [Fact]
    public void OnHitModeLocksBothParticipantsOnConfirmedDamagingContact()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState(-2f, 0f) with { PY = Gpy });
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 0f) with { PY = Gpy });
        SpawnContact(sim, sim.GetState(100));

        sim.Tick(new()
        {
            { 1, new InputState { LockMode = TargetLockMode.OnHit } },
            { 100, new InputState { LockMode = TargetLockMode.OnHit } },
        });

        Assert.True(sim.GetState(1).LockOn);
        Assert.Equal(100UL, sim.GetState(1).TargetEntityId);
        Assert.True(sim.GetState(100).LockOn);
        Assert.Equal(1UL, sim.GetState(100).TargetEntityId);
        Assert.True(sim.GetState(100).DamagePercent > 0);
    }

    [Fact]
    public void OnHitModeDoesNotLockOnBlockedContact()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState(-2f, 0f) with { PY = Gpy });
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 0f) with { PY = Gpy });
        SpawnContact(sim, sim.GetState(100));

        sim.Tick(new()
        {
            { 1, new InputState { LockMode = TargetLockMode.OnHit } },
            { 100, new InputState { LockMode = TargetLockMode.OnHit, ShieldHeld = true } },
        });

        Assert.False(sim.GetState(1).LockOn);
        Assert.False(sim.GetState(100).LockOn);
        Assert.True(sim.LastTickHits[0].Blocked);
        Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
    }

    // ────────────────────────── Behavioral ──────────────────────────

    [Fact]
    public void Lock_Idle_KeepsFacing_ToggleOff_FreezesIt()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = MathF.PI };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        // t0: toggle on → facing is NOT steered while idle (lock is passive); it stays PI
        // even though the target sits at +Z (yaw 0).
        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        var locked = sim.GetState(1);
        Assert.True(locked.LockOn);
        TestHelpers.AssertNear(MathF.PI, locked.FacingYaw, 1e-4f);

        // Hold the lock 29 more ticks: facing still untouched
        for (int i = 0; i < 29; i++) sim.Tick(new() { { 1, default } });
        float facingAt30 = sim.GetState(1).FacingYaw;
        TestHelpers.AssertNear(MathF.PI, facingAt30, 1e-4f);

        // t30: toggle off → facing unchanged (no movement input, no steering either way)
        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        Assert.False(sim.GetState(1).LockOn);
        for (int i = 0; i < 70; i++) sim.Tick(new() { { 1, default } });
        TestHelpers.AssertNear(facingAt30, sim.GetState(1).FacingYaw, 1e-4f);
    }

    [Fact]
    public void Lock_Air_KeepsStickyFacing_NotSteeredToTarget()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = 2f, IsGrounded = false, JumpsLeft = 0, FacingYaw = MathF.PI };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        // Lock does NOT override sticky air facing: while airborne (falls in ~4 ticks)
        // facing stays PI — the target sits at +Z (yaw 0) but the lock is passive.
        var airborne = sim.GetState(1);
        Assert.False(airborne.IsGrounded);
        TestHelpers.AssertNear(MathF.PI, airborne.FacingYaw, 1e-4f);

        for (int i = 0; i < 100; i++) sim.Tick(new() { { 1, default } });

        var state = sim.GetState(1);
        Assert.True(state.LockOn, "air lock stays on (target alive, in range)");
        TestHelpers.AssertNear(MathF.PI, state.FacingYaw, 1e-4f);
    }

    [Fact]
    public void Locked_GroundMovement_RefacesToWalk()
    {
        // Walking perpendicular (+X) while locked re-faces the fighter to the walk
        // direction (the lock is passive — it does NOT steer facing while moving).
        // So facing points +X (PI/2), NOT at the NPC.
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, new InputState { MoveX = 1f } } }); // walking +X, perpendicular

        var state = sim.GetState(1);
        Assert.True(state.LockOn, "still in range (~6.7m separation)");
        Assert.True(state.PX > 5f, "player actually walked");
        // Facing follows the walk direction (+X), not the target (yaw toward NPC ~0).
        TestHelpers.AssertNear(MathF.PI / 2f, state.FacingYaw, 1e-3f);
    }

    [Fact]
    public void FightGuy_NonTargetEnabledSpecials_DoNotAutoFace()
    {
        var def = TestHelpers.FightGuyDef;
        AssertLockedAttackDoesNotFace(def, AbilitySlots.E, 18000);
        AssertLockedAttackDoesNotFace(def, AbilitySlots.R);
        // F, Title Match, lunges at the current target by design (ADR-0053); TitleMatchTests covers its bearing.
    }

    private static void AssertLockedAttackDoesNotFace(CharacterDefinition def, byte activeSlot, short aimYaw = 0)
    {
        var sim = TestHelpers.MakeSim();
        float gpy = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = gpy, FacingYaw = MathF.PI });
        sim.RegisterEntity(100, def, TestHelpers.NpcState(0f, 5f) with { PY = gpy });
        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        sim.Tick(new() { { 1, new InputState { AimYaw = aimYaw } } });
        sim.Tick(new() { { 1, new InputState { ActiveSlot = activeSlot, AimYaw = aimYaw } } });
        sim.Tick(new() { { 1, default } });
        var state = sim.GetState(1);
        Assert.True(state.LockOn);
        Assert.True(MathF.Abs(MathF.PI - state.FacingYaw) <= 1e-3f, $"active slot {activeSlot} changed facing to {state.FacingYaw}.");
    }

    [Fact]
    public void LmbSnap_KeepsLock_AndFacesCamera()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });
        for (int i = 0; i < 9; i++) sim.Tick(new() { { 1, default } });
        Assert.True(sim.GetState(1).LockOn);

        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } });
        var snapped = sim.GetState(1);
        Assert.True(snapped.LockOn, "accepted LMB snap must not exit the lock");
        TestHelpers.AssertNear(MathF.PI, snapped.FacingYaw, 1e-4f);

        for (int i = 0; i < 30; i++) sim.Tick(new() { { 1, default } });
        Assert.True(sim.GetState(1).LockOn);
    }

    [Fact]
    public void LmbSnap_RejectedMidAttack_KeepsLock()
    {
        // The snap is rejected while attack-locked (same gate as the input layer), so
        // the lock survives: "LMB is otherwise unchanged" (ADR-0018 decision 5).
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var player = TestHelpers.PlayerState() with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } });          // lock on
        sim.Tick(new() { { 1, new InputState { ActiveSlot = AbilitySlots.Normal1 } } }); // attack (AnimLockTicks > 0)
        sim.Tick(new() { { 1, new InputState { FaceToCamera = true, AimYaw = 18000 } } }); // LMB mid-attack

        var state = sim.GetState(1);
        Assert.True(state.AnimLockTicks > 0, "mid-attack");
        Assert.True(state.LockOn, "rejected snap must not exit the lock");
        Assert.True(MathF.Abs(state.FacingYaw) < 1f, "no snap: facing stayed on the attack target");
    }

    [Fact]
    public void OwnerDeath_ResetsLockState()
    {
        // Respawn starts fresh even when the previous lock was explicitly suppressed.
        // Spawn the player at Z=-1 — just off the 200x200 heightmap grid (origin 0,0),
        // so the below-floor force-snap doesn't rescue it: it falls freely into the
        // void and dies (blast Deaths=1).
        var sim = TestHelpers.MakeSim(DeathArena());
        var player = TestHelpers.PlayerState(0f, -1f) with
        {
            PY = -25f,
            IsGrounded = false,
            FacingYaw = 0f,
            LockOn = true,
            AutoLockSuppressed = true,
            TargetEntityId = 100,
        };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, 3f) with { PY = Gpy });

        sim.Tick(new() { { 1, default } });
        for (int i = 0; i < 5; i++) sim.Tick(new() { { 1, default } });

        var state = sim.GetState(1);
        Assert.Equal((byte)1, state.Deaths);
        Assert.False(state.LockOn, "fresh respawn state has the lock off");
        Assert.False(state.AutoLockSuppressed, "fresh respawn clears the manual suppression latch");
    }

    [Fact]
    public void TargetDeath_LockRetargetsRespawnedEnemy()
    {
        // Behavioral side of the death golden: after the NPC dies and respawns in range,
        // the lock stays on and the resolver re-targets the respawned enemy (facing is
        // not steered while idle — only the target pointer updates).
        // NPC spawns at (0,-1) — off the heightmap grid, so no below-floor force-snap;
        // it falls through the void and dies end of tick 0.
        var sim = TestHelpers.MakeSim(DeathArena());
        var player = TestHelpers.PlayerState(0f, 5f) with { PY = Gpy, FacingYaw = 0f };
        sim.RegisterEntity(1, Def, player);
        sim.RegisterEntity(100, Def, TestHelpers.NpcState(0f, -1f) with { PY = -25f, IsGrounded = false });

        sim.Tick(new() { { 1, new InputState { ToggleLock = true } } }); // t0: lock on; NPC dies end of tick
        for (int i = 0; i < 60; i++) sim.Tick(new() { { 1, default } });

        var playerState = sim.GetState(1);
        var npc = sim.GetState(100);
        Assert.Equal((byte)1, npc.Deaths);
        Assert.True(npc.IsGrounded, "NPC respawned at the arena spawn");
        Assert.True(playerState.LockOn, "lock survives target death when the respawn is in range");
        Assert.Equal(100UL, playerState.TargetEntityId);
    }
}
