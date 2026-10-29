using System.Linq;
using System.Text.Json.Nodes;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Bashie's retired Ring Bounce (slop.ability.aimed-recovery.v1), kept as the aimed-recovery admission fixture: it
/// replaces her E in a copy of her source, with her shell kit (ADR-0045), Rescue Ring tether (ADR-0048) and Cannonball taken
/// out so the copy needs only the runtime API that aimed recovery itself needs.
/// </summary>
internal static class RingBounceFixture
{
    private const string Slot = """
        {"id":"ground.E","name":"Ring Bounce","description":"Throw the ring ahead, hop automatically onto it, compress, then spring high. Movement input at the spring trades some height for horizontal reach; neutral maximizes height. Works with ordinary jumps spent.","iconId":"icon.e","behavior":"meleeCombo","aimMode":"none","aimMovement":"fixed","cooldownTicks":210,"isRecoveryMove":true,"preserveMomentumOnStart":false,"allowSlideCarry":false,"timeline":{"stages":[{"durationTicks":54,"iasaTicks":0,"landingLagTicks":0,"autoCancelBeforeTicks":0,"autoCancelAfterTicks":0,"attackRange":0,"warpRange":0,"useTargetLock":false,"rotateTowardTarget":false,"trackingStrength":0,"animationIds":["anim.bashie.ge"],"operations":[{"kind":"startCapability","tick":0,"unit":"ticks","capabilityId":"slop.ability.aimed-recovery.v1","capabilityVersion":"1","parameters":{"startupTicks":44,"verticalSpeed":15,"directionalVerticalSpeed":13,"horizontalSpeed":8,"preparationRiseSpeed":0,"hop":{"startTicks":18,"contactTicks":34,"forwardDistance":0.792,"arcHeight":0.495,"landingHeight":0.198}}}]}]}}
        """;

    internal static void Apply(JsonNode character)
    {
        character.AsObject().Remove("shell");
        character["presentation"]!.AsObject().Remove("shell");
        var requirements = character["capabilityRequirements"]!.AsArray();
        for (int i = requirements.Count - 1; i >= 0; i--)
            if ((string?)requirements[i]!["capabilityId"] is CharacterPackageCompiler.ShellStanceCapabilityId
                or CharacterPackageCompiler.ThrustCapabilityId or CharacterPackageCompiler.TetherCapabilityId
                or CharacterPackageCompiler.CannonballCapabilityId)
                requirements.RemoveAt(i);
        foreach (var slot in character["slots"]!.AsArray())
        {
            slot!.AsObject().Remove("shellPolicy");
            foreach (var stage in slot["timeline"]!["stages"]!.AsArray())
            {
                var operations = stage!["operations"]!.AsArray();
                for (int i = operations.Count - 1; i >= 0; i--)
                {
                    var operation = operations[i]!;
                    if ((string?)operation["capabilityId"] is CharacterPackageCompiler.ThrustCapabilityId
                        or CharacterPackageCompiler.TetherCapabilityId or CharacterPackageCompiler.CannonballCapabilityId)
                        operations.RemoveAt(i);
                    else if ((string?)operation["velocityMode"] == "vertical")
                        operation["velocityMode"] = "absolute";
                }
            }
        }
        if (!requirements.Any(x => (string?)x!["capabilityId"] == CharacterPackageCompiler.AimedRecoveryCapabilityId))
            requirements.Add(new JsonObject
            {
                ["capabilityId"] = CharacterPackageCompiler.AimedRecoveryCapabilityId,
                ["capabilityVersion"] = CharacterPackageCompiler.AimedRecoveryCapabilityVersion,
            });
        var slots = character["slots"]!.AsArray();
        int index = slots.Select((slot, i) => (slot, i)).Single(x => (string?)x.slot!["id"] == "ground.E").i;
        slots[index] = JsonNode.Parse(Slot);
    }
}
