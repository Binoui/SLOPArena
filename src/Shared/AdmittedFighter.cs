using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public enum MatchContentPolicy : byte { LocalPlay, StandardRoom, WorkshopRoom }

/// <summary>Verified gameplay admitted by Shared. External file admission is deliberately unavailable.</summary>
public sealed class AdmittedFighter
{
    internal CookedCharacterPackageLoadResult Loaded { get; }
    public MatchContentIdentity Identity => Loaded.Identity;
    public CharacterClass? LegacySelector { get; }
    readonly MatchContentPackageRequirement? trustedPin;

    internal AdmittedFighter(CookedCharacterPackageLoadResult loaded, CharacterClass? selector,
        MatchContentPackageRequirement? trustedPin)
    {
        if (!loaded.IsValid) throw new ArgumentException("Invalid cooked package.", nameof(loaded));
        Loaded = loaded;
        LegacySelector = selector;
        this.trustedPin = trustedPin;
    }

    internal bool Allowed(MatchContentPolicy policy) => policy switch
    {
        MatchContentPolicy.LocalPlay => true,
        MatchContentPolicy.StandardRoom or MatchContentPolicy.WorkshopRoom => trustedPin != null
            && trustedPin.PackageId == Identity.PackageId && trustedPin.Version == Identity.Version
            && trustedPin.CookedContentHash == Identity.CookedContentHash && trustedPin.PackageHash == Identity.PackageHash,
        _ => false,
    };

    internal static AdmittedFighter TestFixture(IReadOnlyDictionary<string, byte[]> files, MatchContentPackageRequirement requirement)
    {
        var loaded = CookedCharacterPackageLoader.LoadFiles(files, requirement);
        if (!loaded.IsValid || !loaded.Identity.PackageId.StartsWith("local.", StringComparison.Ordinal))
            throw new ArgumentException("Invalid local fixture: " + string.Join("; ", loaded.Diagnostics.Select(x => x.Code)));
        return new AdmittedFighter(loaded, null, null);
    }
}
