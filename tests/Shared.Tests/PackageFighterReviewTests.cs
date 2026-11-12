using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Client.UI;
using SlopArena.Shared.Replays;
using SlopArena.Shared.Tests.Replays;
using Xunit;

namespace SlopArena.Shared.Tests;

public partial class PackageFighterContentTests
{
    [Fact]
    public void CallerPinsAndDevelopmentContentCannotAuthorizeAStandardRoom()
    {
        var forged = Fixture("bonk");
        var loaded = CookedCharacterPackageLoader.LoadFiles(forged.Files, forged.Requirement);
        Assert.True(loaded.IsValid);
        var dto = new BuiltInRosterManifest(1, new[] { new BuiltInRosterEntry(CharacterClass.Bonk,"bonk",forged.Requirement) });
        var packages = new Dictionary<string,CookedCharacterPackageLoadResult> { ["bonk"] = loaded };
        var builder = new MatchContentCatalogBuilder();
        Assert.False(builder.Build(dto,packages,MatchContentPolicy.StandardRoom).IsValid);
        Assert.False(builder.Build(dto,packages,MatchContentPolicy.LocalPlay).IsValid);
        Assert.True(builder.Build(dto,packages,MatchContentPolicy.EditorDevelopment).IsValid);
        Assert.Empty(typeof(ShippedRoster).GetConstructors());
        var shipped = ShippedRoster.ReadApplication();
        var all = shipped.Manifest.Entries.ToDictionary(e => e.PackageId,
            e => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(shipped.CookedRoot,e.PackageId),e.Requirement));
        Assert.True(builder.Build(shipped,all,MatchContentPolicy.StandardRoom).IsValid);
        all["bonk"] = loaded;
        Assert.False(builder.Build(shipped,all,MatchContentPolicy.StandardRoom).IsValid);
        var development = new AdmittedFighter(loaded,CharacterClass.Bonk,null,true);
        Assert.False(builder.Build(new[] { development },MatchContentPolicy.StandardRoom).IsValid);
    }

    [Fact]
    public void PlayerSetterPublishesOneValidatedPairAndRejectsMismatchWithoutMutation()
    {
        try
        {
            MatchConfig.SetPlayerFighter(CharacterClass.FightGuy,"fightguy");
            Assert.Throws<ArgumentException>(() => MatchConfig.SetPlayerFighter(CharacterClass.FightGuy,"salamander"));
            Assert.Equal(CharacterClass.FightGuy,MatchConfig.PlayerClass);
            Assert.Equal("fightguy",MatchConfig.PlayerPackageId);
            MatchConfig.SetPlayerFighter(Bundled().ResolvePackage("salamander")!);
            Assert.Equal(CharacterClass.Salamander,MatchConfig.PlayerClass);
            Assert.Equal("salamander",MatchConfig.PlayerPackageId);
            Assert.Null(typeof(MatchConfig).GetProperty(nameof(MatchConfig.PlayerClass))!.SetMethod);
            Assert.Null(typeof(MatchConfig).GetProperty(nameof(MatchConfig.PlayerPackageId))!.SetMethod);
            MatchConfig.SetPlayerFighter(Local().ResolvePackage("local.tumbleweed")!);
            Assert.Equal(CharacterClass.None,MatchConfig.PlayerClass);
            Assert.Equal("local.tumbleweed",MatchConfig.PlayerPackageId);
            MatchConfig.Reset();
            Assert.Equal(CharacterClass.FightGuy,MatchConfig.PlayerClass);
            Assert.Equal("fightguy",MatchConfig.PlayerPackageId);
        }
        finally { MatchConfig.Reset(); }
    }

    [Fact]
    public void NoMapReplayKeepsExactAfterASelectorFreeInstallShiftsHandles()
    {
        var bundled = Bundled();
        var installed = Local();
        var content = ReplayWorldTests.Content(installed);
        var arena = content.LoadArena("slop_pit")!;
        var roster = new[] { bundled.ResolvePackage("fightguy")!,bundled.ResolvePackage("nyx")! }
            .Select((e,i) => new ReplayParticipant((ulong)i+1,"cpu","",e.Identity,"base","","",e.DisplayName)).ToArray();
        var map = MatchContentHandleMap.CaptureBundled(bundled,new(arena.Definition.Name,arena.Digest));
        var header = new ReplayHeader("old",arena.Definition.Name,new("stock","off",3,false),1,roster,
            ReplayBuildIdentity.Current("old",SteamMatchDescriptor.HashContent(map)),arena.Digest);
        Assert.NotEqual(bundled.ResolvePackage("nyx")!.Handle,installed.ResolvePackage("nyx")!.Handle);
        var rebuilt = ReplayExactEnvironment.For(content,arena,header);
        Assert.Null(ReplayExact.Check(header,rebuilt));
        Assert.Equal(header.Build.ContentHash,rebuilt.MatchContentHash);
        Assert.Null(ReplayMetadata.ReadReplayHeader(ReplayMetadata.Write(header)).ContentMap);
    }

    [Fact]
    public void OnlineRoutesAndHashingRefuseLocalMapsAndLocalParserRefusesSelectors()
    {
        var map = MatchContentHandleMap.CaptureLocal(Local(),new("slop_pit",new string('d',64)));
        Assert.Throws<ArgumentException>(() => SteamMatchDescriptor.HashContent(map));
        var json = MatchContentHandleMapCodec.Serialize(map);
        var obj = JsonNode.Parse(json)!;
        obj["entries"]![0]!["selector"] = "Bonk";
        Assert.False(MatchContentHandleMapCodec.TryParse(obj.ToJsonString(),out _));
        foreach (bool steam in new[] { false,true })
        {
            var payload = new JsonObject
            {
                ["serverId"] = Guid.NewGuid().ToString(), ["matchId"] = Guid.NewGuid().ToString(),
                ["arenaName"] = "slop_pit", ["matchPort"] = steam ? 0 : 9877,
                ["players"] = new JsonArray(), ["content"] = JsonNode.Parse(json),
            };
            if (steam) payload["descriptor"] = new JsonObject
            {
                ["transport"] = "steam-p2p",["matchId"] = payload["matchId"]!.GetValue<string>(),["serverSteamId"] = "90293421017699331",
                ["virtualPort"] = 0,["protocolVersion"] = SteamMatchDescriptor.CurrentProtocolVersion,
                ["contentHash"] = MatchContentHandleMapCodec.HashLocalContent(map),["admissionExpiresAtUtc"] = DateTimeOffset.UtcNow.AddSeconds(60).ToString("O")
            };
            using var document = JsonDocument.Parse(payload.ToJsonString());
            Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(document.RootElement));
        }
    }

    [Fact]
    public void GenericBuilderReportsNullRatherThanDereferencingIt()
    {
        var result = new MatchContentCatalogBuilder().Build(new AdmittedFighter[] { null! },MatchContentPolicy.LocalPlay);
        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics,d => d.Code == "catalog.fighter.null");
    }

    [Fact]
    public void RosterAliasesAreExplicitMappingsRatherThanLowercaseEnumNames()
    {
        var fixture = Fixture();
        var loaded = CookedCharacterPackageLoader.LoadFiles(fixture.Files,fixture.Requirement);
        var dto = new BuiltInRosterManifest(1,new[] { new BuiltInRosterEntry(CharacterClass.Bonk,fixture.Requirement.PackageId,fixture.Requirement) });
        var serialized = BuiltInRosterManifestCodec.Serialize(dto);
        var read = BuiltInRosterManifestCodec.ParseCooked(serialized);
        Assert.Empty(MatchContentCatalogBuilder.ValidateRoster(read));
        Assert.True(new MatchContentCatalogBuilder().Build(read,new Dictionary<string,CookedCharacterPackageLoadResult>
            { [fixture.Requirement.PackageId] = loaded },MatchContentPolicy.EditorDevelopment).IsValid);
        Assert.False(new MatchContentCatalogBuilder().Build(read,new Dictionary<string,CookedCharacterPackageLoadResult>
            { [fixture.Requirement.PackageId] = loaded },MatchContentPolicy.StandardRoom).IsValid);
    }

    [Fact]
    public void RecordingRefusesOversizedHeaderBeforeCaptureAndAcceptsTheLastFittingMap()
    {
        var basis = Bundled().Entries[0];
        MatchContentCatalog Catalog(int count) => new(Enumerable.Range(1,count).Select(i => new MatchContentEntry(
            new((ushort)i),null,basis.Identity with { PackageId = "local.bound"+i },new string('x',100),basis.Definition,basis.BakedAnimation,basis.CookedCharacterPackage)).ToArray());
        var content = ReplayWorldTests.Content();
        var arena = content.LoadArena("slop_pit")!;
        ReplayHeader Header(MatchContentCatalog catalog) => new("bound",arena.Definition.Name,new("stock","off",3,false),1,
            catalog.Entries.Take(2).Select((e,i) => new ReplayParticipant((ulong)i+1,"cpu","",e.Identity,"base","","",e.DisplayName)).ToArray(),
            ReplayBuildIdentity.Current("test","pending"),arena.Digest);
        int count = 2;
        ReplayHeader? last = null;
        for (; count < 300; count++)
        {
            var catalog = Catalog(count);
            if (!Header(catalog).TryWithLocalContent(catalog,out var prepared,out var refusal))
            {
                Assert.Null(prepared);
                Assert.Contains("64 KiB",refusal);
                break;
            }
            last = prepared;
        }
        Assert.InRange(count,3,299);
        Assert.InRange(ReplayMetadata.Write(last!).Length,64000,ReplayStorage.MaxHeaderBytes);
        using var output = new MemoryStream();
        using var writer = new ReplayWriter(output,last!);
        Assert.True(output.Length > 64000);
        var sim = TestHelpers.MakeSim();
        sim.Tick(new Dictionary<ulong,InputState>());
        Assert.Equal(1u,sim.CurrentTick);
    }

    [Fact]
    public void SelectorFreeFighterAndBundledOpponentExecuteAttacksForHundredsOfTicks()
    {
        var catalog = Local();
        var a = catalog.ResolvePackage("local.tumbleweed")!;
        var b = catalog.ResolvePackage("salamander")!;
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1,a.Definition,TestHelpers.PlayerState(x:0,z:0),a.BakedAnimation);
        sim.RegisterEntity(2,b.Definition,TestHelpers.PlayerState(x:0,z:1),b.BakedAnimation);
        int localAttacks=0,bundledAttacks=0;
        for (int tick=0;tick<360;tick++)
        {
            sim.Tick(new Dictionary<ulong,InputState>
            {
                [1] = new() { ActiveSlot = tick%60 == 0 ? AbilitySlots.Normal1 : (byte)0 },
                [2] = new() { ActiveSlot = tick%60 == 30 ? AbilitySlots.Normal1 : (byte)0 },
            });
            if (sim.GetActiveMove(1)!=null) localAttacks++;
            if (sim.GetActiveMove(2)!=null) bundledAttacks++;
        }
        Assert.True(localAttacks>10);
        Assert.True(bundledAttacks>10);
        Assert.Equal(360u,sim.CurrentTick);
        Assert.Equal(CharacterClass.None,sim.GetDefinition(1)!.Class);
        Assert.Equal(CharacterClass.Salamander,sim.GetDefinition(2)!.Class);
    }
}
