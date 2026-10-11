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
        Assert.Equal(16, result.Package!.Definition.Slots.Count);
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
    public void EntryCopiesMutableContentAndPreservesDefensePresentation()
    {
        var source = TestHelpers.EngineDef;
        source.ShieldAnim = "shield.test";
        source.GrabAnim = "grab.test";
        source.GrabbedAnim = "grabbed.test";
        source.ThrowForwardAnim = "throw.test";
        source.AirDodgeAnim = "dodge.test";
        source.HurtboxBoneDefs = new[] { new HurtboxBoneDef("bone", 0, 0, 0, .4f) };
        source.ClipOverrides = new[] { new AnimationClipConfig { Name = "clip", FrameRateOverride = 30 } };
        source.Slot1 = new AbilitySpec
        {
            AimAnimationId = "aim.test",
            HitPresentationId = "hit.test",
            AnimationNames = new[] { "swing" },
            Params = new() { ["hitstop_multiplier"] = .5f },
            BoneTrails = new[] { new BoneTrailDef { Width = .1f } },
            Stages = new[]
            {
                new AttackStage
                {
                    DurationTicks = 20,
                    HitboxEvents = new[] { new HitboxEvent { Damage = 7 } },
                    BoneTrails = new[] { new BoneTrailDef { Width = .2f } },
                },
            },
        };
        source.AirSlot1 = source.Slot1;
        var slots = source.CookedSlots!.ToArray();
        source.CookedSlots = slots;
        var baked = new BakedAnimationData
        {
            BoneNames = new[] { "bone" },
            Animations = new[] { new BakedAnimationData.BakedAnim { Name = "swing", FrameCount = 1, Frames = new[] { new[] { 1f, 2f, 3f } } } },
        };
        var identity = new MatchContentIdentity("test", "1", "", "", "");
        var entry = new MatchContentEntry(new ContentHandle(1), CharacterClass.Manki, identity, "test", source, baked);

        source.Movement.Gravity = 99;
        source.HurtboxCapsules![0].Radius = 9;
        source.HurtboxBoneDefs[0].Radius = 9;
        source.ClipOverrides[0].FrameRateOverride = 99;
        source.Slot1.Params["hitstop_multiplier"] = 9;
        source.Slot1.AnimationNames[0] = "changed";
        source.Slot1.BoneTrails[0].Width = 9;
        source.Slot1.Stages[0].HitboxEvents[0].Damage = 99;
        source.Slot1.Stages[0].BoneTrails![0].Width = 9;
        slots[0] = slots[1];
        baked.BoneNames[0] = "changed";
        baked.Animations[0].Frames[0][0] = 99;

        var copy = entry.Definition;
        Assert.Equal("shield.test", copy.ShieldAnim);
        Assert.Equal("grab.test", copy.GrabAnim);
        Assert.Equal("grabbed.test", copy.GrabbedAnim);
        Assert.Equal("throw.test", copy.ThrowForwardAnim);
        Assert.Equal("dodge.test", copy.AirDodgeAnim);
        Assert.Equal(36f, copy.Movement.Gravity);
        Assert.Equal(.3f, copy.HurtboxCapsules![0].Radius);
        Assert.Equal(.4f, copy.HurtboxBoneDefs![0].Radius);
        Assert.Equal(30f, copy.ClipOverrides![0].FrameRateOverride);
        Assert.Equal("aim.test", copy.Slot1!.AimAnimationId);
        Assert.Equal("hit.test", copy.Slot1.HitPresentationId);
        Assert.Equal(.5f, copy.Slot1.Params["hitstop_multiplier"]);
        Assert.Equal("swing", copy.Slot1.AnimationNames![0]);
        Assert.Equal(.1f, copy.Slot1.BoneTrails![0].Width);
        Assert.Equal(7f, copy.Slot1.Stages[0].HitboxEvents[0].Damage);
        Assert.Equal(7f, copy.AirSlot1!.Stages[0].HitboxEvents[0].Damage);
        Assert.Equal(.2f, copy.Slot1.Stages[0].BoneTrails![0].Width);
        Assert.Equal("ground.1", copy.CookedSlots![0].Id);
        Assert.Equal("bone", entry.BakedAnimation!.BoneNames[0]);
        Assert.Equal(1f, entry.BakedAnimation.Animations[0].Frames[0][0]);
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
