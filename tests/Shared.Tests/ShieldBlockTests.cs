using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ShieldBlockTests
{
    private const ulong AttackerId = 1;
    private const ulong DefenderId = 100;

    private static ServerSimulation CreateSimulation(
        out CharacterDefinition attackerDef,
        CharacterDefinition? defenderDef = null,
        ArenaDefinition? arena = null,
        float defenderX = 0f,
        float attackerX = -2f)
    {
        var sim = new ServerSimulation(arena ?? TestHelpers.TestArena());
        attackerDef = TestHelpers.EngineDef;
        defenderDef ??= attackerDef;

        var attacker = TestHelpers.PlayerState(attackerX);
        attacker.PY = attackerDef.CapsuleHeight * 0.5f;
        var defender = TestHelpers.PlayerState(defenderX);
        defender.EntityId = DefenderId;
        defender.PY = attackerDef.CapsuleHeight * 0.5f;
        sim.RegisterEntity(AttackerId, attackerDef, attacker);
        sim.RegisterEntity(DefenderId, defenderDef, defender);
        return sim;
    }

    private static ServerSimulation ManualBubbleSimulation(out CharacterDefinition def)
    {
        def = new CharacterDefinition
        {
            CapsuleRadius = 0.3f,
            CapsuleHeight = 1.5f,
            ShieldRadius = 1.05f,
            HurtboxCapsules = new[]
            {
                new HurtboxCapsule(0f, -0.65f, 0f, 0f, 0.65f, 0f, 0.3f),
            },
            Movement = new MovementStats { Gravity = 20f, GroundFriction = 8f, MaxFallSpeed = 20f },
        };
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var attacker = TestHelpers.PlayerState(-2f);
        attacker.PY = 0.75f;
        var defender = TestHelpers.PlayerState();
        defender.EntityId = DefenderId;
        defender.PY = 0.75f;
        sim.RegisterEntity(AttackerId, def, attacker);
        sim.RegisterEntity(DefenderId, def, defender);
        return sim;
    }

    private static Hitbox Contact(
        in CharacterState defender,
        float damage = 5f,
        ulong activationId = 17,
        float xOffset = 0f,
        float yOffset = 0f,
        float zOffset = 0f,
        bool freezesOwner = false,
        bool multipleOpponents = false,
        ushort duration = 20) => new()
    {
        X = defender.PX + xOffset,
        Y = defender.PY + yOffset,
        Z = defender.PZ + zOffset,
        EndX = defender.PX + xOffset,
        EndY = defender.PY + yOffset,
        EndZ = defender.PZ + zOffset,
        Radius = 0.45f,
        Shape = HitboxShape.Sphere,
        Damage = damage,
        BaseKnockback = 2f,
        KnockbackGrowth = 3f,
        KnockbackAngle = 0,
        StunTicks = 20,
        DurationTicks = duration,
        OwnerId = AttackerId,
        ActivationId = activationId,
        AttackSlot = 1,
        FreezesOwner = freezesOwner,
        HitsMultipleOpponents = multipleOpponents,
    };

    private static Dictionary<ulong, InputState> ShieldInput() => new()
    {
        [DefenderId] = new InputState { ShieldHeld = true },
    };

    [Fact]
    public void ShieldEntryTickBlocksBeforeDamageHooksAndFreezesMeleeOwner()
    {
        var sim = CreateSimulation(out var def);
        var ownerState = sim.GetState(AttackerId);
        ownerState.ChargeStockSpent = 1;
        ownerState.ChargeStockRegenTicks = 100;
        sim.SetState(AttackerId, ownerState);
        var hook = new MutatingHitHook();
        sim.ActivateAbility(AttackerId, hook, 0, def);

        var defender = sim.GetState(DefenderId);
        defender.DamagePercent = 437;
        sim.SetState(DefenderId, defender);
        var meleeContact = Contact(
            in defender, damage: 5f, activationId: sim.GetLastActivationId(AttackerId),
            freezesOwner: true, multipleOpponents: true);
        meleeContact.AttackSequence = hook.PresentationAttackSequence;
        sim.Resolver.Spawn(meleeContact);
        sim.Tick(ShieldInput());

        defender = sim.GetState(DefenderId);
        ownerState = sim.GetState(AttackerId);
        var blocked = Assert.Single(sim.LastTickHits);
        Assert.True(blocked.Blocked);
        Assert.Equal(0f, blocked.Damage);
        Assert.Equal(0f, blocked.ImpactForce);
        Assert.Equal((uint)1, blocked.MatchTick);
        Assert.Equal(new SpellResolver.BlockEventIdentity(1, AttackerId, sim.GetLastActivationId(AttackerId), DefenderId),
            blocked.BlockEventIdentity);
        Assert.Equal((ushort)5, defender.BlockStunTicks);
        Assert.Equal((ushort)437, defender.DamagePercent);
        Assert.Equal(0, hook.HitCalls);
        Assert.Equal((byte)1, ownerState.ChargeStockSpent);
        Assert.Equal(ActionState.Shielding, defender.State);
        Assert.Equal((byte)DefenseBlockHitstopKind.ShieldContact, defender.BlockHitstopKind);
        Assert.Equal(ServerSimulation.ComputeHitstopTicks(5f, def.GetSlotAbility(2, false)), blocked.HitstopTicks);
        Assert.Equal(blocked.HitstopTicks, defender.HitstopTicks);
        Assert.Equal(blocked.HitstopTicks, ownerState.HitstopTicks);
        Assert.Equal((ushort)0, defender.QueuedKBStun);
        Assert.False(defender.SdiApplied);
        Assert.InRange(defender.PX, 0.099f, 0.101f);

        var presentation = Assert.Single(sim.GetPresentationEvents(clear: true));
        Assert.Equal(PresentationEventSource.BlockContact, presentation.Source);
        Assert.Equal("combat.block", presentation.PresentationId);
        Assert.Equal(DefenderId, presentation.EntityId);
        Assert.Equal(blocked.MatchTick, presentation.MatchTick);
        Assert.Equal(blocked.AttackSequence, presentation.AttackSequence);
        Assert.Equal(17, presentation.OperationIndex);
    }

    [Theory]
    [InlineData(0f, 0f, 0.6f)]
    [InlineData(0f, 0f, -0.6f)]
    [InlineData(0.6f, 0f, 0f)]
    [InlineData(-0.6f, 0f, 0f)]
    [InlineData(0f, 0.85f, 0f)]
    public void ShieldCoversFrontBackSidesAndAbove(float x, float y, float z)
    {
        var sim = CreateSimulation(out _);
        var defender = sim.GetState(DefenderId);
        defender.FacingYaw = 0f;
        sim.Resolver.Spawn(Contact(in defender, xOffset: x, yOffset: y, zOffset: z));

        sim.Tick(ShieldInput());

        Assert.Equal(ActionState.Shielding, sim.GetState(DefenderId).State);
        Assert.True(Assert.Single(sim.LastTickHits).Blocked);
    }

    [Theory]
    [InlineData(0.9f, 0f, 0f)]
    [InlineData(-0.9f, 0f, 0f)]
    [InlineData(0f, 0f, 0.9f)]
    [InlineData(0f, 0f, -0.9f)]
    [InlineData(0f, 1.02f, 0f)]
    public void ShieldBubbleBlocksContactOutsideOrdinaryHurtboxes(float x, float y, float z)
    {
        var sim = ManualBubbleSimulation(out var def);
        var defender = sim.GetState(DefenderId);
        var hitbox = Contact(in defender, xOffset: x, yOffset: y, zOffset: z);
        hitbox.Radius = 0.02f;
        sim.Resolver.Spawn(hitbox);

        sim.Tick(ShieldInput());

        var blocked = Assert.Single(sim.LastTickHits);
        Assert.True(blocked.Blocked);
        Assert.Equal((ushort)0, sim.GetState(DefenderId).DamagePercent);
        float dx = blocked.HitX - defender.PX;
        float dy = blocked.HitY - defender.PY;
        float dz = blocked.HitZ - defender.PZ;
        float radius = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        Assert.InRange(radius, def.ShieldRadius - 0.001f, def.ShieldRadius + 0.001f);
        float incomingLength = MathF.Sqrt(x * x + y * y + z * z);
        Assert.Equal(defender.PX + x / incomingLength * def.ShieldRadius, blocked.HitX, 3);
        Assert.Equal(defender.PY + y / incomingLength * def.ShieldRadius, blocked.HitY, 3);
        Assert.Equal(defender.PZ + z / incomingLength * def.ShieldRadius, blocked.HitZ, 3);
        var ripple = Assert.Single(sim.GetPresentationEvents(clear: true));
        Assert.Equal(blocked.HitX, ripple.WorldX, 3);
        Assert.Equal(blocked.HitY, ripple.WorldY, 3);
        Assert.Equal(blocked.HitZ, ripple.WorldZ, 3);
    }

    [Fact]
    public void BlockEventRetainsFirstShieldImpactBeforePushback()
    {
        var sim = ManualBubbleSimulation(out var def);
        var defender = sim.GetState(DefenderId);
        var projectile = Contact(in defender, damage: 3f, xOffset: -2f);
        projectile.Radius = 0.1f;
        projectile.VX = 120f;
        sim.Resolver.Spawn(projectile);

        sim.Tick(ShieldInput());

        var hit = Assert.Single(sim.LastTickHits);
        var presentation = Assert.Single(sim.GetPresentationEvents(clear: true));
        Assert.True(hit.Blocked);
        Assert.True(sim.GetState(DefenderId).PX > defender.PX);
        Assert.Equal(defender.PX - def.ShieldRadius, hit.HitX, 3);
        Assert.Equal(hit.HitX, presentation.WorldX, 3);
        Assert.Equal(hit.HitY, presentation.WorldY, 3);
        Assert.Equal(hit.HitZ, presentation.WorldZ, 3);
    }

    [Fact]
    public void UnshieldedFighterDoesNotGainBubbleReach()
    {
        var sim = ManualBubbleSimulation(out _);
        var defender = sim.GetState(DefenderId);
        var hitbox = Contact(in defender, xOffset: 0.9f);
        hitbox.Radius = 0.02f;
        sim.Resolver.Spawn(hitbox);

        sim.Tick(new Dictionary<ulong, InputState>());

        Assert.Empty(sim.LastTickHits);
        Assert.Equal((ushort)0, sim.GetState(DefenderId).DamagePercent);
    }

    [Fact]
    public void MissingBodyPoseCannotDisableActiveShieldSurface()
    {
        var sim = ManualBubbleSimulation(out var def);
        def.HurtboxCapsules = Array.Empty<HurtboxCapsule>();
        var defender = sim.GetState(DefenderId);
        var hitbox = Contact(in defender, xOffset: 0.9f);
        hitbox.Radius = 0.02f;
        sim.Resolver.Spawn(hitbox);

        sim.Tick(ShieldInput());

        Assert.True(Assert.Single(sim.LastTickHits).Blocked);
        Assert.Empty(sim.GetLastEntityData());
    }

    [Fact]
    public void ProjectileImpactsBubbleSurfaceBeforeBodyAndExplosionBlocksOnce()
    {
        var sim = ManualBubbleSimulation(out var def);
        var defender = sim.GetState(DefenderId);
        var projectile = Contact(in defender, damage: 3f, xOffset: -1.25f);
        projectile.Radius = 0.1f;
        projectile.VX = 12f;
        projectile.Explosion = new ProjectileExplosion
        {
            Radius = 0.5f, Damage = 8f,
            Knockback = new KnockbackData
            {
                Profile = KnockbackProfile.Custom, Angle = 0,
                BaseKnockback = 1f, KnockbackGrowth = 2f,
            },
            DurationTicks = 2,
        };
        sim.Resolver.Spawn(projectile);
        sim.Tick(ShieldInput());
        var block = Assert.Single(sim.LastTickHits);
        Assert.True(block.Blocked);
        Assert.InRange(block.HitX - defender.PX,
            -def.ShieldRadius - 0.001f,
            -def.ShieldRadius + 0.001f);
        Assert.Single(sim.Resolver.GetActiveHitboxes());

        sim.Tick(ShieldInput());
        Assert.True(Assert.Single(sim.LastTickHits).Blocked);

        // The blast lasts its whole window but never blocks the same fighter twice.
        sim.Tick(ShieldInput());
        Assert.Empty(sim.LastTickHits);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void ShieldBubbleEndsWithShieldDropAndDoesNotBlockOutsideItsRadius()
    {
        var sim = ManualBubbleSimulation(out _);
        sim.Tick(ShieldInput());
        var defender = sim.GetState(DefenderId);
        var outside = Contact(in defender, xOffset: 1.3f);
        outside.Radius = 0.02f;
        outside.DurationTicks = 1;
        sim.Resolver.Spawn(outside);
        sim.Tick(ShieldInput());
        Assert.Empty(sim.LastTickHits);

        var beforeDrop = sim.GetState(DefenderId);
        var outerOnly = Contact(in beforeDrop, xOffset: 0.9f);
        outerOnly.Radius = 0.02f;
        outerOnly.DurationTicks = 1;
        sim.Resolver.Spawn(outerOnly);
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.Equal(ActionState.ShieldDrop, sim.GetState(DefenderId).State);
        Assert.Empty(sim.LastTickHits);

        var vulnerable = sim.GetState(DefenderId);
        var bodyHit = Contact(in vulnerable);
        bodyHit.DurationTicks = 1;
        sim.Resolver.Spawn(bodyHit);
        sim.Tick(new Dictionary<ulong, InputState>());
        Assert.False(Assert.Single(sim.LastTickHits).Blocked);
        Assert.True(sim.GetState(DefenderId).DamagePercent > 0);
    }

    [Fact]
    public void ProjectileHitsCloserUnshieldedFighterBeforeFartherShield()
    {
        var sim = ManualBubbleSimulation(out var def);
        var other = TestHelpers.PlayerState(1.2f);
        other.EntityId = 101;
        other.PY = def.CapsuleHeight * 0.5f;
        sim.RegisterEntity(101, def, other);
        var shielded = sim.GetState(DefenderId);
        var projectile = Contact(in shielded, damage: 3f, xOffset: 1.28f);
        projectile.Radius = 0.12f;
        projectile.VX = -13.8f; // End at x=1.05: both surfaces overlap, closer fighter first.
        sim.Resolver.Spawn(projectile);

        sim.Tick(ShieldInput());

        Assert.Equal((ushort)0, sim.GetState(DefenderId).DamagePercent);
        Assert.Equal((ushort)3, sim.GetState(101).DamagePercent);
        Assert.Equal(101UL, Assert.Single(sim.LastTickHits).TargetEntityId);
    }

    [Theory]
    [InlineData(1f, 4)]
    [InlineData(3.5f, 5)]
    [InlineData(20f, 14)]
    [InlineData(22f, 15)]
    public void BlockStunUsesIncomingDamageCeilingAndClamp(float incomingDamage, ushort expectedStun)
    {
        var sim = CreateSimulation(out _);
        var defender = sim.GetState(DefenderId);
        defender.DamagePercent = 700;
        sim.SetState(DefenderId, defender);
        sim.Resolver.Spawn(Contact(in defender, damage: incomingDamage));

        sim.Tick(ShieldInput());

        Assert.Equal(expectedStun, sim.GetState(DefenderId).BlockStunTicks);
        Assert.Equal((ushort)700, sim.GetState(DefenderId).DamagePercent);
    }

    [Fact]
    public void WeakerBlockKeepsRemainingStunInsteadOfAddingDurations()
    {
        var sim = CreateSimulation(out _);
        var defender = sim.GetState(DefenderId);
        defender.State = ActionState.Shielding;
        defender.BlockStunTicks = 10;
        sim.SetState(DefenderId, defender);
        sim.Resolver.Spawn(Contact(in defender, damage: 1f));

        sim.Tick(ShieldInput());

        Assert.Equal((ushort)9, sim.GetState(DefenderId).BlockStunTicks);
    }

    [Fact]
    public void NonPiercingProjectileIsConsumedAndItsExplosionBlocksOnceWithoutOwnerFreeze()
    {
        var sim = CreateSimulation(out _);
        var defender = sim.GetState(DefenderId);
        var projectile = Contact(in defender, damage: 3f, activationId: 91);
        projectile.AttackSequence = 11;
        projectile.Explosion = new ProjectileExplosion
        {
            Radius = 1f,
            Damage = 8f,
            Knockback = new KnockbackData
            {
                Profile = KnockbackProfile.Custom,
                Angle = 0,
                BaseKnockback = 1f,
                KnockbackGrowth = 2f,
            },
            DurationTicks = 2,
        };
        sim.Resolver.Spawn(projectile);

        sim.Tick(ShieldInput());
        var projectileBlock = Assert.Single(sim.LastTickHits);
        Assert.True(projectileBlock.Blocked);
        Assert.Equal((byte)11, projectileBlock.AttackSequence);
        Assert.Equal((ushort)0, sim.GetState(AttackerId).HitstopTicks);
        Assert.Single(sim.Resolver.GetActiveHitboxes()); // projectile impact spawned its explosion
        var impactEvent = Assert.Single(sim.GetPresentationEvents(clear: true));
        Assert.Equal((byte)11, impactEvent.AttackSequence);

        sim.Tick(ShieldInput());
        var explosionBlock = Assert.Single(sim.LastTickHits);
        Assert.True(explosionBlock.Blocked);
        Assert.Equal((byte)11, explosionBlock.AttackSequence);
        Assert.Equal(0f, explosionBlock.Damage);
        Assert.Equal((ushort)0, sim.GetState(AttackerId).HitstopTicks);
        var explosionEvent = Assert.Single(sim.GetPresentationEvents(clear: true));
        Assert.NotEqual(impactEvent.Key, explosionEvent.Key);
        Assert.Equal(impactEvent.MatchTick + 1, explosionEvent.MatchTick);

        // The blast lasts its whole window but never blocks the same fighter twice.
        sim.Tick(ShieldInput());
        Assert.Empty(sim.LastTickHits);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void MultihitContactAcrossMultipleHurtboxesEmitsOneBlockFeedbackEvent()
    {
        var targetDef = TestHelpers.EngineDef;
        var capsule = new HurtboxCapsule(0, -0.65f, 0, 0, 0.65f, 0, 0.3f);
        targetDef.HurtboxCapsules = new[] { capsule, capsule, capsule };
        var sim = CreateSimulation(out _, targetDef);
        var defender = sim.GetState(DefenderId);
        sim.Resolver.Spawn(Contact(
            in defender, activationId: 101, freezesOwner: true, multipleOpponents: true, duration: 20));

        int blockCount = 0;
        var feedbackKeys = new List<PresentationEventKey>();
        for (int i = 0; i < 10; i++)
        {
            sim.Tick(ShieldInput());
            blockCount += sim.LastTickHits.Count(hit => hit.Blocked);
            feedbackKeys.AddRange(sim.GetPresentationEvents(clear: true).Select(evt => evt.Key));
        }
        for (int i = 0; i < 15; i++)
        {
            sim.Tick(new Dictionary<ulong, InputState>());
            blockCount += sim.LastTickHits.Count(hit => hit.Blocked);
            feedbackKeys.AddRange(sim.GetPresentationEvents(clear: true).Select(evt => evt.Key));
        }

        Assert.Equal((ushort)0, sim.GetState(DefenderId).DamagePercent);

        Assert.Equal(3, sim.GetLastEntityData().Count(entity => entity.Id == DefenderId));
        Assert.Equal(1, blockCount);
        Assert.Single(feedbackKeys);
    }

    [Fact]
    public void SeparateHitWindowsRefreshShieldContactAndFeedback()
    {
        var sim = CreateSimulation(out _);
        var defender = sim.GetState(DefenderId);

        var ticks = new List<uint>();
        var feedbackKeys = new List<PresentationEventKey>();
        for (int i = 0; i < 7; i++)
        {
            if (i % 3 == 0)
            {
                defender = sim.GetState(DefenderId);
                sim.Resolver.Spawn(Contact(in defender, activationId: 202, duration: 1));
            }
            sim.Tick(ShieldInput());
            ticks.AddRange(sim.LastTickHits.Where(hit => hit.Blocked).Select(hit => hit.MatchTick));
            feedbackKeys.AddRange(sim.GetPresentationEvents(clear: true).Select(evt => evt.Key));
        }

        Assert.Equal(new uint[] { 1, 4, 7 }, ticks);
        Assert.Equal(3, feedbackKeys.Distinct().Count());
    }

    [Theory]
    [InlineData(2, false, 0)]
    [InlineData(3, true, 22)]
    [InlineData(4, true, 22)]
    public void IasaShieldCancellationPreservesAcceptedShieldAndSetsCooldown(
        ushort elapsed, bool accepted, ushort expectedCooldown)
    {
        var (sim, ability) = IasaSimulation(elapsed);
        sim.Tick(new Dictionary<ulong, InputState>
        {
            [AttackerId] = new InputState { ShieldHeld = true },
        });

        var state = sim.GetState(AttackerId);
        if (accepted)
        {
            Assert.Equal(ActionState.Shielding, state.State);
            Assert.Equal((byte)0, state.AttackSlot);
            Assert.Equal(expectedCooldown, state.GetCooldown(AbilitySlots.Slot1));
            Assert.Null(sim.GetActiveAbility(AttackerId));
        }
        else
        {
            Assert.Equal(ActionState.Attacking, state.State);
            Assert.Equal(AbilitySlots.Slot1, state.AttackSlot);
            Assert.Equal((ushort)0, state.GetCooldown(AbilitySlots.Slot1));
            Assert.Same(ability, sim.GetActiveAbility(AttackerId));
        }
    }

    [Fact]
    public void HeldShieldBeatsSameTickAttackAdmissionFromIdle()
    {
        var sim = CreateSimulation(out _, attackerX: 2f);
        sim.Tick(new Dictionary<ulong, InputState>
        {
            [AttackerId] = new InputState
            {
                ShieldHeld = true,
                ActiveSlot = AbilitySlots.Slot1,
            },
        });

        var state = sim.GetState(AttackerId);
        Assert.Equal(ActionState.Shielding, state.State);
        Assert.Equal((byte)0, state.AttackSlot);
        Assert.Null(sim.GetActiveAbility(AttackerId));
        Assert.Equal(0UL, sim.GetLastActivationId(AttackerId));
    }

    [Fact]
    public void SameTickBlocksFromDifferentAttackersHaveDistinctFeedbackKeys()
    {
        var sim = CreateSimulation(out var def);
        var secondAttacker = TestHelpers.PlayerState(3f);
        secondAttacker.PY = def.CapsuleHeight * 0.5f;
        sim.RegisterEntity(2, def, secondAttacker);
        var secondDefender = TestHelpers.PlayerState(5f);
        secondDefender.EntityId = 101;
        secondDefender.PY = def.CapsuleHeight * 0.5f;
        sim.RegisterEntity(101, def, secondDefender);

        var firstDefender = sim.GetState(DefenderId);
        var first = Contact(in firstDefender, activationId: 11);
        first.AttackSequence = 4;
        var secondTarget = sim.GetState(101);
        var second = Contact(in secondTarget, activationId: 12);
        second.OwnerId = 2;
        second.AttackSequence = 4;
        sim.Resolver.Spawn(first);
        sim.Resolver.Spawn(second);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            [DefenderId] = new InputState { ShieldHeld = true },
            [101] = new InputState { ShieldHeld = true },
        });

        Assert.Equal(2, sim.LastTickHits.Count(hit => hit.Blocked));
        var events = sim.GetPresentationEvents(clear: true).OrderBy(evt => evt.EntityId).ToArray();
        Assert.Equal(2, events.Length);
        Assert.Equal(events[0].AttackSequence, events[1].AttackSequence);
        Assert.NotEqual(events[0].Key, events[1].Key);
        Assert.NotEqual(events[0].OperationIndex, events[1].OperationIndex);
    }

    [Fact]
    public void BlockPushbackStopsAtWallAndDoesNotPushOffHeightmapEdge()
    {
        var def = TestHelpers.EngineDef;
        const float wallX = 2f;
        float wallStartX = wallX - def.CapsuleRadius - 0.04f;
        var wallSim = CreateSimulation(out _, def, ArenaWithWall(wallX), wallStartX, wallStartX - 2f);
        var wallTarget = wallSim.GetState(DefenderId);
        wallSim.Resolver.Spawn(Contact(in wallTarget));
        wallSim.Tick(ShieldInput());
        float wallDisplacement = wallSim.GetState(DefenderId).PX - wallStartX;
        Assert.InRange(wallDisplacement, 0f, 0.05f);

        const float edgeStartX = 198.95f;
        var edgeSim = CreateSimulation(out _, arena: TestHelpers.TestArena(),
            defenderX: edgeStartX, attackerX: edgeStartX - 2f);
        var edgeTarget = edgeSim.GetState(DefenderId);
        edgeSim.Resolver.Spawn(Contact(in edgeTarget));
        edgeSim.Tick(ShieldInput());
        Assert.Equal(edgeStartX, edgeSim.GetState(DefenderId).PX, 3);
    }

    [Fact]
    public void BlockContactPresentationSourceRoundTripsAndRejectsUnknownSources()
    {
        var packet = new PresentationEventPacket(
            42, DefenderId, 123, "combat.block", 9, PresentationEventSource.BlockContact,
            1f, 2f, 3f, 0.5f);
        var bytes = new byte[packet.WireSize];
        packet.Serialize(bytes);

        Assert.True(PresentationEventPacket.TryDeserialize(bytes, out var decoded));
        Assert.Equal(packet.ToEvent().Key, decoded!.Value.ToEvent().Key);
        Assert.Equal(PresentationEventSource.BlockContact, decoded.Value.Source);

        bytes[22] = byte.MaxValue;
        Assert.False(PresentationEventPacket.TryDeserialize(bytes, out _));
    }

    private static (ServerSimulation sim, CleanupRewriterAbility ability) IasaSimulation(ushort elapsed)
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[0] = new CookedSlotDefinition(
            0, "ground.1", false, "IASA test", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 23, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(20, 4, 0, 0, 0, Array.Empty<string>(), Array.Empty<CookedTimelineOperation>()),
            }));
        def.CookedSlots = slots;
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var attacker = TestHelpers.PlayerState(2f);
        attacker.PY = def.CapsuleHeight * 0.5f;
        sim.RegisterEntity(AttackerId, def, attacker);
        var defender = TestHelpers.PlayerState();
        defender.EntityId = DefenderId;
        defender.PY = def.CapsuleHeight * 0.5f;
        sim.RegisterEntity(DefenderId, def, defender);

        var ability = new CleanupRewriterAbility { Cooldown = 23 };
        sim.ActivateAbility(AttackerId, ability, 2, def);
        var state = sim.GetState(AttackerId);
        state.AttackElapsedTicks = elapsed;
        sim.SetState(AttackerId, state);
        return (sim, ability);
    }

    private static ArenaDefinition ArenaWithWall(float wallX)
    {
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = -10, AY = 0, AZ = -10, BX = -10, BY = 0, BZ = 10, CX = 10, CY = 0, CZ = 10 },
            new CollisionTriangle { AX = -10, AY = 0, AZ = -10, BX = 10, BY = 0, BZ = 10, CX = 10, CY = 0, CZ = -10 },
            new CollisionTriangle { AX = wallX, AY = 0, AZ = -5, BX = wallX, BY = 3, BZ = 5, CX = wallX, CY = 3, CZ = -5 },
            new CollisionTriangle { AX = wallX, AY = 0, AZ = -5, BX = wallX, BY = 0, BZ = 5, CX = wallX, CY = 3, CZ = 5 },
        };
        return arena;
    }

    private sealed class MutatingHitHook : ServerAbility
    {
        public int HitCalls { get; private set; }

        public override void OnStart(ref CharacterState state, CharacterDefinition def)
        {
            state.State = ActionState.Attacking;
            state.AttackSlot = (byte)(Slot + 1);
            state.AnimLockTicks = 30;
        }

        public override void Tick(ref CharacterState state, ref InputState input, CharacterDefinition def) { }

        public override void OnHitEntity(
            ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef, CharacterDefinition targetDef,
            ref float damage, ref float knockbackForce)
        {
            HitCalls++;
            attacker.ChargeStockSpent = 0;
            damage = 99f;
            knockbackForce = 99f;
        }
    }
    private sealed class CleanupRewriterAbility : ServerAbility
    {
        public override void OnStart(ref CharacterState state, CharacterDefinition def)
        {
            state.State = ActionState.Attacking;
            state.AttackSlot = (byte)(Slot + 1);
            state.AnimLockTicks = 30;
        }

        public override void Tick(ref CharacterState state, ref InputState input, CharacterDefinition def) { }

        public override void OnCancel(ref CharacterState state)
        {
            state.State = ActionState.Idle;
            state.AttackSlot = 0;
        }
    }
}
