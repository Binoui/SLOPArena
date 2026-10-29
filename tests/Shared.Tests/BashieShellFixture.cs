using System.Linq;
using static System.FormattableString;
using System.Text.Json.Nodes;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Bashie's source package with her shell kit (ADR-0050) at fixed test values, so the shell's rules are tested apart
/// from her tuning: Cannonball on ground R, the Cannonball Dive on air R, Hydro Jet on E, Wave Pool on F (air F is
/// its alias), the shell's splash and ride and its pose, and a vertical-only hop on ground 4.
/// </summary>
internal static class BashieShellFixture
{
    internal const ushort RCooldown = 90, ECooldown = 120, FCooldown = 300;
    internal const float PopOutDeceleration = 30f, BlockedRecoilSpeed = 4f;
    internal const ushort PopUpTicks = 24, SplashTicks = 3;
    internal const float SplashRadius = 1.5f, SplashDamage = 8f;
    internal const ushort RideTicks = 60;
    internal const float RideSpeed = 12f, WaveDamage = 3f;
    internal const ushort TakeoffTick = 2, MinFlightTicks = 8, MaxFlightTicks = 40, CannonballTicks = 70;
    internal const float MaxRange = 10f, NoTargetDistance = 5f, MaxLaunchSpeed = 25f;
    internal const ushort WhistleTicks = 20;
    internal const float JetSpeed = 15f, JetStickPitch = 20f;
    internal const ushort JetTicks = 18, JetIasa = 6;
    internal const float DiveSpeed = 12f, DiveAcceleration = 30f, DiveMaxSpeed = 24f, DivePitch = -50f, DiveCone = 60f;
    internal const ushort DiveTicks = 40, DiveIasa = 20;

    internal static CharacterDefinition Definition { get; } = TestHelpers.ResolveSourceDef("bashie", Apply);

    /// <summary>Her shell with a ride that turns toward her target at this rate.</summary>
    internal static CharacterDefinition Turning(float degreesPerSecond)
        => TestHelpers.ResolveSourceDef("bashie", c => { Apply(c); c["shell"]!["ride"]!["turnDegreesPerSecond"] = degreesPerSecond; });

    /// <summary>Hydro Jet's cooked thrust, for the snapshot contract's capability coverage.</summary>
    internal static CookedThrustCapabilityParameters JetParameters
        => (CookedThrustCapabilityParameters)Capability("ground.E");

    /// <summary>Ground R's cooked cannonball, for the snapshot contract's capability coverage.</summary>
    internal static CookedCannonballCapabilityParameters CannonballParameters
        => (CookedCannonballCapabilityParameters)Capability("ground.R");

    private static CookedCapabilityParameters Capability(string slot)
        => Definition.CookedSlots!.Single(s => s.Id == slot)
            .Timeline.Stages[0].Operations.OfType<CookedStartCapabilityOperation>().Single().Parameters;

    internal static void Apply(JsonObject character)
    {
        var requirements = new JsonArray();
        foreach (var id in new[] { CharacterPackageCompiler.TetherCapabilityId, CharacterPackageCompiler.ShellStanceCapabilityId,
                     CharacterPackageCompiler.ThrustCapabilityId, CharacterPackageCompiler.CannonballCapabilityId })
            requirements.Add(new JsonObject { ["capabilityId"] = id, ["capabilityVersion"] = "1" });
        character["capabilityRequirements"] = requirements;
        character["presentation"]!["shell"] = "anim.bashie.gr-slide";
        character["shell"] = JsonNode.Parse(Invariant($$"""
            {"popOutDeceleration":{{PopOutDeceleration}},"blockedRecoilSpeed":{{BlockedRecoilSpeed}},
             "splash":{"popUpTicks":{{PopUpTicks}},"presentationId":"presentation.bashie.shell.plop",
               "hitbox":{"shape":"sphere","radius":{{SplashRadius}},"offsetX":0,"offsetY":-0.75,"offsetZ":0,"endOffsetX":0,
                 "endOffsetY":0,"endOffsetZ":0,"startBoneId":null,"endBoneId":null,"damage":{{SplashDamage}},"angle":85,
                 "baseKnockback":12,"knockbackGrowth":30,"stunTicks":20,"durationTicks":{{SplashTicks}},"interruptible":false,
                 "hitGroup":0,"knockbackDirection":"awayFromOwner"} },
             "ride":{"ticks":{{RideTicks}},"speed":{{RideSpeed}},"turnDegreesPerSecond":0,
               "hitPresentationId":"presentation.bashie.shell.hit",
               "wave":{"shape":"capsule","radius":0.7,"offsetX":-1,"offsetY":-0.3,"offsetZ":0.9,"endOffsetX":1,
                 "endOffsetY":-0.3,"endOffsetZ":0.9,"startBoneId":null,"endBoneId":null,"damage":{{WaveDamage}},"angle":10,
                 "baseKnockback":6,"knockbackGrowth":0,"stunTicks":0,"durationTicks":1,"interruptible":false,
                 "hitGroup":0,"knockbackDirection":"awayFromOwner"} } }
            """));
        var aliases = character["aliases"]!.AsArray();
        for (int i = aliases.Count - 1; i >= 0; i--)
            if ((string?)aliases[i]!["from"] is "air.R" or "air.F") aliases.RemoveAt(i);
        aliases.Add(new JsonObject { ["from"] = "air.F", ["to"] = "ground.F" });
        var slots = character["slots"]!.AsArray();
        for (int i = slots.Count - 1; i >= 0; i--)
            if ((string?)slots[i]!["id"] is "ground.R" or "air.R" or "ground.E" or "ground.F" or "air.F") slots.RemoveAt(i);
        slots.Add(Slot("ground.R", "Cannonball", RCooldown, false, CannonballTicks, 0, "anim.bashie.gr", "tuck",
            Capability(CharacterPackageCompiler.CannonballCapabilityId, Invariant($$"""
            {"takeoffTick":{{TakeoffTick}},"maxRange":{{MaxRange}},"noTargetDistance":{{NoTargetDistance}},
             "minFlightTicks":{{MinFlightTicks}},"maxFlightTicks":{{MaxFlightTicks}},"maxLaunchSpeed":{{MaxLaunchSpeed}}}
            """))));
        slots.Add(Slot("air.R", "Cannonball Dive", RCooldown, false, DiveTicks, DiveIasa, "anim.bashie.gr-slide", "tuck",
            Capability(CharacterPackageCompiler.ThrustCapabilityId, Invariant($$"""
            {"aim":"targetAhead","targetConeDegrees":{{DiveCone}},"neutralPitchDegrees":{{DivePitch}},"stickPitchDegrees":{{DivePitch}},
             "speed":{{DiveSpeed}},"acceleration":{{DiveAcceleration}},"maxSpeed":{{DiveMaxSpeed}},"keepIncomingSpeed":true,
             "thrustTicks":{{DiveTicks}}}
            """))));
        slots.Add(Slot("ground.E", "Hydro Jet", ECooldown, true, JetTicks, JetIasa, "anim.bashie.gr-slide", "tuck",
            Capability(CharacterPackageCompiler.ThrustCapabilityId, Invariant($$"""
            {"aim":"stick","neutralPitchDegrees":90,"stickPitchDegrees":{{JetStickPitch}},"speed":{{JetSpeed}},
             "acceleration":0,"maxSpeed":{{JetSpeed}},"keepIncomingSpeed":false,"thrustTicks":{{JetTicks}},
             "trailHitbox":{"shape":"capsule","radius":0.5,"offsetX":0,"offsetY":0,"offsetZ":-0.6,"endOffsetX":0,
               "endOffsetY":0,"endOffsetZ":-1.6,"startBoneId":null,"endBoneId":null,"damage":0,"angle":10,
               "baseKnockback":10,"knockbackGrowth":0,"stunTicks":0,"durationTicks":1,"interruptible":false,
               "hitGroup":0,"knockbackDirection":"awayFromOwner"} }
            """))));
        slots.Add(Slot("ground.F", "Wave Pool", FCooldown, false, WhistleTicks, 0, "anim.bashie.whistle", "ride",
            JsonNode.Parse(Invariant($$"""
            {"kind":"gravityWindow","tick":0,"unit":"normalized","gravityScale":0.15,"durationTicks":{{WhistleTicks}}}
            """))!));
        foreach (var stage in SlotNamed(character, "ground.4")["timeline"]!["stages"]!.AsArray())
            foreach (var operation in stage!["operations"]!.AsArray())
                if ((string?)operation!["kind"] == "setVelocity") operation["velocityMode"] = "vertical";
    }

    /// <summary>Takes her Rescue Ring (ADR-0048) out, for checks of what the shell alone needs.</summary>
    internal static void WithoutRing(JsonObject character)
    {
        var requirements = character["capabilityRequirements"]!.AsArray();
        for (int i = requirements.Count - 1; i >= 0; i--)
            if ((string?)requirements[i]!["capabilityId"] == CharacterPackageCompiler.TetherCapabilityId)
                requirements.RemoveAt(i);
        foreach (var stage in SlotNamed(character, "ground.A")["timeline"]!["stages"]!.AsArray())
        {
            var operations = stage!["operations"]!.AsArray();
            for (int i = operations.Count - 1; i >= 0; i--)
                if ((string?)operations[i]!["capabilityId"] == CharacterPackageCompiler.TetherCapabilityId)
                    operations.RemoveAt(i);
        }
    }

    internal static JsonObject SlotNamed(JsonObject character, string id)
        => character["slots"]!.AsArray().Select(x => x!.AsObject()).Single(x => (string?)x["id"] == id);

    private static JsonNode Capability(string id, string parameters) => new JsonObject
    {
        ["kind"] = "startCapability", ["tick"] = 0, ["unit"] = "ticks",
        ["capabilityId"] = id, ["capabilityVersion"] = "1", ["parameters"] = JsonNode.Parse(parameters),
    };

    private static JsonObject Slot(string id, string name, ushort cooldown, bool recovery, ushort duration, ushort iasa,
        string clip, string shellPolicy, JsonNode operation) => new()
    {
        ["id"] = id, ["name"] = name, ["description"] = "", ["iconId"] = "icon." + id[^1..].ToLowerInvariant(),
        ["behavior"] = "meleeCombo", ["aimMode"] = "none", ["aimMovement"] = "fixed",
        ["cooldownTicks"] = cooldown, ["isRecoveryMove"] = recovery, ["preserveMomentumOnStart"] = false,
        ["allowSlideCarry"] = false, ["shellPolicy"] = shellPolicy,
        ["timeline"] = new JsonObject
        {
            ["stages"] = new JsonArray(new JsonObject
            {
                ["durationTicks"] = duration, ["iasaTicks"] = iasa, ["landingLagTicks"] = 0,
                ["autoCancelBeforeTicks"] = 0, ["autoCancelAfterTicks"] = 0,
                ["animationIds"] = new JsonArray(clip), ["operations"] = new JsonArray(operation),
            }),
        },
    };
}
