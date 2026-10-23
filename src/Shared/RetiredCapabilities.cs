using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

internal static class RetiredCapabilities
{
    private static readonly IReadOnlyDictionary<string, string> Reasons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["slop.internal.bonk.targeted-jump-slam.v1"] = "The Bonk-only targeted jump slam capability has been retired.",
        ["slop.internal.wibou.dash-slash.v1"] = "The Wibou dash-slash capability has been retired.",
        ["slop.internal.wibou.blade-flurry.v1"] = "Blade Flurry v1 has been retired. Use v2 with explicit ascent and descent timing.",
    };

    internal static bool TryGetReason(string id, out string reason) => Reasons.TryGetValue(id, out reason!);
}

internal sealed class RetiredCapabilityException : Exception
{
    internal string CapabilityId { get; }

    internal RetiredCapabilityException(string capabilityId, string reason) : base(reason)
        => CapabilityId = capabilityId;
}
