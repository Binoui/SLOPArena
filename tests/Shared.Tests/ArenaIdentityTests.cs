using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

public sealed class ArenaIdentityTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void DevelopmentRequestPreservesTheRoomCatalogPin()
    {
        string json = $$"""
            {"matchId":"11111111-1111-1111-1111-111111111111","arenaName":"slop_pit",
             "catalogHash":"{{new string('a', 64)}}",
             "players":[{"steamId":1,"entityId":1,"characterClass":"FightGuy"},{"steamId":2,"entityId":2,"characterClass":"Manki"}]}
            """;
        using var document = JsonDocument.Parse(json);
        var request = MatchStartRequestCodec.TryParse(document.RootElement);
        Assert.NotNull(request);
        Assert.Equal(0, request.ProtocolVersion);
        Assert.Equal(new string('a', 64), request.CatalogHash);
        using var invalid = JsonDocument.Parse(json.Replace(new string('a', 64), "bad"));
        Assert.Null(MatchStartRequestCodec.TryParse(invalid.RootElement));
    }

    [Fact]
    public void DevelopmentMatchPushRejectsCatalogOnlyContent()
    {
        using var document = JsonDocument.Parse("""
            {"serverId":"22222222-2222-2222-2222-222222222222",
             "matchId":"11111111-1111-1111-1111-111111111111",
             "matchPort":9877,"arenaName":"slop_pit","players":[],
             "content":{"schemaVersion":1,"entries":[]}}
            """);
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(document.RootElement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothMatchRoutesRequireTheNamedArenaAndPinnedContent(bool steam)
    {
        var matchId = Guid.NewGuid();
        var map = new MatchContentHandleMap(2, Array.Empty<MatchContentHandleRecord>())
            .WithArena(new MatchArenaRecord("slop_pit", new string('a', 64)));
        using var content = JsonDocument.Parse(MatchContentHandleMapCodec.Serialize(map));
        var payload = new
        {
            serverId = Guid.NewGuid(), matchId, matchPort = steam ? 0 : 9877, arenaName = "slop_pit", players = Array.Empty<object>(),
            content = content.RootElement,
            descriptor = steam ? new
            {
                transport = "steam-p2p", matchId, serverSteamId = "90293421017699331", virtualPort = 0,
                protocolVersion = SteamMatchDescriptor.CurrentProtocolVersion,
                contentHash = SteamMatchDescriptor.HashContent(map), admissionExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(60)
            } : null
        };
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        using var valid = JsonDocument.Parse(json);
        Assert.NotNull(LobbyPayloadCodec.TryParseMatchStarted(valid.RootElement));
        using var wrongName = JsonDocument.Parse(json.Replace("\"arenaName\":\"slop_pit\"", "\"arenaName\":\"other\""));
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(wrongName.RootElement));
        string legacy = json.Replace(MatchContentHandleMapCodec.Serialize(map), "{\"schemaVersion\":1,\"entries\":[]}");
        using var unpinned = JsonDocument.Parse(legacy);
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(unpinned.RootElement));
    }

    [Fact]
    public void ArenaMapRoundTripsAndKeepsTheOriginalCatalogHashBytes()
    {
        const string originalCatalogBytes = "{\"schemaVersion\":1,\"entries\":[]}";
        var catalog = new MatchContentHandleMap(MatchContentHandleMap.CurrentSchemaVersion, Array.Empty<MatchContentHandleRecord>());
        var map = catalog.WithArena(new MatchArenaRecord("slop_pit", new string('a', 64)));
        var json = MatchContentHandleMapCodec.Serialize(map);
        Assert.True(MatchContentHandleMapCodec.TryParse(json, out var parsed));
        Assert.Equal(map.Arena, parsed!.Arena);
        Assert.Equal(json, MatchContentHandleMapCodec.Serialize(parsed));
        string oldHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalCatalogBytes))).ToLowerInvariant();
        Assert.Equal(oldHash, SteamMatchDescriptor.HashCatalog(catalog));
        Assert.Equal(oldHash, SteamMatchDescriptor.HashCatalog(map));
        Assert.NotEqual(oldHash, SteamMatchDescriptor.HashContent(map));
        Assert.NotEqual(SteamMatchDescriptor.HashContent(map), SteamMatchDescriptor.HashContent(
            map.WithArena(new MatchArenaRecord("slop_pit", new string('b', 64)))));
        Assert.NotEqual(SteamMatchDescriptor.HashContent(map), SteamMatchDescriptor.HashContent(
            map.WithArena(new MatchArenaRecord("other", new string('a', 64)))));
        Assert.Throws<ArgumentException>(() => SteamMatchDescriptor.HashContent(catalog));
        Assert.True(MatchContentHandleMapCodec.TryParse(originalCatalogBytes, out _));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"entries\":[],\"arena\":null}")]
    [InlineData("{\"schemaVersion\":2,\"entries\":[],\"arena\":{\"name\":\"slop_pit\",\"digest\":\"bad\"}}")]
    [InlineData("{\"schemaVersion\":2,\"entries\":[],\"arena\":{\"name\":\"slop_pit\",\"name\":\"other\",\"digest\":\"bad\"}}")]
    [InlineData("{\"schemaVersion\":2,\"entries\":[],\"arena\":{\"name\":\"slop_pit\",\"digest\":\"bad\",\"extra\":1}}")]
    public void ArenaMapRejectsMalformedIdentity(string json) => Assert.False(MatchContentHandleMapCodec.TryParse(json, out _));

    [Fact]
    public void EveryShippedArenaHashesTheSameBytesItParses()
    {
        var files = Directory.GetFiles(Path.Combine(Root, "data/arenas"), "*.arena");
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            Assert.True(MatchArenaRecord.TryLoad(file, Path.GetFileNameWithoutExtension(file), out var identity, out var arena, out var error), error);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant(), identity!.Digest);
            Assert.Equal(arena.Name, identity.Name);
            Assert.True(identity.TryLoadVerified(file, out var verified, out error), error);
            Assert.Equal(arena.KillHeight, verified.KillHeight);
            Assert.False(new MatchArenaRecord(identity.Name, new string('0', 64)).TryLoadVerified(file, out _, out error));
            Assert.Contains("digest", error);
            Assert.False(MatchArenaRecord.TryLoad(file, "different_name", out _, out _, out error));
            Assert.Contains("name", error);
        }
    }

    [Fact]
    public void DigestUsesRawBakedBytesEvenWhenParsingNormalizesThem()
    {
        // Plain v5 arenas accept trailing bytes. Serialization would silently discard them.
        string source = Path.Combine(Root, "data/arenas/slop_pit.arena");
        string path = Path.GetTempFileName();
        try
        {
            var bytes = File.ReadAllBytes(source).Concat(new byte[] { 0x42 }).ToArray();
            File.WriteAllBytes(path, bytes);
            Assert.True(MatchArenaRecord.TryLoad(path, "slop_pit", out var identity, out var arena, out var error), error);
            string serializedHash = Convert.ToHexString(SHA256.HashData(ArenaBinaryFormat.Serialize(arena))).ToLowerInvariant();
            Assert.NotEqual(serializedHash, identity!.Digest);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), identity.Digest);
        }
        finally { File.Delete(path); }
    }
}
