using System;
using System.IO;
using System.Linq;
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
        public static readonly CharacterClass[] Classes = LoadClasses();

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
                CharacterClass.Bonk => "Bonk do bonky bonks.",
                CharacterClass.Bashie => "NO RUNNING! (She speed-walks.)",
                _ => "Admitted fighter with a style all its own.",
            };
        }
    }
}
