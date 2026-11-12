#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.Client.UI
{
    public sealed partial class LocalFighterLibrary
    {
        readonly MatchContentCatalog? catalog;
        readonly MatchContentPolicy policy;
        public IReadOnlyList<string> PackageIds { get; }
        public string? Diagnostic { get; }
        static LocalFighterLibrary? current;
        public static LocalFighterLibrary Current => current ??= ReadShipped();
        LocalFighterLibrary(MatchContentCatalog? content, IReadOnlyList<string> packages, string? diagnostic, MatchContentPolicy policy = MatchContentPolicy.LocalPlay)
        { catalog = content; PackageIds = packages; Diagnostic = diagnostic; this.policy = policy; }
        static LocalFighterLibrary ReadShipped()
        {
            try
            {
                MatchContentCatalog? content = null;
                var policy = MatchContentPolicy.LocalPlay;
                ReadDevelopmentContent(ref content, ref policy);
                content ??= BuiltInContentResolver.ReadApplicationCatalog();
                var ids = ShippedRoster.ReadApplication().Manifest.Entries.Select(p => p.PackageId).ToList().AsReadOnly();
                return new LocalFighterLibrary(content, ids, null, policy);
            }
            catch (Exception error)
            {
                ReportDiagnostic("[LocalFighterLibrary] " + error.Message);
                return new LocalFighterLibrary(null, Array.Empty<string>(), error.Message);
            }
        }
        static partial void ReadDevelopmentContent(ref MatchContentCatalog? content, ref MatchContentPolicy policy);
        static partial void Warn(string message);
        internal static void ReportDiagnostic(string message)
        { System.Diagnostics.Trace.TraceWarning(message); Warn(message); }
        public bool TryPrepare(IReadOnlyList<FighterChoice> choices, out MatchContentCatalog? prepared, out string? failure)
        {
            prepared = null;
            failure = Diagnostic;
            if (catalog == null) return false;
            if (choices == null || choices.Count == 0)
            { failure = "content.choice.missing: A local match requires a fighter choice."; return false; }
            var selected = new Dictionary<string, MatchContentEntry>(StringComparer.Ordinal);
            foreach (var choice in choices)
            {
                var entry = choice?.Resolve(catalog);
                if (entry == null)
                { failure = "content.choice.unavailable: " + (choice?.Revision.PackageId ?? "missing seat") + " exact revision is unavailable."; return false; }
                selected[entry.Identity.PackageId] = entry;
            }
            if (catalog.Entries.All(entry => entry.IsBundled))
            { prepared = catalog; failure = null; return true; }
            var admitted = catalog.Entries.Where(entry => entry.IsBundled).Concat(selected.Values.Where(entry => !entry.IsBundled))
                .Select(entry => entry.Choose().Fighter).ToArray();
            var result = new MatchContentCatalogBuilder().Build(admitted, policy);
            if (!result.IsValid)
            { failure = string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)); return false; }
            prepared = result.Catalog;
            failure = null;
            return true;
        }
        public bool Contains(string package) => catalog?.ResolvePackage(package) != null;
        public bool HasForms(string package) => catalog?.ResolvePackage(package)?.Definition.AlternateForm != null;
        public FighterChoice Choose(string package, byte form = 0)
            => (catalog?.ResolvePackage(package) ?? throw new ArgumentException("Fighter unavailable: " + package)).Choose(form);
    }
}
