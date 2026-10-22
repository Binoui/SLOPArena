using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using SlopArena.Client.Network;
using SlopArena.Client.World;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Fighter Select page (issue #219): a fragment mounted into the
    /// FrontendShell hosts — portrait grid and selection brief in the body,
    /// participant cards and CPU editing in the lower-right summary, and the
    /// primary action outside the conversation cell. Training and Solo share
    /// player/CPU fighter and difficulty selection before stage selection:
    /// <list type="bullet">
    /// <item><b>Training</b> — enter Training with the selected NPC.</item>
    /// <item><b>Solo</b> — start a stock match against the selected CPU.</item>
    /// <item><b>PvP</b> — multiplayer via SignalR: all players pick simultaneously,
    /// lock in, and the host starts the match when everyone is locked in (min 2).</item>
    /// </list>
    /// </summary>
    public class CharSelectController : MonoBehaviour, IFrontendPageController
    {
        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;

        private CharacterClass _selected = CharacterClass.None;
        private bool _selectingCpu;
        private readonly List<Button> _gridButtons = new();
        private Button _btnEditPlayer;
        private Button _btnEditCpu;
        private Label _selectionTarget;

        // PvP state
        private LobbyClient _lobby;
        private VisualElement _rosterPanel;
        private Button _btnLockIn;
        private Button _btnStartMatch;
        private Label _lblPvPStatus;
        private bool _lockedIn;
        private LobbySnapshot _snapshot;
        private RoomSnapshot _roomSnapshot;
        private bool _roomMode;
        private ChatSession _chatSession;
        private bool _roomActionPending;
        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void OnEnable()
        {
            _roomMode = false;
            _roomSnapshot = null;
            _roomActionPending = false;
            _lockedIn = false;
            _context?.Q<VisualElement>("camp-select")?.style.SetDisplay(false);
            _gridButtons.Clear();
            _selectingCpu = false;
            if (_context == null)
            {
                Debug.LogError("[CharSelectController] No page context was injected; fighter select stays blank.");
                enabled = false;
                return;
            }
            var grid = _context.Q<VisualElement>("char-grid");
            grid?.Clear();

            CharacterClass[] classes = MenuRoster.Classes;
            _selected = classes.Contains(MatchConfig.PlayerClass)
                ? MatchConfig.PlayerClass
                : classes.Length > 0 ? classes[0] : CharacterClass.None;
            if ((MatchConfig.Mode is GameMode.Solo or GameMode.Training) &&
                classes.Length > 0 &&
                !classes.Contains(MatchConfig.SoloBotClass))
                MatchConfig.SoloBotClass = classes[0];

            // Build portrait cards in the admitted manifest's authored order.
            if (grid != null)
            {
                foreach (var cls in classes)
                {
                    var capturedCls = cls;
                    var btn = new Button(() => SelectCharacter(capturedCls, _context))
                    {
                        name = $"char-{cls}"
                    };
                    btn.AddToClassList("char-card");

                    var portrait = new VisualElement { name = "char-portrait" };
                    portrait.AddToClassList("char-portrait");
                    var texture = Resources.Load<Texture2D>($"UI/Portraits/{cls}");
                    if (texture != null)
                        portrait.style.backgroundImage = new StyleBackground(texture);

                    var name = new Label(cls.ToString().ToUpperInvariant()) { name = "char-card-name" };
                    name.AddToClassList("char-card-name");
                    var check = new VisualElement { pickingMode = PickingMode.Ignore };
                    check.AddToClassList("char-card-check");
                    var markers = new VisualElement { name = "char-markers" };
                    markers.AddToClassList("char-markers");

                    btn.Add(portrait);
                    btn.Add(name);
                    btn.Add(check);
                    btn.Add(markers);
                    grid.Add(btn);
                    _gridButtons.Add(btn);
                }
            }

            if (_selected != CharacterClass.None)
                SelectCharacter(_selected, _context);

            if (MatchConfig.Mode == GameMode.PvP &&
                ClientSession.SelectedOnlineMode == ClientSession.OnlineSelection.Room)
                InitRoom(_context);
            else if (MatchConfig.Mode == GameMode.PvP)
                InitPvP(_context);
            else if (MatchConfig.Mode == GameMode.Tutorial)
                InitTutorial(_context);
            else
                InitLocal(_context);
        }

        /// <summary>The tutorial picks only the player's fighter; its stage and dummy are fixed.</summary>
        private void InitTutorial(FrontendPageContext context)
        {
            SetModeChrome(context, "TUTORIAL // SELECT YOUR FIGHTER", "ONE STEP  /  FIGHTER");
            ApplyLocalSelectLayout(context);
            context.Q<VisualElement>("page-summary")?.AddToClassList("char-summary-section--solo");
            context.Q<VisualElement>("pvp-action-area")?.style.SetDisplay(false);
            context.Q<VisualElement>("solo-config")?.style.SetDisplay(false);
            context.Q<VisualElement>("roster-panel")?.style.SetDisplay(false);
            var rosterMeta = context.Q<Label>("roster-meta");
            if (rosterMeta != null)
                rosterMeta.text = MenuRoster.Classes.Length == 0
                    ? "NO ADMITTED FIGHTERS"
                    : $"{MenuRoster.Classes.Length} FIGHTERS // TUTORIAL";

            var startButton = context.Q<Button>("btn-select");
            if (startButton != null)
            {
                startButton.style.display = DisplayStyle.Flex;
                startButton.text = "START TUTORIAL";
                startButton.SetEnabled(MenuRoster.Classes.Length > 0);
                startButton.clicked += () =>
                {
                    if (_selected == CharacterClass.None) return;
                    MatchConfig.Mode = GameMode.Tutorial;
                    MatchConfig.ArenaName = TutorialMatch.Layout.ArenaKey;
                    MatchConfig.IsHost = true;
                    SceneManager.LoadScene(TutorialMatch.SceneName);
                };
            }

            var btnBack = context.Q<Button>("btn-back");
            Action back = () => FrontendController.Show(FrontendPage.Home);
            if (btnBack != null)
                btnBack.clicked += back;
            ConfigureNavigation(context, _gridButtons.FirstOrDefault(b => b.name == $"char-{_selected}")
                ?? _gridButtons.FirstOrDefault(), btnBack, back, startButton);

            if (MenuRoster.Classes.Length == 0)
                ShowRosterUnavailable(context, "NO ADMITTED FIGHTERS", "Cooked fighter content is unavailable. Return to the menu.");
        }

        private void InitLocal(FrontendPageContext context)
        {
            bool training = MatchConfig.Mode == GameMode.Training;
            SetModeChrome(context, training ? "TRAINING // SELECT YOUR FIGHTER" : "SOLO // SELECT YOUR FIGHTER",
                "STEP 1 OF 2  /  FIGHTERS");
            ApplyLocalSelectLayout(context);
            context.Q<VisualElement>("page-summary")?.AddToClassList("char-summary-section--solo");
            // The active card and editing panel already name this fighter;
            // keep the kit brief without a duplicate display line.
            context.Q<Label>("char-name")?.style.SetDisplay(false);
            var rosterMeta = context.Q<Label>("roster-meta");
            if (rosterMeta != null)
                rosterMeta.text = MenuRoster.Classes.Length == 0
                    ? "NO ADMITTED FIGHTERS"
                    : $"{MenuRoster.Classes.Length} FIGHTERS // {(training ? "TRAINING" : "SOLO")}";
            // Local modes show both participant cards and CPU editing.
            context.Q<VisualElement>("pvp-action-area")?.style.SetDisplay(false);

            var selectButton = context.Q<Button>("btn-select");
            if (selectButton != null)
            {
                selectButton.style.display = DisplayStyle.Flex;
                selectButton.text = "SELECT STAGE";
                selectButton.SetEnabled(MenuRoster.Classes.Length > 0);
            }

            _rosterPanel = context.Q<VisualElement>("roster-panel");

            var config = context.Q<VisualElement>("solo-config");
            if (config != null)
                config.style.display = DisplayStyle.Flex;

            _btnEditPlayer = context.Q<Button>("btn-edit-player");
            _btnEditCpu = context.Q<Button>("btn-edit-cpu");
            _selectionTarget = context.Q<Label>("selection-target");
            if (_btnEditPlayer != null)
                _btnEditPlayer.clicked += () => SetSelectionTarget(context, selectingCpu: false);
            if (_btnEditCpu != null)
                _btnEditCpu.clicked += () => SetSelectionTarget(context, selectingCpu: true);
            SetSelectionTarget(context, selectingCpu: false);

            MatchConfig.SoloCpuDifficulty = BotDifficultyProfile.Normalize(MatchConfig.SoloCpuDifficulty);
            foreach (CpuDifficulty difficulty in (CpuDifficulty[])Enum.GetValues(typeof(CpuDifficulty)))
            {
                var capturedDifficulty = difficulty;
                var button = context.Q<Button>($"btn-cpu-difficulty-{difficulty.ToString().ToLowerInvariant()}");
                if (button == null) continue;
                button.clicked += () =>
                {
                    MatchConfig.SoloCpuDifficulty = capturedDifficulty;
                    UpdateDifficultyButtons(context);
                };
            }
            UpdateDifficultyButtons(context);

            if (selectButton != null)
            {
                selectButton.clicked += () =>
                {
                    if (_selected == CharacterClass.None) return;
                    FrontendController.Show(FrontendPage.StageSelect);
                };
            }

            var btnBack = context.Q<Button>("btn-back");
            Action back = () => FrontendController.Show(FrontendPage.Home);
            if (btnBack != null)
                btnBack.clicked += back;
            ConfigureNavigation(context, MenuRoster.Classes.Length > 0
                ? _btnEditPlayer ?? _gridButtons.FirstOrDefault()
                : null, btnBack, back, selectButton);

            if (MenuRoster.Classes.Length == 0)
                ShowRosterUnavailable(context, "NO ADMITTED FIGHTERS", "Cooked fighter content is unavailable. Return to the menu.");
        }

        private static void ApplyLocalSelectLayout(FrontendPageContext context)
        {
            context.Q<VisualElement>("page-header")?.AddToClassList("char-header--local");
            var summary = context.Q<VisualElement>("page-summary");
            var nextAction = context.Q<VisualElement>("solo-next-action");
            var stepStatus = context.Q<VisualElement>("char-header-status");
            if (summary == null || nextAction == null || stepStatus == null) return;

            summary.AddToClassList("char-summary-section--local");
            summary.Add(nextAction);
            nextAction.Insert(0, stepStatus);
        }

        private static void SetModeChrome(FrontendPageContext context, string title, string progress)
        {
            var titleLabel = context.Q<Label>("title");
            if (titleLabel != null)
                titleLabel.text = title;
            var progressLabel = context.Q<Label>("flow-progress");
            if (progressLabel != null)
                progressLabel.text = progress;
        }

        private static void ConfigureNavigation(
            FrontendPageContext context,
            Button? initial,
            Button? backButton,
            Action back,
            Button? forward = null)
        {
            MenuNavigation.Configure(context, initial ?? backButton, back, forward);
        }

        private static void ShowRosterUnavailable(FrontendPageContext context, string heading, string detail)
        {
            var nameLabel = context.Q<Label>("char-name");
            if (nameLabel != null)
                nameLabel.text = heading;
            var roleLabel = context.Q<Label>("char-role");
            if (roleLabel != null)
                roleLabel.text = detail;
            var statusLabel = context.Q<Label>("lbl-pvp-status");
            if (statusLabel != null)
                statusLabel.text = detail;
        }

        private static void UpdateDifficultyButtons(FrontendPageContext context)
        {
            CpuDifficulty current = BotDifficultyProfile.Normalize(MatchConfig.SoloCpuDifficulty);
            foreach (CpuDifficulty difficulty in (CpuDifficulty[])Enum.GetValues(typeof(CpuDifficulty)))
            {
                var button = context.Q<Button>($"btn-cpu-difficulty-{difficulty.ToString().ToLowerInvariant()}");
                button?.EnableInClassList("active", difficulty == current);
            }
        }

        private void SetSelectionTarget(FrontendPageContext context, bool selectingCpu, bool focusSlot = false)
        {
            if (MatchConfig.Mode is not (GameMode.Solo or GameMode.Training))
                return;

            _selectingCpu = selectingCpu;
            _selected = selectingCpu ? MatchConfig.SoloBotClass : MatchConfig.PlayerClass;
            if (_selectionTarget != null)
                _selectionTarget.text = selectingCpu ? "EDITING P2 // CPU" : "EDITING P1 // PLAYER";
            _btnEditPlayer?.EnableInClassList("active", !selectingCpu);
            _btnEditCpu?.EnableInClassList("active", selectingCpu);
            context.Q<VisualElement>("solo-difficulty-buttons")?.style.SetDisplay(selectingCpu);
            UpdateSelectionVisuals(_selected, context);
            RenderSoloRoster();
            if (focusSlot)
                _rosterPanel?.Q<Button>(selectingCpu ? "solo-slot-p2" : "solo-slot-p1")?.Focus();
        }

        private void UpdateSelectionVisuals(CharacterClass cls, FrontendPageContext context)
        {
            foreach (var btn in _gridButtons)
            {
                btn.RemoveFromClassList("char-card--selected");
                if (btn.name == $"char-{cls}")
                    btn.AddToClassList("char-card--selected");
            }

            var nameLabel = context.Q<Label>("char-name");
            if (nameLabel != null)
                nameLabel.text = cls.ToString().ToUpperInvariant();
            var roleLabel = context.Q<Label>("char-role");
            if (roleLabel != null)
                roleLabel.text = MenuRoster.Description(cls);
        }

        private void RenderSoloRoster()
        {
            if (_rosterPanel == null) return;
            _rosterPanel.Clear();
            if (MenuRoster.Classes.Length == 0)
                return;

            string playerCardName = $"char-{MatchConfig.PlayerClass}";
            string cpuCardName = $"char-{MatchConfig.SoloBotClass}";
            foreach (var button in _gridButtons)
            {
                var markers = button.Q<VisualElement>("char-markers");
                markers.Clear();
                bool playerSelected = button.name == playerCardName;
                bool cpuSelected = button.name == cpuCardName;
                if (!playerSelected && !cpuSelected) continue;
                var marker = new Label(playerSelected && cpuSelected ? "P1 / P2" : playerSelected ? "P1" : "P2");
                marker.AddToClassList("char-marker");
                marker.AddToClassList("char-marker--local");
                markers.Add(marker);
            }

            var player = (Button)BuildPlayerCard(
                "P1", "YOU", MatchConfig.PlayerClass,
                _selectingCpu ? "" : "EDITING FIGHTER", local: true, host: true);
            player.name = "solo-slot-p1";
            player.clicked += () => SetSelectionTarget(_context, selectingCpu: false, focusSlot: true);
            _rosterPanel.Add(player);

            var cpu = (Button)BuildPlayerCard(
                "P2", "CPU", MatchConfig.SoloBotClass,
                _selectingCpu ? "EDITING FIGHTER" : "", local: false, host: false);
            cpu.name = "solo-slot-p2";
            cpu.clicked += () => SetSelectionTarget(_context, selectingCpu: true, focusSlot: true);
            _rosterPanel.Add(cpu);
        }
        private void InitRoom(FrontendPageContext context)
        {
            _roomMode = true;
            SetModeChrome(context, "ROOM // CHOOSE YOUR FIGHTER", "STEP 2 OF 2  /  FIGHTER");
            context.Q<Button>("btn-select")?.style.SetDisplay(false);
            context.Q<VisualElement>("solo-config")?.style.SetDisplay(false);
            _rosterPanel = context.Q<VisualElement>("roster-panel");
            _btnLockIn = context.Q<Button>("btn-lockin");
            _btnStartMatch = context.Q<Button>("btn-start-match");
            _lblPvPStatus = context.Q<Label>("lbl-pvp-status");
            _btnLockIn?.SetEnabled(false);
            _btnStartMatch?.SetEnabled(false);
            _btnLockIn?.style.SetDisplay(true);
            _btnStartMatch?.style.SetDisplay(false);
            if (_btnLockIn != null)
            {
                _btnLockIn.text = "LOCK IN";
                _btnLockIn.clicked += OnRoomLockInClicked;
            }
            if (_btnStartMatch != null)
            {
                _btnStartMatch.text = "START MATCH";
                _btnStartMatch.clicked += OnRoomStartMatchClicked;
            }
            var back = context.Q<Button>("btn-back");
            if (back != null) back.clicked += OnRoomBackClicked;
            if (context.Q<Button>("btn-camp-teal") is { } teal) teal.clicked += SelectTealCamp;
            if (context.Q<Button>("btn-camp-clay") is { } clay) clay.clicked += SelectClayCamp;
            ConfigureNavigation(context, _btnLockIn, back, OnRoomBackClicked);

            _chatSession = ChatSession.Instance;
            if (_chatSession != null)
                _chatSession.ActiveLobbyChanged += OnActiveLobbyChanged;
            _lobby = _chatSession?.ActiveLobby ?? ClientSession.ActiveLobby;
            BindRoomLobby(_lobby);
            for (int i = 0; i < _gridButtons.Count; i++)
                _gridButtons[i].SetEnabled(false);
            SetRoomStatus("Loading Room state…", false);
            if (_lobby == null)
                FrontendController.Show(FrontendPage.LobbyRoom);
            else
                _ = RefreshRoom();
        }
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
            _roomActionPending = false;
            _lockedIn = false;
            ClientSession.IsLobbyHost = false;
            BindRoomLobby(lobby);
            RenderRoomRoster();
            RenderRoomCardMarkers();
            for (int i = 0; i < _gridButtons.Count; i++)
                _gridButtons[i].SetEnabled(false);
            _btnLockIn?.SetEnabled(false);
            _btnStartMatch?.SetEnabled(false);
            SetRoomStatus(lobby == null
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
                    SetRoomStatus($"Couldn’t restore Room state: {ex.Message}", true);
            }
        }

        private void OnRoomConnected() => _ = RefreshRoom();

        private void OnRoomDisconnected(Exception _)
        {
            if (isActiveAndEnabled)
            {
                UpdateRoomControls();
                SetRoomStatus("Room connection dropped. Reconnecting…", true);
            }
        }

        private void OnRoomError(string message)
        {
            if (isActiveAndEnabled) SetRoomStatus(message, true);
        }

        private void OnRoomUpdated(RoomSnapshot room)
        {
            if (!isActiveAndEnabled || room.Id != ClientSession.SelectedRoomId)
                return;
            _roomSnapshot = room;
            _roomActionPending = false;
            if (string.Equals(room.Phase, "Lobby", StringComparison.Ordinal))
            {
                FrontendController.Show(FrontendPage.LobbyRoom);
                return;
            }
            if (string.Equals(room.Phase, "Stage Select", StringComparison.Ordinal))
            {
                FrontendController.Show(FrontendPage.StageSelect);
                return;
            }
            ShowRoomArena(room);
            if (string.Equals(room.Phase, "Match Starting", StringComparison.Ordinal) ||
                string.Equals(room.Phase, "In Match", StringComparison.Ordinal))
            {
                // ChatSession enters the match when Master's MatchStarted arrives.
                _roomActionPending = true;
                RenderRoomRoster();
                UpdateRoomControls();
                if (_btnStartMatch != null) _btnStartMatch.text = "MATCH STARTING";
                SetRoomStatus(room.Phase == "In Match"
                    ? "Match host allocated. Waiting for the Room match descriptor…"
                    : "Master is preparing the Room match…", false);
                return;
            }
            ApplyRoomCharacterAllowlist();
            RenderRoomRoster();
            UpdateRoomControls();
        }

        /// <summary>The Room chose its arena in Stage Select; show it while fighters are picked.</summary>
        private void ShowRoomArena(RoomSnapshot room)
        {
            if (string.IsNullOrWhiteSpace(room.ArenaName)) return;
            ArenaDefinition? arena = ArenaRegistry.Get(room.ArenaName);
            string name = room.Format == MatchFormats.SlopballVersusWire ? "Slopball versus · First to 21"
                : string.Equals(room.Format, MatchFormats.SlopballCoopWire, StringComparison.Ordinal)
                ? "Slopball co-op"
                : arena.HasValue && !string.IsNullOrWhiteSpace(arena.Value.DisplayName)
                    ? arena.Value.DisplayName
                    : room.ArenaName.Replace('_', ' ');
            SetModeChrome(_context, "ROOM // CHOOSE YOUR FIGHTER", $"STEP 2 OF 2  /  {name.ToUpperInvariant()}");
        }

        private void ApplyRoomCharacterAllowlist()
        {
            var admitted = _roomSnapshot?.AdmittedCharacters ?? Array.Empty<string>();
            CharacterClass firstAllowed = CharacterClass.None;
            for (int i = 0; i < _gridButtons.Count; i++)
            {
                var button = _gridButtons[i];
                string characterName = button.name.Substring("char-".Length);
                bool allowed = Array.Exists(admitted,
                    name => string.Equals(name, characterName, StringComparison.OrdinalIgnoreCase));
                button.style.display = allowed ? DisplayStyle.Flex : DisplayStyle.None;
                button.SetEnabled(allowed && !_lockedIn && !_roomActionPending);
                if (allowed && firstAllowed == CharacterClass.None &&
                    Enum.TryParse(characterName, true, out CharacterClass parsed))
                    firstAllowed = parsed;
            }
            if (_selected == CharacterClass.None ||
                !Array.Exists(admitted, name => string.Equals(name, _selected.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                _selected = firstAllowed;
                if (_selected != CharacterClass.None)
                    SelectCharacter(_selected, _context);
            }
            if (admitted.Length == 0)
                SetRoomStatus("Master has not admitted any characters for this Room.", true);
        }

        private void RenderRoomRoster()
        {
            if (_rosterPanel == null) return;
            _rosterPanel.Clear();
            var members = _roomSnapshot?.Members ?? Array.Empty<RoomMemberInfo>();
            for (int i = 0; i < members.Length; i++)
            {
                var member = members[i];
                bool hasCharacter = Enum.TryParse(member.CharacterSelection, true, out CharacterClass cls);
                _rosterPanel.Add(BuildPlayerCard(
                    string.IsNullOrEmpty(member.Camp) ? $"P{i + 1}" : $"P{i + 1} · {member.Camp.ToUpperInvariant()}", member.Name,
                    hasCharacter ? cls : (CharacterClass?)null,
                    member.LockedIn ? "LOCKED" : "WAITING",
                    member.SteamId == ClientSession.SteamId,
                    member.IsLeader));
            }
        }

        private void UpdateRoomControls()
        {
            var members = _roomSnapshot?.Members ?? Array.Empty<RoomMemberInfo>();
            bool versus = _roomSnapshot?.Format == MatchFormats.SlopballVersusWire;
            bool balanced = !versus || members.Length is 2 or 4
                && members.All(m => m.Camp is "teal" or "clay")
                && members.Count(m => m.Camp == "teal") == members.Length / 2;
            _context.Q<VisualElement>("camp-select")?.style.SetDisplay(versus);
            string ownCamp = members.FirstOrDefault(m => m.SteamId == ClientSession.SteamId)?.Camp ?? "";
            if (_context.Q<Label>("camp-label") is { } campLabel) campLabel.text = "YOUR CAMP: " + ownCamp.ToUpperInvariant();
            foreach (string camp in new[] { "teal", "clay" })
                if (_context.Q<Button>("btn-camp-" + camp) is { } button)
                {
                    button.EnableInClassList("active", ownCamp == camp);
                    button.SetEnabled(versus && !_roomActionPending && _lobby?.IsConnected == true);
                    button.style.color = camp == "teal" ? new Color(.22f, .9f, .85f) : new Color(1f, .55f, .35f);
                }
            bool localLocked = false;
            bool leader = false;
            bool allLocked = members.Length >= 2 && balanced;
            for (int i = 0; i < members.Length; i++)
            {
                allLocked &= members[i].LockedIn;
                if (members[i].SteamId == ClientSession.SteamId)
                {
                    localLocked = members[i].LockedIn;
                    leader = members[i].IsLeader;
                }
            }
            _lockedIn = localLocked;
            ClientSession.IsLobbyHost = leader;
            var admittedCharacters = _roomSnapshot?.AdmittedCharacters ?? Array.Empty<string>();
            for (int i = 0; i < _gridButtons.Count; i++)
            {
                string characterName = _gridButtons[i].name.Substring("char-".Length);
                bool allowed = Array.Exists(admittedCharacters,
                    name => string.Equals(name, characterName, StringComparison.OrdinalIgnoreCase));
                _gridButtons[i].SetEnabled(allowed && !localLocked && !_roomActionPending &&
                    _lobby?.IsConnected == true);
            }
            if (_btnLockIn != null)
            {
                _btnLockIn.style.SetDisplay(true);
                _btnLockIn.text = localLocked ? "LOCKED" : "LOCK IN";
                _btnLockIn.SetEnabled(!localLocked && !_roomActionPending &&
                    _selected != CharacterClass.None && _lobby?.IsConnected == true);
            }
            if (_btnStartMatch != null)
            {
                _btnStartMatch.style.SetDisplay(leader);
                _btnStartMatch.text = "START MATCH";
                _btnStartMatch.SetEnabled(leader && allLocked && !_roomActionPending &&
                    _lobby?.IsConnected == true);
            }
            _context.ForwardButton = localLocked ? _btnStartMatch : _btnLockIn;
            SetRoomStatus(admittedCharacters.Length == 0
                ? "Master has not admitted any characters for this Room."
                : localLocked
                    ? (leader
                        ? allLocked ? "Everyone is locked in. Start the match." : "Locked in. Waiting for the other members."
                        : "Locked in. Waiting for the Room leader to start.")
                    : "Choose your fighter, then lock in.", admittedCharacters.Length == 0);
            RenderRoomCardMarkers();
            if (!balanced) SetRoomStatus("Choose equal Teal and Clay camps before starting. Switching camp unlocks your fighter.", false);
        }

        private void SelectTealCamp() => SelectCamp("teal");
        private void SelectClayCamp() => SelectCamp("clay");
        private async void SelectCamp(string camp)
        {
            var lobby = _lobby;
            if (_roomActionPending || lobby?.IsConnected != true || _roomSnapshot?.Format != MatchFormats.SlopballVersusWire) return;
            _roomActionPending = true;
            UpdateRoomControls();
            try
            {
                var room = await lobby.RoomSelectCampAsync(camp);
                if (isActiveAndEnabled && ReferenceEquals(_lobby, lobby)) OnRoomUpdated(room);
            }
            catch (Exception ex)
            { if (isActiveAndEnabled) SetRoomStatus("Could not change camp: " + ex.Message, true); }
            finally
            {
                if (isActiveAndEnabled && ReferenceEquals(_lobby, lobby))
                { _roomActionPending = false; UpdateRoomControls(); }
            }
        }

        private void RenderRoomCardMarkers()
        {
            foreach (var button in _gridButtons)
                button.Q<VisualElement>("char-markers")?.Clear();
            var members = _roomSnapshot?.Members ?? Array.Empty<RoomMemberInfo>();
            for (int i = 0; i < members.Length; i++)
            {
                if (!Enum.TryParse<CharacterClass>(members[i].CharacterSelection, true, out var cls))
                    continue;
                var markers = _context.Q<Button>($"char-{cls}")?.Q<VisualElement>("char-markers");
                if (markers == null) continue;
                var marker = new Label($"P{i + 1} {(members[i].LockedIn ? "LOCKED" : "SELECTING")}");
                marker.AddToClassList("char-marker");
                marker.AddToClassList(members[i].LockedIn ? "char-marker--ready" : "char-marker--selecting");
                markers.Add(marker);
            }
        }

        private async void OnRoomLockInClicked()
        {
            var lobby = _lobby;
            if (_roomActionPending || _lockedIn || lobby == null || _selected == CharacterClass.None)
                return;
            string character = _selected.ToString();
            _roomActionPending = true;
            UpdateRoomControls();
            try
            {
                var room = await lobby.RoomSelectCharacterAsync(character);
                if (isActiveAndEnabled && ReferenceEquals(_lobby, lobby))
                    OnRoomUpdated(room);
            }
            catch (Exception ex)
            {
                if (!isActiveAndEnabled || !ReferenceEquals(_lobby, lobby)) return;
                _roomActionPending = false;
                UpdateRoomControls();
                SetRoomStatus($"Character selection rejected: {ex.Message}", true);
            }
        }

        private async void OnRoomStartMatchClicked()
        {
            var lobby = _lobby;
            var snapshot = _roomSnapshot;
            Guid roomId = ClientSession.SelectedRoomId;
            if (_roomActionPending || lobby == null || snapshot == null || !lobby.IsConnected)
                return;
            var members = snapshot.Members ?? Array.Empty<RoomMemberInfo>();
            bool isLeader = false;
            bool allLocked = members.Length >= 2;
            foreach (var member in members)
            {
                allLocked &= member.LockedIn;
                if (member.SteamId == ClientSession.SteamId) isLeader = member.IsLeader;
            }
            if (!isLeader || !allLocked) return;
            _roomActionPending = true;
            UpdateRoomControls();
            SetRoomStatus("Starting the Room match with Master…", false);
            try
            {
                await lobby.RoomStartMatchAsync();
            }
            catch (Exception ex)
            {
                if (!isActiveAndEnabled || !ReferenceEquals(_lobby, lobby) ||
                    ClientSession.SelectedRoomId != roomId)
                    return;
                if (_roomSnapshot?.Phase is "Match Starting" or "In Match")
                    return;
                _roomActionPending = false;
                UpdateRoomControls();
                SetRoomStatus($"Could not start the Room match: {ex.Message}. You can retry.", true);
            }
        }

        private void OnRoomBackClicked() => FrontendController.Show(FrontendPage.LobbyRoom);

        private void SetRoomStatus(string message, bool error)
        {
            if (_lblPvPStatus == null) return;
            _lblPvPStatus.text = message;
            if (error) _lblPvPStatus.AddToClassList("error");
            else _lblPvPStatus.RemoveFromClassList("error");
        }


        private void InitPvP(FrontendPageContext context)
        {
            SetModeChrome(context, "ONLINE // CHOOSE YOUR FIGHTER", "STEP 1 OF 2  /  FIGHTER");
            // PvP shows the participant cards and the lock-in/status actions;
            // the solo continue action stays hidden.
            context.Q<Button>("btn-select")?.style.SetDisplay(false);
            context.Q<VisualElement>("solo-config")?.style.SetDisplay(false);

            var rosterMeta = context.Q<Label>("roster-meta");
            _snapshot = ClientSession.LobbyRoster;
            if (rosterMeta != null)
                rosterMeta.text = MenuRoster.Classes.Length == 0
                    ? "NO ADMITTED FIGHTERS"
                    : $"{MenuRoster.Classes.Length} FIGHTERS // {_snapshot?.Players.Count ?? 0} PLAYERS";
            _rosterPanel   = context.Q<VisualElement>("roster-panel");
            _btnLockIn     = context.Q<Button>("btn-lockin");
            _btnStartMatch = context.Q<Button>("btn-start-match");
            _lblPvPStatus  = context.Q<Label>("lbl-pvp-status");
            _lockedIn = false;

            bool isHost = IsLocalHost();
            ClientSession.IsLobbyHost = isHost;
            if (_btnLockIn != null)
            {
                _btnLockIn.text = "LOCK IN";
                _btnLockIn.SetEnabled(MenuRoster.Classes.Length > 0);
                _btnLockIn.clicked += OnLockInClicked;
            }

            // Host-only: move everyone to stage select once all players lock in.
            if (_btnStartMatch != null)
            {
                if (isHost)
                {
                    _btnStartMatch.text = "SELECT STAGE";
                    _btnStartMatch.style.display = DisplayStyle.Flex;
                    _btnStartMatch.SetEnabled(false);
                    _btnStartMatch.clicked += OnStartMatchClicked;
                }
                else
                {
                    _btnStartMatch.style.display = DisplayStyle.None;
                }
            }

            var btnBack = context.Q<Button>("btn-back");
            if (btnBack != null)
                btnBack.clicked += OnPvPBackClicked;
            ConfigureNavigation(context, _gridButtons.FirstOrDefault() ?? _btnLockIn, btnBack, OnPvPBackClicked,
                _btnLockIn);

            _lobby = ClientSession.ActiveLobby;
            if (_lobby == null)
            {
                if (_lblPvPStatus != null)
                    _lblPvPStatus.text = "No lobby connection. Returning to server browser.";
                FrontendController.Show(FrontendPage.ServerBrowser);
                return;
            }

            _lobby.LobbyUpdated    += OnLobbyUpdated;
            _lobby.CharacterSelected += OnCharacterSelected;
            _lobby.StageSelect      += OnStageSelect;
            _lobby.MatchStarted     += OnMatchStarted;
            _lobby.Error            += OnPvPError;

            if (_lblPvPStatus != null)
                _lblPvPStatus.text = MenuRoster.Classes.Length == 0
                    ? "Fighter content is unavailable. Return to the menu."
                    : "Choose your fighter, then lock in.";
            RenderRoster();
            UpdateStartMatchButton();
            RenderCardMarkers();
            Debug.Log($"[CharSelect] InitPvP: isHost={isHost}, snapshot={_snapshot?.Players.Count ?? 0} players, " +
                $"steamId={ClientSession.SteamId}");
        }

        private bool IsLocalHost()
        {
            var players = _snapshot?.Players ?? System.Array.Empty<LobbyPlayerInfo>();
            foreach (var p in players)
            {
                if (p.SteamId == ClientSession.SteamId && p.IsHost)
                    return true;
            }
            return false;
        }

        private void OnLockInClicked()
        {
            if (_lockedIn) return;
            _btnLockIn.SetEnabled(false);
            _btnLockIn.text = "LOCKED";
            if (IsLocalHost())
                _context.ForwardButton = _btnStartMatch;
            Debug.Log($"[CharSelect] Locking in {_selected}");
            _ = _lobby.SelectCharacterAsync(_selected.ToString());
        }

        private void OnStartMatchClicked()
        {
            _btnStartMatch.SetEnabled(false);
            _lblPvPStatus.text = "Selecting stage...";
            _ = _lobby.StartStageSelectAsync();
        }

        private void OnStageSelect(MatchStartingConfig config)
        {
            // Everyone moves to the stage select screen; the host picks the
            // arena there, then the match starts from StageSelect.
            FrontendController.Show(FrontendPage.StageSelect);
        }

        private void OnLobbyUpdated(LobbySnapshot snapshot)
        {
            _snapshot = snapshot;
            RenderRoster();
            UpdateStartMatchButton();

            RenderCardMarkers();
            Debug.Log($"[CharSelect] LobbyUpdated: {DescribePlayers(snapshot)}");
        }

        private void OnCharacterSelected(LobbyPlayerInfo player)
        {
            // The LobbyUpdated that follows carries the same info; just re-render.
            RenderRoster();
            UpdateStartMatchButton();

            RenderCardMarkers();
            Debug.Log($"[CharSelect] CharacterSelected: {player.Name} locked={player.LockedIn} char={player.CharacterSelection} host={player.IsHost}");
        }

        private void OnMatchStarted(MatchStartedConfig config)
        {
            if (config.RoomId != null)
                return;
            Debug.Log($"[CharSelect] Match started: {config.Players.Count} players, port={config.MatchPort}, arena={config.ArenaName}.");

            // Keep the lobby connection alive through the match (issue #40): the
            // results screen + lobby return rely on it. Just unsubscribe the
            // event handlers so nothing fires while in Arena_PvP.
            if (_lobby != null)
            {
                _lobby.LobbyUpdated    -= OnLobbyUpdated;
                _lobby.CharacterSelected -= OnCharacterSelected;
                _lobby.StageSelect      -= OnStageSelect;
                _lobby.MatchStarted     -= OnMatchStarted;
                _lobby.Error            -= OnPvPError;
            }

            ClientSession.ApplyMatchStarted(config);
        }

        private void OnPvPError(string message)
        {
            _lblPvPStatus.text = message;
            Debug.LogWarning($"[CharSelect] PvP error: {message}");
            // Re-enable lock-in on error (e.g. rejected selection)
            _btnLockIn.SetEnabled(!_lockedIn);
            // Re-enable the stage-select button on a rejected transition.
            UpdateStartMatchButton();
        }

        private void OnPvPBackClicked()
        {
            if (_lobby != null)
            {
                _lobby.LobbyUpdated    -= OnLobbyUpdated;
                _lobby.CharacterSelected -= OnCharacterSelected;
                _lobby.StageSelect      -= OnStageSelect;
                _lobby.MatchStarted     -= OnMatchStarted;
                _lobby.Error            -= OnPvPError;
            }
            // The host owns the embedded server subprocess (ADR-0005): backing
            // out of char-select must stop it, or the orphaned server keeps
            // running and stays registered (issue #48). Non-hosts never touch
            // it — MatchConfig.IsHost is the authoritative flag, not the roster.
            if (MatchConfig.IsHost)
                ServerHost.Instance?.Stop();

            // Return to lobby room (connection still alive)
            FrontendController.Show(FrontendPage.LobbyRoom);
        }


        private void RenderRoster()
        {
            if (_rosterPanel == null) return;
            _rosterPanel.Clear();

            var players = _snapshot?.Players ?? System.Array.Empty<LobbyPlayerInfo>();
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                CharacterClass selectedClass = CharacterClass.None;
                bool hasCharacter = System.Enum.TryParse(
                    player.CharacterSelection, true, out selectedClass);
                _rosterPanel.Add(BuildPlayerCard(
                    $"P{i + 1}",
                    player.Name,
                    hasCharacter ? selectedClass : (CharacterClass?)null,
                    hasCharacter ? (player.LockedIn ? "LOCKED" : "PICKING") : "WAITING",
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
            VisualElement card = MatchConfig.Mode is GameMode.Solo or GameMode.Training
                ? new Button()
                : new VisualElement();
            card.AddToClassList("player-card");
            if (local) card.AddToClassList("player-card--local");
            if ((MatchConfig.Mode is GameMode.Solo or GameMode.Training) && statusText == "EDITING FIGHTER")
                card.AddToClassList("player-card--editing");

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
            else
            {
                var waiting = new Label("WAITING FOR PLAYER");
                waiting.AddToClassList("player-card__waiting");
                card.Add(waiting);
            }

            if (!string.IsNullOrEmpty(statusText))
            {
                var status = new Label(statusText);
                status.AddToClassList("player-card__status");
                status.AddToClassList(statusText == "LOCKED" || statusText == "BOT"
                    ? "player-card__status--locked"
                    : statusText == "EDITING FIGHTER"
                        ? "player-card__status--editing"
                        : "player-card__status--picking");
                card.Add(status);
            }
            return card;
        }

        private void UpdateStartMatchButton()
        {
            if (_btnStartMatch == null || _btnStartMatch.style.display == DisplayStyle.None)
                return;

            var players = _snapshot?.Players ?? System.Array.Empty<LobbyPlayerInfo>();
            bool canStart = players.Count >= 2 && players.All(p => p.LockedIn);
            _btnStartMatch.SetEnabled(canStart);
            _context.ForwardButton = _btnLockIn?.enabledSelf == true ? _btnLockIn : _btnStartMatch;

            Debug.Log($"[CharSelect] StartMatch check: count={players.Count} locked={string.Join(",", players.Select(p => $"{p.Name}:{p.LockedIn}"))} -> canStart={canStart}");

            if (!canStart)
            {
                int locked = 0;
                foreach (var p in players) if (p.LockedIn) locked++;
                _lblPvPStatus.text = players.Count < 2
                    ? "Waiting for players..."
                    : $"Waiting for locks ({locked}/{players.Count})...";
            }
            else
            {
                _lblPvPStatus.text = "All players locked in. Host can select the stage.";
            }
        }

        private static string DescribePlayers(LobbySnapshot snapshot)
        {
            var parts = snapshot?.Players.Select(p =>
                $"{p.Name}(locked={p.LockedIn},char={p.CharacterSelection ?? "?"},host={p.IsHost},steam={p.SteamId})");
            return $"server={snapshot?.ServerId}, players=[{string.Join(", ", parts ?? System.Array.Empty<string>())}]";
        }

        // ── Shared ──



        private void SelectCharacter(CharacterClass cls, FrontendPageContext context)
        {
            if (cls == CharacterClass.None)
                return;

            _selected = cls;
            if ((MatchConfig.Mode is GameMode.Solo or GameMode.Training) && _selectingCpu)
                MatchConfig.SoloBotClass = cls;
            else
                MatchConfig.PlayerClass = cls;

            if ((MatchConfig.Mode is GameMode.Solo or GameMode.Training) && _rosterPanel != null)
                SetSelectionTarget(context, _selectingCpu);
            else
                UpdateSelectionVisuals(cls, context);
        }

        private void RenderCardMarkers()
        {
            foreach (var btn in _gridButtons)
                btn.Q<VisualElement>("char-markers")?.Clear();

            var players = _snapshot?.Players ?? System.Array.Empty<LobbyPlayerInfo>();
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!System.Enum.TryParse<CharacterClass>(player.CharacterSelection, true, out var cls))
                    continue;

                var card = _context.Q<Button>($"char-{cls}");
                var markers = card?.Q<VisualElement>("char-markers");
                if (markers == null) continue;

                var marker = new Label($"P{i + 1} {(player.LockedIn ? "READY" : "SELECTING")}");
                marker.AddToClassList("char-marker");
                marker.AddToClassList(player.LockedIn
                    ? "char-marker--ready"
                    : "char-marker--selecting");
                markers.Add(marker);
            }
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
                    if (_btnLockIn != null) _btnLockIn.clicked -= OnRoomLockInClicked;
                    if (_btnStartMatch != null) _btnStartMatch.clicked -= OnRoomStartMatchClicked;
                    if (_context.Q<Button>("btn-camp-teal") is { } teal) teal.clicked -= SelectTealCamp;
                    if (_context.Q<Button>("btn-camp-clay") is { } clay) clay.clicked -= SelectClayCamp;
                    var backButton = _context.Q<Button>("btn-back");
                    if (backButton != null) backButton.clicked -= OnRoomBackClicked;
                }
                else
                {
                    _lobby.LobbyUpdated -= OnLobbyUpdated;
                    _lobby.CharacterSelected -= OnCharacterSelected;
                    _lobby.StageSelect -= OnStageSelect;
                    _lobby.MatchStarted -= OnMatchStarted;
                    _lobby.Error -= OnPvPError;
                }
                _lobby = null;
            }
        }

    }
}
