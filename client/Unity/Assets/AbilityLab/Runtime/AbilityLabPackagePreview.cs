using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Tools;

public sealed class AbilityLabPackagePreviewResult
{
    public AbilityLabPackagePreviewResult(
        bool isAvailable,
        CookedCharacterPackage package,
        BakedAnimationData bakedPoses,
        CharacterAnimationCatalog animationCatalog,
        GameObject rig,
        MatchContentIdentity identity,
        IReadOnlyList<SlotAddress> slots,
        IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        IsAvailable = isAvailable;
        Package = package;
        BakedPoses = bakedPoses;
        AnimationCatalog = animationCatalog;
        Rig = rig;
        Identity = identity;
        Slots = new ReadOnlyCollection<SlotAddress>(new List<SlotAddress>(slots ?? Array.Empty<SlotAddress>()));
        Diagnostics = new ReadOnlyCollection<CharacterDiagnostic>(
            new List<CharacterDiagnostic>(diagnostics ?? Array.Empty<CharacterDiagnostic>()));
    }

    public bool IsAvailable { get; }
    public CookedCharacterPackage Package { get; }
    public BakedAnimationData BakedPoses { get; }
    public CharacterAnimationCatalog AnimationCatalog { get; }
    public GameObject Rig { get; }
    public MatchContentIdentity Identity { get; }
    public IReadOnlyList<SlotAddress> Slots { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
}

public static class AbilityLabPackagePreviewLoader
{
    public static AbilityLabPackagePreviewResult Load(string packageId)
    {
        var resolver = LocalContentResolver.CreateDefault();
        var resolution = resolver.ResolveCookedPackage(packageId);
        if (!resolution.Success || resolution.Requirement == null)
            return Unavailable(resolution.Diagnostics);

        CookedCharacterPackageLoadResult loaded;
        try
        {
            loaded = CookedCharacterPackageLoader.LoadDirectory(
                Path.Combine(resolution.RootPath, packageId),
                resolution.Requirement);
        }
        catch (Exception ex)
        {
            return Unavailable(new[]
            {
                Error("preview.package.load-failed", resolution.ManifestPath, ex.Message),
            });
        }

        if (!loaded.IsValid || loaded.Package == null || loaded.BakedAnimation == null)
        {
            var diagnostics = new List<CharacterDiagnostic>(loaded.Diagnostics);
            if (loaded.Package != null && loaded.BakedAnimation == null)
                diagnostics.Add(Error("preview.poses.missing", Path.Combine(resolution.RootPath, packageId, CharacterPackageAssembler.PosePath), "Cooked pose data is missing."));
            return Unavailable(diagnostics);
        }

        var previewDiagnostics = new List<CharacterDiagnostic>(loaded.Diagnostics);
        if (!CookedCharacterClientAssetResolver.TryResolve(
                loaded.Identity,
                loaded.Package,
                out var animationCatalog,
                out var rig,
                out var bindingError))
        {
#if UNITY_EDITOR
            // Generated catalogs are gitignored, so fresh clones never have one. Build it
            // from the committed bindings of the package verified above; clips that are not
            // installed leave their moves unanimated, with overlays intact (ADR-0038).
            string bindingPath = Path.Combine(resolution.RootPath, packageId, CharacterPackageAssembler.BindingPath);
            byte[] bindingBytes;
            try
            {
                bindingBytes = File.ReadAllBytes(bindingPath);
            }
            catch (Exception ex)
            {
                return Unavailable(new[] { Error("preview.binding.failed", bindingPath, $"{bindingError} {ex.Message}") });
            }
            if (!CookedCharacterClientAssetResolver.TryCreateEditorCatalogFromBindings(
                    loaded.Identity, bindingBytes, out animationCatalog, out var committedError))
            {
                return Unavailable(new[]
                {
                    Error("preview.binding.failed", $"Generated/CharacterPackages/{packageId}", $"{bindingError} {committedError}"),
                });
            }
            rig = animationCatalog.Rig;
            int missingClips = Array.FindAll(animationCatalog.Animations, entry => entry != null && entry.Clip == null).Length;
            if (missingClips > 0)
                previewDiagnostics.Add(new CharacterDiagnostic(
                    CharacterDiagnosticSeverity.Warning,
                    "preview.clips.not-installed",
                    bindingPath,
                    $"{missingClips} animation clip(s) are not installed. Those moves show hitboxes and hurtboxes but don't animate."));
#else
            return Unavailable(new[]
            {
                Error("preview.binding.failed", $"Generated/CharacterPackages/{packageId}", bindingError),
            });
#endif
        }

#if UNITY_EDITOR
        // Keep an owned snapshot so asset refresh cannot invalidate the
        // animation catalog used by an active authoring preview.
        animationCatalog = UnityEngine.Object.Instantiate(animationCatalog);
        animationCatalog.hideFlags = HideFlags.DontSave;
#endif

        return new AbilityLabPackagePreviewResult(
            true,
            loaded.Package,
            loaded.BakedAnimation,
            animationCatalog,
            rig,
            loaded.Identity,
            CanonicalSlotProjection.All,
            previewDiagnostics);
    }

    private static AbilityLabPackagePreviewResult Unavailable(IReadOnlyList<CharacterDiagnostic> diagnostics)
        => new(false, null, null, null, null, null, Array.Empty<SlotAddress>(), diagnostics);

    private static CharacterDiagnostic Error(string code, string path, string message)
        => new(CharacterDiagnosticSeverity.Error, code, path, message);
}
