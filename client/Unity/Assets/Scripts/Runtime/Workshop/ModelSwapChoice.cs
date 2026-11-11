#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using SlopArena.Client.UI;
using UnityEngine;

namespace SlopArena.Client.Workshop
{
    /// <summary>
    /// The model one player wears over their built-in fighter (a model swap, ADR-0065): a rigged humanoid GLB on
    /// disk. Its bone map file, when there is one, sits beside it.
    /// </summary>
    public sealed class ModelSwapChoice
    {
        public ModelSwapChoice(string glbPath) => GlbPath = glbPath;

        public string GlbPath { get; }

        public string Name => Path.GetFileNameWithoutExtension(GlbPath);

        /// <summary><c>&lt;name&gt;.humanoid.json</c> for a loose GLB, else <c>humanoid.json</c> in a Workshop item folder.</summary>
        public string? BoneMapPath
        {
            get
            {
                string folder = Path.GetDirectoryName(GlbPath) ?? "";
                string named = Path.Combine(folder, Name + ".humanoid.json");
                if (File.Exists(named)) return named;
                string item = Path.Combine(folder, "humanoid.json");
                return File.Exists(item) ? item : null;
            }
        }
    }

    /// <summary>
    /// Fills <see cref="MatchConfig.ModelSwaps"/> at launch until Fighter Select and the Workshop screen do:
    /// <c>-modelSwap 2=C:\models\dusty.glb</c> (repeatable, or several pairs joined by <c>;</c>), else
    /// <c>p1.glb</c> to <c>p4.glb</c> in the <c>ModelSwaps</c> folder of the player's data folder.
    /// </summary>
    public static class ModelSwapLaunchOptions
    {
        public const string Argument = "-modelSwap";

        public static string FolderPath => Path.Combine(Application.persistentDataPath, "ModelSwaps");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            Fill(MatchConfig.ModelSwaps, Environment.GetCommandLineArgs(), FolderPath);
            foreach (var pair in MatchConfig.ModelSwaps)
                Debug.Log($"[ModelSwap] P{pair.Key} will wear {pair.Value.GlbPath}");
        }

        public static void Fill(IDictionary<int, ModelSwapChoice> swaps, IReadOnlyList<string> args, string folder)
        {
            for (int i = 0; i + 1 < args.Count; i++)
            {
                if (args[i] != Argument) continue;
                foreach (string pair in args[i + 1].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int equals = pair.IndexOf('=');
                    if (equals > 0 && int.TryParse(pair.Substring(0, equals), out int player) && player is >= 1 and <= 4)
                        swaps[player] = new ModelSwapChoice(pair.Substring(equals + 1).Trim());
                    else
                        Debug.LogWarning($"[ModelSwap] Ignored {Argument} {pair}: expected <player 1-4>=<path to .glb>.");
                }
            }
            if (swaps.Count > 0 || !Directory.Exists(folder)) return;
            for (int player = 1; player <= 4; player++)
            {
                string path = Path.Combine(folder, $"p{player}.glb");
                if (File.Exists(path)) swaps[player] = new ModelSwapChoice(path);
            }
        }
    }
}
