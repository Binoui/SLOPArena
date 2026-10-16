using System.Text.Json;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

/// <summary>
/// Tests for <see cref="LobbyPayloadCodec"/>: the only testable seam of the
/// SignalR lobby client (issue #33). The codec maps the master server's
/// camelCase JSON payloads (System.Text.Json default for ASP.NET Core SignalR)
/// to the Shared DTOs. These pin the wire contract the Unity LobbyClient relies on.
/// </summary>
public class LobbyPayloadCodecTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    // ── Player ──

    [Fact]
    public void TryParsePlayer_HostWithNullSelection_ReturnsPlayer()
    {
        var el = Parse("""{"steamId":12345,"name":"Guest-12345","characterSelection":null,"lockedIn":false,"isHost":true}""");

        var p = LobbyPayloadCodec.TryParsePlayer(el);

        Assert.NotNull(p);
        Assert.Equal(12345, p!.SteamId);
        Assert.Equal("Guest-12345", p.Name);
        Assert.Null(p.CharacterSelection);
        Assert.False(p.LockedIn);
        Assert.True(p.IsHost);
    }

[Fact]
    public void TryParsePlayer_NonHostWithSelection_ReturnsPlayer()
    {
        var el = Parse("""{"steamId":99,"name":"Guest-99","characterSelection":"Manki","lockedIn":true,"isHost":false}""");

        var p = LobbyPayloadCodec.TryParsePlayer(el);

        Assert.NotNull(p);
        Assert.Equal(99, p!.SteamId);
        Assert.Equal("Manki", p.CharacterSelection);
        Assert.True(p.LockedIn);
        Assert.False(p.IsHost);
    }

    [Fact]
    public void TryParsePlayer_MissingCharacterSelection_TreatedAsNull()
    {
        var el = Parse("""{"steamId":1,"name":"Guest-1","isHost":true}""");

        var p = LobbyPayloadCodec.TryParsePlayer(el);

        Assert.NotNull(p);
        Assert.Null(p!.CharacterSelection);
        Assert.False(p.LockedIn);
        Assert.True(p.IsHost);
    }

    [Theory]
    [InlineData("""{"steamId":"x","name":"n","isHost":true}""")]            // steamId not a number
    [InlineData("""{"steamId":1,"name":5,"isHost":true}""")]                  // name not a string
    [InlineData("""{"steamId":1,"name":"n"}""")]                              // missing isHost
    [InlineData("""{"name":"n","isHost":true}""")]                            // missing steamId
    [InlineData("""[]""")]                                                     // not an object
    public void TryParsePlayer_Malformed_ReturnsNull(string json)
    {
        Assert.Null(LobbyPayloadCodec.TryParsePlayer(Parse(json)));
    }

    // ── Snapshot ──

    [Fact]
    public void TryParseSnapshot_TwoPlayers_PreservesOrder()
    {
        var json = """
        {"serverId":"33333333-3333-3333-3333-333333333333","players":[
            {"steamId":1,"name":"Host","characterSelection":null,"lockedIn":false,"isHost":true},
            {"steamId":2,"name":"Guest","characterSelection":null,"lockedIn":true,"isHost":false}
        ]}
        """;

        var snap = LobbyPayloadCodec.TryParseSnapshot(Parse(json));

        Assert.NotNull(snap);
        Assert.Equal(new System.Guid("33333333-3333-3333-3333-333333333333"), snap!.ServerId);
        Assert.Equal(2, snap.Players.Count);
        Assert.Equal("Host", snap.Players[0].Name);
        Assert.True(snap.Players[0].IsHost);
        Assert.False(snap.Players[0].LockedIn);
        Assert.Equal("Guest", snap.Players[1].Name);
        Assert.False(snap.Players[1].IsHost);
        Assert.True(snap.Players[1].LockedIn);
    }

    [Fact]
    public void TryParseSnapshot_EmptyPlayers_IsValid()
    {
        var json = """{"serverId":"33333333-3333-3333-3333-333333333333","players":[]}""";

        var snap = LobbyPayloadCodec.TryParseSnapshot(Parse(json));

        Assert.NotNull(snap);
        Assert.Empty(snap!.Players);
    }

    [Fact]
    public void TryParseSnapshot_BadGuid_ReturnsNull()
    {
        var json = """{"serverId":"not-a-guid","players":[]}""";

        Assert.Null(LobbyPayloadCodec.TryParseSnapshot(Parse(json)));
    }

    [Fact]
    public void TryParseSnapshot_MalformedPlayer_ReturnsNull()
    {
        var json = """{"serverId":"33333333-3333-3333-3333-333333333333","players":[{"steamId":1}]}""";

        Assert.Null(LobbyPayloadCodec.TryParseSnapshot(Parse(json)));
    }

    // ── MatchStarting (same shape as snapshot) ──

[Fact]
    public void TryParseMatchStarting_TwoPlayers_ReturnsConfig()
    {
        var json = """
        {"serverId":"11111111-1111-1111-1111-111111111111","players":[
            {"steamId":7,"name":"A","characterSelection":null,"lockedIn":false,"isHost":true},
            {"steamId":8,"name":"B","characterSelection":"FightGuy","lockedIn":true,"isHost":false}
        ]}
        """;

        var cfg = LobbyPayloadCodec.TryParseMatchStarting(Parse(json));

        Assert.NotNull(cfg);
        Assert.Equal(new System.Guid("11111111-1111-1111-1111-111111111111"), cfg!.ServerId);
        Assert.Equal(2, cfg.Players.Count);
        Assert.Equal("FightGuy", cfg.Players[1].CharacterSelection);
        Assert.True(cfg.Players[1].LockedIn);
    }

    [Fact]
    public void TryParseMatchStarting_Malformed_ReturnsNull()
    {
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarting(Parse("""{"players":[]}""")));
    }
    // ── MatchStarted requires one approved route and content ──


    [Fact]
    public void TryParseMatchStarted_Malformed_ReturnsNull()
    {
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(Parse("""{"players":[]}""")));
    }

    [Fact]
    public void TryParseMatchStarted_RejectsPushWithoutAnAdmittedTransport()
    {
        var json = """
            {"serverId":"22222222-2222-2222-2222-222222222222","arenaName":"slop_pit",
             "content":{"schemaVersion":1,"entries":[]},"players":[
               {"steamId":1,"name":"A","characterSelection":"Manki","lockedIn":true,"isHost":true,"entityId":1},
               {"steamId":2,"name":"B","characterSelection":"FightGuy","lockedIn":true,"isHost":false,"entityId":2}]}
            """;
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(Parse(json)));
    }
    // ── Player.entityId (issue #35) ──

    [Fact]
    public void TryParsePlayer_WithEntityId_ReturnsEntityId()
    {
        var el = Parse("""{"steamId":1,"name":"A","characterSelection":"Manki","lockedIn":true,"isHost":true,"entityId":3}""");

        var p = LobbyPayloadCodec.TryParsePlayer(el);

        Assert.NotNull(p);
        Assert.Equal(3, p!.EntityId);
    }

    [Fact]
    public void TryParsePlayer_MissingEntityId_DefaultsZero()
    {
        var el = Parse("""{"steamId":1,"name":"A","isHost":true}""");

        var p = LobbyPayloadCodec.TryParsePlayer(el);

        Assert.NotNull(p);
        Assert.Equal(0, p!.EntityId);
    }

    // ── MatchStarted matchPort + arenaName (issue #35) ──

    [Fact]
    public void TryParseMatchStarted_DevelopmentRoomRequiresMatchId()
    {
        var json = """
        {"serverId":"22222222-2222-2222-2222-222222222222","roomId":"33333333-3333-3333-3333-333333333333","serverAddress":"203.0.113.60","matchId":"44444444-4444-4444-4444-444444444444","matchPort":9877,"arenaName":"split","content":{"schemaVersion":1,"entries":[{"handle":1,"selector":"fightguy","identity":{"packageId":"fightguy","version":"0.0.0-dev","sourceHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","cookedContentHash":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","packageHash":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"},"displayName":"FightGuy"}]},"players":[
            {"steamId":1,"name":"A","characterSelection":"Manki","lockedIn":true,"isHost":true,"entityId":1},
            {"steamId":2,"name":"B","characterSelection":"FightGuy","lockedIn":true,"isHost":false,"entityId":2}
        ]}
        """;

        var cfg = LobbyPayloadCodec.TryParseMatchStarted(Parse(json));

        Assert.NotNull(cfg);
        Assert.Equal(System.Guid.Parse("44444444-4444-4444-4444-444444444444"), cfg!.MatchId);
        Assert.Equal(9877, cfg.MatchPort);
        Assert.Equal("split", cfg.ArenaName);
        Assert.Equal(System.Guid.Parse("33333333-3333-3333-3333-333333333333"), cfg.RoomId);
        Assert.Equal("203.0.113.60", cfg.ServerAddress);
        Assert.Equal(1, cfg.Players[0].EntityId);
        Assert.Equal(2, cfg.Players[1].EntityId);
        Assert.Equal("Manki", cfg.Players[0].CharacterSelection);
        Assert.Equal("FightGuy", cfg.Players[1].CharacterSelection);
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(Parse(
            json.Replace("\"matchId\":\"44444444-4444-4444-4444-444444444444\",", ""))));
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(Parse(
            json.Replace("\"matchId\":\"44444444-4444-4444-4444-444444444444\"",
                "\"matchId\":\"not-a-guid\""))));
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(Parse(
            json.Replace("\"serverAddress\":\"203.0.113.60\",", ""))));
    }

}
