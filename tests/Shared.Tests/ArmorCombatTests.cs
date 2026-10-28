using System.Collections.Generic;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ArmorCombatTests
{
    private const ulong AttackerId = 1;
    private const ulong DefenderId = 100;

    [Fact]
    public void ArmorRetainsDamageAndHitstopWithoutApplyingReactionOrSdi()
    {
        var def = TestHelpers.EngineDef;
        var sim = CreateSimulation(def, out var defender);
        defender.IsGrounded = false;
        defender.PY += 10f;
        defender.AirTimeTicks = 100;
        defender.FacingYaw = 0.7f;
        defender.IsFastFalling = true;
        defender.JumpTakeoff = JumpTakeoff.Slide;
        defender.KVX = 1.25f;
        defender.KVY = 0.5f;
        defender.KVZ = -0.75f;
        sim.SetState(DefenderId, defender);
        var control = CreateSimulation(def, out _);
        control.SetState(DefenderId, defender);
        control.ActivateAbility(DefenderId, new ArmorAbility(8), 2, def);
        var armor = new ArmorAbility(8);
        sim.ActivateAbility(DefenderId, armor, 2, def);
        SpawnHit(sim, defender, fixedHitstunTicks: 24);

        sim.Tick(new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default });
        control.Tick(new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default });
        var uninterrupted = control.GetState(DefenderId);

        var hit = Assert.Single(sim.LastTickHits);
        Assert.True(hit.ArmorProtected);
        Assert.Equal(5f, hit.Damage);
        Assert.Equal(0f, hit.ImpactForce);
        Assert.True(hit.HitstopTicks > 0);
        var afterHit = sim.GetState(DefenderId);
        Assert.Equal((ushort)5, afterHit.DamagePercent);
        Assert.Equal(hit.HitstopTicks, afterHit.HitstopTicks);
        Assert.Equal(uninterrupted.KVX, afterHit.KVX);
        Assert.Equal(uninterrupted.KVY, afterHit.KVY);
        Assert.Equal(uninterrupted.KVZ, afterHit.KVZ);
        Assert.Equal(hit.HitstopTicks, sim.GetState(AttackerId).HitstopTicks);
        Assert.Equal((ushort)0, afterHit.HitstunTicks);
        Assert.Equal(ActionState.Attacking, afterHit.State);
        Assert.Equal(uninterrupted.FacingYaw, afterHit.FacingYaw);
        Assert.True(afterHit.IsFastFalling);
        Assert.Equal(JumpTakeoff.Slide, afterHit.JumpTakeoff);
        Assert.Equal((ushort)0, afterHit.QueuedKBFixedHitstunTicks);
        Assert.True(armor.HasArmor);

        var startX = afterHit.PX;
        for (var tick = 0; tick < hit.HitstopTicks; tick++)
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [AttackerId] = default,
                [DefenderId] = new InputState { MoveX = 1f },
            });

        var afterFreeze = sim.GetState(DefenderId);
        Assert.Equal(startX, afterFreeze.PX);
        Assert.Equal((ushort)0, afterFreeze.HitstunTicks);
        Assert.Equal(ActionState.Attacking, afterFreeze.State);
        Assert.Equal(afterHit.FacingYaw, afterFreeze.FacingYaw);
        Assert.True(afterFreeze.IsFastFalling);
        Assert.Equal(JumpTakeoff.Slide, afterFreeze.JumpTakeoff);
        Assert.Equal(afterHit.KVX, afterFreeze.KVX);
        Assert.Equal(afterHit.KVY, afterFreeze.KVY);
        Assert.Equal(afterHit.KVZ, afterFreeze.KVZ);
        Assert.True(armor.HasArmor);
        Assert.False(afterFreeze.SdiApplied);
    }

    [Fact]
    public void CookedTimelineStartsAndExpiresArmorOnAuthoredTicks()
    {
        var def = TestHelpers.EngineDef;
        var slot = new CookedSlotDefinition(
            0, "ground.1", false, "Armor timeline", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(8, 0, 0, 0, 0, System.Array.Empty<string>(),
                    new CookedTimelineOperation[]
                    {
                        new CookedArmorWindowOperation(2, AuthoringUnit.Ticks, 3),
                    }),
            }));
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(DefenderId, def, state);
        var ability = new CookedTimelineAbility(slot);
        sim.ActivateAbility(DefenderId, ability, (byte)(AbilitySlots.Slot1 - 1), def);
        Assert.False(ability.HasArmor);

        var inputs = new Dictionary<ulong, InputState> { [DefenderId] = default };
        sim.Tick(inputs);
        Assert.False(ability.HasArmor);
        sim.Tick(inputs);
        Assert.True(ability.HasArmor);
        sim.Tick(inputs);
        Assert.True(ability.HasArmor);
        sim.Tick(inputs);
        Assert.True(ability.HasArmor);
        sim.Tick(inputs);
        Assert.False(ability.HasArmor);
    }

    [Theory]
    [InlineData(20, 24, 24)]
    [InlineData(0, 24, 0)]
    [InlineData(20, 300, 240)]
    public void FixedHitstunAppliesAfterHitstopOnlyWithStunGate(
        ushort stunGate, ushort fixedHitstunTicks, ushort expectedHitstun)
    {
        var def = TestHelpers.EngineDef;
        var sim = CreateSimulation(def, out var defender);
        SpawnHit(sim, defender, stunGate, fixedHitstunTicks);
        var inputs = new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default };

        sim.Tick(inputs);
        var hit = Assert.Single(sim.LastTickHits);
        Assert.True(hit.HitstopTicks > 0);
        Assert.Equal((ushort)0, sim.GetState(DefenderId).HitstunTicks);
        while (sim.GetState(DefenderId).HitstopTicks > 0)
            sim.Tick(inputs);

        Assert.Equal(expectedHitstun, sim.GetState(DefenderId).HitstunTicks);
    }

    [Fact]
    public void ZeroFixedHitstunRetainsTheFormulaDerivedDefault()
    {
        var def = TestHelpers.EngineDef;
        var sim = CreateSimulation(def, out var defender);
        SpawnHit(sim, defender);
        var inputs = new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default };

        sim.Tick(inputs);
        var hit = Assert.Single(sim.LastTickHits);
        Assert.True(hit.HitstopTicks > 0);
        while (sim.GetState(DefenderId).HitstopTicks > 0)
            sim.Tick(inputs);

        var expected = (ushort)(CombatTuning.Default.HitstunCoefficient
            * (hit.ImpactForce / CombatTuning.Default.KnockbackScale + CombatTuning.Default.HitstunMagnitudeBonus));
        Assert.Equal(expected, sim.GetState(DefenderId).HitstunTicks);
    }

    [Fact]
    public void NaturalAbilityEndClearsArmorBeforeAFollowingContact()
    {
        var def = TestHelpers.EngineDef;
        var sim = CreateSimulation(def, out var defender);
        var armor = new ArmorAbility(8, endOnSecondTick: true);
        sim.ActivateAbility(DefenderId, armor, 2, def);
        sim.Tick(new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default });
        Assert.True(armor.HasArmor);
        sim.Tick(new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default });
        Assert.False(armor.HasArmor);
        Assert.Null(sim.GetActiveAbility(DefenderId));

        SpawnHit(sim, sim.GetState(DefenderId));
        sim.Tick(new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default });
        Assert.False(Assert.Single(sim.LastTickHits).ArmorProtected);
        Assert.True(sim.GetState(DefenderId).QueuedKBStun > 0);
    }

    [Fact]
    public void ExpiredArmorDoesNotProtectAHit()
    {
        var def = TestHelpers.EngineDef;
        var sim = CreateSimulation(def, out var defender);
        var armor = new ArmorAbility(1);
        sim.ActivateAbility(DefenderId, armor, 2, def);
        sim.Tick(new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default });
        Assert.True(armor.HasArmor);
        sim.Tick(new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default });
        Assert.False(armor.HasArmor);

        SpawnHit(sim, sim.GetState(DefenderId));
        sim.Tick(new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default });
        Assert.False(Assert.Single(sim.LastTickHits).ArmorProtected);
        Assert.True(sim.GetState(DefenderId).QueuedKBStun > 0);
    }

    [Fact]
    public void CancellationDropsTheArmorWindowBeforeTheNextContact()
    {
        var def = TestHelpers.EngineDef;
        var sim = CreateSimulation(def, out var defender);
        var armor = new ArmorAbility(8);
        sim.ActivateAbility(DefenderId, armor, 2, def);
        sim.Tick(new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default });
        Assert.True(armor.HasArmor);

        var cancelled = sim.GetState(DefenderId);
        cancelled.State = ActionState.Idle;
        sim.SetState(DefenderId, cancelled);
        SpawnHit(sim, cancelled);
        sim.Tick(new Dictionary<ulong, InputState> { [AttackerId] = default, [DefenderId] = default });

        Assert.False(armor.HasArmor);
        Assert.False(Assert.Single(sim.LastTickHits).ArmorProtected);
        Assert.True(sim.GetState(DefenderId).QueuedKBStun > 0);
    }

    [Fact]
    public void GrabCapturesAnArmoredOpponentAndCancelsItsProtection()
    {
        var def = TestHelpers.EngineDef;
        def = def with { CaptureGeometry = new CookedCaptureGeometry(1.1f, 0.8f, 1.2f, 0.6f,
            new CaptureAnchor(0f, 0.6f, 0.2f), new CaptureAnchor(0f, 0.6f, 0.65f)) };
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var grabber = TestHelpers.PlayerState(100f, 100f);
        grabber.PY = TestHelpers.GroundPY(def);
        var defender = TestHelpers.NpcState(100f, 100.7f);
        defender.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(AttackerId, def, grabber);
        sim.RegisterEntity(DefenderId, def, defender);
        var armor = new ArmorAbility(32);
        sim.ActivateAbility(DefenderId, armor, 2, def);
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.True(armor.HasArmor);

        sim.Tick(new Dictionary<ulong, InputState> { [AttackerId] = new() { GrabPressed = true } });
        for (int tick = 0; tick < DefenseConfig.GrabStartupTicks; tick++)
            sim.Tick(new Dictionary<ulong, InputState>());

        Assert.Equal(ActionState.Grabbed, sim.GetState(DefenderId).State);
        Assert.Equal(ActionState.Grabbing, sim.GetState(AttackerId).State);
        Assert.Equal(sim.GetState(AttackerId).InteractionId, sim.GetState(DefenderId).InteractionId);
        Assert.False(armor.HasArmor);
        Assert.Null(sim.GetActiveAbility(DefenderId));
    }

    [Fact]
    public void ReplacementDoesNotInheritThePriorActivationsArmor()
    {
        var def = TestHelpers.EngineDef;
        var sim = CreateSimulation(def, out _);
        var armor = new ArmorAbility(32);
        sim.ActivateAbility(DefenderId, armor, 2, def);
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.True(armor.HasArmor);
        sim.ActivateAbility(DefenderId, new ArmorAbility(0), 3, def);
        SpawnHit(sim, sim.GetState(DefenderId));
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.False(Assert.Single(sim.LastTickHits).ArmorProtected);
        while (sim.GetState(DefenderId).HitstopTicks > 0)
            sim.Tick(new Dictionary<ulong, InputState>());
        Assert.Equal(ActionState.Hitstun, sim.GetState(DefenderId).State);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(3, 3, true)]
    [InlineData(3, 4, false)]
    public void TickZeroArmorProtectsItsFullAuthoredContactBudget(
        ushort duration, int contactStep, bool protectedContact)
    {
        var def = TestHelpers.EngineDef;
        var sim = CreateSimulation(def, out _);
        var slot = new CookedSlotDefinition(
            0, "ground.1", false, "Armor startup", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(8, 0, 0, 0, 0, System.Array.Empty<string>(),
                    new CookedTimelineOperation[]
                    {
                        new CookedArmorWindowOperation(0, AuthoringUnit.Ticks, duration),
                    }),
            }));
        sim.ActivateAbility(DefenderId, new CookedTimelineAbility(slot),
            (byte)(AbilitySlots.Slot1 - 1), def);
        for (int step = 1; step < contactStep; step++)
            sim.Tick(new Dictionary<ulong, InputState>());
        SpawnHit(sim, sim.GetState(DefenderId));
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.Equal(protectedContact, Assert.Single(sim.LastTickHits).ArmorProtected);
    }

    [Fact]
    public void FinalHitstopStepDoesNotSpendTheLastArmoredContactTick()
    {
        var def = TestHelpers.EngineDef;
        var sim = CreateSimulation(def, out var defender);
        var armor = new ArmorAbility(1);
        sim.ActivateAbility(DefenderId, armor, 2, def);
        SpawnHit(sim, defender);
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.True(Assert.Single(sim.LastTickHits).ArmorProtected);
        while (sim.GetState(DefenderId).HitstopTicks > 1)
            sim.Tick(new Dictionary<ulong, InputState>());
        SpawnHit(sim, sim.GetState(DefenderId));
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.True(Assert.Single(sim.LastTickHits).ArmorProtected);
        Assert.Equal(ActionState.Attacking, sim.GetState(DefenderId).State);

        while (sim.GetState(DefenderId).HitstopTicks > 0)
            sim.Tick(new Dictionary<ulong, InputState>());
        SpawnHit(sim, sim.GetState(DefenderId));
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.False(Assert.Single(sim.LastTickHits).ArmorProtected);
    }

    [Fact]
    public void LateActivationCountsTheCollisionFrameBeforeItsFirstAbilityTick()
    {
        var def = TestHelpers.EngineDef;
        var sim = CreateSimulation(def, out var defender);
        var slot = new CookedSlotDefinition(
            0, "ground.1", false, "Late armor", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(8, 0, 0, 0, 0, System.Array.Empty<string>(),
                    new CookedTimelineOperation[]
                    {
                        new CookedArmorWindowOperation(0, AuthoringUnit.Ticks, 1),
                    }),
            }));
        // Resolver callbacks occur after ability dispatch but before accepted-hit reactions.
        bool activated = false;
        sim.Resolver.OnHitboxRemoved = (_, _, _, _) =>
        {
            if (activated) return;
            activated = true;
            sim.ActivateAbility(DefenderId,
                new CookedTimelineAbility(slot),
                (byte)(AbilitySlots.Slot1 - 1), def);
        };
        SpawnHit(sim, defender);
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.True(Assert.Single(sim.LastTickHits).ArmorProtected);
        sim.Resolver.OnHitboxRemoved = null;
        while (sim.GetState(DefenderId).HitstopTicks > 0)
            sim.Tick(new Dictionary<ulong, InputState>());
        SpawnHit(sim, sim.GetState(DefenderId));
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.False(Assert.Single(sim.LastTickHits).ArmorProtected);
    }

    private static ServerSimulation CreateSimulation(CharacterDefinition def, out CharacterState defender)
    {
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var attacker = TestHelpers.PlayerState(-2f);
        attacker.PY = TestHelpers.GroundPY(def);
        defender = TestHelpers.PlayerState();
        defender.EntityId = DefenderId;
        defender.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(AttackerId, def, attacker);
        sim.RegisterEntity(DefenderId, def, defender);
        return sim;
    }

    private static void SpawnHit(
        ServerSimulation sim,
        CharacterState defender,
        ushort stunTicks = 20,
        ushort fixedHitstunTicks = 0)
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
            KnockbackAngle = 0,
            StunTicks = stunTicks,
            FixedHitstunTicks = fixedHitstunTicks,
            DurationTicks = 1,
            OwnerId = AttackerId,
            AttackSlot = 1,
            FreezesOwner = true,
        });
    }

    private sealed class ArmorAbility : ServerAbility
    {
        private readonly ushort _durationTicks;

        private readonly bool _endOnSecondTick;
        private bool _started;

        public ArmorAbility(ushort durationTicks, bool endOnSecondTick = false)
        {
            _durationTicks = durationTicks;
            _endOnSecondTick = endOnSecondTick;
        }

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
            => s.State = ActionState.Attacking;

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            if (_started)
            {
                if (_endOnSecondTick)
                    EndAbility(ref s);
                return;
            }
            _started = true;
            StartArmorWindow(_durationTicks);
        }
    }
}
