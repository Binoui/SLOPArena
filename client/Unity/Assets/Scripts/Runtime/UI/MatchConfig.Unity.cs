#nullable enable
using SlopArena.Shared;
namespace SlopArena.Client.UI
{
    public static partial class MatchConfig
    {
        static partial void ReadModeStage(ref ArcadeStageDefinition? stage)
        {
            var path = BakedContentPaths.ResolveArena(ArenaName);
            if (path != null && MatchArenaRecord.TryLoad(path, ArenaName, out _, out var arena, out _)) stage = arena.Arcade;
        }
    }
}
