using System;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    internal sealed class CharacterCourseView : IDisposable
    {
        private readonly VisualElement _live, _results;
        private readonly Label _time, _targets, _checkpoint, _cue, _resultTime, _best, _feedback;
        private readonly Label _tip;
        private readonly Button _retry;
        public CharacterCourseView(VisualElement hud, Action retry, Action leave)
        {
            _live = new VisualElement { name = "course-live", pickingMode = PickingMode.Ignore };
            _live.AddToClassList("slopball-live");
            var style = Resources.Load<StyleSheet>("UI/Arcade");
            if (style != null) _live.styleSheets.Add(style);
            Add(_live, "SANCTUARY COURSE", "arcade-eyebrow");
            _time = Add(_live, "0:00.00", "course-time"); _time.name = "course-time";
            _targets = Add(_live, "", "slopball-count"); _targets.name = "course-targets";
            _cue = Add(_live, "Break every target, then reach the shrine.", "slopball-cue");
            _checkpoint = Add(_live, "", "arcade-eyebrow"); _checkpoint.name = "course-checkpoint";
            _tip = Add(_live, "", "slopball-cue");
            _tip.style.display = SlopArena.Client.World.CharacterCourseMatch.MovementTipsEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            Add(_live, "BACKSPACE RETRY  ·  ESC / START PAUSE", "slopball-controls");
            hud.Add(_live);
            _results = new VisualElement { name = "course-results" }; _results.AddToClassList("course-results");
            if (style != null) _results.styleSheets.Add(style);
            Add(_results, "COURSE COMPLETE", "arcade-mode-title");
            _resultTime = Add(_results, "", "course-time");
            _best = Add(_results, "", "slopball-count");
            _feedback = Add(_results, "", "arcade-feedback");
            var actions = new VisualElement(); actions.AddToClassList("course-actions");
            _retry = new Button(retry) { name = "course-retry", text = "RETRY" }; _retry.AddToClassList("btn-primary");
            var back = new Button(leave) { name = "course-arcade", text = "ARCADE" }; back.AddToClassList("btn-secondary");
            actions.Add(_retry); actions.Add(back); _results.Add(actions); hud.Add(_results);
            _results.style.display = DisplayStyle.None;
        }

        public static string FormatTime(ulong ticks) => $"{ticks / 3600}:{ticks / 60 % 60:00}.{ticks % 60 * 100 / 60:00}";

        public void Present(CharacterCourseState state, bool paused, ulong? best, bool improved, string feedback)
        {
            bool completed = state.Phase == CharacterCoursePhase.Completed;
            bool newlyShown = completed && _results.style.display == DisplayStyle.None && !paused;
            _live.style.display = paused || completed ? DisplayStyle.None : DisplayStyle.Flex;
            _results.style.display = completed && !paused ? DisplayStyle.Flex : DisplayStyle.None;
            _time.text = FormatTime(state.ElapsedTicks);
            _targets.text = $"TARGETS {state.BrokenTargets} / {state.TotalTargets}";
            _checkpoint.text = state.SavedCheckpoint < 0 ? "FALLS RETURN TO START" : $"CHECKPOINT {state.SavedCheckpoint + 1} SAVED";
            _cue.text = state.Phase == CharacterCoursePhase.Countdown ? "Get ready. Release held controls."
                : state.BrokenTargets == state.TotalTargets ? "All targets cleared. Reach the finish shrine."
                : "Break every target, then reach the shrine.";
            _tip.text = state.SavedCheckpoint switch
            {
                < 0 => "Jump then R to cross the broken bridge, or use the pole detour. Any route counts.",
                0 => "Jump then E to reach the upper terrace, or climb the staggered ledges.",
                1 => "The summit shortcut combines Jump, E, then R. Islands offer another route.",
                _ => "Press Down again while descending to FastFall after committed move motion ends.",
            };
            if (!completed) return;
            _resultTime.text = FormatTime(state.ElapsedTicks);
            _best.text = improved ? "NEW PERSONAL BEST" : best.HasValue ? "BEST " + FormatTime(best.Value) : "";
            _feedback.text = feedback ?? "";
            if (newlyShown) _retry.Focus();
        }
        private static Label Add(VisualElement parent, string text, string css)
        { var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(css); parent.Add(label); return label; }
        public void Dispose() { _live.RemoveFromHierarchy(); _results.RemoveFromHierarchy(); }
    }
}
