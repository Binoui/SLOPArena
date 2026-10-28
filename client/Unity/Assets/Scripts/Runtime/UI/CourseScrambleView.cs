using System;
using System.Linq;
using System.Collections.Generic;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    internal sealed class CourseScrambleView : IDisposable
    {
        private readonly CharacterCourseView _course;
        private readonly VisualElement _board, _results, _rankings;
        private readonly Label _winner;
        private readonly Button _retry;
        private bool _shown;
        private readonly Dictionary<ulong, Label> _rows = new();
        public CourseScrambleView(VisualElement hud, CharacterCourseView course, Action retry, Action leave)
        {
            _course = course;
            _board = new VisualElement { name = "scramble-scoreboard", pickingMode = PickingMode.Ignore };
            _board.AddToClassList("course-online-roster"); _course.AttachOnlineRoster(_board);
            _results = new VisualElement { name = "scramble-results" }; _results.AddToClassList("course-results");
            var style = Resources.Load<StyleSheet>("UI/Arcade"); if (style != null) _results.styleSheets.Add(style);
            _winner = new Label(); _winner.AddToClassList("arcade-mode-title"); _results.Add(_winner);
            _rankings = new VisualElement(); _results.Add(_rankings);
            var actions = new VisualElement(); actions.AddToClassList("course-actions");
            _retry = new Button(retry) { name = "scramble-retry", text = "RACE AGAIN" }; _retry.AddToClassList("btn-primary");
            var back = new Button(leave) { name = "scramble-leave", text = "LOCAL PLAY" }; back.AddToClassList("btn-secondary");
            actions.Add(_retry); actions.Add(back); _results.Add(actions); hud.Add(_results); Reset();
        }
        public void Present(ServerSimulation sim, bool paused)
        {
            var run = sim.OnlineCourse.Value;
            ulong leader = sim.Scramble.Value.LeaderEntityId;
            var local = sim.CourseFor(leader == 0 ? sim.CoursePlayers[0].EntityId : leader).Value;
            _course.Present(local, paused || run.Ended, null, false, null);
            _course.PresentOnlineStatus(run.FinishWindowTicksLeft > 0
                ? $"{(run.FinishWindowTicksLeft + 59) / 60}s LEFT TO FINISH"
                : local.BrokenTargets == local.TotalTargets ? "FINISH OPEN · RACE TO THE FINISH" : "SHARED TARGETS · MOST POINTS WINS",
                "FALLS AND STRAGGLERS RETURN TO THE LEADER", leader == 0 ? "GET READY" : $"LEADER P{leader} · CATCH-UP ENABLED");
            foreach (var player in sim.CoursePlayers)
            {
                string place = player.Place == 0 ? "RACING" : $"FINISH #{player.Place}";
                if (!_rows.TryGetValue(player.EntityId, out var row))
                {
                    row = new Label { name = $"scramble-p{player.EntityId}" };
                    row.AddToClassList("course-online-player"); row.style.color = SeatColours.For((int)player.EntityId);
                    row.style.borderLeftColor = SeatColours.For((int)player.EntityId); _board.Add(row); _rows.Add(player.EntityId, row);
                }
                row.text = $"P{player.EntityId}   {player.Points} PTS   {place}";
            }
            _results.style.display = run.Ended && !paused ? DisplayStyle.Flex : DisplayStyle.None;
            if (!run.Ended || _shown) return;
            _shown = true;
            var result = CourseRunResult.From(sim); int highest = result.Players.Max(p => p.Points);
            var winners = result.RankedPlayers.Where(p => p.Points == highest).Select(p => $"P{p.EntityId}");
            _winner.text = string.Join(" + ", winners) + " WIN" + (result.Players.Count(p => p.Points == highest) == 1 ? "S" : "");
            foreach (var player in result.RankedPlayers)
            {
                string finish = player.Place == 0 ? "DNF" : $"FINISH #{player.Place} · {CharacterCourseView.FormatTime(player.FinishTicks)}";
                var row = new Label($"P{player.EntityId}   {player.Points} PTS   {finish}");
                row.AddToClassList("slopball-count"); row.style.color = SeatColours.For((int)player.EntityId); _rankings.Add(row);
            }
            _retry.Focus();
        }
        public void Reset() { _shown = false; _rankings.Clear(); _results.style.display = DisplayStyle.None; _retry.Blur(); }
        public void Dispose() { _board.RemoveFromHierarchy(); _results.RemoveFromHierarchy(); }
    }
}
