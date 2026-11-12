using System;
using System.IO;
using SlopArena.Shared;
using SlopArena.Client;
using UnityEditor;
using UnityEngine;

public static class FighterAppearanceSelfTest
{
    [MenuItem("SLOPArena/Tests/Fighter appearance")]
    public static void Run()
    {
        var folder = Path.Combine(Application.dataPath, "CharacterPackages/salamander");
        string metadata = File.ReadAllText(Path.Combine(folder, "package.json")).Replace("\"salamander\"", "\"local.appearance-fixture\"");
        string character = File.ReadAllText(Path.Combine(folder, "character.json")).Replace("\"Salamander\"", "\"Fixture Fighter\"");
        var compiled = CharacterPackageCompiler.Compile(metadata, character, CharacterCookProfile.Workshop);
        if (compiled.CookedPackage == null) throw new InvalidOperationException("Appearance fixture did not compile.");
        var package = compiled.CookedPackage;
        var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(package);
        var identity = new MatchContentIdentity(package.Metadata.PackageId, package.Metadata.Version, "fixture", "fixture", "fixture");
        var content = new MatchContentEntry(new ContentHandle(1), null, identity, definition.DisplayName, definition, cookedCharacterPackage: package);
        var appearance = FighterPresentation.Resolve(content);
        Require(appearance.Art.Name() == "Fixture Fighter" && definition.Class == CharacterClass.None, "Fixture lost its cooked name or borrowed a selector.");
        Require(appearance.Art.Portrait() == FighterUiArt.NeutralPortrait, "Fixture borrowed a portrait.");
        foreach (byte slot in new[] { AbilitySlots.Normal1, AbilitySlots.Normal2, AbilitySlots.Normal3, AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F })
        {
            var image = appearance.Art.MoveIcon(0, "icon." + slot, slot, slot is AbilitySlots.Normal1 or AbilitySlots.Normal2 or AbilitySlots.Normal3);
            Require(image.Texture != null && image.Texture.name == "Neutral slot " + slot && image.Sprite == null, "Fixture borrowed slot art.");
        }
        Require(ReferenceEquals(appearance, FighterPresentation.Resolve(content)), "Content did not retain one appearance.");
        Debug.Log("[FighterAppearanceSelfTest] selector-free cooked name, neutral portrait and seven neutral slot glyphs passed.");
    }
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
