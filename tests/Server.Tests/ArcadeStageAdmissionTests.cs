using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class ArcadeStageAdmissionTests
{
    [Theory]
    [InlineData("arcade_ball_lab")]
    [InlineData("slopball")]
    [InlineData("fightguy_sanctuary")]
    [InlineData("fightguy_pier")]
    public void ArcadeCourtIsDiscoverableButCannotBeAssignedToPvP(string stage)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data", "arenas"));
        var court = ArenaRegistry.Get(stage);
        Assert.True(court.HasValue);
        Assert.Equal(ArenaPurpose.Arcade, court.Value.Purpose);
        Assert.Null(ArenaRegistry.GetPvP(stage));
        var orchestrator = new MultiMatchOrchestrator(new ServerConfig());
        try
        {
            bool admitted = orchestrator.TryAssignMatch("arcade-admission-check", stage,
                new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
                3, null, null, out int port, out var content, out var hash, out string? error);
            Assert.False(admitted);
            Assert.Equal(-1, port);
            Assert.Null(content); Assert.Null(hash);
            Assert.Contains(stage, error);
            Assert.Equal(0, orchestrator.CurrentMatchCount);
        }
        finally { orchestrator.Shutdown(); }
    }

    /// <summary>ADR-0040: a format admits only its own arena; co-op Slopball takes the court.</summary>
    [Theory]
    [InlineData("slopball", MatchFormat.SlopballCoop, true)]
    [InlineData("slop_pit", MatchFormat.SlopballCoop, false)]
    [InlineData("fightguy_pier", MatchFormat.SlopballCoop, false)]
    [InlineData("arcade_ball_lab", MatchFormat.SlopballCoop, false)]
    [InlineData("slopball", MatchFormat.Stock, false)]
    public void FormatsAdmitOnlyTheirOwnArena(string stage, MatchFormat format, bool arenaAdmitted)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data", "arenas"));
        var orchestrator = new MultiMatchOrchestrator(new ServerConfig());
        try
        {
            orchestrator.TryAssignMatch("format-admission-check", stage,
                new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
                3, null, null, out _, out _, out _, out string? error, format);
            // Past the arena check this test host has no cooked roster, so content is what stops it.
            Assert.Equal(!arenaAdmitted, error?.Contains($"unavailable for {format.ToWire()}") == true);
            Assert.Equal(0, orchestrator.CurrentMatchCount);
        }
        finally { orchestrator.Shutdown(); }
    }
}
