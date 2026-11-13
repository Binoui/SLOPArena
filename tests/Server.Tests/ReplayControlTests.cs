using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using SlopArena.Server;
using SlopArena.Shared;
using SlopArena.Shared.Replays;
using Xunit;

namespace SlopArena.Server.Tests;

public sealed class ReplayControlTests
{
    sealed class Rig : IDisposable
    {
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "replay-control-" + Guid.NewGuid());
        public readonly MatchReplayStore? Store;
        public readonly HttpClient Http;
        readonly MatchControlServer control;
        readonly MultiMatchOrchestrator orchestrator;
        public Rig(bool enabled = true, string? key = "secret")
        {
            if (enabled) Store = new(Dir);
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            int number = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            orchestrator = new(new ServerConfig { Port = number, MaxConcurrentMatches = 1 }, replays: Store);
            control = new(orchestrator, number, "slop_pit", key);
            control.Start();
            Http = new() { BaseAddress = new Uri($"http://127.0.0.1:{number}") };
        }
        public void Auth() => Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");
        public string File(string id, bool part = false, int age = 0)
        {
            string day = Path.Combine(Dir, "2026-11-02"); Directory.CreateDirectory(day);
            string path = Path.Combine(day, id + ".slopreplay" + (part ? ".part" : ""));
            System.IO.File.WriteAllBytes(path, new byte[] { 83, 76, 79, 80, 82, 69, 80, 0, 42 });
            System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-age));
            return path;
        }
        public void Dispose()
        {
            Http.Dispose(); control.Dispose(); orchestrator.Shutdown();
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
        }
    }

    [Theory]
    [InlineData("secret")]
    [InlineData(null)]
    public async Task BothRoutesAlwaysRequireKey(string? key)
    {
        using var r = new Rig(key: key);
        foreach (var route in new[] { "/match/replays", "/match/replays/" + Guid.NewGuid() })
        {
            using var response = await r.Http.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
    }

    [Fact]
    public async Task ListsNewestCappedPrefersCompleteAndStreamsExactBytes()
    {
        using var r = new Rig(); r.Auth();
        for (int i = 0; i < 2002; i++) r.File(Guid.NewGuid().ToString(), age: i + 10);
        string newest = Guid.NewGuid().ToString(), partial = Guid.NewGuid().ToString();
        string completePath = r.File(newest);
        r.File(newest, part: true);
        string partPath = r.File(partial, part: true, age: 2);
        r.File("not-a-guid");
        string impostor = Guid.NewGuid().ToString();
        r.File(impostor + ".extra");
        using var list = await r.Http.GetAsync("/match/replays");
        Assert.True(list.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var entries = json.RootElement.GetProperty("replays").EnumerateArray().ToArray();
        Assert.Equal(2000, entries.Length);
        Assert.Equal(newest, entries[0].GetProperty("matchId").GetString());
        Assert.True(entries[0].GetProperty("complete").GetBoolean());
        Assert.False(entries[1].GetProperty("complete").GetBoolean());
        Assert.True(entries[0].GetProperty("modifiedAtUtc").GetDateTime().Kind == DateTimeKind.Utc);
        Assert.Equal(entries.OrderByDescending(e => e.GetProperty("modifiedAtUtc").GetDateTime()).Select(e => e.GetProperty("matchId").GetString()),
            entries.Select(e => e.GetProperty("matchId").GetString()));
        foreach (var (id, path) in new[] { (newest, completePath), (partial, partPath) })
        {
            using var response = await r.Http.GetAsync("/match/replays/" + id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(new FileInfo(path).Length, response.Content.Headers.ContentLength);
            Assert.Equal(await System.IO.File.ReadAllBytesAsync(path), await response.Content.ReadAsByteArrayAsync());
        }
        foreach (var id in new[] { Guid.NewGuid().ToString(), impostor, "not-a-guid", newest.Replace("-", ""), newest + "%2f..%2fsecret", "%2e%2e%2fsecret" })
            Assert.Equal(HttpStatusCode.NotFound, (await r.Http.GetAsync("/match/replays/" + id)).StatusCode);
        using (var file = System.IO.File.OpenWrite(completePath)) file.SetLength(64_000_001);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await r.Http.GetAsync("/match/replays/" + newest)).StatusCode);
    }

    [Fact]
    public async Task ActiveWriterIsExcludedAndDownloadConflicts()
    {
        using var r = new Rig(); r.Auth();
        var entries = new[] { BuiltInContentResolver.ResolvePackage("fightguy"), BuiltInContentResolver.ResolvePackage("manki") };
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data/arenas/slop_pit.arena"))!.Value;
        string id = Guid.NewGuid().ToString();
        var header = new ReplayHeader(id, arena.Name, new("stock", "off", 3, false), 1,
            entries.Select((e, i) => new ReplayParticipant((ulong)i + 1, "cpu", i.ToString(), e.Identity, "base", "", "")).ToArray(),
            ReplayBuildIdentity.Current("test", "test"));
        Assert.True(new MatchContentCatalogProvider().TryBuild(out var catalog, out _, out _));
        var recording = r.Store!.Start(header, catalog!, arena, new MatchDiagnostics(id, arena.Name, "test"), DateTimeOffset.UtcNow);
        try
        {
            using var response = await r.Http.GetAsync("/match/replays");
            Assert.Equal("{\"replays\":[]}", await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict, (await r.Http.GetAsync("/match/replays/" + id)).StatusCode);
        }
        finally
        {
            recording.Complete(new(ReplayEndReason.Cancelled, "test", null));
            Assert.True(r.Store.WaitForWriters(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task DisabledReturnsEmptyAndNotFound()
    {
        using var r = new Rig(enabled: false); r.Auth();
        Assert.Equal("{\"replays\":[]}", await r.Http.GetStringAsync("/match/replays"));
        Assert.Equal(HttpStatusCode.NotFound, (await r.Http.GetAsync("/match/replays/" + Guid.NewGuid())).StatusCode);
    }
}
