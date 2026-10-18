using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopArena.Client.Input
{
    /// <summary>
    /// Arcade retry trigger. The keyboard binding restarts at once; a gamepad binding must be
    /// held briefly so a brushed button never throws a run away. A press that is already down
    /// when this is created (across a retry) or while retry is unavailable must be released first.
    /// Create it from match start, not a field initializer: it reads the Input System.
    /// </summary>
    public sealed class ArcadeRetryInput
    {
        public const float GamepadHoldSeconds = .5f;
        private float _held;
        private bool _awaitRelease = HumanInputActions.Get("Retry").IsPressed();

        /// <summary>0–1 while a gamepad hold is building, otherwise 0.</summary>
        public float HoldProgress => Mathf.Clamp01(_held / GamepadHoldSeconds);

        /// <summary>Call once per frame. True on the frame a retry should start.</summary>
        public bool Poll(bool allowed)
        {
            var action = HumanInputActions.Get("Retry");
            if (!action.IsPressed()) { _awaitRelease = false; _held = 0f; return false; }
            if (!allowed) _awaitRelease = true;
            if (_awaitRelease) { _held = 0f; return false; }
            if (action.activeControl?.device is not Gamepad)
            {
                if (!action.WasPressedThisFrame()) return false;
            }
            else if ((_held += Time.unscaledDeltaTime) < GamepadHoldSeconds) return false;
            _held = 0f; _awaitRelease = true;
            return true;
        }
    }
}
