using System;
using System.Collections.Generic;
using SlopArena.Client.Input;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    internal sealed class CharacterCourseView : IDisposable
    {
        /// <summary>The finish celebration plays this long before the results card replaces the live panel.</summary>
        public const float ResultsDelay = 1f;
        private const float ToastSeconds = 3.6f;
        private const float PopSeconds = 1f;
        private const float NotSavedSeconds = 3f;
        private readonly VisualElement _hud, _live, _results, _hold, _holdFill, _pips, _toast;
        private readonly Label _time, _targets, _checkpoint, _cue, _resultTime, _best, _feedback, _toastTitle, _toastLine;
        private readonly Label _tip, _controls;
        private readonly Button _retry, _leave;
        private readonly List<VisualElement> _pipViews = new();
        private readonly List<(Label label, Vector3 world, float age)> _pops = new();
        private readonly List<VisualElement> _markers = new();
        private byte[] _pipLooks = Array.Empty<byte>();
        private bool _controlsGamepad, _paused;
        private readonly bool _online;
        private int _controlsRevision = -1;
        private float _toastAge = -1f, _notSaved, _completedAge = -1f;
        private readonly SlopArena.Client.World.CharacterCourseInfo _info;
        public CharacterCourseView(VisualElement hud, SlopArena.Client.World.CharacterCourseInfo info, Action retry, Action leave, bool online = false)
        {
            _hud = hud; _info = info; _online = online;
            _live = new VisualElement { name = "course-live", pickingMode = PickingMode.Ignore };
            _live.AddToClassList("slopball-live");
            if (online) _live.AddToClassList("course-live--online");
            var style = Resources.Load<StyleSheet>("UI/Arcade");
            if (style != null) _live.styleSheets.Add(style);
            Add(_live, info.Title, "arcade-eyebrow");
            _time = Add(_live, "0:00.00", "course-time"); _time.name = "course-time";
            _targets = Add(_live, "", "slopball-count"); _targets.name = "course-targets";
            _pips = new VisualElement { name = "course-pips", pickingMode = PickingMode.Ignore };
            _pips.AddToClassList("course-pips"); _live.Add(_pips);
            _cue = Add(_live, $"Break every target, then reach {info.Finish}.", "slopball-cue");
            _checkpoint = Add(_live, "", "arcade-eyebrow"); _checkpoint.name = "course-checkpoint";
            _tip = Add(_live, "", "slopball-cue");
            _tip.style.display = SlopArena.Client.World.CharacterCourseMatch.MovementTipsEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            _controls = Add(_live, "", "slopball-controls"); _controls.name = "course-controls";
            _hold = new VisualElement { name = "course-retry-hold", pickingMode = PickingMode.Ignore };
            _hold.AddToClassList("course-retry-hold");
            _holdFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _holdFill.AddToClassList("course-retry-hold-fill");
            _hold.Add(_holdFill); _live.Add(_hold);
            _toast = new VisualElement { name = "course-toast", pickingMode = PickingMode.Ignore };
            _toast.AddToClassList("course-toast");
            Add(_toast, "", "course-toast-tape");
            _toastTitle = Add(_toast, "", "course-toast-title");
            _toastLine = Add(_toast, "", "course-toast-line");
            _live.Add(_toast);
            hud.Add(_live);
            _results = new VisualElement { name = "course-results" }; _results.AddToClassList("course-results");
            if (style != null) _results.styleSheets.Add(style);
            Add(_results, "COURSE COMPLETE", "arcade-mode-title");
            _resultTime = Add(_results, "", "course-time");
            _best = Add(_results, "", "slopball-count");
            _feedback = Add(_results, "", "arcade-feedback");
            var actions = new VisualElement(); actions.AddToClassList("course-actions");
            _retry = new Button(retry) { name = "course-retry", text = "RETRY" }; _retry.AddToClassList("btn-primary");
            _leave = new Button(leave) { name = "course-arcade", text = "ARCADE" }; _leave.AddToClassList("btn-secondary");
            actions.Add(_retry); actions.Add(_leave); _results.Add(actions); hud.Add(_results);
            _results.style.display = DisplayStyle.None;
        }

        public static string FormatTime(ulong ticks) => $"{ticks / 3600}:{ticks / 60 % 60:00}.{ticks % 60 * 100 / 60:00}";

        /// <summary>A retry hides the results; keep their buttons from holding focus into the new attempt.</summary>
        public void ResetForRetry()
        {
            _retry.Blur(); _leave.Blur();
            _toastAge = -1f; _toast.style.display = DisplayStyle.None;
            _notSaved = 0f; _completedAge = -1f;
            foreach (var (label, _, _) in _pops) label.RemoveFromHierarchy();
            _pops.Clear();
            foreach (var marker in _markers) marker.style.display = DisplayStyle.None;
        }

        /// <summary>Follow the device in use and any rebinding; a gamepad retry is a short hold.</summary>
        public void PresentRetryHold(float progress)
        {
            bool gamepad = HumanInputActions.LastUsedGamepad;
            if (gamepad != _controlsGamepad || HumanInputActions.BindingRevision != _controlsRevision)
            {
                _controlsGamepad = gamepad; _controlsRevision = HumanInputActions.BindingRevision;
                string group = gamepad ? HumanInputActions.GamepadGroup : HumanInputActions.KeyboardMouseGroup;
                string retry = HumanInputActions.BindingLabel("Retry", group).ToUpperInvariant();
                string pause = HumanInputActions.BindingLabel("Pause", group).ToUpperInvariant();
                _controls.text = gamepad ? $"HOLD {retry} RETRY  ·  {pause} PAUSE" : $"{retry} RETRY  ·  {pause} PAUSE";
            }
            _hold.style.display = progress > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            _holdFill.style.width = Length.Percent(progress * 100f);
        }

        /// <summary>One diamond per target, grouped by section, so a skipped target shows as a gap.</summary>
        public void ConfigurePips(IReadOnlyList<int> sections)
        {
            _pips.Clear(); _pipViews.Clear();
            foreach (int size in sections)
            {
                var group = new VisualElement { pickingMode = PickingMode.Ignore };
                group.AddToClassList("course-pip-group");
                for (int i = 0; i < size; i++)
                {
                    var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                    pip.AddToClassList("course-pip");
                    group.Add(pip); _pipViews.Add(pip);
                }
                _pips.Add(group);
            }
            _pipLooks = new byte[_pipViews.Count];
            Array.Fill(_pipLooks, byte.MaxValue);
        }

        public void PresentPips(IReadOnlyList<bool> broken, IReadOnlyList<bool> missed)
        {
            for (int i = 0; i < _pipViews.Count && i < broken.Count; i++)
            {
                byte look = broken[i] ? (byte)1 : missed[i] ? (byte)2 : (byte)0;
                if (_pipLooks[i] == look) continue;
                _pipLooks[i] = look;
                _pipViews[i].EnableInClassList("course-pip--broken", look == 1);
                _pipViews[i].EnableInClassList("course-pip--missed", look == 2);
            }
        }

        /// <summary>A card beside the panel: section clears in jade, missed targets in red.</summary>
        public void ShowToast(string title, string line, bool warning)
        {
            _toastTitle.text = title; _toastLine.text = line;
            _toast.EnableInClassList("course-toast--warning", warning);
            _toastAge = 0f;
            _toast.style.display = DisplayStyle.Flex;
            AnimateToast();
        }

        /// <summary>The checkpoint line turns red for a moment after reaching a checkpoint that cannot save.</summary>
        public void FlashCheckpointNotSaved() => _notSaved = NotSavedSeconds;

        /// <summary>The running count floats up from a broken target (or a bonus target's hit count).</summary>
        public void ShowCountPop(Vector3 world, string text, string variant = null, Color? colour = null)
        {
            // A target's next count replaces its last, so a flurry of bonus hits reads as one climbing number.
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                var other = _pops[i].world;
                if (new Vector2(other.x - world.x, other.z - world.z).sqrMagnitude < .36f && Mathf.Abs(other.y - world.y) < 1f)
                {
                    _pops[i].label.RemoveFromHierarchy();
                    _pops.RemoveAt(i);
                }
            }
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("course-count-pop");
            if (!string.IsNullOrEmpty(variant)) label.AddToClassList(variant);
            if (_live.styleSheets.count > 0) label.styleSheets.Add(_live.styleSheets[0]);
            if (colour.HasValue) label.style.color = colour.Value;
            label.style.display = DisplayStyle.None;
            _hud.Add(label);
            _pops.Add((label, world, 0f));
            if (_pops.Count > 6) { _pops[0].label.RemoveFromHierarchy(); _pops.RemoveAt(0); }
        }

        /// <summary>Red diamonds on the screen edge toward missed targets that are out of view.</summary>
        public void PresentEdgeMarkers(UnityEngine.Camera camera, IReadOnlyList<Vector3> targets)
        {
            int shown = 0;
            float width = _hud.layout.width, height = _hud.layout.height;
            if (camera != null && !_paused && float.IsFinite(width) && width > 0f && height > 0f)
            {
                foreach (var target in targets)
                {
                    var viewport = camera.WorldToViewportPoint(target);
                    if (viewport.z > 0f && viewport.x > .03f && viewport.x < .97f && viewport.y > .05f && viewport.y < .95f) continue;
                    var direction = new Vector2(viewport.x - .5f, viewport.y - .5f);
                    if (viewport.z < 0f) direction = -direction;
                    if (direction.sqrMagnitude < 1e-6f) direction = Vector2.down;
                    float scale = Mathf.Min(.44f / Mathf.Max(1e-4f, Mathf.Abs(direction.x)), .42f / Mathf.Max(1e-4f, Mathf.Abs(direction.y)));
                    var edge = new Vector2(.5f, .5f) + direction * scale;
                    if (shown == _markers.Count)
                    {
                        var marker = new VisualElement { pickingMode = PickingMode.Ignore };
                        marker.AddToClassList("course-edge-marker");
                        if (_live.styleSheets.count > 0) marker.styleSheets.Add(_live.styleSheets[0]);
                        _hud.Add(marker); _markers.Add(marker);
                    }
                    var view = _markers[shown++];
                    view.style.display = DisplayStyle.Flex;
                    view.style.left = edge.x * width;
                    view.style.top = (1f - edge.y) * height;
                    if (shown == 4) break;
                }
            }
            for (int i = shown; i < _markers.Count; i++) _markers[i].style.display = DisplayStyle.None;
        }

        /// <summary>Per frame: toast, count pops, the not-saved flash and the results delay.</summary>
        public void Tick(float deltaTime, UnityEngine.Camera camera, bool completed)
        {
            _completedAge = completed ? Mathf.Max(0f, _completedAge) + deltaTime : -1f;
            _notSaved = Mathf.Max(0f, _notSaved - deltaTime);
            if (_toastAge >= 0f)
            {
                _toastAge += deltaTime;
                if (_toastAge >= ToastSeconds) { _toastAge = -1f; _toast.style.display = DisplayStyle.None; }
                else AnimateToast();
            }
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                var (label, world, age) = _pops[i];
                age += deltaTime;
                if (age >= PopSeconds || camera == null) { label.RemoveFromHierarchy(); _pops.RemoveAt(i); continue; }
                _pops[i] = (label, world, age);
                var viewport = camera.WorldToViewportPoint(world);
                if (viewport.z <= 0f || _paused || label.panel == null) { label.style.display = DisplayStyle.None; continue; }
                var point = RuntimePanelUtils.CameraTransformWorldToPanel(label.panel, world, camera);
                float pop = 1f + .3f * Mathf.Exp(-age / .06f);
                label.style.display = DisplayStyle.Flex;
                label.style.left = point.x;
                label.style.top = point.y - 46f * (1f - Mathf.Pow(1f - age / PopSeconds, 2f));
                label.style.scale = new Scale(Vector2.one * pop);
                label.style.opacity = 1f - Mathf.Clamp01((age - (PopSeconds - .3f)) / .3f);
            }
        }

        private void AnimateToast()
        {
            float enter = Mathf.Clamp01(_toastAge / .14f), exit = Mathf.Clamp01((_toastAge - (ToastSeconds - .4f)) / .4f);
            _toast.style.opacity = enter * (1f - exit);
            _toast.style.translate = new Translate(14f * (1f - enter), 0f, 0f);
        }

        public void Present(CharacterCourseState state, bool paused, ulong? best, bool improved, string feedback)
        {
            _paused = paused;
            bool completed = state.Phase == CharacterCoursePhase.Completed;
            bool results = !_online && completed && _completedAge >= ResultsDelay;
            bool newlyShown = results && _results.style.display == DisplayStyle.None && !paused;
            _live.style.display = paused || results ? DisplayStyle.None : DisplayStyle.Flex;
            _results.style.display = results && !paused ? DisplayStyle.Flex : DisplayStyle.None;
            _time.text = FormatTime(state.ElapsedTicks);
            _targets.text = $"TARGETS {state.BrokenTargets} / {state.TotalTargets}";
            bool notSaved = _notSaved > 0f && !completed;
            _checkpoint.text = notSaved ? "CHECKPOINT NOT SAVED"
                : state.SavedCheckpoint < 0 ? "FALLS RETURN TO START" : $"CHECKPOINT {state.SavedCheckpoint + 1} SAVED";
            _checkpoint.EnableInClassList("course-checkpoint--warning", notSaved);
            _cue.text = state.Phase == CharacterCoursePhase.Countdown ? "Get ready. Release held controls."
                : completed ? "Course complete!"
                : state.BrokenTargets == state.TotalTargets ? $"All targets cleared. Reach {_info.Finish}."
                : $"Break every target, then reach {_info.Finish}.";
            _tip.text = _info.Tip(state.SavedCheckpoint);
            if (!results) return;
            _resultTime.text = FormatTime(state.ElapsedTicks);
            _best.text = improved ? "NEW PERSONAL BEST" : best.HasValue ? "BEST " + FormatTime(best.Value) : "";
            _feedback.text = feedback ?? "";
            if (newlyShown) _retry.Focus();
        }
        public void AttachOnlineRoster(VisualElement roster) => _live.Add(roster);

        public void PresentOnlineStatus(string status, string controls, string checkpoint = null)
        {
            _cue.text = status; _controls.text = controls;
            if (checkpoint != null) _checkpoint.text = checkpoint;
            _hold.style.display = DisplayStyle.None;
        }

        private static Label Add(VisualElement parent, string text, string css)
        { var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(css); parent.Add(label); return label; }
        public void Dispose()
        {
            _live.RemoveFromHierarchy(); _results.RemoveFromHierarchy();
            foreach (var (label, _, _) in _pops) label.RemoveFromHierarchy();
            foreach (var marker in _markers) marker.RemoveFromHierarchy();
        }
    }
}
