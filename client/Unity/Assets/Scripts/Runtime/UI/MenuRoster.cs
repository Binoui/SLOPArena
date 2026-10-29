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
        private static readonly Dictionary<CharacterClass, string> AlternateNames = new();
        public static readonly CharacterClass[] Classes = LoadClasses();

        /// <summary>
        /// The fighter's player-facing name: the cooked package's displayName ("Sir Bonk"),
        /// falling back to the selector. Never use CharacterClass.ToString() in UI text.
        /// </summary>
        public static string Name(CharacterClass characterClass) =>
            Names.TryGetValue(characterClass, out var name) ? name : characterClass.ToString();

        /// <summary>True for a fighter with two forms (ADR-0051), whose card picks the starting form.</summary>
        public static bool HasForms(CharacterClass characterClass) => AlternateNames.ContainsKey(characterClass);

        /// <summary>The name of one of the fighter's forms ("Nyxxie" for Nyx's form 1).</summary>
        public static string Name(CharacterClass characterClass, byte form) =>
            form == 1 && AlternateNames.TryGetValue(characterClass, out var name) ? name : Name(characterClass);

        /// <summary>The portrait resource of one of the fighter's forms; form 1 adds "_Fake".</summary>
        public static string PortraitPath(CharacterClass characterClass, byte form) =>
            form == 1 && HasForms(characterClass) ? $"UI/Portraits/{characterClass}_Fake" : $"UI/Portraits/{characterClass}";

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
                    {
                        string runtime = Path.Combine(root, entry.PackageId, "character.runtime.json");
                        if (ReadDisplayName(runtime) is { } name)
                            Names[entry.Selector] = name;
                        if (ReadDisplayName(runtime, "alternateForm") is { } alternate)
                            AlternateNames[entry.Selector] = alternate;
                    }
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

        private static string? ReadDisplayName(string path, string? block = null)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                if (!document.RootElement.TryGetProperty("character", out var character)) return null;
                if (block != null && !character.TryGetProperty(block, out character)) return null;
                return character.TryGetProperty("displayName", out var name)
                    && name.GetString() is { Length: > 0 } text ? text : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MenuRoster] No display name in {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>True for a fighter still being built; menus tag it WIP so players know its kit and art are unfinished.</summary>
        public static bool IsWorkInProgress(CharacterClass characterClass) => characterClass is CharacterClass.Bashie or CharacterClass.Salamander or CharacterClass.Nyx;

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
                CharacterClass.Nyx => "A witch with another side.",
                _ => "Admitted fighter with a style all its own.",
            };
        }
    }
}
