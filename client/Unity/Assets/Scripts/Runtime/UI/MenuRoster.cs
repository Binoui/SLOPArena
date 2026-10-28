#nullable enable
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// The player-facing roster for pre-match screens. The cooked manifest is the
    /// single source of truth, including its authored entry order.
    /// </summary>
    public static class MenuRoster
    {
        private static readonly Dictionary<CharacterClass, string> Names = new();
        public static readonly CharacterClass[] Classes = LoadClasses();

        /// <summary>
        /// The fighter's player-facing name: the cooked package's displayName ("Sir Bonk"),
        /// falling back to the selector. Never use CharacterClass.ToString() in UI text.
        /// </summary>
        public static string Name(CharacterClass characterClass) =>
            Names.TryGetValue(characterClass, out var name) ? name : characterClass.ToString();

        private static CharacterClass[] LoadClasses()
        {
            string[] roots =
            {
                "content-cooked",
                Path.Combine(Application.dataPath, "../../../content-cooked"),
                Path.Combine(Application.streamingAssetsPath, "content-cooked"),
            };

            try
            {
                foreach (string root in roots)
                {
                    string path = Path.Combine(root, "roster", "manifest.json");
                    if (!File.Exists(path))
                        continue;

                    BuiltInRosterManifest manifest = BuiltInRosterManifestCodec.Load(path);
                    foreach (var entry in manifest.Entries)
                        if (ReadDisplayName(Path.Combine(root, entry.PackageId, "character.runtime.json")) is { } name)
                            Names[entry.Selector] = name;
                    return manifest.Entries.Select(entry => entry.Selector).ToArray();
                }

                throw new FileNotFoundException("Cooked roster manifest is missing.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MenuRoster] Admitted roster unavailable: {ex.Message}");
                return Array.Empty<CharacterClass>();
            }
        }

        private static string? ReadDisplayName(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                return document.RootElement.TryGetProperty("character", out var character)
                    && character.TryGetProperty("displayName", out var name)
                    && name.GetString() is { Length: > 0 } text ? text : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MenuRoster] No display name in {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Short, player-facing guidance that distinguishes each admitted fighter's
        /// game plan without pretending to expose uncooked move data.
        /// </summary>
        public static string Description(CharacterClass characterClass)
        {
            return characterClass switch
            {
                CharacterClass.FightGuy => "Loves punching stuff. But also kicking stuff.",
                CharacterClass.Manki => "There's nothing a few explosives can't fix.",
                CharacterClass.Wibou => "He studied the blade. A lot.",
                CharacterClass.Bonk => "Sir Bonk do bonky bonks.",
                CharacterClass.Bashie => "NO RUNNING! (She speed-walks.)",
                CharacterClass.Salamander => "The fastest fingers in the West.",
                _ => "Admitted fighter with a style all its own.",
            };
        }
    }
}
