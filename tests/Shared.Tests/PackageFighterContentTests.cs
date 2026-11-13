using System.Text.Json.Nodes;
using SlopArena.Shared;
using SlopArena.Shared.Replays;
using SlopArena.Shared.Tests.Replays;
using Xunit;

namespace SlopArena.Shared.Tests;

[Collection("Local fighter state")]
public partial class PackageFighterContentTests
{
    static string Root => ReplayWorldTests.Root;
    static MatchContentCatalog Bundled()
    {
        var roster = BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"));
        return new MatchContentCatalogBuilder().Build(ShippedRoster.ReadApplication(), roster.Entries.ToDictionary(e => e.PackageId,
            e => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(Root, "content-cooked", e.PackageId), e.Requirement))).Catalog!;
    }

    static (Dictionary<string, byte[]> Files, MatchContentPackageRequirement Requirement) Fixture(string packageId = "local.tumbleweed", string? version = null, bool forms = false)
    {
        var path = Path.Combine(Root, "client/Unity/Assets/CharacterPackages/salamander");
        var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(path, "package.json")))!;
        metadata["packageId"] = packageId;
        if (version != null) metadata["version"] = version;
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(path, "character.json")))!;
        if (forms)
        {
            ((JsonArray)character["capabilityRequirements"]!).Add(new JsonObject { ["capabilityId"] = "slop.ability.forms.v1", ["capabilityVersion"] = "1" });
            var formPresentation = character["presentation"]!.DeepClone().AsObject();
            var allowed = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "client/Unity/Assets/CharacterPackages/nyx/character.json")))!["alternateForm"]!["presentation"]!.AsObject().Select(pair => pair.Key).ToHashSet();
            foreach (var key in formPresentation.Select(pair => pair.Key).Where(key => !allowed.Contains(key)).ToArray()) formPresentation.Remove(key);
            character["alternateForm"] = new JsonObject {
                ["id"] = "other", ["displayName"] = "Other form", ["movement"] = character["movement"]!.DeepClone(),
                ["presentation"] = formPresentation, ["slots"] = character["slots"]!.DeepClone(), ["aliases"] = character["aliases"]!.DeepClone()
            };
        }
        var compiled = CharacterPackageCompiler.Compile(metadata.ToJsonString(), character.ToJsonString(), CharacterCookProfile.Workshop);
        Assert.True(compiled.CookedPackage != null, string.Join("; ", compiled.Diagnostics.Select(d => d.Path + ": " + d.Message)));
        var package = compiled.CookedPackage!;
        var assembly = CharacterPackageAssembler.Assemble(CharacterPackageAssemblerTests.BuildInput(package,
            Array.Empty<PackageDependencySource>(), package.Definition.CapabilityRequirements, Array.Empty<CharacterDiagnostic>()));
        Assert.True(assembly.IsValid, string.Join("; ", assembly.Diagnostics));
        return (new() { ["manifest.json"] = assembly.ManifestBytes, ["character.runtime.json"] = assembly.RuntimeBytes,
            ["poses.bin"] = assembly.PoseBytes, ["client.bindings"] = assembly.BindingBytes },
            new(package.Metadata.PackageId, package.Metadata.Version, assembly.CookedContentHash, assembly.PackageHash));
    }

    static MatchContentCatalog Local()
    {
        var fixture = Fixture();
        var local = AdmittedFighter.TestFixture(fixture.Files, fixture.Requirement);
        var builtins = Bundled();
        var entries = builtins.Entries.Select(e => new AdmittedFighter(
            CookedCharacterPackageLoader.LoadDirectory(Path.Combine(Root, "content-cooked", e.Identity.PackageId),
                new(e.Identity.PackageId, e.Identity.Version, e.Identity.CookedContentHash, e.Identity.PackageHash)), e.LegacySelector,
                ShippedRoster.ReadApplication())).Append(local).ToArray();
        var result = new MatchContentCatalogBuilder().Build(entries, MatchContentPolicy.LocalPlay);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics));
        return result.Catalog!;
    }

    [Fact]
    public void SelectorFreeFighterAndSalamanderResolveIndependently()
    {
        var catalog = Local();
        var local = catalog.ResolvePackage("local.tumbleweed")!;
        var salamander = catalog.Resolve(CharacterClass.Salamander)!;
        Assert.Null(local.LegacySelector);
        Assert.Equal(CharacterClass.None, local.Definition.Class);
        Assert.Same(local, catalog.Resolve(local.Handle));
        Assert.Same(salamander, catalog.Resolve(salamander.Handle));
        Assert.NotEqual(local.Handle, salamander.Handle);
    }

    [Fact]
    public void AdmissionRejectsChangedHashAndStandardRoomRejectsLocal()
    {
        var fixture = Fixture();
        Assert.Throws<ArgumentException>(() => AdmittedFighter.TestFixture(fixture.Files,
            fixture.Requirement with { PackageHash = new string('0',64) }));
        var local = AdmittedFighter.TestFixture(fixture.Files, fixture.Requirement);
        Assert.False(new MatchContentCatalogBuilder().Build(new[] { local }, MatchContentPolicy.StandardRoom).IsValid);
        Assert.True(new MatchContentCatalogBuilder().Build(new[] { local }, MatchContentPolicy.LocalPlay).IsValid);
        Assert.True(new MatchContentCatalogBuilder().Build(Array.Empty<AdmittedFighter>(), MatchContentPolicy.LocalPlay).IsValid);
    }

    [Fact]
    public void Schema3RoundTripsEveryHandleWithoutSelectors()
    {
        var catalog = Local();
        var map = MatchContentHandleMap.CaptureLocal(catalog, new("slop_pit", new string('d',64)));
        var json = MatchContentHandleMapCodec.Serialize(map);
        Assert.DoesNotContain("selector", json);
        Assert.True(MatchContentHandleMapCodec.TryParse(json, out var read));
        Assert.Equal(json, MatchContentHandleMapCodec.Serialize(read!));
        foreach (var entry in catalog.Entries) Assert.Equal(entry.Identity, read!.Resolve(entry.Handle)!.Identity);
    }

    [Fact]
    public void ReplayContentMapRebuildsCompleteSetAndPreservesOldFallback()
    {
        var catalog = Local();
        var content = ReplayWorldTests.Content(catalog);
        var arena = content.LoadArena("slop_pit")!;
        var picked = new[] { catalog.ResolvePackage("local.tumbleweed")!, catalog.ResolvePackage("salamander")! };
        var roster = picked.Select((e,i) => new ReplayParticipant((ulong)i+1,"cpu",i.ToString(),e.Identity,"base","","",e.DisplayName)).ToArray();
        var header = new ReplayHeader("local",arena.Definition.Name,new("stock","off",3,false),1,roster,
            ReplayBuildIdentity.Current("test","pending"),arena.Digest).WithLocalContent(catalog);
        using var stream = new MemoryStream();
        var sim = new ServerSimulation(arena.Definition, MatchFormat.Stock.CreateRule(3));
        foreach (var pair in picked.Select((entry,i) => (entry,id:(ulong)i+1)))
            sim.RegisterEntity(pair.id,pair.entry.Definition,new CharacterState { EntityId = pair.id },pair.entry.BakedAnimation);
        var capture = new ReplayCapture(header,catalog,arena.Definition);
        sim.Tick(new Dictionary<ulong,InputState>());
        var packet = new WorldFramePacket(MatchState.Playing,sim.CaptureSnapshot(),Array.Empty<RelayedInput>(),
            Array.Empty<TimelinePresentationEvent>(),Array.Empty<SpellResolver.HitResult>());
        var tick = capture.Capture(sim,packet);
        Assert.Equal(ReplayCaptureStatus.Accepted,tick.Status);
        using (var writer = new ReplayWriter(stream,header))
        {
            writer.Append(tick.Capture!);
            writer.Complete(new(ReplayEndReason.Cancelled,"test",null));
        }
        if (Environment.GetEnvironmentVariable("SLOPARENA_REPLAY_COMPAT_OUTPUT") is { } output)
            File.WriteAllBytes(output,stream.ToArray());
        var reopened = ReplayReader.Open(new MemoryStream(stream.ToArray()));
        Assert.Null(reopened.Failure);
        Assert.Null(reopened.Reader!.ReadView(1).Failure);
        var read = reopened.Reader!.Header;
        Assert.Equal(catalog.Entries.Count, read.ContentMap!.Entries.Count);
        var environment = ReplayExactEnvironment.For(content,arena,read);
        Assert.Null(ReplayExact.Check(read,environment));
        Assert.NotNull(environment.Catalog.ResolvePackage("bonk"));
        Assert.Equal(catalog.Entries.Count,environment.Catalog.Entries.Count);
        var legacyContent = ReplayWorldTests.Content(Bundled());
        var legacy = ReplayExactEnvironment.For(legacyContent,arena);
        var oldHeader = header with { ContentMap = null, Build = header.Build with { ContentHash = legacy.MatchContentHash }, Roster = roster.Select(p => p with { Package = legacyContent.Catalog.ResolvePackage("salamander")!.Identity }).ToArray() };
        var oldRead = ReplayMetadata.ReadReplayHeader(ReplayMetadata.Write(oldHeader));
        Assert.Null(oldRead.ContentMap);
        Assert.DoesNotContain(ReplayBinary.Fields(ReplayMetadata.Write(oldHeader)), f => f.Key == 9);
        Assert.Null(ReplayExact.Check(oldRead,ReplayExactEnvironment.For(legacyContent,arena,oldRead)));
        var oldTable = new ReplayFields.Table<ReplayHeader>(() => oldHeader, ReplayMetadata.ReplayHeaderTable.Fields.Where(f => f.Tag != 9).ToArray());
        Assert.Null(oldTable.Read(ReplayMetadata.Write(header)).ContentMap);
    }
}
