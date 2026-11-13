using System.Text.Json;
using SlopArena.Client.UI;
using SlopArena.Shared.Replays;
using SlopArena.Shared.Tests.Replays;
using Xunit;

namespace SlopArena.Shared.Tests;

public partial class PackageFighterContentTests
{
    internal static string CopyTwoFighterRoot(string root)
    {
        string target = Path.Combine(root, "cooked");
        Directory.CreateDirectory(Path.Combine(target,"roster"));
        var shipped = ShippedRoster.ReadApplication();
        var entries = shipped.Manifest.Entries.Where(e => e.Selector is CharacterClass.FightGuy or CharacterClass.Manki).ToArray();
        File.WriteAllText(Path.Combine(target,"roster/manifest.json"),BuiltInRosterManifestCodec.Serialize(new(1,entries)));
        foreach(var entry in entries)
        {
            Directory.CreateDirectory(Path.Combine(target,entry.PackageId));
            foreach(string name in new[] { "manifest.json","character.runtime.json","poses.bin","client.bindings" })
                File.Copy(Path.Combine(shipped.CookedRoot,entry.PackageId,name),Path.Combine(target,entry.PackageId,name));
        }
        return target;
    }

    [Fact]
    public void ExplicitContentRootUsesItsOwnSubsetAndCannotAuthorizeStandardRoom()
    {
        string temp = Path.Combine(Path.GetTempPath(),"sloparena-tool-"+Guid.NewGuid().ToString("N"));
        try
        {
            string root = CopyTwoFighterRoot(temp);
            var medium = BuiltInContentResolver.Resolve(CharacterClass.FightGuy,root);
            var small = BuiltInContentResolver.Resolve(CharacterClass.Manki,root);
            Assert.Equal((ushort)1,medium.Handle.Value);
            Assert.Equal((ushort)2,small.Handle.Value);
            var targets = HitReliabilityTargets.Load(root);
            Assert.Equal(medium.Identity,targets.Medium.Identity);
            Assert.Equal(small.Identity,targets.Small.Identity);
            var manifest=BuiltInRosterManifestCodec.Load(Path.Combine(root,"roster/manifest.json"));
            var loaded=manifest.Entries.ToDictionary(e=>e.PackageId,e=>CookedCharacterPackageLoader.LoadDirectory(Path.Combine(root,e.PackageId),e.Requirement));
            Assert.False(new MatchContentCatalogBuilder().Build(manifest,loaded,MatchContentPolicy.StandardRoom).IsValid);
        }
        finally { if(Directory.Exists(temp)) Directory.Delete(temp,true); }
    }

    [Fact]
    public void SelectionStateResolvesAdmittedPackageAndForm()
    {
        try
        {
            MatchConfig.SetPlayerFighter(Bundled().ResolvePackage("nyx")!);
            MatchConfig.PlayerForm=1;
            var saved=MatchConfig.Selection;
            string json=JsonSerializer.Serialize(new FighterChoiceState(saved.Choice), new JsonSerializerOptions { IncludeFields = true });
            MatchConfig.SetPlayerFighter(CharacterClass.FightGuy,"fightguy");
            MatchConfig.PlayerForm=0;
            MatchConfig.SetPlayerFighter(JsonSerializer.Deserialize<FighterChoiceState>(json, new JsonSerializerOptions { IncludeFields = true })!.Restore());
            Assert.Equal(CharacterClass.Nyx,MatchConfig.PlayerClass);
            Assert.Equal("nyx",MatchConfig.PlayerPackageId);
            Assert.Equal((byte)1,MatchConfig.PlayerForm);
            Assert.Equal(CharacterClass.Nyx,saved.Selector);
            Assert.Equal((byte)1,saved.Form);
            Assert.Equal("nyx",saved.PackageId);
        }
        finally { MatchConfig.Reset(); MatchConfig.PlayerForm=0; }
    }

    [Theory]
    [InlineData("5")]
    [InlineData("129")]
    [InlineData("unknown")]
    [InlineData("None")]
    public void InvalidDevelopmentSelectionCannotResolveToAnotherFighter(string selection)
    {
        try
        {
            MatchConfig.SetPlayerFighter(CharacterClass.FightGuy,"fightguy");
            var previous=MatchConfig.Selection;
            Assert.Null(Bundled().ResolveLegacySelection(selection,CharacterClass.Manki));
            Assert.Same(previous,MatchConfig.Selection);
            Assert.Equal("fightguy",MatchConfig.PlayerPackageId);
            Assert.NotNull(Bundled().ResolveLegacySelection("Salamander",CharacterClass.Manki));
        }
        finally { MatchConfig.Reset(); }
    }

    [Fact]
    public void RosterCacheReusesReadsReloadsChangedEditorFilesAndRetainsFailures()
    {
        string temp=Path.Combine(Path.GetTempPath(),"sloparena-cache-"+Guid.NewGuid().ToString("N"));
        try
        {
            string root=CopyTwoFighterRoot(temp);
            string path=Path.Combine(root,"roster/manifest.json");
            string json=File.ReadAllText(path);
            int locations=0;
            var editor=new ShippedRoster.Cache(()=>{ locations++; return(root,true); });
            var first=editor.Read();
            Assert.Same(first,editor.Read());
            Assert.Equal(1,locations);
            File.WriteAllText(path,"corrupt");
            var error=Assert.Throws<InvalidDataException>(()=>editor.Read());
            Assert.Contains("content.roster.invalid",error.Message);
            Assert.Equal(error.Message,Assert.Throws<InvalidDataException>(()=>editor.Read()).Message);
            File.WriteAllText(path,json);
            File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddSeconds(2));
            Assert.NotSame(first,editor.Read());
            var player=new ShippedRoster.Cache(()=>(root,false));
            var pinned=player.Read();
            File.Delete(path);
            Assert.Same(pinned,player.Read());
            var missing=new ShippedRoster.Cache(()=>(root,false));
            Assert.Contains("content.roster.invalid",Assert.Throws<InvalidDataException>(()=>missing.Read()).Message);
            File.WriteAllText(path,json);
            Assert.Throws<InvalidDataException>(()=>missing.Read());
            Assert.NotNull(new ShippedRoster.Cache(()=>(root,false)).Read());
        }
        finally { if(Directory.Exists(temp)) Directory.Delete(temp,true); }
    }

    [Fact]
    public void ApplicationLocationFailureAndEmptyLocationHaveExplicitDiagnostics()
    {
        string temp=Path.Combine(Path.GetTempPath(),"sloparena-locate-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var absent=Assert.Throws<InvalidDataException>(()=>ShippedRoster.LocateApplication(Path.Combine(temp,"Shared.dll"),temp,false));
            Assert.Contains("content.application.root-missing",absent.Message);
            var empty=Assert.Throws<InvalidDataException>(()=>ShippedRoster.LocateApplication("",temp,false));
            Assert.Contains("Assembly.Location is empty",empty.Message);
            Assert.Contains("content.application.location-empty",Assert.Throws<InvalidDataException>(()=>ShippedRoster.LocateApplication("","",false)).Message);
            string data=Path.Combine(temp,"Game_Data");Directory.CreateDirectory(data);
            Assert.Equal(Path.Combine(data,"StreamingAssets/content-cooked"),ShippedRoster.LocateApplication("",data,false).Root);
            int attempts=0;
            var broken=new ShippedRoster.Cache(()=>{ attempts++;throw new InvalidDataException("content.application.root-missing"); });
            Assert.Throws<InvalidDataException>(()=>broken.Read());Assert.Throws<InvalidDataException>(()=>broken.Read());
            Assert.Equal(1,attempts);
        }
        finally { Directory.Delete(temp,true); }
    }

    [Fact]
    public void RosterDefaultUsesAnExplicitAliasWithoutLowercasingTheSelector()
    {
        string temp=Path.Combine(Path.GetTempPath(),"sloparena-default-"+Guid.NewGuid().ToString("N"));
        try
        {
            string root=CopyTwoFighterRoot(temp),path=Path.Combine(root,"roster/manifest.json");
            var manifest=BuiltInRosterManifestCodec.Load(path);
            var entries=manifest.Entries.Select(e=>e.Selector==CharacterClass.FightGuy
                ? e with { PackageId="curated.fighter",Requirement=e.Requirement with { PackageId="curated.fighter" } }:e).ToArray();
            File.WriteAllText(path,BuiltInRosterManifestCodec.Serialize(new(1,entries)));
            var roster=new ShippedRoster.Cache(()=>(root,false)).Read();
            Assert.Equal(CharacterClass.FightGuy,roster.DefaultFighter.Selector);
            Assert.Equal("curated.fighter",roster.DefaultFighter.PackageId);
        }
        finally { if(Directory.Exists(temp))Directory.Delete(temp,true); }
    }

    [Fact]
    public void ReplayRefusalNamesMissingParticipantInsteadOfClaimingSizeFailure()
    {
        var catalog=Bundled();
        var arena=ReplayWorldTests.Content(catalog).LoadArena("slop_pit")!;
        var participants=catalog.Entries.Take(2).Select((e,i)=>new ReplayParticipant((ulong)i+1,"cpu","",e.Identity,"base","","",e.DisplayName)).ToArray();
        participants[0]=participants[0] with { Package=participants[0].Package with { PackageId="local.missing" } };
        var header=new ReplayHeader("bad-map",arena.Definition.Name,new("stock","off",3,false),1,participants,ReplayBuildIdentity.Current("test","pending"),arena.Digest);
        Assert.False(header.TryWithLocalContent(catalog,out var prepared,out var refusal));
        Assert.Null(prepared);
        Assert.Contains("local.missing",refusal);
        Assert.DoesNotContain("64 KiB",refusal);
    }

    [Fact]
    public void BuildRosterEmitsEachSchemaOrDuplicateErrorOnce()
    {
        var roster=ShippedRoster.ReadApplication();
        var packages=roster.Manifest.Entries.ToDictionary(e=>e.PackageId,e=>CookedCharacterPackageLoader.LoadDirectory(Path.Combine(roster.CookedRoot,e.PackageId),e.Requirement));
        var schema=new MatchContentCatalogBuilder().Build(new BuiltInRosterManifest(42,roster.Manifest.Entries),packages);
        Assert.Single(schema.Diagnostics,d=>d.Code=="catalog.manifest.schema");
        var duplicate=new MatchContentCatalogBuilder().Build(new BuiltInRosterManifest(1,roster.Manifest.Entries.Append(roster.Manifest.Entries[0]).ToArray()),packages);
        Assert.Single(duplicate.Diagnostics,d=>d.Code=="catalog.roster.duplicate");
        Assert.DoesNotContain(duplicate.Diagnostics,d=>d.Code=="catalog.selector.duplicate"||d.Code=="catalog.package.duplicate");
    }
}
