using System.Linq;
using static System.FormattableString;
using System.Text.Json.Nodes;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Bashie's source package with her shell kit (ADR-0045) at fixed test values, so the shell's rules are
/// tested apart from her tuning: Shell Shift on ground R, Shell Dive on air R, Hydro Jet on E, the shell
/// block and pose, and a vertical-only hop on ground 4.
/// </summary>
internal static class BashieShellFixture
{
    internal const ushort SkateTicks = 60, TuckTicks = 6, RCooldown = 90, ECooldown = 120;
    internal const float MinSkateSpeed = 8f, MaxSkateSpeed = 30f, LandingSpeedRatio = .25f;
    internal const float PopOutDeceleration = 30f, BlockedRecoilSpeed = 4f;
    internal const float JetSpeed = 15f, JetStickPitch = 20f;
    internal const ushort JetTicks = 18, JetIasa = 6;
    internal const float DiveSpeed = 12f, DiveAcceleration = 30f, DiveMaxSpeed = 24f, DivePitch = -50f, DiveCone = 60f;
    internal const ushort DiveTicks = 40, DiveIasa = 20;

    internal static CharacterDefinition Definition { get; } = TestHelpers.ResolveSourceDef("bashie", Apply);

    /// <summary>Hydro Jet's cooked thrust, for the snapshot contract's capability coverage.</summary>
    internal static CookedThrustCapabilityParameters JetParameters
        => (CookedThrustCapabilityParameters)Definition.CookedSlots!.Single(slot => slot.Id == "ground.E")
            .Timeline.Stages[0].Operations.OfType<CookedStartCapabilityOperation>().Single().Parameters;

    internal static void Apply(JsonObject character)
    {
        var requirements = new JsonArray();
        foreach (var id in new[] { CharacterPackageCompiler.ChargedStreamCapabilityId,
                     CharacterPackageCompiler.ShellStanceCapabilityId, CharacterPackageCompiler.ThrustCapabilityId })
            requirements.Add(new JsonObject { ["capabilityId"] = id, ["capabilityVersion"] = "1" });
        character["capabilityRequirements"] = requirements;
        character["presentation"]!["shell"] = "anim.bashie.gr-slide";
        character["shell"] = JsonNode.Parse(Invariant($$"""
            {"skateTicks":{{SkateTicks}},"minSkateSpeed":{{MinSkateSpeed}},"maxSkateSpeed":{{MaxSkateSpeed}},
             "landingSpeedRatio":{{LandingSpeedRatio}},"popOutDeceleration":{{PopOutDeceleration}},
             "blockedRecoilSpeed":{{BlockedRecoilSpeed}},"hitPresentationId":"presentation.bashie.shell.hit",
             "hitbox":{"shape":"capsule","radius":0.6,"offsetX":0,"offsetY":-0.3,"offsetZ":-0.2,"endOffsetX":0,
               "endOffsetY":-0.3,"endOffsetZ":0.8,"startBoneId":null,"endBoneId":null,"damage":6,"angle":70,
               "baseKnockback":6,"knockbackGrowth":20,"stunTicks":15,"durationTicks":1,"interruptible":false,
               "hitGroup":0,"knockbackDirection":"awayFromOwner"} }
            """));
        var aliases = character["aliases"]!.AsArray();
        for (int i = aliases.Count - 1; i >= 0; i--)
            if ((string?)aliases[i]!["from"] == "air.R") aliases.RemoveAt(i);
        var slots = character["slots"]!.AsArray();
        for (int i = slots.Count - 1; i >= 0; i--)
            if ((string?)slots[i]!["id"] is "ground.R" or "air.R" or "ground.E") slots.RemoveAt(i);
        slots.Add(Slot("ground.R", "Shell Shift", RCooldown, false, TuckTicks, 0, "anim.bashie.gr", null));
        slots.Add(Slot("air.R", "Shell Dive", RCooldown, false, DiveTicks, DiveIasa, "anim.bashie.gr-slide", Invariant($$"""
            {"aim":"targetAhead","targetConeDegrees":{{DiveCone}},"neutralPitchDegrees":{{DivePitch}},"stickPitchDegrees":{{DivePitch}},
             "speed":{{DiveSpeed}},"acceleration":{{DiveAcceleration}},"maxSpeed":{{DiveMaxSpeed}},"keepIncomingSpeed":true,
             "thrustTicks":{{DiveTicks}}}
            """)));
        slots.Add(Slot("ground.E", "Hydro Jet", ECooldown, true, JetTicks, JetIasa, "anim.bashie.gr-slide", Invariant($$"""
            {"aim":"stick","neutralPitchDegrees":90,"stickPitchDegrees":{{JetStickPitch}},"speed":{{JetSpeed}},
             "acceleration":0,"maxSpeed":{{JetSpeed}},"keepIncomingSpeed":false,"thrustTicks":{{JetTicks}},
             "trailHitbox":{"shape":"capsule","radius":0.5,"offsetX":0,"offsetY":0,"offsetZ":-0.6,"endOffsetX":0,
               "endOffsetY":0,"endOffsetZ":-1.6,"startBoneId":null,"endBoneId":null,"damage":0,"angle":10,
               "baseKnockback":10,"knockbackGrowth":0,"stunTicks":0,"durationTicks":1,"interruptible":false,
               "hitGroup":0,"knockbackDirection":"awayFromOwner"} }
            """)));
        foreach (var stage in SlotNamed(character, "ground.4")["timeline"]!["stages"]!.AsArray())
            foreach (var operation in stage!["operations"]!.AsArray())
                if ((string?)operation!["kind"] == "setVelocity") operation["velocityMode"] = "vertical";
    }

    internal static JsonObject SlotNamed(JsonObject character, string id)
        => character["slots"]!.AsArray().Select(x => x!.AsObject()).Single(x => (string?)x["id"] == id);

    private static JsonObject Slot(string id, string name, ushort cooldown, bool recovery, ushort duration, ushort iasa,
        string clip, string? thrust)
    {
        var operations = new JsonArray();
        if (thrust != null)
            operations.Add(new JsonObject
            {
                ["kind"] = "startCapability", ["tick"] = 0, ["unit"] = "ticks",
                ["capabilityId"] = CharacterPackageCompiler.ThrustCapabilityId, ["capabilityVersion"] = "1",
                ["parameters"] = JsonNode.Parse(thrust),
            });
        return new JsonObject
        {
            ["id"] = id, ["name"] = name, ["description"] = "", ["iconId"] = "icon." + id[^1..].ToLowerInvariant(),
            ["behavior"] = "meleeCombo", ["aimMode"] = "none", ["aimMovement"] = "fixed",
            ["cooldownTicks"] = cooldown, ["isRecoveryMove"] = recovery, ["preserveMomentumOnStart"] = false,
            ["allowSlideCarry"] = false, ["shellPolicy"] = "tuck",
            ["timeline"] = new JsonObject
            {
                ["stages"] = new JsonArray(new JsonObject
                {
                    ["durationTicks"] = duration, ["iasaTicks"] = iasa, ["landingLagTicks"] = 0,
                    ["autoCancelBeforeTicks"] = 0, ["autoCancelAfterTicks"] = 0,
                    ["animationIds"] = new JsonArray(clip), ["operations"] = operations,
                }),
            },
        };
    }
}
