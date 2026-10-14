using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class ArcadeStageAdmissionTests
{
    [Fact]
    public void ArcadeCourtIsDiscoverableButCannotBeAssignedToPvP()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data", "arenas"));
        var court = ArenaRegistry.Get("arcade_ball_lab");
        Assert.True(court.HasValue);
        Assert.Equal(ArenaPurpose.Arcade, court.Value.Purpose);
        Assert.Null(ArenaRegistry.GetPvP("arcade_ball_lab"));
        var orchestrator = new MultiMatchOrchestrator(new ServerConfig());
        try
        {
            bool admitted = orchestrator.TryAssignMatch("arcade-admission-check", "arcade_ball_lab",
                new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
                3, null, null, out int port, out var content, out var hash, out string? error);
            Assert.False(admitted);
            Assert.Equal(-1, port);
            Assert.Null(content); Assert.Null(hash);
            Assert.Contains("arcade_ball_lab", error);
            Assert.Equal(0, orchestrator.CurrentMatchCount);
        }
        finally { orchestrator.Shutdown(); }
    }
}
