using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>Master match push. Steam descriptor is required for packaged online play;
/// the legacy port remains for explicit development matches only. Every push carries
/// a match ID, which must agree with the Steam descriptor when one is present.</summary>
public sealed record MatchStartedConfig(
    Guid ServerId,
    IReadOnlyList<LobbyPlayerInfo> Players,
    int MatchPort = 0,
    string ArenaName = "",
    int MaxStocks = MatchDefaults.DefaultMaxStocks,
    MatchContentHandleMap? Content = null,
    SteamMatchDescriptor? Descriptor = null,
    Guid? RoomId = null,
    string? ServerAddress = null,
    Guid MatchId = default,
    MatchFormat Format = MatchFormat.Stock,
    bool FriendlyFire = false,
    bool BallOnly = false);
