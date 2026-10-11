using System;
using System.Collections.Generic;

using Xunit;

namespace SlopArena.Shared.Tests;
using SlopArena.Shared.AI;

/// <summary>
/// CPU policy decisions are driven by delayed, runner-owned opponent observations. The tests
/// inspect only the public InputState seam while retaining the existing movement, attack, lock,
/// and seeded-determinism coverage.
/// </summary>

public class BotPolicyTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static readonly HeuristicBotPolicy Policy = new();

    private static ArenaDefinition RecoveryArena()
    {
        const int size = 33;
        var data = new float[size * size];
        Array.Fill(data, float.MinValue);
        for (int z = 10; z <= 22; z++)
        for (int x = 10; x <= 22; x++)
            data[z * size + x] = 0f;
        return new ArenaDefinition
        {
            Name = "recovery-test",
            KillHeight = -20f,
            MinX = -6f,
            MaxX = 6f,
            MinZ = -6f,
            MaxZ = 6f,
            Heightmap = new ArenaHeightmap
            {
                Data = data,
                Width = size,
                Height = size,
                CellSize = 1f,
                OriginX = -16f,
                OriginZ = -16f,
            },
            SpawnPoints = new[] { new SpawnPoint { X = 0f, Y = 0f, Z = 0f } },
        };
    }

    private static ArenaDefinition BlockedRecoveryArena()
    {
        var arena = RecoveryArena();
        arena.MinX = -5f;
        arena.MaxX = 5f;
        arena.MinZ = -5f;
        arena.MaxZ = 5f;
        for (int z = 0; z < arena.Heightmap.Height; z++)
        for (int x = 0; x < arena.Heightmap.Width; x++)
            if (x < 10 || x > 16 || z < 10 || z > 22)
                arena.Heightmap.Data[z * arena.Heightmap.Width + x] = float.MinValue;

        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle
            {
                AX = -6f, AY = 0f, AZ = -6f,
                BX = -6f, BY = 0f, BZ = 6f,
                CX = 0f, CY = 0f, CZ = -6f,
            },
            new CollisionTriangle
            {
                AX = 0f, AY = 0f, AZ = 6f,
                BX = 0f, BY = 0f, BZ = -6f,
                CX = -6f, CY = 0f, CZ = 6f,
            },
            new CollisionTriangle
            {
                AX = -6f, AY = 2f, AZ = -6f,
                BX = -6f, BY = 2f, BZ = 6f,
                CX = 6f, CY = 2f, CZ = -6f,
            },
            new CollisionTriangle
            {
                AX = 6f, AY = 2f, AZ = 6f,
                BX = 6f, BY = 2f, BZ = -6f,
                CX = -6f, CY = 2f, CZ = 6f,
            },
        };
        return arena;
    }
    private static CharacterState Self(float x = 0f, float z = 0f)
    {
        var s = TestHelpers.PlayerState(x, z);
        s.PY = TestHelpers.GroundPY(Def);
        return s;
    }

    private static CharacterState Opponent(float x = 0f, float z = 0f)
    {
        var s = TestHelpers.PlayerState(x, z);
        s.PY = TestHelpers.GroundPY(Def);
        s.EntityId = 100;
        return s;
    }

    private static void Prime(BotMemory memory, CharacterState target)
    {
        int delay = BotDifficultyProfile.ForDifficulty(memory.Difficulty).ReactionDelayTicks;
        for (int i = 0; i <= delay; i++)
            memory.ObserveOpponent(target);
    }

    private static InputState Decide(CharacterState self, CharacterState target, int seed = 42, BotMemory? memory = null)
    {
        memory ??= new BotMemory();
        Prime(memory, target);
        return Policy.Decide(self, target, Def, new Random(seed), memory);
    }
    private static void LockNonAimSlots(ref CharacterState state)
    {
        foreach (var slot in new[]
        {
            AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
            AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
        })
            state.SetCooldown(slot, 999);
    }

    private static CharacterDefinition IasaCooldownDef(ushort cooldown)
    {
        var def = TestHelpers.EngineDef;
        var slots = System.Linq.Enumerable.ToArray(def.CookedSlots!);
        foreach (var (ordinal, id, air, ticks) in new[]
        {
            (0, "ground.1", false, cooldown), (1, "ground.2", false, (ushort)0),
            (8, "air.1", true, cooldown), (9, "air.2", true, (ushort)0),
        })
            slots[ordinal] = new CookedSlotDefinition(
                ordinal, id, air, "IASA fixture", "", "",
                AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, ticks, false, false,
                new CookedTimeline(new[]
                {
                    new CookedStage(20, 4, 0, 0, 0, Array.Empty<string>(),
                        new CookedTimelineOperation[]
                        {
                            new CookedSpawnHitboxOperation(1, AuthoringUnit.Meters,
                                new CookedHitbox(AuthoringHitboxShape.Sphere, 1,
                                    0, 0, 1, 0, 0, 0, null, null, 6, 30, 4, 20, 12, 2, true, 0)),
                        }),
                }));
        def.CookedSlots = slots;
        return def;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Iasa_AttackSelection_ExcludesPendingCooldownSlot(bool airborne)
    {
        var def = IasaCooldownDef(120);
        var self = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def), IsGrounded = !airborne,
            State = ActionState.Attacking, AttackSlot = AbilitySlots.Slot1,
            AttackElapsedTicks = 4, AnimLockTicks = 16,
        };
        var target = self with
        {
            EntityId = 2, PZ = 1, State = ActionState.Idle, AttackSlot = 0,
        };
        var policy = new HeuristicBotPolicy();
        int alternatives = 0;
        for (int seed = 0; seed < 32; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(memory, target);
            var input = policy.Decide(self, target, def, new Random(seed), memory, RecoveryArena());
            Assert.NotEqual(AbilitySlots.Slot1, input.ActiveSlot);
            if (input.ActiveSlot == AbilitySlots.Slot2) alternatives++;
        }
        Assert.True(alternatives > 0, "Another ready move must remain selectable at IASA.");
    }

    [Theory]
    [InlineData(false, 120, AbilitySlots.Slot1, false)]
    [InlineData(true, 120, AbilitySlots.Slot1, false)]
    [InlineData(false, 0, AbilitySlots.Slot1, true)]
    [InlineData(false, 120, AbilitySlots.Slot2, true)]
    public void Iasa_QueuedPress_RespectsPendingCooldown(
        bool airborne, ushort cooldown, byte slot, bool accepted)
    {
        var def = IasaCooldownDef(cooldown);
        var self = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def), IsGrounded = !airborne,
            State = ActionState.Attacking, AttackSlot = AbilitySlots.Slot1,
            AttackElapsedTicks = 4, AnimLockTicks = 16,
        };
        var target = self with { EntityId = 2, PZ = 1, State = ActionState.Idle, AttackSlot = 0 };
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        Prime(memory, target);
        memory.StartPlan(slot, false, 0, 0, 0, 0, self.Deaths, self.IsGrounded, pressIssued: false);

        var input = new HeuristicBotPolicy().Decide(
            self, target, def, new Random(42), memory, RecoveryArena());

        Assert.Equal(accepted ? slot : AbilitySlots.None, input.ActiveSlot);
        Assert.Equal(accepted, memory.PlanPressIssued);
    }

    [Fact]
    public void FarOpponent_ApproachesWithWorldSpaceMovement_DespiteReservedBurstRecovery()
    {
        var self = Self();
        self.BurstRecoveryTicks = ushort.MaxValue;
        var target = Opponent(z: 50f); // well beyond every resolved move envelope on +Z

        var input = Decide(self, target);

        // Approaches toward the target: MoveY IS the world Z axis.
        Assert.Equal(0f, input.MoveX);
        Assert.Equal(1f, input.MoveY);
        Assert.Equal(0, input.ActiveSlot); // out of perceived range → no attack
        Assert.False(input.Jump);
    }

    [Fact]
    public void FarOpponentToTheSide_MovesAndFacesCorrectly()
    {
        var self = Self();
        var target = Opponent(x: 50f, z: 0f); // well beyond every resolved move envelope on +X

        var input = Decide(self, target);

        Assert.Equal(1f, input.MoveX);
        Assert.Equal(0f, input.MoveY);
        // Facing: atan2(+X, 0) = 90° → AimYaw deg×100 = 9000 (reuse the game's facing snap).
        Assert.Equal(9000, input.AimYaw);
        Assert.True(input.FaceToCamera);
    }

    [Fact]
    public void OpponentInReach_AttacksWithASlot()
    {
        var self = Self();
        var target = Opponent(z: 0.5f); // well within connect range

        var input = Decide(self, target);

        Assert.True(input.ActiveSlot > 0, $"expected an attack press, got ActiveSlot={input.ActiveSlot}");
    }
    [Fact]
    public void InRangeNormalDifficulty_PrefersNormalsOverSpecials()
    {
        var self = Self();
        var target = Opponent(z: 0.75f);
        int normals = 0;
        int specials = 0;

        for (int seed = 0; seed < 256; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Normal };
            Prime(memory, target);
            byte slot = Policy.Decide(self, target, Def, new Random(seed), memory).ActiveSlot;
            if (slot is >= AbilitySlots.Slot1 and <= AbilitySlots.Slot4)
                normals++;
            else if (slot is AbilitySlots.A or AbilitySlots.E or AbilitySlots.R or AbilitySlots.F)
                specials++;
        }

        Assert.True(normals > specials, $"expected normal attacks to outnumber specials, got normals={normals}, specials={specials}");
    }


    [Fact]
    public void InHitstun_NeverEmitsActionInput()
    {
        var self = Self(z: 0f);
        self.HitstunTicks = 10;
        var target = Opponent(z: 0.5f);

        var input = Decide(self, target);

        Assert.Equal(0, input.ActiveSlot);
        Assert.False(input.Jump);
    }

    [Fact]
    public void InHitstop_NeverEmitsActionInput()
    {
        var self = Self();
        self.HitstopTicks = 5;
        var target = Opponent(z: 0.5f);

        var input = Decide(self, target);

        Assert.Equal(0, input.ActiveSlot);
        Assert.False(input.Jump);
    }
    [Fact]
    public void ThreatenedBotUsesShieldWhenDefenseWins()
    {
        var self = Self();
        LockNonAimSlots(ref self);
        self.SetCooldown(AbilitySlots.A, 999);
        var target = Opponent(z: 0.5f);
        target.State = ActionState.Attacking;
        bool shielded = false;
        for (int seed = 0; seed < 64; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(memory, target);
            var input = Policy.Decide(self, target, Def, new Random(seed), memory);
            if (input.ShieldPressed)
            {
                shielded = true;
                Assert.True(input.ShieldHeld);
                Assert.Equal(0, input.ActiveSlot);
            }
            Assert.False(input.Burst);
        }
        Assert.True(shielded, "No seeded decision selected shield against a grounded threat.");
    }

    [Fact]
    public void ShieldingBotHoldsDefenseOnlyWhileObservedOpponentThreatens()
    {
        var self = Self();
        self.State = ActionState.Shielding;
        var target = Opponent(z: 0.5f);
        target.State = ActionState.Attacking;
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        Prime(memory, target);

        var input = Policy.Decide(self, target, Def, new Random(42), memory);

        Assert.True(input.ShieldHeld);
        Assert.False(input.ShieldPressed);
        Assert.False(input.Burst);

        target.State = ActionState.Idle;
        Prime(memory, target); // decision uses the delayed observation, not the live target
        input = Policy.Decide(self, target, Def, new Random(42), memory);
        Assert.False(input.ShieldHeld);
    }

    [Fact]
    public void MovementMagnitude_NeverExceedsOne()
    {
        var self = Self();
        var rng = new Random(1);
        for (int i = 0; i < 100; i++)
        {
            var target = Opponent((float)(rng.NextDouble() * 20 - 10), (float)(rng.NextDouble() * 20 - 10));
            var input = Decide(self, target, seed: i);
            Assert.True(MathF.Sqrt(input.MoveX * input.MoveX + input.MoveY * input.MoveY) <= 1f + 0.0001f,
                $"movement magnitude exceeded 1: ({input.MoveX},{input.MoveY})");
        }
    }

    [Fact]
    public void SameSeed_ProducesIdenticalDecisions()
    {
        var self = Self();
        var target = Opponent(x: 3f, z: 4f);

        var a = Decide(self, target, seed: 99);
        var b = Decide(self, target, seed: 99);

        Assert.Equal(a.ActiveSlot, b.ActiveSlot);
        Assert.Equal(a.MoveX, b.MoveX);
        Assert.Equal(a.MoveY, b.MoveY);
        Assert.Equal(a.AimYaw, b.AimYaw);
        Assert.Equal(a.ShieldHeld, b.ShieldHeld);
        Assert.Equal(a.ShieldPressed, b.ShieldPressed);
        Assert.Equal(a.Jump, b.Jump);
    }

    [Fact]
    public void LockedState_EmitsNoActionInput()
    {
        var self = Self();
        self.AnimLockTicks = 4;
        var target = Opponent(z: 0.5f);

        var input = Decide(self, target);

        Assert.Equal(0f, input.MoveX);
        Assert.Equal(0f, input.MoveY);
        Assert.Equal(0, input.ActiveSlot);
        Assert.Equal(0, input.AimYaw);
        Assert.False(input.FaceToCamera);
        Assert.False(input.Jump);
    }


    [Theory]
    [InlineData(CpuDifficulty.Easy, 24)]
    [InlineData(CpuDifficulty.Normal, 18)]
    [InlineData(CpuDifficulty.Hard, 12)]
    public void ReactionDelay_UsesOnlyInformationAtOrBeyondNamedBoundary(
        CpuDifficulty difficulty, int expectedDelay)
    {
        var self = Self();
        var oldTarget = Opponent(z: 10f);
        var changedTarget = Opponent(z: 0.5f);
        changedTarget.State = ActionState.Attacking;
        var oldMemory = new BotMemory { Difficulty = difficulty };
        var changedMemory = new BotMemory { Difficulty = difficulty };
        var oldRng = new Random(184);
        var changedRng = new Random(184);

        for (int tick = 0; tick < expectedDelay; tick++)
        {
            var oldInput = Policy.Decide(self, oldTarget, Def, oldRng, oldMemory);
            var changedInput = Policy.Decide(self, changedTarget, Def, changedRng, changedMemory);
            Assert.Equal(oldInput.MoveX, changedInput.MoveX);
            Assert.Equal(oldInput.MoveY, changedInput.MoveY);
            Assert.Equal(oldInput.ActiveSlot, changedInput.ActiveSlot);
            Assert.Equal(oldInput.ShieldHeld, changedInput.ShieldHeld);
            Assert.Equal(oldInput.ShieldPressed, changedInput.ShieldPressed);
            Assert.Equal(oldInput.Jump, changedInput.Jump);
            Assert.Equal(oldInput.AimYaw, changedInput.AimYaw);
            Assert.Equal(oldInput.FaceToCamera, changedInput.FaceToCamera);
        }

        var oldAtBoundary = Policy.Decide(self, oldTarget, Def, oldRng, oldMemory);
        var changedAtBoundary = Policy.Decide(self, changedTarget, Def, changedRng, changedMemory);
        bool changed = oldAtBoundary.MoveX != changedAtBoundary.MoveX
            || oldAtBoundary.MoveY != changedAtBoundary.MoveY
            || oldAtBoundary.ActiveSlot != changedAtBoundary.ActiveSlot
            || oldAtBoundary.ShieldHeld != changedAtBoundary.ShieldHeld
            || oldAtBoundary.ShieldPressed != changedAtBoundary.ShieldPressed
            || oldAtBoundary.Jump != changedAtBoundary.Jump;
        Assert.True(changed, "opponent change did not become observable at the delay boundary");
    }

    [Fact]
    public void ConfirmedHitMemory_EnablesMoreHardDifficultyFollowUps()
    {
        var self = Self();
        var target = Opponent(z: 0.5f);
        int easyAttacks = 0;
        int hardAttacks = 0;

        for (int seed = 0; seed < 100; seed++)
        {
            var easyMemory = new BotMemory { Difficulty = CpuDifficulty.Easy };
            var hardMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(easyMemory, target);
            Prime(hardMemory, target);
            easyMemory.RecordOpponentHit(AbilitySlots.Slot1, target, 18, 0);
            hardMemory.RecordOpponentHit(AbilitySlots.Slot1, target, 18, 0);
            for (int i = 0; i < BotDifficultyProfile.ForDifficulty(easyMemory.Difficulty).ReactionDelayTicks; i++)
                easyMemory.ObserveOpponent(target);
            for (int i = 0; i < BotDifficultyProfile.ForDifficulty(hardMemory.Difficulty).ReactionDelayTicks; i++)
                hardMemory.ObserveOpponent(target);
            if (Policy.Decide(self, target, Def, new Random(seed), easyMemory).ActiveSlot > 0)
                easyAttacks++;
            if (Policy.Decide(self, target, Def, new Random(seed), hardMemory).ActiveSlot > 0)
                hardAttacks++;
        }

        Assert.True(hardAttacks > easyAttacks,
            $"expected more Hard follow-ups, got easy={easyAttacks} hard={hardAttacks}");
    }

    [Fact]
    public void ConfirmedHit_SurvivesReactionDelayAndActionLock()
    {
        var self = Self();
        self.State = ActionState.Attacking;
        self.AttackSlot = AbilitySlots.Slot1;
        self.AnimLockTicks = 6;
        var target = Opponent(z: 0.5f);
        target.State = ActionState.Attacking;
        target.AttackSlot = AbilitySlots.Slot1;
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        Prime(memory, target);
        memory.RecordOpponentHit(AbilitySlots.Slot1, target, 0, 4);

        int delay = BotDifficultyProfile.ForDifficulty(memory.Difficulty).ReactionDelayTicks;
        var rng = new Random(42);
        for (int i = 0; i < delay; i++)
            Assert.Equal(0, Policy.Decide(self, target, Def, rng, memory).ActiveSlot);

        self.State = ActionState.Idle;
        self.AttackSlot = 0;
        self.AnimLockTicks = 0;
        var followUp = Policy.Decide(self, target, Def, rng, memory);

        Assert.True(followUp.ActiveSlot > 0,
            "confirmed hit was forgotten while the CPU was action-locked");
    }

    [Fact]
    public void AttackPress_IsNotFollowedByMandatoryRetreatWindow()
    {
        var self = Self();
        var target = Opponent(z: 0.5f);
        var memory = new BotMemory();
        var rng = new Random(42);

        Prime(memory, target);
        var first = Policy.Decide(self, target, Def, rng, memory);
        Assert.True(first.ActiveSlot > 0);

        var next = Policy.Decide(self, target, Def, rng, memory);

        Assert.Equal(0, next.ActiveSlot);
        Assert.Equal(0f, next.MoveX);
        Assert.Equal(0f, next.MoveY);
    }
    [Fact]
    public void CanonicalSecondaryGroundSlot_IsEligibleWhenOtherSlotsAreLocked()
    {
        var self = Self();
        foreach (var slot in new[]
        {
            AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4,
            AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
        })
            self.SetCooldown(slot, 999);

        var target = Opponent(z: 0.5f);
        bool selected = false;
        for (int seed = 0; seed < 32 && !selected; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(memory, target);
            selected = Policy.Decide(self, target, Def, new Random(seed), memory).ActiveSlot
                == AbilitySlots.Slot2;
        }

        Assert.True(selected, "canonical grounded slot 2 was never considered");
    }

    [Fact]
    public void EmptyAerialSlot_IsUnavailable()
    {
        var def = TestHelpers.WithEmptyAirSlot2(Def);
        var self = TestHelpers.PlayerState();
        self.PY = TestHelpers.GroundPY(def);
        self.IsGrounded = false;
        foreach (var slot in new[]
        {
            AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4,
            AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
        })
            self.SetCooldown(slot, 999);

        var target = TestHelpers.PlayerState(z: 0.5f);
        target.EntityId = 100;
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        Prime(memory, target);

        var input = Policy.Decide(self, target, def, new Random(1), memory);

        Assert.Equal(0, input.ActiveSlot);
    }

    [Fact]
    public void AimedMove_HoldsThenReleasesTheResolvedAimPlan()
    {
        var def = TestHelpers.ResolveDef(CharacterClass.Manki);
        var self = TestHelpers.PlayerState();
        self.PY = TestHelpers.GroundPY(def);
        foreach (var slot in new[]
        {
            AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
            AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
        })
            self.SetCooldown(slot, 999);

        var target = TestHelpers.PlayerState(z: 0.75f);
        target.EntityId = 100;
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        Prime(memory, target);
        var initial = Policy.Decide(self, target, def, new Random(0), memory);

        Assert.Equal(AbilitySlots.A, initial.ActiveSlot);
        Assert.True(initial.IsAiming);

        self.State = ActionState.Aiming;
        self.AttackSlot = AbilitySlots.A;
        var held = Policy.Decide(self, def, new Random(0), memory);
        Assert.True(held.IsAiming);
        Assert.Equal(initial.AimYaw, held.AimYaw);
        Assert.Equal(initial.AimPitch, held.AimPitch);
        Assert.Equal(initial.AimDistance, held.AimDistance);

        InputState released = default;
        for (int i = 0; i < 12; i++)
            released = Policy.Decide(self, def, new Random(0), memory);
        Assert.False(released.IsAiming);
        Assert.Equal(0, released.ActiveSlot);
    }

    [Fact]
    public void ActionPlan_InvalidatesOnInterruptionDeathAndReset()
    {
        var def = TestHelpers.MankiDef;
        var target = Opponent(z: 0.75f);

        var interruptedMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var interruptedSelf = Self();
        LockNonAimSlots(ref interruptedSelf);
        Prime(interruptedMemory, target);
        var initial = Policy.Decide(interruptedSelf, target, def, new Random(0), interruptedMemory);
        Assert.Equal(AbilitySlots.A, initial.ActiveSlot);
        Assert.True(initial.IsAiming);

        interruptedSelf.State = ActionState.Hitstun;
        interruptedSelf.HitstunTicks = 4;
        var interrupted = Policy.Decide(interruptedSelf, def, new Random(0), interruptedMemory);
        Assert.Equal(0, interrupted.ActiveSlot);
        Assert.False(interrupted.IsAiming);

        var landingMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var landingSelf = Self();
        LockNonAimSlots(ref landingSelf);
        Prime(landingMemory, target);
        Assert.True(Policy.Decide(landingSelf, target, def, new Random(0), landingMemory).IsAiming);
        landingSelf.State = ActionState.Aiming;
        landingSelf.AttackSlot = AbilitySlots.A;
        landingSelf.IsGrounded = false;
        Assert.True(Policy.Decide(landingSelf, def, new Random(0), landingMemory).IsAiming);
        landingSelf.IsGrounded = true;
        var afterLanding = Policy.Decide(landingSelf, def, new Random(0), landingMemory);
        Assert.Equal(0, afterLanding.ActiveSlot);
        Assert.False(afterLanding.IsAiming);

        var deadMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var deadSelf = Self();
        LockNonAimSlots(ref deadSelf);
        Prime(deadMemory, target);
        Assert.True(Policy.Decide(deadSelf, target, def, new Random(0), deadMemory).IsAiming);
        deadSelf.Deaths = 1;
        var afterDeath = Policy.Decide(deadSelf, def, new Random(0), deadMemory);
        Assert.Equal(0, afterDeath.ActiveSlot);
        Assert.False(afterDeath.IsAiming);

        var resetMemory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var resetSelf = Self();
        LockNonAimSlots(ref resetSelf);
        Prime(resetMemory, target);
        Assert.True(Policy.Decide(resetSelf, target, def, new Random(0), resetMemory).IsAiming);
        resetMemory.Reset();
        resetSelf.State = ActionState.Aiming;
        resetSelf.AttackSlot = AbilitySlots.A;
        var afterReset = Policy.Decide(resetSelf, def, new Random(0), resetMemory);
        Assert.Equal(0, afterReset.ActiveSlot);
        Assert.False(afterReset.IsAiming);
    }

    [Fact]
    public void AimedPlan_ReachesResolverThroughPolicyAcrossSeeds()
    {
        var def = TestHelpers.MankiDef;
        for (int seed = 0; seed < 8; seed++)
        {
            var baked = TestHelpers.LoadBakedData(def);
            var sim = TestHelpers.MakeSim();
            var self = Self();
            self.PY = TestHelpers.GroundPY(def);
            foreach (var slot in new[]
            {
                AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
                AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
            })
                self.SetCooldown(slot, 999);

            var target = Opponent(z: 2f);
            target.PY = TestHelpers.GroundPY(def);
            sim.RegisterEntity(1, def, self, baked);
            sim.RegisterEntity(100, def, target, baked);

            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            var rng = new Random(seed);
            bool aimed = false;
            bool hit = false;
            for (int tick = 0; tick < 180 && !hit; tick++)
            {
                var currentSelf = sim.GetState(1);
                var currentTarget = sim.GetState(100);
                var input = Policy.Decide(currentSelf, currentTarget, def, rng, memory);
                aimed |= input.IsAiming;
                sim.Tick(new Dictionary<ulong, InputState>
                {
                    [1] = input,
                    [100] = default,
                });
                hit = sim.LastTickHits.Exists(x =>
                    x.OwnerEntityId == 1 && x.TargetEntityId == 100 && x.Damage > 0f);
            }

            Assert.True(aimed, $"seed {seed} never entered the aim plan");
            Assert.True(hit, $"seed {seed} never produced a resolver hit");
        }
    }

    [Fact]
    public void DirectionalPlan_ExecutesMovementThroughSimulationAcrossSeeds()
    {
        var def = TestHelpers.WibouDef;
        for (int seed = 0; seed < 8; seed++)
        {
            var sim = TestHelpers.MakeSim();
            var self = Self();
            self.PY = TestHelpers.GroundPY(def);
            foreach (var slot in new[]
            {
                AbilitySlots.Lmb, AbilitySlots.Rmb, AbilitySlots.Slot1, AbilitySlots.E,
                AbilitySlots.F, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
                AbilitySlots.Slot5, AbilitySlots.A,
            })
                self.SetCooldown(slot, 999);

            var target = Opponent(z: 2f);
            target.PY = TestHelpers.GroundPY(def);
            TestHelpers.RegisterPlayer(sim, def, self);
            TestHelpers.RegisterNpc(sim, def, target);

            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            var rng = new Random(seed);
            bool aimed = false;
            bool moved = false;
            for (int tick = 0; tick < 180 && !moved; tick++)
            {
                var input = Policy.Decide(sim.GetState(1), sim.GetState(100), def, rng, memory);
                aimed |= input.IsAiming;
                sim.Tick(new Dictionary<ulong, InputState>
                {
                    [1] = input,
                    [100] = default,
                });
                moved = sim.GetState(1).PZ > 1f;
            }

            Assert.True(aimed, $"seed {seed} never entered the directional plan");
            Assert.True(moved, $"seed {seed} never moved through the directional dash");
        }
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void StageRecovery_ReturnsToAStageSurface(CharacterClass character)
    {
        var arena = RecoveryArena();
        var def = TestHelpers.ResolveDef(character);
        for (int seed = 0; seed < 4; seed++)
        {
            var state = TestHelpers.PlayerState(x: 8f);
            state.PY = 4f;
            state.VY = -3f;
            state.IsGrounded = false;
            state.AirTimeTicks = def.Movement.FloatWindowTicks;
            state.JumpsLeft = def.Movement.MaxJumps;
            var sim = TestHelpers.MakeSim(arena);
            sim.RegisterEntity(1, def, state);
            var target = TestHelpers.NpcState(x: 20f);
            target.PY = TestHelpers.GroundPY(def);
            sim.RegisterEntity(100, def, target);
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            var rng = new Random(seed);
            bool returned = false;

            for (int tick = 0; tick < 180; tick++)
            {
                var input = Policy.Decide(sim.GetState(1), sim.GetState(100), def,
                    rng, memory, arena);
                sim.Tick(new Dictionary<ulong, InputState>
                {
                    [1] = input,
                    [100] = default,
                });
                var current = sim.GetState(1);
                if (current.IsGrounded && current.PX >= arena.MinX
                    && current.PX <= arena.MaxX && current.PZ >= arena.MinZ
                    && current.PZ <= arena.MaxZ)
                {
                    returned = true;
                    break;
                }
                Assert.True(current.PY > arena.KillHeight,
                    $"{character} seed {seed} crossed blast height at tick {tick}");
            }

            Assert.True(returned, $"{character} seed {seed} did not return to stage");
        }
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void RecoveryMove_IsReservedWhenOffstage(CharacterClass character)
    {
        var arena = RecoveryArena();
        var def = TestHelpers.ResolveDef(character);
        var self = TestHelpers.PlayerState(x: 10f);
        self.PY = 4f;
        self.VY = -5f;
        self.IsGrounded = false;
        self.JumpsLeft = 0;
        self.DashCooldownTicks = 999;
        // Keep the opponent toward the stage: homing recovery must not chase outward.
        var target = TestHelpers.NpcState(x: 4f);
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        int delay = BotDifficultyProfile.ForDifficulty(memory.Difficulty).ReactionDelayTicks;
        for (int i = 0; i <= delay; i++)
            memory.ObserveOpponent(target);

        var input = Policy.Decide(self, target, def, new Random(0), memory, arena);

        Assert.Equal(AbilitySlots.E, input.ActiveSlot);
    }

    [Fact]
    public void HomingRecovery_DoesNotChaseOpponentAwayFromStage()
    {
        var self = TestHelpers.PlayerState(x: 10f);
        self.PY = 4f;
        self.VY = -5f;
        self.IsGrounded = false;
        self.JumpsLeft = 0;
        self.DashCooldownTicks = 999;
        var target = TestHelpers.NpcState(x: 14f); // Outward opponent must not redirect recovery.
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        Prime(memory, target);

        var input = Policy.Decide(self, target, TestHelpers.WibouDef,
            new Random(0), memory, RecoveryArena());

        Assert.Equal(AbilitySlots.E, input.ActiveSlot);
        Assert.True(input.MoveX < 0f, "Recovery must drift toward the stage, not the opponent.");
        Assert.True(MathF.Sin(input.AimYaw * MathF.PI / 18000f) < 0f,
            "Recovery aim must point toward the stage, not the outward opponent.");
    }

    [Fact]
    public void RecoveryRejectsHeightmapSurfaceBehindBlockingTriangle()
    {
        var arena = BlockedRecoveryArena();
        var def = TestHelpers.FightGuyDef;
        var self = TestHelpers.PlayerState(x: 5.5f);
        self.PY = 4f;
        self.VY = -5f;
        self.IsGrounded = false;
        self.JumpsLeft = 0;
        self.DashCooldownTicks = 999;
        var target = TestHelpers.NpcState(x: 20f);
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };

        var input = Policy.Decide(self, target, def, new Random(0), memory, arena);

        Assert.Equal(0, input.ActiveSlot);
        Assert.True(input.MoveX < -0.5f, "blocked recovery target did not fall back toward stage");
    }

    [Fact]
    public void OffstageBesideEdge_RecoversInsteadOfWaitingForDisabledGrab()
    {
        var arena = RecoveryArena();
        arena.MaxX = 8f;
        var def = TestHelpers.FightGuyDef;
        var self = TestHelpers.PlayerState(x: 7.2f);
        self.PY = TestHelpers.GroundPY(def);
        self.VY = -1f;
        self.IsGrounded = false;
        self.JumpsLeft = 0;
        var target = TestHelpers.NpcState(x: 20f);
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };

        var input = Policy.Decide(self, target, def, new Random(0), memory, arena);

        Assert.True(input.MoveX < -0.5f, "bot must steer back onto the stage");
        Assert.Equal(AbilitySlots.E, input.ActiveSlot);
    }

    [Fact]
    public void StageSideEdgeguard_DoesNotPursueOffstageTarget()
    {
        var arena = RecoveryArena();
        var def = TestHelpers.FightGuyDef;
        var self = TestHelpers.PlayerState(x: 5f);
        self.PY = TestHelpers.GroundPY(def);
        var target = TestHelpers.NpcState(x: 10f);
        target.PY = 2f;
        target.IsGrounded = false;
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        int delay = BotDifficultyProfile.ForDifficulty(memory.Difficulty).ReactionDelayTicks;
        for (int i = 0; i <= delay; i++)
            memory.ObserveOpponent(target);

        for (int seed = 0; seed < 8; seed++)
        {
            var input = Policy.Decide(self, target, def, new Random(seed), memory, arena);
            Assert.False(input.Jump);
            Assert.True(input.MoveX <= 0f,
                $"seed {seed} pursued offstage target with MoveX={input.MoveX}");
        }
    }
    private static ArenaDefinition LungeArena()
        => new()
        {
            MinX = -30f, MaxX = 30f, MinZ = -30f, MaxZ = 30f,
            KillHeight = -10f,
            SpawnPoints = new[] { new SpawnPoint() },
            Heightmap = new ArenaHeightmap
            {
                Data = new float[60 * 60], Width = 60, Height = 60,
                CellSize = 1f, OriginX = -30f, OriginZ = -30f,
            },
        };

    private static CharacterState OnlyCyclone(float z, float yaw)
    {
        var self = Self(z: z);
        self.FacingYaw = yaw;
        foreach (var slot in new[]
        {
            AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
            AbilitySlots.A, AbilitySlots.E, AbilitySlots.F,
        })
            self.SetCooldown(slot, 999);
        return self;
    }

    [Fact]
    public void CommittedLunge_RejectsOffstageTravel_ButKeepsInwardAttackAvailable()
    {
        var arena = LungeArena();
        var policy = new HeuristicBotPolicy();
        int inwardAttacks = 0;
        for (int seed = 0; seed < 32; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            var target = Opponent(z: 28f);
            Prime(memory, target);
            var outward = policy.Decide(OnlyCyclone(25f, 0f), target,
                Def, new Random(seed), memory, arena);
            Assert.NotEqual(AbilitySlots.R, outward.ActiveSlot);

            memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            target = Opponent(z: 22f);
            Prime(memory, target);
            var inward = policy.Decide(OnlyCyclone(25f, MathF.PI), target,
                Def, new Random(seed), memory, arena);
            if (inward.ActiveSlot == AbilitySlots.R) inwardAttacks++;
        }
        Assert.True(inwardAttacks > 0, "Safe inward Cyclone must remain usable.");
    }

    [Fact]
    public void CommittedLunge_UsesCapabilityDurationRatherThanRecoveryTimeline()
    {
        var arena = LungeArena();
        var target = Opponent(z: 18f);
        var policy = new HeuristicBotPolicy();
        // Explicit fixture: 11.33 units of propulsion fits; 19.83 units would cross the edge.
        var def = TestHelpers.FightGuyDef;
        var slots = System.Linq.Enumerable.ToArray(def.CookedSlots!);
        slots[6] = new CookedSlotDefinition(6, "ground.R", false, "Cyclone", "Cyclone", "icon.r",
                AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
                new CookedTimeline(new[]
                {
                    new CookedStage(70, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
                    {
                        new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                            "slop.internal.fightguy.cyclone-kick.v1", "1",
                            new CookedCycloneKickCapabilityParameters(17, 6, 34, 40, .8f, .4f, .8f, 7, 15, 8, 5, 6, .8f, .3f)),
                    }),
                }));
        def.CookedSlots = slots;
        int attacks = 0;
        for (int seed = 0; seed < 32; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(memory, target);
            var input = policy.Decide(OnlyCyclone(15f, 0f), target,
                def, new Random(seed), memory, arena);
            if (input.ActiveSlot == AbilitySlots.R) attacks++;
        }
        Assert.True(attacks > 0, "Safe bounded Cyclone must not be rejected for stationary recovery time.");
    }

    [Fact]
    public void ConfirmedHitFollowUp_RejectsOffstageTravel()
    {
        var arena = LungeArena();
        var target = Opponent(z: 28f);
        target.State = ActionState.Hitstun;
        target.HitstunTicks = 60;
        var policy = new HeuristicBotPolicy();
        for (int seed = 0; seed < 32; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(memory, target);
            memory.RecordOpponentHit(AbilitySlots.A, target, 60, 0);
            Prime(memory, target);
            var input = policy.Decide(OnlyCyclone(25f, 0f), target,
                Def, new Random(seed), memory, arena);
            Assert.NotEqual(AbilitySlots.R, input.ActiveSlot);
        }
    }

    [Fact]
    public void CommittedLunge_RejectsGapEvenWhenEndpointHasSupport()
    {
        var arena = LungeArena();
        for (int z = 34; z < 37; z++)
        for (int x = 0; x < 60; x++)
            arena.Heightmap.Data[z * 60 + x] = float.MinValue;
        var target = Opponent(z: 3f);
        var policy = new HeuristicBotPolicy();
        for (int seed = 0; seed < 32; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(memory, target);
            var input = policy.Decide(OnlyCyclone(0f, 0f), target,
                Def, new Random(seed), memory, arena);
            Assert.NotEqual(AbilitySlots.R, input.ActiveSlot);
        }
    }

    [Fact]
    public void AerialAttack_RejectsOutwardMomentumDuringLock_ButAllowsInwardMomentum()
    {
        var arena = LungeArena();
        var def = TestHelpers.EngineDef;
        var slots = System.Linq.Enumerable.ToArray(def.CookedSlots!);
        slots[12] = new CookedSlotDefinition(12, "air.A", true, "Momentum projectile", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, true,
            new CookedTimeline(new[]
            {
                new CookedStage(45, 0, 0, 0, 0, Array.Empty<string>(),
                    new CookedTimelineOperation[]
                    {
                        new CookedSpawnProjectileOperation(8, AuthoringUnit.Ticks,
                            new CookedProjectile(0, 0, 0, 20, 0, .5f, 6, 30, 3, 5, 12, 60)),
                    }),
            }));
        def.CookedSlots = slots;
        var self = TestHelpers.PlayerState(z: 25f) with { IsGrounded = false, PY = 3f };
        var target = Opponent(z: 28f);
        var policy = new HeuristicBotPolicy();
        int safeShots = 0;
        for (int seed = 0; seed < 32; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(memory, target);
            self.VZ = 17f;
            var outward = policy.Decide(self, target, def, new Random(seed), memory, arena);
            Assert.NotEqual(AbilitySlots.A, outward.ActiveSlot);

            memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(memory, target);
            self.VZ = -17f;
            var inward = policy.Decide(self, target, def, new Random(seed), memory, arena);
            if (inward.ActiveSlot == AbilitySlots.A) safeShots++;
        }
        Assert.True(safeShots > 0, "Safe inherited momentum must not disable aerial attacks.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KiShot_BotChargesReleasesAndFiresOnGroundAndInAir(bool airborne)
    {
        var def = TestHelpers.FightGuyDef;
        var self = Self(20f, 20f);
        LockNonAimSlots(ref self);
        self.IsGrounded = !airborne;
        if (airborne) self.PY += 5f;
        var target = Opponent(20f, 25f);
        var policy = new HeuristicBotPolicy();
        BotMemory? selectedMemory = null;
        InputState initial = default;
        for (int seed = 0; seed < 32; seed++)
        {
            var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
            Prime(memory, target);
            initial = policy.Decide(self, target, def, new Random(seed), memory);
            if (initial.ActiveSlot != AbilitySlots.A) continue;
            selectedMemory = memory;
            break;
        }
        Assert.NotNull(selectedMemory);
        Assert.True(initial.IsAiming);
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, self);
        var held = TestHelpers.TickN(sim, initial, 1);
        Assert.Equal(ActionState.Aiming, held.State);
        bool released = false;
        for (int tick = 0; tick < 90 && sim.Resolver.GetActiveHitboxes().Count == 0; tick++)
        {
            var input = policy.Decide(sim.GetState(1), target, def, new Random(0), selectedMemory!);
            var state = TestHelpers.TickN(sim, input, 1);
            released |= state.State == ActionState.Attacking && !state.IsAiming;
        }
        Assert.True(released);
        Assert.Single(sim.Resolver.GetActiveHitboxes());
    }

}
