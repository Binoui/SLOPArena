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

        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void OnEnable()
        {
            _selectedArena = "";
            _roomSnapshot = null;
            _roomActionPending = false;
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
            SetModeChrome(_roomMode ? "ROOM // SELECT ARENA" : isOnline ? "ONLINE // SELECT STAGE" :
                isTraining ? "TRAINING // SELECT STAGE" : "SOLO // SELECT STAGE",
                _roomMode ? "ROOM PREPARATION  /  ARENA" : "STEP 2 OF 2  /  STAGE");
            _context.Q<Label>("subtitle").text = _roomMode
                ? (isHost ? "CHOOSE THE ROOM ARENA" : "THE ROOM LEADER WILL CHOOSE THE ARENA")
                : isHost ? "CHOOSE YOUR BATTLEGROUND" : "THE HOST WILL CHOOSE THE BATTLEGROUND";
            _context.Q<Label>("lbl-host").text = _roomMode
                ? (isHost ? "ROOM LEADER CHOOSES THE ARENA" : "WAITING FOR ROOM LEADER")
                : isOnline ? (isHost ? "HOST CHOOSES THE STAGE" : "WAITING FOR HOST")
                : "YOU CHOOSE THE STAGE";

            if (_btnConfirm != null)
            {
                _btnConfirm.style.display = DisplayStyle.None;
                _btnConfirm.text = _roomMode ? "CONFIRM ARENA" : isOnline ? "START MATCH" :
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
            foreach (var arena in ArenaRegistry.All)
            {
                if (!IsInStageSelectRoster(arena.Name, isTraining)) continue;
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
            else if (isTraining && firstStageButton != null)
            {
                SelectStage(firstStageButton.name.Substring("stage-".Length));
            }

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
        }

        private static void SetModeChrome(string title, string progress)
        {
            var titleLabel = FrontendController.CurrentContext?.Q<Label>("title");
            if (titleLabel != null)
                titleLabel.text = title;
            var progressLabel = FrontendController.CurrentContext?.Q<Label>("flow-progress");
            if (progressLabel != null)
                progressLabel.text = progress;
        }

        // Curated picker roster: other baked arenas remain available for
        // compatibility and authoring until their rebuilds are ready.
        private static bool IsInStageSelectRoster(string arenaName, bool isTraining)
        {
            switch (arenaName)
            {
                case "table_manners":
                case "splash_deck":
                case "slop_pit":
                case "after_hours":
                case "last_stop":
                case "log_off":
                    return true;
                case "training":
                    return isTraining;
                default:
                    return false;
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
                _roomSnapshot = room;
                _roomActionPending = true;
                ClientSession.IsLobbyHost = IsLocalRoomLeader(room);
                if (_btnConfirm != null)
                {
                    _btnConfirm.style.display = DisplayStyle.Flex;
                    _btnConfirm.text = "MATCH STARTING";
                    _btnConfirm.SetEnabled(false);
                }
                ShowRoomStatus(room.Phase == "In Match"
                    ? "Match host allocated. Waiting for the Room match descriptor…"
                    : "Master is preparing the Room match…", false);
                return;
            }
            _roomSnapshot = room;
            _roomActionPending = false;
            ClientSession.IsLobbyHost = IsLocalRoomLeader(room);
            if (_context.Q<Label>("subtitle") is { } subtitle)
                subtitle.text = ClientSession.IsLobbyHost ? "CHOOSE THE ROOM ARENA" : "THE ROOM LEADER WILL CHOOSE THE ARENA";
            if (_context.Q<Label>("lbl-host") is { } hostLabel)
                hostLabel.text = ClientSession.IsLobbyHost ? "ROOM LEADER CHOOSES THE ARENA" : "WAITING FOR ROOM LEADER";
            ApplyRoomArenaAllowlist();
            RenderPlayerCards();
            if (_btnConfirm != null)
            {
                _btnConfirm.style.display = DisplayStyle.Flex;
                _btnConfirm.text = ClientSession.IsLobbyHost ? "CONFIRM ARENA" : "WAITING FOR LEADER";
            }
            if (!string.IsNullOrWhiteSpace(room.ArenaName) &&
                _grid?.Q<VisualElement>($"stage-{room.ArenaName}") != null)
            {
                SelectStage(room.ArenaName);
                if (_lblSelectedStage != null)
                    _lblSelectedStage.text = $"ROOM ARENA: {DisplayArenaName(room.ArenaName)}";
                if (_btnConfirm != null)
                    _btnConfirm.text = ClientSession.IsLobbyHost
                        ? string.Equals(_selectedArena, room.ArenaName, StringComparison.OrdinalIgnoreCase)
                            ? "START MATCH"
                            : "CHANGE ARENA"
                        : "ARENA CONFIRMED";
                _btnConfirm?.SetEnabled(ClientSession.IsLobbyHost && !_roomActionPending &&
                    _lobby?.IsConnected == true);
                ShowRoomStatus("Arena selection is confirmed for every Room member.", false);
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

        private async void ChooseRoomArena()
        {
            var lobby = _lobby;
            string arena = _selectedArena;
            if (_roomActionPending || lobby == null || _roomSnapshot == null ||
                !ClientSession.IsLobbyHost || string.IsNullOrEmpty(arena))
                return;
            _roomActionPending = true;
            ApplyRoomArenaAllowlist();
            _btnConfirm?.SetEnabled(false);
            ShowRoomStatus("Confirming the arena with Master…", false);
            try
            {
                var room = await lobby.RoomChooseArenaAsync(arena);
                if (isActiveAndEnabled && ReferenceEquals(_lobby, lobby))
                    OnRoomUpdated(room);
            }
            catch (Exception ex)
            {
                if (!isActiveAndEnabled || !ReferenceEquals(_lobby, lobby)) return;
                _roomActionPending = false;
                ApplyRoomArenaAllowlist();
                _btnConfirm?.SetEnabled(ClientSession.IsLobbyHost && !string.IsNullOrEmpty(_selectedArena) &&
                    _lobby?.IsConnected == true);
                if (_lblSelectedStage != null)
                    _lblSelectedStage.text = $"ARENA NOT CONFIRMED: {DisplayArenaName(arena)}";
                ShowRoomStatus($"Master rejected this arena: {ex.Message}", true);
            }
        }
        private async void StartRoomMatch()
        {
            var lobby = _lobby;
            Guid roomId = ClientSession.SelectedRoomId;
            if (_roomActionPending || lobby == null || _roomSnapshot?.Id != roomId ||
                !ClientSession.IsLobbyHost || !lobby.IsConnected ||
                !string.Equals(_selectedArena, _roomSnapshot.ArenaName, StringComparison.OrdinalIgnoreCase))
                return;
            _roomActionPending = true;
            _btnConfirm?.SetEnabled(false);
            ShowRoomStatus("Starting the Room match with Master…", false);
            try
            {
                await lobby.RoomStartMatchAsync();
            }
            catch (Exception ex)
            {
                if (!isActiveAndEnabled || !ReferenceEquals(_lobby, lobby) ||
                    ClientSession.SelectedRoomId != roomId)
                    return;
                if (_roomSnapshot?.Id == roomId &&
                    (_roomSnapshot.Phase == "Match Starting" || _roomSnapshot.Phase == "In Match"))
                    return;
                _roomActionPending = false;
                ApplyRoomArenaAllowlist();
                _btnConfirm?.SetEnabled(ClientSession.IsLobbyHost &&
                    !string.IsNullOrEmpty(_selectedArena) && lobby.IsConnected);
                ShowRoomStatus($"Could not start the Room match: {ex.Message}. You can retry.", true);
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
                if (_roomSnapshot != null &&
                    string.Equals(_roomSnapshot.ArenaName, _selectedArena, StringComparison.OrdinalIgnoreCase))
                    StartRoomMatch();
                else
                    ChooseRoomArena();
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
                ShowRoomStatus($"Master rejected the arena choice: {message}", true);
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
                    _btnConfirm.text = _roomSnapshot != null &&
                        string.Equals(name, _roomSnapshot.ArenaName, StringComparison.OrdinalIgnoreCase)
                        ? "START MATCH"
                        : "CONFIRM ARENA";
                _btnConfirm.SetEnabled(!_roomMode || ClientSession.IsLobbyHost &&
                    !_roomActionPending && _lobby?.IsConnected == true);
            }
        }
    }
}
