#nullable enable
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using SlopArena.Client.UI;

namespace SlopArena.Client.Input
{
    /// <summary>Couch play (ADR-0041): the pad that Solo's second Local Player joined with.</summary>
    public static class CouchPads
    {
        public static Gamepad? P2 => MatchConfig.SoloP2Human && MatchConfig.SoloP2DeviceId != 0
            ? InputSystem.GetDeviceById(MatchConfig.SoloP2DeviceId) as Gamepad
            : null;

        /// <summary>Set by Fighter Select while it waits for P2: any pad but P1's is joining, not navigating.</summary>
        public static bool Listening;
        public static Gamepad? P1MenuPad;

        private static bool OwnsMenus(Gamepad pad) => Listening ? pad == P1MenuPad : pad != P2;

        /// <summary>Menus belong to P1, so P2's presses never click P1's buttons.</summary>
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
