#nullable enable
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.Client.UI
{
    public static class MenuRoster
    {
        static readonly Dictionary<CharacterClass, string> Packages = ShippedRoster.ReadApplication().Manifest.Entries.ToDictionary(e => e.Selector, e => e.PackageId);
        static readonly Dictionary<CharacterClass, FighterUiArt> Artworks = Packages.ToDictionary(e => e.Key, e => FighterPresentation.Menu(e.Value));
        public static readonly CharacterClass[] Classes = ShippedRoster.ReadApplication().Manifest.Entries.Select(e => e.Selector).ToArray();
        public static string? PackageId(CharacterClass fighter) => Packages.TryGetValue(fighter, out var id) ? id : null;
        public static FighterUiArt Art(CharacterClass fighter) => Artworks[fighter];
        public static string Name(CharacterClass fighter) => Artworks.TryGetValue(fighter, out var art) ? art.Name() : fighter.ToString();
        public static string Name(CharacterClass fighter, byte form) => Art(fighter).Name(form);
        public static bool HasForms(CharacterClass fighter) => Art(fighter).HasForms;
        public static bool IsWorkInProgress(CharacterClass fighter) => Art(fighter).Card.IsWorkInProgress;
        public static string Description(CharacterClass fighter) => Art(fighter).Card.Description;
    }
}
