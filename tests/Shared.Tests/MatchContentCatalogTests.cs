using System.IO;
using SlopArena.Shared;
using SlopArena.Shared.Tests;
using Xunit;

namespace SlopArena.Tests;

public sealed class MatchContentCatalogTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void CommittedFightGuyPackage_LoadsWithExactIdentity()
    {
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));
        var roster = manifest.Resolve(CharacterClass.FightGuy)!;
        Assert.NotNull(roster);
        var result = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(Root, "content-cooked/fightguy"), roster.Requirement);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics));
        Assert.Equal("fightguy", result.Identity.PackageId);
        Assert.Equal(roster.Requirement.CookedContentHash, result.Identity.CookedContentHash);
        Assert.Equal(14, result.Package!.Definition.Slots.Count);
        Assert.NotNull(result.BakedAnimation);
    }


    [Fact]
    public void Catalog_AssignsHandlesByStablePackageId()
    {
        var roster = BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));
        var fightGuy = roster.Resolve(CharacterClass.FightGuy)!;
        var bonk = roster.Resolve(CharacterClass.Bonk)!;
        var wibou = roster.Resolve(CharacterClass.Wibou)!;
        var manki = roster.Resolve(CharacterClass.Manki)!;
        var manifest = new BuiltInRosterManifest(roster.SchemaVersion, new[] { bonk, fightGuy, wibou, manki });
        var loadedFightGuy = CookedCharacterPackageLoader.LoadDirectory(
            Path.Combine(Root, "content-cooked/fightguy"), fightGuy.Requirement);
        var loadedWibou = CookedCharacterPackageLoader.LoadDirectory(
            Path.Combine(Root, "content-cooked/wibou"), wibou.Requirement);
        var loadedManki = CookedCharacterPackageLoader.LoadDirectory(
            Path.Combine(Root, "content-cooked/manki"), manki.Requirement);
        var loadedBonk = CookedCharacterPackageLoader.LoadDirectory(
            Path.Combine(Root, "content-cooked/bonk"), bonk.Requirement);

        Assert.True(loadedFightGuy.IsValid, string.Join("; ", loadedFightGuy.Diagnostics));
        Assert.True(loadedWibou.IsValid, string.Join("; ", loadedWibou.Diagnostics));
        Assert.True(loadedManki.IsValid, string.Join("; ", loadedManki.Diagnostics));
        Assert.True(loadedBonk.IsValid, string.Join("; ", loadedBonk.Diagnostics));
        var result = new MatchContentCatalogBuilder().Build(
            manifest,
            new Dictionary<string, CookedCharacterPackageLoadResult>
            {
                ["bonk"] = loadedBonk,
                ["fightguy"] = loadedFightGuy,
                ["wibou"] = loadedWibou,
                ["manki"] = loadedManki,
            });
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics));
        var catalog = result.Catalog!;
        Assert.Equal(4, catalog.Entries.Count);
        Assert.Equal(1, catalog.ResolvePackage("bonk")!.Handle.Value);
        Assert.Equal(2, catalog.ResolvePackage("fightguy")!.Handle.Value);
        Assert.Equal(3, catalog.ResolvePackage("manki")!.Handle.Value);
        Assert.Equal(4, catalog.ResolvePackage("wibou")!.Handle.Value);
    }

    [Fact]
    public void EntrySharesImmutableContentAndPreservesDefensePresentation()
    {
        var definition = TestHelpers.EngineDef with
        {
            ShieldAnim = "shield.test", GrabAnim = "grab.test", GrabbedAnim = "grabbed.test",
            ThrowForwardAnim = "throw.test", AirDodgeAnim = "dodge.test",
        };
        var identity = new MatchContentIdentity("test", "1", "", "", "");
        var entry = new MatchContentEntry(new ContentHandle(1), CharacterClass.Manki, identity, "test", definition);
        Assert.Same(definition, entry.Definition);
        Assert.Equal("shield.test", entry.Definition.ShieldAnim);
        Assert.Equal("grab.test", entry.Definition.GrabAnim);
        Assert.Equal("grabbed.test", entry.Definition.GrabbedAnim);
        Assert.Equal("throw.test", entry.Definition.ThrowForwardAnim);
        Assert.Equal("dodge.test", entry.Definition.AirDodgeAnim);
    }

    [Fact]
    public void MissingCookedWibouPackage_FailsClosed()
    {
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));
        var fightGuy = manifest.Resolve(CharacterClass.FightGuy)!;
        var loaded = CookedCharacterPackageLoader.LoadDirectory(Path.Combine(Root, "content-cooked/fightguy"), fightGuy.Requirement);
        var result = new MatchContentCatalogBuilder().Build(manifest, new Dictionary<string, CookedCharacterPackageLoadResult> { ["fightguy"] = loaded });
        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, x => x.Code == "catalog.package.missing" && x.Path == "wibou");
    }

    [Fact]
    public void HandleMap_RoundTripsDeterministically()
    {
        var identity = new MatchContentIdentity("fightguy", "0.0.0-dev", new string('a',64), new string('b',64), new string('c',64));
        var map = new MatchContentHandleMap(1, new[] { new MatchContentHandleRecord(new ContentHandle(2), CharacterClass.FightGuy, identity, "FightGuy") });
        var json = MatchContentHandleMapCodec.Serialize(map);
        Assert.True(MatchContentHandleMapCodec.TryParse(json, out var parsed));
        Assert.Equal(json, MatchContentHandleMapCodec.Serialize(parsed!));
    }
    [Fact]
    public void CookedLoader_RejectsMissingRequiredPayload()
    {
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));
        var requirement = manifest.Resolve(CharacterClass.FightGuy)!.Requirement;
        var files = new Dictionary<string, byte[]>
        {
            ["manifest.json"] = File.ReadAllBytes(Path.Combine(Root, "content-cooked/fightguy/manifest.json")),
            ["character.runtime.json"] = File.ReadAllBytes(Path.Combine(Root, "content-cooked/fightguy/character.runtime.json")),
            ["client.bindings"] = File.ReadAllBytes(Path.Combine(Root, "content-cooked/fightguy/client.bindings"))
        };
        Assert.False(CookedCharacterPackageLoader.LoadFiles(files, requirement).IsValid);
    }

    [Fact]
    public void HandleMap_RejectsUnknownFieldsAndDuplicateHandles()
    {
        var unknown = "{\"schemaVersion\":1,\"entries\":[],\"extra\":true}";
        Assert.False(MatchContentHandleMapCodec.TryParse(unknown, out _));
        var duplicate = "{\"schemaVersion\":1,\"entries\":[{\"handle\":1,\"selector\":\"FightGuy\",\"identity\":{\"packageId\":\"fightguy\",\"version\":\"1\",\"sourceHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"cookedContentHash\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"packageHash\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"},\"displayName\":\"FightGuy\"},{\"handle\":1,\"selector\":\"Manki\",\"identity\":{\"packageId\":\"manki\",\"version\":\"1\",\"sourceHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"cookedContentHash\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"packageHash\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"},\"displayName\":\"Manki\"}]}";
        Assert.False(MatchContentHandleMapCodec.TryParse(duplicate, out _));
    }
}
