#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SlopArena.Client.UI;
using UnityEngine;

namespace SlopArena.Client.Workshop
{
    public static class ModelSwapLibrary
    {
        private static readonly ModelSwapSession Session = new();
        public static WorkshopLibrary Library => new(Path.Combine(Application.persistentDataPath, "Workshop"));
        public static Task Ready { get; private set; } = Task.CompletedTask;
        public static void Restore()
        {
            EndPreview();
            MatchConfig.ModelSwaps.Clear();
            Ready = RestoreAsync();
        }
        private static async Task RestoreAsync()
        {
            var library = Library;
            try
            {
                var saved = await Task.Run(() =>
                {
                    library.EnsureDirectories();
                    var choices = new Dictionary<int, ModelSwapChoice>();
                    for (int seat = 1; seat <= 4; seat++) if (library.Worn(seat) is { } item)
                        choices[seat] = new ModelSwapChoice(item.GlbPath, item.Manifest.BaseFighterPackageId);
                    return choices;
                });
                foreach (var pair in saved) if (!MatchConfig.ModelSwaps.TryGetValue(pair.Key, out var current) || current.Source != ModelSwapChoiceSource.Player)
                    MatchConfig.ModelSwaps[pair.Key] = pair.Value;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            { Debug.LogWarning("[Workshop] Saved choices unavailable: " + e.Message); }
        }
        public static async Task Wear(int player, WorkshopLibraryItem? item)
        {
            await Ready;
            var library = Library;
            await Task.Run(() => library.Wear(player, item));
            EndPreview();
            if (item == null) MatchConfig.ModelSwaps.Remove(player);
            else MatchConfig.ModelSwaps[player] = new ModelSwapChoice(item.GlbPath, item.Manifest.BaseFighterPackageId);
        }
        public static void Preview(WorkshopLibraryItem item) => Session.Preview(MatchConfig.ModelSwaps, new ModelSwapChoice(item.GlbPath, item.Manifest.BaseFighterPackageId));
        public static void EndPreview() => Session.EndPreview(MatchConfig.ModelSwaps);
        public static ModelSwapChoice? ChoiceFor(int entity) => ModelSwapSession.ForFighter(MatchConfig.ModelSwaps, entity,
            MatchConfig.Mode == GameMode.PvP, MatchConfig.LocalEntityId);
    }
}
