using System;
using SlopArena.Client.Network;
using UnityEditor;
using UnityEngine;

public static class RoomSteamInviteSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Room Steam Invite")]
    public static void Run()
    {
        var id = Guid.Parse("12345678-90ab-cdef-1234-567890abcdef");
        var room = new RoomSnapshot
        {
            Id = id, Phase = "Lobby", Joinable = true, MemberCount = 2, Capacity = 4
        };
        string? Eligibility(bool latest = true, bool steam = true,
            bool session = true, bool overlay = true, bool busy = false,
            Guid? joined = null) =>
            ChatSession.InviteUnavailableReason(room, joined ?? id, latest,
                steam, session, overlay, busy);
        Require(Eligibility() == null, "Any member of a confirmed, joinable Room may invite.");
        Require(Eligibility(joined: Guid.Empty) != null && Eligibility(latest: false) != null,
            "Leaving or losing the latest membership snapshot must disable invite.");
        Require(Eligibility(steam: false) != null && Eligibility(session: false) != null &&
            Eligibility(overlay: false)?.Contains("overlay") == true && Eligibility(busy: true) != null,
            "Authentication, unavailable overlay and active dialog must block invite.");
        room.MemberCount = 4;
        Require(Eligibility() != null, "A full Room cannot advertise an invitation.");
        room.MemberCount = 2;
        foreach (var phase in new[] { "Character Select", "Stage Select", "Match Starting", "In Match" })
        {
            room.Phase = phase;
            Require(Eligibility() != null, "Preparing or playing cannot invite: " + phase);
        }
        room.Phase = "Lobby";
        room.Joinable = false;
        Require(Eligibility() != null, "Master-declared unjoinable Room must disable invite.");
        room.Joinable = true;

        float next = 0;
        string? payload = null;
        int opens = 0;
        void Open(string value) { payload = value; opens++; }
        Require(ChatSession.TryOpenInviteDialog(id, true, Open, 10, ref next, out var feedback) &&
            payload == RoomConnection.Format(id) && !feedback.Contains("sent", StringComparison.OrdinalIgnoreCase),
            "Invite dialog must receive only the current Room locator; opening is not delivery.");
        Require(!ChatSession.TryOpenInviteDialog(id, true, Open, 10.2f, ref next, out _) && opens == 1,
            "Held input or repeated activation reopened the Steam dialog.");
        Require(ChatSession.TryOpenInviteDialog(id, true, Open, 12, ref next, out _) && opens == 2,
            "A deliberate later attempt should remain available.");
        Require(!ChatSession.TryOpenInviteDialog(id, false, Open, 14, ref next, out feedback) &&
            feedback.Contains("overlay", StringComparison.OrdinalIgnoreCase) && opens == 2,
            "Unavailable Steam overlay must not make an invitation attempt.");
        Require(!ChatSession.TryOpenInviteDialog(Guid.Empty, true, Open, 14, ref next, out _) &&
            opens == 2, "An empty or stale Room locator must not open the dialog.");
        void Throw(string _) => throw new InvalidOperationException("Overlay unavailable");
        Require(!ChatSession.TryOpenInviteDialog(id, true, Throw, 14, ref next, out feedback) &&
            feedback.Contains("could not open", StringComparison.OrdinalIgnoreCase) && opens == 2,
            "Overlay exceptions must not claim delivery or interrupt play.");
        Debug.Log("[RoomSteamInviteSelfTest] Eligibility, payload, debounce and overlay failure passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
