using System;
using System.Text.Json;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

public sealed class SteamMatchRoutingWireTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly Guid Match = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void JoinFrame_BindsMatchVersionAndContent_AndAckBindsEntity()
    {
        var descriptor = new SteamMatchDescriptor(Match, 90293421017699331UL, 0,
            SteamMatchDescriptor.CurrentProtocolVersion, Digest,
            DateTimeOffset.UtcNow.AddSeconds(60));
        var join = SteamGameplayWire.CreateJoin(descriptor);
        Assert.True(SteamGameplayWire.TryParseJoin(join, out var parsedMatch, out var parsedDigest));
        Assert.Equal(Match, parsedMatch);
        Assert.Equal(Digest, parsedDigest);
        join[37] = 2; // Previous protocol version.
        Assert.False(SteamGameplayWire.TryParseJoin(join, out _, out _));
        join[37] = (byte)SteamMatchDescriptor.CurrentProtocolVersion;
        join[39] = (byte)'z'; // Noncanonical content hash.
        Assert.False(SteamGameplayWire.TryParseJoin(join, out _, out _));
        Assert.False(SteamGameplayWire.TryParseJoin(join.AsSpan(0, join.Length - 1), out _, out _));

        var ack = SteamGameplayWire.CreateAck(4);
        Assert.True(SteamGameplayWire.TryParseAck(ack, out var entity));
        Assert.Equal(4UL, entity);
        ack[0] = SteamGameplayWire.Deny;
        Assert.False(SteamGameplayWire.TryParseAck(ack, out _));
    }

    [Fact]
    public void Descriptor_ParsesLosslessServerIdentityAndRejectsIncompatibleRoute()
    {
        string json = $$"""
            {"transport":"steam-p2p","matchId":"11111111-2222-3333-4444-555555555555","serverSteamId":"90293421017699331","virtualPort":0,"protocolVersion":{{SteamMatchDescriptor.CurrentProtocolVersion}},"contentHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","admissionExpiresAtUtc":"2026-09-26T12:00:00Z"}
            """;
        using var document = JsonDocument.Parse(json);
        Assert.True(SteamMatchDescriptor.TryParse(document.RootElement, out var descriptor));
        using var previousVersion = JsonDocument.Parse(json.Replace($"\"protocolVersion\":{SteamMatchDescriptor.CurrentProtocolVersion}", $"\"protocolVersion\":{SteamMatchDescriptor.CurrentProtocolVersion - 1}"));

        Assert.False(SteamMatchDescriptor.TryParse(previousVersion.RootElement, out _));
        using var wrongPort = JsonDocument.Parse(json.Replace("\"virtualPort\":0", "\"virtualPort\":1"));
        Assert.False(SteamMatchDescriptor.TryParse(wrongPort.RootElement, out _));
        using var numericIdentity = JsonDocument.Parse(json.Replace("\"90293421017699331\"", "90293421017699331"));
        Assert.False(SteamMatchDescriptor.TryParse(numericIdentity.RootElement, out _));
    }

    [Fact]
    public void SteamStart_RejectsDuplicateRosterOrMissingDeadline()
    {
        string json = $$"""
            {"matchId":"11111111-2222-3333-4444-555555555555","arenaName":"slop_pit","players":[{"steamId":76561198000000001,"characterClass":"Manki","entityId":1},{"steamId":76561198000000002,"characterClass":"Bonk","entityId":2}],"protocolVersion":{{SteamMatchDescriptor.CurrentProtocolVersion}},"virtualPort":0,"catalogHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","admissionExpiresAtUtc":"2026-09-26T12:00:00Z"}
            """;
        using var valid = JsonDocument.Parse(json);
        Assert.NotNull(MatchStartRequestCodec.TryParse(valid.RootElement));
        using var previousVersion = JsonDocument.Parse(json.Replace($"\"protocolVersion\":{SteamMatchDescriptor.CurrentProtocolVersion}", $"\"protocolVersion\":{SteamMatchDescriptor.CurrentProtocolVersion - 1}"));
        Assert.Null(MatchStartRequestCodec.TryParse(previousVersion.RootElement));
        using var duplicate = JsonDocument.Parse(json.Replace("76561198000000002", "76561198000000001"));
        Assert.Null(MatchStartRequestCodec.TryParse(duplicate.RootElement));
        using var noDeadline = JsonDocument.Parse(json.Replace(",\"admissionExpiresAtUtc\":\"2026-09-26T12:00:00Z\"", ""));
        Assert.Null(MatchStartRequestCodec.TryParse(noDeadline.RootElement));
        using var noCatalog = JsonDocument.Parse(json.Replace(
            ",\"catalogHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", ""));
        Assert.Null(MatchStartRequestCodec.TryParse(noCatalog.RootElement));
    }
    [Fact]
    public void MatchPush_RejectsContentChangedAfterDescriptorWasIssued()
    {
        var original = new MatchContentHandleMap(1, new[]
        {
            new MatchContentHandleRecord(new ContentHandle(1), CharacterClass.FightGuy,
                new MatchContentIdentity("fightguy", "0.0.0-dev",
                    new string('a', 64), new string('b', 64), new string('c', 64)), "FightGuy")
        });
        original = original.WithArena(new MatchArenaRecord("slop_pit", new string('d', 64)));
        var contentHash = SteamMatchDescriptor.HashContent(original);
        using var contentDocument = JsonDocument.Parse(MatchContentHandleMapCodec.Serialize(original));
        var payload = new
        {
            serverId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            matchId = Match,
            arenaName = "slop_pit",
            players = new[]
            {
                new { steamId = 76561198000000001L, name = "A", characterSelection = "FightGuy",
                    lockedIn = true, isHost = true, entityId = 1 },
                new { steamId = 76561198000000002L, name = "B", characterSelection = "FightGuy",
                    lockedIn = true, isHost = false, entityId = 2 }
            },
            content = contentDocument.RootElement,
            descriptor = new
            {
                transport = "steam-p2p",
                matchId = Match,
                serverSteamId = "90293421017699331",
                virtualPort = 0,
                protocolVersion = SteamMatchDescriptor.CurrentProtocolVersion,
                contentHash,
                admissionExpiresAtUtc = DateTimeOffset.Parse("2026-09-26T12:00:00Z")
            }
        };
        string serialized = JsonSerializer.Serialize(payload);
        using var valid = JsonDocument.Parse(serialized);
        Assert.NotNull(LobbyPayloadCodec.TryParseMatchStarted(valid.RootElement));
        var firstMatchId = serialized.IndexOf($"\"matchId\":\"{Match:D}\"", StringComparison.Ordinal);
        Assert.True(firstMatchId >= 0);
        var mismatch = serialized.Remove(firstMatchId, $"\"matchId\":\"{Match:D}\"".Length)
            .Insert(firstMatchId, "\"matchId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"");
        using var mismatched = JsonDocument.Parse(mismatch);
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(mismatched.RootElement));
        var tampered = serialized.Replace("FightGuy", "Manki");
        using var changed = JsonDocument.Parse(tampered);
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(changed.RootElement));

        // ADR-0040: a missing format is a stock fight; Master names Arcade formats on the wire.
        Assert.Equal(MatchFormat.Stock, LobbyPayloadCodec.TryParseMatchStarted(valid.RootElement)!.Format);
        using var coop = JsonDocument.Parse(serialized.Insert(serialized.LastIndexOf('}'), ",\"format\":\"slopball-coop\""));
        Assert.Equal(MatchFormat.SlopballCoop, LobbyPayloadCodec.TryParseMatchStarted(coop.RootElement)!.Format);
        Assert.Equal(FriendlyFireMode.Off, LobbyPayloadCodec.TryParseMatchStarted(coop.RootElement)!.FriendlyFire);
        foreach (string value in new[] { "\"on\"", "\"off\"", "true", "false", "null", "1", "\"shoves\"" })
        {
            using var rules = JsonDocument.Parse(coop.RootElement.GetRawText().Insert(1, $"\"friendlyFire\":{value},"));
            var parsed = LobbyPayloadCodec.TryParseMatchStarted(rules.RootElement);
            if (value is "\"on\"" or "\"off\"") Assert.Equal(value == "\"on\"" ? FriendlyFireMode.On : FriendlyFireMode.Off, parsed!.FriendlyFire);
            else Assert.Null(parsed);
        }
        using var stockFriendly = JsonDocument.Parse(serialized.Insert(1, "\"friendlyFire\":true,"));
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(stockFriendly.RootElement));
        using var unknown = JsonDocument.Parse(serialized.Insert(serialized.LastIndexOf('}'), ",\"format\":\"volleyball\""));
        Assert.Null(LobbyPayloadCodec.TryParseMatchStarted(unknown.RootElement));

        var development = System.Text.Json.Nodes.JsonNode.Parse(serialized)!;
        development["descriptor"] = null;
        development["matchPort"] = 19883;
        development["serverAddress"] = "127.0.0.1";
        development["format"] = "manki-mayhem";
        development["friendlyFire"] = "shoves";
        using var developmentPush = JsonDocument.Parse(development.ToJsonString());
        var mayhem = LobbyPayloadCodec.TryParseMatchStarted(developmentPush.RootElement);
        Assert.NotNull(mayhem);
        Assert.Equal(MatchFormat.MankiMayhem, mayhem!.Format);
        Assert.Equal(FriendlyFireMode.Shoves, mayhem.FriendlyFire);

    }
}
