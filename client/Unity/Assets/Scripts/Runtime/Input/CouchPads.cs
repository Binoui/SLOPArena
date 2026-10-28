#nullable enable
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using SlopArena.Client.UI;

namespace SlopArena.Client.Input
{
    /// <summary>Device ownership for Solo's menus and joined Local Players.</summary>
    public static class CouchPads
    {
        public static Gamepad? ForSeat(int playerNumber) => (MatchConfig.Mode == GameMode.Solo || MatchConfig.Mode == GameMode.Arcade && MatchConfig.LocalMode == LocalPlayMode.CharacterCourse) &&
            MatchConfig.Seat(playerNumber).Kind == SeatKind.LocalPlayer
                ? InputSystem.GetDeviceById(MatchConfig.Seat(playerNumber).DeviceId) as Gamepad : null;

        public static bool Listening;
        public static Gamepad? P1MenuPad;

        public static bool IsJoined(Gamepad pad)
        {
            for (int player = 2; player <= 4; player++)
                if (ForSeat(player) == pad) return true;
            return false;
        }

        private static bool OwnsMenus(Gamepad pad) => Listening || MatchConfig.Mode == GameMode.Solo || MatchConfig.Mode == GameMode.Arcade && MatchConfig.LocalMode == LocalPlayMode.CharacterCourse
            ? pad == P1MenuPad : !IsJoined(pad);

        public static Gamepad? MenuPad => Gamepad.current != null && OwnsMenus(Gamepad.current) ? Gamepad.current : null;

        /// <summary>
        /// Trickle-down guard for UI Toolkit's own gamepad navigation, which reads every pad:
        /// drop a submit, cancel or move that a pad without the menus pressed this frame.
        /// </summary>
        public static void BlockForeignPress<T>(T evt) where T : EventBase
        {
            foreach (var pad in Gamepad.all)
            {
                if (OwnsMenus(pad)) continue;
                if (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
                    || pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame
                    || pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame)
                {
                    evt.StopImmediatePropagation();
                    (evt.target as VisualElement)?.focusController?.IgnoreEvent(evt);
                    return;
                }
            }
        }
    }
}
