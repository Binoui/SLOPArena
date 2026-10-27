using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class ArcadeStageAdmissionTests
{
    [Theory]
    [InlineData("manki_mayhem")]
    [InlineData("arcade_ball_lab")]
    [InlineData("slopball")]
    [InlineData("fightguy_sanctuary")]
    [InlineData("fightguy_pier")]
    [InlineData("wibou_mall")]
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
        var arena = ArenaRegistry.Get(stage);
        Assert.True(arena.HasValue);
        Assert.Equal(arenaAdmitted, format.Admits(arena.Value));
    }

    [Theory]
    [InlineData("slop_pit")]
    [InlineData("fightguy_pier")]
    [InlineData("arcade_ball_lab")]
    public void CoopAssignmentRejectsOtherArenasBeforeContentAdmission(string stage)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data", "arenas"));
        var orchestrator = new MultiMatchOrchestrator(new ServerConfig());
        try
        {
            Assert.False(orchestrator.TryAssignMatch("coop-arena-rejection", stage,
                new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
                3, null, null, out int port, out var content, out var hash, out string? error,
                MatchFormat.SlopballCoop));
            Assert.Equal($"Arena '{stage}' is unavailable for slopball-coop.", error);
            Assert.Equal(-1, port);
            Assert.Null(content);
            Assert.Null(hash);
            Assert.Equal(0, orchestrator.CurrentMatchCount);
        }
        finally { orchestrator.Shutdown(); }
    }

    [Theory]
    [InlineData("manki_mayhem", 2, FriendlyFireMode.Off, true)]
    [InlineData("manki_mayhem", 3, FriendlyFireMode.Shoves, true)]
    [InlineData("manki_mayhem", 4, FriendlyFireMode.On, true)]
    [InlineData("manki_mayhem", 1, FriendlyFireMode.Shoves, false)]
    [InlineData("manki_mayhem", 5, FriendlyFireMode.Shoves, false)]
    [InlineData("manki_mayhem", 2, (FriendlyFireMode)255, false)]
    [InlineData("slop_pit", 2, FriendlyFireMode.Shoves, false)]
    [InlineData("slopball", 2, FriendlyFireMode.Shoves, false)]
    public void MayhemAdmissionChecksArenaSizeAndRules(string stage, int count, FriendlyFireMode mode, bool admitted)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        var host = new MultiMatchOrchestrator(new ServerConfig { MaxConcurrentMatches = 0 });
        try
        {
            Assert.True(host.ContentReady);
            Assert.False(host.TryAssignMatch("mayhem-admission", stage,
                Enumerable.Range(1, count).Select(i => new MatchPlayer(100 + i, CharacterClass.Manki, i)).ToArray(),
                3, null, null, out _, out _, out _, out var error, MatchFormat.MankiMayhem, mode));
            Assert.Equal(admitted, error!.StartsWith("No capacity", StringComparison.Ordinal));
            Assert.Equal(0, host.CurrentMatchCount);
        }
        finally { host.Shutdown(); }
    }

    [Fact]
    public void CoopCourtPassesArenaAdmissionWithoutAllocatingAMatch()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data", "arenas"));
        var provider = new MatchContentCatalogProvider();
        bool contentReady = provider.TryBuild(out _, out _, out string? contentError);
        var orchestrator = new MultiMatchOrchestrator(new ServerConfig { MaxConcurrentMatches = 0 });
        try
        {
            Assert.Equal(contentReady, orchestrator.ContentReady);
            const string matchId = "coop-content-admission";
            Assert.False(orchestrator.TryAssignMatch(matchId, "slopball",
                new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
                3, null, null, out int port, out _, out _, out string? error, MatchFormat.SlopballCoop));
            Assert.Equal(contentError ?? $"No capacity or duplicate match ID for match {matchId}.", error);
            Assert.Equal(-1, port);
            Assert.Equal(0, orchestrator.CurrentMatchCount);
        }
        finally { orchestrator.Shutdown(); }
    }
}
