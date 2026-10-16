using System;
using System.Collections.Generic;
using System.Text;
using SlopArena.Client.Network;
using UnityEditor;
using UnityEngine;

public static class RoomSteamPresenceSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Room Steam Presence")]
    public static void Run()
    {
        var id = Guid.Parse("12345678-90ab-cdef-1234-567890abcdef");
        var room = new RoomSnapshot
        {
            Id = id, Name = "朋友的房间", Phase = "Lobby", MemberCount = 2,
            Capacity = 4, Joinable = true
        };
        Require(RoomConnection.Format(id) == "+slop_room 12345678-90ab-cdef-1234-567890abcdef",
            "Connection payload must contain only the Room GUID.");
        Require(RoomConnection.TryParse(RoomConnection.Format(id), out var parsed) && parsed == id,
            "Canonical connection payload must round-trip.");
        foreach (string malformed in new[]
        {
            "", "+slop_room ", "+slop_room " + Guid.Empty.ToString("D"),
            "+slop_room 12345678-90ab-cdef-1234-567890abcdeF",
            "+slop_room 12345678-90ab-cdef-1234-567890abcdef ",
            " +slop_room 12345678-90ab-cdef-1234-567890abcdef",
            "+slop_room 12345678-90ab-cdef-1234-567890abcdef +slop_room 12345678-90ab-cdef-1234-567890abcdef",
            "+slop_room https://example.com/12345678-90ab-cdef-1234-567890abcdef",
            "+slop_room 12345678-90ab-cdef-1234-567890abcdef" + new string('x', 256)
        })
        {
            Require(!RoomConnection.TryParse(malformed, out _),
                "Malformed connection payload must be rejected: " + malformed);
        }
        Require(!RoomConnection.TryParse(null, out _) &&
            !RoomConnection.TryParse("ordinary game launch", out _),
            "Missing or non-room launch payload must be rejected.");
        bool rejectedEmpty = false;
        try { RoomConnection.Format(Guid.Empty); }
        catch (ArgumentException) { rejectedEmpty = true; }
        Require(rejectedEmpty, "Empty Room ID must not be advertised.");

        var values = RoomSteamPresence.Map(room, id, true);
        Require(values[0] == RoomConnection.Format(id) && values[1] == id.ToString("D") &&
            values[2] == "2" && values[4] == "Lobby" && values[5] == "2/4" &&
            values[6] == "#SlopArenaRoom", "Joinable Room presence is incomplete.");
        room.Phase = "In Match";
        room.Joinable = false;
        values = RoomSteamPresence.Map(room, id, true);
        Require(values[0] == "" && values[1] == id.ToString("D") && values[4] == "In Match",
            "An unjoinable match must retain grouping but clear Join Game.");
        foreach (string phase in new[] { "Character Select", "Stage Select", "Match Starting" })
        {
            room.Phase = phase;
            Require(RoomSteamPresence.Map(room, id, true)[4] == phase,
                "Master Room phase must remain readable: " + phase);
        }
        room.Phase = "Lobby";
        room.Joinable = true;
        Require(RoomSteamPresence.Map(room, id, true)[0] == RoomConnection.Format(id),
            "Returning to a joinable Lobby must restore the connect payload.");
        Require(RoomSteamPresence.Map(room, Guid.NewGuid(), true)[0] == "" &&
            RoomSteamPresence.Map(room, id, false)[1] == "" &&
            RoomSteamPresence.Map(null, id, true)[1] == "",
            "Stale, unconfirmed or lost membership must clear presence.");

        room.Name = new string('界', 90) + "\n" + char.ConvertFromUtf32(0x1F600);
        values = RoomSteamPresence.Map(room, id, true);
        Require(Encoding.UTF8.GetByteCount(values[3]) <= 160 &&
            Encoding.UTF8.GetByteCount(values[7]) <= 255 &&
            !values[3].Contains("\n") && !values[3].Contains("\uFFFD") &&
            values[0] == RoomConnection.Format(id) && values[6] == "#SlopArenaRoom",
            "UTF-8 display limits may not corrupt the ID or localization token.");

        var published = new Dictionary<string, string>();
        int writes = 0;
        var presence = new RoomSteamPresence((key, value) =>
        {
            published[key] = value;
            writes++;
            return true;
        });
        presence.Reset(true, 0);
        Require(published["connect"] == "" && published["steam_player_group"] == "" &&
            published["steam_player_group_size"] == "" && published["steam_display"] == "",
            "Account/lifecycle reset must clear all owned Room keys.");
        int initialWrites = writes;
        presence.Refresh(true, false, false, 1);
        Require(writes == initialWrites, "Stable offline state must not publish per frame.");
        int attempts = 0;
        var retrying = new RoomSteamPresence((key, value) =>
        {
            attempts++;
            return key != "connect" || attempts > 1;
        });
        retrying.Reset(true, 0);
        int firstPass = attempts;
        retrying.Refresh(true, false, false, 1);
        Require(attempts == firstPass, "Steam failures must not retry every frame.");
        retrying.Refresh(true, false, false, 5);
        Require(attempts > firstPass, "A failed publication must retry after backoff.");
        Debug.Log("[RoomSteamPresenceSelfTest] Room mapping, connection payload, UTF-8 and reset passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
