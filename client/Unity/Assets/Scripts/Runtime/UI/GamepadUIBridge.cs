using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Controller submit/cancel bridge for UI Toolkit panels (issue #215).
    /// Unity's runtime panel translates gamepad dpad movement natively but
    /// does not generate NavigationSubmit/NavigationCancel from gamepad face
    /// buttons, so this bridge pumps them onto the focused element exactly
    /// like the panel does for keyboard Enter/Escape. One button press
    /// produces one navigation event.
    /// </summary>
    internal static class GamepadUIBridge
    {
        // One face-button press must produce one navigation event regardless
        // of whether the panel also synthesizes its own from the same input
        // (issue #215); debounced because both would arrive within a frame or
        // two of each other.
        private const float DebounceSeconds = 0.2f;
        private static float _lastSubmitTime = float.NegativeInfinity;
        private static float _lastCancelTime = float.NegativeInfinity;

        /// <summary>Poll the current gamepad and dispatch submit/cancel
        /// navigation events for the panel's focused element.</summary>
        public static void Pump(IPanel? panel, bool cancel = true)
        {
            var pad = SlopArena.Client.Input.CouchPads.MenuPad;
            if (pad == null || panel?.focusController == null)
                return;
            float now = Time.unscaledTime;
            if (pad.buttonSouth.wasPressedThisFrame && now - _lastSubmitTime > DebounceSeconds)
            {
                _lastSubmitTime = now;
                Dispatch<NavigationSubmitEvent>(panel);
            }
            if (cancel && pad.buttonEast.wasPressedThisFrame && now - _lastCancelTime > DebounceSeconds)
            {
                _lastCancelTime = now;
                Dispatch<NavigationCancelEvent>(panel);
            }
        }

        internal static bool IsAvailable(VisualElement? element)
        {
            if (element?.panel == null || !element.enabledInHierarchy)
                return false;
            for (var current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None
                    || current.resolvedStyle.visibility == Visibility.Hidden)
                    return false;
            }
            return true;
        }

        internal static void Submit(Button? button)
        {
            if (!IsAvailable(button))
                return;
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = button;
            button!.SendEvent(evt);
        }

        private static void Dispatch<T>(IPanel panel) where T : EventBase<T>, new()
        {
            if (panel.focusController.focusedElement is not VisualElement focused || !IsAvailable(focused))
                return;
            using var evt = EventBase<T>.GetPooled();
            evt.target = focused;
            focused.SendEvent(evt);
        }
    }
}
