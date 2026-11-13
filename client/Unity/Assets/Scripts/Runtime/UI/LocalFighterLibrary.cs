#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.UI
{
    public sealed class FighterCard
    {
        public FighterChoice Choice { get; }
        public FighterUiArt Art { get; }
        internal FighterCard(MatchContentEntry entry)
        {
            Choice = entry.Choose();
            try { Art = new FighterAppearance(entry).Art; }
            catch (Exception error)
            {
                Debug.LogWarning("[LocalFighterLibrary] Card unavailable for " + entry.Identity.PackageId + ": " + error.Message);
                Art = FighterPresentation.Unavailable(entry.DisplayName);
            }
        }
    }

    public sealed partial class LocalFighterLibrary
    {
        static partial void ReadDevelopmentContent(ref MatchContentCatalog? content, ref MatchContentPolicy policy, ref string? failure)
        {
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                if (ClientSession.TryReadEditorDevelopmentContent(out var development, out failure))
                { content = development; policy = MatchContentPolicy.EditorDevelopment; }
                else failure ??= "Development content provider is unavailable.";
            }
#endif
        }
        static partial void Warn(string message) => Debug.LogWarning(message);
        IReadOnlyList<FighterCard>? fighters;
        public IReadOnlyList<FighterCard> Fighters => fighters ??= PackageIds.Select(id => new FighterCard(catalog!.ResolvePackage(id)!)).ToList().AsReadOnly();
        public FighterUiArt Art(string package) => Fighters.FirstOrDefault(f => f.Choice.Revision.PackageId == package)?.Art
            ?? FighterPresentation.Unavailable(package ?? "");
        public string Name(string package) => Art(package).Name();
        public string Name(string package, byte form) => Art(package).Name(form);
        public bool IsWorkInProgress(string package) => Art(package).Card.IsWorkInProgress;
        public string Description(string package) => Art(package).Card.Description;
    }
}
