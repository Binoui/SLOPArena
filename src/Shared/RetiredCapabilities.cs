using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

internal static class RetiredCapabilities
{
    private static readonly IReadOnlyDictionary<string, string> Reasons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["slop.internal.bonk.targeted-jump-slam.v1"] = "The Bonk-only targeted jump slam capability has been retired.",
        ["slop.ability.charged-directional-dash.v1"] = "Charged directional dash has been retired. Use slop.ability.dash-strike.v1 (Quick Draw, ADR-0052).",
        ["slop.internal.wibou.dash-slash.v1"] = "The Wibou dash-slash capability has been retired.",
        ["slop.internal.wibou.blade-flurry.v1"] = "Blade Flurry v1 has been retired. Use slop.ability.flash-cut.v1 (the flash, ADR-0052).",
        ["slop.internal.wibou.blade-flurry.v2"] = "Blade Flurry (Skyfall) has been retired. Use slop.ability.flash-cut.v1 (the flash, ADR-0052).",
        ["slop.ability.shell-stance.v1"] = "Shell stance v1 (the skate and the bowl) has been retired. Use slop.ability.shell-stance.v2 with a splash and a ride (ADR-0050).",
        ["slop.ability.clinch.v1"] = "Clinch v1 (Title Match) has been retired. Use slop.ability.command-grab.v1 with level, anchorDistance, releaseLockTicks and whiffRecoveryTicks (ADR-0057).",
    };

    internal static bool TryGetReason(string id, out string reason) => Reasons.TryGetValue(id, out reason!);
}

internal sealed class RetiredCapabilityException : Exception
{
    internal string CapabilityId { get; }

    internal RetiredCapabilityException(string capabilityId, string reason) : base(reason)
        => CapabilityId = capabilityId;
}
