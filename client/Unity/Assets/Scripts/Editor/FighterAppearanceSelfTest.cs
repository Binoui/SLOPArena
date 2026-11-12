using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using SlopArena.Client.World;
using SlopArena.Client.Entities;
using SlopArena.Client.Simulation;
using SlopArena.Client.UI;
using SlopArena.Shared.Mayhem;
using UnityEngine.UIElements;
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
        var roster = ShippedRoster.ReadApplication();
        var loaded = roster.Manifest.Entries.ToDictionary(e => e.PackageId,
            e => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(roster.CookedRoot, e.PackageId), e.Requirement));
        var catalog = new MatchContentCatalogBuilder().Build(roster, loaded, MatchContentPolicy.LocalPlay).Catalog;
        Require(catalog != null, "Guest-art fixture catalog failed admission.");
        using var mayhem = new MankiMayhemView(new VisualElement(), Array.Empty<HUDManager.HudPlayer>(), catalog);
        var guest = MayhemChannels.All.First(channel => MankiMayhemView.GuestFor(channel) == "bashie");
        Require(mayhem.GuestPortrait(guest) == FighterPresentation.Resolve(catalog.ResolvePackage("bashie")).Art.Portrait(), "Mayhem did not retain its supplied catalog's guest art.");
        CheckPresentationBoundaries(catalog);
        Debug.Log("[FighterAppearanceSelfTest] selector-free cooked name, neutral portrait, seven neutral slot glyphs and explicit Mayhem guest content passed.");
    }
    static void CheckPresentationBoundaries(MatchContentCatalog catalog)
    {
        var manki = catalog.ResolvePackage("manki");
        var wibou = catalog.ResolvePackage("wibou");
        var go = new GameObject("Fighter binding fixture");
        try
        {
            var probe = go.AddComponent<PvPMatch>();
            bool Bind(ulong id, MatchContentEntry entry) => (bool)typeof(MatchBase).GetMethod("BindFighter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(probe, new object[] { id, entry });
            bool Hud(ulong id) => (bool)typeof(MatchBase).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(m => m.Name == "SetupHUD" && m.GetParameters().Length == 3).Invoke(probe, new object[] { Array.Empty<HUDManager.HudPlayer>(), 0, id });
            Require(Bind(1, manki) && Bind(3, wibou), "Content must bind before any renderer exists.");
            Require(probe.DescribeFeedbackFighters().Count == 2, "Undrawn fighters disappeared from feedback.");
            Require(Hud(1), "HUD must accept bound content without a renderer.");
            Require(probe.FighterFor(3).Content == wibou, "P3 lost its exact admitted content.");
            var changed = new MatchContentEntry(manki.Handle, manki.LegacySelector, manki.Identity with { Version = "different" }, manki.DisplayName, manki.Definition);
            bool rendered = (bool)typeof(MatchBase).GetMethod("SetupRenderer", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(probe, new object[] { go.AddComponent<PlayerRenderer>(), changed, null, 1UL, false });
            Require(!rendered && probe.ContentFailure.Contains("revision"), "Revision mismatch must fail without throwing.");
            Require(!Hud(99) && probe.ContentFailure.Contains("99"), "Missing HUD binding must reject with an entity reason.");
            var hud = go.AddComponent<HUDManager>();
            hud.Initialize(_ => default, new[] { new HUDManager.HudPlayer(1, "P1", FighterPresentation.Resolve(manki).Art, true),
                new HUDManager.HudPlayer(3, "P3", FighterPresentation.Resolve(wibou).Art, true) }, 0);
            hud.SetFighter(1, FighterPresentation.Resolve(manki));
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Require(ReferenceEquals(typeof(HUDManager).GetField("_localArt", fields).GetValue(hud), FighterPresentation.Resolve(manki).Art)
                && ReferenceEquals(typeof(HUDManager).GetField("_charDef", fields).GetValue(hud), manki.Definition), "Deck definition and art must share P1's binding.");
            foreach (var key in new[] { CharacterClass.None, (CharacterClass)99 })
            {
                var panelType = typeof(HUDManager).Assembly.GetType("SlopArena.Client.UI.FighterPanel");
                var panel = (VisualElement)panelType.GetMethod("Build").Invoke(null, new object[] { 2, "Guest", "PLAYER", key, "", false, false, (byte)0 });
                Require(panel.Q<Label>(className: "fighter-panel__placeholder")?.text == "?", "Invalid lobby selector lost its placeholder.");
            }
            var endType = typeof(HUDManager).Assembly.GetType("SlopArena.Client.UI.SoloEndCardView");
            var ending = Activator.CreateInstance(endType, new object[] { new VisualElement() });
            endType.GetMethod("Show").Invoke(ending, new object[] { "P1", null, false });
            endType.GetMethod("ShowPoster").Invoke(ending, new object[] { "END", "DRAW", "", null, null, false });
            var binding = typeof(FighterPresentation).GetMethod("Binding", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "manki" });
            var forms = (FighterUiArt)Activator.CreateInstance(typeof(FighterUiArt), BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { new[] { "Manki", "New form" }, true, binding }, null);
            Require(forms.Portrait(1) == FighterUiArt.NeutralPortrait && forms.MoveIcon(1, "missing", AbilitySlots.A, false).Texture != null,
                "A new form with stale bindings must use neutral art.");
            var missing = FighterPresentation.Menu("local.missing-binding", "Local Fighter");
            Require(missing.Name() == "Local Fighter" && missing.Portrait() == FighterUiArt.NeutralPortrait, "Missing menu binding must degrade.");
            Require(SlopArena.Client.Diagnostics.PracticeSessionLog.Fighter(1, "player", catalog.ResolvePackage("bonk")).Character == "Bonk", "Legacy practice header/slug changed.");
            string assets = Application.dataPath;
            string cards = Path.Combine(assets, "CharacterPackages/bonk/card.json");
            string generated = Path.Combine(assets, "Resources/Generated/FighterUi.json");
            Require(CharacterPackageAuthoringService.HasRosterPresentation("bonk", CharacterClass.Bonk, cards, generated), "Prepared presentation must admit.");
            Require(!CharacterPackageAuthoringService.HasRosterPresentation("local.new-fighter", CharacterClass.Bonk, cards, generated)
                && !CharacterPackageAuthoringService.HasRosterPresentation("bonk", CharacterClass.Bonk, cards + ".missing", generated), "Missing card/binding must refuse admission.");
            var slug = typeof(SlopArena.Client.Diagnostics.PracticeSessionLog).GetMethod("Slug", BindingFlags.Static | BindingFlags.NonPublic);
            Require((string)slug.Invoke(null, new object[] { "Bonk" }) == "bonk", "Legacy filename slug changed.");
            var texture = FighterUiArt.NeutralPortrait;
            typeof(FighterUiArt).GetMethod("ReleaseNeutralTextures", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Require(texture == null && FighterUiArt.NeutralPortrait != null, "Neutral textures were not released and recreated.");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
        Debug.Log("[FighterAppearanceSelfTest] pre-render bindings, undrawn feedback, deck ownership, nonthrowing failures, placeholders, null end cards, stale forms, menu fallback, log key and texture cleanup passed.");
    }
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
