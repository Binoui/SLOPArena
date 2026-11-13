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
        static readonly SourceCache source = new(ReadSources, () => ShippedRoster.TryReadApplication(out var roster, out _) ? roster : null);
        static IReadOnlyList<AdmittedFighter> made = Array.Empty<AdmittedFighter>();
        public static void SetMadeFighters(IReadOnlyList<AdmittedFighter> fighters)
        { made = fighters.ToArray(); Invalidate(); }
        public IReadOnlyList<string> PackagesFor(bool allowMade)
            => allowMade ? PackageIds : PackageIds.Where(id => catalog!.ResolvePackage(id)!.IsBundled).ToArray();
        public string OriginLabel(string package) => catalog?.ResolvePackage(package)?.IsBundled != false ? ""
            : package.StartsWith("workshop.", StringComparison.Ordinal) ? "WORKSHOP" : "YOURS";
        static readonly HashSet<string> warned = new(StringComparer.Ordinal);
        public static LocalFighterLibrary Current => source.Read();
        public static void Invalidate() => source.Invalidate();
        public static LocalFighterLibrary Refresh() => source.Refresh();
        LocalFighterLibrary(MatchContentCatalog? content, IReadOnlyList<string> packages, string? diagnostic, MatchContentPolicy policy = MatchContentPolicy.LocalPlay)
        {
            catalog = content;
            this.policy = policy;
            Diagnostic = diagnostic;
            var available = new List<string>();
            foreach (var package in packages)
                if (content?.ResolvePackage(package) != null) available.Add(package);
                else ReportDiagnostic("[LocalFighterLibrary] Roster fighter is unavailable: " + package);
            PackageIds = available.AsReadOnly();
        }
        internal sealed class SourceCache
        {
            readonly Func<LocalFighterLibrary> read;
            readonly Func<object?> stamp;
            LocalFighterLibrary? snapshot;
            object? revision;
            internal SourceCache(Func<LocalFighterLibrary> read, Func<object?> stamp)
            { this.read = read; this.stamp = stamp; }
            internal LocalFighterLibrary Read()
            {
                var next = stamp();
                if (snapshot == null || !ReferenceEquals(next, revision))
                { snapshot = read(); revision = next; }
                return snapshot;
            }
            internal void Invalidate() => snapshot = null;
            internal LocalFighterLibrary Refresh() { Invalidate(); return Read(); }
        }
        static LocalFighterLibrary ReadSources()
        {
            try
            {
                MatchContentCatalog? content = null;
                var policy = MatchContentPolicy.LocalPlay;
                string? failure = null;
                ReadDevelopmentContent(ref content, ref policy, ref failure);
                if (failure != null) throw new InvalidOperationException(failure);
                content ??= BuiltInContentResolver.ReadApplicationCatalog();
                var ids = ShippedRoster.ReadApplication().Manifest.Entries.Select(p => p.PackageId).ToArray();
                return new LocalFighterLibrary(content, ids, null, policy).WithMadeFighters(made);
            }
            catch (Exception error)
            {
                ReportDiagnostic("[LocalFighterLibrary] " + error.Message);
                return new LocalFighterLibrary(null, Array.Empty<string>(), error.Message);
            }
        }
        static partial void ReadDevelopmentContent(ref MatchContentCatalog? content, ref MatchContentPolicy policy, ref string? failure);
        static partial void Warn(string message);
        internal static void ReportDiagnostic(string message)
        { if (warned.Add(message)) { System.Diagnostics.Trace.TraceWarning(message); Warn(message); } }
        public bool TryGetCatalog(out MatchContentCatalog? content, out string? failure)
        { content = catalog; failure = Diagnostic; return content != null; }
        public bool TryChoose(string package, byte form, out FighterChoice? choice)
        {
            choice = null;
            var entry = catalog?.ResolvePackage(package);
            if (entry == null)
            { ReportDiagnostic("[LocalFighterLibrary] No admitted fighter for '" + package + "'."); return false; }
            if (form > 1 || form == 1 && entry.Definition.AlternateForm == null)
            { ReportDiagnostic("[LocalFighterLibrary] Starting form " + form + " is unavailable for '" + package + "'; using form 0."); form = 0; }
            choice = entry.Choose(form);
            return true;
        }
        public bool TryDefault(out FighterChoice? choice)
        {
            choice = null;
            return ShippedRoster.TryReadApplication(out var roster, out _) && TryChoose(roster!.DefaultFighter.PackageId, 0, out choice);
        }
        public bool TryRefreshChoice(FighterChoice? previous, out FighterChoice? choice)
            => previous == null ? TryDefault(out choice) : TryChoose(previous.Revision.PackageId, previous.StartingForm, out choice);
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
        public LocalFighterLibrary WithMadeFighters(IReadOnlyList<AdmittedFighter> fighters)
        {
            if (catalog == null) throw new InvalidOperationException(Diagnostic);
            if (fighters.Count == 0 && catalog.Entries.All(e => e.IsBundled)) return this;
            var admitted = catalog.Entries.Where(e => e.IsBundled).Select(e => e.Choose().Fighter).Concat(fighters).ToArray();
            var built = new MatchContentCatalogBuilder().Build(admitted, policy);
            if (!built.IsValid) throw new InvalidOperationException(string.Join("; ", built.Diagnostics.Select(LocalFighterWorkspace.DiagnosticText)));
            var bundled = PackageIds.Where(id => catalog.ResolvePackage(id)!.IsBundled);
            var extras = built.Catalog!.Entries.Where(e => !e.IsBundled).OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Identity.PackageId, StringComparer.Ordinal).Select(e => e.Identity.PackageId);
            return new LocalFighterLibrary(built.Catalog, bundled.Concat(extras).ToArray(), null, policy);
        }
        public LocalFighterLibrary WithLocalItem(AdmittedFighter fighter) => WithMadeFighters(new[] { fighter });
        public FighterChoice ChooseTrainingCpu()
        {
            var builtIns = new HashSet<string>(ShippedRoster.ReadApplication().Manifest.Entries.Select(e => e.PackageId), StringComparer.Ordinal);
            if (TryDefault(out var preferred) && preferred!.Resolve(catalog!)?.IsBundled == true) return preferred;
            return (catalog?.Entries.FirstOrDefault(entry => entry.IsBundled && builtIns.Contains(entry.Identity.PackageId))
                ?? throw new InvalidOperationException("Training requires an available built-in CPU fighter.")).Choose();
        }
        public bool Contains(string package) => catalog?.ResolvePackage(package) != null;
        public bool HasForms(string package) => catalog?.ResolvePackage(package)?.Definition.AlternateForm != null;
        public FighterChoice Choose(string package, byte form = 0)
            => (catalog?.ResolvePackage(package) ?? throw new ArgumentException("Fighter unavailable: " + package)).Choose(form);
    }
}
