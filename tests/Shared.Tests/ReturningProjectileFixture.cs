using System.Linq;
using System.Text.Json.Nodes;

namespace SlopArena.Shared.Tests;

/// <summary>Turns a package's character.json into a Workshop package whose ground.1 throws a returning lid.</summary>
internal static class ReturningProjectileFixture
{
    internal const string CatchPresentationId = "presentation.fightguy.normal.hit";

    internal static JsonObject Operation() => JsonNode.Parse("""
        {"kind":"spawnProjectile","tick":7,"unit":"meters",
         "projectile":{"launchOffsetX":0,"launchOffsetY":0.4,"launchOffsetZ":0.6,"speed":14,"gravity":0,"radius":0.35,
           "damage":4,"angle":30,"baseKnockback":1,"knockbackGrowth":0,"stunTicks":12,"maxFlightTicks":240,"yawOffsetDegrees":0,
           "return":{"outboundTicks":20,"returnSpeed":16,"returnAcceleration":80,"selfHitDamage":0,"selfHitStunTicks":30,
             "catchPresentationId":"presentation.fightguy.normal.hit"}}}
        """)!.AsObject();

    /// <summary>Drops every lifecycle (so the package fits the Workshop profile) and adds the lid to ground.1.</summary>
    internal static void Apply(JsonObject character)
    {
        FightGuyFixture.WithoutTitleMatch(character);
        character["capabilityRequirements"] = new JsonArray();
        foreach (JsonObject slot in character["slots"]!.AsArray())
            foreach (JsonObject stage in slot["timeline"]!["stages"]!.AsArray())
            {
                var operations = stage["operations"]!.AsArray();
                for (int i = operations.Count - 1; i >= 0; i--)
                    if ((string?)operations[i]!["kind"] == "startCapability")
                        operations.RemoveAt(i);
            }
        Operations(character).Add(Operation());
    }

    internal static JsonArray Operations(JsonObject character)
        => SteeredChargeFixture.Slot(character, "ground.1")["timeline"]!["stages"]![0]!["operations"]!.AsArray();

    internal static JsonObject Projectile(JsonObject character)
        => Operations(character).Last(x => (string?)x!["kind"] == "spawnProjectile")!["projectile"]!.AsObject();

    internal static JsonObject Return(JsonObject character) => Projectile(character)["return"]!.AsObject();
}
