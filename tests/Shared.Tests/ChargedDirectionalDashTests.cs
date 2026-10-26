using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ChargedDirectionalDashTests
{
    private static readonly CharacterDefinition Definition = CreateDefinition();
    private static readonly CharacterDefinition TargetDef = TestHelpers.EngineDef;
    private static float GroundY => TestHelpers.GroundPY(Definition);

    [Theory]
    [InlineData(19, 2.8466667f, 6f)]
    [InlineData(20, 2.9333334f, 9f)]
    [InlineData(44, 5.0133333f, 9f)]
    [InlineData(45, 5.1f, 12f)]
    [InlineData(60, 6.4f, 12f)]
    public void ChargeBoundariesControlContinuousDistanceAndFinisherDamage(
        int chargeTicks, float expectedDistance, float expectedDamage)
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, expectedDistance + .6f);
        BeginCharge(sim, chargeTicks, aimYaw: 0);
        Assert.Equal((ushort)chargeTicks, sim.GetState(1).ChargeTicks);
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());

        Tick(sim, new InputState { AimYaw = 9000 }); // release with a different camera yaw
        var hits = new List<SpellResolver.HitResult>(sim.LastTickHits);
        TickUntilEnd(sim, hits);

        var state = sim.GetState(1);
        Assert.Equal(ActionState.Idle, state.State);
        Assert.InRange(state.PZ, expectedDistance - 0.002f, expectedDistance + 0.002f);
        Assert.InRange(state.PX, -0.002f, 0.002f);
        Assert.Contains(hits, hit => hit.TargetEntityId == 2 && !hit.Blocked
            && MathF.Abs(hit.Damage - expectedDamage) < 0.001f);
    }

    [Fact]
    public void ChargeRemainsHeldAtCapThenCommitsCachedDirectionWithoutSteeringOrInvulnerability()
    {
        var sim = MakeSim();
        BeginCharge(sim, 75, aimYaw: 0);

        var held = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, held.State);
        Assert.Equal((ushort)60, held.ChargeTicks);
        Assert.NotNull(sim.GetActiveAbility(1));

        Tick(sim, new InputState { AimYaw = 9000 });
        var released = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, released.State);
        Assert.Equal(0f, released.FacingYaw);
        Assert.True(sim.GetActiveAbility(1)!.IgnoresFighterPushboxes);
        Assert.Equal((ushort)0, released.InvincibilityTicks);

        for (int i = 0; i < 120 && sim.GetActiveAbility(1) != null; i++)
            Tick(sim, new InputState { IsAiming = true, AimYaw = 18000, MoveX = 1f, MoveY = 1f });

        var finished = sim.GetState(1);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal(ActionState.Idle, finished.State);
        Assert.InRange(finished.PZ, 6.398f, 6.402f);
        Assert.InRange(finished.PX, -0.002f, 0.002f);
        Assert.Equal(0f, finished.InvincibilityTicks);
    }

    [Fact]
    public void TraversalHitsEachOpponentOnceAndFinisherUsesIndependentHitIdentity()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 4.2f);
        BeginCharge(sim, 45, aimYaw: 0);
        Tick(sim, default);

        var hits = new List<SpellResolver.HitResult>(sim.LastTickHits);
        TickUntilEnd(sim, hits);

        var targetHits = hits.FindAll(hit => hit.TargetEntityId == 2 && !hit.Blocked);
        Assert.Equal(2, targetHits.Count);
        Assert.Contains(targetHits, hit => MathF.Abs(hit.Damage - 1f) < 0.001f);
        Assert.Contains(targetHits, hit => MathF.Abs(hit.Damage - 12f) < 0.001f);
        Assert.Equal((ushort)13, sim.GetState(2).DamagePercent);
    }

    [Fact]
    public void FinisherLaunchCannotBeReplacedByALateTraversalHitDuringHitstop()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 5.6f);
        var target = sim.GetState(2);
        target.DamagePercent = 120;
        sim.SetState(2, target);
        BeginCharge(sim, 45, aimYaw: 0);
        Tick(sim, default);
        var hits = new List<SpellResolver.HitResult>(sim.LastTickHits);
        bool sawLaunch = false;
        for (int tick = 0; tick < 160; tick++)
        {
            Tick(sim, default);
            hits.AddRange(sim.LastTickHits);
            target = sim.GetState(2);
            sawLaunch |= target.HitstunTicks > 0 && target.KVY > 0f;
        }

        var targetHits = hits.Where(hit => hit.TargetEntityId == 2 && !hit.Blocked).ToArray();
        Assert.Contains(targetHits, hit => hit.Damage == 12f);
        Assert.True(sawLaunch, "A connected finisher must launch after hitstop.");
        int finisher = Array.FindIndex(targetHits, hit => hit.Damage == 12f);
        Assert.DoesNotContain(targetHits.Skip(finisher + 1), hit => hit.Damage == 1f);
    }

    [Fact]
    public void TraversalReachesLaterOpponentsAfterEarlierContactsPauseTravel()
    {
        var sim = MakeSim();
        foreach (var (entityId, z) in new[] { (2UL, 1f), (3UL, 2.8f), (4UL, 6.2f) })
            sim.RegisterEntity(entityId, TargetDef, TestHelpers.PlayerState(0f, z) with
            {
                PY = TestHelpers.GroundPY(TargetDef),
            });
        BeginCharge(sim, 60, aimYaw: 0);
        Tick(sim, default);
        var hits = new List<SpellResolver.HitResult>(sim.LastTickHits);
        TickUntilEnd(sim, hits);

        foreach (ulong targetId in new[] { 2UL, 3UL, 4UL })
            Assert.Single(hits.Where(hit => hit.TargetEntityId == targetId && !hit.Blocked
                && MathF.Abs(hit.Damage - 1f) < 0.001f));
    }

    [Fact]
    public void DashPhasesOnlyThroughFighterPushboxes()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 2f, invincibilityTicks: 500);
        BeginCharge(sim, 60, aimYaw: 0);
        Tick(sim, default);

        bool crossedPushbox = false;
        int hitCount = 0;
        for (int i = 0; i < 120 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default);
            var attacker = sim.GetState(1);
            var target = sim.GetState(2);
            float delta = MathF.Abs(attacker.PZ - target.PZ);
            crossedPushbox |= delta < Definition.CapsuleRadius + TargetDef.CapsuleRadius;
            hitCount += sim.LastTickHits.Count;
            Assert.Equal(2f, target.PZ);
            Assert.Equal((ushort)0, attacker.InvincibilityTicks);
        }

        Assert.True(crossedPushbox);
        Assert.Equal(0, hitCount);
        Assert.InRange(sim.GetState(1).PZ, 6.398f, 6.402f);
    }

    [Fact]
    public void DashEndsOnItsMarkerWhenItFinishesInsideAFighter()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 6.25f, invincibilityTicks: 500);
        BeginCharge(sim, 60, aimYaw: 0);
        Tick(sim, default);

        bool recovered = false;
        for (int i = 0; i < 120 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default);
            if (sim.GetActiveAbility(1) is not { IgnoresFighterPushboxes: false }) continue;
            recovered = true;
            Assert.InRange(sim.GetState(1).PZ, 6.398f, 6.402f);
        }

        Assert.True(recovered);
        Assert.InRange(sim.GetState(1).PZ, 6.398f, 6.402f);
        Assert.True(6.4f - sim.GetState(2).PZ >= Definition.CapsuleRadius + TargetDef.CapsuleRadius - 0.001f);
    }

    [Fact]
    public void LandingDuringTheDashKeepsItsFullDistance()
    {
        var sim = MakeSim(FlatStage());
        sim.SetState(1, TestHelpers.PlayerState() with
        {
            PY = GroundY + 0.12f,
            IsGrounded = false,
            AirTimeTicks = 100, // past the float window, so gravity pulls it down mid-dash
        });
        BeginCharge(sim, 1, aimYaw: 0);
        Tick(sim, default);

        bool landedWhileDashing = false;
        for (int i = 0; i < 120 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default);
            landedWhileDashing |= sim.GetState(1).IsGrounded
                && sim.GetActiveAbility(1) is { IgnoresFighterPushboxes: true };
        }

        Assert.True(landedWhileDashing);
        float expected = 1.2f + 5.2f / 60f;
        Assert.InRange(sim.GetState(1).PZ, expected - 0.002f, expected + 0.002f);
    }

    [Fact]
    public void DashClimbsARampToItsFullDistance()
    {
        float rampTop = 2f * MathF.Tan(20f * MathF.PI / 180f);
        var sim = MakeSim(StageFrom(
            Quad(-5f, 5f, -5f, 0f, 1f, 0f),
            Quad(-5f, 5f, 1f, 0f, 3f, rampTop),
            Quad(-5f, 5f, 3f, rampTop, 10f, rampTop)));
        BeginCharge(sim, 60, aimYaw: 0);
        TickUntilEnd(sim);

        var state = sim.GetState(1);
        Assert.Equal(ActionState.Idle, state.State);
        Assert.InRange(state.PZ, 6.398f, 6.402f);
        Assert.InRange(state.PY, GroundY + rampTop - 0.01f, GroundY + rampTop + 0.01f);
    }

    [Theory]
    [InlineData(0.8f)]
    [InlineData(4f)]
    public void TerrainStopsDashWithoutSeekingAnUnspawnedFinisher(float wallZ)
    {
        var sim = MakeSim(ArenaWithWall(wallZ));
        BeginCharge(sim, 60, aimYaw: 0);
        for (int i = 0; i < 200 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default);
            if (sim.GetActiveAbility(1) is { IgnoresFighterPushboxes: false })
            {
                var stopped = sim.GetState(1);
                Assert.True(stopped.AttackElapsedTicks < 31,
                    $"wall={wallZ}, z={stopped.PZ}, elapsed={stopped.AttackElapsedTicks}, tick={i}, " +
                    $"hitboxes={string.Join(",", sim.Resolver.GetActiveHitboxes().Select(h => h.Damage))}");
            }
        }

        Assert.Null(sim.GetActiveAbility(1));
        var state = sim.GetState(1);
        Assert.Equal(ActionState.Idle, state.State);
        Assert.InRange(state.PZ, 0f, wallZ);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void ShieldBlocksStopTheDashWithoutDamagingOrPassingThroughTheFighter()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 2f, shield: true);
        BeginCharge(sim, 1, aimYaw: 0, shieldHeld: true);
        Tick(sim, default, shieldHeld: true);

        var hits = new List<SpellResolver.HitResult>(sim.LastTickHits);
        for (int i = 0; i < 120 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default, shieldHeld: true);
            hits.AddRange(sim.LastTickHits);
        }

        Assert.Contains(hits, hit => hit.TargetEntityId == 2 && hit.Blocked && hit.Damage == 0f);
        Assert.Equal((ushort)0, sim.GetState(2).DamagePercent);
        Assert.True(sim.GetState(1).PZ < sim.GetState(2).PZ - 0.6f);
        Assert.Null(sim.GetActiveAbility(1));
    }

    [Fact]
    public void InterruptionRemovesActivationHitboxesAndPreservesIncomingHitstunVelocity()
    {
        var sim = MakeSim();
        BeginCharge(sim, 1, aimYaw: 0);
        Tick(sim, default);
        Tick(sim, default);
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
        Assert.True(sim.GetActiveAbility(1)!.IgnoresFighterPushboxes);

        var hitstun = sim.GetState(1);
        hitstun.State = ActionState.Hitstun;
        hitstun.HitstunTicks = 20;
        hitstun.KVX = 4f;
        hitstun.KVZ = 6f;
        sim.SetState(1, hitstun);
        Tick(sim, default);

        var interrupted = sim.GetState(1);
        Assert.Equal(ActionState.Hitstun, interrupted.State);
        Assert.True(interrupted.KVX > 0f || interrupted.KVZ > 0f);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncomingAttackCanInterruptBothHeldChargeAndCommittedDash(bool released)
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 30f);
        BeginCharge(sim, 3, aimYaw: 0);
        if (released)
            Tick(sim, default);
        var actor = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 2, X = actor.PX, Y = actor.PY, Z = actor.PZ,
            Radius = 0.25f, Damage = 7, BaseKnockback = 4f, StunTicks = 10,
            DurationTicks = 1,
        });

        InputState incomingTick = released ? default : new InputState { IsAiming = true };
        Tick(sim, incomingTick);
        Assert.Equal(7, sim.GetState(1).DamagePercent);
        for (int i = 0; i < 30 && sim.GetActiveAbility(1) != null; i++)
            Tick(sim, incomingTick);
        Assert.Equal(ActionState.Hitstun, sim.GetState(1).State);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == 1);
        Assert.False(sim.GetState(1).IsAiming);
    }

    [Fact]
    public void AirRUsesTheSameCommittedDirectionAndExactDistance()
    {
        var sim = MakeSim();
        var airborne = TestHelpers.PlayerState() with
        {
            PY = GroundY + 2f,
            IsGrounded = false,
        };
        sim.SetState(1, airborne);
        BeginCharge(sim, 5, aimYaw: 9000);
        Assert.True(sim.GetActiveAbility(1)!.AirborneAtStart);

        Tick(sim, new InputState { AimYaw = 0 });
        for (int i = 0; i < 120 && sim.GetActiveAbility(1) != null; i++)
            Tick(sim, new InputState { AimYaw = 18000, MoveX = -1f });

        var finished = sim.GetState(1);
        float expectedDistance = 1.2f + (5.2f * 5f / 60f);
        Assert.Equal(ActionState.Idle, finished.State);
        Assert.InRange(finished.PX, expectedDistance - 0.002f, expectedDistance + 0.002f);
        Assert.InRange(finished.PZ, -0.002f, 0.002f);
        Assert.Equal((ushort)0, finished.InvincibilityTicks);
    }

    [Fact]
    public void WhiffRecoveryBeginsAtTheLockedEndpointAndKeepsTheFinisherAnimationSeek()
    {
        var sim = MakeSim();
        BeginCharge(sim, 1, aimYaw: 0);
        Tick(sim, default);

        bool sawFinisher = false;
        bool sawFinisherAdvance = false;
        for (int i = 0; i < 20 && sim.GetActiveAbility(1)?.IgnoresFighterPushboxes == true; i++)
        {
            Tick(sim, default);
            var state = sim.GetState(1);
            var finisher = sim.Resolver.GetActiveHitboxes().Any(hitbox =>
                hitbox.SourceEvent.EndBoneName == "_weapon_tip");
            if (finisher && !sawFinisher)
            {
                Assert.Equal((ushort)31, state.AttackElapsedTicks);
                sawFinisher = true;
            }
            else if (!sawFinisher)
            {
                Assert.True(state.AttackElapsedTicks < 31);
            }
            else if (!sawFinisherAdvance)
            {
                Assert.Equal((ushort)32, state.AttackElapsedTicks);
                sawFinisherAdvance = true;
            }
        }

        Assert.True(sawFinisher);
        Assert.True(sawFinisherAdvance);

        var endpoint = sim.GetState(1);
        Assert.InRange(endpoint.PZ, 1.285f, 1.288f);
        Assert.Equal(ActionState.Attacking, endpoint.State);
        Assert.False(sim.GetActiveAbility(1)!.IgnoresFighterPushboxes);

        for (int i = 0; i < 23; i++)
            Tick(sim, default);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.NotNull(sim.GetActiveAbility(1));
        Tick(sim, default);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.Null(sim.GetActiveAbility(1));
    }

    private static ServerSimulation MakeSim(ArenaDefinition? arena = null)
    {
        var sim = new ServerSimulation(arena ?? TestHelpers.TestArena());
        var state = TestHelpers.PlayerState() with { PY = GroundY };
        sim.RegisterEntity(1, Definition, state);
        return sim;
    }

    private static void RegisterTarget(ServerSimulation sim, float x, float z,
        ushort invincibilityTicks = 0, bool shield = false)
    {
        var state = TestHelpers.PlayerState(x, z) with
        {
            PY = TestHelpers.GroundPY(TargetDef),
            InvincibilityTicks = invincibilityTicks,
            State = shield ? ActionState.Shielding : ActionState.Idle,
        };
        sim.RegisterEntity(2, TargetDef, state);
    }

    private static void BeginCharge(ServerSimulation sim, int chargeTicks, short aimYaw,
        bool shieldHeld = false)
    {
        Tick(sim, new InputState
        {
            ActiveSlot = AbilitySlots.R,
            IsAiming = chargeTicks > 0,
            AimYaw = aimYaw,
        }, shieldHeld);
        for (int i = 1; i < chargeTicks; i++)
            Tick(sim, new InputState { IsAiming = true, AimYaw = aimYaw }, shieldHeld);
    }

    private static void Tick(ServerSimulation sim, InputState attacker, bool shieldHeld = false)
    {
        var inputs = new Dictionary<ulong, InputState> { [1] = attacker };
        if (shieldHeld)
            inputs[2] = new InputState { ShieldHeld = true };
        sim.Tick(inputs);
    }

    private static void TickUntilEnd(ServerSimulation sim, List<SpellResolver.HitResult>? hits = null)
    {
        for (int i = 0; i < 180 && sim.GetActiveAbility(1) != null; i++)
        {
            Tick(sim, default);
            hits?.AddRange(sim.LastTickHits);
        }
    }

    private static ArenaDefinition ArenaWithWall(float wallZ)
    {
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = new[]
        {
            Floor(0f, -10f, 10f, -10f, 10f),
            new CollisionTriangle
            {
                AX = 10f, AY = 0f, AZ = -10f, BX = -10f, BY = 0f, BZ = -10f, CX = -10f, CY = 0f, CZ = 10f,
            },
            new CollisionTriangle
            {
                AX = -10f, AY = 0f, AZ = 10f, BX = 10f, BY = 0f, BZ = 10f, CX = 10f, CY = 0f, CZ = -10f,
            },
            new CollisionTriangle
            {
                AX = -10f, AY = 0f, AZ = wallZ, BX = -10f, BY = 3f, BZ = wallZ, CX = 10f, CY = 0f, CZ = wallZ,
            },
            new CollisionTriangle
            {
                AX = 10f, AY = 3f, AZ = wallZ, BX = 10f, BY = 0f, BZ = wallZ, CX = -10f, CY = 3f, CZ = wallZ,
            },
        };
        arena.MinX = arena.MinZ = -10f;
        arena.MaxX = arena.MaxZ = 10f;
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }

    private static ArenaDefinition FlatStage()
        => StageFrom(Quad(-10f, 10f, -10f, 0f, 10f, 0f));

    private static ArenaDefinition StageFrom(params CollisionTriangle[][] quads)
    {
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = quads.SelectMany(quad => quad).ToArray();
        arena.MinX = arena.MinZ = -10f;
        arena.MaxX = arena.MaxZ = 10f;
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }

    /// <summary>Upward-facing quad over x0..x1, rising linearly from (z0, y0) to (z1, y1).</summary>
    private static CollisionTriangle[] Quad(float x0, float x1, float z0, float y0, float z1, float y1)
        => new[]
        {
            new CollisionTriangle { AX = x0, AY = y0, AZ = z0, BX = x0, BY = y1, BZ = z1, CX = x1, CY = y0, CZ = z0 },
            new CollisionTriangle { AX = x0, AY = y1, AZ = z1, BX = x1, BY = y1, BZ = z1, CX = x1, CY = y0, CZ = z0 },
        };

    private static CollisionTriangle Floor(float y, float minX, float maxX, float minZ, float maxZ)
        => new()
        {
            AX = minX, AY = y, AZ = minZ,
            BX = minX, BY = y, BZ = maxZ,
            CX = maxX, CY = y, CZ = minZ,
        };

    private static CharacterDefinition CreateDefinition()
    {
        var definition = TestHelpers.EngineDef;
        var traversal = new CookedHitbox(AuthoringHitboxShape.Sphere, .45f,
            0f, 0f, 0f, 0f, 0f, 0f, null, null, 1f, 0f, 0f, 0f, 0, 1, true, 0);
        var finisher = new CookedHitbox(AuthoringHitboxShape.Capsule, .5f,
            0f, 0f, 0f, 0f, 0f, 0f, "_weapon_hilt", "_weapon_tip", 6f, 45f, 5f, 80f, 8, 9, true, 0);
        var parameters = new CookedChargedDirectionalDashCapabilityParameters(
            60, 20, 45, 1.2f, 6.4f, 10.5f, 4, 31, 24, 9f, 12f, traversal, finisher);
        var timeline = new CookedTimeline(new[]
        {
            new CookedStage(80, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
            {
                new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                    CharacterPackageCompiler.ChargedDirectionalDashCapabilityId, "1", parameters),
            }),
        });
        var slots = definition.CookedSlots!.ToArray();
        foreach (bool airborne in new[] { false, true })
        {
            CanonicalSlotProjection.TryGet(airborne, "R", out var address);
            slots[address.Ordinal] = new CookedSlotDefinition(address.Ordinal,
                address.Id, airborne, "Charged dash fixture", "", "",
                AuthoringAbilityBehavior.DirectionalDash, AuthoringAimMode.GroundVector, 0, false, false,
                timeline, aimMovement: AuthoringAimMovementMode.Mobile);
        }
        definition = definition with { CookedSlots = slots };

        return definition;
    }
}
