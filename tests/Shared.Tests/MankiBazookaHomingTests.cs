using System.Numerics;
using System.Text.Json.Nodes;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class MankiBazookaHomingTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "content-cooked"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private static CharacterDefinition Manki()
    {
        var roster = BuiltInRosterManifestCodec.Load(Path.Combine(Root(), "content-cooked/roster/manifest.json"));
        var loaded = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(Root(), "content-cooked/manki"),
            roster.Resolve(CharacterClass.Manki)!.Requirement);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Diagnostics.Select(d => d.Code)));
        return loaded.ToCharacterDefinition(CharacterClass.Manki);
    }

    private static Hitbox Rocket() => new()
    {
        X = 20f, Y = 10f, Z = 20f, VZ = 30f, Radius = .6f,
        OwnerId = 1, DurationTicks = 60, HomingTargetEntityId = 100,
        HomingTurnCos = .99904823f, HomingTurnSin = .043619387f,
    };

    private static Vector3 Velocity(Hitbox h) => new(h.VX, h.VY, h.VZ);

    [Fact]
    public void RocketCurvesTowardMovingAirborneTargetWithinTurnLimitAndKeepsSpeed()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(Rocket());
        for (int tick = 0; tick < 20; tick++)
        {
            var before = Assert.Single(resolver.GetActiveHitboxes());
            var target = new Vector3(35f + tick * .2f, 20f + tick * .1f, 60f);
            var direction = Vector3.Normalize(target - new Vector3(before.X, before.Y, before.Z));
            float alignment = Vector3.Dot(Vector3.Normalize(Velocity(before)), direction);
            resolver.UpdateHomingProjectiles((_, _) => (true, target.X, target.Y, target.Z));
            var after = Assert.Single(resolver.GetActiveHitboxes());
            float turnCos = Vector3.Dot(Vector3.Normalize(Velocity(before)), Vector3.Normalize(Velocity(after)));
            Assert.InRange(turnCos, MathF.Cos(2.501f * MathF.PI / 180f), 1.000001f);
            Assert.True(Vector3.Dot(Vector3.Normalize(Velocity(after)), direction) > alignment);
            Assert.Equal(30f, Velocity(after).Length(), 3);
            resolver.Tick(new());
        }
        var curved = Assert.Single(resolver.GetActiveHitboxes());
        Assert.True(curved.X > 21f && curved.Y > 11f);
    }

    [Theory]
    [InlineData(false, 50f)]
    [InlineData(true, 19f)]
    [InlineData(true, 20f)]
    public void InvalidOrBehindTargetPermanentlyBreaksHoming(bool exists, float targetZ)
    {
        var resolver = new SpellResolver();
        var original = Rocket();
        resolver.Spawn(original);
        resolver.UpdateHomingProjectiles((_, _) => (exists, 30f, 10f, targetZ));
        var ballistic = Assert.Single(resolver.GetActiveHitboxes());
        Assert.Equal(0UL, ballistic.HomingTargetEntityId);
        Assert.Equal(Velocity(original), Velocity(ballistic));
        resolver.UpdateHomingProjectiles((_, _) => (true, 30f, 20f, 60f));
        Assert.Equal(Velocity(ballistic), Velocity(Assert.Single(resolver.GetActiveHitboxes())));
    }

    [Fact]
    public void RocketSnapsToTargetInsideTurnLimitWithoutOvershooting()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(Rocket());
        resolver.UpdateHomingProjectiles((_, _) => (true, 20f, 10.5f, 60f));
        var after = Assert.Single(resolver.GetActiveHitboxes());
        Assert.Equal(30f, Velocity(after).Length(), 4);
        Assert.Equal(.5f / 40f, after.VY / after.VZ, 5);
    }

    private static ServerSimulation Match()
    {
        var sim = TestHelpers.MakeSim();
        var def = Manki();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20f, 20f) with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(100, TestHelpers.EngineDef, TestHelpers.NpcState(20f, 38f) with { PY = 12f, IsGrounded = false });
        return sim;
    }

    private static InputState TargetInput(byte target = 100, bool fire = false)
        => new() { ActiveSlot = fire ? AbilitySlots.R : (byte)0, TargetEntityId = target };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PressChargesThenFiresWithTimedPresentation(bool held)
    {
        var sim = TestHelpers.MakeSim();
        var def = Manki();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20f, 20f) with
            { PY = TestHelpers.GroundPY(def), FacingYaw = MathF.PI / 2f });
        for (int tick = 0; tick <= 80; tick++)
        {
            sim.Tick(new() { [1] = new InputState
            {
                ActiveSlot = tick == 0 ? AbilitySlots.R : (byte)0,
                IsAiming = held, AimPitch = -9000, FacingYaw = 9000,
            } });
            var owner = sim.GetState(1);
            Assert.NotEqual(ActionState.Aiming, owner.State);
            if (sim.GetActiveAbility(1) != null && tick < 39) Assert.True(owner.IsAiming);
            var events = sim.GetPresentationEvents(clear: true);
            if (tick == 0)
            {
                Assert.Equal(ActionState.Attacking, owner.State);
                Assert.Equal((byte)1, owner.ComboStage);
                Assert.Equal((byte)1, sim.GetActiveAbility(1)!.AnimIndex);
                Assert.Contains(events, e => e.PresentationId == "presentation.manki.bazooka.ready");
            }
            if (tick < 39) Assert.Empty(sim.Resolver.GetActiveHitboxes());
            if (tick == 39)
            {
                var rocket = Assert.Single(sim.Resolver.GetActiveHitboxes());
                Assert.Equal(0UL, rocket.HomingTargetEntityId);
                Assert.Equal(30f, rocket.VX, 4);
                Assert.Equal(0f, rocket.VY + rocket.Gravity * Simulation.TickDt, 4);
                Assert.Equal(0f, rocket.VZ, 4);
                Assert.Equal((ushort)60, rocket.DurationTicks);
                Assert.Contains(events, e => e.PresentationId == "presentation.manki.bazooka.muzzle");
            }
            else Assert.DoesNotContain(events, e => e.PresentationId == "presentation.manki.bazooka.muzzle");
        }
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void LaunchKeepsPressTargetAndReadsItsCurrentChestPosition()
    {
        var sim = Match();
        sim.Tick(new() { [1] = TargetInput(fire: true), [100] = default });
        sim.RegisterEntity(101, TestHelpers.EngineDef, TestHelpers.NpcState(25f, 35f) with { PY = 8f, IsGrounded = false });
        for (int tick = 1; tick <= 39; tick++)
            sim.Tick(new() { [1] = TargetInput(101), [100] = default, [101] = new InputState { MoveX = 1f } });
        var rocket = Assert.Single(sim.Resolver.LastTickCheckedHitboxes, h => h.Explosion.HasValue);
        var owner = sim.GetState(1);
        var target = sim.GetState(100);
        var expected = Vector3.Normalize(new(target.PX - owner.PX,
            target.PY + TestHelpers.EngineDef.CapsuleHeight * .25f - owner.PY - 1f, target.PZ - owner.PZ));
        var launched = new Vector3(rocket.VX, rocket.VY + rocket.Gravity * Simulation.TickDt, rocket.VZ);
        Assert.False(owner.LockOn);
        Assert.Equal(100UL, rocket.HomingTargetEntityId);
        Assert.InRange(Vector3.Dot(expected, Vector3.Normalize(launched)), .99999f, 1.000001f);
    }

    [Fact]
    public void SnapshotRestoresHomingAndBallisticBreakIdenticallyAfterAbilityEnds()
    {
        var continuous = Match();
        for (int tick = 0; tick <= 39; tick++)
            continuous.Tick(new() { [1] = TargetInput(fire: tick == 0), [100] = default });
        var snapshot = continuous.CaptureSnapshot();
        var restored = Match();
        restored.RestoreSnapshot(snapshot);
        Assert.Equal(100UL, Assert.Single(restored.Resolver.GetActiveHitboxes()).HomingTargetEntityId);
        for (int tick = 40; tick <= 90; tick++)
        {
            foreach (var sim in new[] { continuous, restored })
            {
                var target = sim.GetState(100);
                target.PX = 22f + (tick - 40) * .12f;
                target.PY = 12f;
                target.PZ = tick >= 84 ? 10f : 70f;
                target.VY = 0f;
                sim.SetState(100, target);
                sim.Tick(new() { [1] = TargetInput(), [100] = default });
            }
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()), $"diverged at {tick}");
            if (tick == 78)
            {
                Assert.Null(restored.GetActiveAbility(1));
                Assert.Equal(100UL, Assert.Single(restored.Resolver.GetActiveHitboxes()).HomingTargetEntityId);
            }
        }
        Assert.Equal(0UL, Assert.Single(restored.Resolver.GetActiveHitboxes()).HomingTargetEntityId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedOrRespawnedTargetCannotBeReacquired(bool respawn)
    {
        var sim = Match();
        for (int tick = 0; tick <= 39; tick++)
            sim.Tick(new() { [1] = TargetInput(fire: tick == 0), [100] = default });
        if (respawn)
            sim.SetState(100, sim.GetState(100) with { PY = -100f, PZ = 250f, IsGrounded = false, VY = -10f });
        else
            sim.RemoveEntity(100);
        sim.Tick(new() { [1] = TargetInput() });
        Assert.Equal(0UL, Assert.Single(sim.Resolver.GetActiveHitboxes()).HomingTargetEntityId);
        if (respawn) Assert.Equal((byte)1, sim.GetState(100).Deaths);
        else sim.RegisterEntity(100, TestHelpers.EngineDef, TestHelpers.NpcState(25f, 60f));
        sim.Tick(new() { [1] = TargetInput(), [100] = default });
        Assert.Equal(0UL, Assert.Single(sim.Resolver.GetActiveHitboxes()).HomingTargetEntityId);
    }

    [Fact]
    public void ExplosionSelfDamageSurvivesAbilityEndAndSnapshot()
    {
        var sim = Match();
        ProjectileExplosion explosion = default;
        for (int tick = 0; tick <= 78; tick++)
        {
            sim.Tick(new() { [1] = TargetInput(fire: tick == 0), [100] = default });
            if (tick == 39) explosion = Assert.Single(sim.Resolver.GetActiveHitboxes()).Explosion!.Value;
        }
        Assert.Null(sim.GetActiveAbility(1));
        var owner = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 1, X = owner.PX, Y = owner.PY, Z = owner.PZ,
            DurationTicks = 1, Explosion = explosion, AttackSlot = AbilitySlots.R,
        });
        var restored = Match();
        restored.RestoreSnapshot(sim.CaptureSnapshot());
        for (int tick = 0; tick < 3; tick++) restored.Tick(new() { [1] = default, [100] = default });
        Assert.Equal((ushort)4, restored.GetState(1).DamagePercent);
    }

    [Fact]
    public void InterruptedAbilityLeavesTheRocketHomingAtItsLaunchTarget()
    {
        var sim = Match();
        for (int tick = 0; tick <= 39; tick++)
            sim.Tick(new() { [1] = TargetInput(fire: tick == 0), [100] = default });
        sim.SetState(1, sim.GetState(1) with { State = ActionState.Hitstun, HitstunTicks = 20 });
        sim.SetState(100, sim.GetState(100) with { PX = 24f });
        sim.Tick(new() { [1] = default, [100] = default });
        Assert.Null(sim.GetActiveAbility(1));
        var rocket = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(100UL, rocket.HomingTargetEntityId);
        Assert.True(rocket.VX > 0f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RocketCapturesArcadeBallOrCourseTarget(bool course)
    {
        var arena = TestHelpers.TestArena();
        if (course)
            arena.Arcade = ArcadeStageDefinition.ForCourse("homing", 1, new[] { "manki" },
                new CharacterCourseDefinition(new[] { new CourseTargetDefinition("crate", new(25f, 5f, 30f), .6f) },
                    Array.Empty<CourseCheckpointDefinition>(), new ArcadeVolume(new(60f, 1f, 60f), new(1f, 1f, 1f))));
        else
            arena.Arcade = new ArcadeStageDefinition("homing", 1, new[] { "manki" },
                new ArcadeBallDefinition(new(25f, 5f, 30f), new(1f, 1f, 0f), .5f, 0f, .5f, 1f));
        var sim = new ServerSimulation(arena, NoWinMatchRule.Instance);
        var def = Manki();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20f, 20f) with { PY = TestHelpers.GroundPY(def) });
        if (course)
        {
            sim.BeginCourseAttempt(1);
            sim.Tick(new());
            sim.Tick(new() { [1] = default });
        }
        byte target = course ? sim.CourseTargetInputAlias(0) : ServerSimulation.ArcadeBallTargetInputId;
        ulong expected = course ? sim.CourseTargetEntityId(0) : sim.ArcadeBall!.Value.EntityId;
        for (int tick = 0; tick <= 39; tick++) sim.Tick(new() { [1] = TargetInput(target, tick == 0) });
        var rocket = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(expected, rocket.HomingTargetEntityId);
        Assert.True(rocket.VX > 0f && rocket.VY > 0f && rocket.VZ > 0f);
    }

    [Fact]
    public void LegacyBazookaV2KeepsItsSixTickLaunch()
    {
        var sim = TestHelpers.MakeSim();
        var def = TestHelpers.ResolveSourceDef("manki", json =>
        {
            foreach (var requirement in json["capabilityRequirements"]!.AsArray())
                if ((string?)requirement!["capabilityId"] == "slop.internal.manki.bazooka.v3")
                    requirement["capabilityId"] = "slop.internal.manki.bazooka.v2";
            foreach (var slot in json["slots"]!.AsArray())
            foreach (var stage in slot!["timeline"]!["stages"]!.AsArray())
            foreach (var operation in stage!["operations"]!.AsArray())
                if ((string?)operation!["capabilityId"] == "slop.internal.manki.bazooka.v3")
                {
                    operation["capabilityId"] = "slop.internal.manki.bazooka.v2";
                    operation["parameters"]!.AsObject().Remove("chargeTicks");
                }
        });
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20, 20) with { PY = TestHelpers.GroundPY(def) });
        for (int i = 0; i < 6; i++)
        {
            sim.Tick(new() { [1] = TargetInput(0, i == 0) });
            Assert.False(sim.GetState(1).IsAiming);
            if (i < 5) Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }
        Assert.Single(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void HitCancelsChargeBeforeAnyRocketLaunches()
    {
        var sim = Match();
        sim.Tick(new() { [1] = TargetInput(fire: true) });
        var owner = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 100, X = owner.PX, Y = owner.PY, Z = owner.PZ,
            Radius = 2, Damage = 1, StunTicks = 15, DurationTicks = 1 });
        for (int i = 0; i < 80; i++) sim.Tick(new() { [1] = TargetInput() });
        Assert.Null(sim.GetActiveAbility(1));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == 1);
        Assert.DoesNotContain(sim.GetPresentationEvents(), e => e.PresentationId == "presentation.manki.bazooka.muzzle");
    }

    [Fact]
    public void MidChargeSnapshotRestoresCapturedLockAndLaunch()
    {
        var sim = Match();
        for (int i = 0; i < 15; i++) sim.Tick(new() { [1] = TargetInput(fire: i == 0) });
        Assert.Equal(100UL, sim.GetState(1).TargetEntityId);
        Assert.InRange(sim.GetState(1).ChargeTicks, 14, 16);
        var snapshot = sim.CaptureSnapshot();
        for (int i = 0; i < 30; i++) sim.Tick(new() { [1] = TargetInput(0) });
        var expected = sim.CaptureSnapshot();
        sim.RestoreSnapshot(snapshot);
        for (int i = 0; i < 30; i++) sim.Tick(new() { [1] = TargetInput(0) });
        Assert.True(expected.ContentEquals(sim.CaptureSnapshot()));
        Assert.Equal(100UL, Assert.Single(sim.Resolver.GetActiveHitboxes()).HomingTargetEntityId);
    }

    [Fact]
    public void RecoveryClearsChargeProgressSoTheReticleCannotReappear()
    {
        var sim = Match();
        for (int i = 0; i < 60; i++) sim.Tick(new() { [1] = TargetInput(fire: i == 0) });
        Assert.Equal(0, sim.GetState(1).AttackElapsedTicks);
        Assert.Equal(0, sim.GetState(1).ChargeTicks);
    }

    [Fact]
    public void NoTargetAtPressNeverAcquiresALaterTarget()
    {
        var sim = Match();
        sim.Tick(new() { [1] = TargetInput(0, true) });
        for (int i = 1; i < 40; i++) sim.Tick(new() { [1] = TargetInput() });
        Assert.Equal(0UL, Assert.Single(sim.Resolver.GetActiveHitboxes()).HomingTargetEntityId);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(90.1f)]
    public void CompilerRejectsOutOfRangeTurnRates(float turn)
    {
        var sourcePath = Path.Combine(Root(), "client/Unity/Assets/CharacterPackages/manki");
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(sourcePath, "character.json")))!;
        var slot = json["slots"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == "ground.R")!;
        slot["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!["homingTurnDegPerTick"] = turn;
        var compiled = CharacterPackageCompiler.Compile(File.ReadAllText(Path.Combine(sourcePath, "package.json")),
            json.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
        Assert.Null(compiled.CookedPackage);
        Assert.Contains(compiled.Diagnostics, d => d.Code == "value.out-of-range" && d.Path.EndsWith("homingTurnDegPerTick"));
    }
}
