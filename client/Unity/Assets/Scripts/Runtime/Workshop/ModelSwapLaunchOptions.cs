#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using SlopArena.Client.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.Client.Workshop
{
    public static class ModelSwapLaunchOptions
    {
        public const string Argument = ModelSwapChoiceSources.Argument;
        public static string FolderPath => Path.Combine(Application.persistentDataPath, "ModelSwaps");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ModelSwapLibrary.Restore();
            Refresh();
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Arena_Offline" || MatchConfig.Mode != GameMode.Training) ModelSwapLibrary.EndPreview();
            Refresh();
        }
        public static void Refresh() => Fill(MatchConfig.ModelSwaps, Environment.GetCommandLineArgs(), FolderPath);
        public static void Fill(IDictionary<int, ModelSwapChoice> swaps, IReadOnlyList<string> args, string folder)
            => ModelSwapChoiceSources.Fill(swaps, args, folder);
    }
}
