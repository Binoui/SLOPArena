using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using SlopArena.Client.Network;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Stage select page (issue #221): a fragment mounted into the
    /// FrontendShell hosts — the stage grid in an intentional scroll region,
    /// the participant summary in the lower-right summary cell, and the
    /// selected-stage label plus start action outside the conversation cell.
    /// The stage registry remains file-driven; this screen owns only
    /// presentation and the existing start-match flow. Training uses this
    /// picker before launching its local match.
    /// </summary>
    public class StageSelectController : MonoBehaviour, IFrontendPageController
    {
        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;

        private string _selectedArena = "";
        private Button _btnConfirm;
        private Label _lblSelectedStage;
        private Label _lblWaiting;
        private VisualElement _playerCards;
        private VisualElement _grid;
        private LobbyClient _lobby;
        private ChatSession _chatSession;
        private bool _roomMode;
        private RoomSnapshot _roomSnapshot;
        private bool _roomActionPending;
        private bool _confirmInFlight;
        private bool _arcadeTab;
        // The leader's local Arcade choice before it is confirmed: co-op Slopball, the one online format so far.
        private bool _arcadePick;
        private string _arcadeFormat = MatchFormats.SlopballCoopWire;
        private const string SlopballArena = "slopball";
        private static readonly string CoopFormat = MatchFormats.SlopballCoopWire;

        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void OnEnable()
        {
            _selectedArena = "";
            _roomSnapshot = null;
            _roomActionPending = false;
            _confirmInFlight = false;
            _grid = _context.Q<VisualElement>("stage-grid");
            _grid?.Clear();
            _btnConfirm = _context.Q<Button>("btn-confirm");
            _lblSelectedStage = _context.Q<Label>("lbl-selected-stage");
            _lblWaiting = _context.Q<Label>("lbl-waiting");
            _playerCards = _context.Q<VisualElement>("player-cards-area");

            _roomMode = MatchConfig.Mode == GameMode.PvP &&
                ClientSession.SelectedOnlineMode == ClientSession.OnlineSelection.Room;
            bool isOnline = MatchConfig.Mode == GameMode.PvP;
            bool isTraining = MatchConfig.Mode == GameMode.Training;
            bool isHost = _roomMode ? false : !isOnline || ClientSession.IsLobbyHost;
            SetModeChrome(_roomMode ? "ROOM // WHAT DO WE PLAY?" : isOnline ? "ONLINE // SELECT STAGE" :
                isTraining ? "TRAINING // SELECT STAGE" : "SOLO // SELECT STAGE",
                _roomMode ? "STEP 1 OF 2  /  WHAT TO PLAY" : "STEP 2 OF 2  /  STAGE");
            _context.Q<Label>("subtitle").text = _roomMode
                ? (isHost ? "CHOOSE WHAT THE ROOM PLAYS" : "THE ROOM LEADER IS CHOOSING WHAT TO PLAY")
                : isHost ? "CHOOSE YOUR BATTLEGROUND" : "THE HOST WILL CHOOSE THE BATTLEGROUND";
            _context.Q<Label>("lbl-host").text = _roomMode
                ? (isHost ? "ROOM LEADER CHOOSES" : "WAITING FOR ROOM LEADER")
                : isOnline ? (isHost ? "HOST CHOOSES THE STAGE" : "WAITING FOR HOST")
                : "YOU CHOOSE THE STAGE";

            if (_btnConfirm != null)
            {
                _btnConfirm.style.display = DisplayStyle.None;
                _btnConfirm.text = _roomMode ? "PICK FIGHTERS" : isOnline ? "START MATCH" :
                    isTraining ? "ENTER TRAINING" : "START SOLO";
            }
            if (_lblWaiting != null)
            {
                _lblWaiting.style.display = isHost ? DisplayStyle.None : DisplayStyle.Flex;
                if (!isHost)
                    _lblWaiting.text = _roomMode ? "WAITING FOR THE ROOM LEADER TO CHOOSE" : "WAITING FOR HOST TO PICK A STAGE";
            }
            RenderPlayerCards();

            string? arenaDir = BakedContentPaths.ArenaDirectory();
            if (arenaDir != null)
                ArenaRegistry.LoadFromDirectory(arenaDir);

            Button firstStageButton = null;
            int stageCount = 0;
            var roster = StageRoster.Load();
            foreach (var arena in ArenaRegistry.All)
            {
                if (!roster.Offers(arena.Name, isTraining)) continue;
                string? baked = BakedContentPaths.ResolveArena(arena.Name);
                if (baked == null) continue;
                var arenaOpt = ArenaBinaryFormat.LoadFromFile(baked);
                if (arenaOpt is not ArenaDefinition arenaDef) continue;
                if (arenaDef.Purpose != ArenaPurpose.PvP) continue;
                if (arenaDef.CollisionTriangles == null || arenaDef.CollisionTriangles.Length == 0) continue;
                if (Resources.Load<GameObject>($"Stages/{arena.Name}") == null) continue;

                string capturedName = arena.Name;
                var card = new Button(() => SelectStage(capturedName))
                {
                    name = $"stage-{arena.Name}"
                };
                card.AddToClassList("stage-card");

                var swatch = new VisualElement();
                swatch.AddToClassList("stage-swatch");
                var preview = Resources.Load<Texture2D>($"UI/Stages/{arena.Name}");
                if (preview != null)
                    swatch.style.backgroundImage = new StyleBackground(preview);
                else if (!string.IsNullOrEmpty(arena.PreviewColor) &&
                    ColorUtility.TryParseHtmlString(arena.PreviewColor, out var swatchColor))
                    swatch.style.backgroundColor = swatchColor;

                var label = new Label(DisplayName(arena));
                label.AddToClassList("stage-name");
                card.Add(swatch);
                card.Add(label);
                card.SetEnabled(isHost);
                if (isTraining && arena.Name == "training")
                {
                    _grid?.Insert(0, card);
                    firstStageButton = card;
                }
                else
                {
                    _grid?.Add(card);
                    firstStageButton ??= card;
                }
                stageCount++;
            }

            if (stageCount == 0)
            {
                _selectedArena = "";
                if (_lblSelectedStage != null)
                    _lblSelectedStage.text = "NO STAGES AVAILABLE";
                if (_lblWaiting != null)
                {
                    _lblWaiting.text = isHost
                        ? "No admitted stages are available. Return to the menu."
                        : "Waiting for host. No stage list is available.";
                    _lblWaiting.style.display = DisplayStyle.Flex;
                }
                _btnConfirm?.SetEnabled(false);
            }
            // Solo keeps its in-flow stage choice through backward navigation;
            // online stage selection derives from server state, never from a
            // restored preference left by a previous flow (issue #213).
            else if (MatchConfig.Mode == GameMode.Solo && !string.IsNullOrEmpty(MatchConfig.ArenaName) &&
                MatchConfig.ArenaName != "training" &&
                _grid?.Q<VisualElement>($"stage-{MatchConfig.ArenaName}") != null)
            {
                SelectStage(MatchConfig.ArenaName);
            }
            // Local modes always open with a stage chosen, so the start action is
            // visible at once: Training's own arena, or Solo's first stage when no
            // earlier Solo stage is remembered.
            else if ((isTraining || MatchConfig.Mode == GameMode.Solo) && firstStageButton != null)
            {
                SelectStage(firstStageButton.name.Substring("stage-".Length));
            }

            BindRoomTabs();

            var btnBack = _context.Q<Button>("btn-back");
            Action back = _roomMode ? BackToRoom : BackToFighterSelect;
            if (btnBack != null)
            {
                btnBack.clicked += back;
                if (_roomMode) btnBack.style.display = DisplayStyle.None;
            }
            Button initial = isHost && firstStageButton != null ? firstStageButton : btnBack;
            if (initial != null)
                MenuNavigation.Configure(_context, initial, back, _btnConfirm);

            if (_roomMode)
            {
                _chatSession = ChatSession.Instance;
                if (_chatSession != null)
                    _chatSession.ActiveLobbyChanged += OnActiveLobbyChanged;
                _lobby = _chatSession?.ActiveLobby ?? ClientSession.ActiveLobby;
                BindRoomLobby(_lobby);
                if (_lobby != null)
                    _ = RefreshRoom();
            }
            else
            {
                _lobby = isOnline ? ClientSession.ActiveLobby : null;
                if (_lobby != null)
                {
                    _lobby.MatchStarted += OnMatchStarted;
                    _lobby.Error += OnError;
                }
            }

            if (_btnConfirm != null)
                _btnConfirm.clicked += OnConfirmClicked;
        }


        private void OnDisable()
        {
            if (_roomMode && _chatSession != null)
                _chatSession.ActiveLobbyChanged -= OnActiveLobbyChanged;
            _chatSession = null;
            if (_lobby != null)
            {
                if (_roomMode)
                {
                    UnbindRoomLobby(_lobby);
                }
                else
                {
                    _lobby.MatchStarted -= OnMatchStarted;
                    _lobby.Error -= OnError;
                }
                _lobby = null;
            }
            if (_btnConfirm != null) _btnConfirm.clicked -= OnConfirmClicked;
            UnbindRoomTabs();
        }

        /// <summary>
        /// Online Rooms choose what to play before fighters (ADR-0040). PVP lists the stages;
        /// ARCADE shows the Slopball and Character Course cards, which come online in later slices.
        /// </summary>
        private void BindRoomTabs()
        {
            _arcadeTab = false;
            _arcadePick = false;
            _context.Q<VisualElement>("stage-tabs")?.style.SetDisplay(_roomMode);
            _grid?.style.SetDisplay(true);
            _context.Q<VisualElement>("stage-arcade")?.style.SetDisplay(false);
            if (!_roomMode) return;
            var style = Resources.Load<StyleSheet>("UI/Arcade");
            if (style != null)
                foreach (var root in _context.OwnedRoots)
                    if (!root.styleSheets.Contains(style)) root.styleSheets.Add(style);
            if (_context.Q<Button>("stage-tab-pvp") is { } pvp) pvp.clicked += ShowPvpTab;
            if (_context.Q<Button>("stage-tab-arcade") is { } arcade) arcade.clicked += ShowArcadeTab;
            if (_context.Q<Button>("stage-arcade-slopball") is { } slopball) slopball.clicked += PickSlopball;
            if (_context.Q<Button>("stage-arcade-versus") is { } versus) versus.clicked += PickVersus;
            if (_context.Q<Button>("stage-arcade-course") is { } course) course.clicked += ShowArcadeComing;
            ShowRoomTab(false);
        }

        private void UnbindRoomTabs()
        {
            if (_context.Q<Button>("stage-tab-pvp") is { } pvp) pvp.clicked -= ShowPvpTab;
            if (_context.Q<Button>("stage-tab-arcade") is { } arcade) arcade.clicked -= ShowArcadeTab;
            if (_context.Q<Button>("stage-arcade-slopball") is { } slopball) slopball.clicked -= PickSlopball;
            if (_context.Q<Button>("stage-arcade-versus") is { } versus) versus.clicked -= PickVersus;
            if (_context.Q<Button>("stage-arcade-course") is { } course) course.clicked -= ShowArcadeComing;
        }

        private void ShowPvpTab() => ShowRoomTab(false);
        private void ShowArcadeTab() => ShowRoomTab(true);

        private void ShowRoomTab(bool arcade)
        {
            SetTabVisuals(arcade);
            if (arcade) ApplyArcadeTabState();
            else if (_roomSnapshot != null) OnRoomUpdated(_roomSnapshot);
        }

        private void SetTabVisuals(bool arcade)
        {
            _arcadeTab = arcade;
            _context.Q<Button>("stage-tab-pvp")?.EnableInClassList("shell-nav-tab--selected", !arcade);
            _context.Q<Button>("stage-tab-arcade")?.EnableInClassList("shell-nav-tab--selected", arcade);
            _grid?.style.SetDisplay(!arcade);
            _context.Q<VisualElement>("stage-arcade")?.style.SetDisplay(arcade);
        }

        private bool CoopAdmitted => _roomSnapshot?.AdmitsArcade(CoopFormat, SlopballArena) == true;

        private static bool IsSlopballPick(RoomSnapshot room)
            => (room.Format == CoopFormat || room.Format == MatchFormats.SlopballVersusWire) &&
               string.Equals(room.ArenaName, SlopballArena, StringComparison.Ordinal);

        /// <summary>The ARCADE tab's cards, label and start action; Slopball co-op is the format Master admits so far.</summary>
        private bool ArcadeAdmitted(string format) => _roomSnapshot?.AdmitsArcade(format, SlopballArena) == true
            && (format != MatchFormats.SlopballVersusWire || _roomSnapshot.Members.Length is 2 or 4);

        private void ApplyArcadeTabState()
        {
            bool leader = ClientSession.IsLobbyHost;
            string selected = leader && _arcadePick ? _arcadeFormat : _roomSnapshot?.Format;
            foreach (var (id, format) in new[] { ("stage-arcade-slopball", CoopFormat),
                ("stage-arcade-versus", MatchFormats.SlopballVersusWire) })
            {
                var card = _context.Q<Button>(id);
                bool admitted = ArcadeAdmitted(format);
                card?.EnableInClassList("arcade-mode-card--selected", selected == format);
                card?.SetEnabled(admitted && !_roomActionPending);
                if (card?.Q<Label>(className: "stage-arcade-soon") is { } badge)
                    badge.text = !admitted ? "NEEDS " + (format == CoopFormat ? "2–4 PLAYERS" : "2 OR 4 PLAYERS")
                        : format == CoopFormat ? "CO-OP ONLINE" : _roomSnapshot.Members.Length == 4 ? "2v2 ONLINE" : "1v1 ONLINE";
                if (format == CoopFormat && card?.Q<Label>(className: "arcade-mode-rule") is { } rule)
                    rule.text = "Two to four of you against the Launcher. Unlimited misses.";
            }
            string label = selected == MatchFormats.SlopballVersusWire ? "SLOPBALL VERSUS · FIRST TO 21" : "SLOPBALL CO-OP";
            if (_lblSelectedStage != null)
                _lblSelectedStage.text = _arcadePick || _roomSnapshot != null && IsSlopballPick(_roomSnapshot)
                    ? "ROOM PICK: " + label : "CHOOSE A SLOPBALL FORMAT";
            if (_btnConfirm != null)
            {
                _btnConfirm.style.display = DisplayStyle.Flex;
                _btnConfirm.text = leader ? "PICK FIGHTERS" : "WAITING FOR LEADER";
                _btnConfirm.SetEnabled(leader && _arcadePick && ArcadeAdmitted(_arcadeFormat)
                    && !_roomActionPending && _lobby?.IsConnected == true);
            }
            ShowRoomStatus(leader ? "Choose co-op or Versus, then pick fighters. Versus needs two or four players."
                : "The Room leader chooses the format.", false);
        }

        private void PickSlopball() => PickSlopballFormat(CoopFormat);
        private void PickVersus() => PickSlopballFormat(MatchFormats.SlopballVersusWire);
        private void PickSlopballFormat(string format)
        {
            if (!ArcadeAdmitted(format) || !ClientSession.IsLobbyHost) return;
            _arcadePick = true;
            _arcadeFormat = format;
            ApplyArcadeTabState();
        }

        private void ShowArcadeComing()
            => ShowRoomStatus("Not online yet. Pick a PVP stage for now; Slopball comes online first.", false);

        private static void SetModeChrome(string title, string progress)
        {
            var titleLabel = FrontendController.CurrentContext?.Q<Label>("title");
            if (titleLabel != null)
                titleLabel.text = title;
            var progressLabel = FrontendController.CurrentContext?.Q<Label>("flow-progress");
            if (progressLabel != null)
                progressLabel.text = progress;
        }

        /// <summary>
        /// Curated picker roster from Resources/Stages/roster.json, the single list the release
        /// also turns into Master's online admission. Other baked arenas remain available for
        /// compatibility and authoring until their rebuilds are ready.
        /// </summary>
        [Serializable]
        private sealed class StageRoster
        {
            public string[] picker = Array.Empty<string>();
            public string[] trainingOnly = Array.Empty<string>();

            public bool Offers(string arenaName, bool isTraining)
                => Array.IndexOf(picker, arenaName) >= 0 || (isTraining && Array.IndexOf(trainingOnly, arenaName) >= 0);

            public static StageRoster Load()
            {
                var asset = Resources.Load<TextAsset>("Stages/roster");
                if (asset == null)
                {
                    Debug.LogError("[StageSelect] Missing Resources/Stages/roster.json; no stages are offered.");
                    return new StageRoster();
                }
                return JsonUtility.FromJson<StageRoster>(asset.text) ?? new StageRoster();
            }
        }

        private static string DisplayName(ArenaDefinition arena)
        {
            if (!string.IsNullOrWhiteSpace(arena.DisplayName))
                return arena.DisplayName;
            if (string.IsNullOrWhiteSpace(arena.Name))
                return "UNKNOWN STAGE";

            string[] words = arena.Name.Split('_');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0) continue;
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            }
            return string.Join(" ", words);
        }

        private static void BackToFighterSelect()
            => FrontendController.Show(FrontendPage.FighterSelect);

        private void BackToRoom() => FrontendController.Show(FrontendPage.LobbyRoom);
        private void BindRoomLobby(LobbyClient lobby)
        {
            if (lobby == null) return;
            lobby.RoomUpdated += OnRoomUpdated;
            lobby.RoomDeleted += OnRoomDeleted;
            lobby.RoomMembershipRevoked += OnRoomMembershipRevoked;
            lobby.Connected += OnRoomConnected;
            lobby.Disconnected += OnRoomDisconnected;
            lobby.Error += OnRoomError;
        }

        private void UnbindRoomLobby(LobbyClient lobby)
        {
            lobby.RoomUpdated -= OnRoomUpdated;
            lobby.RoomDeleted -= OnRoomDeleted;
            lobby.RoomMembershipRevoked -= OnRoomMembershipRevoked;
            lobby.Connected -= OnRoomConnected;
            lobby.Disconnected -= OnRoomDisconnected;
            lobby.Error -= OnRoomError;
        }

        private void OnActiveLobbyChanged(LobbyClient lobby)
        {
            if (!_roomMode || !isActiveAndEnabled || ReferenceEquals(_lobby, lobby))
                return;
            if (_lobby != null) UnbindRoomLobby(_lobby);
            _lobby = lobby;
            _roomSnapshot = null;
            _selectedArena = "";
            _roomActionPending = false;
            ClientSession.IsLobbyHost = false;
            BindRoomLobby(lobby);
            RenderPlayerCards();
            if (_lblSelectedStage != null) _lblSelectedStage.text = "RESTORING ROOM ARENA…";
            _btnConfirm?.style.SetDisplay(false);
            ApplyRoomArenaAllowlist();
            _btnConfirm?.SetEnabled(false);
            ShowRoomStatus(lobby == null
                ? "Room authentication expired. Reconnecting…"
                : "Restoring Room state…", false);
            if (lobby != null) _ = RefreshRoom();
        }

        private void OnRoomDeleted(Guid roomId)
        {
            if (roomId == ClientSession.SelectedRoomId)
                ReturnToRoomBrowser("This room has closed.");
        }

        private void OnRoomMembershipRevoked(Guid roomId)
        {
            if (roomId == ClientSession.SelectedRoomId)
                ReturnToRoomBrowser("Your Room membership ended.");
        }

        private void ReturnToRoomBrowser(string notice)
        {
            ClientSession.SelectedRoomId = Guid.Empty;
            ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
            ServerBrowserUI.PendingReturnNotice = notice;
            FrontendController.Show(FrontendPage.ServerBrowser);
        }

        private async System.Threading.Tasks.Task RefreshRoom()
        {
            var lobby = _lobby;
            if (lobby == null) return;
            try
            {
                if (!lobby.IsConnected && !await lobby.ConnectAsync()) return;
                var room = await lobby.GetMyRoomAsync();
                if (!isActiveAndEnabled || !ReferenceEquals(_lobby, lobby)) return;
                if (room == null || room.Id != ClientSession.SelectedRoomId)
                    ReturnToRoomBrowser("You are no longer a member of this Room. Return to the browser and join another room.");
                else
                    OnRoomUpdated(room);
            }
            catch (Exception ex)
            {
                if (isActiveAndEnabled && ReferenceEquals(_lobby, lobby))
                    ShowRoomStatus($"Couldn’t restore Room state: {ex.Message}", true);
            }
        }

        private void OnRoomConnected() => _ = RefreshRoom();

        private void OnRoomDisconnected(Exception _)
        {
            if (isActiveAndEnabled)
            {
                ApplyRoomArenaAllowlist();
                _btnConfirm?.SetEnabled(false);
                ShowRoomStatus("Room connection dropped. Reconnecting…", true);
            }
        }

        private void OnRoomError(string message)
        {
            if (!isActiveAndEnabled) return;
            if (_lobby?.IsConnected != true)
            {
                ApplyRoomArenaAllowlist();
                _btnConfirm?.SetEnabled(false);
            }
            ShowRoomStatus($"Room connection error: {message}", true);
        }

        private void OnRoomUpdated(RoomSnapshot room)
        {
            if (!isActiveAndEnabled || room.Id != ClientSession.SelectedRoomId)
                return;
            if (string.Equals(room.Phase, "Lobby", StringComparison.Ordinal))
            {
                FrontendController.Show(FrontendPage.LobbyRoom);
                return;
            }
            if (string.Equals(room.Phase, "Character Select", StringComparison.Ordinal))
            {
                FrontendController.Show(FrontendPage.FighterSelect);
                return;
            }
            if (string.Equals(room.Phase, "Match Starting", StringComparison.Ordinal) ||
                string.Equals(room.Phase, "In Match", StringComparison.Ordinal))
            {
                // Character Select is the last preparation page and shows the launch.
                FrontendController.Show(FrontendPage.FighterSelect);
                return;
            }
            _roomSnapshot = room;
            _roomActionPending = _confirmInFlight;
            ClientSession.IsLobbyHost = IsLocalRoomLeader(room);
            if (_context.Q<Label>("subtitle") is { } subtitle)
                subtitle.text = ClientSession.IsLobbyHost ? "CHOOSE WHAT THE ROOM PLAYS" : "THE ROOM LEADER IS CHOOSING WHAT TO PLAY";
            if (_context.Q<Label>("lbl-host") is { } hostLabel)
                hostLabel.text = ClientSession.IsLobbyHost ? "ROOM LEADER CHOOSES" : "WAITING FOR ROOM LEADER";
            ApplyRoomArenaAllowlist();
            RenderPlayerCards();
            if (_btnConfirm != null)
            {
                _btnConfirm.style.display = DisplayStyle.Flex;
                _btnConfirm.text = ClientSession.IsLobbyHost ? "PICK FIGHTERS" : "WAITING FOR LEADER";
            }
            if (IsSlopballPick(room))
            {
                // The Room's pick is Arcade: everyone sees it on the ARCADE tab.
                if (!_arcadeTab) SetTabVisuals(true);
                if (ClientSession.IsLobbyHost) { _arcadePick = true; _arcadeFormat = room.Format; }
                ApplyArcadeTabState();
                return;
            }
            if (!string.IsNullOrWhiteSpace(room.ArenaName) &&
                _grid?.Q<VisualElement>($"stage-{room.ArenaName}") != null)
            {
                // The leader confirmed this arena; Character Select opens next. A leader who
                // has since picked another card keeps it until they confirm again.
                bool leaderPickedAnother = ClientSession.IsLobbyHost && !string.IsNullOrEmpty(_selectedArena) &&
                    !string.Equals(_selectedArena, room.ArenaName, StringComparison.OrdinalIgnoreCase);
                if (!leaderPickedAnother)
                    SelectStage(room.ArenaName);
                if (_lblSelectedStage != null)
                    _lblSelectedStage.text = leaderPickedAnother
                        ? $"ARENA TO CONFIRM: {DisplayArenaName(_selectedArena)}"
                        : $"ROOM ARENA: {DisplayArenaName(room.ArenaName)}";
                if (_btnConfirm != null)
                    _btnConfirm.text = ClientSession.IsLobbyHost ? "PICK FIGHTERS" : "ARENA CHOSEN";
                _btnConfirm?.SetEnabled(ClientSession.IsLobbyHost && !_roomActionPending &&
                    _lobby?.IsConnected == true);
                ShowRoomStatus(ClientSession.IsLobbyHost
                    ? "Arena confirmed. Pick fighters when the Room is ready."
                    : $"The Room leader chose {DisplayArenaName(room.ArenaName)}.", false);
            }
            else if (string.IsNullOrWhiteSpace(room.ArenaName))
            {
                if (_lblSelectedStage != null)
                    _lblSelectedStage.text = string.IsNullOrEmpty(_selectedArena)
                        ? "ROOM LEADER HAS NOT CHOSEN AN ARENA"
                        : $"ARENA TO CONFIRM: {DisplayArenaName(_selectedArena)}";
                _btnConfirm?.SetEnabled(ClientSession.IsLobbyHost && !string.IsNullOrEmpty(_selectedArena) &&
                    !_roomActionPending && _lobby?.IsConnected == true);
                ShowRoomStatus(string.IsNullOrEmpty(_selectedArena)
                    ? ClientSession.IsLobbyHost ? "Choose an admitted arena, then confirm it." : "Waiting for the Room leader to choose an arena."
                    : "Confirm the selected arena to share it with the Room.", false);
            }
            else
            {
                if (_lblSelectedStage != null)
                    _lblSelectedStage.text = $"ROOM ARENA: {DisplayArenaName(room.ArenaName)}";
                _btnConfirm?.SetEnabled(false);
                ShowRoomStatus($"The chosen arena ({room.ArenaName}) is not available in this client build.", true);
            }
            if (_arcadeTab) ApplyArcadeTabState();
        }


        private static bool IsLocalRoomLeader(RoomSnapshot room)
        {
            var members = room.Members ?? Array.Empty<RoomMemberInfo>();
            for (int i = 0; i < members.Length; i++)
                if (members[i].SteamId == ClientSession.SteamId)
                    return members[i].IsLeader;
            return false;
        }

        private void ApplyRoomArenaAllowlist()
        {
            var admitted = _roomSnapshot?.AdmittedArenas ?? Array.Empty<string>();
            if (_grid == null) return;
            foreach (var element in _grid.Children())
            {
                if (element is not Button button || !button.name.StartsWith("stage-", StringComparison.Ordinal))
                    continue;
                string arenaName = button.name.Substring("stage-".Length);
                bool allowed = Array.Exists(admitted,
                    name => string.Equals(name, arenaName, StringComparison.OrdinalIgnoreCase));
                button.style.display = allowed ? DisplayStyle.Flex : DisplayStyle.None;
                button.SetEnabled(allowed && ClientSession.IsLobbyHost && !_roomActionPending &&
                    _lobby?.IsConnected == true);
                if (!allowed && _selectedArena == arenaName)
                {
                    _selectedArena = "";
                    if (_lblSelectedStage != null) _lblSelectedStage.text = "CHOOSE AN ADMITTED ARENA";
                    _btnConfirm?.SetEnabled(false);
                }
            }
            if (admitted.Length == 0 && _lblWaiting != null)
            {
                _lblWaiting.text = "Master has not admitted any arenas for this Room.";
                _lblWaiting.style.display = DisplayStyle.Flex;
            }
        }

        private string DisplayArenaName(string arenaName)
        {
            ArenaDefinition? arena = ArenaRegistry.Get(arenaName);
            return arena.HasValue ? DisplayName(arena.Value).ToUpperInvariant() : arenaName.ToUpperInvariant();
        }

        private void ShowRoomStatus(string message, bool error)
        {
            if (_lblWaiting == null) return;
            _lblWaiting.text = message;
            _lblWaiting.style.display = DisplayStyle.Flex;
            if (error) _lblWaiting.AddToClassList("error");
            else _lblWaiting.RemoveFromClassList("error");
        }

        /// <summary>Confirms the leader's arena with Master, then opens Character Select.</summary>
        private async void ConfirmRoomPick()
        {
            var lobby = _lobby;
            bool arcade = _arcadeTab;
            string format = arcade ? _arcadeFormat : MatchFormats.StockWire;
            string arena = arcade ? SlopballArena : _selectedArena;
            Guid roomId = ClientSession.SelectedRoomId;
            if (_roomActionPending || lobby == null || _roomSnapshot?.Id != roomId ||
                !ClientSession.IsLobbyHost || !lobby.IsConnected || string.IsNullOrEmpty(arena) ||
                arcade && (!_arcadePick || !ArcadeAdmitted(_arcadeFormat)))
                return;
            _confirmInFlight = true;
            _roomActionPending = true;
            ApplyRoomArenaAllowlist();
            _btnConfirm?.SetEnabled(false);
            ShowRoomStatus("Confirming the arena with Master…", false);
            try
            {
                if (!string.Equals(_roomSnapshot.ArenaName, arena, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_roomSnapshot.Format, format, StringComparison.Ordinal))
                    await lobby.RoomChoosePickAsync(format, arena);
                if (!isActiveAndEnabled || !ReferenceEquals(_lobby, lobby) || ClientSession.SelectedRoomId != roomId)
                    return;
                ShowRoomStatus("Opening Character Select…", false);
                var room = await lobby.RoomStartCharacterSelectAsync();
                _confirmInFlight = false;
                if (isActiveAndEnabled && ReferenceEquals(_lobby, lobby))
                    OnRoomUpdated(room);
            }
            catch (Exception ex)
            {
                _confirmInFlight = false;
                if (!isActiveAndEnabled || !ReferenceEquals(_lobby, lobby) || ClientSession.SelectedRoomId != roomId)
                    return;
                _roomActionPending = false;
                ApplyRoomArenaAllowlist();
                _btnConfirm?.SetEnabled(ClientSession.IsLobbyHost && !string.IsNullOrEmpty(_selectedArena) &&
                    _lobby?.IsConnected == true);
                ShowRoomStatus($"Master didn't accept the pick: {ex.Message}", true);
            }
        }

        private void RenderPlayerCards()
        {
            if (_playerCards == null) return;
            _playerCards.Clear();

            if (_roomMode)
            {
                var members = _roomSnapshot?.Members ?? Array.Empty<RoomMemberInfo>();
                for (int i = 0; i < members.Length; i++)
                {
                    var member = members[i];
                    bool hasCharacter = Enum.TryParse(member.CharacterSelection, true, out CharacterClass selectedClass);
                    _playerCards.Add(BuildPlayerCard(
                        $"P{i + 1}", member.Name,
                        hasCharacter ? selectedClass : (CharacterClass?)null,
                        member.LockedIn ? "LOCKED" : "WAITING",
                        member.SteamId == ClientSession.SteamId,
                        member.IsLeader));
                }
                return;
            }


            if (MatchConfig.Mode is GameMode.Solo or GameMode.Training)
            {
                _playerCards.Add(BuildPlayerCard(
                    "P1", "YOU", MatchConfig.PlayerClass, "READY", true, true));
                _playerCards.Add(BuildPlayerCard(
                    "P2", "CPU", MatchConfig.SoloBotClass,
                    $"CPU {BotDifficultyProfile.DisplayName(MatchConfig.SoloCpuDifficulty)}",
                    false, false));
                return;
            }

            var players = ClientSession.LobbyRoster?.Players;
            if (players == null) return;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                CharacterClass selectedClass;
                bool hasCharacter = System.Enum.TryParse(player.CharacterSelection, true, out selectedClass);
                _playerCards.Add(BuildPlayerCard(
                    $"P{i + 1}",
                    player.Name,
                    hasCharacter ? selectedClass : (CharacterClass?)null,
                    hasCharacter && player.LockedIn ? "LOCKED" : "WAITING",
                    player.SteamId == ClientSession.SteamId,
                    player.IsHost));
            }
        }

        private VisualElement BuildPlayerCard(
            string playerNumber,
            string playerName,
            CharacterClass? selectedClass,
            string statusText,
            bool local,
            bool host)
        {
            var card = new VisualElement();
            card.AddToClassList("player-card");
            if (local) card.AddToClassList("player-card--local");

            var identity = new VisualElement();
            identity.AddToClassList("player-card__identity");
            var number = new Label(playerNumber);
            number.AddToClassList("player-card__number");
            identity.Add(number);
            var role = new Label(_roomMode
                ? (host ? "LEADER" : "MEMBER")
                : MatchConfig.Mode == GameMode.PvP
                    ? (host ? "HOST" : "PLAYER")
                    : (local ? "PLAYER" : "CPU"));
            role.AddToClassList("player-card__host");
            identity.Add(role);
            card.Add(identity);

            var name = new Label(playerName) { enableRichText = false };
            name.AddToClassList("player-card__name");
            card.Add(name);

            if (selectedClass.HasValue)
            {
                var portrait = new VisualElement();
                portrait.AddToClassList("player-card__portrait");
                var texture = Resources.Load<Texture2D>($"UI/Portraits/{selectedClass.Value}");
                if (texture != null)
                    portrait.style.backgroundImage = new StyleBackground(texture);
                card.Add(portrait);

                var character = new Label(selectedClass.Value.ToString().ToUpper());
                character.AddToClassList("player-card__character");
                card.Add(character);
            }

            var status = new Label(statusText);
            status.AddToClassList("player-card__status");
            status.AddToClassList(statusText == "LOCKED" || statusText == "BOT" || statusText == "READY"
                ? "player-card__status--locked"
                : "player-card__status--picking");
            card.Add(status);
            return card;
        }

        private void OnConfirmClicked()
        {
            if (_roomMode && _arcadeTab)
            {
                if (_arcadePick) ConfirmRoomPick();
                return;
            }
            if (string.IsNullOrEmpty(_selectedArena))
            {
                if (_lblWaiting != null)
                {
                    _lblWaiting.text = _roomMode ? "Choose an admitted arena before confirming." : "Choose a stage before starting.";
                    _lblWaiting.style.display = DisplayStyle.Flex;
                }
                return;
            }
            if (_roomMode)
            {
                ConfirmRoomPick();
                return;
            }

            MatchConfig.ArenaName = _selectedArena;
            if (MatchConfig.Mode != GameMode.PvP)
            {
                SceneManager.LoadScene("Arena_Offline");
                return;
            }
            _btnConfirm?.SetEnabled(false);
            if (_lblWaiting != null)
            {
                _lblWaiting.text = "STARTING MATCH…";
                _lblWaiting.style.display = DisplayStyle.Flex;
            }
            if (_lobby == null)
            {
                Debug.LogError("[StageSelect] PvP mode but no lobby connection. Returning to server browser.");
                if (_lblWaiting != null)
                    _lblWaiting.text = "No lobby connection. Returning to server browser.";
                FrontendController.Show(FrontendPage.ServerBrowser);
                return;
            }
            _ = _lobby.StartMatchAsync(_selectedArena);
        }

        private void OnMatchStarted(MatchStartedConfig config)
        {
            if (config.RoomId != null)
                return;

            Debug.Log($"[StageSelect] Match started: {config.Players.Count} players, port={config.MatchPort}, arena={config.ArenaName}.");
            if (_lobby != null)
            {
                _lobby.MatchStarted -= OnMatchStarted;
                _lobby.Error -= OnError;
            }
            ClientSession.ApplyMatchStarted(config);
        }

        private void OnError(string message)
        {
            if (_roomMode)
            {
                _roomActionPending = false;
                ApplyRoomArenaAllowlist();
                if (_btnConfirm != null)
                    _btnConfirm.SetEnabled(ClientSession.IsLobbyHost && !string.IsNullOrEmpty(_selectedArena) &&
                        _lobby?.IsConnected == true);
                ShowRoomStatus($"Master rejected the pick: {message}", true);
                return;
            }
            _btnConfirm?.SetEnabled(true);
            if (_lblWaiting != null)
            {
                _lblWaiting.text = $"COULD NOT START MATCH: {message}";
                _lblWaiting.style.display = DisplayStyle.Flex;
            }
            Debug.LogWarning($"[StageSelect] PvP error: {message}");
        }
        private void SelectStage(string name)
        {
            if (MatchConfig.Mode == GameMode.PvP && !ClientSession.IsLobbyHost)
                return;

            _selectedArena = name;
            if (!_roomMode) MatchConfig.ArenaName = name;
            var grid = _grid;
            if (grid != null)
            {
                foreach (var card in grid.Children())
                {
                    card.RemoveFromClassList("stage-card--selected");
                    if (card.name == $"stage-{name}")
                        card.AddToClassList("stage-card--selected");
                }
            }

            ArenaDefinition? arena = ArenaRegistry.Get(name);
            string label = arena.HasValue ? DisplayName(arena.Value) : name.Replace('_', ' ');
            if (_lblSelectedStage != null)
                _lblSelectedStage.text = _roomMode
                    ? $"ARENA TO CONFIRM: {label.ToUpperInvariant()}"
                    : $"STAGE SELECTED: {label.ToUpperInvariant()}";
            if (_lblWaiting != null)
                _lblWaiting.style.display = DisplayStyle.None;
            if (_btnConfirm != null)
            {
                _btnConfirm.style.display = DisplayStyle.Flex;
                if (_roomMode)
                    _btnConfirm.text = "PICK FIGHTERS";
                _btnConfirm.SetEnabled(!_roomMode || ClientSession.IsLobbyHost &&
                    !_roomActionPending && _lobby?.IsConnected == true);
            }
        }
    }
}
