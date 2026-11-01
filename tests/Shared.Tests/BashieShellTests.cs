using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using F = SlopArena.Shared.Tests.BashieShellFixture;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Bashie's shell (ADR-0050): every tucked landing splashes, the Cannonball hops onto where her target stood, the
/// dive and Hydro Jet land through the same splash, and the compiler admits the shell only whole. Her wave is in
/// <see cref="BashieWaveTests"/>.
/// </summary>
public sealed class BashieShellTests
{
    internal const ulong Bashie = 1, Dummy = 2, Third = 3;
    private static readonly CharacterDefinition Def = F.Definition;
    internal static float GroundY => TestHelpers.GroundPY(Def);
    private static float RunSpeed => Def.Movement.RunSpeed;

    [Fact]
    public void GroundRLandsWhereTheTargetStoodAndSplashesItUpward()
    {
        var sim = Sim(dummy: Dummy2(0f, 4f));
        Step(sim, Press(AbilitySlots.R));
        Assert.True(Tucked(sim), "the cannonball is a tuck move");
        var splash = RunToSplash(sim);
        var landed = State(sim);
        Assert.InRange(landed.PZ, 3.4f, 4.6f);
        Assert.InRange(landed.PX, -.2f, .2f);
        Assert.Equal(ActionState.Idle, landed.State);
        Assert.Equal(F.PopUpTicks, landed.LandingLagTicks);
        Assert.True(landed.GetCooldown(AbilitySlots.R) > 0, "R's cooldown starts at the splash");
        Assert.Equal(Dummy, Assert.Single(splash).TargetEntityId);
        Assert.Equal(F.SplashDamage, splash[0].Damage);
        Assert.True(Launched(sim, Dummy), "the splash launches upward");
    }

    [Fact]
    public void MovingAwayAfterTakeoffDodgesItAndSheStillPopsUp()
    {
        var sim = Sim(dummy: Dummy2(0f, 4f));
        var runAway = new Dictionary<ulong, InputState> { [Dummy] = new InputState { MoveY = 1f } };
        Step(sim, Press(AbilitySlots.R));
        var splash = RunToSplash(sim, runAway);
        Assert.Empty(splash);
        Assert.InRange(State(sim).PZ, 3.4f, 4.6f);
        Assert.Equal(F.PopUpTicks, State(sim).LandingLagTicks);
    }

    [Fact]
    public void NoTargetLandsAFixedDistanceAhead()
    {
        var sim = Sim();
        Step(sim, Press(AbilitySlots.R));
        Assert.Empty(RunToSplash(sim));
        Assert.InRange(State(sim).PZ, F.NoTargetDistance - .3f, F.NoTargetDistance + .3f);
        Assert.InRange(State(sim).PX, -.05f, .05f);
    }

    [Fact]
    public void FarTargetsAreClampedToMaxRange()
    {
        var sim = Sim(dummy: Dummy2(0f, 16f));
        Step(sim, Press(AbilitySlots.R));
        Assert.Empty(RunToSplash(sim));
        Assert.InRange(State(sim).PZ, F.MaxRange - .3f, F.MaxRange + .3f);
    }

    [Fact]
    public void ATargetOnAHigherPlatformIsReached()
    {
        var arena = CourseShellArena.FromBoxes(new[]
        {
            (-60f, -1f, -60f, 60f, 0f, 60f),
            (-3f, 0f, 4f, 3f, 1.5f, 10f),
        }, new SpawnPoint { X = 0f, Y = 1f, Z = 0f });
        var dummy = Dummy2(0f, 6f);
        dummy.PY += 1.5f;
        var sim = Sim(dummy: dummy, arena: arena);
        Step(sim, Press(AbilitySlots.R));
        var splash = RunToSplash(sim);
        Assert.InRange(State(sim).PY, GroundY + 1.5f - .02f, GroundY + 1.5f + .02f);
        Assert.Equal(Dummy, Assert.Single(splash).TargetEntityId);
    }

    [Fact]
    public void WithNoLandingByTheStageEndSheFallsFreeAndCanRecover()
    {
        // A deep pit past the ledge: the far box only lowers the blast zone.
        var pit = CourseShellArena.FromBoxes(new[] { (-60f, -1f, -60f, 60f, 0f, 10f), (50f, -41f, 50f, 60f, -40f, 60f) },
            new SpawnPoint { X = 0f, Y = 1f, Z = 0f });
        var sim = Sim(Standing(0f, 8f), Dummy2(0f, 14f), pit);
        Step(sim, Press(AbilitySlots.R));
        int ticks = 0;
        while (State(sim).State == ActionState.Attacking && ticks++ < F.CannonballTicks + 5) Step(sim);
        var free = State(sim);
        Assert.Equal(ActionState.Idle, free.State);
        Assert.False(free.IsGrounded);
        Assert.False(Tucked(sim));
        Assert.Equal(F.CannonballTicks, ticks + 1);
        Step(sim, Press(AbilitySlots.E));
        Assert.Equal(ActionState.Attacking, State(sim).State);
        Assert.Equal(AbilitySlots.E, State(sim).AttackSlot);
    }

    [Fact]
    public void AHitInFlightEndsItWithoutASplash()
    {
        var sim = Sim(dummy: Dummy2(0f, 6f));
        Step(sim, Press(AbilitySlots.R));
        while (State(sim).IsGrounded) Step(sim);
        Step(sim);
        var me = State(sim);
        sim.Resolver.Spawn(new Hitbox
        {
            X = me.PX, Y = me.PY, Z = me.PZ, EndX = me.PX, EndY = me.PY, EndZ = me.PZ,
            Radius = 1f, Shape = HitboxShape.Sphere, Damage = 5f, BaseKnockback = 5f, KnockbackAngle = 30,
            StunTicks = 20, DurationTicks = 2, OwnerId = Third, AttackSlot = AbilitySlots.Normal1,
        });
        var events = new List<TimelinePresentationEvent>();
        for (int i = 0; i < 80; i++)
        {
            Step(sim);
            Assert.DoesNotContain(sim.LastTickHits, h => h.OwnerEntityId == Bashie);
            events.AddRange(sim.GetPresentationEvents(clear: true));
        }
        Assert.DoesNotContain(events, e => e.Source == PresentationEventSource.CapabilityLanding);
        Assert.Equal(0, State(sim).LandingLagTicks);
    }

    [Fact]
    public void TheSplashHitsEachFighterOnceAndAShieldBlocksIt()
    {
        var sim = Sim(dummy: Dummy2(0f, 4.5f));
        var shielder = Dummy2(1f, 3.6f);
        shielder.EntityId = Third;
        sim.RegisterEntity(Third, TestHelpers.EngineDef, shielder);
        var shield = new Dictionary<ulong, InputState> { [Third] = new InputState { ShieldHeld = true } };
        Step(sim, Press(AbilitySlots.R) with { TargetEntityId = (byte)Dummy }, shield);
        var splash = RunToSplash(sim, shield, ticksAfter: F.SplashTicks + 2);
        Assert.Single(splash, h => h.TargetEntityId == Dummy && !h.Blocked);
        Assert.Single(splash, h => h.TargetEntityId == Third && h.Blocked);
        Assert.True(sim.GetState(Third).BlockStunTicks > 0 || sim.GetState(Third).HitstopTicks > 0);
        Assert.True(State(sim).LandingLagTicks > 0, "a block does not cut her pop-up short");
    }

    [Fact]
    public void FightersHighAboveTheSplashAreMissed()
    {
        var sim = Sim(dummy: Dummy2(0f, 4f));
        var high = Dummy2(0f, 4f);
        high.EntityId = Third;
        high.PY += 3.5f;
        high.IsGrounded = false;
        sim.RegisterEntity(Third, TestHelpers.EngineDef, high);
        sim.NoGravityEntityId = Third;
        Step(sim, Press(AbilitySlots.R) with { TargetEntityId = (byte)Dummy });
        var splash = RunToSplash(sim, ticksAfter: F.SplashTicks + 1);
        Assert.Contains(splash, h => h.TargetEntityId == Dummy);
        Assert.DoesNotContain(splash, h => h.TargetEntityId == Third);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(10f, 0f)]
    [InlineData(25f, 0f)]
    [InlineData(5f, 1.5f)]
    [InlineData(5f, -2f)]
    public void TheSolvedArcLandsOnItsPointAtItsPlannedTick(float distance, float rise)
    {
        var p = F.CannonballParameters;
        float gravity = Def.Movement.Gravity;
        var arc = Abilities.CannonballFlight.Solve(0f, 0f, 0f, .3f, 0f, rise, distance, p, gravity);
        float clamped = MathF.Min(distance, p.MaxRange);
        Assert.Equal((ushort)MathF.Round(p.MinFlightTicks + (p.MaxFlightTicks - p.MinFlightTicks) * clamped / p.MaxRange), arc.FlightTicks);
        Assert.Equal(distance > 0f ? 0f : .3f, arc.Yaw, 4);
        Assert.InRange(arc.HorizontalSpeed * arc.FlightTicks * Simulation.TickDt, clamped - .001f, clamped + .001f);
        // Her first flight tick leaves her support without gravity, as the simulation integrates it.
        float y = arc.VerticalSpeed * Simulation.TickDt, vy = arc.VerticalSpeed;
        for (int tick = 2; tick <= arc.FlightTicks; tick++) { vy -= gravity * Simulation.TickDt; y += vy * Simulation.TickDt; }
        if (rise >= 0f) Assert.InRange(y, rise - .001f, rise + .001f);
        else Assert.True(arc.VerticalSpeed > 0f, "she always hops up, even toward a lower target");
    }

    [Fact]
    public void TheLaunchCapLandsShort()
    {
        var p = F.CannonballParameters;
        var arc = Abilities.CannonballFlight.Solve(0f, 0f, 0f, 0f, 0f, 30f, 2f, p, Def.Movement.Gravity);
        Assert.Equal(p.MaxLaunchSpeed, arc.VerticalSpeed);
    }

    [Fact]
    public void AirRDivesTowardTheTargetAndSplashesWithNoSkate()
    {
        var sim = Sim(Airborne(4f), Dummy2(1.2f, 3.2f));
        Step(sim, Press(AbilitySlots.R));
        Assert.InRange(State(sim).FacingYaw, MathF.Atan2(1.2f, 3.2f) - .01f, MathF.Atan2(1.2f, 3.2f) + .01f);
        var splash = RunToSplash(sim);
        Assert.Equal(ActionState.Idle, State(sim).State);
        Assert.Equal(F.PopUpTicks, State(sim).LandingLagTicks);
        Assert.Equal(0f, Speed(State(sim)));
        Assert.Single(splash);
        Step(sim);
        Assert.NotEqual(ActionState.Shell, State(sim).State);
    }

    [Fact]
    public void TheDiveLeansTowardATargetAheadButNotOneBehind()
    {
        var ahead = Sim(Airborne(4f), Dummy2(3f, 6f));
        Step(ahead, Press(AbilitySlots.R));
        Assert.InRange(State(ahead).FacingYaw, MathF.Atan2(3f, 6f) - .01f, MathF.Atan2(3f, 6f) + .01f);
        Assert.True(State(ahead).VX > 0f && State(ahead).VY < 0f);

        var behind = Sim(Airborne(4f), Dummy2(0f, -5f));
        Step(behind, Press(AbilitySlots.R));
        Assert.Equal(0f, State(behind).FacingYaw);
        Assert.Equal(0f, State(behind).VX, 3);
        Assert.True(State(behind).VZ > 0f);
    }

    [Fact]
    public void RMidJetKeepsTheJetSpeedWithoutUntuckingAndSplashes()
    {
        var sim = Sim();
        Step(sim, new InputState { ActiveSlot = AbilitySlots.E, MoveY = 1f });
        for (int i = 0; i < F.JetIasa; i++) { Assert.True(Tucked(sim)); Step(sim); }
        Step(sim, Press(AbilitySlots.R));
        var dive = State(sim);
        Assert.True(dive.GetCooldown(AbilitySlots.E) > 0, "R cancelling the jet starts E's cooldown");
        float diveSpeed = MathF.Sqrt(dive.VX * dive.VX + dive.VY * dive.VY + dive.VZ * dive.VZ);
        float firstTick = F.DiveAcceleration * Simulation.TickDt;
        Assert.InRange(diveSpeed, F.JetSpeed + firstTick - .01f, F.JetSpeed + firstTick + .01f);
        int ticks = 0;
        var events = new List<TimelinePresentationEvent>();
        while (State(sim).State == ActionState.Attacking && ticks++ < 120)
        {
            Assert.True(Tucked(sim), $"tucked every tick from E to the splash (tick {ticks})");
            Step(sim);
            events.AddRange(sim.GetPresentationEvents(clear: true));
        }
        Assert.True(State(sim).IsGrounded);
        Assert.Equal(F.PopUpTicks, State(sim).LandingLagTicks);
        Assert.Contains(events, e => e.Source == PresentationEventSource.CapabilityLanding
            && e.PresentationId == "presentation.bashie.shell.plop");
    }

    [Fact]
    public void HydroJetEndsInAnOrdinaryFall()
    {
        var sim = Sim();
        Step(sim, Press(AbilitySlots.E));
        Assert.True(Tucked(sim));
        Assert.InRange(State(sim).VY, F.JetSpeed - .001f, F.JetSpeed + .001f);
        while (State(sim).State == ActionState.Attacking) Step(sim);
        Assert.Equal(ActionState.Idle, State(sim).State);
        Assert.False(State(sim).IsGrounded);
        Assert.False(Tucked(sim));
        int ticks = 0;
        while (!State(sim).IsGrounded && ticks++ < 200) Step(sim);
        Assert.Equal(0, State(sim).LandingLagTicks);
        Assert.Empty(Windows(sim));
    }

    [Fact]
    public void AHydroJetThatMeetsTheGroundSplashes()
    {
        var def = TestHelpers.ResolveSourceDef("bashie", c =>
        {
            F.Apply(c);
            F.SlotNamed(c, "ground.E")["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!["stickPitchDegrees"] = -30;
        });
        var sim = new ServerSimulation(Floor());
        var me = Airborne(2f);
        me.EntityId = Bashie;
        sim.RegisterEntity(Bashie, def, me);
        Step(sim, new InputState { ActiveSlot = AbilitySlots.E, MoveY = 1f });
        int ticks = 0;
        while (State(sim).State == ActionState.Attacking && ticks++ < F.JetTicks) Step(sim);
        Assert.True(ticks < F.JetTicks, "the jet met the ground before it ended");
        Assert.True(State(sim).IsGrounded);
        Assert.Equal(F.PopUpTicks, State(sim).LandingLagTicks);
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(0f, -1f)]
    [InlineData(.7071f, .7071f)]
    public void HydroJetFollowsTheStickWithNoWindUp(float x, float z)
    {
        var sim = Sim();
        Step(sim, new InputState { ActiveSlot = AbilitySlots.E, MoveX = x, MoveY = z });
        var jet = State(sim);
        float pitch = F.JetStickPitch * MathF.PI / 180f;
        float length = MathF.Sqrt(x * x + z * z);
        Assert.InRange(jet.VX, x / length * F.JetSpeed * MathF.Cos(pitch) - .01f, x / length * F.JetSpeed * MathF.Cos(pitch) + .01f);
        Assert.InRange(jet.VZ, z / length * F.JetSpeed * MathF.Cos(pitch) - .01f, z / length * F.JetSpeed * MathF.Cos(pitch) + .01f);
        Assert.InRange(jet.VY, F.JetSpeed * MathF.Sin(pitch) - .01f, F.JetSpeed * MathF.Sin(pitch) + .01f);
        Assert.InRange(jet.FacingYaw, MathF.Atan2(x, z) - .001f, MathF.Atan2(x, z) + .001f);
    }

    [Fact]
    public void TheJetShovesWhoeverIsBehindHerAndNotWhoeverIsAhead()
    {
        var behind = Sim(dummy: Dummy2(0f, -1f));
        Step(behind, new InputState { ActiveSlot = AbilitySlots.E, MoveY = 1f });
        for (int i = 0; i < 3; i++) Step(behind);
        Assert.True(behind.GetState(Dummy).KVZ < -.5f || behind.GetState(Dummy).VZ < -.5f, "the jet shoves backward");

        var ahead = Sim(dummy: Dummy2(0f, 1.2f));
        Step(ahead, new InputState { ActiveSlot = AbilitySlots.E, MoveY = 1f });
        int hits = 0;
        for (int i = 0; i < 3; i++) { Step(ahead); hits += ahead.LastTickHits.Count(h => h.TargetEntityId == Dummy); }
        Assert.Equal(0, hits);
    }

    [Fact]
    public void OneCurrentTargetForTheCannonballTheDiveAndTheRide()
    {
        // The closest fighter is the current target; selecting another one wins while it is in lock range.
        foreach (byte selected in new byte[] { 0, (byte)Third })
        {
            float yaw = selected == 0 ? MathF.Atan2(-2f, 3f) : MathF.Atan2(4f, 5f);
            foreach (var (start, slot) in new[] { (Standing(), AbilitySlots.R), (Airborne(4f), AbilitySlots.R), (Standing(), AbilitySlots.F) })
            {
                var sim = Sim(start, Dummy2(-2f, 3f));
                var other = Dummy2(4f, 5f);
                other.EntityId = Third;
                sim.RegisterEntity(Third, TestHelpers.EngineDef, other);
                var input = Press(slot) with { TargetEntityId = selected };
                // Her client sends its selection every tick; the dive reads the target the last tick resolved.
                Step(sim, new InputState { TargetEntityId = selected });
                Step(sim, input);
                for (int i = 0; i < F.WhistleTicks + 6 && State(sim).State != ActionState.Shell && State(sim).IsGrounded == start.IsGrounded && slot == AbilitySlots.F; i++)
                    Step(sim, new InputState { TargetEntityId = selected });
                for (int i = 0; i < F.TakeoffTick + 2 && slot == AbilitySlots.R && start.IsGrounded; i++)
                    Step(sim, new InputState { TargetEntityId = selected });
                Assert.InRange(State(sim).FacingYaw, yaw - .02f, yaw + .02f);
            }
        }
    }

    [Fact]
    public void TheCompilerAdmitsTheShellOnlyWhole()
    {
        Assert.Contains(Compile(c => c["shell"] = null), d => d.Code == "schema.missing");
        Assert.Contains(Compile(c => c["capabilityRequirements"]!.AsArray().RemoveAt(1)), d => d.Code == "capability.unknown");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.1")["shellPolicy"] = "tuck"), d => d.Code == "value.out-of-range");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.1")["shellPolicy"] = "ride"), d => d.Code == "value.out-of-range");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.E")["allowSlideCarry"] = true), d => d.Code == "capability.ambiguous");
        Assert.Contains(Compile(c => ((System.Text.Json.Nodes.JsonObject)c["presentation"]!).Remove("shell")),
            d => d.Code == "schema.missing");
        // A splash exists exactly when a slot tucks, a ride exactly when one special rides.
        Assert.Contains(Compile(c => ((System.Text.Json.Nodes.JsonObject)c["shell"]!).Remove("splash")), d => d.Code == "capability.ambiguous");
        Assert.Contains(Compile(c => ((System.Text.Json.Nodes.JsonObject)c["shell"]!).Remove("ride")), d => d.Code == "capability.ambiguous");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.F")["shellPolicy"] = "popOut"), d => d.Code == "capability.ambiguous");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.A")["shellPolicy"] = "ride"), d => d.Code is "capability.ambiguous");
        // The ride is the shell's: a ride move carries no motion or capability of its own.
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.F")["timeline"]!["stages"]![0]!["iasaTicks"] = 4), d => d.Code == "capability.ambiguous");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.F")["timeline"]!["stages"]![0]!["operations"]!.AsArray().Add(
            System.Text.Json.Nodes.JsonNode.Parse("""{"kind":"setVelocity","tick":0,"unit":"ticks","velocityMode":"absolute","x":0,"y":0,"z":0}"""))),
            d => d.Code == "capability.ambiguous");
        Assert.Contains(Compile(c => c["shell"]!["ride"]!["wave"]!["stunTicks"] = 5), d => d.Code == "value.out-of-range");
        Assert.Contains(Compile(c => c["shell"]!["splash"]!["popUpTicks"] = 0), d => d.Code == "value.out-of-range");
        // The cannonball tucks and fits its stage.
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.R")["shellPolicy"] = "popOut"), d => d.Code == "capability.ambiguous");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.R")["timeline"]!["stages"]![0]!["durationTicks"] = 30), d => d.Code == "capability.ambiguous");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.R")["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!["minFlightTicks"] = 0),
            d => d.Code == "value.out-of-range");
        Assert.Empty(Compile(_ => { }).Where(d => d.Severity == CharacterDiagnosticSeverity.Error));
    }

    [Fact]
    public void ShellStanceV1IsRefusedWithItsRetirementReason()
    {
        var diagnostics = Compile(c => c["capabilityRequirements"]![1]!["capabilityId"] = "slop.ability.shell-stance.v1");
        Assert.Contains(diagnostics, d => d.Code == "capability.retired" && d.Message.Contains("shell-stance.v2"));
    }

    [Fact]
    public void ShellContentNeedsRuntimeApi114AndOtherPackagesWriteNoShellFields()
    {
        var bashie = CompilePackage("bashie", c => { F.Apply(c); F.WithoutRing(c); });
        Assert.Equal("1.14.0", bashie.Metadata.RuntimeApiMin);
        Assert.Contains("\"splash\"", System.Text.Encoding.UTF8.GetString(bashie.CanonicalBytes));
        foreach (var package in new[] { "fightguy", "wibou", "manki", "bonk", "salamander" })
        {
            var cooked = CompilePackage(package, null);
            string text = System.Text.Encoding.UTF8.GetString(cooked.CanonicalBytes);
            Assert.DoesNotContain("\"shell", text);
            Assert.NotEqual("1.14.0", cooked.Metadata.RuntimeApiMin);
            Assert.Null(cooked.Definition.Shell);
        }
    }

    [Fact]
    public void HydroJetReturnsHerFromBelowALedgeWithBothJumpsSpent()
    {
        var definition = TestHelpers.ResolveSourceDef("bashie");
        var arena = AimedRecoveryTests.FloorArena(100f);
        for (int z = 0; z < arena.Heightmap.Height; z++)
            for (int x = 100; x < arena.Heightmap.Width; x++)
                arena.Heightmap.Data[z * arena.Heightmap.Width + x] = float.MinValue;
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState(103f, 100f);
        state.PY = TestHelpers.GroundPY(definition) - 2f;
        state.VY = -11f;
        state.JumpsLeft = state.AirDodgesLeft = 0;
        state.IsGrounded = false;
        sim.RegisterEntity(1, definition, state);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { ActiveSlot = AbilitySlots.E, MoveX = -1f } });
        bool landed = false;
        for (int tick = 1; tick < 180; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { MoveX = -1f } });
            var current = sim.GetState(1);
            Assert.Equal((byte)0, current.Deaths);
            if (!current.IsGrounded) continue;
            Assert.True(current.PX < 100f);
            Assert.InRange(current.PY, TestHelpers.GroundPY(definition) - .01f, TestHelpers.GroundPY(definition) + .01f);
            landed = true;
            break;
        }
        Assert.True(landed, "recover onto the top surface; no ledge grab is involved");
    }

    [Fact]
    public void HerLiveKitTucksOnRAndERidesOnFAndCannonballsOnGroundR()
    {
        var live = TestHelpers.ResolveSourceDef("bashie");
        Assert.NotNull(live.Shell?.Splash);
        Assert.NotNull(live.Shell?.Ride);
        Assert.Equal("anim.bashie.gr-slide", live.ShellAnim);
        foreach (var id in new[] { "ground.R", "air.R", "ground.E", "air.E" })
            Assert.Equal(ShellPolicy.Tuck, live.CookedSlots!.Single(slot => slot.Id == id).ShellPolicy);
        foreach (var id in new[] { "ground.F", "air.F" })
            Assert.Equal(ShellPolicy.Ride, live.CookedSlots!.Single(slot => slot.Id == id).ShellPolicy);
        foreach (var id in new[] { "ground.1", "ground.2", "ground.3", "air.1", "ground.A" })
            Assert.Equal(ShellPolicy.PopOut, live.CookedSlots!.Single(slot => slot.Id == id).ShellPolicy);
        Assert.Contains(live.CookedSlots!.Single(slot => slot.Id == "ground.R").Timeline.Stages[0].Operations,
            op => op is CookedStartCapabilityOperation { CapabilityId: CharacterPackageCompiler.CannonballCapabilityId });
        foreach (var id in new[] { "air.R", "ground.E" })
            Assert.Contains(live.CookedSlots!.Single(slot => slot.Id == id).Timeline.Stages[0].Operations,
                op => op is CookedStartCapabilityOperation { CapabilityId: CharacterPackageCompiler.ThrustCapabilityId });
    }

    // ── helpers ──

    internal static InputState Press(byte slot) => new() { ActiveSlot = slot };

    /// <summary>Whistles (F) and ticks until she rides.</summary>
    internal static void RideOn(ServerSimulation sim, InputState input = default, Dictionary<ulong, InputState>? others = null)
    {
        Step(sim, input with { ActiveSlot = AbilitySlots.F }, others);
        int ticks = 0;
        while (State(sim).State != ActionState.Shell && ticks++ < F.WhistleTicks + 5) Step(sim, input, others);
        Assert.Equal(ActionState.Shell, State(sim).State);
    }

    /// <summary>Steps until her tucked move splashes, then a few ticks more; returns the splash's contacts.</summary>
    internal static List<SpellResolver.HitResult> RunToSplash(ServerSimulation sim,
        Dictionary<ulong, InputState>? others = null, int ticksAfter = 0, int maxTicks = 120)
    {
        var hits = new List<SpellResolver.HitResult>();
        int ticks = 0;
        while (State(sim).State == ActionState.Attacking && ticks++ < maxTicks)
        {
            Step(sim, default, others);
            hits.AddRange(sim.LastTickHits.Where(h => h.OwnerEntityId == Bashie && !h.OwnerWindow));
        }
        Assert.True(State(sim).IsGrounded && State(sim).LandingLagTicks == F.PopUpTicks, $"she splashed ({State(sim).State})");
        for (int i = 0; i < ticksAfter; i++)
        {
            Step(sim, default, others);
            hits.AddRange(sim.LastTickHits.Where(h => h.OwnerEntityId == Bashie && !h.OwnerWindow));
        }
        return hits;
    }

    internal static bool Launched(ServerSimulation sim, ulong id)
    {
        for (int i = 0; i < 20; i++)
        {
            var s = sim.GetState(id);
            if (s.KVY > 0f || s.VY > 0f && !s.IsGrounded) return true;
            Step(sim);
        }
        return false;
    }

    private static IReadOnlyList<CharacterDiagnostic> Compile(Action<System.Text.Json.Nodes.JsonObject> edit)
    {
        var (package, character) = Source("bashie");
        F.Apply(character);
        edit(character);
        return CharacterPackageCompiler.Compile(package, character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn).Diagnostics;
    }

    private static CookedCharacterPackage CompilePackage(string id, Action<System.Text.Json.Nodes.JsonObject>? edit)
    {
        var (package, character) = Source(id);
        edit?.Invoke(character);
        var result = CharacterPackageCompiler.Compile(package, character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.True(result.CookedPackage != null, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        return result.CookedPackage!;
    }

    private static (string Package, System.Text.Json.Nodes.JsonObject Character) Source(string id)
    {
        string root = $"client/Unity/Assets/CharacterPackages/{id}";
        return (System.IO.File.ReadAllText(RepoFile(root + "/package.json")),
            System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(RepoFile(root + "/character.json")))!.AsObject());
    }

    private static string RepoFile(string relative)
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, relative)))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory!.FullName, relative);
    }

    internal static ServerSimulation Sim(CharacterState? bashie = null, CharacterState? dummy = null, ArenaDefinition? arena = null,
        CharacterDefinition? def = null)
    {
        var sim = new ServerSimulation(arena ?? Floor());
        var me = bashie ?? Standing();
        me.EntityId = Bashie;
        sim.RegisterEntity(Bashie, def ?? Def, me);
        var other = dummy ?? Dummy2(30f, 30f);
        other.EntityId = Dummy;
        sim.RegisterEntity(Dummy, TestHelpers.EngineDef, other);
        return sim;
    }

    internal static ArenaDefinition Floor(float? wallZ = null, float? ledgeZ = null)
    {
        var boxes = new List<(float, float, float, float, float, float)>();
        if (ledgeZ is { } edge)
        {
            boxes.Add((-60f, -1f, -60f, 60f, 0f, edge));
            boxes.Add((-60f, -4f, edge, 60f, -3f, 60f));
        }
        else boxes.Add((-60f, -1f, -60f, 60f, 0f, 60f));
        if (wallZ is { } wall) boxes.Add((-60f, 0f, wall, 60f, 3f, wall + 1f));
        return CourseShellArena.FromBoxes(boxes, new SpawnPoint { X = 0f, Y = 1f, Z = 0f });
    }

    internal static CharacterState Standing(float x = 0f, float z = 0f)
    {
        var state = TestHelpers.PlayerState(x, z);
        state.PY = GroundY;
        return state;
    }

    internal static CharacterState Running(float speed, float z = 0f)
    {
        var state = Standing(0f, z);
        state.State = ActionState.Run;
        state.VZ = speed;
        return state;
    }

    internal static CharacterState Airborne(float height)
    {
        var state = Standing();
        state.PY = GroundY + height;
        state.IsGrounded = false;
        state.AirTimeTicks = Def.Movement.FloatWindowTicks;
        return state;
    }

    internal static CharacterState Dummy2(float x, float z)
    {
        var state = TestHelpers.PlayerState(x, z);
        state.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        state.FacingYaw = MathF.PI;
        return state;
    }

    internal static void Step(ServerSimulation sim, InputState input = default, Dictionary<ulong, InputState>? others = null)
    {
        var inputs = others == null ? new Dictionary<ulong, InputState>() : new Dictionary<ulong, InputState>(others);
        inputs[Bashie] = input;
        sim.Tick(inputs);
    }

    internal static CharacterState State(ServerSimulation sim) => sim.GetState(Bashie);
    internal static bool Tucked(ServerSimulation sim) => Shell.IsTucked(State(sim).State, sim.GetActiveMove(Bashie)?.Slot);
    internal static float Speed(in CharacterState s) => MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ);
    internal static List<Hitbox> Windows(ServerSimulation sim)
        => sim.Resolver.GetActiveHitboxes().Where(h => h.OwnerWindow && h.OwnerId == Bashie).ToList();
}
