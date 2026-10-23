using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CombatReviewRegressionTests
{
    [Fact]
    public void CapabilityRecoveryKeepsItsExistingCancelClock()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var package = Path.Combine(root, "client/Unity/Assets/CharacterPackages/manki");
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(package, "character.json")))!;
        source["slots"]!.AsArray().Single(slot => slot!["id"]!.GetValue<string>() == "ground.R")!
            ["timeline"]!["stages"]![0]!["iasaTicks"] = 20;
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(package, "package.json")),
            source.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var def = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage!);
        var sim = new ServerSimulation(Arena());
        sim.RegisterEntity(1, def, Initial(def, 1));
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.R, IsAiming = true } });
        while (sim.CurrentTick < 22) sim.Tick(new());
        var before = sim.GetState(1);
        Assert.Equal(1, before.ComboStage);
        Assert.Equal(0, before.AttackElapsedTicks);
        Assert.Equal(15, before.AnimLockTicks);

        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Slot1 } });

        Assert.Equal(AbilitySlots.R, sim.GetState(1).AttackSlot);
    }

    [Fact]
    public void AnExistingProjectileOverlapKeepsItsSplashOriginAndVictims()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox { OwnerId = 1, X = -.7f, VX = 42, Radius = .1f, DurationTicks = 10,
            Explosion = new ProjectileExplosion { Radius = 1, Damage = 7 } });
        Assert.Single(resolver.Tick(new() { new() { Id = 2, Active = true, Radius = .5f } }));
        var explosion = Assert.Single(resolver.DrainPendingExplosions());
        TestHelpers.AssertNear(-.7f, explosion.x);
        var splash = new SpellResolver();
        splash.Spawn(new Hitbox { OwnerId = 1, X = explosion.x, Radius = explosion.explosion.Radius, DurationTicks = 1 });
        Assert.Single(splash.Tick(new() { new() { Id = 3, PosX = -1.8f, Radius = .15f, Active = true } }));
    }

    [Fact]
    public void InterruptedAimReleasesAttackStateAndAllowsAnotherNormal()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Manki);
        var sim = Create(entry);
        sim.RegisterEntity(2, entry.Definition, Initial(entry.Definition, 2, 20), entry.BakedAnimation);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.R, IsAiming = true } });
        Assert.NotNull(sim.GetActiveAbility(1));
        var victim = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            X = victim.PX, Y = victim.PY, Z = victim.PZ, Radius = 2,
            DurationTicks = 1, Damage = 1, StunTicks = 1, FixedHitstunTicks = 3, OwnerId = 2,
        });
        sim.Tick(new() { [1] = new InputState { IsAiming = true } });
        for (int i = 0; i < 24; i++) sim.Tick(new());
        var recovered = sim.GetState(1);
        Assert.Equal(1, recovered.DamagePercent);
        Assert.Equal(ActionState.Idle, recovered.State);
        Assert.Equal(0, recovered.HitstunTicks);
        Assert.Equal(0, recovered.HitstopTicks);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal(0, recovered.AnimLockTicks);
        Assert.Equal(AbilitySlots.None, recovered.AttackSlot);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Slot1 } });
        Assert.Equal(AbilitySlots.Slot1, sim.GetState(1).AttackSlot);
        Assert.NotNull(sim.GetActiveAbility(1));
    }

    [Fact]
    public void GroundHeavyWalkingOffALedgeKeepsGroundCancelTiming()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Wibou);
        var def = entry.Definition;
        var sim = new ServerSimulation(Arena());
        var initial = Initial(def, 1);
        initial.PZ = 98.95f;
        initial.State = ActionState.Sliding;
        initial.VZ = def.Movement.RunSpeed;
        sim.RegisterEntity(1, def, initial, entry.BakedAnimation);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Slot4 } });
        for (int i = 0; i < 35; i++) sim.Tick(new());
        var before = sim.GetState(1);
        Assert.False(before.IsGrounded);
        Assert.Equal(AbilitySlots.Slot4, before.AttackSlot);
        Assert.InRange(before.AttackElapsedTicks, 31, 62);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Slot1 } });
        Assert.Equal(AbilitySlots.Slot4, sim.GetState(1).AttackSlot);
    }

    [Theory]
    [InlineData(HitboxShape.Sphere)]
    [InlineData(HitboxShape.Capsule)]
    public void ProjectileCrossingAnOrdinaryFighterMakesContact(HitboxShape targetShape)
    {
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox
        {
            X = -2, VX = 240, Radius = .1f, DurationTicks = 10, Damage = 5, OwnerId = 1,
        });
        var hits = resolver.Tick(new()
        {
            new SpellResolver.EntityData { Id = 2, Radius = .5f, Shape = targetShape, EndY = 1, Active = true },
        });
        Assert.Single(hits);
        Assert.Equal(2UL, hits[0].TargetEntityId);
    }

    [Fact]
    public void RemovingAnOwnerRemovesProjectilesFromCompletedActivations()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Manki);
        var sim = Create(entry);
        sim.RegisterEntity(2, entry.Definition, Initial(entry.Definition, 2, 20), entry.BakedAnimation);
        var victim = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            X = victim.PX, Y = victim.PY, Z = victim.PZ, VX = 1, Radius = 1,
            DurationTicks = 20, Damage = 5, StunTicks = 1, OwnerId = 2, ActivationId = 100,
        });
        Assert.Null(sim.GetActiveAbility(2));
        sim.RemoveEntity(2);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        sim.RegisterEntity(2, entry.Definition, Initial(entry.Definition, 2, 20), entry.BakedAnimation);
        sim.Tick(new());
        Assert.Equal(0, sim.GetState(1).DamagePercent);
    }

    [Theory]
    [InlineData(AbilitySlots.E)]
    [InlineData(AbilitySlots.R)]
    [InlineData(AbilitySlots.F)]
    public void CapabilityCancellationPreservesTheIncomingReaction(byte slot)
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Wibou);
        var sim = Create(entry);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = slot, IsAiming = true } });
        Assert.NotNull(sim.GetActiveAbility(1));
        var reaction = sim.GetState(1) with
        {
            State = ActionState.Hitstun, StateTicks = 13, HitstunTicks = 12, HitstopTicks = 5,
            VX = 3, VY = 9, VZ = -4, KVX = 3, KVY = 9, KVZ = -4,
            QueuedKBDirX = .4f, QueuedKBForce = 20, QueuedKBStun = 11,
            HitReactionSequence = 8, ChargeStockSpent = 1,
        };
        sim.SetState(1, reaction);
        sim.TickAbilities(new());
        var actual = sim.GetState(1);
        reaction.ClearAttackFields();
        reaction.SetCooldown(slot, actual.GetCooldown(slot));
        Assert.Equal(reaction, actual);
        Assert.Null(sim.GetActiveAbility(1));
    }

    [Fact]
    public void RisingSlashInterruptionHasTheSameLaunchAsAnIdleVictim()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Wibou);
        var attacking = Create(entry);
        attacking.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.E } });
        var victim = attacking.GetState(1) with { PX = 0, PY = 6, PZ = 0, VX = 0, VY = 0, VZ = 0 };
        attacking.SetState(1, victim);
        var idle = Create(entry);
        var idleVictim = victim;
        idleVictim.ClearAttackFields();
        idleVictim.State = ActionState.Idle;
        idle.SetState(1, idleVictim);
        foreach (var sim in new[] { idle, attacking })
        {
            sim.RegisterEntity(2, entry.Definition, Initial(entry.Definition, 2, 20), entry.BakedAnimation);
            sim.Resolver.Spawn(new Hitbox
            {
                OwnerId = 2, X = 0, Y = 6, Z = -.3f, Radius = 2, DurationTicks = 1,
                Damage = 6, BaseKnockback = 30, KnockbackGrowth = 2, KnockbackAngle = 45,
                StunTicks = 10, FixedHitstunTicks = 20,
            });
        }
        for (int tick = 0; tick < 12; tick++)
        {
            idle.Tick(new());
            attacking.Tick(new());
            var expected = idle.GetState(1);
            var actual = attacking.GetState(1);
            Assert.Equal(expected.HitstunTicks, actual.HitstunTicks);
            Assert.Equal(expected.HitReactionSequence, actual.HitReactionSequence);
            Assert.Equal(expected.KVX, actual.KVX);
            Assert.Equal(expected.KVY, actual.KVY);
            Assert.Equal(expected.KVZ, actual.KVZ);
            if (expected.HitstopTicks == 0 && expected.HitstunTicks < 20 && actual.HitReactionSequence > 0)
            {
                Assert.Equal(expected.VX, actual.VX);
                Assert.Equal(expected.VY, actual.VY);
                Assert.Equal(expected.VZ, actual.VZ);
            }
        }
        Assert.Null(attacking.GetActiveAbility(1));
        Assert.Equal(1, attacking.GetState(1).HitReactionSequence);
    }

    [Fact]
    public void InterruptedAirAimAllowsAirDodgeOnTheFirstFreeTick()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Manki);
        var sim = Create(entry);
        sim.SetState(1, sim.GetState(1) with { PY = 10, IsGrounded = false });
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.R, IsAiming = true } });
        var victim = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 2, X = victim.PX, Y = victim.PY, Z = victim.PZ, Radius = 2,
            DurationTicks = 1, Damage = 1, StunTicks = 1, FixedHitstunTicks = 3,
        });
        sim.Tick(new());
        for (int tick = 0; tick < 30; tick++)
        {
            var state = sim.GetState(1);
            if (state.State == ActionState.Idle && state.HitstopTicks == 0 && state.HitstunTicks == 0)
                break;
            sim.Tick(new());
        }
        Assert.False(sim.GetState(1).IsGrounded);
        sim.Tick(new() { [1] = new InputState { ShieldPressed = true } });
        Assert.Equal(ActionState.AirDodgeMovement, sim.GetState(1).State);
    }

    [Fact]
    public void ActiveMoveKeepsItsVariantAndTimelineCursorThroughSnapshotRestore()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Wibou);
        var sim = Create(entry);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Slot4 } });
        for (int tick = 0; tick < 20; tick++) sim.Tick(new());
        sim.SetState(1, sim.GetState(1) with { IsGrounded = false, ComboStage = 9 });
        var move = sim.GetActiveMove(1)!.Value;
        Assert.False(move.AirborneAtStart);
        Assert.Equal("ground.4", move.Slot.Id);
        Assert.Equal(0, move.StageIndex);
        Assert.Equal(21, move.StageTick);
        var restored = Create(entry);
        restored.RestoreSnapshot(sim.CaptureSnapshot());
        Assert.Equal(move, restored.GetActiveMove(1)!.Value);
        Assert.False(Simulation.IsIasaUnlocked(sim.GetState(1), move));
    }

    [Fact]
    public void AirAimKeepsItsMobileMovementPolicyAfterLanding()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        CookedSlotDefinition AimSlot(int ordinal, bool air, AuthoringAimMovementMode movement)
            => new(ordinal, air ? "air.1" : "ground.1", air, "test", "", "",
                AuthoringAbilityBehavior.AimedProjectile, AuthoringAimMode.CameraForward3D, 0, false, false,
                new CookedTimeline(new[] { new CookedStage(120, 0, 0, 0, 0, Array.Empty<string>(),
                    new CookedTimelineOperation[] { new CookedSetAimStateOperation(0, AuthoringUnit.Ticks, AuthoringAimMode.CameraForward3D) }) }),
                aimMovement: movement, aimMovementSpeedScale: .25f);
        slots[0] = AimSlot(0, false, AuthoringAimMovementMode.Fixed);
        slots[8] = AimSlot(8, true, AuthoringAimMovementMode.Mobile);
        def = def with { CookedSlots = slots };
        var sim = new ServerSimulation(Arena());
        var state = Initial(def, 1) with { PY = 4, IsGrounded = false };
        sim.RegisterEntity(1, def, state);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Slot1, IsAiming = true } });
        sim.SetState(1, sim.GetState(1) with { PY = def.CapsuleHeight * .5f, VY = 0, IsGrounded = true });
        for (int tick = 0; tick < 10; tick++)
            sim.Tick(new() { [1] = new InputState { IsAiming = true, MoveY = 1 } });
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
        Assert.True(sim.GetActiveMove(1)!.Value.AirborneAtStart);
        Assert.Equal(def.Movement.RunSpeed * .25f, sim.GetState(1).VZ, 4);
    }

    [Fact]
    public void RemovingAnOwnerDiscardsPendingExplosionsAndEveryActivation()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Manki);
        var sim = Create(entry);
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 1, ActivationId = 10, X = 50, Radius = .1f, DurationTicks = 1,
            Explosion = new ProjectileExplosion { Radius = 4, Damage = 8 },
        });
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 2, ActivationId = 20, X = 60, Radius = .1f, DurationTicks = 1,
            Explosion = new ProjectileExplosion { Radius = 4, Damage = 8 },
        });
        sim.Resolver.Tick(new());
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, ActivationId = 11, DurationTicks = 20, CancelWithAbility = true });
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, ActivationId = 12, DurationTicks = 20, VX = 2 });
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, ActivationId = 13, DurationTicks = 20, Return = new ProjectileReturn() });
        sim.Resolver.Spawn(new Hitbox { OwnerId = 2, ActivationId = 14, DurationTicks = 20 });
        var removals = new List<ulong>();
        sim.Resolver.OnHitboxRemoved = (box, _, _, _) => removals.Add(box.ActivationId);
        sim.RemoveEntity(1);
        Assert.Equal(new ulong[] { 13, 12, 11 }, removals);
        Assert.Equal(2UL, Assert.Single(sim.Resolver.DrainPendingExplosions()).ownerId);
        Assert.Equal(2UL, Assert.Single(sim.Resolver.GetActiveHitboxes()).OwnerId);
        sim.RegisterEntity(1, entry.Definition, Initial(entry.Definition, 1), entry.BakedAnimation);
        sim.Tick(new());
        Assert.Equal(0, sim.GetState(1).DamagePercent);
    }

    [Fact]
    public void ReusedEntityIdInheritsNoKillCreditOrLastHitContext()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Manki);
        var sim = Create(entry);
        sim.RegisterEntity(2, entry.Definition, Initial(entry.Definition, 2, 20), entry.BakedAnimation);
        var victim = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 2, X = victim.PX, Y = victim.PY, Z = victim.PZ, Radius = 2,
            DurationTicks = 1, Damage = 1, StunTicks = 1,
        });
        sim.Tick(new());
        Assert.Equal(1, sim.GetState(1).DamagePercent);
        sim.RemoveEntity(2);
        sim.RegisterEntity(2, entry.Definition, Initial(entry.Definition, 2, 20), entry.BakedAnimation);
        sim.SetState(1, sim.GetState(1) with { PY = -110, IsGrounded = false });
        sim.Tick(new());
        var death = Assert.Single(sim.LastTickDeaths);
        Assert.Equal(0UL, death.KillerEntityId);
        Assert.Equal(0UL, death.LastHitEntityId);
        Assert.Equal(0, sim.GetKOs(2));
    }

    private static ServerSimulation Create(MatchContentEntry entry)
    {
        var sim = new ServerSimulation(Arena());
        sim.RegisterEntity(1, entry.Definition, Initial(entry.Definition, 1), entry.BakedAnimation);
        return sim;
    }

    private static CharacterState Initial(CharacterDefinition def, ulong id, float x = 0) => new()
    {
        EntityId = id, PX = x, PY = def.CapsuleHeight * .5f, State = ActionState.Idle,
        IsGrounded = true, JumpsLeft = 2, AirDodgesLeft = 1,
    };

    private static ArenaDefinition Arena() => new()
    {
        Name = "review-regression", KillHeight = -100, SpawnPoints = new[] { new SpawnPoint() },
        Heightmap = new ArenaHeightmap
        {
            Data = new float[40000], Width = 200, Height = 200, CellSize = 1, OriginX = -100, OriginZ = -100,
        },
    };
}
