#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using SlopArena.Client;
using SlopArena.Client.Network;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class UnityAuditAsyncSelfTest
    {
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        private static readonly Guid RoomId = new("75f26813-fd47-483b-9398-22b65bc0166b");
        private static readonly Guid OtherRoomId = new("9c526944-6dfb-4393-a81b-a04a77db010b");
        private static readonly Guid MatchId = new("3ae64064-b3be-4765-a35c-35d1653755b5");

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Async/All")]
        public static void RunAll()
        {
            RunChatOwnership();
            RunRoomLeaveOwnership();
            RunPageOwnership();
            RunPvPExitOwnership();
            Debug.Log("[UnityAuditAsyncSelfTest] All delayed ownership regressions passed.");
        }

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Async/Chat")]
        public static void RunChatOwnership()
        {
            foreach (string change in new[] { "account", "lobby", "destroyed" })
            foreach (bool failure in new[] { false, true })
            {
                using var f = new Fixture();
                var pending = f.Hub.Expect("GetChatState");
                var work = CallTask(f.Chat, "RefreshAfterReconnectAsync");
                pending.RequireStarted();
                f.ChangeChatOwner(change);
                Set(f.Chat, "_chatStateReady", false);
                f.InstallProfile("202", "Current owner");
                f.SetDirectory("902");
                Set(f.Chat, "_status", "Current status");
                int notifications = 0;
                f.Chat.Changed += () => notifications++;
                pending.Complete(failure ? null : Snapshot("101", "Obsolete owner"));
                f.Pump.Finish(work);
                Require(f.Chat.Self?.PlayerId == "202" && ClientSession.Username == "Current owner" &&
                    ClientSession.SteamId == 202, change + " reconnect overwrote the current profile.");
                Require(f.Chat.OnlinePlayers.Single().PlayerId == "902" && f.Chat.DirectoryAvailable &&
                    f.Chat.Status == "Current status" && notifications == 0 &&
                    !(bool)Get(f.Chat, "_chatStateReady")! &&
                    !f.Chat.Conversations.SelectMany(c => c.Messages).Any(m => m.Sender.PlayerId == "101"),
                    change + " reconnect changed current directory/readiness/status or published a stale change.");
            }

            foreach (string method in new[] { "RefreshDirectoryAsync", "RefreshDirectoryCoreAsync", "RefreshAfterReconnectAsync" })
            foreach (bool failure in new[] { false, true })
            {
                using var f = new Fixture();
                Deferred? snapshot = method == "RefreshAfterReconnectAsync" ? f.Hub.Expect("GetChatState") : null;
                var pending = f.Hub.Expect("GetOnlinePlayers");
                var work = CallTask(f.Chat, method);
                snapshot?.Complete(Snapshot("101", "Original owner"));
                pending.RequireStarted();
                f.ChangeChatOwner("account");
                f.InstallProfile("202", "Current owner");
                f.SetDirectory("902");
                Set(f.Chat, "_status", "Current status");
                int notifications = 0;
                f.Chat.Changed += () => notifications++;
                pending.Complete(failure ? null : Players("101"));
                f.Pump.Finish(work);
                Require(f.Chat.OnlinePlayers.Single().PlayerId == "902" && f.Chat.DirectoryAvailable &&
                    f.Chat.Status == "Current status" && notifications == 0,
                    method + " applied an obsolete directory result or failure.");
            }

            using (var f = new Fixture())
            {
                Set(f.Chat, "_chatStateReady", false);
                var connection = new TaskCompletionSource<bool>();
                Set(f.Chat, "_connectTask", connection.Task);
                var work = f.Chat.RefreshDirectoryAsync();
                Require(!work.IsCompleted, "Directory refresh did not wait for connection admission.");
                f.ChangeChatOwner("account");
                f.InstallProfile("202", "Current owner");
                connection.SetResult(true);
                f.Pump.Finish(work);
                Require(f.Hub.Calls.Count == 0, "An old directory refresh requested the replacement account's directory.");
            }

            using (var f = new Fixture())
            {
                var state = f.Hub.Expect("GetChatState");
                var directory = f.Hub.Expect("GetOnlinePlayers");
                var work = CallTask(f.Chat, "RefreshAfterReconnectAsync");
                state.Complete(Snapshot("303", "Reconnected owner"));
                directory.RequireStarted();
                directory.Complete(Players("404", "405"));
                f.Pump.Finish(work);
                Require(f.Chat.Self?.PlayerId == "303" && ClientSession.Username == "Reconnected owner" &&
                    f.Chat.IsConnected && f.Chat.DirectoryAvailable && f.Chat.OnlinePlayers.Count == 2,
                    "A current reconnect failed to publish its profile and directory.");
            }

            using (var f = new Fixture())
            {
                var directory = f.Hub.Expect("GetOnlinePlayers");
                var work = f.Chat.RefreshDirectoryAsync();
                directory.Complete(null);
                f.Pump.Finish(work);
                Require(!f.Chat.DirectoryAvailable && f.Chat.Status == "Online directory unavailable.",
                    "A current directory failure did not remain visible.");
                var retry = f.Hub.Expect("GetOnlinePlayers");
                work = f.Chat.RefreshDirectoryAsync();
                retry.Complete(Players("505"));
                f.Pump.Finish(work);
                Require(f.Chat.DirectoryAvailable && f.Chat.OnlinePlayers.Single().PlayerId == "505" &&
                    f.Chat.Status == "Connected", "A current directory retry did not recover.");
            }

            foreach (bool replaceDuringDirectory in new[] { false, true })
            {
                using var f = new Fixture();
                Set(f.Chat, "_lobby", null);
                Set(f.Chat, "_chatStateReady", false);
                Set(f.Chat, "_developmentGuest", true);
                Set(f.Chat, "_authTask", Task.FromResult(true));
                var state = f.Hub.Expect("GetChatState");
                var directory = f.Hub.Expect("GetOnlinePlayers");
                int created = 0;
                f.Chat.ActiveLobbyChanged += lobby =>
                {
                    if (lobby == null) return;
                    created++;
                    Set(lobby, "_conn", f.Hub);
                };
                var work = (Task<bool>)Call(f.Chat, "ConnectCoreAsync")!;
                state.Complete(Snapshot("101", "Connected owner"));
                directory.RequireStarted();
                if (replaceDuringDirectory)
                {
                    f.ChangeChatOwner("account");
                    f.InstallProfile("202", "Current owner");
                    f.SetDirectory("902");
                }
                directory.Complete(Players("707"));
                f.Pump.Finish(work);
                Require(created == 1, "Initial connection creation did not use the normal lobby creation path.");
                Require(work.Result == !replaceDuringDirectory,
                    "ConnectCoreAsync reported ownership incorrectly after its directory await.");
                Require(f.Chat.Self?.PlayerId == (replaceDuringDirectory ? "202" : "101") &&
                    f.Chat.OnlinePlayers.Single().PlayerId == (replaceDuringDirectory ? "902" : "707"),
                    "Initial connect or replacement directory state was lost.");
            }
            Debug.Log("[UnityAuditAsyncSelfTest] Delayed reconnect, directory, account and initial-connect cases passed.");
        }

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Async/Room Leave")]
        public static void RunRoomLeaveOwnership()
        {
            foreach (Guid nextRoom in new[] { RoomId, OtherRoomId })
            {
                using var f = new Fixture();
                f.JoinRoom();
                var gate = (SemaphoreSlim)Get(f.Lobby, "_roomOperationGate")!;
                Require(gate.Wait(0), "Fixture could not hold the room gate.");
                var refresh = f.Hub.Expect("GetMyRoom");
                Task roomWork;
                Task<bool> leave;
                try
                {
                    roomWork = f.Lobby.GetMyRoomAsync();
                    leave = LeaveCurrent(f.Lobby);
                    Require(!roomWork.IsCompleted && !leave.IsCompleted, "Room operations did not queue behind the gate.");
                }
                finally { gate.Release(); }
                f.Pump.Until(() => refresh.Started, "Queued membership refresh did not acquire the gate.");
                refresh.Complete(Room(nextRoom));
                f.Pump.Finish(roomWork);
                f.Pump.Finish(leave);
                Require(!leave.Result && f.Lobby.JoinedRoomId == nextRoom &&
                    f.Hub.Calls.SequenceEqual(new[] { "GetMyRoom" }),
                    "A queued leave sent an RPC after a newer membership operation acquired the gate.");
            }

            using (var f = new Fixture())
            {
                f.JoinRoom();
                var pending = f.Hub.Expect("LeaveRoom");
                var leave = LeaveCurrent(f.Lobby);
                pending.RequireStarted();
                var changed = f.Hub.Expect("GetChatState");
                var roomWork = f.Lobby.GetChatStateAsync();
                changed.Complete(new ChatSnapshot { Server = new ServerChatState { RoomId = OtherRoomId } });
                f.Pump.Finish(roomWork);
                Require(f.Lobby.JoinedRoomId == RoomId, "Chat refresh replaced membership during a pending leave.");
                Call(f.Lobby, "SetJoinedRoomId", OtherRoomId);
                pending.Complete(null);
                f.Pump.Finish(leave);
                int clears = 0;
                f.Lobby.ChatServerChanged += _ => clears++;
                f.Lobby.Pump();
                Require(!leave.Result && f.Lobby.JoinedRoomId == OtherRoomId && clears == 0,
                    "Leave completion claimed or cleared replacement membership.");
            }

            using (var f = new Fixture())
            {
                f.JoinRoom();
                var pending = f.Hub.Expect("LeaveRoom");
                var leave = LeaveCurrent(f.Lobby);
                Require(!leave.IsCompleted && f.Lobby.JoinedRoomId == RoomId, "Current leave did not await its RPC.");
                int clears = 0;
                f.Lobby.ChatServerChanged += state => { if (state.RoomId == null) clears++; };
                pending.Complete(null);
                f.Pump.Finish(leave);
                f.Lobby.Pump();
                Require(leave.Result && f.Lobby.JoinedRoomId == Guid.Empty && clears == 1,
                    "A current leave failed to clear membership and publish the chat departure exactly once.");
                var repeat = LeaveCurrent(f.Lobby);
                f.Pump.Finish(repeat);
                Require(!repeat.Result && f.Hub.Calls.Count == 1, "An obsolete repeat leave reached the transport.");
            }
            Debug.Log("[UnityAuditAsyncSelfTest] Queued, superseded and current room leave cases passed.");
        }

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Async/Pages")]
        public static void RunPageOwnership()
        {
            foreach (bool stage in new[] { false, true })
            foreach (bool failure in new[] { false, true })
            {
                using var f = new Fixture();
                var page = f.CreatePage(stage);
                var pending = f.Hub.Expect("GetMyRoom");
                var work = CallTask(page, "RefreshRoom");
                pending.RequireStarted();
                var current = f.Reactivate(page, stage);
                var currentRoom = Room(RoomId, stage ? "Stage Select" : "Character Select");
                Set(page, "_roomSnapshot", currentRoom);
                Set(page, "_roomActionPending", true);
                f.Status(page).text = "Current page status";
                if (failure) pending.Fail(); else pending.Complete(Room(RoomId));
                f.Pump.Finish(work);
                Require(ReferenceEquals(Get(page, "_roomSnapshot"), currentRoom) &&
                    (bool)Get(page, "_roomActionPending")! && f.Status(page).text == "Current page status" &&
                    ReferenceEquals(FrontendController.CurrentContext, current),
                    "A delayed refresh changed the same controller's later activation.");
                var retry = f.Hub.Expect("GetMyRoom");
                work = CallTask(page, "RefreshRoom");
                var accepted = Room(RoomId, stage ? "Stage Select" : "Character Select");
                retry.Complete(accepted);
                f.Pump.Finish(work);
                Require(ReferenceEquals(Get(page, "_roomSnapshot"), accepted) && !(bool)Get(page, "_roomActionPending")!,
                    "The current activation could not apply a legitimate room refresh.");
            }

            foreach (bool stage in new[] { false, true })
            {
                using var f = new Fixture();
                var page = f.CreatePage(stage);
                f.Hub.DisconnectLocally();
                var connection = f.Hub.Expect("StartConnection");
                var work = CallTask(page, "RefreshRoom");
                connection.RequireStarted();
                var context = f.Reactivate(page, stage);
                f.Status(page).text = "Current connection owner";
                connection.Complete(null);
                f.Pump.Finish(work);
                Require(f.Hub.Calls.SequenceEqual(new[] { "StartConnection" }) && context.Valid &&
                    f.Status(page).text == "Current connection owner",
                    "An obsolete page requested room state after its connection await.");
            }

            foreach (string method in new[] { "SelectCamp", "SelectColour", "OnRoomLockInClicked", "OnRoomStartMatchClicked" })
            foreach (bool failure in new[] { false, true })
            {
                using var f = new Fixture();
                var page = f.CreatePage(false);
                var room = Room(RoomId, "Character Select");
                room.Format = method == "SelectColour" ? MatchFormats.CourseRaceWire : MatchFormats.SlopballVersusWire;
                room.Members = new[] { new RoomMemberInfo { SteamId = 101, IsLeader = true, LockedIn = true, Camp = "teal" },
                    new RoomMemberInfo { SteamId = 102, LockedIn = true, Camp = "clay" } };
                Set(page, "_roomSnapshot", room);
                string rpc = method switch
                {
                    "SelectCamp" => "RoomSelectCamp", "SelectColour" => "RoomSelectColour",
                    "OnRoomLockInClicked" => "RoomSelectCharacter", _ => "RoomStartMatch"
                };
                var pending = f.Hub.Expect(rpc);
                object?[] args = method == "SelectCamp" ? new object?[] { "teal" } :
                    method == "SelectColour" ? new object?[] { (byte)2 } : Array.Empty<object?>();
                Call(page, method, args);
                pending.RequireStarted();
                var context = f.Reactivate(page, false);
                var currentRoom = Room(RoomId, "Character Select");
                currentRoom.Format = room.Format;
                currentRoom.Members = room.Members;
                Set(page, "_roomSnapshot", currentRoom);
                var current = f.Hub.Expect(rpc);
                Call(page, method, args);
                current.RequireStarted();
                string status = f.Status(page).text;
                if (failure) pending.Fail(); else pending.Complete(method == "OnRoomStartMatchClicked" ? (object)default(System.Text.Json.JsonElement) : room);
                f.Pump.WaitForVoids(1);
                Require((bool)Get(page, "_roomActionPending")! &&
                    ReferenceEquals(Get(page, "_roomSnapshot"), currentRoom) &&
                    f.Status(page).text == status && context.Valid,
                    method + " changed the later activation in success, catch or finally.");
                current.Fail();
                f.Pump.FinishVoids();
                Require(!(bool)Get(page, "_roomActionPending")! && f.Status(page).ClassListContains("error"),
                    method + " could not finish the current activation's own rejected request.");
            }

            foreach (bool secondAwait in new[] { false, true })
            foreach (bool failure in new[] { false, true })
            {
                using var f = new Fixture();
                var page = f.CreatePage(true);
                var pick = f.Hub.Expect("RoomChoosePick");
                Deferred? start = secondAwait ? f.Hub.Expect("RoomStartCharacterSelect") : null;
                Call(page, "ConfirmRoomPick");
                pick.RequireStarted();
                if (secondAwait)
                {
                    pick.Complete(Room(RoomId, "Stage Select"));
                    start!.RequireStarted();
                }
                var context = f.Reactivate(page, true);
                var current = f.Hub.Expect("RoomChoosePick");
                Call(page, "ConfirmRoomPick");
                current.RequireStarted();
                string status = f.Status(page).text;
                var pending = start ?? pick;
                if (failure) pending.Fail(); else pending.Complete(Room(RoomId, "Character Select"));
                f.Pump.WaitForVoids(1);
                Require((bool)Get(page, "_confirmInFlight")! && (bool)Get(page, "_roomActionPending")! &&
                    f.Status(page).text == status && context.Valid &&
                    f.Hub.Calls.Count == (secondAwait ? 3 : 2),
                    "Stage confirmation cleared a newer busy flag, navigated, or issued a second obsolete RPC.");
                current.Fail();
                f.Pump.FinishVoids();
                Require(!(bool)Get(page, "_confirmInFlight")! && !(bool)Get(page, "_roomActionPending")! &&
                    f.Status(page).ClassListContains("error"), "Current stage rejection did not release its own controls.");
            }

            using (var f = new Fixture())
            {
                var page = f.CreatePage(true);
                var pick = f.Hub.Expect("RoomChoosePick");
                var start = f.Hub.Expect("RoomStartCharacterSelect");
                Call(page, "ConfirmRoomPick");
                pick.Complete(Room(RoomId, "Stage Select"));
                start.RequireStarted();
                start.Complete(Room(RoomId, "Character Select"));
                f.Pump.FinishVoids();
                Require(!(bool)Get(page, "_confirmInFlight")! &&
                    FrontendController.CurrentPage == FrontendPage.FighterSelect,
                    "A current stage confirmation failed to open fighter selection.");
            }
            Debug.Log("[UnityAuditAsyncSelfTest] Same-lobby page reuse and both stage-confirmation awaits passed.");
        }

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Async/PvP Exit")]
        public static void RunPvPExitOwnership()
        {
            foreach (string change in new[] { "account", "match", "room", "scene", "membership" })
            foreach (bool failure in new[] { false, true })
            {
                if (change == "membership" && failure) continue;
                using var f = new Fixture();
                var match = f.CreateMatch();
                var pending = f.Hub.Expect("LeaveRoom");
                var work = CallTask(match, "LeaveRoomAndReturnAsync");
                pending.RequireStarted();
                f.Pump.Finish(CallTask(match, "LeaveRoomAndReturnAsync"));
                Require(f.Hub.Calls.Count == 1, "Repeated match exit bypassed single-flight.");
                if (change == "account") Set(f.Chat, "_accountGeneration", (int)Get(f.Chat, "_accountGeneration")! + 1);
                if (change == "match") MatchConfig.MatchId = Guid.NewGuid();
                if (change == "room") ClientSession.SelectedRoomId = OtherRoomId;
                if (change == "scene") Object.DestroyImmediate(match.gameObject);
                if (change == "membership") Call(f.Lobby, "SetJoinedRoomId", OtherRoomId);
                var expectedMatch = MatchConfig.MatchId;
                var expectedRoom = ClientSession.SelectedRoomId;
                if (failure) pending.Fail(); else pending.Complete(null);
                f.Pump.Finish(work);
                Require(MatchConfig.MatchId == expectedMatch && ClientSession.SelectedRoomId == expectedRoom &&
                    ClientSession.SelectedOnlineMode == ClientSession.OnlineSelection.Room &&
                    FrontendController.CurrentPage == FrontendPage.Home && f.Gameplay!.IsServerConnected &&
                    ReferenceEquals(Get(typeof(NetworkClient), "_activeMatch"), f.Gameplay),
                    change + " stale exit cleared match/room state or navigated on completion.");
            }

            using (var f = new Fixture())
            {
                var match = f.CreateMatch();
                Set(f.Chat, "_chatStateReady", false);
                var connection = new TaskCompletionSource<bool>();
                Set(f.Chat, "_connectTask", connection.Task);
                var work = CallTask(match, "LeaveRoomAndReturnAsync");
                Require(!work.IsCompleted, "Match exit did not wait for reconnect.");
                Set(f.Chat, "_accountGeneration", (int)Get(f.Chat, "_accountGeneration")! + 1);
                connection.SetResult(true);
                f.Pump.Finish(work);
                Require(f.Hub.Calls.Count == 0 && MatchConfig.MatchId == MatchId &&
                    FrontendController.CurrentPage == FrontendPage.Home && f.Gameplay!.IsServerConnected,
                    "An exit resumed under a different account and sent a leave or navigated.");
            }

            foreach (bool failure in new[] { false, true })
            {
                using var f = new Fixture();
                var match = f.CreateMatch();
                var pending = f.Hub.Expect("LeaveRoom");
                var work = CallTask(match, "LeaveRoomAndReturnAsync");
                if (failure) pending.Fail(); else pending.Complete(null);
                f.Pump.Finish(work);
                Require(MatchConfig.MatchId == null && MatchConfig.Transport == MatchTransport.None &&
                    !f.Gameplay!.IsServerConnected && Get(typeof(NetworkClient), "_activeMatch") == null,
                    "Current exit did not clear the departed match.");
                Require(FrontendController.CurrentPage == (failure ? FrontendPage.LobbyRoom : FrontendPage.ServerBrowser) &&
                    ClientSession.SelectedRoomId == (failure ? RoomId : Guid.Empty) &&
                    ClientSession.SelectedOnlineMode == (failure ? ClientSession.OnlineSelection.Room : ClientSession.OnlineSelection.LegacyServer),
                    "Current exit lost the success destination or room-preserving failure route.");
            }
            Debug.Log("[UnityAuditAsyncSelfTest] Delayed PvP reconnect/leave, single-flight, obsolete owner and current exit cases passed.");
        }

        private static ChatSnapshot Snapshot(string id, string name) => new()
        {
            Self = new ChatPlayer(id, name, "test"),
            GlobalMessages = new[] { new ChatMessage { MessageId = Guid.NewGuid(), Channel = "global",
                Sender = new ChatPlayer(id, name, "test"), Text = "History for " + id } }
        };
        private static ChatPlayer[] Players(params string[] ids) => ids.Select(id => new ChatPlayer(id, "Player " + id, "test")).ToArray();
        private static RoomSnapshot Room(Guid id, string phase = "Lobby") => new()
        {
            Id = id, Phase = phase, Format = MatchFormats.StockWire, AdmittedArenas = new[] { "test-arena" },
            AdmittedCharacters = new[] { "FightGuy" }, Members = new[] { new RoomMemberInfo { SteamId = 101, IsLeader = true } }
        };
        private static Task<bool> LeaveCurrent(LobbyClient lobby) =>
            (Task<bool>)typeof(LobbyClient).GetMethod("LeaveRoomIfCurrentAsync", Fields, null, new[] { typeof(Guid) }, null)!
                .Invoke(lobby, new object[] { RoomId })!;
        private static Task CallTask(object target, string method) => (Task)Call(target, method)!;
        private static object? Call(object target, string method, params object?[] args)
        {
            try { return target.GetType().GetMethod(method, Fields)!.Invoke(target, args); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }
        private static object? Get(object target, string field) => FindField(target is Type type ? type : target.GetType(), field)
            .GetValue(target is Type ? null : target);
        private static void Set(object target, string field, object? value) => FindField(target is Type type ? type : target.GetType(), field)
            .SetValue(target is Type ? null : target, value);
        private static FieldInfo FindField(Type type, string field)
        {
            for (Type? current = type; current != null; current = current.BaseType)
                if (current.GetField(field, Fields) is { } found) return found;
            throw new MissingFieldException(type.FullName, field);
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private sealed class Deferred
        {
            public string Method { get; }
            public TaskCompletionSource<object?> Completion { get; } = new();
            public bool Started { get; set; }
            public Deferred(string method) => Method = method;
            public void RequireStarted()
            {
                ((Continuations)SynchronizationContext.Current!).Until(() => Started,
                    Method + " did not reach its request barrier.");
                Require(!Completion.Task.IsCompleted, Method + " barrier already completed.");
            }
            public void Complete(object? result) { RequireStarted(); Completion.SetResult(result); }
            public void Fail() { RequireStarted(); Completion.SetException(new InvalidOperationException("Expected delayed rejection")); }
        }

        private sealed class DelayedHub : HubConnection
        {
            private readonly Queue<Deferred> _expected = new();
            private readonly List<Deferred> _started = new();
            public List<string> Calls { get; } = new();
            public List<string> Unexpected { get; } = new();
            public DelayedHub() : base(new NoConnections(), new JsonHubProtocol(),
                new IPEndPoint(IPAddress.Loopback, 1), new NoServices(), NullLoggerFactory.Instance)
                => SetState(HubConnectionState.Connected);
            public Deferred Expect(string method)
            { var next = new Deferred(method); _expected.Enqueue(next); return next; }
            public override Task<object?> InvokeCoreAsync(string methodName, Type returnType, object?[] args,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(methodName);
                if (_expected.Count == 0 || _expected.Peek().Method != methodName)
                {
                    Unexpected.Add(methodName);
                    return Task.FromException<object?>(new InvalidOperationException("Unexpected fixture RPC " + methodName));
                }
                var next = _expected.Dequeue();
                next.Started = true;
                _started.Add(next);
                return next.Completion.Task;
            }
            public override async Task StartAsync(CancellationToken cancellationToken = default)
            {
                await InvokeCoreAsync("StartConnection", typeof(object), Array.Empty<object?>(), cancellationToken);
                SetState(HubConnectionState.Connected);
            }
            public void DisconnectLocally() => SetState(HubConnectionState.Disconnected);
            public override ValueTask DisposeAsync() => default;
            private void SetState(HubConnectionState value)
            {
                var state = Get(this, "_state")!;
                state.GetType().GetProperty("OverallState", Fields)!.SetValue(state, value);
                Require(State == value, "SignalR fixture could not set its local connection state.");
            }
            public void CancelOutstanding()
            { foreach (var next in _started) next.Completion.TrySetCanceled(); }
            public void Verify() => Require(_expected.Count == 0 && Unexpected.Count == 0 &&
                _started.All(next => next.Completion.Task.IsCompleted), "Fixture left an expected, unexpected or pending RPC.");
        }

        private sealed class NoConnections : IConnectionFactory
        {
            public ValueTask<ConnectionContext> ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("This selftest must never open a transport.");
        }
        private sealed class NoServices : IServiceProvider
        { public object? GetService(Type serviceType) => null; }

        private sealed class NoHttp : HttpMessageHandler
        {
            public int Requests { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests++;
                throw new InvalidOperationException("This selftest must never issue an HTTP request.");
            }
        }

        private sealed class Continuations : SynchronizationContext, IDisposable
        {
            private readonly SynchronizationContext? _previous = Current;
            private readonly ConcurrentQueue<(SendOrPostCallback callback, object? state)> _callbacks = new();
            private int _voids;
            public Continuations() => SetSynchronizationContext(this);
            public override void Post(SendOrPostCallback callback, object? state) => _callbacks.Enqueue((callback, state));
            public override void OperationStarted() => _voids++;
            public override void OperationCompleted() => _voids--;
            public void Until(Func<bool> done, string message)
            {
                var timeout = Stopwatch.StartNew();
                while (!done())
                {
                    if (_callbacks.TryDequeue(out var next)) next.callback(next.state);
                    else Thread.Sleep(1);
                    Require(timeout.Elapsed.TotalSeconds < 3, message);
                }
                Drain();
            }
            private void Drain()
            { while (_callbacks.TryDequeue(out var next)) next.callback(next.state); }
            public void Finish(Task work)
            { Until(() => work.IsCompleted, "A controlled async request failed to settle."); work.GetAwaiter().GetResult(); }
            public void WaitForVoids(int count) => Until(() => _voids == count, "An async UI handler failed to settle.");
            public void FinishVoids() => WaitForVoids(0);
            public void Dispose() => SetSynchronizationContext(_previous);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Dictionary<FieldInfo, object?> _saved = new();
            private readonly List<GameObject> _objects = new();
            private readonly List<DelayedHub> _hubs = new();
            private readonly Scene _scene;
            private readonly FrontendController _frontend;
            private readonly List<Object> _assets = new();
            private readonly NoHttp _httpHandler = new();
            private HttpClient? _http;
            public Continuations Pump { get; }
            public ChatSession Chat { get; }
            public LobbyClient Lobby { get; }
            public DelayedHub Hub { get; }
            public NetworkClient? Gameplay { get; private set; }
            public Fixture()
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Async selftests require Edit Mode.");
                SaveStatics(typeof(ClientSession));
                SaveStatics(typeof(MatchConfig));
                SaveStatics(typeof(ChatSession));
                SaveStatics(typeof(FrontendController));
                SaveStatics(typeof(NetworkClient));
                SaveStatics(typeof(MatchBase));
                SaveStatics(typeof(ServerBrowserUI));
                SaveStatics(typeof(LobbyRoomUI));
                SaveStatics(typeof(MenuNavigation));
                SaveStatics(typeof(PresentationFloor));
                SaveStatics(typeof(SlopArena.Client.Input.ChatInputGate));
                Pump = new Continuations();
                _scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    Set(typeof(ChatSession), "_instance", null);
                    Set(typeof(FrontendController), "_instance", null);
                    Set(typeof(FrontendController), "PageChanged", null);
                    Set(typeof(NetworkClient), "_activeMatch", null);
                    MatchConfig.Opponents = new(); MatchConfig.Camps = new(); MatchConfig.Colours = new();
                    MatchConfig.ClearActiveMatch();
                    ClientSession.SelectedRoomId = RoomId;
                    ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
                    ClientSession.SteamId = 101;
                    ClientSession.IsLobbyHost = true;
                    Chat = NewObject("Async chat").AddComponent<ChatSession>();
                    Set(typeof(ChatSession), "_instance", Chat);
                    Set(Chat, "_savedDisplayName", "Original owner");
                    Set(Chat, "_masterServerUrl", "async-selftest://offline");
                    _http = new HttpClient(_httpHandler) { BaseAddress = new Uri("https://async-selftest.invalid/") };
                    Set(Chat, "_masterClient", new MasterServerClient(_http));
                    Set(Chat, "_chatStateReady", true);
                    Set(Chat, "_nameApplied", true);
                    Hub = NewHub();
                    Lobby = NewLobby(Hub);
                    Set(Chat, "_lobby", Lobby);
                    ClientSession.ActiveLobby = Lobby;
                    InstallProfile("101", "Original owner");
                    _frontend = NewObject("Async frontend").AddComponent<FrontendController>();
                    Set(typeof(FrontendController), "_instance", _frontend);
                    var shell = NewObject("Async shell").AddComponent<FrontendShellView>();
                    Set(_frontend, "_shell", shell);
                    var fragment = ScriptableObject.CreateInstance<VisualTreeAsset>();
                    _assets.Add(fragment);
                    foreach (string name in new[] { "fighterSelect", "serverBrowser", "lobbyRoom" })
                    {
                        var destination = NewObject("Async destination " + name);
                        var receiver = destination.AddComponent<CharSelectController>();
                        receiver.enabled = false;
                        Set(_frontend, "_" + name + "Page", destination);
                        Set(_frontend, "_" + name + "Fragment", fragment);
                    }
                }
                catch { Restore(); throw; }
            }
            private GameObject NewObject(string name)
            {
                var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                go.SetActive(false);
                SceneManager.MoveGameObjectToScene(go, _scene);
                _objects.Add(go);
                return go;
            }
            private DelayedHub NewHub() { var hub = new DelayedHub(); _hubs.Add(hub); return hub; }
            private static LobbyClient NewLobby(DelayedHub hub)
            {
                var lobby = new LobbyClient("async-selftest://offline", "fixture-token");
                Set(lobby, "_conn", hub);
                return lobby;
            }
            public void JoinRoom() => Call(Lobby, "SetJoinedRoomId", RoomId);
            public void InstallProfile(string id, string name) => Call(Chat, "ApplySelf", new ChatPlayer(id, name, "test"));
            public void SetDirectory(params string[] ids)
            { Call(Chat, "ReplaceOnlinePlayers", (object)Players(ids)); Set(Chat, "_directoryAvailable", true); }
            public void ChangeChatOwner(string change)
            {
                if (change == "account")
                {
                    Call(Chat, "ClearAccountSession");
                    Set(Chat, "_lobby", NewLobby(NewHub()));
                    ClientSession.ActiveLobby = Chat.ActiveLobby;
                }
                else if (change == "lobby") Set(Chat, "_lobby", NewLobby(NewHub()));
                else Set(Chat, "_destroyed", true);
            }
            public MonoBehaviour CreatePage(bool stage)
            {
                ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.Room;
                var go = NewObject(stage ? "Async stage" : "Async fighter");
                MonoBehaviour page = stage ? go.AddComponent<StageSelectController>() : go.AddComponent<CharSelectController>();
                Reactivate(page, stage);
                go.SetActive(true);
                Require(page.isActiveAndEnabled, "Page fixture is not active.");
                return page;
            }
            public FrontendPageContext Reactivate(MonoBehaviour page, bool stage)
            {
                if (FrontendController.CurrentContext is { } previous) Call(previous, "Release");
                var context = (FrontendPageContext)Activator.CreateInstance(typeof(FrontendPageContext), Fields, null,
                    new object?[] { stage ? FrontendPage.StageSelect : FrontendPage.FighterSelect, null }, null)!;
                Set(_frontend, "_currentContext", context);
                ((IFrontendPageController)page).InjectPageContext(context);
                Set(page, "_lobby", Lobby);
                Set(page, "_roomMode", true);
                Set(page, "_roomSnapshot", Room(RoomId, stage ? "Stage Select" : "Character Select"));
                Set(page, "_roomActionPending", false);
                Set(page, stage ? "_lblWaiting" : "_lblPvPStatus", new Label());
                if (stage)
                {
                    Set(page, "_selectedArena", "test-arena");
                    Set(page, "_confirmInFlight", false);
                    Set(page, "_arcadeTab", false);
                    Set(page, "_grid", new VisualElement());
                }
                else { Set(page, "_selected", CharacterClass.FightGuy); Set(page, "_lockedIn", false); }
                return context;
            }
            public Label Status(MonoBehaviour page) => (Label)Get(page, page is StageSelectController ? "_lblWaiting" : "_lblPvPStatus")!;
            public PvPMatch CreateMatch()
            {
                JoinRoom();
                ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.Room;
                MatchConfig.Mode = GameMode.PvP;
                MatchConfig.Transport = MatchTransport.SteamP2P;
                MatchConfig.MatchId = MatchId;
                Gameplay = NewObject("Async gameplay transport").AddComponent<NetworkClient>();
                Require(!(bool)Get(Gameplay, "_hasSteamConnection")! && Get(Gameplay, "_udp") == null,
                    "Gameplay fixture unexpectedly owns a real transport.");
                Set(Gameplay, "_connected", true);
                Set(typeof(NetworkClient), "_activeMatch", Gameplay);
                return NewObject("Async PvP match").AddComponent<PvPMatch>();
            }
            private void SaveStatics(Type type)
            {
                foreach (var field in type.GetFields(Fields))
                    if (field.IsStatic && !field.IsLiteral && !field.IsInitOnly)
                        _saved[field] = field.GetValue(null);
            }
            public void Dispose()
            {
                try
                {
                    foreach (var hub in _hubs) hub.Verify();
                    Require(_httpHandler.Requests == 0, "Fixture attempted an unexpected HTTP request.");
                }
                finally { Restore(); }
            }
            private void Restore()
            {
                if (Chat != null) Set(Chat, "_destroyed", true);
                Set(typeof(ChatSession), "_instance", null);
                if (_frontend != null) Set(_frontend, "_currentContext", null);
                foreach (var hub in _hubs) hub.CancelOutstanding();
                try { Pump.FinishVoids(); }
                finally
                {
                    try
                    {
                        if (Chat != null) Chat.MasterClient?.Dispose();
                        _http?.Dispose();
                        foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
                        foreach (var asset in _assets) Object.DestroyImmediate(asset);
                        EditorSceneManager.ClosePreviewScene(_scene);
                    }
                    finally
                    {
                        foreach (var saved in _saved) saved.Key.SetValue(null, saved.Value);
                        Pump.Dispose();
                    }
                }
            }
        }
    }
}
