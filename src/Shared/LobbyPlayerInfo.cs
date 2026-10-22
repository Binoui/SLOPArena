namespace SlopArena.Shared;

/// <summary>
/// A player present in a SignalR lobby, mirroring the master server's
/// <c>MasterServer.Lobbies.LobbyPlayer</c> wire format (ADR-0004, issue #33).
/// Lobby state lives on the master server; the client only mirrors snapshots
/// pushed via <c>LobbyUpdated</c>/<c>PlayerJoined</c>/<c>MatchStarting</c>.
/// </summary>
/// <param name="IsHost">True for the lobby host (first joiner; promoted on leave).</param>
/// <param name="EntityId">
/// Server-side entity ID assigned to this player at match start (1..N by join
/// order), or 0 when not yet assigned (lobby/char-select snapshots, issue #35).
/// </param>
public sealed record LobbyPlayerInfo(
    long SteamId,
    string Name,
    string? CharacterSelection,
    bool LockedIn,
    bool IsHost,
    int EntityId = 0,
    SlopballCamp Camp = SlopballCamp.None);
