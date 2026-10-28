using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// slop.ability.tether.v1 (ADR-0048), Bashie's Rescue Ring, through her live package: the ring flies straight at her
/// target, the first fighter it meets is reeled to her feet and stuck for one normal, a miss reels the empty ring back,
/// and every way the line can end converges on free fighters. Edge cases edit a copy of her source.
/// </summary>
public sealed class TetherTests
{
    private const ulong Bashie = RingDuel.Bashie, Dummy = RingDuel.Dummy, Third = RingDuel.Third;
    private static readonly InputState Throw = new() { ActiveSlot = AbilitySlots.A };
    private static CookedTetherCapabilityParameters P => Parameters;

    /// <summary>Her live tether, also the snapshot contract's capability fixture.</summary>
    internal static CookedTetherCapabilityParameters Parameters => RingDuel.TetherOf(RingDuel.Her.Def);

    [Fact]
    public void TheRingFliesStraightAtWhereHerTargetIsWhenSheThrows()
    {
        var duel = new RingDuel().WithBashie().WithFighter(Dummy, RingDuel.Standing(1f, 6f));
        var run = new InputState { MoveX = 1f };
        duel.Step(Throw with { TargetEntityId = (byte)Dummy }, (Dummy, run));
        while (!duel.RingOut) duel.Step(Aim(Dummy), (Dummy, run));
        var ring = duel.Ring;
        var target = duel.State(Dummy);
        Assert.True(target.PX > 1.05f, "the target ran sideways during the wind-up");
        float aimY = target.PY + TestHelpers.EngineDef.CapsuleHeight * .25f;
        AssertParallel(target.PX - ring.X, aimY - ring.Y, target.PZ - ring.Z, ring.VX, ring.VY, ring.VZ);
        Assert.Equal(P.Projectile.Speed, MathF.Sqrt(ring.VX * ring.VX + ring.VY * ring.VY + ring.VZ * ring.VZ), 3);

        var velocity = (ring.VX, ring.VY, ring.VZ);
        duel.Step(Aim(Dummy), (Dummy, new InputState { MoveX = -1f }));
        for (int tick = 0; tick < 6 && duel.RingOut; tick++)
        {
            Assert.Equal(velocity, (duel.Ring.VX, duel.Ring.VY, duel.Ring.VZ));
            duel.Step(Aim(Dummy), (Dummy, new InputState { MoveX = -1f }));
        }
    }

    [Fact]
    public void WithNoTargetTheRingFliesLevelAlongHerFacing()
    {
        var duel = new RingDuel().WithBashie(RingDuel.Standing(0f, 0f, yawDegrees: 90f))
            .WithFighter(Dummy, RingDuel.Standing(0f, -30f));
        duel.Step(Throw);
        while (!duel.RingOut) duel.Step();
        var ring = duel.Ring;
        Assert.Equal(P.Projectile.Speed, ring.VX, 3);
        Assert.Equal(0f, ring.VY);
        Assert.Equal(0f, ring.VZ, 3);
    }

    [Fact]
    public void ACatchPairsBothFightersAndReelsTheVictimToHerFeet()
    {
        var duel = new RingDuel().WithBashie().WithFighter(Dummy, RingDuel.Standing(0f, 5f, yawDegrees: 90f));
        duel.Step(Throw);
        duel.RunUntil(() => duel.State(Dummy).State == ActionState.Reeled, 60);
        var her = duel.State(Bashie);
        var victim = duel.State(Dummy);
        Assert.Equal(ActionState.Attacking, her.State);
        Assert.NotEqual(0ul, her.InteractionId);
        Assert.Equal(her.InteractionId, victim.InteractionId);
        Assert.Equal(Dummy, her.InteractionPartnerId);
        Assert.Equal(Bashie, victim.InteractionPartnerId);
        Assert.Equal((byte)DefenseInteractionPhase.Reeling, her.InteractionPhase);
        Assert.Equal((byte)DefenseInteractionPhase.Reeling, victim.InteractionPhase);
        var caught = Assert.Single(duel.Hits, h => h.OwnerEntityId == Bashie && h.TargetEntityId == Dummy);
        Assert.False(caught.Blocked);
        Assert.Equal((ushort)P.Projectile.Damage, victim.DamagePercent);
        Assert.Equal((ushort)0, victim.HitstopTicks);
        Assert.Equal((ushort)0, victim.HitstunTicks);
        Assert.False(duel.RingOut);

        duel.RunUntil(() => duel.State(Bashie).State != ActionState.Attacking, 120);
        her = duel.State(Bashie);
        victim = duel.State(Dummy);
        Assert.Null(duel.Sim.GetActiveAbility(Bashie));
        Assert.Equal((ushort)0, her.AnimLockTicks);
        Assert.Equal(ActionState.Reeled, victim.State);
        Assert.Equal(0ul, victim.InteractionId);
        Assert.Equal(0ul, her.InteractionId);
        Assert.Equal(P.ArrivalStunTicks, victim.StateTicks);
        float gap = MathF.Max(P.AnchorDistance, 0.3f + 0.3f + 0.05f);
        Assert.InRange(victim.PZ - her.PZ, gap - .06f, gap + .06f);
        Assert.InRange(victim.PX - her.PX, -.06f, .06f);
        Assert.InRange(victim.PY - her.PY, -.01f, .01f);
        Assert.InRange(AngleBetween(victim.FacingYaw, MathF.PI), 0f, .01f);

        duel.Step(new InputState { ActiveSlot = AbilitySlots.Slot1 });
        Assert.Equal(AbilitySlots.Slot1, duel.State(Bashie).AttackSlot);
    }

    [Theory]
    [InlineData(5f)]
    [InlineData(-30f)]
    public void TheReelAndTheEmptyReelPlayTheirPhaseClips(float dummyZ)
    {
        var duel = new RingDuel().WithBashie().WithFighter(Dummy, RingDuel.Standing(0f, dummyZ));
        duel.Step(Throw);
        var seen = new HashSet<string>();
        for (int tick = 0; tick < 120 && duel.Sim.GetActiveMove(Bashie) is { } move; tick++)
        {
            seen.Add(move.Slot.Clip(duel.State(Bashie).AnimIndex).AnimationId);
            duel.Step();
        }
        Assert.Contains("anim.bashie.ring", seen);
        Assert.Contains(dummyZ > 0f ? P.ReelAnimationId : P.RetractAnimationId, seen);
        Assert.DoesNotContain(dummyZ > 0f ? P.RetractAnimationId : P.ReelAnimationId, seen);
    }

    [Fact]
    public void TheArrivalWindowHoldsTheVictimInPlaceAndAHeldShieldRisesOnItsFirstFreeTick()
    {
        var duel = Arrived(out _);
        var victim = duel.State(Dummy);
        var hold = new InputState { ShieldHeld = true, MoveX = 1f, Jump = true };
        for (int tick = 1; tick < P.ArrivalStunTicks; tick++)
        {
            duel.Step(default, (Dummy, hold));
            var now = duel.State(Dummy);
            Assert.Equal(ActionState.Reeled, now.State);
            Assert.Equal(victim.PX, now.PX);
            Assert.Equal(victim.PZ, now.PZ);
        }
        duel.Step(default, (Dummy, hold with { Jump = false }));
        var free = duel.State(Dummy);
        Assert.Equal(ActionState.Shielding, free.State);
        Assert.Equal(victim.PX, free.PX);
        Assert.Equal(victim.PZ, free.PZ);
    }

    [Fact]
    public void HerJabStartedOnHerFirstFreeTickConnectsThroughAHeldShield()
    {
        var duel = Arrived(out _, victimShields: true);
        var shield = new InputState { ShieldHeld = true };
        duel.Step(new InputState { ActiveSlot = AbilitySlots.Slot1 }, (Dummy, shield));
        duel.RunUntil(() => duel.Hits.Any(h => h.OwnerEntityId == Bashie && h.AttackSlot == AbilitySlots.Slot1), 60,
            (Dummy, shield));
        var jab = duel.Hits.First(h => h.OwnerEntityId == Bashie && h.AttackSlot == AbilitySlots.Slot1);
        Assert.False(jab.Blocked);
        Assert.Equal(Dummy, jab.TargetEntityId);
        duel.RunUntil(() => duel.State(Dummy).State == ActionState.Hitstun, 30, (Dummy, shield));
    }

    [Theory]
    [InlineData(AbilitySlots.Slot3)]
    [InlineData(AbilitySlots.Slot4)]
    public void HerSlowerNormalsStartedOnHerFirstFreeTickAreShielded(byte slot)
    {
        var duel = Arrived(out _, victimShields: true);
        var shield = new InputState { ShieldHeld = true };
        duel.Step(new InputState { ActiveSlot = slot }, (Dummy, shield));
        duel.RunUntil(() => duel.Hits.Any(h => h.OwnerEntityId == Bashie && h.AttackSlot == slot), 60, (Dummy, shield));
        Assert.True(duel.Hits.First(h => h.OwnerEntityId == Bashie && h.AttackSlot == slot).Blocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARangeMissReelsTheEmptyRingBackAndLocksHerUntilItIsHome(bool holdShield)
    {
        var duel = new RingDuel().WithBashie().WithFighter(Dummy, RingDuel.Standing(0f, -30f));
        duel.Step(Throw);
        duel.RunUntil(() => duel.Retracts.Any(), 90);
        var retract = Assert.Single(duel.Retracts);
        float range = P.Projectile.Speed * P.Projectile.MaxFlightTicks / 60f;
        Assert.InRange(retract.WorldZ, P.Projectile.LaunchOffsetZ + range - .01f, P.Projectile.LaunchOffsetZ + range + .01f);
        ushort home = (ushort)Math.Ceiling(range / (P.RetractSpeed / 60.0) - 1e-3);
        Assert.Equal(home, retract.Placement.DurationTicks);
        Assert.False(duel.RingOut, "the empty ring has no body on the way back");

        uint free = retract.MatchTick + home + P.MissRecoveryTicks + 1;
        var press = holdShield ? new InputState { ShieldHeld = true } : new InputState { ActiveSlot = AbilitySlots.Slot1 };
        while (duel.Sim.CurrentTick < free - 2)
        {
            duel.Step(press);
            Assert.Equal(ActionState.Attacking, duel.State(Bashie).State);
            Assert.Equal(AbilitySlots.A, duel.State(Bashie).AttackSlot);
        }
        duel.Step(press);
        Assert.Equal(ActionState.Idle, duel.State(Bashie).State);
        duel.Step(press);
        if (holdShield) Assert.Equal(ActionState.Shielding, duel.State(Bashie).State);
        else Assert.Equal(AbilitySlots.Slot1, duel.State(Bashie).AttackSlot);
    }

    [Fact]
    public void TheRingStopsAtAWallAndReelsBackFromThere()
    {
        var duel = new RingDuel(BashieShellTests.Floor(wallZ: 4f)).WithBashie().WithFighter(Dummy, RingDuel.Standing(0f, 7f));
        duel.Step(Throw);
        duel.RunUntil(() => duel.Retracts.Any(), 90, aim: Dummy);
        Assert.InRange(Assert.Single(duel.Retracts).WorldZ, 3f, 4f - P.Projectile.Radius + .01f);
        Assert.DoesNotContain(duel.Hits, h => h.OwnerEntityId == Bashie);
        Assert.Equal(ActionState.Idle, duel.State(Dummy).State);
    }

    [Fact]
    public void AShieldBouncesTheRing()
    {
        var duel = new RingDuel().WithBashie().WithFighter(Dummy, RingDuel.Standing(0f, 4f, yawDegrees: 180f));
        var shield = new InputState { ShieldHeld = true };
        duel.Step(Throw, (Dummy, shield));
        duel.RunUntil(() => duel.Retracts.Any(), 90, (Dummy, shield));
        var blocked = Assert.Single(duel.Hits, h => h.OwnerEntityId == Bashie);
        Assert.True(blocked.Blocked);
        Assert.True(Assert.Single(duel.Retracts).MatchTick > blocked.MatchTick);
        Assert.Equal(ActionState.Shielding, duel.State(Dummy).State);
        Assert.Equal(0ul, duel.State(Bashie).InteractionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFirstFighterTheRingMeetsIsCaughtUnlessInvincible(bool nearInvincible)
    {
        var near = RingDuel.Standing(0f, 3f);
        if (nearInvincible) near.InvincibilityTicks = 120;
        var duel = new RingDuel().WithBashie().WithFighter(Third, near).WithFighter(Dummy, RingDuel.Standing(0f, 6f));
        duel.Step(Throw with { TargetEntityId = (byte)Dummy });
        duel.RunUntil(() => duel.State(Bashie).InteractionId != 0 || duel.Retracts.Any(), 90, aim: Dummy);
        ulong caught = nearInvincible ? Dummy : Third, spared = nearInvincible ? Third : Dummy;
        Assert.Equal(ActionState.Reeled, duel.State(caught).State);
        Assert.Equal(caught, duel.State(Bashie).InteractionPartnerId);
        Assert.NotEqual(ActionState.Reeled, duel.State(spared).State);
        Assert.DoesNotContain(duel.Hits, h => h.TargetEntityId == spared);
    }

    [Fact]
    public void AnAirThrowLiftsAnOffstageVictimOverTheLipToHerFeet()
    {
        const float lip = 3f;
        var cliff = CourseShellArena.FromBoxes(new[]
        {
            (-60f, -10f, -60f, 60f, 0f, lip),
            (-60f, -11f, lip, 60f, -10f, 60f),
        }, new SpawnPoint { X = 0f, Y = 1f, Z = 0f });
        var her = RingDuel.Standing(0f, 2f);
        her.PY += 1.5f;
        her.IsGrounded = false;
        her.AirTimeTicks = RingDuel.Her.Def.Movement.FloatWindowTicks;
        var victim = RingDuel.Standing(0f, 4.2f);
        victim.PY -= 2.5f;
        victim.IsGrounded = false;
        var duel = new RingDuel(cliff).WithBashie(her).WithFighter(Dummy, victim);
        duel.Sim.NoGravityEntityId = Dummy;
        duel.Step(Throw);
        duel.RunUntil(() => duel.State(Dummy).State == ActionState.Reeled, 60, aim: Dummy);
        Assert.False(duel.State(Bashie).IsGrounded);
        duel.Sim.NoGravityEntityId = 0;
        float radius = TestHelpers.EngineDef.CapsuleRadius, half = TestHelpers.EngineDef.CapsuleHeight * .5f;
        duel.RunUntil(() =>
        {
            var now = duel.State(Dummy);
            if (now.PY - half < -.01f)
                Assert.True(now.PZ >= lip + radius - .01f, "below the lip it stays outside the cliff face");
            return now.InteractionId == 0;
        }, 90);
        var arrived = duel.State(Dummy);
        Assert.Equal(ActionState.Reeled, arrived.State);
        Assert.Equal(P.ArrivalStunTicks, arrived.StateTicks);
        Assert.True(arrived.PZ < lip, "delivered onto the stage side");
        Assert.True(arrived.PY - half >= -.01f, "lifted over the lip");
        Assert.False(arrived.IsGrounded);
        duel.Step();
        Assert.True(duel.State(Dummy).VY < 0f, "delivered in the air, it falls at once");
        duel.RunUntil(() => duel.State(Dummy).State != ActionState.Reeled && duel.State(Dummy).IsGrounded, 120);
        Assert.InRange(duel.State(Dummy).PY, half - .01f, half + .01f);
        Assert.True(duel.State(Dummy).PZ < lip);
    }

    [Fact]
    public void AHeadOnWallSnapsTheLineAndSheReelsBackEmpty()
    {
        var arena = CourseShellArena.FromBoxes(new[]
        {
            (-60f, -1f, -60f, 60f, 0f, 60f),
            (-60f, 0f, 3f, 60f, 0.4f, 3.5f),
        }, new SpawnPoint { X = 0f, Y = 1f, Z = 0f });
        var duel = new RingDuel(arena).WithBashie().WithFighter(Dummy, RingDuel.Standing(0f, 6f));
        duel.Step(Throw);
        duel.RunUntil(() => duel.State(Dummy).State == ActionState.Reeled, 60, aim: Dummy);
        duel.RunUntil(() => duel.State(Bashie).InteractionId == 0, 60);
        var victim = duel.State(Dummy);
        Assert.Equal(ActionState.Idle, victim.State);
        Assert.Equal(0ul, victim.InteractionId);
        Assert.True(victim.PZ >= 3.5f + 0.3f - .01f, "the wall stopped it");
        duel.Step();
        var retract = Assert.Single(duel.Retracts);
        Assert.InRange(retract.WorldZ, victim.PZ - .3f, victim.PZ + .3f);
        Assert.Equal(ActionState.Attacking, duel.State(Bashie).State);
    }

    [Fact]
    public void RunningOutOfReelTimeSnapsTheLine()
    {
        var slow = RingDuel.Load(c => EditTether(c, p => { p["reelSpeed"] = 2; p["maxReelTicks"] = 10; }));
        var duel = new RingDuel().WithBashie(def: slow).WithFighter(Dummy, RingDuel.Standing(0f, 5f));
        duel.Step(Throw);
        duel.RunUntil(() => duel.State(Dummy).State == ActionState.Reeled, 60);
        uint caught = duel.State(Dummy).InteractionTick;
        while (duel.Sim.CurrentTick < caught + 10)
        {
            duel.Step();
            Assert.Equal(ActionState.Reeled, duel.State(Dummy).State);
        }
        duel.Step();
        Assert.Equal(ActionState.Idle, duel.State(Dummy).State);
        Assert.Equal(0ul, duel.State(Dummy).InteractionId);
        Assert.Single(duel.Retracts);
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(0f)]
    public void HittingHerMidReelDropsTheLineAndTheVictimGoesFreeWithoutStun(float damage)
    {
        var duel = Reeling();
        var before = duel.State(Dummy);
        duel.HitFrom(Third, Bashie, damage);
        duel.Step();
        duel.Step();
        var victim = duel.State(Dummy);
        Assert.NotEqual(ActionState.Reeled, victim.State);
        Assert.Equal(0ul, victim.InteractionId);
        Assert.Equal((ushort)0, victim.HitstunTicks);
        Assert.Equal(before.DamagePercent, victim.DamagePercent);
        Assert.True(victim.PZ > 1.2f, "freed where it was, not delivered");
        Assert.Contains(duel.Hits, h => h.OwnerEntityId == Third && h.TargetEntityId == Bashie);
        duel.RunUntil(() => duel.Sim.GetActiveAbility(Bashie) == null, 60);
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(0f)]
    public void AThirdFighterHittingTheVictimBreaksTheLineAndSheReelsBackEmpty(float damage)
    {
        var duel = Reeling();
        duel.HitFrom(Third, Dummy, damage);
        duel.Step();
        duel.Step();
        var victim = duel.State(Dummy);
        var hit = Assert.Single(duel.Hits, h => h.OwnerEntityId == Third);
        Assert.Equal(Dummy, hit.TargetEntityId);
        Assert.Equal(0ul, victim.InteractionId);
        Assert.NotEqual(ActionState.Reeled, victim.State);
        Assert.True(victim.HitstopTicks > 0 || victim.HitstunTicks > 0 || victim.State == ActionState.Hitstun,
            "the hit's own reaction stands");
        Assert.Single(duel.Retracts);
        Assert.Equal(ActionState.Attacking, duel.State(Bashie).State);
    }

    [Fact]
    public void AVictimKnockedOutMidReelLeavesHerReelingBackEmpty()
    {
        var duel = Reeling();
        duel.Sim.SetState(Dummy, duel.State(Dummy) with { PY = -100f });
        duel.Step();
        duel.Step();
        Assert.Single(duel.Retracts);
        Assert.Equal(0ul, duel.State(Bashie).InteractionId);
        Assert.NotEqual(ActionState.Reeled, duel.State(Dummy).State);
    }

    [Fact]
    public void ReciprocalCatchesInOneBatchClashAndBothRingsComeBackEmpty()
    {
        var other = RingDuel.Standing(0f, 5f, yawDegrees: 180f);
        var duel = new RingDuel().WithBashie().WithBashie(other, Dummy);
        duel.Step(Throw, (Dummy, Throw));
        duel.RunUntil(() => duel.Retracts.Count() == 2, 90);
        Assert.Equal(2, duel.Events.Count(e => e.Source == PresentationEventSource.TetherRetract));
        Assert.DoesNotContain(duel.Hits, h => h.OwnerEntityId == Bashie || h.OwnerEntityId == Dummy);
        foreach (var id in new[] { Bashie, Dummy })
        {
            Assert.NotEqual(ActionState.Reeled, duel.State(id).State);
            Assert.Equal(0ul, duel.State(id).InteractionId);
        }
    }

    [Fact]
    public void TheEarlierCatchWinsAndTakesTheOtherRingWithItsMove()
    {
        var other = RingDuel.Standing(0f, 5f, yawDegrees: 180f);
        var duel = new RingDuel().WithBashie().WithBashie(other, Dummy);
        duel.Step(Throw);
        duel.Step(default, (Dummy, Throw));
        duel.Step();
        duel.Step();
        duel.RunUntil(() => duel.State(Dummy).State == ActionState.Reeled, 60);
        Assert.DoesNotContain(duel.Sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == Dummy);
        Assert.Null(duel.Sim.GetActiveAbility(Dummy));
        Assert.Equal(ActionState.Attacking, duel.State(Bashie).State);
        Assert.Equal(Dummy, duel.State(Bashie).InteractionPartnerId);
    }

    [Fact]
    public void AGrabbedFighterBouncesTheRing()
    {
        var grabber = RingDuel.Standing(0f, 5.7f, yawDegrees: 180f);
        var duel = new RingDuel().WithBashie().WithFighter(Dummy, RingDuel.Standing(0f, 5f))
            .WithBashie(grabber, Third);
        duel.Step(default, (Third, new InputState { GrabPressed = true }));
        duel.RunUntil(() => duel.State(Dummy).State == ActionState.Grabbed, 30);
        duel.Step(Throw);
        duel.RunUntil(() => duel.Retracts.Any(), 90, aim: Dummy);
        Assert.InRange(Assert.Single(duel.Retracts).WorldZ, 4f, 5.5f);
        Assert.DoesNotContain(duel.Hits, h => h.OwnerEntityId == Bashie);
        Assert.Equal(ActionState.Grabbed, duel.State(Dummy).State);
        Assert.Equal(Third, duel.State(Dummy).InteractionPartnerId);
        Assert.Equal(0ul, duel.State(Bashie).InteractionId);
    }

    [Fact]
    public void SheCannotBeGrabbedWhileSheReels()
    {
        var duel = Reeling(thirdIsBashie: true, thirdAt: (0f, 0f, -0.7f));
        duel.Step(default, (Third, new InputState { GrabPressed = true }));
        for (int tick = 0; tick < 12; tick++) duel.Step();
        Assert.NotEqual(ActionState.Grabbed, duel.State(Bashie).State);
        Assert.NotEqual(ActionState.Grabbing, duel.State(Third).State);
    }

    [Fact]
    public void ArmourDoesNotResistTheCatch()
    {
        var bonk = BuiltInContentResolver.Resolve(CharacterClass.Bonk);
        var duel = new RingDuel().WithBashie()
            .WithFighter(Dummy, RingDuel.Standing(0f, 9f, yawDegrees: 180f), bonk.Definition, bonk.BakedAnimation);
        duel.Step(default, (Dummy, new InputState { ActiveSlot = AbilitySlots.R }));
        duel.RunUntil(() => duel.Sim.GetActiveAbility(Dummy)?.HasArmor == true, 60);
        duel.Step(Throw);
        bool armouredWhenCaught = false;
        duel.RunUntil(() =>
        {
            if (duel.Hits.Any(h => h.OwnerEntityId == Bashie)) return true;
            armouredWhenCaught = duel.Sim.GetActiveAbility(Dummy)?.HasArmor == true;
            return false;
        }, 60, aim: Dummy);
        Assert.True(armouredWhenCaught, "the charge was armoured as the ring arrived");
        Assert.Equal(ActionState.Reeled, duel.State(Dummy).State);
        Assert.Null(duel.Sim.GetActiveAbility(Dummy));
    }

    [Fact]
    public void FromTheShellSheDoesNotTuckSheThrows()
    {
        var sim = BashieShellTests.Sim(BashieShellTests.Standing(), BashieShellTests.Dummy2(0f, 40f));
        BashieShellTests.Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        BashieShellTests.SkateOn(sim);
        BashieShellTests.Step(sim, Throw);
        var state = BashieShellTests.State(sim);
        Assert.Equal(ActionState.Attacking, state.State);
        Assert.False(Shell.IsTucked(state.State, sim.GetActiveMove(1)?.Slot));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.OwnerWindow);
        for (int tick = 0; tick < P.ThrowTick && !sim.Resolver.GetActiveHitboxes().Any(h => h.OwnerId == 1); tick++)
            BashieShellTests.Step(sim);
        Assert.Contains(sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == 1 && !h.OwnerWindow);
    }

    [Fact]
    public void HerPackageUsesTheTetherOnGroundAndAirAAndNeedsRuntimeApi113()
    {
        var compiled = RingDuel.Compile(null);
        Assert.Equal("1.13.0", compiled.Metadata.RuntimeApiMin);
        Assert.DoesNotContain(compiled.Definition.CapabilityRequirements,
            r => r.CapabilityId == CharacterPackageCompiler.ChargedStreamCapabilityId);
        foreach (var id in new[] { "ground.A", "air.A" })
            Assert.Contains(compiled.Definition.Slots.Single(slot => slot.Id == id).Timeline.Stages[0].Operations,
                op => op is CookedStartCapabilityOperation { CapabilityId: CharacterPackageCompiler.TetherCapabilityId });
        Assert.True(CharacterPackageCompiler.IsPublicCapability(CharacterPackageCompiler.ChargedStreamCapabilityId,
            CharacterPackageCompiler.ChargedStreamCapabilityVersion), "Hose Down's capability stays public");
    }

    public static IEnumerable<object[]> InvalidSlots()
    {
        yield return Case("another operation", c => Ops(c).Add(JsonNode.Parse(
            """{"kind":"setVelocity","tick":0,"unit":"metersPerSecond","velocityMode":"additive","x":0,"y":1,"z":0}""")));
        yield return Case("a late start", c => Op(c)["tick"] = 1);
        yield return Case("IASA", c => Stage(c)["iasaTicks"] = 5);
        yield return Case("an aim", c => { Slot(c)["aimMode"] = "groundVector"; });
        yield return Case("a tuck", c => Slot(c)["shellPolicy"] = "tuck");
        yield return Case("gravity", c => EditTether(c, p => p["projectile"]!["gravity"] = 4));
        yield return Case("a return", c => EditTether(c, p => p["projectile"]!["return"] = JsonNode.Parse(
            """{"outboundTicks":10,"returnSpeed":10,"returnAcceleration":40,"selfHitDamage":0,"selfHitStunTicks":0}""")));
        yield return Case("a knockback", c => EditTether(c, p => p["projectile"]!["baseKnockback"] = 4));
        yield return Case("no speed", c => EditTether(c, p => p["projectile"]!["speed"] = 0));
        yield return Case("no arrival window", c => EditTether(c, p => p["arrivalStunTicks"] = 0));
        yield return Case("a phase clip first", c => EditTether(c, p => p["reelAnimationId"] = "anim.bashie.ring"));

        static object[] Case(string name, Action<JsonObject> edit) => new object[] { name, edit };
    }

    [Theory]
    [MemberData(nameof(InvalidSlots))]
    public void TheCompilerRejectsInvalidTetherSlots(string name, Action<JsonObject> edit)
    {
        var rejected = RingDuel.TryCompile(edit);
        Assert.True(rejected.CookedPackage == null, $"{name}: the compiler admitted it");
        Assert.Contains(rejected.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error);
    }

    private static RingDuel Arrived(out CharacterState her, bool victimShields = false)
    {
        var duel = new RingDuel().WithBashie().WithFighter(Dummy, RingDuel.Standing(0f, 5f, yawDegrees: 180f));
        duel.Step(Throw);
        duel.RunUntil(() => duel.State(Dummy).State == ActionState.Reeled, 60);
        var shield = new InputState { ShieldHeld = victimShields };
        duel.RunUntil(() => duel.State(Bashie).State != ActionState.Attacking, 120, (Dummy, shield));
        Assert.Equal(ActionState.Reeled, duel.State(Dummy).State);
        her = duel.State(Bashie);
        return duel;
    }

    /// <summary>Caught at 5 m, two ticks into the reel, with a third fighter standing by.</summary>
    private static RingDuel Reeling(bool thirdIsBashie = false, (float X, float Y, float Z)? thirdAt = null)
    {
        var at = thirdAt ?? (3f, 0f, 3f);
        var third = RingDuel.Standing(at.X, at.Z, yawDegrees: 180f);
        var duel = new RingDuel().WithBashie().WithFighter(Dummy, RingDuel.Standing(0f, 5f));
        if (thirdIsBashie) duel.WithBashie(third, Third);
        else duel.WithFighter(Third, third);
        duel.Step(Throw with { TargetEntityId = (byte)Dummy });
        duel.RunUntil(() => duel.State(Dummy).State == ActionState.Reeled, 60, aim: Dummy);
        duel.Step();
        duel.Step();
        Assert.Equal(ActionState.Reeled, duel.State(Dummy).State);
        Assert.NotEqual(0ul, duel.State(Bashie).InteractionId);
        return duel;
    }

    internal static void EditTether(JsonObject character, Action<JsonObject> edit) => edit(Op(character)["parameters"]!.AsObject());
    private static JsonObject Slot(JsonObject c)
        => c["slots"]!.AsArray().Select(s => s!.AsObject()).Single(s => (string?)s["id"] == "ground.A");
    private static JsonObject Stage(JsonObject c) => Slot(c)["timeline"]!["stages"]![0]!.AsObject();
    private static JsonArray Ops(JsonObject c) => Stage(c)["operations"]!.AsArray();
    private static JsonObject Op(JsonObject c) => Ops(c).Select(o => o!.AsObject())
        .Single(o => (string?)o["capabilityId"] == CharacterPackageCompiler.TetherCapabilityId);

    private static InputState Aim(ulong target) => new() { TargetEntityId = (byte)target };

    private static void AssertParallel(float ax, float ay, float az, float bx, float by, float bz)
    {
        float a = MathF.Sqrt(ax * ax + ay * ay + az * az), b = MathF.Sqrt(bx * bx + by * by + bz * bz);
        Assert.InRange((ax * bx + ay * by + az * bz) / (a * b), .9999f, 1.0001f);
    }

    private static float AngleBetween(float a, float b)
    {
        float d = MathF.IEEERemainder(a - b, 2f * MathF.PI);
        return MathF.Abs(d);
    }
}

/// <summary>Bashie (entity 1) on her live package with her cooked poses, against fighters placed by the test.</summary>
internal sealed class RingDuel
{
    internal const ulong Bashie = 1, Dummy = 2, Third = 3;
    internal static readonly (CharacterDefinition Def, BakedAnimationData Poses) Her = Load(null);

    internal readonly ServerSimulation Sim;
    internal readonly List<SpellResolver.HitResult> Hits = new();
    internal readonly List<TimelinePresentationEvent> Events = new();

    internal RingDuel(ArenaDefinition? arena = null) => Sim = new ServerSimulation(arena ?? BashieShellTests.Floor());

    internal IEnumerable<TimelinePresentationEvent> Retracts
        => Events.Where(e => e.Source == PresentationEventSource.TetherRetract);
    internal bool RingOut => Sim.Resolver.GetActiveHitboxes().Any(h => h.OwnerId == Bashie && !h.OwnerWindow);
    internal Hitbox Ring => Sim.Resolver.GetActiveHitboxes().Single(h => h.OwnerId == Bashie && !h.OwnerWindow);
    internal CharacterState State(ulong id) => Sim.GetState(id);

    /// <summary>Registers a Bashie: entity 1 by default, facing +Z at the origin.</summary>
    internal RingDuel WithBashie(CharacterState? state = null, ulong id = Bashie,
        (CharacterDefinition Def, BakedAnimationData Poses)? def = null)
    {
        var s = state ?? Standing(0f, 0f);
        s.EntityId = id;
        var content = def ?? Her;
        Sim.RegisterEntity(id, content.Def, s, content.Poses);
        return this;
    }

    internal RingDuel WithFighter(ulong id, CharacterState state, CharacterDefinition? def = null, BakedAnimationData? poses = null)
    {
        state.EntityId = id;
        Sim.RegisterEntity(id, def ?? TestHelpers.EngineDef, state, poses);
        return this;
    }

    internal void Step(InputState bashie = default, params (ulong Id, InputState Input)[] others)
    {
        var inputs = new Dictionary<ulong, InputState> { [Bashie] = bashie };
        foreach (var (id, input) in others) inputs[id] = input;
        Sim.Tick(inputs);
        Hits.AddRange(Sim.LastTickHits);
        Events.AddRange(Sim.GetPresentationEvents(clear: true));
    }

    /// <summary>Steps until the condition holds, failing past the bound. Bashie keeps aiming at <paramref name="aim"/>.</summary>
    internal void RunUntil(Func<bool> done, int maxTicks, params (ulong Id, InputState Input)[] others)
        => RunUntil(done, maxTicks, 0, others);

    internal void RunUntil(Func<bool> done, int maxTicks, ulong aim, params (ulong Id, InputState Input)[] others)
    {
        for (int tick = 0; tick < maxTicks; tick++)
        {
            if (done()) return;
            Step(new InputState { TargetEntityId = (byte)aim }, others);
        }
        Assert.True(done(), $"not reached within {maxTicks} ticks");
    }

    /// <summary>A one-tick hit from <paramref name="owner"/> on <paramref name="target"/>, resolved next tick.</summary>
    internal void HitFrom(ulong owner, ulong target, float damage)
    {
        var at = State(target);
        Sim.Resolver.Spawn(new Hitbox
        {
            X = at.PX, Y = at.PY, Z = at.PZ, Radius = .5f, Shape = HitboxShape.Sphere,
            Damage = damage, BaseKnockback = 6f, KnockbackGrowth = 0f, KnockbackAngle = 45, StunTicks = 10,
            DurationTicks = 1, OwnerId = owner,
        });
    }

    internal static CharacterState Standing(float x, float z, float yawDegrees = 0f)
    {
        var state = TestHelpers.PlayerState(x, z);
        state.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        state.FacingYaw = yawDegrees * MathF.PI / 180f;
        return state;
    }

    internal static CookedTetherCapabilityParameters TetherOf(CharacterDefinition def)
        => (CookedTetherCapabilityParameters)def.CookedSlots!.Single(slot => slot.Id == "ground.A").Timeline.Stages[0]
            .Operations.OfType<CookedStartCapabilityOperation>().Single().Parameters;

    internal static string SourcePath(string file)
        => Path.Combine(TestPaths.Root, "client/Unity/Assets/CharacterPackages/bashie", file);

    internal static CharacterCompileResult TryCompile(Action<JsonObject>? edit)
    {
        var character = JsonNode.Parse(File.ReadAllText(SourcePath("character.json")))!.AsObject();
        edit?.Invoke(character);
        return CharacterPackageCompiler.Compile(File.ReadAllText(SourcePath("package.json")), character.ToJsonString(),
            CharacterCookProfile.TrustedBuiltIn);
    }

    internal static CookedCharacterPackage Compile(Action<JsonObject>? edit)
    {
        var compiled = TryCompile(edit);
        Assert.True(compiled.CookedPackage != null, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        return compiled.CookedPackage!;
    }

    /// <summary>Her source, optionally edited, with the poses of her current cook.</summary>
    internal static (CharacterDefinition Def, BakedAnimationData Poses) Load(Action<JsonObject>? edit)
    {
        var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(Compile(edit), CharacterClass.Bashie);
        var cooked = Path.Combine(TestPaths.Root, "content-cooked/bashie");
        var poses = BakedAnimationData.LoadFromBin(File.ReadAllBytes(Path.Combine(cooked, "poses.bin")));
        using var bindings = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(cooked, "client.bindings")));
        var names = bindings.RootElement.GetProperty("animations").EnumerateArray().ToDictionary(
            a => a.GetProperty("poseTrackId").GetString()!, a => a.GetProperty("semanticId").GetString()!);
        return (definition, new BakedAnimationData
        {
            BoneNames = poses.BoneNames,
            Animations = poses.Animations.Select(animation => animation.WithName(names[animation.Name])).ToArray(),
        });
    }
}

internal static class TestPaths
{
    internal static readonly string Root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
