using System.Net;
using System.Text;
using System.Text.Json;
using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class GameServerRegistrationTests
{
    private const ulong TestHostSteamId = 76561198000000001;
    private static ServerConfig TestConfig(bool vps = false) => new()
    {
        DeploymentProfile = vps ? "vps" : "development",
        HostId = vps ? Guid.Parse("11111111-1111-1111-1111-111111111111") : null,
        RegistrationKey = vps ? "registration-secret-0123456789abcdef" : null,
        MatchControlKey = vps ? "control-secret-0123456789abcdef01234567" : null,
        PublicIp = "127.0.0.1",
        Port = 9876,
        MaxConcurrentMatches = 5,
        MasterServerUrl = "http://master.test"
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<(string Path, HttpMethod Method, string? Token)> Requests { get; } = new();
        public List<(string Path, string Body)> Bodies { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (_gate)
            {
                Requests.Add((request.RequestUri!.AbsolutePath, request.Method, request.Headers.Authorization?.Parameter));
                Bodies.Add((request.RequestUri.AbsolutePath, request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult() ?? ""));
            }
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Registered(Guid id, string token = "api-token") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { serverId = id, apiToken = token }), Encoding.UTF8, "application/json")
    };

    private sealed class TestClock : TimeProvider
    {
        private long _timestamp = 1;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
    }

    [Theory]
    [InlineData("server-error")]
    [InlineData("connection-failure")]
    [InlineData("timeout")]
    public async Task ReadinessExpiresAfterHeartbeatOutageAndRecoversWithoutLosingSession(string failure)
    {
        using var reservation = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var config = TestConfig(vps: true);
        config.Port = port;
        var id = Guid.NewGuid();
        var clock = new TestClock();
        var failedHeartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reportedToken = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restored = 0;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/servers/register") return Registered(id);
            if (request.RequestUri.AbsolutePath.EndsWith("/heartbeat", StringComparison.Ordinal))
            {
                if (Volatile.Read(ref restored) == 0)
                {
                    failedHeartbeat.TrySetResult();
                    return failure switch
                    {
                        "connection-failure" => throw new HttpRequestException("unavailable"),
                        "timeout" => throw new TaskCanceledException("timed out"),
                        _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (request.RequestUri.AbsolutePath == "/match/result")
                reportedToken.TrySetResult(request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var orchestrator = new MultiMatchOrchestrator(config);
        var registration = new GameServerRegistration(config, orchestrator, handler, clock, () => TestHostSteamId);
        using var control = new MatchControlServer(orchestrator, port, "slop_pit",
            isReady: () => registration.HasFreshRegistration);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        control.Start();
        var run = registration.RunAsync(cts.Token, heartbeatInterval: TimeSpan.FromMilliseconds(30));
        try
        {
            Assert.True(SpinWait.SpinUntil(() => registration.HasFreshRegistration, 3000));
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/ready")).StatusCode);
            await failedHeartbeat.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/ready")).StatusCode);

            clock.Advance(TimeSpan.FromSeconds(16));
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("/ready")).StatusCode);
            await registration.ReportMatchResultAsync(Guid.NewGuid(), 1);
            Assert.Equal("api-token", await reportedToken.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.True(registration.IsRegistered);
            Assert.Equal(id, registration.ServerId);

            Volatile.Write(ref restored, 1);
            Assert.True(SpinWait.SpinUntil(() => registration.HasFreshRegistration, 3000));
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/ready")).StatusCode);
        }
        finally
        {
            cts.Cancel();
            await run;
            control.Stop();
            orchestrator.Shutdown();
        }
    }

    [Fact]
    public async Task MasterUnavailableAtStartup_EventuallyRegistersAndDeregisters()
    {
        var id = Guid.NewGuid();
        var registrations = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/servers/register")
            {
                if (++registrations == 1) throw new HttpRequestException("unavailable");
                ready.TrySetResult();
                return Registered(id);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var config = TestConfig(vps: true);
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler,
            getSteamId: () => TestHostSteamId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = registration.RunAsync(cts.Token, initialRetryDelay: TimeSpan.FromMilliseconds(10));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(SpinWait.SpinUntil(() => registration.IsRegistered, 3000));
        cts.Cancel();
        await run;

        Assert.Equal(2, registrations);
        Assert.Equal(config.RegistrationKey, handler.Requests[0].Token);
        Assert.Equal($"/servers/{id}", Assert.Single(handler.Requests.Where(x => x.Method == HttpMethod.Delete)).Path);
        Assert.False(registration.IsRegistered);
    }

    [Fact]
    public async Task MasterLosesRegistration_HeartbeatReRegistersWithoutRestart()
    {
        var id = Guid.NewGuid();
        var registrations = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/servers/register")
            {
                if (++registrations == 2) ready.TrySetResult();
                return Registered(id, $"api-token-{registrations}");
            }
            if (path.EndsWith("/heartbeat", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var config = TestConfig(vps: true);
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler,
            getSteamId: () => TestHostSteamId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = registration.RunAsync(cts.Token, heartbeatInterval: TimeSpan.FromMilliseconds(30));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(SpinWait.SpinUntil(() => registration.IsRegistered && registration.ServerId == id, 3000));
        cts.Cancel();
        await run;

        Assert.Equal(2, registrations);
        Assert.Equal("api-token-1", Assert.Single(handler.Requests.Where(x => x.Path.EndsWith("/heartbeat"))).Token);
        Assert.Equal("api-token-2", Assert.Single(handler.Requests.Where(x => x.Method == HttpMethod.Delete)).Token);
    }

    [Fact]
    public async Task TemporaryHeartbeatFailure_KeepsApprovedRegistrationAndResultToken()
    {
        var id = Guid.NewGuid();
        var heartbeats = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/servers/register") return Registered(id);
            if (path.EndsWith("/heartbeat", StringComparison.Ordinal))
            {
                if (++heartbeats == 2) ready.TrySetResult();
                return new HttpResponseMessage(heartbeats == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var config = TestConfig(vps: true);
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler,
            getSteamId: () => TestHostSteamId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = registration.RunAsync(cts.Token, heartbeatInterval: TimeSpan.FromMilliseconds(30));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await registration.ReportMatchResultAsync(Guid.NewGuid(), 1);
        cts.Cancel();
        await run;

        Assert.Single(handler.Requests.Where(x => x.Path == "/servers/register"));
        Assert.Equal("api-token", Assert.Single(handler.Requests.Where(x => x.Path == "/match/result")).Token);
        Assert.Equal(2, heartbeats);
    }

    [Fact]
    public async Task OnlyOneRegistrationLoopCanRun()
    {
        var id = Guid.NewGuid();
        var handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath == "/servers/register"
                ? Registered(id) : new HttpResponseMessage(HttpStatusCode.OK));
        var config = TestConfig();
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var run = registration.RunAsync(cts.Token);
        Assert.True(SpinWait.SpinUntil(() => registration.IsRegistered, 3000));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.RunAsync(cts.Token));
        cts.Cancel();
        await run;
        Assert.Single(handler.Requests.Where(x => x.Path == "/servers/register"));
    }

    [Fact]
    public async Task VpsIdentityRotationReRegistersWithSameProcessInstance()
    {
        var serverId = Guid.NewGuid();
        long currentSteamId = (long)TestHostSteamId;
        int registrationCount = 0;
        int heartbeatCount = 0;
        var secondRegistration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/servers/register")
            {
                int count = Interlocked.Increment(ref registrationCount);
                if (count == 2) secondRegistration.TrySetResult();
                return Registered(serverId, $"api-token-{count}");
            }
            if (path.EndsWith("/heartbeat", StringComparison.Ordinal) &&
                Interlocked.Increment(ref heartbeatCount) == 1)
            {
                Interlocked.Exchange(ref currentSteamId, (long)TestHostSteamId + 1);
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var config = TestConfig(vps: true);
        var orchestrator = new MultiMatchOrchestrator(config);
        var registration = new GameServerRegistration(config, orchestrator, handler,
            getSteamId: () => (ulong)Interlocked.Read(ref currentSteamId));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = registration.RunAsync(cts.Token, heartbeatInterval: TimeSpan.FromMilliseconds(15));
        await secondRegistration.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(SpinWait.SpinUntil(() => registration.HasFreshRegistration, 3000));
        cts.Cancel();
        await run;

        Assert.NotEqual(Guid.Empty, registration.InstanceId);
        Assert.NotNull(orchestrator.CatalogHash);
        Assert.Equal(64, orchestrator.CatalogHash!.Length);
        Assert.All(orchestrator.CatalogHash, c => Assert.True(c is >= '0' and <= '9' or >= 'a' and <= 'f'));
        var registerBodies = handler.Bodies.Where(x => x.Path == "/servers/register").Select(x => x.Body).ToArray();
        Assert.Equal(2, registerBodies.Length);
        using var firstRegistration = JsonDocument.Parse(registerBodies[0]);
        using var secondRegistrationBody = JsonDocument.Parse(registerBodies[1]);
        Assert.Equal(TestHostSteamId.ToString(), firstRegistration.RootElement.GetProperty("steamId").GetString());
        Assert.Equal((TestHostSteamId + 1).ToString(), secondRegistrationBody.RootElement.GetProperty("steamId").GetString());
        foreach (var registrationBody in new[] { firstRegistration, secondRegistrationBody })
        {
            Assert.Equal(registration.InstanceId, registrationBody.RootElement.GetProperty("instanceId").GetGuid());
            Assert.Equal(SteamMatchDescriptor.CurrentProtocolVersion, registrationBody.RootElement.GetProperty("protocolVersion").GetInt32());
            Assert.Equal(orchestrator.CatalogHash, registrationBody.RootElement.GetProperty("catalogHash").GetString());
        }
        using var heartbeatBody = JsonDocument.Parse(handler.Bodies.First(x => x.Path.EndsWith("/heartbeat", StringComparison.Ordinal)).Body);
        Assert.Equal(registration.InstanceId, heartbeatBody.RootElement.GetProperty("instanceId").GetGuid());
        Assert.Equal(TestHostSteamId.ToString(), heartbeatBody.RootElement.GetProperty("steamId").GetString());
        Assert.Equal(SteamMatchDescriptor.CurrentProtocolVersion, heartbeatBody.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(orchestrator.CatalogHash, heartbeatBody.RootElement.GetProperty("catalogHash").GetString());
    }

    [Fact]
    public async Task DevelopmentRegistrationAdvertisesCatalogHashWithoutSteamIdentity()
    {
        var serverId = Guid.NewGuid();
        var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/servers/register")
                return Registered(serverId);
            if (request.RequestUri.AbsolutePath.EndsWith("/heartbeat", StringComparison.Ordinal))
                heartbeat.TrySetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var config = TestConfig();
        var orchestrator = new MultiMatchOrchestrator(config);
        string catalogHash = Assert.IsType<string>(orchestrator.CatalogHash);
        var registration = new GameServerRegistration(config, orchestrator, handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = registration.RunAsync(cts.Token, heartbeatInterval: TimeSpan.FromMilliseconds(15));
        try
        {
            await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            cts.Cancel();
            await run;
        }

        using var registerBody = JsonDocument.Parse(handler.Bodies.Single(x => x.Path == "/servers/register").Body);
        Assert.Equal(catalogHash, registerBody.RootElement.GetProperty("catalogHash").GetString());
        Assert.Equal(JsonValueKind.Null, registerBody.RootElement.GetProperty("steamId").ValueKind);
        Assert.Equal(JsonValueKind.Null, registerBody.RootElement.GetProperty("instanceId").ValueKind);
        Assert.Equal(0, registerBody.RootElement.GetProperty("protocolVersion").GetInt32());
        using var heartbeatBody = JsonDocument.Parse(handler.Bodies.Single(x => x.Path.EndsWith("/heartbeat", StringComparison.Ordinal)).Body);
        Assert.Equal(catalogHash, heartbeatBody.RootElement.GetProperty("catalogHash").GetString());
        Assert.Equal(JsonValueKind.Null, heartbeatBody.RootElement.GetProperty("steamId").ValueKind);
        Assert.Equal(JsonValueKind.Null, heartbeatBody.RootElement.GetProperty("protocolVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, heartbeatBody.RootElement.GetProperty("instanceId").ValueKind);
    }

    [Fact]
    public async Task PendingResultRetriesTransientFailureAndCancellationConflictIsTerminal()
    {
        var serverId = Guid.NewGuid();
        int resultRequests = 0;
        int cancelRequests = 0;
        var handler = new StubHandler(request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/servers/register": return Registered(serverId);
                case "/match/result":
                    return new HttpResponseMessage(Interlocked.Increment(ref resultRequests) == 1
                        ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
                case "/match/cancel":
                    Interlocked.Increment(ref cancelRequests);
                    return new HttpResponseMessage(HttpStatusCode.Conflict);
                default: return new HttpResponseMessage(HttpStatusCode.OK);
            }
        });
        var config = TestConfig(vps: true);
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler,
            getSteamId: () => TestHostSteamId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = registration.RunAsync(cts.Token, heartbeatInterval: TimeSpan.FromMilliseconds(15));
        Assert.True(SpinWait.SpinUntil(() => registration.IsRegistered, 3000));

        await registration.ReportMatchResultAsync(Guid.NewGuid(), 22);
        registration.QueueMatchCancellation(Guid.NewGuid(), "absent");
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref resultRequests) == 2, 3000));
        await Task.Delay(100);
        Assert.Equal(2, resultRequests);
        Assert.Equal(1, cancelRequests);

        cts.Cancel();
        await run;
        Assert.Equal(2, handler.Requests.Count(x => x.Path == "/match/result"));
        Assert.Equal(1, handler.Requests.Count(x => x.Path == "/match/cancel"));
    }

    [Fact]
    public async Task InvalidApprovedHostCredential_FailsWithoutRetry()
    {
        var config = TestConfig(vps: true);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var registration = new GameServerRegistration(config, new MultiMatchOrchestrator(config), handler,
            getSteamId: () => TestHostSteamId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.RunAsync(cts.Token));
        Assert.Equal(config.RegistrationKey, Assert.Single(handler.Requests).Token);
        Assert.False(registration.IsRegistered);
    }

    [Fact]
    public void VpsProfile_RequiresDistinctProvisionedCredentials()
    {
        var config = TestConfig(vps: true);
        config.HostId = null;
        Assert.Throws<InvalidOperationException>(config.Validate);
        config.HostId = Guid.NewGuid();
        config.RegistrationKey = null;
        Assert.Throws<InvalidOperationException>(config.Validate);
        config.RegistrationKey = "registration-secret-0123456789abcdef";
        config.PublicIp = "http://bad-host";
        Assert.Throws<InvalidOperationException>(config.Validate);
        config.PublicIp = "game.example.test";
        config.MatchControlKey = config.RegistrationKey;
        Assert.Throws<InvalidOperationException>(config.Validate);
        config.MatchControlKey = "different-control-secret-0123456789abcdef";
        config.RegistrationKey = "invalid space secret-0123456789abcdef";
        Assert.Throws<InvalidOperationException>(config.Validate);
        config.RegistrationKey = "registration-secret-0123456789abcdef";
        config.Validate();
        config.DeploymentProfile = "";
        Assert.Throws<InvalidOperationException>(config.Validate);
    }
}
