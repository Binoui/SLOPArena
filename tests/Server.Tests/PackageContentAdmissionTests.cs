using System.Net;
using System.Net.Sockets;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public sealed class PackageContentAdmissionTests
{
    static string Root => ShippedRoster.ReadApplication().CookedRoot[..^"content-cooked".Length];

    [Fact]
    public void OnlineGoldenUsesProviderArenaBindingAndOrchestratorHash()
    {
        var provider = new MatchContentCatalogProvider(Path.Combine(Root,"content-cooked"),Path.Combine(Root,"content-cooked/roster/manifest.json"));
        Assert.True(provider.TryBuild(out var catalog,out var characters,out var error),error);
        ArenaRegistry.LoadFromDirectory(Path.Combine(Root,"data/arenas"));
        Assert.True(ArenaRegistry.TryGetMatchArena("slop_pit",out _,out var arena));
        var map = characters!.WithArena(arena!);
        string fixture = Path.Combine(Root,"tests/Shared.Tests/Fixtures/MatchContent/schema2-origin-main");
        Assert.Equal(File.ReadAllText(fixture+".json"),MatchContentHandleMapCodec.Serialize(map));
        Assert.Equal(File.ReadAllText(fixture+".sha256"),SteamMatchDescriptor.HashContent(map));
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        reservation.Close();
        var host = new MultiMatchOrchestrator(new ServerConfig { Port=port,MaxConcurrentMatches=1 });
        try
        {
            Assert.True(host.TryAssignMatch(Guid.NewGuid().ToString("D"),"slop_pit",
                new[] { new MatchPlayer(1,"fightguy",1),new MatchPlayer(2,"manki",2) },
                3,null,host.CatalogHash,out _,out var assigned,out var hash,out error),error);
            Assert.Equal(File.ReadAllText(fixture+".json"),MatchContentHandleMapCodec.Serialize(assigned!));
            Assert.Equal(File.ReadAllText(fixture+".sha256"),hash);
            Assert.Equal(catalog!.Entries.Count,assigned!.Entries.Count);
        }
        finally { host.Shutdown(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ServerRefusesEmptyOrTruncatedSuppliedRosters(int count)
    {
        var shipped = ShippedRoster.ReadApplication();
        string path=Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,BuiltInRosterManifestCodec.Serialize(new(1,shipped.Manifest.Entries.Take(count).ToArray())));
            Assert.False(new MatchContentCatalogProvider(shipped.CookedRoot,path).TryBuild(out var catalog,out var map,out var error));
            Assert.Null(catalog); Assert.Null(map);
            Assert.Contains("complete application shipped roster",error);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ServerRefusesAnUnloadableEntryFromTheCompleteRoster()
    {
        string root=Path.Combine(Path.GetTempPath(),"sloparena-missing-"+Guid.NewGuid().ToString("N"));
        var shipped=ShippedRoster.ReadApplication();
        Assert.False(new MatchContentCatalogProvider(root,Path.Combine(shipped.CookedRoot,"roster/manifest.json"))
            .TryBuild(out var catalog,out var map,out var error));
        Assert.Null(catalog); Assert.Null(map);
        Assert.Contains("catalog.package.missing",error);
    }
    [Fact]
    public void DefaultProviderIgnoresWorkingDirectoryAndReadsOneApplicationContentRoot()
    {
        string previous=Directory.GetCurrentDirectory();
        string temp=Path.Combine(Path.GetTempPath(),"sloparena-cwd-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Directory.SetCurrentDirectory(temp);
            Assert.True(new MatchContentCatalogProvider().TryBuild(out var catalog,out var map,out var error),error);
            var shipped=ShippedRoster.ReadApplication();
            Assert.Equal(shipped.Manifest.Entries.Count,catalog!.Entries.Count);
            foreach(var entry in catalog.Entries)
                Assert.Equal(shipped.Manifest.ResolvePackage(entry.Identity.PackageId)!.Requirement.PackageHash,entry.Identity.PackageHash);
            Assert.Equal(catalog.Entries.Count,map!.Entries.Count);
        }
        finally { Directory.SetCurrentDirectory(previous);Directory.Delete(temp,true); }
    }

}
