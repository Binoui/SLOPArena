using System.Net;
using System.Net.Sockets;
using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class MatchControlHealthTests
{
    [Fact]
    public async Task ListenerLivenessRemainsUpWhileRegistrationReadinessChanges()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var config = new ServerConfig { Port = port, MaxConcurrentMatches = 1 };
        var orchestrator = new MultiMatchOrchestrator(config);
        bool ready = false;
        using var control = new MatchControlServer(orchestrator, port, "slop_pit",
            controlKey: "control-secret-0123456789abcdef01234567", isReady: () => ready);
        control.Start();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("/ready")).StatusCode);
            ready = true;
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/ready")).StatusCode);
        }
        finally
        {
            control.Stop();
            orchestrator.Shutdown();
        }
    }

    [Fact]
    public void ShutdownRejectsNewMatchAssignmentBeforeAllocatingPort()
    {
        var orchestrator = new MultiMatchOrchestrator(new ServerConfig { Port = 19200, MaxConcurrentMatches = 1 });
        orchestrator.StopAcceptingMatches();

        var admitted = orchestrator.TryAssignMatch("not-started", "slop_pit",
            new[] { new MatchPlayer(1, CharacterClass.Manki, 1), new MatchPlayer(2, CharacterClass.FightGuy, 2) },
            3, null, null, out var port, out _, out _, out var error);

        Assert.False(admitted);
        Assert.Equal(-1, port);
        Assert.Contains("shutting down", error);
        Assert.Equal(0, orchestrator.CurrentMatchCount);
    }

}
