using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>Master-issued routing coordinates. GameHost still admits only the Steam-authenticated roster identity.</summary>
public sealed record SteamMatchDescriptor(
    Guid MatchId,
    ulong ServerSteamId,
    int VirtualPort,
    int ProtocolVersion,
    string ContentHash,
    DateTimeOffset AdmissionExpiresAtUtc)
{
    public const int CurrentProtocolVersion = SimulationProtocol.Version;
    public const int GameplayVirtualPort = 0;
    public const string Transport = "steam-p2p";
    /// <summary>Longest admission window a GameHost accepts. Clients time out locally from
    /// receipt instead of comparing the absolute deadline with a possibly skewed PC clock.</summary>
    public const int MaximumAdmissionWindowSeconds = 65;

    /// <summary>Preserves the original schema-1 characters-only catalog hash bytes.</summary>
    public static string HashCatalog(MatchContentHandleMap content) =>
        MatchContentInternals.Sha256(Encoding.UTF8.GetBytes(MatchContentHandleMapCodec.Serialize(
            new MatchContentHandleMap(MatchContentHandleMap.CatalogSchemaVersion, content.Entries))));

    public static string HashContent(MatchContentHandleMap content)
    {
        if (content.Arena == null || content.SchemaVersion != MatchContentHandleMap.CurrentSchemaVersion)
            throw new ArgumentException("Match content requires an arena identity.", nameof(content));
        return MatchContentInternals.Sha256(Encoding.UTF8.GetBytes(MatchContentHandleMapCodec.Serialize(content)));
    }
    public static bool TryParse(JsonElement element, out SteamMatchDescriptor? descriptor)
    {
        descriptor = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("transport", out var transport) || transport.ValueKind != JsonValueKind.String ||
            transport.GetString() != Transport ||
            !element.TryGetProperty("matchId", out var match) || match.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(match.GetString(), out var matchId) || matchId == Guid.Empty ||
            !element.TryGetProperty("serverSteamId", out var server) || server.ValueKind != JsonValueKind.String ||
            !ulong.TryParse(server.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var serverId) || serverId == 0 ||
            !element.TryGetProperty("virtualPort", out var port) || port.ValueKind != JsonValueKind.Number ||
            !port.TryGetInt32(out var virtualPort) || virtualPort != GameplayVirtualPort ||
            !element.TryGetProperty("protocolVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var protocolVersion) || protocolVersion != CurrentProtocolVersion ||
            !element.TryGetProperty("contentHash", out var hash) || hash.ValueKind != JsonValueKind.String ||
            !IsHash(hash.GetString()) ||
            !element.TryGetProperty("admissionExpiresAtUtc", out var deadline) || deadline.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(deadline.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var expiresAt))
            return false;
        descriptor = new SteamMatchDescriptor(matchId, serverId, virtualPort, protocolVersion,
            hash.GetString()!, expiresAt);
        return true;
    }

    private static bool IsHash(string? value)
    {
        if (value is not { Length: 64 }) return false;
        foreach (var c in value)
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        return true;
    }
}
