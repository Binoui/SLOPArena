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
        public IReadOnlyList<string> PackageIds { get; }
        public string? Diagnostic { get; }
        static LocalFighterLibrary? current;
        public static LocalFighterLibrary Current => current ??= ReadShipped();
        LocalFighterLibrary(MatchContentCatalog? content, IReadOnlyList<string> packages, string? diagnostic)
        { catalog = content; PackageIds = packages; Diagnostic = diagnostic; }
        static LocalFighterLibrary ReadShipped()
        {
            try
            {
                var content = BuiltInContentResolver.ReadApplicationCatalog();
                var ids = ShippedRoster.ReadApplication().Manifest.Entries.Select(p => p.PackageId).ToList().AsReadOnly();
                return new LocalFighterLibrary(content, ids, null);
            }
            catch (Exception error)
            {
                ReportDiagnostic("[LocalFighterLibrary] " + error.Message);
                return new LocalFighterLibrary(null, Array.Empty<string>(), error.Message);
            }
        }
        static partial void Warn(string message);
        internal static void ReportDiagnostic(string message)
        { System.Diagnostics.Trace.TraceWarning(message); Warn(message); }
        public bool Contains(string package) => catalog?.ResolvePackage(package) != null;
        public bool HasForms(string package) => catalog?.ResolvePackage(package)?.Definition.AlternateForm != null;
        public FighterChoice Choose(string package, byte form = 0)
            => (catalog?.ResolvePackage(package) ?? throw new ArgumentException("Fighter unavailable: " + package)).Choose(form);
    }
}
