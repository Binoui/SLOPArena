using System.Text.Json.Nodes;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class TargetedLeapPresentationTests
{
    private const string LaunchId = "presentation.test.leap.launch";
    private const string LandingId = "presentation.test.leap.land";

    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    public void PhaseEffectsFollowReleaseAndActualLanding(float height)
    {
        var def = Definition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = TestHelpers.GroundPY(def) + height;
        state.IsGrounded = height == 0;
        sim.RegisterEntity(1, def, state);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = 4, IsAiming = true, AimDistance = 400 } });
        for (int tick = 0; tick < 20; tick++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { IsAiming = true, AimDistance = 400 } });
        Assert.Empty(sim.GetPresentationEvents());
        float releaseFeet = sim.GetState(1).PY - def.CapsuleHeight * 0.5f;
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        var launch = Assert.Single(sim.GetPresentationEvents(clear: true));
        Assert.Equal(PresentationEventSource.CapabilityLaunch, launch.Source);
        Assert.Equal(LaunchId, launch.PresentationId);
        TestHelpers.AssertNear(releaseFeet, launch.WorldY);

        var events = new List<TimelinePresentationEvent>();
        for (int tick = 0; tick < 200; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            foreach (var item in sim.GetPresentationEvents(clear: true))
            {
                Assert.True(sim.GetState(1).IsGrounded);
                Assert.Equal((ushort)0, sim.GetState(1).AttackElapsedTicks);  // E1: the landing clip's first frame
                TestHelpers.AssertNear(sim.GetState(1).PX, item.WorldX);
                TestHelpers.AssertNear(sim.GetState(1).PY - def.CapsuleHeight * 0.5f, item.WorldY);
                TestHelpers.AssertNear(sim.GetState(1).PZ, item.WorldZ);
                events.Add(item);
            }
        }
        var landing = Assert.Single(events);
        Assert.Equal(PresentationEventSource.CapabilityLanding, landing.Source);
        Assert.Equal(LandingId, landing.PresentationId);
        Assert.Equal(launch.AttackSequence, landing.AttackSequence);
        Assert.NotEqual(launch.Key, landing.Key);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LandingPlaysTheStagesSecondClipOnlyWhenItListsOne(bool landingClip)
    {
        var source = Source();
        if (!landingClip)
            foreach (var slot in source["slots"]!.AsArray())
                foreach (var stage in slot!["timeline"]!["stages"]!.AsArray())
                {
                    var ids = stage!["animationIds"]!.AsArray();
                    for (int index = ids.Count - 1; index > 0; index--)
                        if (ids[index]!.GetValue<string>() == "anim.bonk.ge-land") ids.RemoveAt(index);
                }
        var compiled = Compile(source);
        Assert.NotNull(compiled.CookedPackage);
        var def = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage!);
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = 4, IsAiming = true, AimDistance = 400 } });
        for (int tick = 0; tick < 20; tick++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { IsAiming = true, AimDistance = 400 } });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Equal((byte)0, sim.GetState(1).ComboStage);  // the flight plays the first clip
        bool landed = false;
        for (int tick = 0; tick < 200 && !landed; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            foreach (var item in sim.GetPresentationEvents(clear: true))
                if (item.Source == PresentationEventSource.CapabilityLanding)
                {
                    landed = true;
                    Assert.Equal(landingClip ? (byte)1 : (byte)0, sim.GetState(1).ComboStage);
                    Assert.Equal(landingClip ? (byte)1 : (byte)0, sim.GetState(1).AnimIndex);
                    var move = Assert.IsType<ActiveMoveView>(sim.GetActiveMove(1));
                    Assert.Equal(sim.GetState(1).AnimIndex, move.ClipIndex);
                    Assert.Equal(landingClip ? "anim.bonk.ge-land" : "anim.bonk.ge", move.Pose.AnimationId);
                }
        }
        Assert.True(landed);
        // The landing clip plays through the whole recovery.
        for (int tick = 0; tick < 60; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
            Assert.Equal(landingClip ? (byte)1 : (byte)0, sim.GetState(1).ComboStage);
            Assert.Equal(landingClip ? (byte)1 : (byte)0, sim.GetState(1).AnimIndex);
        }
    }

    [Fact]
    public void LandingClipSurvivesTheHitstopOfALandingHit()
    {
        var def = Definition();
        void Leap(ServerSimulation sim)
        {
            var state = TestHelpers.PlayerState(x: 20f, z: 10f);
            state.PY = TestHelpers.GroundPY(def);
            sim.RegisterEntity(1, def, state);
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = 4, IsAiming = true, AimDistance = 400 } });
            for (int tick = 0; tick < 20; tick++)
                sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { IsAiming = true, AimDistance = 400 } });
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        }
        var probe = TestHelpers.MakeSim();
        Leap(probe);
        for (int tick = 0; tick < 200 && !probe.GetPresentationEvents(clear: true).Any(e => e.Source == PresentationEventSource.CapabilityLanding); tick++)
            probe.Tick(new Dictionary<ulong, InputState> { [1] = default });
        var landing = probe.GetState(1);

        var sim = TestHelpers.MakeSim();
        var target = TestHelpers.PlayerState(x: landing.PX + 0.8f, z: landing.PZ);
        target.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(2, def, target);
        Leap(sim);
        bool hitstop = false, landed = false;
        for (int tick = 0; tick < 200; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default, [2] = default });
            var s = sim.GetState(1);
            if (s.State != ActionState.Attacking)
                break;
            hitstop |= s.HitstopTicks > 0;
            landed |= s.ComboStage == 1;
            if (landed)
                Assert.Equal((byte)1, s.ComboStage);  // the freeze and the recovery stay on the landing clip
        }
        Assert.True(hitstop);
        Assert.True(landed);
    }

    [Fact]
    public void FlightTimeoutDoesNotProduceLandingEffect()
    {
        var def = Definition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(x: 20f, z: 10f);
        state.PY = 100f;
        state.IsGrounded = false;
        sim.RegisterEntity(1, def, state);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = 4, IsAiming = true, AimDistance = 400 } });
        for (int tick = 0; tick < 9; tick++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { IsAiming = true } });
        sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Equal(PresentationEventSource.CapabilityLaunch, Assert.Single(sim.GetPresentationEvents(clear: true)).Source);
        for (int tick = 0; tick < 150; tick++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
        Assert.Empty(sim.GetPresentationEvents());
        Assert.Null(sim.GetActiveAbility(1));
    }

    [Theory]
    [InlineData("launchPresentationId")]
    [InlineData("landingPresentationId")]
    public void UnresolvedPhaseEffectIsRejected(string field)
    {
        var source = Source();
        source["slots"]![4]!["timeline"]!["stages"]![0]!["operations"]![0]!["parameters"]![field] = "presentation.missing";
        var result = Compile(source);
        Assert.Null(result.CookedPackage);
        Assert.Contains(result.Diagnostics, item => item.Code == "reference.unresolved" && item.Path.EndsWith(field));
    }

    [Fact]
    public void SourceRoundTripRetainsOptionalPhaseEffects()
    {
        var compiled = Compile(Source());
        Assert.NotNull(compiled.CookedPackage);
        var source = CharacterPackageSourceCodec.Load(File.ReadAllText(RepoFile("package.json")), Source().ToJsonString());
        Assert.True(source.IsValid);
        var json = CharacterPackageSourceCodec.SerializeCharacter(source.Source!.Character);
        var roundTrip = Compile(JsonNode.Parse(json)!.AsObject());
        Assert.NotNull(roundTrip.CookedPackage);
        Assert.Equal(compiled.CookedPackage!.CanonicalBytes, roundTrip.CookedPackage!.CanonicalBytes);
    }

    private static CharacterDefinition Definition()
    {
        var result = Compile(Source());
        Assert.NotNull(result.CookedPackage);
        return CookedCharacterRuntimeAdapter.ToCharacterDefinition(result.CookedPackage!);
    }

    private static CharacterCompileResult Compile(JsonObject source)
        => CharacterPackageCompiler.Compile(File.ReadAllText(RepoFile("package.json")), source.ToJsonString(), CharacterCookProfile.TrustedBuiltIn);

    private static JsonObject Source()
    {
        var source = JsonNode.Parse(File.ReadAllText(RepoFile("character.json")))!.AsObject();
        source["presentationIds"] = new JsonArray(LaunchId, LandingId);
        foreach (var slot in source["slots"]!.AsArray())
        {
            slot!.AsObject().Remove("hitPresentationId");
            foreach (var stage in slot["timeline"]!["stages"]!.AsArray())
            {
                var operations = stage!["operations"]!.AsArray();
                for (int index = operations.Count - 1; index >= 0; index--)
                    if (operations[index]!["kind"]!.GetValue<string>() == "emitPresentation") operations.RemoveAt(index);
                foreach (var operation in operations)
                    if (operation!["projectile"]?["return"] is JsonObject returning)
                    {
                        returning.Remove("catchPresentationId");
                        returning.Remove("selfHitPresentationId");
                    }
                foreach (var operation in operations)
                    if (operation!["kind"]!.GetValue<string>() == "startCapability" && operation["capabilityId"]!.GetValue<string>() == CharacterPackageCompiler.SteeredChargeCapabilityId)
                        foreach (var effect in new[] { "gallopPresentationId", "hitEndingPresentationId", "missEndingPresentationId", "airEndingPresentationId" })
                            operation["parameters"]!.AsObject().Remove(effect);
                foreach (var operation in operations)
                    if (operation!["kind"]!.GetValue<string>() == "startCapability" && operation["capabilityId"]!.GetValue<string>() == CharacterPackageCompiler.TargetedLeapCapabilityId)
                    {
                        operation["parameters"]!["launchPresentationId"] = LaunchId;
                        operation["parameters"]!["landingPresentationId"] = LandingId;
                    }
            }
        }
        return source;
    }

    private static string RepoFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine(directory.FullName, "client/Unity/Assets/CharacterPackages/bonk", name);
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(name);
    }
}
