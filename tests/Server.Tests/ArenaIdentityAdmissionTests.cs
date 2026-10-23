using SlopArena.Server;
using SlopArena.Shared;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SlopArena.Server.Tests;

public sealed class ArenaIdentityAdmissionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static MatchPlayer[] Roster => new[] { new MatchPlayer(1, CharacterClass.FightGuy, 1), new MatchPlayer(2, CharacterClass.Manki, 2) };

    [Fact]
    public void UnknownDevelopmentArenaIsRejectedBeforeAllocation()
    {
        var orchestrator = new MultiMatchOrchestrator(new ServerConfig { Port = 29871, MaxConcurrentMatches = 1 });
        try
        {
            Assert.False(orchestrator.TryAssignMatch(Guid.NewGuid().ToString("D"), "missing-arena",
                new[] { new MatchPlayer(1, CharacterClass.FightGuy, 1), new MatchPlayer(2, CharacterClass.Manki, 2) },
                3, null, null, out int port, out _, out _, out string? error));
            Assert.Equal(-1, port);
            Assert.Contains("missing-arena", error);
            Assert.Equal(0, orchestrator.CurrentMatchCount);
        }
        finally { orchestrator.Shutdown(); }
    }

    [Fact]
    public void AssignmentPinsArenaAndNeverPollutesTheRegisteredCatalogHash()
    {
        ArenaRegistry.LoadFromDirectory(Path.Combine(Root, "data/arenas"));
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        reservation.Close();
        var orchestrator = new MultiMatchOrchestrator(new ServerConfig { Port = port, MaxConcurrentMatches = 1 });
        string? catalogHash = orchestrator.CatalogHash;
        try
        {
            Assert.False(orchestrator.TryAssignMatch(Guid.NewGuid().ToString("D"), "slop_pit", Roster,
                3, null, new string('0', 64), out _, out _, out _, out var stale));
            Assert.Contains("catalog", stale);
            Assert.Equal(0, orchestrator.CurrentMatchCount);
            Assert.True(orchestrator.TryAssignMatch(Guid.NewGuid().ToString("D"), "slop_pit", Roster,
                3, null, catalogHash, out _, out var content, out var hash, out var error), error);
            Assert.NotNull(content!.Arena);
            Assert.Equal("slop_pit", content.Arena.Name);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, "data/arenas/slop_pit.arena"))))
                .ToLowerInvariant(), content.Arena.Digest);
            Assert.Equal(SteamMatchDescriptor.HashContent(content), hash);
            Assert.NotEqual(catalogHash, hash);
            Assert.Equal(catalogHash, orchestrator.CatalogHash);
            var oldMap = new MatchContentHandleMap(1, content.Entries);
            string originalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                MatchContentHandleMapCodec.Serialize(oldMap)))).ToLowerInvariant();
            Assert.Equal(originalHash, catalogHash);
            Assert.True(orchestrator.TryRefreshCatalogHash(out error), error);
            Assert.Equal(catalogHash, orchestrator.CatalogHash);
        }
        finally { orchestrator.Shutdown(); }
    }

    [Fact]
    public async Task SteamMatchUsesTheAssignedArenaAfterRegistryReplacementAndRejectsCatalogOnlyJoin()
    {
        ArenaRegistry.LoadFromDirectory(Path.Combine(Root, "data/arenas"));
        Assert.True(ArenaRegistry.TryGetMatchArena("slop_pit", out var arena, out var identity));
        Assert.True(new MatchContentCatalogProvider().TryBuild(out var catalog, out var characters, out var error), error);
        var content = characters!.WithArena(identity!);
        string contentHash = SteamMatchDescriptor.HashContent(content);
        var baseline = new TaskCompletionSource<WorldFramePacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var ended = new ManualResetEventSlim();
        var match = new MatchInstance(0, Guid.NewGuid().ToString("D"), arena, Roster, catalog!, _ => ended.Set(),
            admissionDeadlineUtc: DateTimeOffset.UtcNow.AddSeconds(60), contentHash: contentHash,
            contentHandleMap: content, steamSend: (_, _, frame, _) =>
            {
                if (frame[0] == SteamGameplayWire.Frame)
                    baseline.TrySetResult(WorldFramePacket.Deserialize(frame.AsSpan(1)));
            });
        try
        {
            ArenaRegistry.LoadFromDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            Assert.Null(ArenaRegistry.Get("slop_pit"));
            match.Start();
            Assert.False(match.TryBindSteamPlayer(1, 11, SteamMatchDescriptor.HashCatalog(content), out _, out _, out _));
            Assert.True(match.TryBindSteamPlayer(1, 11, contentHash, out _, out _, out _));
            Assert.True(match.TryBindSteamPlayer(2, 22, contentHash, out _, out _, out _));
            var frame = await baseline.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(MatchState.Waiting, frame.MatchState);
            Assert.Equal(2, frame.Inputs.Count);
            Assert.True(match.TryMarkSteamReady(11, new NetplayControlPacket(NetplayControlKind.Ready, 1, match.ServerTick, match.StartTick)));
        }
        finally
        {
            match.Stop();
            Assert.True(ended.Wait(TimeSpan.FromSeconds(3)));
            ArenaRegistry.LoadFromDirectory(Path.Combine(Root, "data/arenas"));
        }
    }
}
