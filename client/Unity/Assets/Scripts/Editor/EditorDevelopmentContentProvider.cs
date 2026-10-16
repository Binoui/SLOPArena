#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SlopArena.Client;
using SlopArena.Client.Animation;
using SlopArena.Shared;

[InitializeOnLoad]
public static class EditorDevelopmentContentProvider
{
    static EditorDevelopmentContentProvider()
    {
        ClientSession.RegisterEditorDevelopmentContentProvider(TryBuild);
        CookedCharacterClientAssetResolver.RegisterEditorBindingCatalogFactory(
            CharacterAnimationCatalogGenerator.TryCreateAllowingMissingPresentation);
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
            CookedCharacterClientAssetResolver.ClearEditorDevelopmentCatalogs();
    }

    private static bool TryBuild(out MatchContentCatalog catalog, out string failure)
    {
        catalog = null;
        failure = null;
        CookedCharacterClientAssetResolver.ClearEditorDevelopmentCatalogs();
        var diagnostics = new List<CharacterDiagnostic>();
        var transientCatalogs = new List<CharacterAnimationCatalog>();
        try
        {
            var resolver = LocalContentResolver.CreateForMode(LocalContentMode.Development);
            var rosterResolution = resolver.ResolveRoster();
            if (!rosterResolution.Success || rosterResolution.Roster == null)
            {
                diagnostics.AddRange(rosterResolution.Diagnostics);
                return Fail(diagnostics, transientCatalogs, out failure);
            }

            var authoring = new CharacterPackageAuthoringService(resolver.ProjectRoot);
            var packages = new Dictionary<string, CookedCharacterPackageLoadResult>(StringComparer.Ordinal);
            var entries = new List<BuiltInRosterEntry>(rosterResolution.Roster.Entries.Count);
            foreach (var rosterEntry in rosterResolution.Roster.Entries)
            {

                if (!authoring.TryCompileForEditorPlay(rosterEntry.PackageId, out var package, out var animationCatalog, out var compileDiagnostics))
                {
                    diagnostics.AddRange(compileDiagnostics);
                    continue;
                }

                diagnostics.AddRange(compileDiagnostics);
                packages[rosterEntry.PackageId] = package;
                transientCatalogs.Add(animationCatalog);
                var identity = package.Identity;
                entries.Add(new BuiltInRosterEntry(
                    rosterEntry.Selector,
                    rosterEntry.PackageId,
                    new MatchContentPackageRequirement(
                        identity.PackageId,
                        identity.Version,
                        identity.CookedContentHash,
                        identity.PackageHash)));
            }

            if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error))
                return Fail(diagnostics, transientCatalogs, out failure);

            var manifest = new BuiltInRosterManifest(rosterResolution.Roster.SchemaVersion, entries);
            var built = new MatchContentCatalogBuilder().Build(manifest, packages);
            diagnostics.AddRange(built.Diagnostics);
            if (!built.IsValid || built.Catalog == null)
                return Fail(diagnostics, transientCatalogs, out failure);

            for (int i = 0; i < transientCatalogs.Count; i++)
            {
                transientCatalogs[i].hideFlags |= HideFlags.DontSave;
                var package = packages[transientCatalogs[i].PackageId];
                CookedCharacterClientAssetResolver.RegisterEditorDevelopmentCatalog(package.Identity, transientCatalogs[i]);
            }

            foreach (var placeholder in diagnostics.Where(x =>
                         x.Code == CharacterPackageAuthoringService.CommittedPosesDiagnosticCode))
                Debug.LogWarning($"[EditorContent] {placeholder.Path}: {placeholder.Message}");

            catalog = built.Catalog;
            return true;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new CharacterDiagnostic(
                CharacterDiagnosticSeverity.Error,
                "content.development.provider",
                "provider",
                ex.Message));
            return Fail(diagnostics, transientCatalogs, out failure);
        }
    }

    private static bool Fail(
        List<CharacterDiagnostic> diagnostics,
        List<CharacterAnimationCatalog> transientCatalogs,
        out string failure)
    {
        CookedCharacterClientAssetResolver.ClearEditorDevelopmentCatalogs();
        foreach (var transient in transientCatalogs)
            if (transient != null)
                UnityEngine.Object.DestroyImmediate(transient);
        failure = string.Join("; ", diagnostics.Select(d => $"{d.Code} ({d.Path}): {d.Message}"));
        return false;
    }
}
#endif
