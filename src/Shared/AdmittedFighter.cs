using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public enum MatchContentPolicy : byte { LocalPlay, StandardRoom, WorkshopRoom, EditorDevelopment }

/// <summary>Verified gameplay admitted by Shared. External file admission is deliberately unavailable.</summary>
public sealed class AdmittedFighter
{
    internal CookedCharacterPackageLoadResult Loaded { get; }
    public FighterChoice Choose(byte startingForm = 0)
    {
        if (startingForm > 1 || startingForm == 1 && Loaded.Package!.Definition.AlternateForm == null)
            throw new ArgumentOutOfRangeException(nameof(startingForm), "Starting form is not admitted by this revision.");
        return new FighterChoice(this, startingForm);
    }
    public MatchContentIdentity Identity => Loaded.Identity;
    public CharacterClass? LegacySelector { get; }
    readonly ShippedRoster? trustedRoster;
    internal bool IsBundled => trustedRoster != null || development;
    readonly bool development;

    internal AdmittedFighter(CookedCharacterPackageLoadResult loaded, CharacterClass? selector,
        ShippedRoster? trustedRoster, bool development = false)
    {
        if (!loaded.IsValid) throw new ArgumentException("Invalid cooked package.", nameof(loaded));
        Loaded = loaded;
        LegacySelector = selector;
        this.trustedRoster = trustedRoster;
        this.development = development;
    }

    internal bool Allowed(MatchContentPolicy policy) => policy switch
    {
        MatchContentPolicy.LocalPlay => !development,
        MatchContentPolicy.EditorDevelopment => true,
        MatchContentPolicy.StandardRoom or MatchContentPolicy.WorkshopRoom => !development && trustedRoster != null
            && trustedRoster.Authorizes(Identity),
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
