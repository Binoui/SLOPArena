using System;
using SlopArena.Client.Network;
using UnityEditor;
using UnityEngine;

public static class SteamRoomJoinSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Room Steam Join")]
    public static void Run()
    {
        var roomId = Guid.Parse("12345678-90ab-cdef-1234-567890abcdef");
        var otherId = Guid.Parse("87654321-90ab-cdef-1234-567890abcdef");
        var join = new SteamRoomJoinCoordinator(null!);
        join.Receive(null);
        Require(!join.HasPendingJoin, "An ordinary Steam launch must not queue a Room.");
        join.Receive(RoomConnection.Format(roomId));
        join.Receive(RoomConnection.Format(roomId)); // Warm/cold callback replay.
        join.Receive(RoomConnection.Format(otherId)); // Competing URL during confirmation.
        Require(join.PendingRoomId == roomId, "A later notification replaced the active Room target.");
        Require(!SteamRoomJoinCoordinator.CanBegin(true, false, roomId) &&
            !SteamRoomJoinCoordinator.CanBegin(false, true, roomId) &&
            !SteamRoomJoinCoordinator.CanBegin(false, false, Guid.Empty) &&
            SteamRoomJoinCoordinator.CanBegin(false, false, roomId),
            "First-use setup, single-flight and empty targets must gate admission.");
        join.Cancel(); // Explicit cancel/account teardown.
        Require(!join.HasPendingJoin, "Cancellation left a pending Room request.");
        join.Receive(RoomConnection.Format(otherId));
        Require(join.PendingRoomId == otherId, "A new request after cancellation was lost.");
        join.Cancel();
        Require(SteamRoomJoinCoordinator.IsDuplicate(roomId, 10, roomId, 12) &&
            !SteamRoomJoinCoordinator.IsDuplicate(roomId, 10, roomId, 15) &&
            !SteamRoomJoinCoordinator.IsDuplicate(roomId, 10, otherId, 12),
            "Late warm/cold callbacks need bounded deduplication without blocking later requests.");

        var current = new RoomSnapshot { Id = roomId, Phase = "Lobby" };
        Require(SteamRoomJoinCoordinator.CanSwitchAfterConfirmation(roomId, current),
            "A confirmed current Lobby should permit an approved switch.");
        current.Phase = "Character Select";
        Require(SteamRoomJoinCoordinator.CanSwitchAfterConfirmation(roomId, current),
            "Character selection can switch after confirmation.");
        current.Phase = "Stage Select";
        Require(SteamRoomJoinCoordinator.CanSwitchAfterConfirmation(roomId, current),
            "Stage selection can switch after confirmation.");
        current.Phase = "Match Starting";
        Require(!SteamRoomJoinCoordinator.CanSwitchAfterConfirmation(roomId, current),
            "Entering Match Starting during confirmation must prevent leave.");
        current.Phase = "In Match";
        Require(!SteamRoomJoinCoordinator.CanSwitchAfterConfirmation(roomId, current),
            "A running match must not be switched.");
        current.Phase = "Lobby";
        current.Id = otherId;
        Require(!SteamRoomJoinCoordinator.CanSwitchAfterConfirmation(roomId, current) &&
            !SteamRoomJoinCoordinator.CanSwitchAfterConfirmation(roomId, null),
            "A changed or lost membership invalidates the old confirmation.");

        var destination = new RoomSummary { Id = otherId, Phase = "Lobby", Joinable = true };
        Require(SteamRoomJoinCoordinator.DestinationJoinable(new[] { destination }, otherId),
            "Master-advertised joinable Room should be eligible.");
        destination.Joinable = false;
        Require(!SteamRoomJoinCoordinator.DestinationJoinable(new[] { destination }, otherId) &&
            !SteamRoomJoinCoordinator.DestinationJoinable(Array.Empty<RoomSummary>(), otherId) &&
            !SteamRoomJoinCoordinator.DestinationJoinable(new[] { destination }, Guid.Empty),
            "Full, selecting, deleted and empty-ID destinations must not trigger leave.");
        Debug.Log("[SteamRoomJoinSelfTest] Duplicate, cancellation, first-use and switch-race policies passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
