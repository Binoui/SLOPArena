#nullable enable
using System.Collections.Generic;
using System.IO;
using SlopArena.Client.UI;
using UnityEngine;

namespace SlopArena.Client.Workshop
{
    public static class ModelSwapLibrary
    {
        public static WorkshopLibrary Library => new(Path.Combine(Application.persistentDataPath, "Workshop"));
        public static void Wear(int player, WorkshopLibraryItem? item)
        {
            Library.Wear(player, item);
            if (item == null) MatchConfig.ModelSwaps.Remove(player);
            else MatchConfig.ModelSwaps[player] = new ModelSwapChoice(item.GlbPath, item.Manifest.BaseFighterPackageId);
        }
        public static void Fill(IDictionary<int, ModelSwapChoice> choices)
        {
            for (int player = 1; player <= 4; player++)
                if (Library.Worn(player) is { } item)
                    choices[player] = new ModelSwapChoice(item.GlbPath, item.Manifest.BaseFighterPackageId);
        }
    }
}
