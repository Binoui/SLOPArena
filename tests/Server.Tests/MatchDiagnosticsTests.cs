using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using SlopArena.Server;
using Xunit;

namespace SlopArena.Server.Tests;

public class MatchDiagnosticsTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void RepeatingTransportFailurePreservesLifecycleEventsAndReportsAgainLater()
    {
        var clock = new Clock();
        var diagnostics = new MatchDiagnostics(Guid.NewGuid().ToString("D"), "slop_court", "steam-p2p", clock);
        diagnostics.Log(1, "GO!");
        for (uint tick = 2; tick < 400; tick++)
            diagnostics.ReportError(tick, "Gameplay send failed.");
        diagnostics.ReportError(400, "Gameplay receive failed.");
        var first = diagnostics.Snapshot();
        Assert.Equal(3, first.Events.Count);
        Assert.Equal("GO!", first.Events[0].Message);
        Assert.All(first.Events.Skip(1), e => Assert.Equal("error", e.Level));
        clock.Now += TimeSpan.FromSeconds(5);
        diagnostics.ReportError(401, "Gameplay send failed.");
        Assert.Equal(401u, diagnostics.Snapshot().Events[^1].Tick);
    }

    [Fact]
    public void RetentionKeepsActiveMatchesAndBoundsEndedMatchEvents()
    {
        var store = new MatchDiagnosticsStore();
        var activeId = Guid.NewGuid();
        var active = new MatchDiagnostics(activeId.ToString("D"), "slop_court", "steam-p2p");
        store.Add(active);
        for (int i = 0; i < MatchDiagnostics.EventLimit + 8; i++) active.Log((uint)i, $"event {i}");
        var snapshot = store.Get(activeId)!;
        Assert.Equal(256, snapshot.Events.Count);
        Assert.Equal(8, snapshot.DroppedEvents);
        Assert.Equal("event 8", snapshot.Events[0].Message);
        var oldId = Guid.NewGuid();
        var old = new MatchDiagnostics(oldId.ToString("D"), "slop_court", "steam-p2p");
        old.Complete(); store.Add(old);
        for (int i = 0; i < MatchDiagnosticsStore.CompletedLimit; i++)
        {
            var match = new MatchDiagnostics(Guid.NewGuid().ToString("D"), "slop_court", "steam-p2p");
            match.Complete(); store.Add(match);
        }
        Assert.Null(store.Get(oldId));
        Assert.NotNull(store.Get(activeId));
    }

    [Fact]
    public void MeasurementsKeepServerWorkSeparateFromConnectionHealth()
    {
        var diagnostics = new MatchDiagnostics(Guid.NewGuid().ToString("D"), "slop_court", "steam-p2p");
        diagnostics.RecordTick(10, 2, 1, false);
        diagnostics.RecordTick(11, 24, 200, true);
        diagnostics.RecordPeer(2, true, 80, .98f, -1);
        var snapshot = diagnostics.Snapshot();
        Assert.Equal(13d, snapshot.MeanTickMilliseconds);
        Assert.Equal(24d, snapshot.MaxTickMilliseconds);
        Assert.Equal(200d, snapshot.MaxScheduleDelayMilliseconds);
        Assert.Equal(1, snapshot.SlowTicks);
        Assert.Equal(1, snapshot.ScheduleResets);
        Assert.Equal(80, snapshot.Peers.Single().PingMilliseconds);
        Assert.Null(snapshot.Peers.Single().SendQuality);
        Assert.Null(snapshot.EndedAtUtc);
        diagnostics.RecordPeer(2, false);
        diagnostics.Complete();
        Assert.False(diagnostics.Snapshot().Peers.Single().Connected);
        Assert.NotNull(diagnostics.Snapshot().EndedAtUtc);
    }

    [Fact]
    public async Task PrivateEndpointReturnsOnlyRequestedMatchAndRequiresControlCredential()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start(); int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        var orchestrator = new MultiMatchOrchestrator(new ServerConfig { Port = port, MaxConcurrentMatches = 1 });
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var diagnostics = new MatchDiagnostics(id.ToString("D"), "slop_court", "steam-p2p");
        diagnostics.Log(3, "GO!"); diagnostics.Complete(); orchestrator.Diagnostics.Add(diagnostics);
        var otherDiagnostics = new MatchDiagnostics(other.ToString("D"), "slop_pit", "steam-p2p");
        otherDiagnostics.Log(1, "Other match secret-free event"); orchestrator.Diagnostics.Add(otherDiagnostics);
        const string key = "match-control-test-key-0123456789abcdef";
        using var control = new MatchControlServer(orchestrator, port, "slop_court", key);
        control.Start();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var route = $"/match/diagnostics/{id:D}";
        using var missing = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        using var wrong = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(id.ToString("D"), json.RootElement.GetProperty("matchId").GetString());
        Assert.Equal("GO!", json.RootElement.GetProperty("events")[0].GetProperty("message").GetString());
        Assert.DoesNotContain(other.ToString("D"), body);
        Assert.DoesNotContain(key, body);
        using var expired = await client.GetAsync($"/match/diagnostics/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
    }
}
