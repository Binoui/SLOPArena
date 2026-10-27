using UnityEngine;

namespace SlopArena.Client.UI
{
    public static class SeatColours
    {
        private static readonly Color[] Colours =
        {
            new Color32(255, 204, 34, 255),
            new Color32(234, 88, 12, 255),
            new Color32(20, 184, 166, 255),
            new Color32(167, 139, 250, 255),
        };

        public static Color For(int playerNumber) => Colours[(playerNumber - 1) % Colours.Length];
    }
}
