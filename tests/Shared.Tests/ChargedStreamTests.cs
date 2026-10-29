using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>slop.ability.charged-stream.v1 (Bashie's Hose Down): pump, then spray a stream that pushes.</summary>
public sealed class ChargedStreamTests
{
    private const int FireTick = 4, Interval = 3, MinStream = 12, MaxStream = 45, Recovery = 14;
    private const byte SprayClip = 1, EndClip = 2;   // phase clips listed after the stage's own clip

    /// <summary>A charged stream's parameters, for contract tests that need one outside a package.</summary>
    internal static readonly CookedChargedStreamCapabilityParameters Parameters = new(60, FireTick, Interval, MinStream,
        MaxStream, Recovery, 40f, 1.5f, 4.5f,
        new CookedProjectile(0f, 0f, 0.6f, 16f, 0f, 0.35f, 0f, 0f, 22f, 0f, 0, 30, 0f),
        "anim.spray", 12, "anim.stop");

    [Theory]
    [InlineData(0, MinStream, 22f)]   // a tap
    [InlineData(20, 23, 28f)]         // a third of a pump: a third of the way to a full pump's stream and push
    [InlineData(60, MaxStream, 40f)]
    [InlineData(200, MaxStream, 40f)]
    public void ThePumpSetsTheStreamsLengthAndPush(int pumpTicks, int streamTicks, float push)
    {
        var sim = Sim(Definition(), grounded: true);
        Hold(sim, pumpTicks);
        var drops = new System.Collections.Generic.List<Hitbox>();
        for (int tick = 0; tick <= FireTick + MaxStream + 2; tick++)
        {
            TestHelpers.TickDefault(sim, 1);
            // The resolver ages hitboxes after abilities tick: this tick's drops are one tick old.
            drops.AddRange(sim.Resolver.GetActiveHitboxes().Where(h => h.AgeTicks == 1));
        }
        Assert.Equal((streamTicks + Interval - 1) / Interval, drops.Count);
        Assert.All(drops, drop => Assert.Equal(push, drop.BaseKnockback, 3));
        Assert.All(drops, drop => Assert.Equal(0f, drop.Damage));
        Assert.All(drops, drop => Assert.True(drop.VZ > 0f));
    }

    [Fact]
    public void TheMoveEndsAfterTheStreamAndItsRecovery()
    {
        var sim = Sim(Definition(), grounded: true);
        var released = TestHelpers.TickN(sim, new InputState { ActiveSlot = AbilitySlots.A }, 1);
        Assert.Equal(ActionState.Attacking, released.State);
        // Ticks after the release tick: the stream ends at FireTick + MinStream, the move after its recovery.
        Assert.Equal(SprayClip, released.ComboStage);
        AssertPhase(sim, SprayClip, "anim.ki-shot-loop");
        var spraying = TestHelpers.TickDefault(sim, FireTick + MinStream - 1);
        Assert.Equal(ActionState.Attacking, spraying.State);
        Assert.Equal(SprayClip, spraying.ComboStage);
        var ending = TestHelpers.TickDefault(sim, 1);
        Assert.Equal(EndClip, ending.ComboStage);
        AssertPhase(sim, EndClip, "anim.low-kick");
        Assert.Equal(ActionState.Attacking, TestHelpers.TickDefault(sim, Recovery - 1).State);
        Assert.NotEqual(ActionState.Attacking, TestHelpers.TickDefault(sim, 1).State);
    }

    [Theory]
    [InlineData(1, SprayClip, "anim.ki-shot-loop")]
    [InlineData(1 + FireTick + MinStream, EndClip, "anim.low-kick")]
    public void PhaseSelectionAndContinuationSurviveAWorldSnapshot(int ticks, byte clip, string animationId)
    {
        var definition = Definition();
        var continuous = Sim(definition, grounded: true);
        TestHelpers.TickN(continuous, new InputState { ActiveSlot = AbilitySlots.A }, 1);
        TestHelpers.TickDefault(continuous, ticks - 1);
        var restored = Sim(definition, grounded: true);
        restored.RestoreSnapshot(continuous.CaptureSnapshot());
        AssertPhase(continuous, clip, animationId);
        AssertPhase(restored, clip, animationId);
        for (int tick = 0; tick < FireTick + MinStream + Recovery; tick++)
        {
            TestHelpers.TickDefault(continuous, 1);
            TestHelpers.TickDefault(restored, 1);
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()));
        }
    }

    private static void AssertPhase(ServerSimulation simulation, byte clip, string animationId)
    {
        Assert.Equal(clip, simulation.GetState(1).AnimIndex);
        var move = Assert.IsType<ActiveMoveView>(simulation.GetActiveMove(1));
        Assert.Equal(clip, move.ClipIndex);
        Assert.Equal(animationId, move.Pose.AnimationId);
    }

    [Fact]
    public void TheSprayClipLoopsWhileTheStreamRuns()
    {
        var sim = Sim(Definition(), grounded: true);
        Hold(sim, 60);
        TestHelpers.TickDefault(sim, 1);
        var ticks = Enumerable.Range(0, 30).Select(_ => TestHelpers.TickDefault(sim, 1).AttackElapsedTicks).ToArray();
        Assert.All(ticks, tick => Assert.InRange(tick, 0, 11));
        Assert.Contains((ushort)0, ticks.Skip(5));
    }

    [Fact]
    public void InTheAirTheStreamPushesHerBackAndOnTheGroundSheStaysPut()
    {
        var air = Sim(Definition(), grounded: false);
        Hold(air, 60);
        TestHelpers.TickDefault(air, 1 + FireTick);
        var pushed = air.GetState(1);
        Assert.False(pushed.IsGrounded);
        Assert.Equal(-4.5f, pushed.VZ, 3);
        Assert.Equal(0f, pushed.VX, 3);

        var ground = Sim(Definition(), grounded: true);
        Hold(ground, 60);
        var planted = TestHelpers.TickDefault(ground, 1 + FireTick + 10);
        Assert.True(planted.IsGrounded);
        Assert.Equal(0f, planted.VX);
        Assert.Equal(0f, planted.VZ);
    }

    [Fact]
    public void AManuallyAimedStreamFacesTheAimWhilePumpingAndKeepsTheReleaseDirection()
    {
        var sim = Sim(Definition(slot => slot["aimMode"] = "cameraForward3D"), grounded: true);
        TestHelpers.TickHold(sim, new InputState { ActiveSlot = AbilitySlots.A, IsAiming = true, AimYaw = 9000 }, 20);
        Assert.Equal(MathF.PI / 2f, sim.GetState(1).FacingYaw, 3);
        // After the release the camera turns away; the stream keeps the release aim.
        TestHelpers.TickHold(sim, new InputState { AimYaw = -9000 }, 1 + FireTick);
        var drop = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.True(drop.VX > 15f);
        Assert.InRange(drop.VZ, -0.01f, 0.01f);
    }

    [Fact]
    public void AStreamPushesItsTargetWithoutHitstunOrHitstop()
    {
        var def = Definition();
        var sim = Sim(def, grounded: true);
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(20f, 22.5f) with { EntityId = 2, PY = TestHelpers.GroundPY(def) });
        Hold(sim, 60);
        CharacterState target = default;
        for (int tick = 0; tick < 20 && target.KVZ <= 0f; tick++)
        {
            TestHelpers.TickDefault(sim, 1);
            target = sim.GetState(2);
        }
        Assert.True(target.KVZ > 0f);
        Assert.Equal(0, target.HitstopTicks);
        Assert.Equal(0, target.HitstunTicks);
        Assert.Equal((ushort)0, target.DamagePercent);
        // Each drop pushes again: the target keeps sliding while the stream hits it.
        float before = sim.GetState(2).PZ;
        TestHelpers.TickDefault(sim, 15);
        Assert.True(sim.GetState(2).PZ > before + 0.5f);
    }

    [Fact]
    public void CooksUnderRuntimeApi18AndRoundTripsItsSource()
    {
        var (manifest, character) = Source();
        string json = character.ToJsonString();
        var compiled = CharacterPackageCompiler.Compile(manifest, json, CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compiled.CookedPackage != null,
            string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message} ({d.Path})")));
        Assert.Equal("1.8.0", compiled.CookedPackage!.Metadata.RuntimeApiMin);
        var stream = Assert.IsType<CookedChargedStreamCapabilityParameters>(compiled.CookedPackage.Definition.Slots
            .Single(slot => slot.Id == "ground.A").Timeline.Stages.Single().Operations
            .OfType<CookedStartCapabilityOperation>().Single().Parameters);
        Assert.Equal("anim.ki-shot-loop", stream.SprayAnimationId);
        Assert.Equal((ushort)12, stream.SprayLoopTicks);
        Assert.Equal("anim.low-kick", stream.EndAnimationId);

        var source = CharacterPackageSourceCodec.Load(manifest, json);
        Assert.True(source.IsValid, string.Join("; ", source.Diagnostics.Select(d => d.Message)));
        string roundTripped = CharacterPackageSourceCodec.SerializeCharacter(source.Source!.Character);
        Assert.Equal(compiled.CookedPackage.CanonicalBytes, CharacterPackageCompiler.Compile(manifest, roundTripped,
            CharacterCookProfile.TrustedBuiltIn).CookedPackage!.CanonicalBytes);
    }

    [Theory]
    [InlineData("minStreamTicks", 0)]
    [InlineData("maxStreamTicks", 11)]
    [InlineData("emitIntervalTicks", 0)]
    [InlineData("fireTriggerTick", 0)]
    [InlineData("maxBaseKnockback", 21)]
    [InlineData("minRecoilSpeed", -1)]
    [InlineData("maxRecoilSpeed", 1)]
    public void RejectsInvalidLengthsPushAndRecoil(string field, double value)
        => AssertError(Compile(parameters => parameters[field] = value), "value.out-of-range");

    [Fact]
    public void RejectsUnlistedPhaseClipsALoopWithoutASprayClipAndAReturn()
    {
        AssertError(Compile(parameters => parameters["endAnimationId"] = "anim.double-kick"), "reference.unresolved");
        AssertError(Compile(parameters => parameters["sprayAnimationId"] = "anim.high-kick"), "reference.unresolved");
        AssertError(Compile(parameters => parameters.Remove("sprayAnimationId")), "value.out-of-range");
        AssertError(Compile(parameters => parameters["projectile"]!["return"] = new JsonObject
        {
            ["outboundTicks"] = 10, ["returnSpeed"] = 10, ["returnAcceleration"] = 40, ["selfHitDamage"] = 0, ["selfHitStunTicks"] = 10,
        }), "value.out-of-range");
    }

    private static void AssertError(CharacterCompileResult result, string code)
    {
        Assert.Contains(result.Diagnostics, d => d.Severity == CharacterDiagnosticSeverity.Error && d.Code == code);
        Assert.Null(result.CookedPackage);
    }

    private static CharacterCompileResult Compile(Action<JsonObject> parameters, Action<JsonObject>? slot = null)
    {
        var (manifest, character) = Source(slot);
        parameters(StreamParameters(character));
        return CharacterPackageCompiler.Compile(manifest, character.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);
    }

    /// <summary>Pump for `ticks` ticks; 0 is a tap, which releases on the press.</summary>
    private static void Hold(ServerSimulation sim, int ticks)
    {
        if (ticks == 0) TestHelpers.TickN(sim, new InputState { ActiveSlot = AbilitySlots.A }, 1);
        else TestHelpers.TickHold(sim, new InputState { ActiveSlot = AbilitySlots.A, IsAiming = true }, ticks);
    }

    private static ServerSimulation Sim(CharacterDefinition def, bool grounded)
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20f, 20f) with
        {
            // Past the float window, as after a jump: falling for real.
            PY = TestHelpers.GroundPY(def) + (grounded ? 0f : 12f), IsGrounded = grounded,
            AirTimeTicks = grounded ? (ushort)0 : (ushort)120,
        });
        return sim;
    }

    private static CharacterDefinition Definition(Action<JsonObject>? slot = null)
    {
        var def = TestHelpers.ResolveSourceDef("fightguy", character => UseStream(character, slot));
        def = def with { Class = CharacterClass.FightGuy };
        return def;
    }

    private static (string Manifest, JsonObject Character) Source(Action<JsonObject>? slot = null)
    {
        string root = FindRepoDirectory("client/Unity/Assets/CharacterPackages/fightguy");
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!.AsObject();
        UseStream(character, slot);
        return (File.ReadAllText(Path.Combine(root, "package.json")), character);
    }

    /// <summary>FightGuy with a charged stream in place of Ki Shot (A), on his clips: the stage's own clip sorts first.</summary>
    internal static void UseStream(JsonObject character, Action<JsonObject>? edit = null)
    {
        FightGuyFixture.WithoutTitleMatch(character);
        var slots = character["slots"]!.AsArray();
        int index = slots.Select((slot, i) => (slot, i)).Single(x => x.slot!["id"]!.GetValue<string>() == "ground.A").i;
        var stream = JsonNode.Parse($$$"""
            {"id": "ground.A", "name": "Hose Down", "description": "Pump, then spray.", "iconId": "icon.a",
             "behavior": "chargeAttack", "aimMode": "none", "aimMovement": "fixed", "aimAnimationId": "anim.ki-shot-loop",
             "cooldownTicks": 90, "isRecoveryMove": false, "preserveMomentumOnStart": false, "allowSlideCarry": false,
             "timeline": {"stages": [{"durationTicks": 8, "iasaTicks": 0, "landingLagTicks": 0,
               "autoCancelBeforeTicks": 0, "autoCancelAfterTicks": 0, "attackRange": 0, "warpRange": 0,
               "useTargetLock": false, "rotateTowardTarget": false, "trackingStrength": 0,
               "animationIds": ["anim.double-kick", "anim.ki-shot-loop", "anim.low-kick"],
               "operations": [{"kind": "startCapability", "tick": 0, "unit": "ticks",
                 "capabilityId": "slop.ability.charged-stream.v1", "capabilityVersion": "1",
                 "parameters": {"maxChargeTicks": 60, "fireTriggerTick": {{{FireTick}}}, "emitIntervalTicks": {{{Interval}}},
                   "minStreamTicks": {{{MinStream}}}, "maxStreamTicks": {{{MaxStream}}}, "recoveryTicks": {{{Recovery}}},
                   "maxBaseKnockback": 40, "minRecoilSpeed": 1.5, "maxRecoilSpeed": 4.5,
                   "projectile": {"launchOffsetX": 0, "launchOffsetY": 0, "launchOffsetZ": 0.6, "speed": 16, "gravity": 0,
                     "radius": 0.35, "damage": 0, "angle": 0, "baseKnockback": 22, "knockbackGrowth": 0, "stunTicks": 0,
                     "maxFlightTicks": 30, "yawOffsetDegrees": 0},
                   "sprayAnimationId": "anim.ki-shot-loop", "sprayLoopTicks": 12, "endAnimationId": "anim.low-kick"}}]}]}}
            """)!.AsObject();
        edit?.Invoke(stream);
        slots[index] = stream;
        // Ki Shot's effects go with it.
        var presentations = character["presentationIds"]!.AsArray();
        foreach (var id in presentations.Where(id => id!.GetValue<string>().StartsWith("presentation.fightguy.ki-shot", StringComparison.Ordinal)).ToList())
            presentations.Remove(id);
        var requirements = character["capabilityRequirements"]!.AsArray();
        foreach (var requirement in requirements)
            if (requirement!["capabilityId"]!.GetValue<string>() == "slop.ability.charged-projectile.v1")
                requirement["capabilityId"] = "slop.ability.charged-stream.v1";
    }

    private static JsonObject StreamParameters(JsonObject character)
        => character["slots"]!.AsArray().Single(slot => slot!["id"]!.GetValue<string>() == "ground.A")!
            ["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]!.AsObject();

    private static string FindRepoDirectory(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, relative)))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative);
    }
}
