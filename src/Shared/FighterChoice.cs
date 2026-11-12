using System;

namespace SlopArena.Shared;

public sealed class FighterChoice : IEquatable<FighterChoice>
{
    public AdmittedFighter Fighter { get; }
    public MatchContentIdentity Revision => Fighter.Identity;
    public byte StartingForm { get; }
    public bool HasAlternateForm => Fighter.Loaded.Package!.Definition.AlternateForm != null;
    public CharacterClass? LegacySelector => Fighter.LegacySelector;
    internal FighterChoice(AdmittedFighter fighter, byte form)
    { Fighter = fighter; StartingForm = form; }
    public FighterChoice WithForm(byte form) => Fighter.Choose(form);
    public MatchContentEntry? Resolve(MatchContentCatalog catalog)
    {
        var entry = catalog.ResolvePackage(Revision.PackageId);
        return entry?.Identity == Revision ? entry : null;
    }
    public bool Equals(FighterChoice? other) => other != null && Revision == other.Revision && StartingForm == other.StartingForm;
    public override bool Equals(object? other) => other is FighterChoice choice && Equals(choice);
    public override int GetHashCode() => HashCode.Combine(Revision, StartingForm);
}
