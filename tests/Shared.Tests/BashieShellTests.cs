using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using F = SlopArena.Shared.Tests.BashieShellFixture;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Bashie's shell (ADR-0045): Shell Shift skates her straight, Shell Dive and Hydro Jet carry her into the skate,
/// the skate bowls fighters over, and normals, jumps, walls, shields and hits take her out.
/// </summary>
public sealed class BashieShellTests
{
    private const ulong Bashie = 1, Dummy = 2;
    private static readonly CharacterDefinition Def = F.Definition;
    private static float GroundY => TestHelpers.GroundPY(Def);
    private static float RunSpeed => Def.Movement.RunSpeed;

    [Fact]
    public void StandingShellShiftTucksThenPushesOffIntoATimedSkate()
    {
        var sim = Sim();
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        int tuck = 0;
        while (State(sim).State != ActionState.Shell && tuck++ < 20)
        {
            Assert.True(Tucked(sim), "the tuck move already counts as in the shell");
            Step(sim);
        }
        var skate = State(sim);
        Assert.Equal(ActionState.Shell, skate.State);
        Assert.Equal(F.SkateTicks, skate.StateTicks);
        Assert.InRange(skate.VZ, F.MinSkateSpeed - .001f, F.MinSkateSpeed + .001f);
        Assert.Equal(0f, skate.VX);
        Assert.Single(Windows(sim));
    }

    [Fact]
    public void ShellShiftFromARunSkatesStraightAtRunSpeedWhateverTheStick()
    {
        var sim = Sim(Running(RunSpeed));
        var sideways = new InputState { MoveX = 1f };
        Step(sim, sideways with { ActiveSlot = AbilitySlots.R });
        SkateOn(sim, sideways);
        for (int i = 0; i < 20; i++)
        {
            Step(sim, sideways);
            var state = State(sim);
            Assert.Equal(ActionState.Shell, state.State);
            Assert.Equal(0f, state.VX);
            Assert.InRange(state.VZ, RunSpeed - .001f, RunSpeed + .001f);
            Assert.Equal(0f, state.FacingYaw);
        }
    }

    [Fact]
    public void SkateRunsOutAndPopsUpReadyToAct()
    {
        var sim = Sim();
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        SkateOn(sim);
        int ticks = 0;
        while (State(sim).State == ActionState.Shell && ticks++ < 200) Step(sim);
        Assert.Equal(F.SkateTicks, ticks);
        Assert.Equal(ActionState.Idle, State(sim).State);
        Step(sim);
        Assert.Empty(Windows(sim));
        Step(sim, new InputState { ActiveSlot = AbilitySlots.Slot1 });
        Assert.Equal(ActionState.Attacking, State(sim).State);
    }

    [Theory]
    [InlineData(AbilitySlots.Slot1)]
    [InlineData(AbilitySlots.Slot3)]
    [InlineData(AbilitySlots.Slot4)]
    public void AGroundNormalPopsHerOutAtSkateSpeedThenBrakes(byte normal)
    {
        float skateSpeed = 16f;
        var sim = Sim(Running(skateSpeed));
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        SkateOn(sim);
        Step(sim, new InputState { ActiveSlot = normal });
        var popped = State(sim);
        Assert.Equal(ActionState.Attacking, popped.State);
        Assert.False(Tucked(sim));
        float brake = F.PopOutDeceleration * Simulation.TickDt;
        Assert.InRange(Speed(popped), skateSpeed - brake - .001f, skateSpeed - brake + .001f);
        Assert.Empty(Windows(sim));
        float previous = Speed(popped);
        bool hopped = false;
        for (int i = 0; i < 10; i++)
        {
            Step(sim);
            var state = State(sim);
            hopped |= state.PY > GroundY + .05f;
            Assert.InRange(Speed(state), previous - brake - .001f, previous - brake + .001f);
            previous = Speed(state);
        }
        if (normal == AbilitySlots.Slot4)
        {
            for (int i = 0; i < 10 && !hopped; i++) { Step(sim); hopped = State(sim).PY > GroundY + .05f; }
            Assert.True(hopped, "Rising Shell keeps its hop while it carries the skate speed");
        }
    }

    [Fact]
    public void AJumpHopsOutOfTheSkateKeepingItsSpeed()
    {
        float skateSpeed = 18f;
        var sim = Sim(Running(skateSpeed));
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        SkateOn(sim);
        Step(sim, new InputState { Jump = true, JumpHeld = true });
        Assert.Equal(ActionState.JumpSquat, State(sim).State);
        int ticks = 0;
        while (State(sim).IsGrounded && ticks++ < 20) Step(sim, new InputState { JumpHeld = true });
        var airborne = State(sim);
        Assert.False(airborne.IsGrounded);
        Assert.False(Tucked(sim));
        Assert.True(Speed(airborne) > RunSpeed + 1f, $"a hop out of the shell is not capped at RunSpeed ({Speed(airborne)})");
        float airFriction = Def.Movement.AirFriction * Simulation.TickDt;
        Assert.InRange(Speed(airborne), skateSpeed - airFriction - .001f, skateSpeed + .001f);
    }

    [Fact]
    public void NeutralHydroJetGoesStraightUpThenCoastsAndAnAirJumpHopsOut()
    {
        var sim = Sim();
        Step(sim, new InputState { ActiveSlot = AbilitySlots.E });
        var jet = State(sim);
        Assert.True(Tucked(sim));
        Assert.Equal(0f, jet.VX, 3);
        Assert.Equal(0f, jet.VZ, 3);
        Assert.InRange(jet.VY, F.JetSpeed - .001f, F.JetSpeed + .001f);
        int ticks = 0;
        while (State(sim).State == ActionState.Attacking && ticks++ < 40)
        {
            Assert.True(Tucked(sim));
            Step(sim);
        }
        var coast = State(sim);
        Assert.Equal(ActionState.Shell, coast.State);
        Assert.False(coast.IsGrounded);
        Assert.Equal(0, coast.StateTicks);
        Step(sim, new InputState { Jump = true });
        Assert.Equal(ActionState.Idle, State(sim).State);
        Assert.False(Tucked(sim));
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
    public void RMidJetDivesAtTheJetSpeedWithoutUntuckingAndLandsIntoTheSkate()
    {
        var sim = Sim();
        Step(sim, new InputState { ActiveSlot = AbilitySlots.E, MoveY = 1f });
        for (int i = 0; i < F.JetIasa; i++) { Assert.True(Tucked(sim)); Step(sim); }
        ushort eCooldownBefore = State(sim).GetCooldown(AbilitySlots.E);
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        var dive = State(sim);
        Assert.Equal(0, eCooldownBefore);
        Assert.True(dive.GetCooldown(AbilitySlots.E) > 0, "R cancelling the jet starts E's cooldown");
        Assert.Equal(0, dive.GetCooldown(AbilitySlots.R));
        float diveSpeed = MathF.Sqrt(dive.VX * dive.VX + dive.VY * dive.VY + dive.VZ * dive.VZ);
        // The jet's speed is kept, not added to the dive's; the dive then accelerates on its own.
        float firstTick = F.DiveAcceleration * Simulation.TickDt;
        Assert.InRange(diveSpeed, F.JetSpeed + firstTick - .01f, F.JetSpeed + firstTick + .01f);
        Assert.True(dive.VY < 0f);
        int ticks = 0;
        while (!(State(sim).State == ActionState.Shell && State(sim).IsGrounded) && ticks++ < 120)
        {
            Assert.True(Tucked(sim), $"tucked every tick from E to the skate (tick {ticks}, {State(sim).State})");
            Step(sim);
        }
        var skate = State(sim);
        Assert.Equal(ActionState.Shell, skate.State);
        Assert.True(skate.IsGrounded);
        Assert.Equal(F.SkateTicks, skate.StateTicks);
        Assert.True(skate.GetCooldown(AbilitySlots.R) > 0, "air R's cooldown starts at touchdown");
    }

    [Fact]
    public void HigherDivesSkateFasterUpToTheCap()
    {
        float low = DiveLandingSpeed(2.5f), high = DiveLandingSpeed(7f);
        Assert.True(high > low + .5f, $"higher dive {high} vs lower {low}");
        Assert.True(low >= F.MinSkateSpeed && high <= F.MaxSkateSpeed);
    }

    [Fact]
    public void TheDiveLeansTowardATargetAheadButNotOneBehind()
    {
        var ahead = Sim(Airborne(4f), Dummy2(3f, 6f));
        Step(ahead, new InputState { ActiveSlot = AbilitySlots.R });
        Assert.InRange(State(ahead).FacingYaw, MathF.Atan2(3f, 6f) - .01f, MathF.Atan2(3f, 6f) + .01f);
        Assert.True(State(ahead).VX > 0f && State(ahead).VY < 0f);

        var behind = Sim(Airborne(4f), Dummy2(0f, -5f));
        Step(behind, new InputState { ActiveSlot = AbilitySlots.R });
        Assert.Equal(0f, State(behind).FacingYaw);
        Assert.Equal(0f, State(behind).VX, 3);
        Assert.True(State(behind).VZ > 0f);
    }

    [Fact]
    public void TheSkateBowlsAFighterUpOnceAndSkatesOnUnderThem()
    {
        var sim = Sim(Running(RunSpeed), Dummy2(0f, 4f));
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        int hits = 0;
        bool launched = false;
        for (int i = 0; i < 50 && State(sim).State == ActionState.Shell || i < TuckTicks + 2; i++)
        {
            Step(sim);
            hits += sim.LastTickHits.Count(h => h.OwnerEntityId == Bashie && h.TargetEntityId == Dummy && h.OwnerWindow);
            var dummy = sim.GetState(Dummy);
            launched |= dummy.KVY > 0f || dummy.VY > 0f && !dummy.IsGrounded;
            if (State(sim).PZ > 6f) break;
        }
        Assert.Equal(1, hits);
        Assert.True(launched, "the victim is bowled up");
        Assert.Equal(ActionState.Shell, State(sim).State);
        Assert.True(State(sim).PZ > 5.5f, "she skated on past the victim");
    }

    [Fact]
    public void AHeadOnWallPopsHerUpBouncingBack()
    {
        var sim = Sim(Running(RunSpeed), arena: Floor(wallZ: 6f));
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        SkateOn(sim);
        int ticks = 0;
        while (State(sim).State == ActionState.Shell && ticks++ < 60) Step(sim);
        var stopped = State(sim);
        Assert.Equal(ActionState.Idle, stopped.State);
        Assert.InRange(stopped.VZ, -F.BlockedRecoilSpeed - .001f, -F.BlockedRecoilSpeed + .001f);
        Assert.True(stopped.PZ < 6f);
        Assert.Equal(0, stopped.AnimLockTicks);
    }

    [Fact]
    public void AShieldPopsHerUpAndBlockstunsTheShielder()
    {
        var sim = Sim(Running(RunSpeed), Dummy2(0f, 5f));
        var shield = new Dictionary<ulong, InputState> { [Dummy] = new InputState { ShieldHeld = true } };
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R }, shield);
        bool blocked = false;
        for (int i = 0; i < 60 && !blocked; i++)
        {
            Step(sim, default, shield);
            blocked = sim.LastTickHits.Any(h => h.Blocked && h.OwnerWindow);
        }
        Assert.True(blocked);
        Assert.True(sim.GetState(Dummy).BlockStunTicks > 0);
        Assert.Equal(ActionState.Idle, State(sim).State);
        Assert.True(State(sim).VZ < 0f, "she bounces back off the shield");
    }

    [Fact]
    public void AnyHitPopsHerOutOfTheShell()
    {
        var sim = Sim(Running(RunSpeed), Dummy2(5f, 0f));
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        SkateOn(sim);
        var me = State(sim);
        sim.Resolver.Spawn(new Hitbox
        {
            X = me.PX, Y = me.PY, Z = me.PZ + .3f, EndX = me.PX, EndY = me.PY, EndZ = me.PZ + .3f,
            Radius = 1f, Shape = HitboxShape.Sphere, Damage = 5f, BaseKnockback = 5f, KnockbackAngle = 30,
            StunTicks = 10, DurationTicks = 2, OwnerId = Dummy, AttackSlot = AbilitySlots.Slot1,
        });
        Step(sim);
        Assert.NotEqual(ActionState.Shell, State(sim).State);
        Step(sim);
        Assert.Empty(Windows(sim));
    }

    [Fact]
    public void WalkingOffALedgeCoastsOnTheRunningClockAndLandsBackInTheSkate()
    {
        var sim = Sim(Running(RunSpeed, z: 5f), arena: Floor(ledgeZ: 10f));
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        SkateOn(sim);
        ushort clock = State(sim).StateTicks;
        bool coasted = false;
        for (int i = 0; i < 80; i++)
        {
            Step(sim);
            var state = State(sim);
            Assert.Equal(ActionState.Shell, state.State);
            Assert.Equal(clock - 1, state.StateTicks);
            clock = state.StateTicks;
            coasted |= !state.IsGrounded;
            if (coasted && state.IsGrounded) break;
        }
        Assert.True(coasted);
        Assert.True(State(sim).IsGrounded, "she lands back in the skate with the clock still running");
        Assert.True(Speed(State(sim)) > RunSpeed, "the fall adds to the skate");
    }

    [Fact]
    public void RsCooldownStartsAtTheHandOffAndACoolingRChangesNothing()
    {
        var sim = Sim();
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        while (State(sim).State == ActionState.Attacking)
        {
            Assert.Equal(0, State(sim).GetCooldown(AbilitySlots.R));
            Step(sim);
        }
        Assert.Equal(F.RCooldown, State(sim).GetCooldown(AbilitySlots.R));
        Step(sim);
        var before = State(sim);
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        var after = State(sim);
        Assert.Equal(ActionState.Shell, after.State);
        Assert.Equal(before.StateTicks - 1, after.StateTicks);
        Assert.Equal(before.VZ, after.VZ);
    }

    [Fact]
    public void ShieldAndAirDodgeLeaveTheShell()
    {
        var ground = Sim();
        Step(ground, new InputState { ActiveSlot = AbilitySlots.R });
        SkateOn(ground);
        Step(ground, new InputState { ShieldHeld = true });
        Assert.Equal(ActionState.Shielding, State(ground).State);

        var air = Sim();
        Step(air, new InputState { ActiveSlot = AbilitySlots.E });
        while (State(air).State == ActionState.Attacking) Step(air);
        Assert.Equal(ActionState.Shell, State(air).State);
        Step(air, new InputState { ShieldPressed = true, MoveX = 1f });
        Assert.Equal(ActionState.AirDodgeMovement, State(air).State);
    }

    [Fact]
    public void TheCompilerAdmitsTheShellOnlyWhole()
    {
        Assert.Contains(Compile(c => c["shell"] = null), d => d.Code == "schema.missing");
        Assert.Contains(Compile(c => c["capabilityRequirements"]!.AsArray().RemoveAt(1)), d => d.Code == "capability.unknown");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.1")["shellPolicy"] = "tuck"), d => d.Code == "value.out-of-range");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.R")["allowSlideCarry"] = true), d => d.Code == "capability.ambiguous");
        Assert.Contains(Compile(c => F.SlotNamed(c, "ground.E")["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!["speed"] = 0),
            d => d.Code == "value.out-of-range");
        Assert.Contains(Compile(c => ((System.Text.Json.Nodes.JsonObject)c["presentation"]!).Remove("shell")),
            d => d.Code == "schema.missing");
        Assert.Empty(Compile(_ => { }).Where(d => d.Severity == CharacterDiagnosticSeverity.Error));
    }

    [Fact]
    public void ShellContentNeedsRuntimeApi112AndOtherPackagesWriteNoShellFields()
    {
        Assert.Equal("1.12.0", CompilePackage("bashie", c => { F.Apply(c); F.WithoutRing(c); }).Metadata.RuntimeApiMin);
        foreach (var package in new[] { "fightguy", "wibou", "manki", "bonk" })
        {
            var cooked = CompilePackage(package, null);
            if (Environment.GetEnvironmentVariable("SHELL_DUMP") is { } dump)
                System.IO.File.WriteAllBytes($"{dump}/{package}.json", cooked.CanonicalBytes);
            string text = System.Text.Encoding.UTF8.GetString(cooked.CanonicalBytes);
            Assert.DoesNotContain("\"shell", text);
            Assert.NotEqual("1.12.0", cooked.Metadata.RuntimeApiMin);
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
    public void HerLiveKitTucksOnRAndEAndJetsAndDivesThroughThrust()
    {
        var live = TestHelpers.ResolveSourceDef("bashie");
        Assert.NotNull(live.Shell);
        Assert.Equal("anim.bashie.gr-slide", live.ShellAnim);
        foreach (var id in new[] { "ground.R", "air.R", "ground.E", "air.E" })
            Assert.Equal(ShellPolicy.Tuck, live.CookedSlots!.Single(slot => slot.Id == id).ShellPolicy);
        foreach (var id in new[] { "ground.1", "ground.3", "ground.4", "air.1", "ground.A", "ground.F" })
            Assert.Equal(ShellPolicy.PopOut, live.CookedSlots!.Single(slot => slot.Id == id).ShellPolicy);
        foreach (var id in new[] { "air.R", "ground.E" })
            Assert.Contains(live.CookedSlots!.Single(slot => slot.Id == id).Timeline.Stages[0].Operations,
                op => op is CookedStartCapabilityOperation { CapabilityId: CharacterPackageCompiler.ThrustCapabilityId });

        var sim = new ServerSimulation(Floor());
        var me = Standing(); me.EntityId = Bashie;
        sim.RegisterEntity(Bashie, live, me);
        sim.Tick(new Dictionary<ulong, InputState> { [Bashie] = new InputState { ActiveSlot = AbilitySlots.E, MoveY = 1f } });
        int ticks = 0;
        while (ticks++ < 200 && !(sim.GetState(Bashie).State == ActionState.Shell && sim.GetState(Bashie).IsGrounded))
        {
            var state = sim.GetState(Bashie);
            Assert.True(Shell.IsTucked(state.State, sim.GetActiveMove(Bashie)?.Slot), $"tucked at tick {ticks} ({state.State})");
            bool pressR = state.State == ActionState.Attacking && Simulation.IsIasaUnlocked(in state, sim.GetActiveMove(Bashie))
                && state.AttackSlot == AbilitySlots.E;
            sim.Tick(new Dictionary<ulong, InputState> { [Bashie] = pressR ? new InputState { ActiveSlot = AbilitySlots.R } : default });
        }
        Assert.Equal(ActionState.Shell, sim.GetState(Bashie).State);
        Assert.True(sim.GetState(Bashie).IsGrounded, "Hydro Jet, then Shell Dive at its IASA, lands her into the skate");
    }

    private static float DiveLandingSpeed(float height)
    {
        var sim = Sim(Airborne(height));
        Step(sim, new InputState { ActiveSlot = AbilitySlots.R });
        int ticks = 0;
        while (!(State(sim).State == ActionState.Shell && State(sim).IsGrounded) && ticks++ < 120) Step(sim);
        Assert.Equal(ActionState.Shell, State(sim).State);
        return Speed(State(sim));
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

    private const ushort TuckTicks = F.TuckTicks;

    private static string RepoFile(string relative)
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, relative)))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory!.FullName, relative);
    }

    internal static ServerSimulation Sim(CharacterState? bashie = null, CharacterState? dummy = null, ArenaDefinition? arena = null)
    {
        var sim = new ServerSimulation(arena ?? Floor());
        var me = bashie ?? Standing();
        me.EntityId = Bashie;
        sim.RegisterEntity(Bashie, Def, me);
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

    /// <summary>Ticks through the tuck until she skates.</summary>
    internal static void SkateOn(ServerSimulation sim, InputState input = default)
    {
        int ticks = 0;
        while (State(sim).State != ActionState.Shell && ticks++ < 30) Step(sim, input);
        Assert.Equal(ActionState.Shell, State(sim).State);
    }

    internal static void Step(ServerSimulation sim, InputState input = default, Dictionary<ulong, InputState>? others = null)
    {
        var inputs = others == null ? new Dictionary<ulong, InputState>() : new Dictionary<ulong, InputState>(others);
        inputs[Bashie] = input;
        sim.Tick(inputs);
    }

    internal static CharacterState State(ServerSimulation sim) => sim.GetState(Bashie);
    private static bool Tucked(ServerSimulation sim) => Shell.IsTucked(State(sim).State, sim.GetActiveMove(Bashie)?.Slot);
    private static float Speed(in CharacterState s) => MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ);
    private static List<Hitbox> Windows(ServerSimulation sim)
        => sim.Resolver.GetActiveHitboxes().Where(h => h.OwnerWindow && h.OwnerId == Bashie).ToList();
}
