using System.Collections.Generic;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>
/// A player in the master server's match-start command to the game server
/// (ADR-0008, issue #35). The master server assigns <see cref="EntityId"/>
/// (1..N by lobby join order) and sends the locked-in character class so the
/// game server spawns the right entity instead of hardcoded Manki.
/// </summary>
/// <param name="SteamId">Master-verified SteamID (development guests only in development mode).</param>
/// <param name="CharacterClass">Locked-in character class the game server must spawn.</param>
/// <param name="EntityId">Server entity ID (1..N) the player drives and the server broadcasts state for.</param>
public sealed record MatchPlayer(long SteamId, CharacterClass CharacterClass, int EntityId, SlopballCamp Camp = SlopballCamp.None, byte Colour = 0);

/// <summary>Master match-start command. Protocol 2 adds Steam admission coordinates;
/// protocol 0 is retained only for explicit development UDP sessions.</summary>
public sealed record MatchStartRequest(
    string MatchId, string ArenaName, IReadOnlyList<MatchPlayer> Players,
    int MaxStocks = MatchDefaults.DefaultMaxStocks,
    int ProtocolVersion = 0,
    int VirtualPort = SteamMatchDescriptor.GameplayVirtualPort,
    DateTimeOffset? AdmissionExpiresAtUtc = null,
    string? CatalogHash = null,
    MatchFormat Format = MatchFormat.Stock,
    bool FriendlyFire = false,
    bool BallOnly = false);

/// <summary>
/// Parses the <c>POST /match/start</c> JSON body (<see cref="MatchStartRequest"/>).
/// Pure + dependency-free so it is unit-testable from <c>tests/Shared.Tests</c>
/// without the game server runtime. Wire keys are camelCase
/// (<c>matchId</c>, <c>arenaName</c>, <c>players</c>, <c>steamId</c>,
/// <c>characterClass</c>, <c>entityId</c>), matching System.Text.Json's default
/// policy on the master server.
/// </summary>
public static class MatchStartRequestCodec
{
    /// <summary>
    /// Parse a <see cref="MatchStartRequest"/>-shaped element. Returns null on
    /// any shape mismatch (including an unrecognised character class) so a
    /// malformed command never spawns a partial match.
    /// </summary>
    public static MatchStartRequest? TryParse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty("matchId", out var mid) || mid.ValueKind != JsonValueKind.String)
            return null;
        string matchId = mid.GetString()!;
        if (string.IsNullOrEmpty(matchId))
            return null;

        // arenaName is optional: a missing/empty value is treated as "" so the
        // game server can apply its own default arena (issue #35 review). Only
        // a present-but-non-string value is malformed.
        string arenaName = string.Empty;
        if (element.TryGetProperty("arenaName", out var arena))
        {
            if (arena.ValueKind != JsonValueKind.String)
                return null;
            arenaName = arena.GetString() ?? string.Empty;
        }

        if (!element.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<MatchPlayer>();
        foreach (var item in players.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;

            if (!item.TryGetProperty("steamId", out var steam) || steam.ValueKind != JsonValueKind.Number ||
                !steam.TryGetInt64(out var steamId) || steamId <= 0)
                return null;

            if (!item.TryGetProperty("characterClass", out var cc) || cc.ValueKind != JsonValueKind.String)
                return null;
            string classStr = cc.GetString()!;
            if (string.IsNullOrEmpty(classStr))
                return null;
            // Case-insensitive enum parse; reject unknown classes so a typo can't
            // silently spawn Manki (the exact bug this ticket fixes).
            if (!System.Enum.TryParse<CharacterClass>(classStr, ignoreCase: true, out var characterClass) ||
                !System.Enum.IsDefined(typeof(CharacterClass), characterClass) ||
                characterClass == CharacterClass.None)
                return null;

            if (!item.TryGetProperty("entityId", out var eid) || eid.ValueKind != JsonValueKind.Number ||
                !eid.TryGetInt32(out var entityId) || entityId <= 0)
                return null;

            SlopballCamp camp = SlopballCamp.None;
            if (item.TryGetProperty("camp", out var campElement)
                && (campElement.ValueKind != JsonValueKind.String || !SlopballCamps.TryParse(campElement.GetString(), out camp)))
                return null;
            byte colour = 0;
            if (item.TryGetProperty("colour", out var colourElement)
                && (colourElement.ValueKind != JsonValueKind.Number || !colourElement.TryGetByte(out colour) || colour > 3)) return null;
            list.Add(new MatchPlayer(steamId, characterClass, entityId, camp, colour));
        }

        if (list.Count is < 2 or > 4)
            return null;

        // maxStocks is optional (absent → default 3, issue #37). Present but
        // non-numeric (incl. fractional/out-of-int-range) or out of the [1,99]
        // byte range is malformed — never throw, per the codec's null contract.
        int maxStocks = 3;
        if (element.TryGetProperty("maxStocks", out var ms))
        {
            // Number-kind check first: TryGetInt32 throws on non-numeric kinds
            // (e.g. a string), and returns false for fractional/overflow values.
            if (ms.ValueKind != JsonValueKind.Number
                || !ms.TryGetInt32(out maxStocks)
                || maxStocks < 1 || maxStocks > 99)
                return null;
        }

        int protocolVersion = 0;
        int virtualPort = SteamMatchDescriptor.GameplayVirtualPort;
        DateTimeOffset? admissionExpiresAtUtc = null;
        string? catalogHash = null;
        if (element.TryGetProperty("protocolVersion", out var protocol))
        {
            if (protocol.ValueKind != JsonValueKind.Number ||
                !protocol.TryGetInt32(out protocolVersion) ||
                protocolVersion != SteamMatchDescriptor.CurrentProtocolVersion ||
                !Guid.TryParse(matchId, out var matchGuid) || matchGuid == Guid.Empty ||
                !element.TryGetProperty("virtualPort", out var vp) ||
                vp.ValueKind != JsonValueKind.Number ||
                !vp.TryGetInt32(out virtualPort) ||
                virtualPort != SteamMatchDescriptor.GameplayVirtualPort ||
                !element.TryGetProperty("catalogHash", out var expected) ||
                expected.ValueKind != JsonValueKind.String ||
                expected.GetString() is not { Length: 64 } expectedHash ||
                expectedHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
                !element.TryGetProperty("admissionExpiresAtUtc", out var deadline) ||
                deadline.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(deadline.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var expires) ||
                list.Exists(p => p.SteamId <= 0 || p.EntityId <= 0) ||
                list.Select(p => p.SteamId).Distinct().Count() != list.Count ||
                list.Select(p => p.EntityId).Distinct().Count() != list.Count)
                return null;
            admissionExpiresAtUtc = expires;
            catalogHash = expectedHash;
        }
        else if (element.TryGetProperty("admissionExpiresAtUtc", out var developmentDeadline))
        {
            if (developmentDeadline.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(matchId, out var developmentMatchGuid) || developmentMatchGuid == Guid.Empty ||
                !DateTimeOffset.TryParse(developmentDeadline.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var developmentExpiry))
                return null;
            admissionExpiresAtUtc = developmentExpiry;
        }
        // format is optional (absent → a stock fight, ADR-0040); an unknown name is malformed.
        string? formatName = null;
        if (element.TryGetProperty("format", out var formatElement))
        {
            if (formatElement.ValueKind != JsonValueKind.String)
                return null;
            formatName = formatElement.GetString();
        }
        if (!MatchFormats.TryParseWire(formatName, out var format))
            return null;
        bool friendlyFire = false;
        if (element.TryGetProperty("friendlyFire", out var friendly))
        {
            if (friendly.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            friendlyFire = friendly.GetBoolean();
        }
        if (friendlyFire && format != MatchFormat.SlopballCoop) return null;
        bool ballOnly = false;
        if (element.TryGetProperty("ballOnly", out var ballOnlyElement))
        {
            if (ballOnlyElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            ballOnly = ballOnlyElement.GetBoolean();
        }
        if (format == MatchFormat.SlopballVersus ? !SlopballCamps.AreBalanced(list.Select(p => p.Camp))
            : ballOnly || list.Any(p => p.Camp != SlopballCamp.None)) return null;
        if (format == MatchFormat.SlopballVersus && list.Any(p => p.EntityId >= ServerSimulation.ArcadeBallTargetInputId)) return null;
        if (list.Select(p => p.EntityId).Distinct().Count() != list.Count
            || list.Select(p => p.SteamId).Distinct().Count() != list.Count) return null;
        if (format.IsCourse() && (list.Any(p => p.EntityId >= CharacterCourseSimulation.FirstInputAlias)
            || list.Select(p => p.Colour).Distinct().Count() != list.Count)) return null;
        return new MatchStartRequest(matchId, arenaName, list, maxStocks,
            protocolVersion, virtualPort, admissionExpiresAtUtc, catalogHash, format, friendlyFire, ballOnly);
    }
}
