using System.Linq;
using System.Text.Json.Nodes;

namespace SlopArena.Shared.Tests;

/// <summary>Turns a package's character.json into a Workshop package whose ground.R is a steered charge.</summary>
internal static class SteeredChargeFixture
{
    internal static readonly string[] Clips = { "anim.fix.r", "anim.fix.r-air", "anim.fix.r-gallop", "anim.fix.r-hit", "anim.fix.r-miss" };

    internal static JsonObject Operation(ushort tick = 10) => JsonNode.Parse("""
        {"kind":"startCapability","tick":TICK,"unit":"ticks","capabilityId":"slop.ability.steered-charge.v1","capabilityVersion":"1",
         "parameters":{"gallopSpeed":12,"turnDegreesPerSecond":180,"maxGallopTicks":60,"armored":true,
           "lanceHitbox":{"shape":"sphere","radius":0.6,"offsetX":0,"offsetY":0,"offsetZ":1,"endOffsetX":0,"endOffsetY":0,"endOffsetZ":0,
             "startBoneId":null,"endBoneId":null,"damage":10,"angle":40,"baseKnockback":8,"knockbackGrowth":30,"stunTicks":20,
             "durationTicks":1,"interruptible":true,"hitGroup":0,"knockbackDirection":"awayFromOwner"},
           "hitEndingTicks":20,"missEndingTicks":30,"skidTicks":12,"turnAroundOnMiss":true,"airStrideTicks":15,"lookDownTicks":10,
           "gallopAnimationId":"anim.fix.r-gallop","gallopLoopTicks":24,"hitEndingAnimationId":"anim.fix.r-hit",
           "missEndingAnimationId":"anim.fix.r-miss","airEndingAnimationId":"anim.fix.r-air"}}
        """.Replace("TICK", tick.ToString()))!.AsObject();

    /// <summary>Drops every other lifecycle (so the package fits the Workshop profile) and puts the charge on ground.R.</summary>
    internal static void Apply(JsonObject character, JsonObject? operation = null)
    {
        FightGuyFixture.WithoutTitleMatch(character);
        character["capabilityRequirements"] = new JsonArray(new JsonObject
        {
            ["capabilityId"] = CharacterPackageCompiler.SteeredChargeCapabilityId,
            ["capabilityVersion"] = CharacterPackageCompiler.SteeredChargeCapabilityVersion,
        });
        foreach (JsonObject slot in character["slots"]!.AsArray())
            foreach (JsonObject stage in slot["timeline"]!["stages"]!.AsArray())
            {
                var operations = stage["operations"]!.AsArray();
                for (int i = operations.Count - 1; i >= 0; i--)
                    if ((string?)operations[i]!["kind"] == "startCapability")
                        operations.RemoveAt(i);
            }
        var slotR = Slot(character, "ground.R");
        slotR["behavior"] = "meleeCombo";
        slotR["aimMode"] = "none";
        slotR["aimMovement"] = "fixed";
        slotR.Remove("aimAnimationId");
        slotR["timeline"] = new JsonObject
        {
            ["stages"] = new JsonArray(new JsonObject
            {
                ["durationTicks"] = 30,
                ["iasaTicks"] = 0,
                ["landingLagTicks"] = 0,
                ["autoCancelBeforeTicks"] = 0,
                ["autoCancelAfterTicks"] = 0,
                ["attackRange"] = 0,
                ["warpRange"] = 0,
                ["useTargetLock"] = false,
                ["rotateTowardTarget"] = false,
                ["trackingStrength"] = 0,
                ["animationIds"] = new JsonArray(Clips.Select(x => (JsonNode)x).ToArray()),
                ["operations"] = new JsonArray(operation ?? Operation()),
            }),
        };
    }

    internal static JsonObject Slot(JsonObject character, string id)
        => character["slots"]!.AsArray().Select(x => x!.AsObject()).Single(x => (string?)x["id"] == id);

    internal static JsonObject ChargeOperation(JsonObject character)
        => Slot(character, "ground.R")["timeline"]!["stages"]![0]!["operations"]!.AsArray()
            .Select(x => x!.AsObject()).Single(x => (string?)x["kind"] == "startCapability");
}
