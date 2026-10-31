using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class MankiBomberTests
{
    private static CharacterDefinition Def => TestHelpers.ResolveSourceDef("manki");
    private static ServerSimulation Match(bool air = false, bool persisted = false, IMatchRule? rule = null, float killMaxZ = 1000f)
    {
        var arena = TestHelpers.TestArena();
        arena.KillMaxX = 1000f; arena.KillMaxZ = killMaxZ;
        var sim = new ServerSimulation(arena, rule ?? NoWinMatchRule.Instance);
        var def = persisted ? TestHelpers.MankiDef : Def;
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20, 20) with
        { PY = def.CapsuleHeight * .5f + (air ? 6 : 0), IsGrounded = !air });
        return sim;
    }

    private static void Step(ServerSimulation sim, InputState input = default, int ticks = 1)
    {
        for (int i = 0; i < ticks; i++) sim.Tick(new() { [1] = i == 0 ? input : input.WithoutEdges() });
    }

    private static void TakeOff(ServerSimulation sim)
    {
        Step(sim, new() { ActiveSlot = AbilitySlots.E }, 3);
        Assert.True(sim.GetState(1).Flight.Active);
        Assert.Equal(ActionState.Flying, sim.GetState(1).State);
        Assert.Null(sim.GetActiveAbility(1));
    }

    private static void Target(ServerSimulation sim, float x, float y, float z, ushort damage = 0)
        => sim.RegisterEntity(100, TestHelpers.EngineDef, TestHelpers.NpcState(x, z) with
        { PY = y, IsGrounded = y <= .75f, DamagePercent = damage });

    [Fact]
    public void IgnitionHandsOffToFlyingAndSteeringKeepsNeutralHeading()
    {
        var sim = Match(); float y = sim.GetState(1).PY;
        TakeOff(sim); Step(sim, ticks: 8);
        Assert.True(sim.GetState(1).PY > y + .5f);
        Step(sim, new() { MoveX = 1 }); Assert.Equal(12f, sim.GetState(1).VX);
        Step(sim); Assert.Equal(12f, sim.GetState(1).VX);
        Step(sim, new() { MoveY = -1 }); Assert.Equal(-12f, sim.GetState(1).VZ);
        Step(sim, ticks: 15); Assert.Equal(-1.2f, sim.GetState(1).VY);
        byte jumps = sim.GetState(1).JumpsLeft;
        Step(sim, new() { Jump = true }); Assert.Equal(10f, sim.GetState(1).VY);
        Assert.Equal(jumps, sim.GetState(1).JumpsLeft);
        Assert.Equal((ushort)0, sim.GetState(1).GetCooldown(AbilitySlots.E));
    }

    [Theory]
    [InlineData(AbilitySlots.A)]
    [InlineData(AbilitySlots.R)]
    public void QAndRKeepFlyingDuringAndAfterTheirActivation(byte slot)
    {
        var sim = Match(air: true); TakeOff(sim);
        Step(sim, new() { ActiveSlot = slot, MoveX = 1 }, 80);
        Assert.True(sim.GetState(1).Flight.Active);
        Assert.Equal(ActionState.Flying, sim.GetState(1).State);
        Assert.Equal(12f, sim.GetState(1).VX);
        Assert.Equal((ushort)0, sim.GetState(1).GetCooldown(AbilitySlots.E));
        if (slot == AbilitySlots.R)
            Assert.Contains(sim.Resolver.GetActiveHitboxes(), h => h.AttackSlot == AbilitySlots.R);
    }

    [Theory]
    [InlineData(AbilitySlots.Slot1)]
    [InlineData(AbilitySlots.Slot3)]
    [InlineData(AbilitySlots.Slot4)]
    public void NormalEndsFlightAndUsesItsAirVariant(byte slot)
    {
        var sim = Match(air: true); TakeOff(sim);
        Step(sim, new() { ActiveSlot = slot });
        Assert.False(sim.GetState(1).Flight.Active);
        Assert.Equal(slot, sim.GetState(1).AttackSlot);
        Assert.True(sim.GetActiveMove(1)!.Value.Slot.IsAir);
        Assert.True(sim.GetState(1).GetCooldown(AbilitySlots.E) > 0);
    }

    [Fact]
    public void AnyAcceptedHitEndsFlightAndStartsCooldown()
    {
        var sim = Match(air: true); TakeOff(sim); var s = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 100, X = s.PX, Y = s.PY, Z = s.PZ,
            Radius = 2, Damage = 1, BaseKnockback = 2, StunTicks = 5, DurationTicks = 1 });
        Step(sim);
        Assert.False(sim.GetState(1).Flight.Active);
        Assert.Equal((ushort)1, sim.GetState(1).DamagePercent);
        Assert.Equal((ushort)600, sim.GetState(1).GetCooldown(AbilitySlots.E));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FuelOrSecondEEndsFlightAndRefuelWaitsForLanding(bool manual)
    {
        var sim = Match(air: true); TakeOff(sim); Step(sim, ticks: 30);
        if (manual) Step(sim, new() { ActiveSlot = AbilitySlots.E });
        else Step(sim, ticks: 151);
        var ended = sim.GetState(1);
        Assert.False(ended.Flight.Active);
        Assert.Equal(ActionState.Idle, ended.State);
        Assert.True(ended.GetCooldown(AbilitySlots.E) > 0);
        ushort remaining = ended.Flight.FuelTicksLeft;
        Step(sim, ticks: 3); Assert.Equal(remaining, sim.GetState(1).Flight.FuelTicksLeft);
        var falling = sim.GetState(1);
        sim.SetState(1, falling with { PY = Def.CapsuleHeight * .5f + .01f, VY = -5, IsGrounded = false });
        Step(sim);
        Assert.True(sim.GetState(1).IsGrounded);
        Assert.Equal((ushort)180, sim.GetState(1).Flight.FuelTicksLeft);
    }

    [Fact]
    public void AirDropSettlesThenArmsAndExplodesOnce()
    {
        var sim = Match(air: true); TakeOff(sim);
        Step(sim, new() { ActiveSlot = AbilitySlots.A });
        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
        Assert.Equal(0f, bomb.VX); Assert.Equal(0f, bomb.VZ); Assert.True(bomb.Gravity > 0f);
        Target(sim, bomb.X + 1.5f, .75f, bomb.Z);
        Step(sim, ticks: 135);
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
        Assert.True(sim.GetState(100).DamagePercent > 0);
        Assert.Single(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.round-bomb.explosion");
    }

    [Fact]
    public void AirDropExplodesOnFighterBeforeReachingTheFloor()
    {
        var sim = Match(air: true); TakeOff(sim);
        var s = sim.GetState(1); Target(sim, s.PX, s.PY - 2f, s.PZ);
        sim.NoGravityEntityId = 100;
        Step(sim, new() { ActiveSlot = AbilitySlots.A }); Step(sim, ticks: 30);
        Assert.True(sim.GetState(100).DamagePercent > 0);
        Assert.Contains(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.round-bomb.explosion" && e.WorldY > 2f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundMineStaysAtFeetWithoutMovingManki(bool target)
    {
        var sim = Match(); var before = sim.GetState(1);
        if (target) Target(sim, before.PX + 5f, .75f, before.PZ);
        Step(sim, new() { ActiveSlot = AbilitySlots.A, TargetEntityId = target ? (byte)100 : (byte)0 });
        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
        Assert.Equal(before.PX, bomb.X); Assert.Equal(before.PZ, bomb.Z);
        Assert.Equal(before.PY - Def.CapsuleHeight * .5f + bomb.Radius, bomb.Y, 4);
        Assert.Equal(MinePhase.Arming, bomb.MinePhase);
        Assert.Equal(before.PX, sim.GetState(1).PX); Assert.Equal(before.PY, sim.GetState(1).PY);
        Assert.Equal(before.PZ, sim.GetState(1).PZ);
        Assert.Equal(0f, sim.GetState(1).VY);
        Step(sim, new() { MoveX = 1 }, 8);
        Assert.Equal(before.PX, sim.GetState(1).PX);
    }

    [Fact]
    public void GroundPlacementRootsIncomingRunMomentum()
    {
        var sim = Match();
        sim.SetState(1, sim.GetState(1) with { VX = 8f, VZ = 4f });
        var before = sim.GetState(1);
        Step(sim, new() { ActiveSlot = AbilitySlots.A, MoveX = 1 });
        Assert.Equal(before.PX, sim.GetState(1).PX);
        Assert.Equal(before.PZ, sim.GetState(1).PZ);
        Assert.Equal(0f, sim.GetState(1).VX); Assert.Equal(0f, sim.GetState(1).VZ);
    }

    [Fact]
    public void ArmDelayThenWarningThenBlastNeverTriggersOrPushesOwner()
    {
        var sim = Match(); Step(sim, new() { ActiveSlot = AbilitySlots.A });
        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
        Step(sim, ticks: 40);
        Assert.Equal(MinePhase.Armed, Assert.Single(sim.Resolver.GetActiveHitboxes()).MinePhase);
        Assert.Equal((ushort)0, sim.GetState(1).DamagePercent);
        Target(sim, bomb.X + 1.5f, .75f, bomb.Z);
        Step(sim);
        Assert.Equal(MinePhase.Triggered, Assert.Single(sim.Resolver.GetActiveHitboxes()).MinePhase);
        Step(sim, ticks: 11);
        Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
        Step(sim, ticks: 3);
        Assert.True(sim.GetState(100).DamagePercent > 0);
        Assert.Equal((ushort)0, sim.GetState(1).DamagePercent);
        Assert.Equal(0f, sim.GetState(1).VY); Assert.Equal(0f, sim.GetState(1).VX); Assert.Equal(0f, sim.GetState(1).VZ);
    }

    [Fact]
    public void PointBlankOpponentCannotTriggerBeforeArming()
    {
        var sim = Match(); var owner = sim.GetState(1);
        Target(sim, owner.PX + .5f, .75f, owner.PZ);
        Step(sim, new() { ActiveSlot = AbilitySlots.A }, 29);
        Assert.Equal(MinePhase.Arming, Assert.Single(sim.Resolver.GetActiveHitboxes()).MinePhase);
        Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
        Step(sim);
        Assert.Equal(MinePhase.Triggered, Assert.Single(sim.Resolver.GetActiveHitboxes()).MinePhase);
    }

    [Fact]
    public void FourthLiveMineDiscardsOldestWithoutBlastAndLifetimeFizzles()
    {
        var sim = Match(); sim.NoCooldownsEntityId = 1;
        ulong oldest = 0;
        for (int i = 0; i < 4; i++)
        {
            if (i == 3) Step(sim, ticks: 320);
            Step(sim, new() { ActiveSlot = AbilitySlots.A }, 20);
            if (i == 0) oldest = Assert.Single(sim.Resolver.GetActiveHitboxes()).EmissionId;
        }
        Assert.Equal(3, sim.Resolver.GetActiveHitboxes().Count);
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.EmissionId == oldest);
        Step(sim, ticks: 601);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        Assert.DoesNotContain(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.round-bomb.explosion");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void KamikazeHitsTargetOrEndsFacingRunAndPushesOwnerWithoutStun(bool target, bool fromFlight)
    {
        var sim = Match(air: fromFlight);
        if (fromFlight) TakeOff(sim);
        var before = sim.GetState(1);
        if (target) Target(sim, before.PX, .75f, before.PZ + 6f, 80);
        Step(sim, new() { ActiveSlot = AbilitySlots.F, TargetEntityId = target ? (byte)100 : (byte)0 });
        Assert.False(sim.GetState(1).Flight.Active);
        if (fromFlight) Assert.True(sim.GetState(1).VY < 0);
        bool blast = false;
        for (int i = 0; i < 80; i++)
        {
            Step(sim);
            if (sim.GetPresentationEvents().Any(e => e.PresentationId == "presentation.manki.kamikaze.explosion"))
            {
                var owner = sim.GetState(1);
                Assert.Equal((ushort)0, owner.DamagePercent);
                Assert.Equal((ushort)0, owner.HitstunTicks);
                Assert.Equal((ushort)0, owner.HitstopTicks);
                Assert.True(owner.VY > 0f);
                Assert.Null(sim.GetActiveAbility(1));
                blast = true; break;
            }
        }
        Assert.True(blast);
        Step(sim, ticks: 2);
        if (target)
        {
            Assert.True(sim.GetState(100).DamagePercent > 80);
        }
        else Assert.True(sim.GetState(1).PZ > before.PZ + 10f);
    }

    [Theory]
    [InlineData(AbilitySlots.A)]
    [InlineData(AbilitySlots.R)]
    [InlineData(AbilitySlots.F)]
    public void FlyingBombAndKamikazeSnapshotRoundTripReplayExactly(byte slot)
    {
        var sim = Match(air: true); TakeOff(sim);
        Step(sim, new() { ActiveSlot = slot, MoveX = 1 }, 5);
        var packet = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(),
            Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>());
        var snapshot = WorldFramePacket.Deserialize(packet.Serialize()).Snapshot;
        Step(sim, new() { MoveY = -1, Jump = true }, 80); var expected = sim.CaptureSnapshot();
        sim.RestoreSnapshot(snapshot); Assert.True(snapshot.ContentEquals(sim.CaptureSnapshot()));
        Step(sim, new() { MoveY = -1, Jump = true }, 80);
        Assert.True(expected.ContentEquals(sim.CaptureSnapshot()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChargedRocketTracksCapturedTargetWithoutSteeringFlying(bool flying)
    {
        var sim = Match(air: flying);
        if (flying) TakeOff(sim);
        var owner = sim.GetState(1);
        Target(sim, owner.PX, owner.PY, owner.PZ + 12f);
        sim.NoGravityEntityId = 100;
        sim.RegisterEntity(200, TestHelpers.EngineDef, TestHelpers.NpcState(owner.PX + 12f, owner.PZ - 8f) with
        { PY = owner.PY, IsGrounded = !flying });
        Step(sim, new() { ActiveSlot = AbilitySlots.R, TargetEntityId = 100, MoveX = 1 });
        for (int i = 1; i < 40; i++)
        {
            owner = sim.GetState(1);
            sim.SetState(100, sim.GetState(100) with { PX = owner.PX - 8f, PY = owner.PY, PZ = owner.PZ + i * .1f });
            Step(sim, new() { TargetEntityId = 200, MoveX = 1 });
            owner = sim.GetState(1);
            var target = sim.GetState(100);
            Assert.Equal(MathF.Atan2(target.PX - owner.PX, target.PZ - owner.PZ), owner.FacingYaw, 4);
            Assert.Equal(100UL, owner.TargetEntityId);
            if (flying)
            {
                Assert.Equal(12f, owner.VX, 4);
                Assert.Equal(0f, owner.VZ, 4);
            }
            else
            {
                Assert.Equal(0f, owner.VX);
                Assert.Equal(0f, owner.VZ);
            }
        }
        Assert.Contains(sim.Resolver.GetActiveHitboxes(), h => h.HomingTargetEntityId == 100);
    }

    [Fact]
    public void KamikazeRootsForSixteenTicksThenLaunchesAtCapturedTarget()
    {
        var sim = Match();
        var before = sim.GetState(1);
        Target(sim, before.PX + 8f, before.PY, before.PZ);
        sim.RegisterEntity(200, TestHelpers.EngineDef, TestHelpers.NpcState(before.PX, before.PZ - 8f) with
        { PY = before.PY, IsGrounded = true });
        sim.SetState(1, before with { VX = 8f, VZ = 6f });
        Step(sim, new() { ActiveSlot = AbilitySlots.F, TargetEntityId = 100 });
        for (int elapsed = 1; elapsed <= 16; elapsed++)
        {
            var owner = sim.GetState(1);
            Assert.Equal(before.PX, owner.PX);
            Assert.Equal(before.PZ, owner.PZ);
            Assert.Equal(before.PY, owner.PY);
            Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
            Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
            if (elapsed < 16)
            {
                Assert.Equal(0f, owner.VX);
                Assert.Equal(0f, owner.VZ);
                Step(sim, new() { MoveY = -1, TargetEntityId = 200 });
            }
        }
        var launch = sim.GetState(1);
        Assert.Equal(18f, MathF.Sqrt(launch.VX * launch.VX + launch.VY * launch.VY + launch.VZ * launch.VZ), 4);
        Assert.True(launch.VX > 17f);
        Assert.Equal(0f, launch.VZ, 4);
        Step(sim);
        Assert.True(sim.GetState(1).PX > before.PX);
        Step(sim, ticks: 40);
        Assert.True(sim.GetState(100).DamagePercent > 0);
    }

    [Fact]
    public void KamikazeKeepsItsFullFiftyFourTickTravelAllowanceAfterStartup()
    {
        var sim = Match();
        Step(sim, new() { ActiveSlot = AbilitySlots.F }, 16);
        for (int i = 0; i < 53; i++)
        {
            Step(sim);
            Assert.NotNull(sim.GetActiveAbility(1));
            Assert.DoesNotContain(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.kamikaze.explosion");
        }
        Step(sim);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Single(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.kamikaze.explosion");
    }

    [Fact]
    public void KamikazeStartupFallsNormallyAndRollbackReplaysLaunch()
    {
        var sim = Match(air: true);
        var control = Match(air: true);
        sim.SetState(1, sim.GetState(1) with { VY = -3f });
        control.SetState(1, control.GetState(1) with { VY = -3f });
        Step(sim, new() { ActiveSlot = AbilitySlots.F }, 8);
        Step(control, ticks: 8);
        Assert.Equal(control.GetState(1).PY, sim.GetState(1).PY, 4);
        Assert.Equal(control.GetState(1).VY, sim.GetState(1).VY, 4);
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
        var packet = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(),
            Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>());
        var snapshot = WorldFramePacket.Deserialize(packet.Serialize()).Snapshot;
        Step(sim, ticks: 75);
        var expected = sim.CaptureSnapshot();
        sim.RestoreSnapshot(snapshot);
        Step(sim, ticks: 75);
        Assert.True(expected.ContentEquals(sim.CaptureSnapshot()));
    }

    [Fact]
    public void HitDuringKamikazeStartupCancelsWithoutTravelOrExplosion()
    {
        var sim = Match();
        Step(sim, new() { ActiveSlot = AbilitySlots.F }, 8);
        var owner = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 100, X = owner.PX, Y = owner.PY, Z = owner.PZ,
            Radius = 2f, Damage = 1f, BaseKnockback = 2f, StunTicks = 8, DurationTicks = 1 });
        Step(sim);
        Assert.Equal((ushort)1, sim.GetState(1).DamagePercent);
        for (int i = 0; i < 80; i++)
        {
            Step(sim);
            Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
            Assert.DoesNotContain(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.kamikaze.explosion");
        }
        Assert.Null(sim.GetActiveAbility(1));
        Assert.NotEqual(AbilitySlots.F, sim.GetState(1).AttackSlot);
    }

    [Fact]
    public void ThreeChargesAreIndependentAndFourthBombIsRejected()
    {
        var sim = Match();
        for (int i = 0; i < 3; i++) Step(sim, new() { ActiveSlot = AbilitySlots.A }, 32);
        var state = sim.GetState(1); Assert.Equal((byte)3, state.ChargeStockSpent);
        Step(sim, new() { ActiveSlot = AbilitySlots.A }); Assert.Null(sim.GetActiveAbility(1));
        Step(sim, ticks: state.ChargeStockRegenTicks3);
        Assert.Equal((byte)0, sim.GetState(1).ChargeStockSpent);
    }

    [Fact]
    public void KnockoutDiscardsBombsWithoutExplosion()
    {
        var sim = Match(); Step(sim, new() { ActiveSlot = AbilitySlots.A });
        sim.SetState(1, sim.GetState(1) with { PX = 2000f }); Step(sim, ticks: 65);
        Assert.DoesNotContain(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.round-bomb.explosion");
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
    }
    [Theory]
    [InlineData(AbilitySlots.A)]
    [InlineData(AbilitySlots.R)]
    public void HitDuringAnInflightSpecialAlsoEndsFlying(byte slot)
    {
        var sim = Match(air: true); TakeOff(sim);
        Step(sim, new() { ActiveSlot = slot });
        var owner = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 100, X = owner.PX, Y = owner.PY, Z = owner.PZ,
            Radius = 2, Damage = 1, StunTicks = 5, DurationTicks = 1 });
        Step(sim);
        Assert.False(sim.GetState(1).Flight.Active);
        Assert.Equal((ushort)600, sim.GetState(1).GetCooldown(AbilitySlots.E));
    }

    [Fact]
    public void EmptyFuelCannotBeReusedInAirEvenWithTrainingCooldownsDisabled()
    {
        var sim = Match(air: true); sim.NoCooldownsEntityId = 1; TakeOff(sim);
        var s = sim.GetState(1);
        sim.SetState(1, s with { PY = 50f, Flight = s.Flight with { FuelTicksLeft = 1 } });
        Step(sim, ticks: 2);
        Assert.False(sim.GetState(1).Flight.Active);
        Assert.Equal((ushort)0, sim.GetState(1).Flight.FuelTicksLeft);
        Step(sim, new() { ActiveSlot = AbilitySlots.E });
        Assert.Null(sim.GetActiveAbility(1));
        Assert.False(sim.GetState(1).Flight.Active);
    }

    [Fact]
    public void GroundedFuelCannotBypassCooldownFromTheFlightEnd()
    {
        var sim = Match(air: true); TakeOff(sim);
        Step(sim, new() { ActiveSlot = AbilitySlots.E });
        Assert.Equal((ushort)600, sim.GetState(1).GetCooldown(AbilitySlots.E));
        var s = sim.GetState(1);
        sim.SetState(1, s with { PY = Def.CapsuleHeight * .5f, VY = 0, IsGrounded = true });
        Step(sim);
        Assert.Equal((ushort)180, sim.GetState(1).Flight.FuelTicksLeft);
        Step(sim, new() { ActiveSlot = AbilitySlots.E }); Assert.Null(sim.GetActiveAbility(1));
        Step(sim, ticks: 600);
        Step(sim, new() { ActiveSlot = AbilitySlots.E }, 3);
        Assert.True(sim.GetState(1).Flight.Active);
    }

    [Fact]
    public void FallingWithoutFlightDropsAMineWithoutChangingVelocity()
    {
        var sim = Match(air: true); var control = Match(air: true);
        sim.SetState(1, sim.GetState(1) with { VY = -8f });
        control.SetState(1, control.GetState(1) with { VY = -8f });
        Step(sim, new() { ActiveSlot = AbilitySlots.A }); Step(control);
        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
        Assert.Equal(9f, bomb.Gravity);
        Assert.Equal(control.GetState(1).VY, sim.GetState(1).VY);
        Assert.Equal(control.GetState(1).PY, sim.GetState(1).PY);
        var snapshot = sim.CaptureSnapshot();
        Step(sim, ticks: 100); var expected = sim.CaptureSnapshot();
        sim.RestoreSnapshot(snapshot); Step(sim, ticks: 100);
        Assert.True(expected.ContentEquals(sim.CaptureSnapshot()));
        Assert.Equal(MinePhase.Armed, Assert.Single(sim.Resolver.GetActiveHitboxes()).MinePhase);
    }

    [Theory]
    [InlineData(10, MinePhase.Arming)]
    [InlineData(35, MinePhase.Armed)]
    [InlineData(45, MinePhase.Triggered)]
    public void MineRollbackRestoresArmingArmedAndTriggered(int snapshotTick, MinePhase phase)
    {
        var sim = Match(); Step(sim, new() { ActiveSlot = AbilitySlots.A });
        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes());
        for (int i = 1; i < snapshotTick; i++)
        {
            if (i == 40) Target(sim, bomb.X + 1.5f, .75f, bomb.Z);
            Step(sim);
        }
        Assert.Equal(phase, Assert.Single(sim.Resolver.GetActiveHitboxes()).MinePhase);
        var snapshot = sim.CaptureSnapshot();
        Step(sim, ticks: 80); var expected = sim.CaptureSnapshot();
        sim.RestoreSnapshot(snapshot); Step(sim, ticks: 80);
        Assert.True(expected.ContentEquals(sim.CaptureSnapshot()));
    }

    [Theory]
    [InlineData(FriendlyFireMode.Off, false)]
    [InlineData(FriendlyFireMode.On, true)]
    public void BombBlastUsesTheModesFriendlyFireRule(FriendlyFireMode friendlyFire, bool hurts)
    {
        var sim = new ServerSimulation(SlopballTests.Court(), new SlopballCoopMatchRule(friendlyFire: friendlyFire));
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState(-3f, -3f) with { PY = Def.CapsuleHeight * .5f });
        sim.BeginSlopballRun(1337);
        Target(sim, -1.5f, .75f, -3f);
        Step(sim, new() { ActiveSlot = AbilitySlots.A }, 65);
        Assert.Equal(hurts, sim.GetState(100).DamagePercent > 0);
        Assert.Equal((ushort)0, sim.GetState(1).DamagePercent);
    }

    [Fact]
    public void PersistedPackageRunsTheCompleteBomberKit()
    {
        var sim = Match(air: true, persisted: true); TakeOff(sim);
        Step(sim, new() { ActiveSlot = AbilitySlots.A }, 32);
        Assert.True(sim.GetState(1).Flight.Active);
        Step(sim, new() { ActiveSlot = AbilitySlots.R }, 80);
        Assert.True(sim.GetState(1).Flight.Active);
        var s = sim.GetState(1);
        Target(sim, s.PX, .75f, s.PZ + 5f, 80);
        Step(sim, new() { ActiveSlot = AbilitySlots.F, TargetEntityId = 100 }, 45);
        Assert.False(sim.GetState(1).Flight.Active);
        Assert.True(sim.GetState(100).DamagePercent > 80);
        Assert.Equal((ushort)0, sim.GetState(1).DamagePercent);
    }

    [Fact]
    public void FallingBombHitsARaisedTrianglePlatformBeforeTheHeightmap()
    {
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = 18, AY = 3, AZ = 18, BX = 18, BY = 3, BZ = 22, CX = 22, CY = 3, CZ = 18 },
            new CollisionTriangle { AX = 22, AY = 3, AZ = 22, BX = 22, BY = 3, BZ = 18, CX = 18, CY = 3, CZ = 22 },
        };
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox { OwnerId = 1, X = 20, Y = 4.5f, Z = 20, Radius = .35f,
            VY = -60, Gravity = 30, DurationTicks = 90, ContactExplosion = true,
            Explosion = new ProjectileExplosion { Radius = 3, Damage = 10 } });
        for (int i = 0; i < 3; i++)
        {
            resolver.Tick(new());
            resolver.CheckGroundCollision(arena);
        }
        var blast = Assert.Single(resolver.DrainPendingExplosions());
        Assert.InRange(blast.y, 2.99f, 3.01f);
    }

    [Theory]
    [InlineData(4.5f, 61)]
    // Highest live Manki feet to the lowest floor across baked arenas (wibou_mall).
    [InlineData(39.55f, 179)]
    public void SlowDropReachesFloorBeforeItsFlightLifetime(float height, int expectedTicks)
    {
        var sim = Match(air: true); TakeOff(sim);
        Step(sim, new() { ActiveSlot = AbilitySlots.A });
        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
        Assert.Equal(9f, bomb.Gravity);
        var resolver = new SpellResolver();
        bomb.Y = height + bomb.Radius; bomb.VY = 0;
        resolver.Spawn(bomb);
        int elapsed = 0;
        while (resolver.GetActiveHitboxes().Any(h => h.MinePhase == MinePhase.Falling) && elapsed < bomb.DurationTicks)
        {
            resolver.Tick(new()); resolver.CheckGroundCollision(TestHelpers.TestArena()); elapsed++;
        }
        Assert.Equal(expectedTicks, elapsed);
        Assert.True(elapsed < bomb.DurationTicks);
        Assert.Equal(bomb.Radius, Assert.Single(resolver.GetActiveHitboxes()).Y, 3);
        Assert.Empty(resolver.DrainPendingExplosions());
    }

    [Theory]
    [InlineData(.65f, true)]
    [InlineData(.8f, false)]
    public void LargerContactBodyTriggersAtItsNewBoundary(float distance, bool touches)
    {
        var sim = Match(); Step(sim, new() { ActiveSlot = AbilitySlots.A });
        var bomb = Assert.Single(sim.Resolver.GetActiveHitboxes(), h => h.ContactExplosion);
        var resolver = new SpellResolver(); bomb.MinePhase = MinePhase.Falling; resolver.Spawn(bomb);
        resolver.Tick(new() { new SpellResolver.EntityData { Id = 100, PosX = bomb.X + distance,
            PosY = bomb.Y, PosZ = bomb.Z, Radius = .1f, Shape = HitboxShape.Sphere, Active = true } });
        Assert.Equal(touches, resolver.DrainPendingExplosions().Count == 1);
    }

    [Fact]
    public void ProjectionIgnoresDownwardFacesAndDoesNotProjectOntoAPlatformAboveTheBomb()
    {
        var arena = TestHelpers.TestArena();
        Array.Fill(arena.Heightmap.Data, float.MinValue);
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = 18, AY = 3, AZ = 18, BX = 18, BY = 3, BZ = 22, CX = 22, CY = 3, CZ = 18 },
            new CollisionTriangle { AX = 18, AY = 0, AZ = 18, BX = 22, BY = 0, BZ = 18, CX = 18, CY = 0, CZ = 22 },
        };
        Assert.False(SpellResolver.TryProjectStraightDrop(arena, 19, 2, 19, .6f,
            new int[arena.CollisionTriangles.Length], out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectedLandingMatchesActualBombExplosionOnFlatFloorOrRaisedPlatform(bool platform)
    {
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = platform ? new[]
        {
            new CollisionTriangle { AX = 18, AY = 3, AZ = 18, BX = 18, BY = 3, BZ = 22, CX = 22, CY = 3, CZ = 18 },
            new CollisionTriangle { AX = 22, AY = 3, AZ = 22, BX = 22, BY = 3, BZ = 18, CX = 18, CY = 3, CZ = 22 },
        } : Array.Empty<CollisionTriangle>();
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox { OwnerId = 1, X = 20, Y = 5.1f, Z = 20, Radius = .6f,
            Gravity = 9, DurationTicks = 180, ContactExplosion = true,
            Explosion = new ProjectileExplosion { Radius = 4, Damage = 10 } });
        var before = Assert.Single(resolver.GetActiveHitboxes());
        Assert.True(SpellResolver.TryProjectStraightDrop(arena, before.X, before.Y, before.Z, before.Radius,
            new int[arena.CollisionTriangles.Length], out float landing));
        Assert.Equal(platform ? 3f : 0f, landing, 3);
        Assert.Equal(before, Assert.Single(resolver.GetActiveHitboxes()));
        Assert.Empty(resolver.DrainPendingExplosions());
        for (int i = 0; i < 90; i++) { resolver.Tick(new()); resolver.CheckGroundCollision(arena); }
        var explosion = Assert.Single(resolver.DrainPendingExplosions());
        Assert.Equal(landing, explosion.y, 3);
        Assert.Equal(before.X, explosion.x); Assert.Equal(before.Z, explosion.z);
    }

    [Fact]
    public void ProjectionAndBombFindNoFloorOverAPit()
    {
        var arena = TestHelpers.TestArena();
        Array.Fill(arena.Heightmap.Data, float.MinValue);
        Assert.False(SpellResolver.TryProjectStraightDrop(arena, 20, 5, 20, .6f, Array.Empty<int>(), out _));
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox { OwnerId = 1, X = 20, Y = 5, Z = 20, Radius = .6f,
            Gravity = 9, DurationTicks = 180, ContactExplosion = true,
            Explosion = new ProjectileExplosion { Radius = 4, Damage = 10 } });
        for (int i = 0; i < 180; i++) { resolver.Tick(new()); resolver.CheckGroundCollision(arena); }
        Assert.Empty(resolver.GetActiveHitboxes()); Assert.Empty(resolver.DrainPendingExplosions());
    }

    [Fact]
    public void KamikazeIsAStrongLaunchAtKillPercent()
    {
        var sim = Match(killMaxZ: 35f); Target(sim, 20, .75f, 26, 100);
        Step(sim, new() { ActiveSlot = AbilitySlots.F, TargetEntityId = 100 });
        float force = 0;
        for (int i = 0; i < 180; i++)
        {
            Step(sim);
            foreach (var hit in sim.LastTickHits)
                if (hit.TargetEntityId == 100) force = MathF.Max(force, hit.ImpactForce);
        }
        Assert.True(force > 25f, $"Expected a finisher launch, got {force}.");
        Assert.True(sim.GetState(100).Deaths > 0);
        Assert.Equal((ushort)0, sim.GetState(1).DamagePercent);
    }

    [Fact]
    public void AuthoringRenameClonesKamikazeAndUpdatesItsExplosionBinding()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        const string relative = "client/Unity/Assets/CharacterPackages/manki";
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        Assert.NotNull(root);
        string path = Path.Combine(root!.FullName, relative);
        var source = CharacterPackageSourceCodec.Load(File.ReadAllText(Path.Combine(path, "package.json")),
            File.ReadAllText(Path.Combine(path, "character.json"))).Source!;
        const string previous = "presentation.manki.kamikaze.explosion", next = "presentation.manki.bomber.explosion";
        var renamed = CharacterPackageSourceCodec.RenameSemanticId(source, previous, next,
            new[] { new CharacterAssetCatalogBindingSnapshot(previous, previous) });
        Assert.True(renamed.IsValid);
        var compiled = CharacterPackageCompiler.Compile(renamed.Source!, CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(compiled.CookedPackage);
        var f = compiled.CookedPackage!.Definition.Slots.Single(s => s.Id == "ground.F");
        var parameters = Assert.IsType<CookedMankiKamikazeCapabilityParameters>(f.Timeline.Stages[0].Operations
            .OfType<CookedStartCapabilityOperation>().Single().Parameters);
        Assert.Equal(next, parameters.ExplosionPresentationId);
    }

}
