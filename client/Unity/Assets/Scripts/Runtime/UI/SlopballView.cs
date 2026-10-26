using System;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    internal sealed class SlopballView : IDisposable
    {
        private const float ToastSeconds = 2.6f;
        private const float PopSeconds = 1f;
        private readonly VisualElement _hud, _live, _toast, _chip, _heat;
        private readonly Label _count, _cue, _ball, _feedback, _pace, _toastTitle, _toastLine, _chipHeight;
        private readonly VisualElement[] _heatPips = new VisualElement[SlopballDefinition.MaxHeat];
        private readonly StyleSheet _arcade, _slopball;
        private readonly bool _cooperative;
        // A pop floats up from a world point; callouts stack under the combo pop (line 1, 2, …). Side 0 centres the
        // column on the point, +1 starts it there (left-aligned) and -1 ends it there (right-aligned).
        private readonly System.Collections.Generic.List<(Label label, Vector3 world, float age, int line, int side)> _pops = new();
        private int _shownHeat = -1;
        private float _toastAge = -1f;
        private bool _paused;

        public SlopballView(VisualElement hud, bool cooperative = false)
        {
            _cooperative = cooperative;
            _hud = hud;
            _live = new VisualElement { name = "slopball-live", pickingMode = PickingMode.Ignore };
            _live.AddToClassList("slopball-live");
            _arcade = Resources.Load<StyleSheet>("UI/Arcade");
            _slopball = Resources.Load<StyleSheet>("UI/Slopball");
            Style(_live);
            AddLabel(_live, "SLOPBALL", "arcade-eyebrow");
            _cue = AddLabel(_live, "Get ready.", "slopball-cue");
            _count = AddLabel(_live, "", "slopball-count"); _count.name = "slopball-count";
            // Rally heat: one pip per step, every five returns; a miss empties it.
            _heat = new VisualElement { name = "slopball-heat", pickingMode = PickingMode.Ignore };
            _heat.AddToClassList("slopball-heat");
            AddLabel(_heat, "HEAT", "slopball-heat-label");
            for (int i = 0; i < _heatPips.Length; i++)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList("slopball-heat-pip");
                _heat.Add(pip); _heatPips[i] = pip;
            }
            _live.Add(_heat);
            _ball = AddLabel(_live, "", "arcade-eyebrow");
            _pace = AddLabel(_live, "", "slopball-pace"); _pace.name = "slopball-pace";
            _pace.style.display = DisplayStyle.None;
            _feedback = AddLabel(_live, "", "arcade-feedback");
            _feedback.style.display = DisplayStyle.None;
            AddLabel(_live, cooperative ? "ESC / START MENU  ·  LEADER CAN RETURN TO LOBBY"
                : "BACKSPACE / HOLD SELECT RESTART  ·  ESC / START PAUSE", "slopball-controls");
            // The dropped-combo card sits beside the panel, like the course's toasts.
            _toast = new VisualElement { name = "slopball-toast", pickingMode = PickingMode.Ignore };
            _toast.AddToClassList("course-toast");
            _toast.AddToClassList("course-toast--warning");
            AddLabel(_toast, "", "course-toast-tape");
            _toastTitle = AddLabel(_toast, "", "course-toast-title");
            _toastLine = AddLabel(_toast, "", "course-toast-line");
            _live.Add(_toast);
            hud.Add(_live);
            // A chip on the top edge while the ball is above the screen: an arrow, the ball and its height.
            _chip = new VisualElement { name = "slopball-ball-chip", pickingMode = PickingMode.Ignore };
            _chip.AddToClassList("slopball-ball-chip");
            Style(_chip);
            AddLabel(_chip, "", "slopball-ball-chip-arrow");
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("slopball-ball-chip-ball");
            _chip.Add(dot);
            _chipHeight = AddLabel(_chip, "", "slopball-ball-chip-height");
            _chip.style.display = DisplayStyle.None;
            hud.Add(_chip);
        }

        private void Style(VisualElement element)
        {
            if (_arcade != null) element.styleSheets.Add(_arcade);
            if (_slopball != null) element.styleSheets.Add(_slopball);
        }

        private static Label AddLabel(VisualElement parent, string text, string css)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(css); parent.Add(label); return label;
        }

        public void Present(SlopballState state, ArcadeBallState ball, ulong personalBest, string feedback)
        {
            _count.text = _cooperative
                ? $"Team {state.TotalReturns}  ·  Combo {state.Returns}  ·  Best {state.BestStreak}"
                : $"Combo {state.Returns}  ·  Best {Math.Max(personalBest, state.BestStreak)}";
            _cue.text = state.Phase switch
            {
                SlopballPhase.Countdown => "Get ready. Release held controls.",
                SlopballPhase.WaitingServe => $"New ball in {state.ServeInTicks / 60f:0.0}s{ServeName(state.Shot.Kind)}",
                _ => "Hit, juggle, try a bank shot.",
            };
            SetHeat(state.Heat);
            _ball.text = ball.Active ? $"BALL {ball.DamagePercent:0}%  ·  UNLIMITED TOUCHES" : "";
            _feedback.text = feedback ?? "";
            _feedback.style.display = string.IsNullOrEmpty(feedback) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static string ServeName(SlopballServe kind) => kind switch
        {
            SlopballServe.Zinger => "  ·  ZINGER", SlopballServe.Moonball => "  ·  MOONBALL", SlopballServe.Curveball => "  ·  CURVEBALL", _ => "",
        };

        public void PresentVersus(SlopballVersusState state, ArcadeBallState ball, ulong localId, SlopballCamp ownCamp)
        {
            _count.text = $"<color=#38E6D9>TEAL {state.TealScore}</color>  —  <color=#FF8C59>{state.ClayScore} CLAY</color>";
            _heat.style.display = DisplayStyle.None;
            _cue.text = state.Phase switch
            {
                SlopballVersusPhase.WaitingServe => $"{state.ServingCamp} serves in {state.ServeInTicks / 60f:0.0}s",
                SlopballVersusPhase.HeldServe => state.ServerEntityId == localId ? "YOUR SERVE · HIT THE BALL"
                    : $"{state.ServingCamp.ToString().ToUpperInvariant()} P{state.ServerEntityId} SERVES",
                SlopballVersusPhase.Ended => state.Winner.ToString().ToUpperInvariant() + " WINS",
                _ => "Hit, set, bank or spike. Unlimited touches.",
            };
            _ball.text = $"FIRST TO 21 · YOU ARE {ownCamp.ToString().ToUpperInvariant()}"
                + (ball.Active ? $" · BALL {ball.DamagePercent:0}%" : "");
        }

        public void ShowPoint(SlopballCamp scorer)
        {
            _toastTitle.text = scorer.ToString().ToUpperInvariant() + " POINT";
            _toastLine.text = "First to 21 · conceding camp serves next";
            _toastAge = 0f; _toast.style.display = DisplayStyle.Flex; AnimateToast();
        }

        private void SetHeat(int heat)
        {
            if (heat == _shownHeat) return;
            _shownHeat = heat;
            for (int i = 0; i < _heatPips.Length; i++)
            {
                _heatPips[i].EnableInClassList("slopball-heat-pip--lit", i < heat);
                for (int level = 1; level <= _heatPips.Length; level++)
                    _heatPips[i].EnableInClassList($"slopball-heat-pip--h{level}", i < heat && heat == level);
            }
        }

        /// <summary>The temporary pace trial: which serve profile this attempt uses.</summary>
        public void SetPace(string label)
        {
            _pace.text = string.IsNullOrEmpty(label) ? "" : $"PACE · {label}  (change it in Pause)";
            _pace.style.display = string.IsNullOrEmpty(label) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            _live.style.display = paused ? DisplayStyle.None : DisplayStyle.Flex;
            if (paused) _chip.style.display = DisplayStyle.None;
        }

        /// <summary>A combo ended on your side: how long it was, with your best beside it.</summary>
        public void ShowDropToast(ulong combo, ulong best)
        {
            _toastTitle.text = $"COMBO {combo} DROPPED";
            _toastLine.text = combo >= best ? $"That's your best: {combo}." : $"Best {best}.";
            _toastAge = 0f;
            _toast.style.display = DisplayStyle.Flex;
            AnimateToast();
        }

        /// <summary>The running combo pops up beside where a return landed; every fifth is bigger.</summary>
        public void ShowCountPop(Vector3 world, string text, bool big, int side = 0)
            => AddPop(world, text, big ? "slopball-count-pop--big" : null, 0, side);

        /// <summary>A trick shot or HEAT UP! under the combo pop, on <paramref name="line"/> (1, 2, …).</summary>
        public void ShowCallout(Vector3 world, string text, bool heat, int line, int side = 0)
            => AddPop(world, text, heat ? "slopball-callout--heat" : "slopball-callout", line, side);

        private void AddPop(Vector3 world, string text, string variant, int line, int side)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("course-count-pop");
            label.AddToClassList(line == 0 ? "slopball-count-pop" : "slopball-callout-base");
            if (variant != null) label.AddToClassList(variant);
            Style(label);
            label.style.display = DisplayStyle.None;
            if (side != 0) label.style.translate = new Translate(Length.Percent(side > 0 ? 0f : -100f), Length.Percent(-50f), 0f);
            _hud.Add(label);
            _pops.Add((label, world, line * -.12f, line, side));
            if (_pops.Count > 8) { _pops[0].label.RemoveFromHierarchy(); _pops.RemoveAt(0); }
        }

        /// <summary>While the live ball is above the top of the screen, a chip follows it along the top edge.</summary>
        public void PresentBallChip(UnityEngine.Camera camera, Vector3 world, float height, bool live)
        {
            float width = _hud.layout.width;
            bool show = false;
            if (live && !_paused && camera != null && float.IsFinite(width) && width > 0f)
            {
                var viewport = camera.WorldToViewportPoint(world);
                if (viewport.z > 0f && viewport.y > 1.02f)
                {
                    show = true;
                    _chip.style.left = Mathf.Clamp(viewport.x, .06f, .94f) * width;
                    _chipHeight.text = $"{height:0} m";
                }
            }
            _chip.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Tick(float dt, UnityEngine.Camera camera)
        {
            if (_toastAge >= 0f && !_paused)
            {
                _toastAge += dt;
                if (_toastAge >= ToastSeconds) { _toastAge = -1f; _toast.style.display = DisplayStyle.None; }
                else AnimateToast();
            }
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                var (label, world, age, line, side) = _pops[i];
                if (!_paused) age += dt;
                float life = line == 0 ? PopSeconds : PopSeconds + .5f;
                if (age >= life || camera == null) { label.RemoveFromHierarchy(); _pops.RemoveAt(i); continue; }
                _pops[i] = (label, world, age, line, side);
                // Callouts wait a beat after the combo pop (their age starts below zero).
                var viewport = camera.WorldToViewportPoint(world);
                if (age < 0f || viewport.z <= 0f || _paused || label.panel == null) { label.style.display = DisplayStyle.None; continue; }
                var point = RuntimePanelUtils.CameraTransformWorldToPanel(label.panel, world, camera);
                float pop = 1f + .35f * Mathf.Exp(-age / .06f);
                label.style.display = DisplayStyle.Flex;
                label.style.left = point.x;
                label.style.top = point.y + line * 40f - 54f * (1f - Mathf.Pow(1f - Mathf.Clamp01(age / PopSeconds), 2f));
                label.style.scale = new Scale(Vector2.one * (ClientSettingsService.Instance.ReducedFlashing ? 1f : pop));
                label.style.opacity = 1f - Mathf.Clamp01((age - (life - .3f)) / .3f);
            }
        }

        private void AnimateToast()
        {
            float enter = Mathf.Clamp01(_toastAge / .14f), exit = Mathf.Clamp01((_toastAge - (ToastSeconds - .4f)) / .4f);
            _toast.style.opacity = enter * (1f - exit);
            _toast.style.translate = new Translate(14f * (1f - enter), 0f, 0f);
        }

        public void Dispose()
        {
            _live.RemoveFromHierarchy();
            _chip.RemoveFromHierarchy();
            foreach (var (label, _, _, _, _) in _pops) label.RemoveFromHierarchy();
        }
    }
}
