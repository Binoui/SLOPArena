#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SlopArena.Client.Workshop
{
    public static class ModelSwapChoiceSources
    {
        public const string Argument = "-modelSwap";
        public static void Fill(IDictionary<int, ModelSwapChoice> swaps, IReadOnlyList<string> args, string folder)
        {
            foreach (int player in swaps.Where(pair => pair.Value.Source != ModelSwapChoiceSource.Player).Select(pair => pair.Key).ToArray())
                swaps.Remove(player);
            var generated = new Dictionary<int, ModelSwapChoice>();
            for (int i = 0; i + 1 < args.Count; i++)
            {
                if (args[i] != Argument) continue;
                foreach (string pair in args[++i].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int equals = pair.IndexOf('=');
                    if (equals > 0 && int.TryParse(pair.Substring(0, equals), out int player) && player is >= 1 and <= 4)
                        generated[player] = new ModelSwapChoice(pair.Substring(equals + 1).Trim(), source: ModelSwapChoiceSource.LaunchOption);
                }
            }
            for (int player = 1; player <= 4; player++)
            {
                if (swaps.ContainsKey(player)) continue;
                if (generated.TryGetValue(player, out var choice)) swaps[player] = choice;
                else
                {
                    string path = Path.Combine(folder, $"p{player}.glb");
                    if (File.Exists(path)) swaps[player] = new ModelSwapChoice(path, source: ModelSwapChoiceSource.Folder);
                }
            }
        }
    }
}
