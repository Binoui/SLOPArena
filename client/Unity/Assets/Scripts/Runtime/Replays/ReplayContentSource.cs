using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Shared.Replays;

namespace SlopArena.Client.Replays
{
    /// <summary>
    /// Today's content for a Replay: the local catalog Training uses and the baked arenas with the digest of their
    /// bytes. Never historical content.
    /// </summary>
    public static class ReplayContentSource
    {
        /// <summary>The fighter drawn for a recorded package this build lacks.</summary>
        public const string StandInPackageId = "fightguy";

        public static bool TryToday(out ReplayContent? content, out string? failure)
        {
            content = null;
            if (!ClientSession.TryBuildAvailableFighterCatalog(out var catalog, out failure) || catalog == null)
                return false;
            var arenas = new Dictionary<string, ReplayArena?>();
            content = new ReplayContent(catalog, name =>
            {
                if (!arenas.TryGetValue(name, out var arena)) arenas[name] = arena = LoadArena(name);
                return arena;
            }, StandInPackageId);
            return true;
        }

        private static ReplayArena? LoadArena(string name)
        {
            string? path = BakedContentPaths.ResolveArena(name);
            if (path == null || !MatchArenaRecord.TryLoad(path, name, out var identity, out var arena, out _) || identity == null)
                return null;
            return new ReplayArena(arena, identity.Digest);
        }
    }
}
