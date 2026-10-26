#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using SlopArena.Client.World;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Player cursors: a bobbing P1 tag and frame follow UI focus in every document, so controller and
    /// keyboard players can see where they are. During couch play Fighter Select adds P2's tag on P2's
    /// fighter. Presentation only; focus and navigation are unchanged. Hidden while the mouse drives the UI.
    /// </summary>
    public sealed class PlayerCursors : MonoBehaviour
    {
        /// <summary>The element P2 is pointing at, set by Fighter Select during couch play.</summary>
        public static VisualElement? P2Target;

        private static readonly Color P1Colour = new(1f, 204f / 255f, 34f / 255f);
        private static readonly Color P2Colour = new(234f / 255f, 88f / 255f, 12f / 255f);
        private static readonly Color Ink = new(27f / 255f, 26f / 255f, 24f / 255f);

        // Built in Awake: UI Toolkit forbids creating elements in a MonoBehaviour field initializer.
        private Tag? _p1, _p2;
        private readonly List<UIDocument> _documents = new();
        private float _nextScan;
        private bool _mouseMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            P2Target = null;
            if (FindFirstObjectByType<PlayerCursors>() != null) return;
            var host = new GameObject(nameof(PlayerCursors));
            DontDestroyOnLoad(host);
            host.AddComponent<PlayerCursors>();
        }

        private void Awake()
        {
            _p1 = new Tag("P1", P1Colour);
            _p2 = new Tag("P2", P2Colour);
        }

        private void Update()
        {
            if (_p1 == null || _p2 == null) return;
            TrackInputMode();
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                _documents.Clear();
                _documents.AddRange(FindObjectsByType<UIDocument>(FindObjectsSortMode.None));
            }

            float bob = -10f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f));
            var focused = _mouseMode || !MenuOpen() ? null : FocusedElement();
            if (focused != null) _p1.Show(focused, 0f, bob);
            else _p1.Hide();

            var p2 = P2Target != null && GamepadUIBridge.IsAvailable(P2Target) ? P2Target : null;
            if (p2 != null) _p2.Show(p2, p2 == focused ? 140f : 0f, bob);
            else _p2.Hide();
        }

        private void OnDestroy()
        {
            _p1?.Hide();
            _p2?.Hide();
        }

        // In a match only the pause and settings menus take a cursor; the HUD never does.
        private static bool MenuOpen() => MatchBase.Active == null
            || MatchPauseMenu.Active is { IsPaused: true } || SettingsOverlay.Active is { IsOpen: true };

        private VisualElement? FocusedElement()
        {
            VisualElement? best = null;
            int bestOrder = int.MinValue;
            foreach (var document in _documents)
            {
                if (document == null || !document.isActiveAndEnabled) continue;
                var focused = document.rootVisualElement?.panel?.focusController?.focusedElement as VisualElement;
                if (focused == null || !GamepadUIBridge.IsAvailable(focused) || focused.worldBound.width <= 0f) continue;
                int order = (int)document.sortingOrder;
                if (best == null || order > bestOrder)
                {
                    best = focused;
                    bestOrder = order;
                }
            }
            return best;
        }

        private void TrackInputMode()
        {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame))
                _mouseMode = true;
            if (Keyboard.current?.anyKey.wasPressedThisFrame == true)
                _mouseMode = false;
            foreach (var pad in Gamepad.all)
                if (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
                    || pad.dpad.ReadValue().sqrMagnitude > 0.25f || pad.leftStick.ReadValue().sqrMagnitude > 0.25f)
                    _mouseMode = false;
        }

        private sealed class Tag
        {
            private readonly VisualElement _frame = new() { pickingMode = PickingMode.Ignore };
            private readonly VisualElement _chip = new() { pickingMode = PickingMode.Ignore };
            private readonly Label _label;

            public Tag(string text, Color colour)
            {
                _frame.style.position = Position.Absolute;
                SetBorder(_frame, 7f, colour);
                _chip.style.position = Position.Absolute;
                _chip.style.backgroundColor = colour;
                SetBorder(_chip, 6f, Ink);
                _chip.style.paddingLeft = _chip.style.paddingRight = 20f;
                _chip.style.paddingTop = 4f;
                _chip.style.paddingBottom = 6f;
                _label = new Label(text) { pickingMode = PickingMode.Ignore };
                _label.style.color = Ink;
                _label.style.fontSize = 46f;
                _label.style.unityFontStyleAndWeight = FontStyle.Bold;
                _label.style.marginLeft = _label.style.marginRight = _label.style.marginTop = _label.style.marginBottom = 0f;
                _label.style.paddingLeft = _label.style.paddingRight = _label.style.paddingTop = _label.style.paddingBottom = 0f;
                var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
                arrow.style.position = Position.Absolute;
                arrow.style.width = arrow.style.height = 26f;
                arrow.style.left = 22f;
                arrow.style.bottom = -17f;
                arrow.style.backgroundColor = Ink;
                arrow.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
                _chip.Add(arrow);
                _chip.Add(_label);
                Hide();
            }

            public void Show(VisualElement target, float dx, float bob)
            {
                var host = target.panel?.visualTree;
                if (host == null) { Hide(); return; }
                if (_chip.parent != host)
                {
                    host.Add(_frame);
                    host.Add(_chip);
                }
                else
                {
                    _frame.BringToFront();
                    _chip.BringToFront();
                }
                _label.style.unityFontDefinition = target.resolvedStyle.unityFontDefinition;
                Rect bounds = target.worldBound;
                Vector2 min = host.WorldToLocal(bounds.min);
                // A second tag on the same element frames outside the first, so both colours show.
                float grow = dx > 0f ? 16f : 8f;
                _frame.style.left = min.x - grow;
                _frame.style.top = min.y - grow;
                _frame.style.width = bounds.width + grow * 2f;
                _frame.style.height = bounds.height + grow * 2f;
                // Above the element, or just inside its top edge when it touches the top of the screen.
                float chipTop = min.y - 86f < 0f ? min.y + 10f : min.y - 86f;
                _chip.style.left = min.x + 6f + dx;
                _chip.style.top = chipTop;
                _chip.style.translate = new Translate(0f, bob);
                _frame.style.display = _chip.style.display = DisplayStyle.Flex;
            }

            public void Hide()
            {
                _frame.style.display = _chip.style.display = DisplayStyle.None;
            }

            private static void SetBorder(VisualElement element, float width, Color colour)
            {
                element.style.borderLeftWidth = element.style.borderRightWidth =
                    element.style.borderTopWidth = element.style.borderBottomWidth = width;
                element.style.borderLeftColor = element.style.borderRightColor =
                    element.style.borderTopColor = element.style.borderBottomColor = colour;
            }
        }
    }
}
