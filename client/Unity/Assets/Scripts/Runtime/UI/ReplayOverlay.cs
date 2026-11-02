using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// The Replay viewer's controls, drawn on the HUD document: transport, timeline, focus, track badge, a banner
    /// for the end or a failure, and labels over fighters. It only shows values and reports clicks.
    /// </summary>
    public sealed class ReplayOverlay
    {
        public sealed class Actions
        {
            public Action? TogglePlay, StepBack, StepForward, Slower, Faster, PreviousFocus, NextFocus, ToggleNames, Leave;
            public Action<uint>? Seek;
        }

        private readonly VisualElement _root;
        private readonly VisualElement _bar;
        private readonly VisualElement _labelLayer;
        private readonly Button _play;
        private readonly Label _speed, _time, _focus, _track, _banner;
        private readonly SliderInt _timeline;
        private readonly Dictionary<ulong, Label> _labels = new();
        private bool _hidden;

        public ReplayOverlay(VisualElement host, Actions actions, string track, uint first, uint last)
        {
            _root = new VisualElement { name = "replay-overlay", pickingMode = PickingMode.Ignore };
            Fill(_root.style);
            host.Add(_root);

            _labelLayer = new VisualElement { name = "replay-labels", pickingMode = PickingMode.Ignore };
            Fill(_labelLayer.style);
            _root.Add(_labelLayer);

            _banner = new Label { name = "replay-banner", pickingMode = PickingMode.Ignore };
            _banner.style.position = Position.Absolute;
            _banner.style.top = 96;
            _banner.style.alignSelf = Align.Center;
            _banner.style.left = 0;
            _banner.style.right = 0;
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _banner.style.fontSize = 26;
            _banner.style.color = Color.white;
            _banner.style.display = DisplayStyle.None;
            _root.Add(_banner);

            _bar = new VisualElement { name = "replay-transport" };
            _bar.style.position = Position.Absolute;
            // Top edge: the HUD's move deck owns the bottom; the fight cards own the top-left corner.
            _bar.style.left = 236;
            _bar.style.right = 16;
            _bar.style.top = 10;
            _bar.style.flexDirection = FlexDirection.Row;
            _bar.style.alignItems = Align.Center;
            _bar.style.backgroundColor = new Color(0.06f, 0.07f, 0.1f, 0.82f);
            _bar.style.paddingLeft = _bar.style.paddingRight = 10;
            _bar.style.paddingTop = _bar.style.paddingBottom = 6;
            SetRadius(_bar.style, 10);
            _root.Add(_bar);

            _bar.Add(Make("replay-step-back", "◀|", actions.StepBack));
            _play = Make("replay-play", "❚❚", actions.TogglePlay);
            _bar.Add(_play);
            _bar.Add(Make("replay-step-forward", "|▶", actions.StepForward));
            _bar.Add(Make("replay-slower", "−", actions.Slower));
            _speed = Text("replay-speed", "1×", 44);
            _bar.Add(_speed);
            _bar.Add(Make("replay-faster", "+", actions.Faster));

            _timeline = new SliderInt((int)first, (int)last) { name = "replay-timeline" };
            _timeline.style.flexGrow = 1;
            _timeline.style.marginLeft = _timeline.style.marginRight = 10;
            _timeline.RegisterValueChangedCallback(e => actions.Seek?.Invoke((uint)e.newValue));
            _bar.Add(_timeline);
            _time = Text("replay-time", "0:00", 92);
            _bar.Add(_time);

            _bar.Add(Make("replay-focus-previous", "‹", actions.PreviousFocus));
            _focus = Text("replay-focus", "", 170);
            _bar.Add(_focus);
            _bar.Add(Make("replay-focus-next", "›", actions.NextFocus));
            _track = Text("replay-track", track, 150);
            _track.style.color = track.StartsWith("EXACT", StringComparison.Ordinal) ? new Color(0.5f, 0.95f, 0.6f) : new Color(1f, 0.8f, 0.35f);
            _bar.Add(_track);
            _bar.Add(Make("replay-names", "Names", actions.ToggleNames));
            _bar.Add(Make("replay-leave", "Leave", actions.Leave));

            var hint = Text("replay-hint", "Hold right mouse to orbit and zoom · Space play · ←/→ step · ↑/↓ speed · Tab focus · M names · H hide", 0);
            hint.pickingMode = PickingMode.Ignore;
            hint.style.position = Position.Absolute;
            hint.style.left = 244;
            hint.style.top = 60;
            hint.style.fontSize = 13;
            hint.style.color = new Color(1f, 1f, 1f, 0.7f);
            _root.Add(hint);
        }

        public bool NamesShown { get; set; }

        public void Show(uint tick, uint first, bool playing, string speed, string focus)
        {
            _timeline.SetValueWithoutNotify((int)tick);
            _time.text = SlopArena.Client.Replays.ReplayText.Clock(tick - first);
            _play.text = playing ? "❚❚" : "▶";
            _speed.text = speed;
            _focus.text = focus;
        }

        public void Banner(string? text)
        {
            _banner.text = text ?? "";
            _banner.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void ToggleHidden()
        {
            _hidden = !_hidden;
            _bar.style.display = _hidden ? DisplayStyle.None : DisplayStyle.Flex;
            _labelLayer.style.display = _hidden ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Places one fighter's label above its head, or hides it when <paramref name="text"/> is empty.</summary>
        public void Label(ulong entityId, string? text, Vector3 world, UnityEngine.Camera? camera)
        {
            if (!_labels.TryGetValue(entityId, out var label))
            {
                label = Text("replay-label-" + entityId, "", 0);
                label.pickingMode = PickingMode.Ignore;
                label.style.position = Position.Absolute;
                label.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
                label.style.paddingLeft = label.style.paddingRight = 6;
                SetRadius(label.style, 4);
                _labelLayer.Add(label);
                _labels[entityId] = label;
            }
            if (string.IsNullOrEmpty(text) || camera == null || _labelLayer.panel == null
                || camera.WorldToViewportPoint(world).z <= 0f)
            {
                label.style.display = DisplayStyle.None;
                return;
            }
            Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(_labelLayer.panel, world, camera);
            label.text = text;
            label.style.display = DisplayStyle.Flex;
            label.style.left = point.x - 60;
            label.style.top = point.y - 24;
        }

        public void Dispose() => _root.RemoveFromHierarchy();

        private static Button Make(string name, string text, Action? onClick)
        {
            var button = new Button(() => onClick?.Invoke()) { name = name, text = text };
            button.style.minWidth = 34;
            button.style.marginLeft = button.style.marginRight = 2;
            return button;
        }

        private static Label Text(string name, string text, float width)
        {
            var label = new Label(text) { name = name };
            label.style.color = Color.white;
            label.style.fontSize = 15;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (width > 0) label.style.minWidth = width;
            return label;
        }

        private static void Fill(IStyle style)
        {
            style.position = Position.Absolute;
            style.left = style.right = style.top = style.bottom = 0;
        }

        private static void SetRadius(IStyle style, float radius)
            => style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = radius;
    }
}
