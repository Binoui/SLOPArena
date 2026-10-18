using System;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    internal sealed class SlopballView : IDisposable
    {
        private readonly VisualElement _live;
        private readonly Label _count, _cue, _ball, _feedback;
        private readonly Label _streakToast;
        private float _toastTimer;
        private const float ToastDuration = 1.15f;
        private bool _paused;

        public SlopballView(VisualElement hud)
        {
            _live = new VisualElement { name = "slopball-live", pickingMode = PickingMode.Ignore };
            _live.AddToClassList("slopball-live");
            var style = Resources.Load<StyleSheet>("UI/Arcade");
            if (style != null) _live.styleSheets.Add(style);
            AddLabel("SLOPBALL", "arcade-eyebrow");
            _cue = AddLabel("Get ready.", "slopball-cue");
            _count = AddLabel("", "slopball-count"); _count.name = "slopball-count";
            _ball = AddLabel("", "arcade-eyebrow");
            _feedback = AddLabel("", "arcade-feedback");
            _feedback.style.display = DisplayStyle.None;
            AddLabel("BACKSPACE / HOLD SELECT RESTART  ·  ESC / START PAUSE", "slopball-controls");
            _streakToast = AddLabel("", "slopball-streak-toast");
            _streakToast.name = "slopball-streak-toast";
            _streakToast.style.display = DisplayStyle.None;
            hud.Add(_live);
        }

        private Label AddLabel(string text, string css)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(css); _live.Add(label); return label;
        }

        public void Present(SlopballState state, ArcadeBallState ball, ulong personalBest, string feedback)
        {
            _count.text = $"Combo {state.Returns}  ·  Best {Math.Max(personalBest, state.BestStreak)}";
            _cue.text = state.Phase switch
            {
                SlopballPhase.Countdown => "Get ready. Release held controls.",
                SlopballPhase.WaitingServe => $"New ball in {state.ServeInTicks / 60f:0.0}s",
                _ => "Hit, juggle, try a bank shot.",
            };
            _ball.text = ball.Active ? $"BALL {ball.DamagePercent:0}%  ·  UNLIMITED TOUCHES" : "";
            _feedback.text = feedback ?? "";
            _feedback.style.display = string.IsNullOrEmpty(feedback) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            _live.style.display = paused ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void ShowStreakToast(bool dropped)
        {
            _streakToast.text = dropped ? "STREAK DROPPED" : "STREAK +1";
            _streakToast.EnableInClassList("slopball-streak-toast--dropped", dropped);
            _toastTimer = ToastDuration;
            _streakToast.style.display = DisplayStyle.Flex;
            _streakToast.style.opacity = 1f;
            _streakToast.style.scale = new Scale(Vector2.one);
        }

        public void Tick(float dt)
        {
            if (_paused || _toastTimer <= 0f) return;
            _toastTimer = Mathf.Max(0f, _toastTimer - dt);
            float elapsed = ToastDuration - _toastTimer;
            float scale = elapsed < .1f ? Mathf.Lerp(.88f, 1.04f, elapsed / .1f)
                : elapsed < .18f ? Mathf.Lerp(1.04f, 1f, (elapsed - .1f) / .08f) : 1f;
            _streakToast.style.scale = new Scale(Vector2.one *
                (ClientSettingsService.Instance.ReducedFlashing ? 1f : scale));
            _streakToast.style.opacity = Mathf.Clamp01(_toastTimer / .18f);
            if (_toastTimer <= 0f) _streakToast.style.display = DisplayStyle.None;
        }
        public void Dispose() => _live.RemoveFromHierarchy();
    }
}
