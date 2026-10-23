using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using SlopArena.Client.UI;

namespace SlopArena.Client.Input
{
    /// <summary>
    /// Shared input boundary for the chat composer.
    ///
    /// Chat does not pause the match. While the composer owns keyboard focus, gameplay
    /// consumers must ignore human input, camera axes, and background shortcuts. End also
    /// keeps the boundary active until every control that was held during composition is
    /// released, so a typed key cannot become an action on the first gameplay frame after
    /// the composer closes.
    /// </summary>
    public static class ChatInputGate
    {
        private static bool _isComposing;
        private static bool _externalOverlayActive;
        private static bool _externalReleaseRequired;
        private static int _externalSuppressThroughFrame = -1;
        private static bool _releaseRequired;
        private static int _suppressThroughFrame = -1;

        public static bool IsComposing => _isComposing;

        /// <summary>True while human gameplay and camera input must be ignored.</summary>
        public static bool SuppressGameplay => IsInputSuppressed;

        /// <summary>True while menu/pause and other background shortcuts must be ignored.</summary>
        public static bool SuppressShortcuts => IsInputSuppressed;
        public static bool ExternalOverlaySuppressed => _externalOverlayActive ||
            Time.frameCount <= _externalSuppressThroughFrame || _externalReleaseRequired;

        public static void Tick()
        {
            bool releaseChat = _releaseRequired && !_isComposing && Time.frameCount > _suppressThroughFrame;
            bool releaseOverlay = _externalReleaseRequired && !_externalOverlayActive &&
                Time.frameCount > _externalSuppressThroughFrame;
            if ((!releaseChat && !releaseOverlay) || AnyHumanInputHeld()) return;
            if (releaseChat) _releaseRequired = false;
            if (releaseOverlay) _externalReleaseRequired = false;
        }

        public static void BeginExternalOverlay()
        {
            _externalOverlayActive = true;
            _externalReleaseRequired = true;
            _externalSuppressThroughFrame = Mathf.Max(_externalSuppressThroughFrame, Time.frameCount);
        }

        public static void EndExternalOverlay()
        {
            _externalOverlayActive = false;
            _externalReleaseRequired = true;
            _externalSuppressThroughFrame = Mathf.Max(_externalSuppressThroughFrame, Time.frameCount);
        }

        /// <summary>
        /// True while a match owns the scene, including Tutorial and Arcade.
        /// </summary>
        public static bool IsGameplayScene => SlopArena.Client.World.MatchBase.Active != null;

        // Arcade reserves Select for its hold-to-retry shortcut.
        internal static bool GamepadChatAvailable => MatchConfig.Mode != GameMode.Arcade;

        private static bool IsInputSuppressed
        {
            get
            {
                if (ExternalOverlaySuppressed || _isComposing || Time.frameCount <= _suppressThroughFrame)
                    return true;

                // ChatOverlay opens on Enter or Select during gameplay and focuses the field through
                // UI Toolkit scheduling. Consume that opening frame even before FocusIn
                // calls Begin(), so the opening press cannot also reach gameplay.
                if (IsChatOpenPressedThisFrame())
                    return true;

                return _releaseRequired;
            }
        }
        private static bool IsChatOpenPressedThisFrame()
        {
            if (!IsGameplayScene || HumanInputActions.IsCapturing ||
                SettingsOverlay.Active is { IsOpen: true } || MatchPauseMenu.Active is { IsPaused: true })
                return false;

            var keyboard = Keyboard.current;
            return (keyboard != null &&
                    (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) ||
                   (GamepadChatAvailable && Gamepad.current?.selectButton.wasPressedThisFrame == true);
        }

        /// <summary>Begin composing. Idempotent so focus callbacks can safely repeat it.</summary>
        public static void Begin()
        {
            _isComposing = true;
            _releaseRequired = true;
            _suppressThroughFrame = Mathf.Max(_suppressThroughFrame, Time.frameCount);
        }

        /// <summary>
        /// End composing without pausing simulation. The current frame is consumed and all
        /// controls held during typing must be released before gameplay input is accepted.
        /// </summary>
        public static void End()
        {
            _isComposing = false;
            _releaseRequired = true;
            _suppressThroughFrame = Mathf.Max(_suppressThroughFrame, Time.frameCount);
        }

        private static bool AnyHumanInputHeld()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                foreach (var key in keyboard.allKeys)
                {
                    if (key.isPressed)
                        return true;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed ||
                                  mouse.middleButton.isPressed))
                return true;

            foreach (var gamepad in Gamepad.all)
            {
                foreach (var control in gamepad.allControls)
                {
                    if (control is ButtonControl button && button.isPressed)
                        return true;
                }

                if (gamepad.leftStick.ReadValue().sqrMagnitude > 0.0001f ||
                    gamepad.rightStick.ReadValue().sqrMagnitude > 0.0001f ||
                    Mathf.Abs(gamepad.leftTrigger.ReadValue()) > 0.0001f ||
                    Mathf.Abs(gamepad.rightTrigger.ReadValue()) > 0.0001f)
                    return true;
            }

            return false;
        }
    }
}
