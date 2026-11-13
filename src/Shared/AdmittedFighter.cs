using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public enum MatchContentPolicy : byte { LocalPlay, StandardRoom, WorkshopRoom, EditorDevelopment }

/// <summary>Verified gameplay admitted by Shared. External file admission is internal to the Workshop item adapter.</summary>
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
    readonly ShippedRoster? trustedRoster;
    internal bool IsBundled => trustedRoster != null || development;
    readonly bool development;

    internal AdmittedFighter(CookedCharacterPackageLoadResult loaded,
        ShippedRoster? trustedRoster, bool development = false)
    {
        if (!loaded.IsValid) throw new ArgumentException("Invalid cooked package.", nameof(loaded));
        if (trustedRoster != null && (loaded.Profile != CharacterCookProfile.TrustedBuiltIn || !trustedRoster.Authorizes(loaded.Identity)))
            throw new ArgumentException("Trusted admission requires a package loaded through the shipped roster.", nameof(loaded));
        if (trustedRoster == null && !development && loaded.Profile != CharacterCookProfile.Workshop)
            throw new ArgumentException("External admission requires the Workshop profile.", nameof(loaded));
        Loaded = loaded;
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
        var loaded = CookedCharacterPackageLoader.LoadFiles(files, requirement, CharacterCookProfile.Workshop);
        if (!loaded.IsValid || !loaded.Identity.PackageId.StartsWith("local.", StringComparison.Ordinal))
            throw new ArgumentException("Invalid local fixture: " + string.Join("; ", loaded.Diagnostics.Select(x => x.Code)));
        return new AdmittedFighter(loaded, null);
    }
}
