using System;
using SlopArena.Client.Input;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    internal sealed class SlopballView : IDisposable
    {
        private readonly VisualElement _live, _result;
        private readonly Label _count, _cue, _ball, _score, _best, _improvement, _feedback, _heading;
        private readonly Button _retry;
        private readonly Action _back;
        private bool _resultVisible, _paused;

        public SlopballView(VisualElement hud, Action retry, Action back)
        {
            _back = back;
            var style = Resources.Load<StyleSheet>("UI/Arcade");
            _live = new VisualElement { name = "slopball-live", pickingMode = PickingMode.Ignore };
            _live.AddToClassList("slopball-live");
            if (style != null) _live.styleSheets.Add(style);
            AddLabel(_live, "SLOPBALL", "arcade-eyebrow");
            _count = AddLabel(_live, "0 RETURNS", "slopball-count");
            _cue = AddLabel(_live, "Get ready.", "slopball-cue");
            _ball = AddLabel(_live, "", "arcade-eyebrow");
            AddLabel(_live, "BACKSPACE RETRY  ·  ESC / START PAUSE", "slopball-controls");
            hud.Add(_live);

            _result = new VisualElement { name = "slopball-result", pickingMode = PickingMode.Position };
            _result.AddToClassList("arcade-result-overlay");
            if (style != null) _result.styleSheets.Add(style);
            _result.style.display = DisplayStyle.None;
            var card = new VisualElement(); card.AddToClassList("arcade-result-card"); _result.Add(card);
            _heading = AddLabel(card, "FLOOR HIT", "arcade-result-heading");
            _score = AddLabel(card, "0", "arcade-result-score"); _score.name = "slopball-result-score";
            AddLabel(card, "RETURNS", "arcade-eyebrow");
            _best = AddLabel(card, "", "arcade-result-best"); _best.name = "slopball-result-best";
            _improvement = AddLabel(card, "", "arcade-result-improvement");
            _feedback = AddLabel(card, "", "arcade-feedback");
            var actions = new VisualElement(); actions.AddToClassList("arcade-result-actions"); card.Add(actions);
            _retry = new Button(retry) { name = "slopball-retry", text = "RETRY" }; _retry.AddToClassList("arcade-result-button"); actions.Add(_retry);
            var leave = new Button(back) { name = "slopball-back", text = "ARCADE" }; leave.AddToClassList("arcade-result-button"); actions.Add(leave);
            hud.Add(_result);
        }

        private static Label AddLabel(VisualElement parent, string text, string css)
        { var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(css); parent.Add(label); return label; }

        public void Present(SlopballState state, ArcadeBallState ball)
        {
            _count.text = $"{state.Returns} {ReturnUnit(state.Returns)}";
            _cue.text = state.Phase switch
            {
                SlopballPhase.Countdown => "Start from here. Release held controls.",
                SlopballPhase.WaitingServe => $"Next serve in {state.ServeInTicks / 60f:0.0}s",
                _ => !state.Received ? "Receive on your side."
                    : !state.ReturnArmed ? "Hit the ball, then clear the net." : "Clear the net. Banks count!",
            };
            _ball.text = ball.Active ? $"BALL {ball.DamagePercent:0}%  ·  UNLIMITED TOUCHES" : "";
        }

        public void ShowResult(ulong returns, ArcadeRecordUpdate result, bool missed, string feedback)
        {
            if (_resultVisible) return;
            _resultVisible = true;
            UiModalState.Push();
            _heading.text = missed ? "FLOOR HIT!" : "RUN BANKED";
            _score.text = returns.ToString();
            if (_score.text.Length > 5) _score.style.fontSize = Mathf.Max(24, 360 / _score.text.Length);
            _best.text = $"PERSONAL BEST  {result.PersonalBest}";
            _improvement.text = !result.PreviousBest.HasValue ? "FIRST PERSONAL BEST"
                : result.Improved ? $"NEW BEST!  +{returns - result.PreviousBest.Value} {ReturnUnit(returns - result.PreviousBest.Value)}"
                : returns == result.PersonalBest ? "PERSONAL BEST MATCHED"
                : $"{result.PersonalBest - returns} {ReturnUnit(result.PersonalBest - returns)} FROM YOUR BEST";
            _feedback.text = feedback ?? "";
            _live.style.display = DisplayStyle.None;
            _result.style.display = DisplayStyle.Flex;
            _result.BringToFront();
            _retry.schedule.Execute(() => { if (!_paused && _result.panel != null) _retry.Focus(); });
        }

        public void SetPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            if (_resultVisible) _result.style.display = paused ? DisplayStyle.None : DisplayStyle.Flex;
            _live.style.display = paused || _resultVisible ? DisplayStyle.None : DisplayStyle.Flex;
            if (_resultVisible && !paused) _retry.Focus();
        }

        public void PumpResultInput()
        {
            if (!_resultVisible || _paused || ChatInputGate.SuppressShortcuts || SettingsOverlay.Active is { IsOpen: true }) return;
            GamepadUIBridge.Pump(_result.panel, cancel: false);
            var pad = Gamepad.current;
            if (pad?.buttonEast.wasPressedThisFrame == true) _back();
        }

        public void Dispose()
        {
            if (_resultVisible) UiModalState.Pop();
            _live.RemoveFromHierarchy(); _result.RemoveFromHierarchy();
        }

        private static string ReturnUnit(ulong count) => count == 1 ? "RETURN" : "RETURNS";
    }
}
