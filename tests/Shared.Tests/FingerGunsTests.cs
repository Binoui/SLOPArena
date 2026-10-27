using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class FingerGunsTests
{
    private const ushort Fire = 4, Recovery = 10, Reload = 24;
    internal static readonly CookedFingerGunsCapabilityParameters Parameters = new(
        Fire, Recovery, 6, Reload, "anim.reload", 3f, 4f,
        new CookedProjectile(0f, 0f, 0.6f, 28f, 0f, 0.15f, 4f, 15f, 5f, 12f, 10, 30, 0f));

    [Fact]
    public void SixPressesFireSixShotsThenAutomaticallyReloadWithoutAnotherPress()
    {
        var sim = Sim();
        for (byte shot = 1; shot <= 6; shot++)
        {
            Press(sim);
            TestHelpers.TickDefault(sim, Fire - 2);
            Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.AgeTicks == 1);
            TestHelpers.TickDefault(sim, 1);
            var projectile = Assert.Single(sim.Resolver.GetActiveHitboxes(), h => h.AgeTicks == 1);
            Assert.Equal(Parameters.Projectile.Damage, projectile.Damage);
            Assert.Equal(AbilitySlots.A, projectile.AttackSlot);
            Assert.Equal(0, projectile.VisualOperationIndex);
            Assert.Equal(sim.GetLastActivationId(1), projectile.ActivationId);
            Assert.Equal(shot, sim.GetState(1).FingerGunsShotsSpent);
            if (shot < 6)
            {
                Assert.Equal(ActionState.Attacking, TestHelpers.TickDefault(sim, Recovery - 1).State);
                Assert.Equal(ActionState.Idle, TestHelpers.TickDefault(sim, 1).State);
            }
        }
        AssertReload(sim);
        Assert.Equal((byte)6, TestHelpers.TickDefault(sim, Reload - 1).FingerGunsShotsSpent);
        AssertReload(sim);
        Assert.Equal((byte)0, TestHelpers.TickDefault(sim, 1).FingerGunsShotsSpent);
        Assert.Null(sim.GetActiveAbility(1));
        Press(sim);
        TestHelpers.TickDefault(sim, Fire - 1);
        Assert.Equal((byte)1, sim.GetState(1).FingerGunsShotsSpent);
    }

    [Fact]
    public void CountPersistsBetweenUsesAndNeverRefillsWhileIdle()
    {
        var sim = Sim();
        Press(sim);
        TestHelpers.TickDefault(sim, Fire + Recovery - 1);
        Assert.Equal((byte)1, TestHelpers.TickDefault(sim, 1000).FingerGunsShotsSpent);
        Press(sim);
        TestHelpers.TickDefault(sim, Fire - 1);
        Assert.Equal((byte)2, sim.GetState(1).FingerGunsShotsSpent);
    }

    [Theory]
    [InlineData(ActionState.Hitstun)]
    [InlineData(ActionState.Idle)]
    public void InterruptedReloadStaysEmptyAndNextPressOnlyReloads(ActionState successor)
    {
        var sim = Sim(spent: 5);
        Press(sim);
        TestHelpers.TickDefault(sim, Fire - 1);
        AssertReload(sim);
        TestHelpers.TickDefault(sim, Reload / 2);
        var interrupted = sim.GetState(1) with { State = successor, HitstunTicks = (ushort)(successor == ActionState.Hitstun ? 5 : 0) };
        sim.SetState(1, interrupted);
        TestHelpers.TickDefault(sim, 1);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal((byte)6, TestHelpers.TickDefault(sim, 1000).FingerGunsShotsSpent);
        Press(sim);
        AssertReload(sim);
        Assert.Equal((byte)6, TestHelpers.TickDefault(sim, Reload - 2).FingerGunsShotsSpent);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        TestHelpers.TickDefault(sim, 1);
        Assert.Equal((byte)0, sim.GetState(1).FingerGunsShotsSpent);
        Assert.Null(sim.GetActiveAbility(1));
    }

    [Fact]
    public void ARealHitInterruptsReloadWithoutRefilling()
    {
        var sim = Sim(spent: 6);
        var def = Definition();
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(25f, 20f) with { EntityId = 2, PY = TestHelpers.GroundPY(def) });
        Press(sim);
        var target = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 2, X = target.PX, Y = target.PY, Z = target.PZ,
            EndX = target.PX, EndY = target.PY, EndZ = target.PZ,
            Radius = 1f, Shape = HitboxShape.Sphere, Damage = 3f,
            BaseKnockback = 4f, KnockbackGrowth = 4f, KnockbackAngle = 35,
            StunTicks = 10, DurationTicks = 1,
        });
        TestHelpers.TickDefault(sim, 1);
        Assert.True(sim.GetState(1).HitstopTicks > 0 || sim.GetState(1).HitstunTicks > 0);
        TestHelpers.TickDefault(sim, 100);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal((byte)6, sim.GetState(1).FingerGunsShotsSpent);
    }

    [Fact]
    public void CancellationBeforeTheFireTickDoesNotSpendAShot()
    {
        var sim = Sim(spent: 2);
        Press(sim);
        sim.SetState(1, sim.GetState(1) with { State = ActionState.Hitstun, HitstunTicks = 5 });
        TestHelpers.TickDefault(sim, 1);
        Assert.Equal((byte)2, sim.GetState(1).FingerGunsShotsSpent);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void RespawnRefillsAnEmptyInterruptedCylinder()
    {
        var sim = Sim(spent: 6);
        Press(sim);
        sim.SetState(1, sim.GetState(1) with { PX = -100f, PY = -100f, IsGrounded = false });
        TestHelpers.TickDefault(sim, 1);
        Assert.Equal((byte)1, sim.GetState(1).Deaths);
        Assert.Equal((byte)0, sim.GetState(1).FingerGunsShotsSpent);
        Assert.Null(sim.GetActiveAbility(1));
    }

    [Fact]
    public void ReloadIsCommittedPastTheAuthoredStageAndCannotJumpShieldDashOrFire()
    {
        var sim = Sim(spent: 6);
        Press(sim);
        var attempts = new InputState
        {
            ActiveSlot = AbilitySlots.E, Jump = true, JumpHeld = true,
            ShieldPressed = true, ShieldHeld = true, DashPressed = true, GrabPressed = true,
            MoveY = 127,
        };
        for (int i = 1; i < Reload - 1; i++)
        {
            TestHelpers.TickN(sim, attempts, 1);
            AssertReload(sim);
            Assert.True(sim.GetState(1).IsGrounded);
            Assert.Equal((byte)6, sim.GetState(1).FingerGunsShotsSpent);
        }
        Assert.Equal((byte)0, TestHelpers.TickDefault(sim, 1).FingerGunsShotsSpent);
    }

    [Fact]
    public void AirFallIsCappedThroughShotRecoveryAndReloadAndEveryShotRecoils()
    {
        var sim = Sim(air: true, spent: 4);
        sim.SetState(1, sim.GetState(1) with { VY = -20f, IsFastFalling = true });
        Press(sim);
        for (int i = 1; i < Fire + Recovery; i++)
        {
            var s = TestHelpers.TickDefault(sim, 1);
            Assert.InRange(s.VY, -Parameters.AirMaxFallSpeed, 0f);
            if (i >= Fire - 1) Assert.Equal(-Parameters.AirRecoilSpeed, s.VZ, 4);
        }
        sim.SetState(1, sim.GetState(1) with { VX = 5f, VZ = 5f });
        Press(sim);
        TestHelpers.TickDefault(sim, Fire - 1);
        Assert.Equal(-Parameters.AirRecoilSpeed, sim.GetState(1).VZ, 4);
        Assert.Equal(0f, sim.GetState(1).VX, 4);
        AssertReload(sim);
        for (int i = 0; i < Reload - 1; i++)
            Assert.InRange(TestHelpers.TickDefault(sim, 1).VY, -Parameters.AirMaxFallSpeed, 0f);
    }

    [Fact]
    public void AirMoveRetainsGravityWhileRising()
    {
        var sim = Sim(air: true);
        sim.SetState(1, sim.GetState(1) with { VY = 5f });
        Press(sim);
        Assert.InRange(TestHelpers.TickDefault(sim, 2).VY, 0f, 4.9f);
    }

    [Fact]
    public void GroundMoveAndLandingDuringReloadStayPlanted()
    {
        var sim = Sim(spent: 5);
        sim.SetState(1, sim.GetState(1) with { VX = 6f, VZ = 8f });
        Press(sim);
        var planted = TestHelpers.TickDefault(sim, Fire + Reload - 1);
        Assert.Equal(20f, planted.PX);
        Assert.Equal(20f, planted.PZ);
        Assert.Equal(0f, planted.VX);
        Assert.Equal(0f, planted.VZ);

        var landing = Sim(air: true, spent: 6);
        Press(landing);
        landing.SetState(1, landing.GetState(1) with { PY = TestHelpers.GroundPY(Definition()) + .001f, VY = -3f, VX = 4f, VZ = -4f });
        TestHelpers.TickDefault(landing, 1);
        Assert.True(landing.GetState(1).IsGrounded);
        Assert.Equal(0f, landing.GetState(1).VX);
        Assert.Equal(0f, landing.GetState(1).VZ);
        AssertReload(landing);
        Assert.Equal((byte)0, TestHelpers.TickDefault(landing, Reload - 2).FingerGunsShotsSpent);
    }

    [Fact]
    public void ManualAimUsesPressYawAndPitchEvenWhenCameraTurnsDuringStartup()
    {
        var sim = Sim(manualAim: true, air: true);
        TestHelpers.TickN(sim, new InputState { ActiveSlot = AbilitySlots.A, AimYaw = 9000, AimPitch = 3000, IsAiming = true }, 1);
        TestHelpers.TickN(sim, new InputState { AimYaw = -9000, AimPitch = -3000 }, Fire - 1);
        var shot = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(Parameters.Projectile.Speed * MathF.Cos(MathF.PI / 6f), shot.VX, 3);
        Assert.Equal(Parameters.Projectile.Speed * .5f, shot.VY, 3);
        Assert.InRange(shot.VZ, -.001f, .001f);
        Assert.Equal(-Parameters.AirRecoilSpeed, sim.GetState(1).VX, 3);
    }

    [Fact]
    public void WithoutAnAimModeShotTargetsTheOpponentAtLaunch()
    {
        var sim = Sim();
        var def = Definition();
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(25f, 20f) with { EntityId = 2, PY = TestHelpers.GroundPY(def) });
        sim.SetState(1, sim.GetState(1) with { TargetEntityId = 2, LockOn = true });
        Press(sim);
        TestHelpers.TickDefault(sim, Fire - 1);
        var shot = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.True(shot.VX > 27f);
        Assert.InRange(shot.VZ, -4f, 4f);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, Fire + 1)]
    [InlineData(5, Fire + Reload / 2)]
    [InlineData(6, Reload / 2)]
    public void CylinderClipAndRuntimeContinueIdenticallyThroughWorldSnapshot(byte spent, int ticks)
    {
        var continuous = Sim(air: true, spent: spent);
        Press(continuous);
        TestHelpers.TickDefault(continuous, ticks - 1);
        var restored = Sim(air: true);
        restored.RestoreSnapshot(new WorldSnapshot(continuous.CaptureSnapshot().Data.ToArray()));
        Assert.Equal(continuous.GetState(1).FingerGunsShotsSpent, restored.GetState(1).FingerGunsShotsSpent);
        for (int i = 0; i < Reload + Fire + Recovery; i++)
        {
            TestHelpers.TickDefault(continuous, 1);
            TestHelpers.TickDefault(restored, 1);
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()));
        }
    }

    [Theory]
    [InlineData("fireTriggerTick", 0)]
    [InlineData("fireTriggerTick", 121)]
    [InlineData("recoveryTicks", 0)]
    [InlineData("recoveryTicks", 241)]
    [InlineData("shotsPerReload", 0)]
    [InlineData("shotsPerReload", 25)]
    [InlineData("reloadTicks", 0)]
    [InlineData("reloadTicks", 601)]
    [InlineData("airMaxFallSpeed", -1)]
    [InlineData("airMaxFallSpeed", 31)]
    [InlineData("airRecoilSpeed", -1)]
    [InlineData("airRecoilSpeed", 31)]
    public void CompilerRejectsOutOfRangeParameters(string field, double value)
        => AssertError(Compile(p => p[field] = value), "value.out-of-range");

    [Fact]
    public void CompilerRejectsInvalidProjectileAndReloadClip()
    {
        AssertError(Compile(p => p["projectile"]!["speed"] = 0), "value.out-of-range");
        AssertError(Compile(p => p["projectile"]!["radius"] = 0), "value.out-of-range");
        AssertError(Compile(p => p["projectile"]!["maxFlightTicks"] = 0), "value.out-of-range");
        AssertError(Compile(p => p["reloadAnimationId"] = "anim.missing"), "reference.unresolved");
        AssertError(Compile(p => p["reloadAnimationId"] = "anim.double-kick"), "reference.unresolved");
        AssertError(Compile(p => p.Remove("reloadTicks")), "operation.parameter-missing");
        AssertError(Compile(p => p["regenTicks"] = 120), "operation.parameter-unknown");
    }

    [Fact]
    public void CompilerRejectsEarlyCancelChargePoolAndCompetingLifecycle()
    {
        AssertError(Compile(slot: s => s["timeline"]!["stages"]![0]!["iasaTicks"] = 1), "capability.ambiguous");
        AssertError(Compile(slot: s => s["behavior"] = "chargeAttack"), "capability.ambiguous");
        AssertError(Compile(slot: s => s["chargePool"] = new JsonObject { ["maxCharges"] = 6, ["regenTicks"] = 60 }), "capability.ambiguous");
        AssertError(Compile(slot: s => s["timeline"]!["stages"]![0]!["operations"]!.AsArray().Add(
            JsonNode.Parse("""{"kind":"completeTimeline","tick":1,"unit":"ticks"}"""))), "capability.ambiguous");
    }

    [Fact]
    public void PublicCapabilityCooksAtApi111AndRoundTripsSource()
    {
        Assert.True(CharacterPackageCompiler.IsPublicCapability(CharacterPackageCompiler.FingerGunsCapabilityId, "1"));
        Assert.False(CharacterPackageCompiler.IsPublicCapability(CharacterPackageCompiler.FingerGunsCapabilityId, "2"));
        var (manifest, character) = Source();
        // Strip other capabilities to exercise Workshop admission of this public capability.
        character["capabilityRequirements"] = new JsonArray(JsonNode.Parse("""{"capabilityId":"slop.ability.finger-guns.v1","capabilityVersion":"1"}"""));
        foreach (var slot in character["slots"]!.AsArray())
            if ((string?)slot!["id"] != "ground.A")
                foreach (var stage in slot["timeline"]!["stages"]!.AsArray())
                    stage!["operations"] = new JsonArray();
        var compiled = CharacterPackageCompiler.Compile(manifest, character.ToJsonString());
        Assert.NotNull(compiled.CookedPackage);
        Assert.Equal("1.11.0", compiled.CookedPackage!.Metadata.RuntimeApiMin);
        var source = CharacterPackageSourceCodec.Load(manifest, character.ToJsonString());
        Assert.True(source.IsValid);
        string json = CharacterPackageSourceCodec.SerializeCharacter(source.Source!.Character);
        Assert.Equal(compiled.CookedPackage.CanonicalBytes, CharacterPackageCompiler.Compile(manifest, json).CookedPackage!.CanonicalBytes);
    }

    private static void Press(ServerSimulation sim)
        => TestHelpers.TickN(sim, new InputState { ActiveSlot = AbilitySlots.A }, 1);

    private static void AssertReload(ServerSimulation sim)
    {
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.Equal((byte)1, sim.GetState(1).AnimIndex);
        var move = Assert.IsType<ActiveMoveView>(sim.GetActiveMove(1));
        Assert.Equal("anim.reload", move.Pose.AnimationId);
    }

    private static ServerSimulation Sim(bool air = false, byte spent = 0, bool manualAim = false)
    {
        var def = Definition(manualAim);
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20, 20) with
        {
            PY = TestHelpers.GroundPY(def) + (air ? 12f : 0f), IsGrounded = !air,
            AirTimeTicks = air ? (ushort)120 : (ushort)0, FingerGunsShotsSpent = spent,
        });
        return sim;
    }

    private static CharacterDefinition Definition(bool manualAim = false)
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        foreach (var address in CanonicalSlotProjection.All.Where(a => a.Id.EndsWith(".A", StringComparison.Ordinal)))
            slots[address.Ordinal] = new CookedSlotDefinition(address.Ordinal, address.Id, address.IsAirborne,
                "Finger Guns", "", "icon.a", AuthoringAbilityBehavior.MeleeCombo,
                manualAim ? AuthoringAimMode.CameraForward3D : AuthoringAimMode.None, 0, false, false,
                new CookedTimeline(new[] { new CookedStage(2, 0, 0, 0, 0,
                    new[] { "anim.fire", "anim.reload" }, new CookedTimelineOperation[]
                    {
                        new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                            CharacterPackageCompiler.FingerGunsCapabilityId, "1", Parameters),
                    }) }));
        return def with { CookedSlots = slots };
    }

    internal static void UseGuns(JsonObject character, Action<JsonObject>? edit = null)
    {
        ChargedStreamTests.UseStream(character);
        var slot = character["slots"]!.AsArray().Single(s => (string?)s!["id"] == "ground.A")!.AsObject();
        slot["name"] = "Finger Guns";
        slot["behavior"] = "meleeCombo";
        slot["cooldownTicks"] = 0;
        slot.Remove("aimAnimationId");
        var stage = slot["timeline"]!["stages"]![0]!;
        stage["animationIds"] = new JsonArray("anim.double-kick", "anim.low-kick");
        var op = stage["operations"]![0]!;
        op["capabilityId"] = CharacterPackageCompiler.FingerGunsCapabilityId;
        op["parameters"] = JsonNode.Parse("""
            {"fireTriggerTick":4,"recoveryTicks":10,"shotsPerReload":6,"reloadTicks":24,
             "reloadAnimationId":"anim.low-kick","airMaxFallSpeed":3,"airRecoilSpeed":4,
             "projectile":{"launchOffsetX":0,"launchOffsetY":0,"launchOffsetZ":0.6,"speed":28,"gravity":0,
               "radius":0.15,"damage":4,"angle":15,"baseKnockback":5,"knockbackGrowth":12,"stunTicks":10,
               "maxFlightTicks":30,"yawOffsetDegrees":0}}
            """);
        foreach (var req in character["capabilityRequirements"]!.AsArray())
            if ((string?)req!["capabilityId"] == CharacterPackageCompiler.ChargedStreamCapabilityId)
                req["capabilityId"] = CharacterPackageCompiler.FingerGunsCapabilityId;
        edit?.Invoke(slot);
    }

    private static (string Manifest, JsonObject Character) Source(Action<JsonObject>? edit = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        const string path = "client/Unity/Assets/CharacterPackages/fightguy";
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, path))) root = root.Parent;
        Assert.NotNull(root);
        string dir = Path.Combine(root!.FullName, path);
        var c = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "character.json")))!.AsObject();
        UseGuns(c, edit);
        return (File.ReadAllText(Path.Combine(dir, "package.json")), c);
    }

    private static CharacterCompileResult Compile(Action<JsonObject>? parameters = null, Action<JsonObject>? slot = null)
    {
        var (manifest, character) = Source(s =>
        {
            parameters?.Invoke(s["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!.AsObject());
            slot?.Invoke(s);
        });
        return CharacterPackageCompiler.Compile(manifest, character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
    }

    private static void AssertError(CharacterCompileResult result, string code)
    {
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error && d.Code == code);
    }
}
