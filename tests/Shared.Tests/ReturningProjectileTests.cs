using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// A projectile with a "return" block, Sir Bonk's pot lid: it flies out, turns (at the end of its
/// outbound time, at the stage or at a shield), hits each fighter once per leg and is caught by its
/// free owner, or hits him when he is busy. Its slot waits for it.
/// </summary>
public sealed class ReturningProjectileTests
{
    private const float Speed = 14f;
    private const ushort Outbound = 20;
    private static readonly CharacterDefinition Definition = CreateDefinition();
    private static readonly CharacterDefinition TargetDef = TestHelpers.EngineDef;

    internal static CookedProjectile Lid(float baseKnockback = 1f) => new(
        0f, .4f, .6f, Speed, 0f, .35f, 4f, 30f, baseKnockback, 0f, 12, 240,
        Return: new CookedProjectileReturn(Outbound, 16f, 80f, 0f, 30,
            "presentation.lid.catch", "presentation.lid.self-hit"));

    [Fact]
    public void LidFliesOutTurnsAndIsCaughtByItsFreeOwner()
    {
        var sim = MakeSim();
        Throw(sim);
        Assert.Single(Lids(sim));
        Assert.Equal(AbilitySlots.A, sim.GetState(1).ReturningProjectileSlot);

        float farthest = 0f;
        bool caught = false;
        for (int i = 0; i < 200 && !caught; i++)
        {
            Tick(sim, default);
            if (Lids(sim).FirstOrDefault() is { Active: true } lid)
                farthest = MathF.Max(farthest, lid.Z);
            caught = sim.GetPresentationEvents(clear: true).Any(e => e.Source == PresentationEventSource.ProjectileCatch);
        }

        Assert.True(caught);
        Assert.Empty(Lids(sim));
        Assert.Equal((byte)0, sim.GetState(1).ReturningProjectileSlot);
        // Out for 20 ticks at 14 m/s from 0.6 m in front, then it swings round.
        Assert.InRange(farthest, .6f + Speed * Outbound / 60f - .2f, .6f + Speed * Outbound / 60f + 1.2f);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.Equal((ushort)0, sim.GetState(1).DamagePercent);

        Throw(sim);
        Assert.Single(Lids(sim));
    }

    [Fact]
    public void ItsSlotIsRefusedWhileTheLidIsOut()
    {
        var sim = MakeSim();
        Throw(sim);
        for (int i = 0; i < 25; i++)
            Tick(sim, default);
        Assert.Null(sim.GetActiveAbility(1));

        Tick(sim, new InputState { ActiveSlot = AbilitySlots.A });
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Single(Lids(sim));
    }

    [Fact]
    public void LidHitsAFighterOnceEachWayAndPullsThemTowardItsOwnerComingBack()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 3f);
        Throw(sim);

        var hits = new List<SpellResolver.HitResult>();
        float pulledVz = 0f;
        for (int i = 0; i < 200 && Lids(sim).Any(); i++)
        {
            Tick(sim, default);
            foreach (var hit in sim.LastTickHits.Where(h => h.TargetEntityId == 2))
            {
                hits.Add(hit);
                if (hit.ReturnPhase == 2)
                    pulledVz = MathF.Min(pulledVz, sim.GetState(2).KVZ + sim.GetState(2).QueuedKBDirZ);
            }
        }

        Assert.Equal(new byte[] { 1, 2 }, hits.Select(h => h.ReturnPhase).ToArray());
        Assert.Equal(AuthoringKnockbackDirection.TowardOwner, hits[1].KnockbackDirection);
        Assert.True(pulledVz < 0f, "the return hit pulls the fighter toward the thrower at z = 0");
    }

    [Fact]
    public void LidHitsItsBusyOwnerWithAStunAndNoLaunch()
    {
        var sim = MakeSim();
        Throw(sim);
        while (sim.GetActiveAbility(1) != null)
            Tick(sim, default);
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1 });   // a long normal: busy when it comes back
        Assert.NotNull(sim.GetActiveAbility(1));

        bool selfHit = false;
        for (int i = 0; i < 200 && Lids(sim).Any(); i++)
        {
            Tick(sim, default);
            selfHit |= sim.GetPresentationEvents(clear: true).Any(e => e.Source == PresentationEventSource.ProjectileSelfHit);
        }

        Assert.True(selfHit);
        var owner = sim.GetState(1);
        Assert.True(owner.State == ActionState.Hitstun || owner.HitstunTicks > 0 || owner.HitstopTicks > 0);
        Assert.Equal((ushort)0, owner.DamagePercent);
        Assert.InRange(MathF.Abs(owner.KVX) + MathF.Abs(owner.KVY) + MathF.Abs(owner.KVZ), 0f, .01f);
    }

    [Fact]
    public void TheCpuWaitsForItsLidInsteadOfPressingItsSlot()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 4f);
        var policy = new HeuristicBotPolicy();
        var memory = new BotMemory();
        var rng = new Random(7);
        var arena = TestHelpers.TestArena();
        int throws = 0, pressesWhileOut = 0, freeWhileOut = 0;
        for (int i = 0; i < 900; i++)
        {
            var self = sim.GetState(1);
            var input = policy.Decide(self, sim.GetState(2), Definition, rng, memory, arena);
            bool lidOut = self.ReturningProjectileSlot == AbilitySlots.A;
            if (lidOut && self.State is ActionState.Idle or ActionState.Run) freeWhileOut++;
            if (input.ActiveSlot == AbilitySlots.A)
            {
                if (lidOut) pressesWhileOut++;
                else throws++;
            }
            sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
            var target = sim.GetState(2);
            if (MathF.Abs(target.PZ - 4f) > .5f || MathF.Abs(target.PX) > .5f)
            {
                // Keep the target where the lid reaches it.
                target.PX = 0f; target.PZ = 4f; target.KVX = target.KVZ = 0f;
                sim.SetState(2, target);
            }
        }

        Assert.True(throws > 1, $"the CPU throws its lid ({throws})");
        Assert.True(freeWhileOut > 0, "the CPU is free while its lid is out");
        Assert.Equal(0, pressesWhileOut);
    }

    [Theory]
    [InlineData(ActionState.Idle, false, true)]
    [InlineData(ActionState.Idle, true, true)]
    [InlineData(ActionState.Run, false, true)]
    [InlineData(ActionState.JumpSquat, false, true)]
    [InlineData(ActionState.Shielding, false, true)]
    [InlineData(ActionState.LedgeHang, true, true)]
    [InlineData(ActionState.Hitstun, true, true)]
    [InlineData(ActionState.Grabbed, false, true)]
    [InlineData(ActionState.Attacking, false, false)]
    [InlineData(ActionState.AirDodgeMovement, true, false)]
    [InlineData(ActionState.ShieldDrop, false, false)]
    [InlineData(ActionState.GrabAttempt, false, false)]
    public void HeCatchesItWhenFreeShieldingHangingHeldOrHitAndIsBonkedWhenBusy(ActionState state, bool airborne, bool catches)
        => Assert.Equal(catches, ServerSimulation.CatchesReturningProjectile(
            TestHelpers.PlayerState() with { State = state, IsGrounded = !airborne }));

    [Fact]
    public void LagMakesAnIdleOwnerBusy()
    {
        Assert.False(ServerSimulation.CatchesReturningProjectile(TestHelpers.PlayerState() with { LandingLagTicks = 4 }));
        Assert.False(ServerSimulation.CatchesReturningProjectile(TestHelpers.PlayerState() with { HitstopTicks = 4 }));
    }

    [Fact]
    public void LidFollowsItsOwnerWhenHeRunsOff()
    {
        var sim = MakeSim();
        Throw(sim);
        float? caughtAtX = null;
        for (int i = 0; i < 200 && caughtAtX == null; i++)
        {
            // He runs off sideways for half a second, then stands.
            Tick(sim, i < 30 ? new InputState { MoveX = 1f, Right = true } : default);
            caughtAtX = sim.GetPresentationEvents(clear: true)
                .Where(e => e.Source == PresentationEventSource.ProjectileCatch)
                .Select(e => (float?)e.WorldX).FirstOrDefault();
        }

        Assert.NotNull(caughtAtX);
        Assert.True(sim.GetState(1).PX > 3f, $"he ran off ({sim.GetState(1).PX} m)");
        Assert.True(caughtAtX > 3f, $"it swung after him (caught at x = {caughtAtX})");
    }

    [Fact]
    public void ArmourTakesTheSelfHitButItStillBonksHim()
    {
        var sim = MakeSim();
        Throw(sim);
        while (sim.GetActiveAbility(1) != null)
            Tick(sim, default);
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot3 });   // a long armoured normal
        Assert.NotNull(sim.GetActiveAbility(1));

        bool selfHit = false, armoured = false;
        for (int i = 0; i < 200 && Lids(sim).Any(); i++)
        {
            Tick(sim, default);
            selfHit |= sim.GetPresentationEvents(clear: true).Any(e => e.Source == PresentationEventSource.ProjectileSelfHit);
            armoured |= sim.LastTickHits.Any(h => h.TargetEntityId == 1 && h.ArmorProtected);
        }

        Assert.True(selfHit && armoured);
        Assert.Equal((ushort)0, sim.GetState(1).HitstunTicks);
        Assert.NotNull(sim.GetActiveAbility(1));
    }

    [Fact]
    public void ShieldTurnsTheLidBackEarly()
    {
        var sim = MakeSim();
        RegisterTarget(sim, 0f, 2.2f, shield: true);

        float farthest = 0f;
        int blocks = 0;
        var homecoming = new List<PresentationEventSource>();
        for (int i = 0; i < 200 && homecoming.Count == 0; i++)
        {
            Tick(sim, i == 0 ? new InputState { ActiveSlot = AbilitySlots.A } : default, shieldHeld: true);
            blocks += sim.LastTickHits.Count(h => h.TargetEntityId == 2 && h.Blocked);
            if (Lids(sim).FirstOrDefault() is { Active: true } lid)
                farthest = MathF.Max(farthest, lid.Z);
            homecoming.AddRange(sim.GetPresentationEvents(clear: true).Select(e => e.Source)
                .Where(source => source is PresentationEventSource.ProjectileCatch or PresentationEventSource.ProjectileSelfHit));
        }

        Assert.Equal(1, blocks);
        // Bounced back point-blank it is home inside his own 16-tick throw, so it bonks him.
        Assert.Equal(new[] { PresentationEventSource.ProjectileSelfHit }, homecoming);
        Assert.True(farthest < 2f, $"the shield turns it back (farthest {farthest} m)");
        Assert.Equal((ushort)0, sim.GetState(2).DamagePercent);
    }

    [Fact]
    public void WallTurnsTheLidBack()
    {
        var sim = MakeSim(ArenaWithWall(2.5f));
        Throw(sim);

        float farthest = 0f;
        for (int i = 0; i < 200 && (i < 5 || Lids(sim).Any()); i++)
        {
            Tick(sim, default);
            if (Lids(sim).FirstOrDefault() is { Active: true } lid)
                farthest = MathF.Max(farthest, lid.Z);
        }

        Assert.True(farthest < 2.5f, $"the wall turns it back (farthest {farthest} m)");
        Assert.Empty(Lids(sim));
    }

    [Fact]
    public void KoRemovesTheLidAndItsSlotIsReadyAfterTheRespawn()
    {
        var sim = MakeSim();
        sim.SetRespawnPosition(1, 0f, TestHelpers.GroundPY(Definition), 0f);
        Throw(sim);
        Assert.Single(Lids(sim));

        // Off the floor and below the kill height (a fighter under the floor would be snapped back up).
        var falling = sim.GetState(1);
        falling.PX = -50f;
        falling.PY = -50f;
        falling.IsGrounded = false;
        sim.SetState(1, falling);
        Tick(sim, default);

        Assert.Empty(Lids(sim));
        Assert.Equal((byte)0, sim.GetState(1).ReturningProjectileSlot);
        for (int i = 0; i < 400 && sim.GetActiveAbility(1) == null; i++)
            Tick(sim, new InputState { ActiveSlot = AbilitySlots.A });
        Assert.NotNull(sim.GetActiveAbility(1));
        for (int i = 0; i < 3; i++)
            Tick(sim, default);
        Assert.Single(Lids(sim));
    }

    [Fact]
    public void RestoringASnapshotWithTheLidInFlightMatchesTheContinuousRun()
    {
        var continuous = MakeSim();
        RegisterTarget(continuous, 0f, 3f);
        Throw(continuous);
        for (int i = 0; i < 12; i++)
            Tick(continuous, default);
        Assert.Single(Lids(continuous));

        var restored = MakeSim();
        RegisterTarget(restored, 0f, 3f);
        restored.RestoreSnapshot(continuous.CaptureSnapshot());
        for (int i = 0; i < 90; i++)
        {
            Tick(continuous, default);
            Tick(restored, default);
            Assert.True(restored.CaptureSnapshot().ContentEquals(continuous.CaptureSnapshot()), $"diverged at tick {i}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnHitOverridesValuesRecordsChallengeAndLetsABusyOwnerCatch(bool shield)
    {
        var def = CreateDefinition();
        var slots = def.CookedSlots!.ToArray();
        var lid = Lid(0f) with { ChallengeTicks = 120, Return = Lid().Return! with
            { Hit = new ProjectileLegHit(5, 0, 20, 0, 1, 48) } };
        slots[3] = new CookedSlotDefinition(3, "ground.A", false, "Lid", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false, new CookedTimeline(new[] {
            new CookedStage(16, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[] {
                new CookedSpawnProjectileOperation(2, AuthoringUnit.Meters, lid) }) }));
        def = def with { CookedSlots = slots };
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        RegisterTarget(sim, 0, 3, shield);
        Throw(sim, shield);
        bool returnHit = false, caught = false;
        for (int t = 0; t < 160; t++)
        {
            Tick(sim, t == 18 ? new InputState { ActiveSlot = AbilitySlots.Slot1 } : default, shield);
            foreach (var h in sim.LastTickHits.Where(h => h.TargetEntityId == 2 && h.ReturnPhase == 2 && !h.Blocked))
            {
                returnHit = true;
                Assert.Equal(5, h.Damage);
                Assert.Equal((ushort)48, h.FixedHitstunTicks);
                Assert.Equal((ulong)2, sim.GetState(1).Challenge.TargetId);
            }
            bool catchNow = sim.GetPresentationEvents(clear: true).Any(e => e.Source == PresentationEventSource.ProjectileCatch);
            if (catchNow && !shield) Assert.Equal(AbilitySlots.Slot1, sim.GetState(1).AttackSlot);
            caught |= catchNow;
        }
        Assert.Equal(!shield, returnHit);
        Assert.Equal(!shield, caught);
        if (shield) Assert.Equal(default, sim.GetState(1).Challenge);
        else Assert.Equal((ushort)0, sim.GetState(1).DamagePercent);
    }

    [Fact]
    public void TurningClearsOutboundHomingAndSwapsTheReturnHitValues()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox { X = 5, Y = 5, Z = 5, VZ = 15, Radius = .75f, DurationTicks = 200,
            OwnerId = 1, ActivationId = 2, HomingTargetEntityId = 3, Damage = 9, StunTicks = 12,
            Return = new ProjectileReturn { OutboundTicks = 34, ReturnSpeed = 16, ReturnAcceleration = 80,
                Hit = new ProjectileLegHit(5, 0, 20, 0, 1, 48) } });
        resolver.TurnReturningProjectile(1, 2, 3);
        var lid = Assert.Single(resolver.GetActiveHitboxes());
        Assert.True(lid.ReturnLeg);
        Assert.Equal(0UL, lid.HomingTargetEntityId);
        Assert.Equal(5, lid.Damage); Assert.Equal(20, lid.BaseKnockback);
        Assert.Equal((ushort)48, lid.FixedHitstunTicks);
        Assert.Equal(AuthoringKnockbackDirection.TowardOwner, lid.KnockbackDirection);
    }

    private static IEnumerable<Hitbox> Lids(ServerSimulation sim)
        => sim.Resolver.GetActiveHitboxes().Where(h => h.Return.HasValue);

    private static void Throw(ServerSimulation sim, bool shieldHeld = false)
    {
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.A }, shieldHeld);
        for (int i = 0; i < 3; i++)
            Tick(sim, default, shieldHeld);
    }

    private static ServerSimulation MakeSim(ArenaDefinition? arena = null)
    {
        var sim = new ServerSimulation(arena ?? TestHelpers.TestArena());
        sim.RegisterEntity(1, Definition, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(Definition) });
        return sim;
    }

    private static void RegisterTarget(ServerSimulation sim, float x, float z, bool shield = false)
        => sim.RegisterEntity(2, TargetDef, TestHelpers.PlayerState(x, z) with
        {
            PY = TestHelpers.GroundPY(TargetDef),
            State = shield ? ActionState.Shielding : ActionState.Idle,
        });

    private static void Tick(ServerSimulation sim, InputState attacker, bool shieldHeld = false)
    {
        var inputs = new Dictionary<ulong, InputState> { [1] = attacker };
        if (shieldHeld)
            inputs[2] = new InputState { ShieldHeld = true };
        sim.Tick(inputs);
    }

    private static CookedSlotDefinition ThrowSlot(int ordinal, string id, bool air) => new(
        ordinal, id, air, "Lid fixture", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
        0, false, false,
        new CookedTimeline(new[]
        {
            new CookedStage(16, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
            {
                new CookedSpawnProjectileOperation(2, AuthoringUnit.Meters, Lid()),
            }),
        }));

    private static CharacterDefinition CreateDefinition()
    {
        var definition = TestHelpers.EngineDef;
        var slots = definition.CookedSlots!.ToArray();
        slots[3] = ThrowSlot(3, "ground.A", false);
        slots[10] = ThrowSlot(10, "air.A", true);
        // ground.1: a long normal, so the owner is busy when the lid comes back.
        slots[0] = new CookedSlotDefinition(0, "ground.1", false, "Long normal", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(120, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<CookedTimelineOperation>()),
            }));
        // ground.3: the same, armoured.
        slots[1] = new CookedSlotDefinition(1, "ground.3", false, "Long armoured normal", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(120, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
                {
                    new CookedArmorWindowOperation(0, AuthoringUnit.Ticks, 120),
                }),
            }));
        definition = definition with { CookedSlots = slots };
        return definition;
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
