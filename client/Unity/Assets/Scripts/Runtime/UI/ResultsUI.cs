#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Shared;
using SlopArena.Client;
using SlopArena.Client.Network;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Results page (issue #221): a fragment mounted into the FrontendShell
    /// hosts — the winner/standings broadcast composition in the body and the
    /// existing return action outside the conversation cell. It consumes only
    /// the immutable result snapshot prepared by ClientSession; fighter
    /// GameObjects are never queried.
    /// </summary>
    public sealed class ResultsUI : MonoBehaviour, IFrontendPageController
    {
        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;

        private VisualElement _bodyRoot = null!;
        private VisualElement _actionsRoot = null!;
        private VisualElement _winnerCard = null!;
        private Label _standingsLabel = null!;

        private VisualElement _standings = null!;
        private Label _stage = null!;
        private Label _metadata = null!;
        private Label _headline = null!;
        private Button _returnButton = null!;
        private bool _roomCheckPending;


        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void OnEnable()
        {
            UISFX.PlayMenuMusic();
            _bodyRoot = _context.Q<VisualElement>("page-body");
            _actionsRoot = _context.Q<VisualElement>("page-actions");
            _winnerCard = _context.Q<VisualElement>("results-winner");
            _standings = _context.Q<VisualElement>("results-standings");
            _stage = _context.Q<Label>("results-stage");
            _metadata = _context.Q<Label>("results-metadata");
            _standingsLabel = _context.Q<Label>("results-standings-label");

            _headline = _context.Q<Label>("results-headline");
            _returnButton = _context.Q<Button>("btn-return-lobby");

            // Local modes end at Home; only PvP has a lobby to return to.
            bool isLocal = MatchConfig.Mode != GameMode.PvP;
            _returnButton.text = isLocal ? "BACK TO MENU" : "RETURN TO LOBBY";
            _returnButton.clicked += ReturnFromResults;
            // Page activation contract (issue #210): focus the page action and
            // give Escape the page's flow-specific back (return) action.
            MenuNavigation.Configure(_context, _returnButton, ReturnFromResults, _returnButton);
            RenderResults();
        }

        private void OnDisable()
        {
            if (_returnButton != null)
                _returnButton.clicked -= ReturnFromResults;
        }


        private void ReturnFromResults()
        {
            if (MatchConfig.Mode != GameMode.PvP)
            {
                FrontendController.Show(FrontendPage.Home);
                return;
            }

            if (ClientSession.SelectedOnlineMode == ClientSession.OnlineSelection.Room)
            {
                _ = ReturnToRoomAsync();
                return;
            }

            var lobby = ClientSession.ActiveLobby;
            if (lobby != null && lobby.IsConnected &&
                ClientSession.SelectedServerId != Guid.Empty &&
                lobby.JoinedServerId == ClientSession.SelectedServerId)
            {
                FrontendController.Show(FrontendPage.LobbyRoom);
                return;
            }

            ServerBrowserUI.PendingReturnNotice =
                "Your room connection closed during the match. Pick another room or host a new one.";
            FrontendController.Show(FrontendPage.ServerBrowser);
        }

        private async System.Threading.Tasks.Task ReturnToRoomAsync()
        {
            if (_roomCheckPending)
                return;
            _roomCheckPending = true;
            _returnButton.SetEnabled(false);
            _returnButton.text = "CHECKING ROOM…";
            try
            {
                var chat = ChatSession.Instance;
                if (chat == null || !await chat.EnsureConnectedAsync())
                {
                    SetRoomCheckFailure("Couldn’t reconnect to the Room directory. Retry to check your membership.");
                    return;
                }

                var lobby = chat.ActiveLobby;
                if (lobby == null)
                {
                    SetRoomCheckFailure("Couldn’t reconnect to the Room directory. Retry to check your membership.");
                    return;
                }

                var room = await lobby.GetMyRoomAsync();
                if (room == null || room.Id != ClientSession.SelectedRoomId)
                {
                    ClientSession.SelectedRoomId = Guid.Empty;
                    ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
                    ClientSession.SelectedServerName = string.Empty;
                    ServerBrowserUI.PendingReturnNotice =
                        "You are no longer a member of this Room. Join another Room or create one.";
                    FrontendController.Show(FrontendPage.ServerBrowser);
                    return;
                }

                ClientSession.SelectedServerName = room.Name;
                chat.UpdateRoomTitle(room);
                FrontendController.Show(FrontendPage.LobbyRoom);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Results] Room membership check failed: {ex.Message}");
                SetRoomCheckFailure("Couldn’t check Room membership. Retry when the directory connection is available.");
            }
        }

        private void SetRoomCheckFailure(string message)
        {
            _roomCheckPending = false;
            _returnButton.text = "RETRY ROOM CHECK";
            _returnButton.SetEnabled(true);
            if (_metadata != null)
                _metadata.text = $"{_metadata.text}\n{message}";
        }

        private void RenderResults()
        {
            _standings.Clear();
            if (_standingsLabel != null)
                _standings.Add(_standingsLabel);

            var results = ClientSession.CurrentMatchResults;
            if (results == null || results.Entries.Count == 0)
            {
                _headline.text = "FIGHT OVER";
                _stage.text = "MATCH COMPLETE // UNKNOWN STAGE";
                _metadata.text = "RESULT SNAPSHOT UNAVAILABLE";
                return;
            }

            _bodyRoot.EnableInClassList("results-count-2", results.PlayerCount == 2);
            _bodyRoot.EnableInClassList("results-count-3", results.PlayerCount == 3);
            _bodyRoot.EnableInClassList("results-count-4", results.PlayerCount >= 4);

            string stage = string.IsNullOrEmpty(results.StageName)
                ? MatchConfig.ArenaName
                : results.StageName;
            _stage.text = $"MATCH COMPLETE // {stage.ToUpperInvariant()}";
            _metadata.text = $"{FormatDuration(results.DurationTicks)} // {results.PlayerCount} PLAYERS";
            _headline.text = results.SharedVictory ? "SHARED VICTORY" : "FIGHT OVER";
            if (results.Run is { } run)
            {
                // A co-op Slopball run ends together: its team result is the headline (ADR-0040).
                _stage.text = "RUN OVER // SLOPBALL CO-OP";
                _headline.text = run.TotalReturns == 1 ? "1 RETURN" : $"{run.TotalReturns} RETURNS";
                _metadata.text = $"{FormatDuration(results.DurationTicks)} // BEST COMBO {run.BestCombo} // {results.PlayerCount} PLAYERS";
            }

            var ordered = new List<ClientSession.ResultEntry>(results.Entries);
            if (results.Versus is { } score)
            {
                _stage.text = "MATCH COMPLETE // SLOPBALL VERSUS";
                _headline.text = score.Winner.ToString().ToUpperInvariant() + " WINS";
                _metadata.text = $"TEAL {score.TealScore} — {score.ClayScore} CLAY // FIRST TO 21 // {FormatDuration(results.DurationTicks)}";
            }
            if (results.Course is { } course)
            {
                _stage.text = (course.Cooperative ? "COURSE CO-OP // " : "COURSE RACE // ") + stage.Replace('_', ' ').ToUpperInvariant();
                _headline.text = !course.Completed ? "RUN ENDED" : course.Cooperative ? "TEAM COMPLETE" : results.SharedVictory ? "TIED FINISH" : "RACE COMPLETE";
                _metadata.text = $"{CharacterCourseView.FormatTime(course.ElapsedTicks)} // {results.PlayerCount} PLAYERS";
            }
            ordered.Sort((a, b) =>
            {
                int byPlacement = (a.Placement == 0 ? 255 : a.Placement).CompareTo(b.Placement == 0 ? 255 : b.Placement);
                return byPlacement != 0 ? byPlacement : a.EntityId.CompareTo(b.EntityId);
            });

            var hero = ordered[0];
            BuildWinnerCard(hero, results.SharedVictory, results.Run.HasValue || results.Versus.HasValue || results.Course is { Cooperative: true });
            for (int i = 1; i < ordered.Count; i++)
                _standings.Add(BuildStandingRow(ordered[i]));

            // The reveal animation spans two hosts (winner/standings in the
            // body, the return action in the lower row), so the ready class
            // lands on both page-owned roots (issue #221).
            _bodyRoot.schedule.Execute(() =>
            {
                _bodyRoot.AddToClassList("results-ready");
                _actionsRoot.AddToClassList("results-ready");
            }).StartingIn(40);
        }

        private void BuildWinnerCard(ClientSession.ResultEntry entry, bool sharedVictory, bool team = false)
        {
            var metadata = ResolveMetadata(entry);
            var card = new VisualElement();
            card.AddToClassList("results-winner-card");
            card.style.borderLeftColor = AccentForPlacement(entry.Placement);

            var placement = new Label(entry.Placement == 0 ? "DNF" : entry.Placement.ToString("00"));
            placement.AddToClassList("results-winner-placement");
            card.Add(placement);

            var label = new Label(entry.Placement == 0 ? "UNFINISHED" : team ? "TEAM" : sharedVictory ? "TOP FINISHER" : "WINNER");
            label.AddToClassList("results-winner-label");
            card.Add(label);

            var portrait = BuildPortrait(metadata.Class);
            portrait.AddToClassList("results-winner-portrait");
            card.Add(portrait);

            var copy = new VisualElement();
            copy.AddToClassList("results-winner-copy");
            var playerName = new Label(metadata.PlayerName) { enableRichText = false };
            playerName.AddToClassList("results-winner-player");
            copy.Add(playerName);
            var fighterName = new Label(metadata.FighterName);
            fighterName.AddToClassList("results-winner-fighter");
            copy.Add(fighterName);
            copy.Add(BuildStats(entry, "results-winner-stats"));
            card.Add(copy);

            _winnerCard.Add(card);
        }

        private VisualElement BuildStandingRow(ClientSession.ResultEntry entry)
        {
            var metadata = ResolveMetadata(entry);
            var row = new VisualElement();
            row.AddToClassList("results-player-row");
            row.style.borderLeftColor = AccentForPlacement(entry.Placement);

            var placement = new Label(entry.Placement == 0 ? "DNF" : entry.Placement.ToString("00"));
            placement.AddToClassList("results-row-placement");
            row.Add(placement);

            var portrait = BuildPortrait(metadata.Class);
            portrait.AddToClassList("results-row-portrait");
            row.Add(portrait);

            var identity = new VisualElement();
            identity.AddToClassList("results-row-identity");
            var playerName = new Label(metadata.PlayerName) { enableRichText = false };
            playerName.AddToClassList("results-row-player");
            identity.Add(playerName);
            var fighterName = new Label(metadata.FighterName);
            fighterName.AddToClassList("results-row-fighter");
            identity.Add(fighterName);
            row.Add(identity);

            row.Add(BuildStats(entry, "results-row-stats"));
            return row;
        }

        private static VisualElement BuildStats(ClientSession.ResultEntry entry, string className)
        {
            var stats = new VisualElement();
            stats.AddToClassList(className);
            if (ClientSession.CurrentMatchResults?.Versus is { } score
                && MatchConfig.Camps.TryGetValue(entry.EntityId, out var camp))
            {
                var label = new Label(camp.ToString().ToUpperInvariant() + (camp == score.Winner ? " · WINNER" : " · RUNNER-UP"));
                label.AddToClassList("results-stat-label");
                label.style.color = camp == SlopballCamp.Teal ? new Color(.22f, .9f, .85f) : new Color(1f, .55f, .35f);
                stats.Add(label);
                return stats;
            }
            if (ClientSession.CurrentMatchResults?.Course is { } course)
            {
                var finish = System.Linq.Enumerable.First(course.Players, p => p.EntityId == entry.EntityId);
                var label = new Label(finish.FinishTicks != 0 ? CharacterCourseView.FormatTime(finish.FinishTicks)
                    : (finish.Withdrawn ? "DISCONNECTED" : "DNF") + $" · {finish.BrokenTargets}/{finish.TotalTargets} TARGETS");
                label.AddToClassList("results-stat-label"); stats.Add(label);
                AddStat(stats, "FALLS", entry.Falls);
                return stats;
            }
            AddStat(stats, "KOs", entry.KOs);
            AddStat(stats, "FALLS", entry.Falls);
            return stats;
        }

        private static void AddStat(VisualElement parent, string label, int value)
        {
            var stat = new VisualElement();
            stat.AddToClassList("results-stat");
            var valueLabel = new Label(value.ToString());
            valueLabel.AddToClassList("results-stat-value");
            stat.Add(valueLabel);
            var nameLabel = new Label(label);
            nameLabel.AddToClassList("results-stat-label");
            stat.Add(nameLabel);
            parent.Add(stat);
        }

        private static VisualElement BuildPortrait(CharacterClass character)
        {
            var portrait = new VisualElement();
            portrait.AddToClassList("results-portrait");
            var texture = Resources.Load<Texture2D>($"UI/Portraits/{character}");
            if (texture != null)
                portrait.style.backgroundImage = new StyleBackground(texture);
            else
                portrait.AddToClassList("results-portrait-missing");
            return portrait;
        }

        private static PlayerMetadata ResolveMetadata(ClientSession.ResultEntry entry)
        {
            string playerName = entry.Name;
            CharacterClass character = ParseClass(entry.ClassName);

            if (ClientSession.MatchRoster != null)
            {
                foreach (var roster in ClientSession.MatchRoster)
                {
                    if (roster.EntityId != (long)entry.EntityId) continue;
                    if (string.IsNullOrEmpty(playerName))
                        playerName = roster.Name;
                    character = ParseClass(roster.CharacterSelection, character);
                    break;
                }
            }

            if (string.IsNullOrEmpty(playerName))
                playerName = $"P{entry.EntityId}";

            var content = ClientSession.MatchContentCatalog?.Resolve(character);
            string fighterName = string.IsNullOrEmpty(content?.DisplayName)
                ? character.ToString()
                : content!.DisplayName;
            return new PlayerMetadata(
                playerName.ToUpperInvariant(),
                fighterName.ToUpperInvariant(),
                character);
        }
        private static CharacterClass ParseClass(string? value, CharacterClass fallback = CharacterClass.None)
        {
            if (!string.IsNullOrEmpty(value)
                && Enum.TryParse(value, true, out CharacterClass parsed)
                && parsed != CharacterClass.None)
                return parsed;
            return fallback;
        }


        private static string FormatDuration(uint ticks)
        {
            if (ticks == 0) return "DURATION N/A";
            int totalSeconds = Mathf.Max(1, Mathf.RoundToInt(ticks / 60f));
            return $"DURATION {totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }

        private static Color AccentForPlacement(int placement)
        {
            return placement switch
            {
                1 => new Color(1f, 0.78f, 0.13f),
                2 => new Color(0.91f, 0.36f, 0.16f),
                3 => new Color(0.31f, 0.71f, 0.98f),
                _ => new Color(0.29f, 0.86f, 0.53f),
            };
        }

        private readonly struct PlayerMetadata
        {
            public PlayerMetadata(string playerName, string fighterName, CharacterClass @class)
            {
                PlayerName = playerName;
                FighterName = fighterName;
                Class = @class;
            }

            public string PlayerName { get; }
            public string FighterName { get; }
            public CharacterClass Class { get; }
        }
    }
}
