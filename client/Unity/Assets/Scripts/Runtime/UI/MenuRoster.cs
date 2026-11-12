#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.UI
{
    public static class MenuRoster
    {
        static readonly Dictionary<CharacterClass, string> Packages = new();
        static readonly Dictionary<CharacterClass, FighterUiArt> Artworks = new();
        static CharacterClass[] classes = Array.Empty<CharacterClass>();
        static bool loaded;
        static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                foreach (var entry in ShippedRoster.ReadApplication().Manifest.Entries)
                {
                    Packages[entry.Selector] = entry.PackageId;
                    string name = entry.Selector.ToString();
                    try
                    {
                        if (!FighterPresentation.Binding(entry.PackageId).HasValue)
                        {
                            var path = System.IO.Path.Combine(ShippedRoster.ReadApplication().CookedRoot, entry.PackageId);
                            name = CookedCharacterPackageLoader.LoadDirectory(path, entry.Requirement).ToCharacterDefinition().DisplayName;
                        }
                    }
                    catch (Exception error) { Debug.LogWarning($"[MenuRoster] Could not read {entry.PackageId}'s cooked name: {error.Message}"); }
                    Artworks[entry.Selector] = FighterPresentation.Menu(entry.PackageId, name);
                }
                classes = Packages.Keys.ToArray();
            }
            catch (Exception error) { Debug.LogWarning($"[MenuRoster] Roster presentation is unavailable: {error.Message}"); }
        }
        public static CharacterClass[] Classes { get { Load(); return classes; } }
        public static bool Contains(CharacterClass fighter) { Load(); return Packages.ContainsKey(fighter); }
        public static string? PackageId(CharacterClass fighter) { Load(); return Packages.TryGetValue(fighter, out var id) ? id : null; }
        public static FighterUiArt Art(CharacterClass fighter)
        {
            Load();
            return Artworks.TryGetValue(fighter, out var art) ? art : FighterPresentation.Unavailable(fighter.ToString());
        }
        public static string Name(CharacterClass fighter) => Art(fighter).Name();
        public static string Name(CharacterClass fighter, byte form) => Art(fighter).Name(form);
        public static bool HasForms(CharacterClass fighter) => Art(fighter).HasForms;
        public static bool IsWorkInProgress(CharacterClass fighter) => Art(fighter).Card.IsWorkInProgress;
        public static string Description(CharacterClass fighter) => Art(fighter).Card.Description;
    }
}
