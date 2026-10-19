#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Client
{
    /// <summary>
    /// Static match and scene metadata shared by client screens.
    /// ChatSession owns launch authentication and the reusable SignalR connection;
    /// these fields expose its current identity and transport to existing match UI.
    /// </summary>
    public static class ClientSession
    {
#if UNITY_EDITOR
        public delegate bool LocalMatchCatalogProvider(out Shared.MatchContentCatalog? catalog, out string? failure);
        private static LocalMatchCatalogProvider? editorDevelopmentContentProvider;

        public static void RegisterEditorDevelopmentContentProvider(LocalMatchCatalogProvider provider)
        {
            editorDevelopmentContentProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }
#endif

        /// <summary>Packaged Playtest Master; development overrides are Editor-only.</summary>
        public const string DefaultMasterServerUrl = "https://master-test.sloparena.barakaslurp.fr";
        public static string MasterServerUrl = DefaultMasterServerUrl;

        /// <summary>Application JWT bearer token; null until online authentication succeeds.</summary>
        public static string? AuthToken;

        /// <summary>Verified SteamID, or a development guest identity in Editor mode.</summary>
        public static long SteamId;

        /// <summary>Current display name, maintained by ChatSession.</summary>
        public static string? Username;

        /// <summary>Game server the player selected in the Server Browser.</summary>
        public static Guid SelectedServerId;

        /// <summary>Display name of the selected game server (for the lobby title).</summary>
        public static string SelectedServerName = string.Empty;

        public enum OnlineSelection
        {
            LegacyServer,
            Room
        }

        /// <summary>Separates Room membership from the legacy physical-server lobby route.</summary>
        public static OnlineSelection SelectedOnlineMode = OnlineSelection.LegacyServer;
        public static Guid SelectedRoomId;
        /// <summary>
        /// The launch-scoped SignalR client owned by ChatSession. Scene transitions,
        /// match completion, and leaving a GameServer do not dispose this connection.
        /// </summary>
        public static Network.LobbyClient? ActiveLobby;

        /// <summary>
        /// Roster snapshot stashed by LobbyRoomUI.OnMatchStarting so
        /// CharSelectController has the player list immediately on scene load,
        /// before the first LobbyUpdated push arrives (issue #34).
        /// </summary>
        public static Shared.LobbySnapshot? LobbyRoster;

        /// <summary>Roster stashed at match start (entityId → name/class) for the results screen (issue #40).</summary>
        public static IReadOnlyList<Shared.LobbyPlayerInfo>? MatchRoster;
        /// <summary>Immutable content identity admitted for the active match.</summary>
        public static Shared.MatchContentCatalog? MatchContentCatalog { get; private set; }
        public static Shared.MatchContentHandleMap? MatchContentHandleMap { get; private set; }

        /// <summary>
        /// True when the local player is the lobby host. Computed from the
        /// roster (first player in the lobby), NOT <c>MatchConfig.IsHost</c> —
        /// on a dedicated server (alfred) every client joins via the server
        /// browser, which sets <c>MatchConfig.IsHost=false</c> even for the
        /// player the master promotes to lobby host. Set by CharSelectController
        /// on scene load, consumed by StageSelectController.
        /// </summary>
        public static bool IsLobbyHost;

        /// <summary>Immutable final standings snapshot consumed by ResultsUI.</summary>
        public static MatchResultsData? CurrentMatchResults { get; private set; }

        /// <summary>One player's immutable final standing line.</summary>
        public sealed class ResultEntry
        {
            public ResultEntry(
                ulong entityId,
                int placement,
                int kos,
                int falls,
                string name = "",
                string className = "",
                int stocksRemaining = 0,
                int damagePercent = 0)
            {
                EntityId = entityId;
                Placement = placement;
                KOs = kos;
                Falls = falls;
                Name = name ?? "";
                ClassName = className ?? "";
                StocksRemaining = stocksRemaining;
                DamagePercent = damagePercent;
            }

            public ulong EntityId { get; }
            public int Placement { get; }
            public int KOs { get; }
            public int Falls { get; }
            public string Name { get; }
            public string ClassName { get; }
            public int StocksRemaining { get; }
            public int DamagePercent { get; }
            public bool IsWinner => Placement == 1;
        }

        /// <summary>Immutable final standings snapshot for the Results scene.</summary>
        public sealed class MatchResultsData
        {
            public MatchResultsData(
                bool sharedVictory,
                string stageName,
                uint durationTicks,
                IReadOnlyList<ResultEntry> entries,
                Shared.SlopballRunResult? run = null)
            {
                Run = run;
                if (entries == null) throw new ArgumentNullException(nameof(entries));
                var copy = new ResultEntry[entries.Count];
                for (int i = 0; i < entries.Count; i++)
                    copy[i] = entries[i];

                SharedVictory = sharedVictory;
                StageName = stageName ?? "";
                DurationTicks = durationTicks;
                PlayerCount = copy.Length;
                Entries = Array.AsReadOnly(copy);
            }

            public bool SharedVictory { get; }
            public string StageName { get; }
            public uint DurationTicks { get; }
            public int PlayerCount { get; }
            public IReadOnlyList<ResultEntry> Entries { get; }
            /// <summary>A co-op Slopball run's team result; null for a stock fight.</summary>
            public Shared.SlopballRunResult? Run { get; }
        }

        /// <summary>Store the server-authored final result without local re-ranking.</summary>
        public static void ApplyAuthoritativeMatchResult(Shared.MatchResultPacket packet)
        {
            var entries = new ResultEntry[packet.Entries.Length];
            for (int i = 0; i < packet.Entries.Length; i++)
            {
                var entry = packet.Entries[i];
                entries[i] = new ResultEntry(
                    entry.EntityId,
                    entry.Placement,
                    entry.KOs,
                    entry.Falls);
            }

            CurrentMatchResults = new MatchResultsData(
                packet.SharedVictory,
                UI.MatchConfig.ArenaName,
                packet.DurationTicks,
                entries,
                packet.Run);
        }
        public static void SetLocalMatchResults(MatchResultsData results)
            => CurrentMatchResults = results;

        /// <summary>Apply a typed Master match push after validating routing and content.</summary>
        public static void ApplyMatchStarted(Shared.MatchStartedConfig config)
        {
            if (config == null)
            {
                RejectMatchStart("Master sent an empty match-start payload.");
                return;
            }

            var descriptor = config.Descriptor;
            Guid matchId = config.MatchId;
            if (config.RoomId is Guid roomId &&
                (SelectedOnlineMode != OnlineSelection.Room ||
                 SelectedRoomId != roomId || ActiveLobby?.JoinedRoomId != roomId))
            {
                UnityEngine.Debug.LogWarning("[PvP] Ignoring MatchStarted for a Room that is no longer selected or joined.");
                return;
            }
            if (matchId == Guid.Empty ||
                descriptor != null && descriptor.MatchId != matchId)
            {
                RejectMatchStart("Master sent an invalid match ID.");
                return;
            }
            if (UI.MatchConfig.Transport != UI.MatchTransport.None &&
                UI.MatchConfig.MatchId == matchId)
                return;

            bool developmentUdp = IsEditorDevelopmentUdpEnabled();

            MatchContentCatalog = null;
            MatchContentHandleMap = null;

            if (descriptor == null)
            {
                if (!developmentUdp || config.MatchPort <= 0)
                {
                    RejectMatchStart("This client requires a Steam match descriptor; raw UDP is available only in explicit Editor development mode.");
                    return;
                }
                if (config.RoomId is not null &&
                    (string.IsNullOrWhiteSpace(config.ServerAddress) ||
                     Uri.CheckHostName(config.ServerAddress) is not (UriHostNameType.IPv4 or UriHostNameType.Dns)))
                {
                    RejectMatchStart("Room Match has no valid physical GameHost address for development UDP.");
                    return;
                }
            }
            else
            {
                if (config.MatchPort != 0)
                {
                    RejectMatchStart("Steam match descriptor cannot be combined with a UDP port.");
                    return;
                }
                if (!IsValidSteamDescriptor(descriptor, out var descriptorFailure))
                {
                    RejectMatchStart(descriptorFailure);
                    return;
                }
            }

            var content = config.Content;
            if (content == null)
            {
                RejectMatchStart("Match content admission failed: authoritative content map is missing.");
                return;
            }
            if (!TryBuildAndValidateMatchCatalog(content, out var catalog, out var contentFailure) ||
                catalog == null)
            {
                RejectMatchStart($"Match content admission failed: {contentFailure ?? "content catalog could not be built."}");
                return;
            }
            if (descriptor != null &&
                !string.Equals(Shared.SteamMatchDescriptor.HashContent(content), descriptor.ContentHash, StringComparison.Ordinal))
            {
                RejectMatchStart("Steam match content hash does not match the authoritative content map.");
                return;
            }
            if (config.Players == null || config.Players.Count is < 2 or > 4)
            {
                RejectMatchStart("Master match roster is missing or outside the supported player count.");
                return;
            }

            Shared.LobbyPlayerInfo? local = null;
            if (descriptor != null)
            {
                var entityIds = new HashSet<int>();
                var steamIds = new HashSet<long>();
                foreach (var player in config.Players)
                {
                    if (player.SteamId <= 0 || player.EntityId <= 0 ||
                        !steamIds.Add(player.SteamId) || !entityIds.Add(player.EntityId))
                    {
                        RejectMatchStart("Steam match roster has a missing or duplicate Steam/entity identity.");
                        return;
                    }
                }
            }
            foreach (var player in config.Players)
                if (player.SteamId == SteamId)
                    local = player;
            if (local == null || local.EntityId <= 0 || (descriptor != null && SteamId <= 0))
            {
                RejectMatchStart("Master match roster does not contain this authenticated Steam account with an assigned entity.");
                return;
            }
            MatchContentCatalog = catalog;
            MatchContentHandleMap = content;
            CurrentMatchResults = null;
            UI.MatchConfig.Mode = UI.GameMode.PvP;
            UI.MatchConfig.Transport = descriptor != null
                ? UI.MatchTransport.SteamP2P
                : UI.MatchTransport.DevelopmentUdp;
            UI.MatchConfig.SteamDescriptor = descriptor;
            UI.MatchConfig.MatchId = matchId;
            UI.MatchConfig.ArenaName = config.ArenaName;
            UI.MatchConfig.ServerPort = descriptor != null ? 0 : config.MatchPort;
            if (descriptor == null && config.RoomId is not null)
                UI.MatchConfig.ServerIP = config.ServerAddress!;


            var playerClass = ParseClass(local.CharacterSelection,
                descriptor == null ? Shared.CharacterClass.Manki : Shared.CharacterClass.None);
            if (descriptor != null && (playerClass == Shared.CharacterClass.None ||
                catalog.Resolve(playerClass) == null))
            {
                RejectMatchStart("Master roster selected an unavailable player character.");
                return;
            }
            UI.MatchConfig.PlayerClass = playerClass;
            UI.MatchConfig.PlayerPackageId = catalog.Resolve(playerClass)?.Identity.PackageId ?? string.Empty;
            UI.MatchConfig.LocalEntityId = (ulong)local.EntityId;
            UI.MatchConfig.MaxStocks = config.MaxStocks;
            UI.MatchConfig.Format = config.Format;
            UI.MatchConfig.Opponents.Clear();
            foreach (var player in config.Players)
            {
                if (player.SteamId == SteamId) continue;
                if (player.EntityId <= 0) continue;
                var opponentClass = ParseClass(player.CharacterSelection,
                    descriptor == null ? Shared.CharacterClass.Manki : Shared.CharacterClass.None);
                if (descriptor != null && (opponentClass == Shared.CharacterClass.None ||
                    catalog.Resolve(opponentClass) == null))
                {
                    RejectMatchStart("Master roster selected an unavailable opponent character.");
                    return;
                }
                UI.MatchConfig.Opponents.Add(new UI.MatchConfig.OpponentInfo(
                    (ulong)player.EntityId,
                    opponentClass,
                    catalog.Resolve(opponentClass)?.Identity.PackageId ?? string.Empty));
            }

            MatchRoster = config.Players;
            UnityEngine.SceneManagement.SceneManager.LoadScene("Arena_PvP");
        }

        public static void ClearMatchForExit()
        {
            Network.NetworkClient.DisconnectActiveMatch();

            MatchContentCatalog = null;
            MatchContentHandleMap = null;
            MatchRoster = null;
            CurrentMatchResults = null;
            UI.MatchConfig.ClearActiveMatch();
        }

        public static void RejectMatchStart(string reason)
        {
            UnityEngine.Debug.LogError($"[PvP] {reason}");
            MatchContentCatalog = null;
            MatchContentHandleMap = null;
            MatchRoster = null;
            CurrentMatchResults = null;
            UI.MatchConfig.ClearActiveMatch();
            if (SelectedOnlineMode == OnlineSelection.Room && SelectedRoomId != Guid.Empty)
                UI.LobbyRoomUI.PendingRoomNotice = reason;
            UI.FrontendController.Show(SelectedRoomId != Guid.Empty || SelectedServerId != Guid.Empty
                ? UI.FrontendPage.LobbyRoom
                : UI.FrontendPage.ServerBrowser);
        }

        public static bool ApplyMatchAborted(Guid matchId, string reason)
        {
            if (matchId == Guid.Empty ||
                UI.MatchConfig.Transport is not (UI.MatchTransport.SteamP2P or UI.MatchTransport.DevelopmentUdp) ||
                UI.MatchConfig.MatchId != matchId)
                return false;
            EndActiveMatch($"Match aborted ({reason}).");
            return true;
        }

        public static void ApplySteamTransportFailure(Guid matchId, string reason)
        {
            if (UI.MatchConfig.Transport == UI.MatchTransport.SteamP2P &&
                UI.MatchConfig.SteamDescriptor is { } descriptor &&
                descriptor.MatchId == matchId)
                EndActiveMatch($"Match connection failed: {reason}");
        }

        private static void EndActiveMatch(string notice)
        {
            Network.NetworkClient.DisconnectActiveMatch();
            MatchContentCatalog = null;
            MatchContentHandleMap = null;
            MatchRoster = null;
            CurrentMatchResults = null;
            UI.MatchConfig.ClearActiveMatch();
            if (SelectedOnlineMode == OnlineSelection.Room && SelectedRoomId != Guid.Empty)
            {
                UI.LobbyRoomUI.PendingRoomNotice = notice;
                UI.FrontendController.Show(UI.FrontendPage.LobbyRoom);
                return;
            }
            if (SelectedServerId != Guid.Empty)
            {
                UI.FrontendController.Show(UI.FrontendPage.LobbyRoom);
                return;
            }

            UI.ServerBrowserUI.PendingReturnNotice = notice;
            UI.FrontendController.Show(UI.FrontendPage.ServerBrowser);
        }

        public static void ClearActiveMatchForAccountChange()
        {
            if (UI.MatchConfig.Transport is not (UI.MatchTransport.SteamP2P or UI.MatchTransport.DevelopmentUdp))
                return;
            Network.NetworkClient.DisconnectActiveMatch();
            MatchContentCatalog = null;
            MatchContentHandleMap = null;
            MatchRoster = null;
            CurrentMatchResults = null;
            UI.MatchConfig.ClearActiveMatch();
            UI.FrontendController.Show(UI.FrontendPage.ServerBrowser);
        }

        private static bool IsValidSteamDescriptor(Shared.SteamMatchDescriptor descriptor, out string failure)
        {
            failure = "Steam match descriptor is invalid.";
            // The admission deadline is not compared with this PC's clock: a skewed clock
            // would reject every match. The GameHost denies expired joins authoritatively.
            if (descriptor.MatchId == Guid.Empty ||
                descriptor.ServerSteamId == 0 ||
                descriptor.VirtualPort != Shared.SteamMatchDescriptor.GameplayVirtualPort ||
                descriptor.ProtocolVersion != Shared.SteamMatchDescriptor.CurrentProtocolVersion ||
                !IsLowerSha256(descriptor.ContentHash))
                return false;
            failure = string.Empty;
            return true;
        }

        private static bool IsLowerSha256(string value)
        {
            if (value == null || value.Length != 64)
                return false;
            foreach (char c in value)
                if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                    return false;
            return true;
        }

        private static bool IsEditorDevelopmentUdpEnabled()
        {
#if UNITY_EDITOR
            return UnityEngine.Application.isEditor &&
                string.Equals(Environment.GetEnvironmentVariable("SLOPARENA_DEV_UDP"), "1", StringComparison.Ordinal);
#else
            return false;
#endif
        }
        public static void InstallLocalMatchCatalog(Shared.MatchContentCatalog catalog)
        {
            MatchContentCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            var records = new System.Collections.Generic.List<Shared.MatchContentHandleRecord>();
            foreach (var entry in catalog.Entries)
                if (entry.LegacySelector.HasValue)
                    records.Add(new Shared.MatchContentHandleRecord(entry.Handle, entry.LegacySelector.Value, entry.Identity, entry.DisplayName));
            MatchContentHandleMap = new Shared.MatchContentHandleMap(Shared.MatchContentHandleMap.CurrentSchemaVersion, records);
        }
        public static bool TryBuildLocalMatchCatalog(out Shared.MatchContentCatalog? catalog, out string? failure)
        {
#if UNITY_EDITOR
            if (UnityEngine.Application.isEditor)
            {
                if (editorDevelopmentContentProvider == null)
                {
                    catalog = null;
                    failure = "content.development.provider-missing (provider): Editor development content provider is not registered.";
                    return false;
                }
                return editorDevelopmentContentProvider(out catalog, out failure);
            }
#endif
            return TryBuildPersistedLocalMatchCatalogCore(out catalog, out failure);
        }

        public static bool TryBuildPersistedLocalMatchCatalog(out Shared.MatchContentCatalog? catalog, out string? failure)
            => TryBuildPersistedLocalMatchCatalogCore(out catalog, out failure);

        private static bool TryBuildPersistedLocalMatchCatalogCore(out Shared.MatchContentCatalog? catalog, out string? failure)
        {
            catalog = null;
            failure = null;
            try
            {
                var resolver = LocalContentResolver.CreateDefault();
                var rosterResolution = resolver.ResolveRoster();
                if (!rosterResolution.Success || rosterResolution.Roster == null)
                {
                    failure = FormatDiagnostics(rosterResolution.Diagnostics);
                    return false;
                }

                var packages = new System.Collections.Generic.Dictionary<string, Shared.CookedCharacterPackageLoadResult>();
                foreach (var rosterEntry in rosterResolution.Roster.Entries)
                {
                    var packageResolution = resolver.ResolveCookedPackage(rosterEntry.PackageId);
                    if (!packageResolution.Success || packageResolution.Requirement == null)
                    {
                        failure = FormatDiagnostics(packageResolution.Diagnostics);
                        return false;
                    }

                    packages[rosterEntry.PackageId] = Shared.CookedCharacterPackageLoader.LoadDirectory(
                        System.IO.Path.Combine(packageResolution.RootPath, rosterEntry.PackageId),
                        rosterEntry.Requirement);
                }
                var built = new Shared.MatchContentCatalogBuilder().Build(
                    rosterResolution.Roster,
                    packages);
                if (!built.IsValid || built.Catalog == null)
                {
                    failure = FormatDiagnostics(built.Diagnostics);
                    return false;
                }

                catalog = built.Catalog;
                return true;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                return false;
            }
        }

        private static string FormatDiagnostics(System.Collections.Generic.IEnumerable<Shared.CharacterDiagnostic> diagnostics)
            => string.Join("; ", diagnostics.Select(d => $"{d.Code} ({d.Path}): {d.Message}"));

        private static bool TryBuildAndValidateMatchCatalog(Shared.MatchContentHandleMap received, out Shared.MatchContentCatalog? catalog, out string? failure)
        {
            catalog = null;
            failure = null;
            if (!TryBuildPersistedLocalMatchCatalogCore(out var builtCatalog, out failure) || builtCatalog == null)
                return false;

            if (received == null || received.Entries.Count != builtCatalog.Entries.Count)
            {
                failure = "Authoritative content handle map is incomplete.";
                return false;
            }

            foreach (var record in received.Entries)
            {
                var local = builtCatalog.Resolve(record.Handle);
                if (local == null || local.Identity != record.Identity || local.LegacySelector != record.Selector)
                {
                    failure = $"Content handle {record.Handle.Value} identity mismatch.";
                    return false;
                }
            }

            catalog = builtCatalog;
            return true;
        }

        private static Shared.CharacterClass ParseClass(string? name, Shared.CharacterClass fallback)
        {
            if (string.IsNullOrEmpty(name))
                return fallback;
            return System.Enum.TryParse<Shared.CharacterClass>(name, ignoreCase: true, out var c) && c != Shared.CharacterClass.None
                ? c
                : fallback;
        }

        public static void Reset()
        {
            MasterServerUrl = DefaultMasterServerUrl;
            ClearAccountData();
        }

        /// <summary>Drop identity and match state without changing the selected development endpoint.</summary>
        public static void ClearAccountData()
        {
            Network.NetworkClient.DisconnectActiveMatch();

            UI.MatchConfig.ClearActiveMatch();
            AuthToken = null;
            SteamId = 0;
            Username = null;
            SelectedServerId = Guid.Empty;
            SelectedServerName = string.Empty;
            SelectedOnlineMode = OnlineSelection.LegacyServer;
            SelectedRoomId = Guid.Empty;
            ActiveLobby = null;
            LobbyRoster = null;
            MatchRoster = null;
            MatchContentCatalog = null;
            MatchContentHandleMap = null;
            CurrentMatchResults = null;
            IsLobbyHost = false;
        }
    }
}
