using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using SlopArena.Shared.Abilities;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Wibou's flash (ADR-0052), a counter: a hit caught in the stance locks, straight legs cut each locked victim and
/// hold them, and every held victim launches on the pop tick fixed at the lock. Wibou's source with fixture numbers,
/// against engine fighters.
/// </summary>
public sealed class FlashCutTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void VictimsHoldStillUntilTheyAllLaunchOnThePopTickNearestFirst(int count)
    {
        var duel = new FlashDuel().WithVictims(count);
        var held = new Dictionary<ulong, (uint Tick, float X, float Y, float Z)>();
        var launched = new Dictionary<ulong, uint>();
        duel.Press();
        for (int i = 0; i < 120; i++)
        {
            duel.Step();
            foreach (ulong id in FlashDuel.Victims.Take(count))
            {
                var v = duel.State(id);
                if (v.State == ActionState.CutHeld)
                {
                    if (!held.ContainsKey(id)) held[id] = (duel.Tick, v.PX, v.PY, v.PZ);
                    var at = held[id];
                    Assert.Equal((at.X, at.Y, at.Z), (v.PX, v.PY, v.PZ));
                }
                else if (held.ContainsKey(id) && !launched.ContainsKey(id))
                {
                    Assert.True(v.HitstunTicks > 0 && v.KVX * v.KVX + v.KVY * v.KVY + v.KVZ * v.KVZ > 1f, $"victim {id} launches");
                    launched[id] = duel.Tick;
                }
            }
        }
        uint lockTick = duel.Single(FlashDuel.Lock).Select(e => e.MatchTick).Distinct().Single();
        uint pop = lockTick + (uint)FlashDuel.P.PopAfterLock(count);
        Assert.Equal(count, held.Count);
        Assert.All(launched.Values, tick => Assert.Equal(pop, tick));
        Assert.Equal(count, launched.Count);
        Assert.Equal(FlashDuel.Victims.Take(count), held.OrderBy(x => x.Value.Tick).Select(x => x.Key));
        Assert.Equal(held.Values.Select(x => x.Tick).Distinct().Count(), count);
        float travelled = MathF.Sqrt(MathF.Pow(duel.WibouAt(lockTick).PX, 2) + MathF.Pow(duel.WibouAt(lockTick + (uint)(count * FlashDuel.P.LegTicks)).PZ - duel.WibouAt(lockTick).PZ, 2));
        Assert.True(travelled > 2f, "Wibou moves while his victims hold");

        foreach (var (id, cue, tick) in new[] { (FlashDuel.Lock, "lock", lockTick), (FlashDuel.Reveal, "reveal", pop - FlashDuel.P.RevealLeadTicks), (FlashDuel.Pop, "pop", pop) })
        {
            var events = duel.Single(id).ToList();
            Assert.True(events.Count == count, $"{cue}: one cue per victim");
            Assert.All(events, e => Assert.Equal(tick, e.MatchTick));
            Assert.Equal(count, events.Select(e => e.Key).Distinct().Count());
        }
        var click = Assert.Single(duel.Single(FlashDuel.Click));
        Assert.Equal(pop, click.MatchTick);
        Assert.DoesNotContain(duel.Single(FlashDuel.Pop), e => e.Key == click.Key);
    }

    [Theory]
    [InlineData(1)] [InlineData(3)]
    public void EachPopLaunchesItsVictimAwayFromWhereWibouSheathes(int count)
    {
        var duel = new FlashDuel().WithVictims(count);
        var wasHeld = new HashSet<ulong>();
        var gapAtPop = new Dictionary<ulong, float>();
        duel.Press();
        for (int i = 0; i < 160; i++)
        {
            duel.Step();
            var wibou = duel.State(FlashDuel.Wibou);
            foreach (ulong id in FlashDuel.Victims.Take(count))
            {
                var v = duel.State(id);
                if (v.State == ActionState.CutHeld) { wasHeld.Add(id); continue; }
                if (!wasHeld.Contains(id)) continue;
                float dx = v.PX - wibou.PX, dz = v.PZ - wibou.PZ;
                float gap = MathF.Sqrt(dx * dx + dz * dz);
                if (gapAtPop.TryAdd(id, gap))
                    Assert.True(v.KVX * dx + v.KVZ * dz > 0f, $"victim {id} launches away from Wibou");
                else
                    Assert.True(gap >= gapAtPop[id] - 0.05f, $"victim {id} comes back toward Wibou at tick {duel.Tick}");
            }
        }
        Assert.Equal(count, gapAtPop.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AVictimWhoKeepsRunningFromMidRangeEscapesTheStraightLegButAStandingOneIsCut(bool runs)
    {
        // A homing leg would chase the runner; a straight one aims where they stood when it started. The leg and
        // cut need room for a runner to clear: a slower leg and a thinner cut than the shared fixture's.
        var slowLegs = TestHelpers.ResolveSourceDef("wibou", c =>
        {
            FlashDuel.Pin(c);
            var p = FlashDuel.Operation(c)["parameters"]!.AsObject();
            p["legTicks"] = 10;
            p["cutHitbox"]!["radius"] = 0.8;
        });
        var duel = new FlashDuel(wibou: slowLegs).With(FlashDuel.V2, 0f, 3f);
        var run = new InputState { MoveX = 1f, Right = true };
        duel.Press();
        for (int i = 0; i < 60; i++)
            duel.Step(default, runs && duel.Tick >= 8 ? new[] { (FlashDuel.V2, run) } : Array.Empty<(ulong, InputState)>());
        Assert.Contains(duel.Single(FlashDuel.Lock), _ => true);
        bool cut = duel.Hits.Any(h => h.OwnerEntityId == FlashDuel.Wibou && h.TargetEntityId == FlashDuel.V2 && h.Damage > 0f);
        Assert.Equal(!runs, cut);
    }

    [Theory]
    [InlineData((ushort)0)] [InlineData((ushort)100)]
    public void WibouStaysSheathedUntilHisPoppedVictimCanAct(ushort percent)
    {
        var duel = new FlashDuel().WithVictims(1, percent);
        bool held = false, popped = false;
        duel.Press();
        for (int i = 0; i < 260; i++)
        {
            duel.Step();
            var v = duel.State(FlashDuel.V2);
            if (v.State == ActionState.CutHeld) held = true;
            else if (held) popped = true;
            if (!popped) continue;
            bool victimReeling = v.State == ActionState.Hitstun || v.HitstopTicks > 0;
            var w = duel.State(FlashDuel.Wibou);
            if (victimReeling)
                Assert.True(w.State == ActionState.Attacking, $"Wibou acts at tick {duel.Tick} while his victim still reels");
        }
        Assert.True(popped);
        Assert.NotEqual(ActionState.Attacking, duel.State(FlashDuel.Wibou).State);
    }

    [Fact]
    public void AFighterOutsideRangeAtTheLockWalkingOntoTheLegIsNotCut()
    {
        var duel = new FlashDuel()
            .With(FlashDuel.V2, 0f, 6.5f)
            .With(FlashDuel.V3, .6f, 7.6f);
        duel.Press();
        duel.RunTo(() => duel.Single(FlashDuel.Lock).Any());
        for (int i = 0; i < 60; i++) duel.Step(others: (FlashDuel.V3, new InputState { MoveX = -1f }));
        Assert.Contains(duel.Hits, h => h.OwnerEntityId == FlashDuel.Wibou && h.TargetEntityId == FlashDuel.V2);
        Assert.DoesNotContain(duel.Hits, h => h.OwnerEntityId == FlashDuel.Wibou && h.TargetEntityId == FlashDuel.V3);
        Assert.Equal(0, duel.State(FlashDuel.V3).DamagePercent);
        Assert.True(duel.WibouAt(duel.Tick).PZ > 7f, "the leg ran past where the late fighter stood");
    }

    [Fact]
    public void AShieldBlocksItsCutWithNoHoldWhileTheOtherVictimStillPops()
    {
        var duel = new FlashDuel().WithVictims(2);
        var guard = new InputState { ShieldHeld = true, ShieldPressed = true };
        duel.Press(others: (FlashDuel.V2, guard));
        for (int i = 0; i < 100; i++) duel.Step(others: (FlashDuel.V2, guard with { ShieldPressed = false }));
        Assert.Equal(2, duel.Single(FlashDuel.Lock).Count());
        Assert.Contains(duel.Hits, h => h.TargetEntityId == FlashDuel.V2 && h.Blocked);
        Assert.Equal(0, duel.State(FlashDuel.V2).DamagePercent);
        Assert.DoesNotContain(duel.History, s => s.Id == FlashDuel.V2 && s.State.State == ActionState.CutHeld);
        Assert.Single(duel.Single(FlashDuel.Pop));
        Assert.Contains(duel.History, s => s.Id == FlashDuel.V3 && s.State.State == ActionState.CutHeld);
    }

    [Fact]
    public void AnAirDodgeThroughTheLegAvoidsTheCut()
    {
        var duel = new FlashDuel().With(FlashDuel.V2, 0f, 2.5f, height: 8f);
        duel.Press();
        duel.RunTo(() => duel.Tick + 1 >= FlashDuel.StanceEnd);
        duel.Step(others: (FlashDuel.V2, new InputState { ShieldPressed = true, ShieldHeld = true }));
        Assert.Equal(ActionState.AirDodgeMovement, duel.State(FlashDuel.V2).State);
        for (int i = 0; i < 60; i++) duel.Step();
        Assert.Single(duel.Single(FlashDuel.Lock));
        Assert.DoesNotContain(duel.Hits, h => h.TargetEntityId == FlashDuel.V2);
        Assert.Empty(duel.Single(FlashDuel.Pop));
    }

    [Fact]
    public void ArmourTakesTheCutsDamageAsAnOrdinaryHitWithoutAHold()
    {
        var duel = new FlashDuel().WithVictims(1);
        duel.Sim.ActivateAbility(FlashDuel.V2, new ArmouredStance(120), 2, TestHelpers.EngineDef);
        duel.Press();
        duel.RunTo(() => duel.Hits.Any(h => h.TargetEntityId == FlashDuel.V2));
        var hit = Assert.Single(duel.Hits, h => h.TargetEntityId == FlashDuel.V2);
        Assert.True(hit.ArmorProtected);
        var victim = duel.State(FlashDuel.V2);
        Assert.NotEqual(ActionState.CutHeld, victim.State);
        Assert.Equal(ServerSimulation.ComputeHitstopTicks(FlashDuel.P.CutHitbox.Damage), victim.HitstopTicks);
        Assert.Equal((ushort)FlashDuel.P.CutHitbox.Damage, victim.DamagePercent);
        for (int i = 0; i < 80; i++) duel.Step();
        Assert.Empty(duel.Single(FlashDuel.Pop));
        Assert.True(duel.State(FlashDuel.V2).KVX * duel.State(FlashDuel.V2).KVX + duel.State(FlashDuel.V2).KVZ * duel.State(FlashDuel.V2).KVZ < .0001f);
    }

    [Fact]
    public void AThirdFightersHitAddsDamageOnlyAndThePopStaysWibousKnockbackAndKill()
    {
        var control = new FlashDuel(blastLines: 45f).WithVictims(1, percent: 300).With(FlashDuel.Third, 30f, 30f);
        var hit = new FlashDuel(blastLines: 45f).WithVictims(1, percent: 300).With(FlashDuel.Third, 30f, 30f);
        control.Press(); hit.Press();
        hit.RunTo(() => hit.State(FlashDuel.V2).State == ActionState.CutHeld);
        control.RunTo(() => control.Tick == hit.Tick);
        hit.Spawn(FlashDuel.Third, FlashDuel.V2, damage: 9f, angle: 90, knockback: 30f);
        control.Step(); hit.Step();
        Assert.Equal(control.State(FlashDuel.V2).DamagePercent + 9, hit.State(FlashDuel.V2).DamagePercent);
        Assert.Equal(ActionState.CutHeld, hit.State(FlashDuel.V2).State);
        Assert.Equal(control.State(FlashDuel.V2).HitstopTicks, hit.State(FlashDuel.V2).HitstopTicks);

        uint popControl = control.RunTo(() => control.State(FlashDuel.V2).State != ActionState.CutHeld);
        uint popHit = hit.RunTo(() => hit.State(FlashDuel.V2).State != ActionState.CutHeld);
        Assert.Equal(popControl, popHit);
        var a = control.State(FlashDuel.V2); var b = hit.State(FlashDuel.V2);
        float dot = (a.KVX * b.KVX + a.KVZ * b.KVZ) / MathF.Sqrt((a.KVX * a.KVX + a.KVZ * a.KVZ) * (b.KVX * b.KVX + b.KVZ * b.KVZ));
        Assert.InRange(dot, .9999f, 1.0001f);
        Assert.True(b.KVX * b.KVX + b.KVZ * b.KVZ >= a.KVX * a.KVX + a.KVZ * a.KVZ, "the added damage only strengthens Wibou's launch");

        var deaths = new List<ServerSimulation.DeathEvent>();
        for (int i = 0; i < 400 && deaths.Count == 0; i++) { hit.Step(); deaths.AddRange(hit.Sim.LastTickDeaths); }
        Assert.Equal(FlashDuel.Wibou, Assert.Single(deaths).KillerEntityId);
    }

    [Theory]
    [InlineData("grab")] [InlineData("tether")] [InlineData("wave")]
    public void GrabsTetherRingsAndTheWaveRefuseAHeldFighter(string capture)
    {
        var (control, _, _) = Capture(capture, flash: false);
        Assert.Contains(control.History, s => s.Id == FlashDuel.V2
            && s.State.State is ActionState.Grabbed or ActionState.Reeled or ActionState.Swept);
        var (held, heldAt, released) = Capture(capture, flash: true);
        Assert.True(released - heldAt > 20, "the capture had time to reach the held fighter");
        Assert.DoesNotContain(held.History, s => s.Id == FlashDuel.V2
            && s.State.State is ActionState.Grabbed or ActionState.Reeled or ActionState.Swept);
        Assert.Equal(released, Assert.Single(held.Single(FlashDuel.Pop)).MatchTick);
    }

    /// <summary>The same capture on the same fighter at the same tick, held by a cut or standing free.</summary>
    private static (FlashDuel Duel, uint HeldAt, uint Released) Capture(string capture, bool flash)
    {
        var duel = capture switch
        {
            "grab" => new FlashDuel().WithVictims(1).With(FlashDuel.Third, 40f, 40f, def: Grabber),
            "tether" => new FlashDuel().WithVictims(1).With(FlashDuel.Third, 40f, 40f, def: RingDuel.Her.Def, poses: RingDuel.Her.Poses),
            _ => new FlashDuel().WithVictims(1).With(FlashDuel.Third, 40f, 0f, yawDegrees: 90f, def: BashieShellFixture.Definition),
        };
        var whistle = capture == "wave" ? new InputState { ActiveSlot = AbilitySlots.F } : default;
        if (flash) duel.Press((FlashDuel.Third, whistle));
        else duel.Step(default, (FlashDuel.Third, whistle));
        uint heldAt = duel.RunTo(() => flash ? duel.State(FlashDuel.V2).State == ActionState.CutHeld
            : duel.Tick > FlashDuel.StanceEnd);
        var victim = duel.State(FlashDuel.V2);
        var third = duel.State(FlashDuel.Third);
        if (capture == "wave")
        {
            Assert.Equal(ActionState.Shell, third.State);
            float speed = MathF.Sqrt(third.VX * third.VX + third.VZ * third.VZ);
            (third.VX, third.VZ) = (0f, speed);
        }
        (third.PX, third.PZ, third.FacingYaw) = capture switch
        {
            "grab" => (victim.PX + .7f, victim.PZ, -MathF.PI / 2),
            "tether" => (victim.PX - 3f, victim.PZ, MathF.PI / 2),
            _ => (victim.PX, victim.PZ - 1f, 0f),
        };
        duel.Sim.SetState(FlashDuel.Third, third);
        var press = capture switch
        {
            "grab" => new InputState { GrabPressed = true },
            "tether" => new InputState { ActiveSlot = AbilitySlots.A, TargetEntityId = (byte)FlashDuel.V2 },
            _ => default,
        };
        duel.Step(others: (FlashDuel.Third, press));
        uint released = 0;
        for (int i = 0; i < 40; i++)
        {
            duel.Step(others: (FlashDuel.Third, capture == "tether" ? press with { ActiveSlot = 0 } : default));
            if (released == 0 && flash && duel.State(FlashDuel.V2).State != ActionState.CutHeld) released = duel.Tick;
        }
        return (duel, heldAt, released);
    }

    [Fact]
    public void HittingWibouAfterTheFirstCutEndsTheFlashButTheFirstVictimStillPopsOnTime()
    {
        var duel = new FlashDuel().WithVictims(2).With(FlashDuel.Third, 30f, 30f);
        duel.Press();
        duel.RunTo(() => duel.State(FlashDuel.V2).State == ActionState.CutHeld);
        uint lockTick = duel.Single(FlashDuel.Lock).First().MatchTick;
        duel.Spawn(FlashDuel.Third, FlashDuel.Wibou, damage: 12f, angle: 40, knockback: 20f);
        uint released = duel.RunTo(() => duel.State(FlashDuel.V2).State != ActionState.CutHeld);
        Assert.Equal(lockTick + (uint)FlashDuel.P.PopAfterLock(2), released);
        Assert.NotEqual(ActionState.Attacking, duel.WibouAt(released).State);
        Assert.Equal(released, Assert.Single(duel.Single(FlashDuel.Pop)).MatchTick);
        Assert.Empty(duel.Single(FlashDuel.Click));
        Assert.Equal(0, duel.State(FlashDuel.V3).DamagePercent);
        Assert.DoesNotContain(duel.History, s => s.Id == FlashDuel.V3 && s.State.State == ActionState.CutHeld);
    }

    [Fact]
    public void AJabCaughtInTheStanceDealsNoDamageAndTheJabberIsCutAndPops()
    {
        var duel = new FlashDuel().With(FlashDuel.Third, 0f, 1.1f, yawDegrees: 180f, def: Jabber);
        duel.PressUncaught();
        duel.Step(others: (FlashDuel.Third, new InputState { ActiveSlot = AbilitySlots.Slot1 }));
        duel.RunTo(() => duel.Single(FlashDuel.Lock).Any(), maxTicks: 12);
        Assert.True(duel.Tick < FlashDuel.StanceEnd, "the catch locks at once, not at the end of the stance");
        Assert.DoesNotContain(duel.Hits, h => h.TargetEntityId == FlashDuel.Wibou);
        uint popped = duel.RunTo(() => duel.Single(FlashDuel.Pop).Any());
        Assert.Contains(duel.History, s => s.Id == FlashDuel.Third && s.State.State == ActionState.CutHeld);
        Assert.Equal(0, duel.State(FlashDuel.Wibou).DamagePercent);
        Assert.DoesNotContain(duel.History, s => s.Id == FlashDuel.Wibou && s.Tick <= popped
            && (s.State.HitstunTicks > 0 || s.State.State == ActionState.Hitstun));
        Assert.Equal((ushort)FlashDuel.P.CutHitbox.Damage, duel.State(FlashDuel.Third).DamagePercent);
    }

    [Fact]
    public void TheVictimALegDashesAtCannotHitHimOutOfIt()
    {
        var duel = new FlashDuel().WithVictims(1);
        duel.Press();
        duel.RunTo(() => duel.Single(FlashDuel.Lock).Any());
        duel.Spawn(FlashDuel.V2, FlashDuel.Wibou, damage: 12f, angle: 40, knockback: 20f);
        duel.RunTo(() => duel.Single(FlashDuel.Pop).Any());
        Assert.Equal(0, duel.State(FlashDuel.Wibou).DamagePercent);
        Assert.Contains(duel.History, s => s.Id == FlashDuel.V2 && s.State.State == ActionState.CutHeld);
    }

    [Fact]
    public void AStanceNobodyHitsWhiffsAndANormalPunishesTheWhiff()
    {
        var duel = new FlashDuel().With(FlashDuel.Third, 0f, 3f, yawDegrees: 180f, def: Jabber);
        duel.PressUncaught();
        duel.RunTo(() => duel.Tick >= FlashDuel.StanceEnd);
        Assert.Empty(duel.Single(FlashDuel.Lock));
        Assert.Equal(AbilitySlots.F, duel.State(FlashDuel.Wibou).AttackSlot);
        var third = duel.State(FlashDuel.Third);
        third.PZ = 1.1f;
        duel.Sim.SetState(FlashDuel.Third, third);
        duel.Step(others: (FlashDuel.Third, new InputState { ActiveSlot = AbilitySlots.Slot1 }));
        duel.RunTo(() => duel.Hits.Any(h => h.OwnerEntityId == FlashDuel.Third), maxTicks: 12);
        Assert.True(duel.Tick < FlashDuel.StanceEnd + FlashDuel.P.WhiffTicks, "the normal lands inside the whiff");
        Assert.Contains(duel.Hits, h => h.OwnerEntityId == FlashDuel.Third && h.TargetEntityId == FlashDuel.Wibou && !h.Blocked);

        var alone = new FlashDuel();
        alone.PressUncaught();
        uint free = alone.RunTo(() => alone.State(FlashDuel.Wibou).AttackSlot == 0);
        Assert.Equal(FlashDuel.StanceEnd + FlashDuel.P.WhiffTicks, free);
        Assert.Single(alone.Single(FlashDuel.Click));
    }

    [Fact]
    public void AnAirborneVictimHangsWhileHeldAndWibouHangsUntilTheClick()
    {
        var duel = new FlashDuel().With(FlashDuel.V2, 0f, 2f, height: 8f);
        duel.Press();
        uint heldAt = duel.RunTo(() => duel.State(FlashDuel.V2).State == ActionState.CutHeld);
        Assert.False(duel.State(FlashDuel.V2).IsGrounded);
        float victimY = duel.State(FlashDuel.V2).PY;
        uint pop = duel.Single(FlashDuel.Lock).Single().MatchTick + (uint)FlashDuel.P.PopAfterLock(1);
        duel.RunTo(() => duel.Tick == pop - 1);
        Assert.Equal(victimY, duel.State(FlashDuel.V2).PY);
        float wibouY = duel.State(FlashDuel.Wibou).PY;
        Assert.False(duel.State(FlashDuel.Wibou).IsGrounded);
        Assert.Equal(wibouY, duel.WibouAt(heldAt + (uint)FlashDuel.P.LegTicks).PY, 3);
        for (int i = 0; i < 6; i++) duel.Step();
        Assert.True(duel.State(FlashDuel.Wibou).PY < wibouY - .05f, "he falls once he recovers");
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ARespawnOrModeResetDuringTheHoldLeavesNoHoldAndNoPop(bool newLife)
    {
        var duel = new FlashDuel().WithVictims(1);
        duel.Press();
        duel.RunTo(() => duel.State(FlashDuel.V2).State == ActionState.CutHeld);
        var held = duel.State(FlashDuel.V2);
        // The fresh state respawns and mode resets write.
        duel.Sim.SetState(FlashDuel.V2, new CharacterState
        {
            EntityId = FlashDuel.V2, PX = 5f, PY = TestHelpers.GroundPY(TestHelpers.EngineDef), PZ = -5f,
            State = ActionState.Idle, IsGrounded = true, JumpsLeft = 2, AirDodgesLeft = 1,
            Deaths = (byte)(held.Deaths + (newLife ? 1 : 0)),
        });
        duel.Step();
        Assert.NotEqual(ActionState.CutHeld, duel.State(FlashDuel.V2).State);
        Assert.Equal(0, duel.State(FlashDuel.V2).HitstopTicks);
        for (int i = 0; i < 60; i++) duel.Step(others: (FlashDuel.V2, new InputState { MoveX = 1f }));
        Assert.Empty(duel.Single(FlashDuel.Pop));
        Assert.True(duel.State(FlashDuel.V2).PX > 6f, "the fresh fighter moves freely");
    }

    [Fact]
    public void AHeldFighterWhoseFreezeEndsAnotherWayReturnsToIdle()
    {
        var duel = new FlashDuel().WithVictims(1);
        duel.Press();
        duel.RunTo(() => duel.State(FlashDuel.V2).State == ActionState.CutHeld);
        var held = duel.State(FlashDuel.V2);
        held.HitstopTicks = 0;
        duel.Sim.SetState(FlashDuel.V2, held);
        duel.Step();
        Assert.Equal(ActionState.Idle, duel.State(FlashDuel.V2).State);
    }

    [Theory]
    [InlineData(3)]   // mid first leg
    [InlineData(8)]   // between the two cuts
    [InlineData(25)]  // mid-sheathe
    [InlineData(-1)]  // right before the pop
    [InlineData(0)]   // the pop tick
    [InlineData(1)]   // right after it
    public void ASnapshotRestoredAnywhereInTheFlashReproducesTheRun(int at)
    {
        var continuous = new FlashDuel().WithVictims(2);
        continuous.Press();
        continuous.RunTo(() => continuous.Single(FlashDuel.Lock).Any());
        uint lockTick = continuous.Tick;
        uint capture = at is >= -1 and <= 1 ? (uint)(lockTick + FlashDuel.P.PopAfterLock(2) + at) : lockTick + (uint)at;
        continuous.RunTo(() => continuous.Tick == capture);
        var restored = new FlashDuel().WithVictims(2);
        restored.Sim.RestoreSnapshot(continuous.Sim.CaptureSnapshot());
        continuous.Sim.ClearPresentationEvents(); restored.Sim.ClearPresentationEvents();
        for (int i = 0; i < 70; i++)
        {
            continuous.Step(); restored.Step();
            Assert.True(continuous.Sim.CaptureSnapshot().ContentEquals(restored.Sim.CaptureSnapshot()), $"restore {at}, tick {i}");
            Assert.Equal(continuous.Sim.LastTickHits, restored.Sim.LastTickHits);
            Assert.Equal(continuous.LastEvents, restored.LastEvents);
        }
    }

    [Fact]
    public void TheCpuNeverOpensWithTheCounterAgainstAnIdleOpponentInRange()
    {
        var duel = new FlashDuel().With(FlashDuel.V2, 0f, 3f);
        var self = duel.State(FlashDuel.Wibou);
        var policy = new HeuristicBotPolicy();
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        for (int tick = 0; tick < 600; tick++)
            Assert.NotEqual(AbilitySlots.F,
                policy.Decide(self, duel.State(FlashDuel.V2), FlashDuel.Def, new Random(tick), memory).ActiveSlot);
    }

    [Fact]
    public void TheCpuCountersAJabAndCutsTheJabber()
    {
        var duel = new FlashDuel().With(FlashDuel.Third, 0f, 1.1f, yawDegrees: 180f, def: Jabber);
        var policy = new HeuristicBotPolicy();
        var memory = new BotMemory { Difficulty = CpuDifficulty.Hard };
        var rng = new Random(7);
        var arena = TestHelpers.TestArena();
        for (int tick = 0; tick < 600 && !duel.Single(FlashDuel.Lock).Any(); tick++)
        {
            var wibou = duel.State(FlashDuel.Wibou);
            policy.ObserveOpponent(memory, duel.State(FlashDuel.Third), Jabber, null, duel.Sim.GetActiveMove(FlashDuel.Third)?.Slot);
            memory.ChooseOpponent(wibou);
            var input = policy.Decide(wibou, FlashDuel.Def, rng, memory, arena, activeMove: duel.Sim.GetActiveMove(FlashDuel.Wibou)?.Slot);
            var jab = tick % 30 == 0 ? new InputState { ActiveSlot = AbilitySlots.Slot1 } : default;
            duel.Step(input with { MoveX = 0f, MoveY = 0f }, (FlashDuel.Third, jab));
        }
        Assert.Contains(duel.Single(FlashDuel.Lock), _ => true);
        duel.RunTo(() => duel.Single(FlashDuel.Pop).Any());
        Assert.Contains(duel.History, s => s.Id == FlashDuel.Third && s.State.State == ActionState.CutHeld);
    }

    [Fact]
    public void FlashCutIsPublicAndAdmissionRejectsGapsLongHoldsAndCompetingOperations()
    {
        Assert.True(CharacterPackageCompiler.IsPublicCapability(CharacterPackageCompiler.FlashCutCapabilityId, "1"));
        var source = FlashDuel.Source(FlashDuel.Pin);
        Assert.Equal("1.15.0", Compile(source).CookedPackage!.Metadata.RuntimeApiMin);
        foreach (var (name, edit, code) in new (string, Action<JsonObject>, string)[]
        {
            ("gap", p => p["cutHitbox"]!["radius"] = .5, "value.out-of-range"),
            ("long hold", p => { p["maxTargets"] = 7; p["legTicks"] = 30; p["sheatheTicks"] = 60; }, "value.out-of-range"),
            ("reveal after the sheathe", p => p["revealLeadTicks"] = 31, "value.out-of-range"),
            ("no launch", p => { p["cutHitbox"]!["baseKnockback"] = 0; p["cutHitbox"]!["knockbackGrowth"] = 0; }, "value.out-of-range"),
            ("bone cut", p => p["cutHitbox"]!["startBoneId"] = "_weapon_tip", "value.out-of-range"),
            ("unlisted clip", p => p["legAnimationId"] = "anim.wibou.g1", "capability.ambiguous"),
            ("undeclared cue", p => p["popPresentationId"] = "presentation.wibou.flash.missing", "reference.unresolved"),
        })
        {
            var copy = (JsonObject)source.DeepClone();
            edit(FlashDuel.Operation(copy)["parameters"]!.AsObject());
            Assert.True(Compile(copy).Diagnostics.Any(d => d.Code == code), name);
        }
        var competing = (JsonObject)source.DeepClone();
        FlashDuel.Stage(competing)["operations"]!.AsArray().Add(JsonNode.Parse(
            """{"kind":"armorWindow","tick":0,"unit":"ticks","durationTicks":10}"""));
        Assert.Contains(Compile(competing).Diagnostics, d => d.Code == "capability.ambiguous");
        var watchdog = (JsonObject)source.DeepClone();
        FlashDuel.Stage(watchdog)["durationTicks"] = 60;
        Assert.Contains(Compile(watchdog).Diagnostics, d => d.Code == "capability.ambiguous");
        foreach (string retired in new[] { "slop.internal.wibou.blade-flurry.v2", "slop.internal.wibou.blade-flurry.v1" })
            Assert.Contains(Compile(JsonNode.Parse(source.ToJsonString()
                .Replace(CharacterPackageCompiler.FlashCutCapabilityId, retired))!.AsObject()).Diagnostics, d => d.Code == "capability.retired");
        Assert.False(InternalCapabilityRegistry.TryCreate("slop.internal.wibou.blade-flurry.v2", "2", FlashDuel.P, out _));
    }

    private static CharacterCompileResult Compile(JsonObject character)
        => CharacterPackageCompiler.Compile(File.ReadAllText(FlashDuel.SourcePath("package.json")), character.ToJsonString(),
            CharacterCookProfile.TrustedBuiltIn);

    private static CharacterDefinition Grabber => TestHelpers.EngineDef with
    {
        CaptureGeometry = new CookedCaptureGeometry(1.1f, 0.8f, 1.2f, 0.6f,
            new CaptureAnchor(0f, 0.6f, 0.2f), new CaptureAnchor(0f, 0.6f, 0.65f)),
    };

    private static CharacterDefinition Jabber => TestHelpers.WithMove(TestHelpers.EngineDef, AbilitySlots.Slot1, false, "Jab", 20,
        hits: new[]
        {
            new HitboxEvent
            {
                TriggerTick = 1, DurationTicks = 4, Shape = HitboxShape.Sphere, Radius = .6f, OffZ = .7f, Damage = 5f,
                Knockback = new KnockbackData { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 6f },
                StunTicks = 12,
            },
        });

    /// <summary>An armour window the length of the stance, standing still.</summary>
    private sealed class ArmouredStance : ServerAbility
    {
        private readonly ushort _ticks;
        private bool _started;
        public ArmouredStance(ushort ticks) => _ticks = ticks;
        public override void OnStart(ref CharacterState s, CharacterDefinition def) => s.State = ActionState.Attacking;
        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            if (_started) return;
            _started = true;
            StartArmorWindow(_ticks);
        }
    }
}

/// <summary>Wibou (entity 1) at the origin facing +Z with a fixture flash on F, against engine fighters.</summary>
internal sealed class FlashDuel
{
    internal const ulong Wibou = 1, V2 = 2, V3 = 3, V4 = 4, Third = 9;
    internal static readonly ulong[] Victims = { V2, V3, V4 };
    internal const string Lock = "presentation.wibou.flash.lock", Reveal = "presentation.wibou.flash.nani",
        Click = "presentation.wibou.flash.click", Pop = "presentation.wibou.flash.pop";
    internal static readonly CharacterDefinition Def = TestHelpers.ResolveSourceDef("wibou", Pin);
    internal static readonly CookedFlashCutCapabilityParameters P = (CookedFlashCutCapabilityParameters)Def.CookedSlots!
        .Single(s => s.Id == "ground.F").Timeline.Stages[0].Operations.OfType<CookedStartCapabilityOperation>().Single().Parameters;
    /// <summary>The match tick of the stance's last tick when F is pressed on the first tick.</summary>
    internal static uint StanceEnd => P.StanceTicks;

    internal readonly ServerSimulation Sim;
    internal readonly List<SpellResolver.HitResult> Hits = new();
    internal readonly List<TimelinePresentationEvent> Events = new();
    internal readonly List<(uint Tick, ulong Id, CharacterState State)> History = new();
    internal List<TimelinePresentationEvent> LastEvents = new();
    private readonly List<ulong> _ids = new() { Wibou };
    private ulong _catchBy;

    internal FlashDuel(float blastLines = 0f, CharacterDefinition? wibou = null)
    {
        var arena = TestHelpers.TestArena();
        arena.Heightmap.OriginX = arena.Heightmap.OriginZ = -100;
        if (blastLines > 0f)
            (arena.KillTop, arena.KillMinX, arena.KillMaxX, arena.KillMinZ, arena.KillMaxZ)
                = (blastLines, -blastLines, blastLines, -blastLines, blastLines);
        Sim = new ServerSimulation(arena);
        var def = wibou ?? Def;
        Sim.RegisterEntity(Wibou, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
    }

    internal uint Tick => Sim.CurrentTick;
    internal CharacterState State(ulong id) => Sim.GetState(id);
    internal CharacterState WibouAt(uint tick) => History.Last(h => h.Id == Wibou && h.Tick <= tick).State;
    internal IEnumerable<TimelinePresentationEvent> Single(string id)
        => Events.Where(e => e.EntityId == Wibou && e.PresentationId == id && e.Source == PresentationEventSource.CapabilityLaunch);

    /// <summary>Victims nearest first: 2 m ahead, then 4.2 m and 5.7 m off to either side.</summary>
    internal FlashDuel WithVictims(int count, ushort percent = 0)
    {
        var at = new[] { (0f, 2f), (3f, 3f), (-4f, 4f) };
        for (int i = 0; i < count; i++) With(Victims[i], at[i].Item1, at[i].Item2, percent: percent);
        return this;
    }

    internal FlashDuel With(ulong id, float x, float z, float height = 0f, float yawDegrees = 180f,
        CharacterDefinition? def = null, BakedAnimationData? poses = null, ushort percent = 0)
    {
        var body = def ?? TestHelpers.EngineDef;
        var state = TestHelpers.PlayerState(x, z) with
        {
            EntityId = id, PY = TestHelpers.GroundPY(body) + height, IsGrounded = height == 0f,
            FacingYaw = yawDegrees * MathF.PI / 180f, DamagePercent = percent,
            AirTimeTicks = height == 0f ? (ushort)0 : body.Movement.FloatWindowTicks,
        };
        Sim.RegisterEntity(id, body, state, poses);
        _ids.Add(id);
        return this;
    }

    /// <summary>
    /// Presses F and has the first other fighter hit him on the stance's second-to-last tick, so the catch locks on
    /// <see cref="StanceEnd"/>, where the lock would fall with nobody to catch.
    /// </summary>
    internal void Press(params (ulong Id, InputState Input)[] others)
    {
        _catchBy = _ids.Skip(1).FirstOrDefault();
        PressUncaught(others);
    }

    internal void PressUncaught(params (ulong Id, InputState Input)[] others)
        => Step(new InputState { ActiveSlot = AbilitySlots.F }, others);

    internal void Step(InputState wibou = default, params (ulong Id, InputState Input)[] others)
    {
        if (_catchBy != 0 && Tick + 1 == StanceEnd - 1)
        {
            Spawn(_catchBy, Wibou, damage: 1f, angle: 0, knockback: 0f);
            _catchBy = 0;
        }
        var inputs = _ids.ToDictionary(id => id, _ => default(InputState));
        inputs[Wibou] = wibou;
        foreach (var (id, input) in others) inputs[id] = input;
        Sim.Tick(inputs);
        Hits.AddRange(Sim.LastTickHits);
        LastEvents = Sim.GetPresentationEvents(clear: true).ToList();
        Events.AddRange(LastEvents);
        foreach (ulong id in _ids) History.Add((Tick, id, Sim.GetState(id)));
    }

    /// <summary>Steps until the condition holds and returns the tick it first held on.</summary>
    internal uint RunTo(Func<bool> done, int maxTicks = 200)
    {
        for (int i = 0; i < maxTicks && !done(); i++) Step();
        Assert.True(done(), $"not reached by tick {Tick}");
        return Tick;
    }

    /// <summary>A one-pass hit from <paramref name="owner"/> on <paramref name="target"/>, resolved next tick.</summary>
    internal void Spawn(ulong owner, ulong target, float damage, sbyte angle, float knockback)
    {
        var at = State(target);
        Sim.Resolver.Spawn(new Hitbox
        {
            X = at.PX, Y = at.PY, Z = at.PZ, EndX = at.PX, EndY = at.PY, EndZ = at.PZ, Radius = .6f,
            Shape = HitboxShape.Sphere, Damage = damage, BaseKnockback = knockback, KnockbackAngle = angle,
            StunTicks = 20, DurationTicks = 1, OwnerId = owner, AttackSlot = AbilitySlots.Slot1,
        });
    }

    /// <summary>The flash F with numbers these tests own, and distinct declared cues to tell them apart.</summary>
    internal static void Pin(JsonObject character)
    {
        var p = Operation(character)["parameters"]!.AsObject();
        foreach (var (key, value) in new (string, double)[]
        {
            ("stanceTicks", 24), ("lockRange", 7), ("maxTargets", 3), ("legTicks", 6), ("passThroughDistance", 1.2),
            ("sheatheTicks", 30), ("revealLeadTicks", 18), ("recoveryTicks", 14), ("whiffTicks", 40),
        })
            p[key] = value;
        var cut = p["cutHitbox"]!.AsObject();
        foreach (var (key, value) in new (string, double)[]
        {
            ("radius", 1.3), ("damage", 16), ("angle", 35), ("baseKnockback", 12), ("knockbackGrowth", 42), ("stunTicks", 36),
        })
            cut[key] = value;
        p["lockPresentationId"] = Lock;
        p["revealPresentationId"] = Reveal;
        p["sheathePresentationId"] = Click;
        p["popPresentationId"] = Pop;
    }

    internal static JsonObject Source(Action<JsonObject>? edit = null)
    {
        var character = JsonNode.Parse(File.ReadAllText(SourcePath("character.json")))!.AsObject();
        edit?.Invoke(character);
        return character;
    }

    internal static string SourcePath(string file)
        => Path.Combine(TestPaths.Root, "client/Unity/Assets/CharacterPackages/wibou", file);

    internal static JsonObject Stage(JsonObject character)
        => character["slots"]!.AsArray().Single(s => (string?)s!["id"] == "ground.F")!["timeline"]!["stages"]![0]!.AsObject();

    internal static JsonObject Operation(JsonObject character)
        => Stage(character)["operations"]!.AsArray()
            .Single(o => (string?)o!["capabilityId"] == CharacterPackageCompiler.FlashCutCapabilityId)!.AsObject();
}
