using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.UI
{
    public static class CourseColours
    {
        public static readonly string[] Names = { "TEAL", "CLAY", "GOLD", "LILAC" };
        private static readonly Color[] Palette = { new(.22f, .9f, .85f), new(1f, .55f, .35f),
            new(1f, .83f, .27f), new(.78f, .62f, 1f) };
        public static Color For(byte colour) => Palette[colour % Palette.Length];
        public static Color? ForEntity(ulong id) => MatchConfig.Format.IsCourse() && MatchConfig.Colours.TryGetValue(id, out var colour)
            ? For(colour) : null;
    }
}
