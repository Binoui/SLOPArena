#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using SlopArena.Client;
using SlopArena.Client.Animation;
using SlopArena.Shared;

public static class EditorDevelopmentContentSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Editor Development Content")]
    public static void Run()
    {
        using var fixture = new CharacterPackageSelfTestScope("fightguy");
        var resolver = LocalContentResolver.CreateForMode(LocalContentMode.Development);
        string repositoryRoot = Path.GetFullPath(Path.Combine(resolver.ProjectRoot, "..", ".."));
        string packageRoot = Path.Combine(resolver.ProjectRoot, "Assets", "CharacterPackages", "fightguy");
        string characterPath = Path.Combine(packageRoot, "character.json");
        string generatedPath = Path.Combine(resolver.ProjectRoot, CharacterCookOutput.For("fightguy").GeneratedAssetPath);
        byte[] originalCharacter = File.ReadAllBytes(characterPath);
        Dictionary<string, byte[]> cookedSnapshot = SnapshotDirectory(Path.Combine(repositoryRoot, "content-cooked", "fightguy"));
        byte[] generatedSnapshot = File.ReadAllBytes(generatedPath);

        try
        {
            var persistedRoster = resolver.ResolveRoster();
            Require(persistedRoster.Success && persistedRoster.Roster != null, Format(persistedRoster.Diagnostics));
            Require(persistedRoster.Roster.TryGetBySelector(CharacterClass.FightGuy, out var persistedRosterEntry), "Persisted FightGuy roster entry is missing.");
            Require(ClientSession.TryBuildPersistedLocalMatchCatalog(out var persistedCatalog, out var persistedFailure) && persistedCatalog != null,
                persistedFailure ?? "Persisted local catalog failed.");
            var persistedEntry = persistedCatalog.Resolve(CharacterClass.FightGuy);
            Require(persistedEntry != null && persistedEntry.Identity.PackageId == persistedRosterEntry.PackageId &&
                persistedEntry.Identity.Version == persistedRosterEntry.Requirement.Version &&
                persistedEntry.Identity.CookedContentHash == persistedRosterEntry.Requirement.CookedContentHash &&
                persistedEntry.Identity.PackageHash == persistedRosterEntry.Requirement.PackageHash,
                "Persisted FightGuy identity did not match its roster requirement.");

            var loaded = CharacterPackageSourceCodec.Load(
                File.ReadAllText(Path.Combine(packageRoot, "package.json")),
                Encoding.UTF8.GetString(originalCharacter));
            Require(loaded.IsValid && loaded.Source != null, Format(loaded.Diagnostics));

            float changedRunSpeed = loaded.Source.Character.Movement.RunSpeed + 3.75f;
            var changedSource = loaded.Source with
            {
                Character = loaded.Source.Character with
                {
                    Movement = loaded.Source.Character.Movement with { RunSpeed = changedRunSpeed }
                }
            };
            File.WriteAllText(characterPath, CharacterPackageSourceCodec.SerializeCharacter(changedSource.Character), Encoding.UTF8);

            Require(ClientSession.TryBuildLocalMatchCatalog(out var changedCatalog, out var changedFailure) && changedCatalog != null,
                changedFailure ?? "Editor development catalog failed for valid source.");
            var changedEntry = changedCatalog.Resolve(CharacterClass.FightGuy);
            Require(changedEntry != null && Math.Abs(changedEntry.Definition.Movement.RunSpeed - changedRunSpeed) < 0.0001f,
                "Editor development catalog did not use the changed RunSpeed.");
            Require(changedEntry.CookedCharacterPackage != null && changedEntry.BakedAnimation != null,
                "Editor development catalog did not retain cooked runtime and baked animation payloads.");
            Require(CookedCharacterClientAssetResolver.TryResolve(
                    changedEntry.Identity,
                    changedEntry.CookedCharacterPackage,
                    out CharacterAnimationCatalog transientCatalog,
                    out _,
                    out _,
                    out var transientFailure) && transientCatalog != null &&
                transientCatalog.SourceHash == changedEntry.Identity.SourceHash,
                transientFailure ?? "Transient semantic catalog did not resolve.");
            Require(SameSnapshot(SnapshotDirectory(Path.Combine(repositoryRoot, "content-cooked", "fightguy")), cookedSnapshot) &&
                File.ReadAllBytes(generatedPath).SequenceEqual(generatedSnapshot),
                "Valid Editor Play compilation modified persisted cooked content.");

            CharacterPackageSource invalidSource = CreateInvalidSource(loaded.Source);
            File.WriteAllText(characterPath, CharacterPackageSourceCodec.SerializeCharacter(invalidSource.Character), Encoding.UTF8);
            Require(!ClientSession.TryBuildLocalMatchCatalog(out var invalidCatalog, out var invalidFailure) && invalidCatalog == null,
                "Invalid source unexpectedly produced an Editor development catalog.");
            Require(invalidFailure != null && invalidFailure.Contains("value.out-of-range", StringComparison.Ordinal) &&
                invalidFailure.Contains("character.operation.tick", StringComparison.Ordinal),
                "Invalid source diagnostic did not include value.out-of-range at character.operation.tick: " + invalidFailure);
            Require(ClientSession.TryBuildPersistedLocalMatchCatalog(out var restoredPersistedCatalog, out var restoredPersistedFailure) &&
                restoredPersistedCatalog != null && restoredPersistedCatalog.Resolve(CharacterClass.FightGuy)?.Identity == persistedEntry.Identity,
                restoredPersistedFailure ?? "Persisted path failed after invalid Editor source.");
            Require(SameSnapshot(SnapshotDirectory(Path.Combine(repositoryRoot, "content-cooked", "fightguy")), cookedSnapshot) &&
                File.ReadAllBytes(generatedPath).SequenceEqual(generatedSnapshot),
                "Invalid Editor Play compilation modified persisted cooked content.");

            RequireUninstalledClipUsesCommittedPoses(characterPath, originalCharacter, loaded.Source);
            Require(SameSnapshot(SnapshotDirectory(Path.Combine(repositoryRoot, "content-cooked", "fightguy")), cookedSnapshot) &&
                File.ReadAllBytes(generatedPath).SequenceEqual(generatedSnapshot),
                "Editor Play without an installed clip modified persisted cooked content.");
        }
        finally
        {
            File.WriteAllBytes(characterPath, originalCharacter);
            CookedCharacterClientAssetResolver.ClearEditorDevelopmentCatalogs();
        }
    }

    // ADR-0038: a bound clip that is not installed degrades to the committed pose data,
    // while an edit that changes pose baking still needs the clips.
    private static void RequireUninstalledClipUsesCommittedPoses(
        string characterPath, byte[] originalCharacter, CharacterPackageSource source)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(
            "Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset");
        Require(catalog != null, "FightGuy asset catalog is missing.");
        var binding = catalog.Bindings.First(x => x != null && x.Clip != null);
        var installedClip = binding.Clip;
        File.WriteAllBytes(characterPath, originalCharacter);
        try
        {
            binding.Clip = null; // in memory only; the fixture re-imports the asset
            Require(ClientSession.TryBuildLocalMatchCatalog(out var placeholderCatalog, out var placeholderFailure) &&
                placeholderCatalog != null,
                placeholderFailure ?? "Editor development catalog failed with an uninstalled clip.");
            var entry = placeholderCatalog.Resolve(CharacterClass.FightGuy);
            Require(entry?.CookedCharacterPackage != null && entry.BakedAnimation != null,
                "Editor development catalog did not use the committed pose data.");
            Require(CookedCharacterClientAssetResolver.TryResolve(
                    entry.Identity,
                    entry.CookedCharacterPackage,
                    out CharacterAnimationCatalog animations,
                    out var rig,
                    out _,
                    out var resolveFailure) && rig != null &&
                animations.Animations.Any(x => x.SemanticId == binding.SemanticId && x.Clip == null),
                resolveFailure ?? "The uninstalled clip did not become a null catalog entry.");

            var rebakeSource = source with
            {
                Character = source.Character with
                {
                    Presentation = source.Character.Presentation with
                    {
                        VisualScale = source.Character.Presentation.VisualScale + 0.1f
                    }
                }
            };
            File.WriteAllText(characterPath, CharacterPackageSourceCodec.SerializeCharacter(rebakeSource.Character), Encoding.UTF8);
            Require(!ClientSession.TryBuildLocalMatchCatalog(out var rebakeCatalog, out var rebakeFailure) && rebakeCatalog == null &&
                rebakeFailure != null && rebakeFailure.Contains("asset-catalog.pose.rebake-required", StringComparison.Ordinal),
                "A pose-bake edit without the clip did not fail with asset-catalog.pose.rebake-required: " + rebakeFailure);
        }
        finally
        {
            binding.Clip = installedClip;
            File.WriteAllBytes(characterPath, originalCharacter);
        }
    }

    private static CharacterPackageSource CreateInvalidSource(CharacterPackageSource source)
    {
        for (int slotIndex = 0; slotIndex < source.Character.Slots.Count; slotIndex++)
        {
            var slot = source.Character.Slots[slotIndex];
            for (int stageIndex = 0; stageIndex < slot.Timeline.Stages.Count; stageIndex++)
            {
                var stage = slot.Timeline.Stages[stageIndex];
                for (int operationIndex = 0; operationIndex < stage.Operations.Count; operationIndex++)
                {
                    if (stage.Operations[operationIndex] is SpawnHitboxOperationSource hitbox)
                    {
                        var invalidOperation = hitbox with { Tick = stage.DurationTicks };
                        var operations = stage.Operations.ToList();
                        operations[operationIndex] = invalidOperation;
                        var invalidStage = stage with { Operations = operations };
                        var stages = slot.Timeline.Stages.ToList();
                        stages[stageIndex] = invalidStage;
                        var invalidSlot = slot with { Timeline = new CharacterTimelineSource(stages) };
                        var slots = source.Character.Slots.ToList();
                        slots[slotIndex] = invalidSlot;
                        return source with { Character = source.Character with { Slots = slots } };
                    }
                }
            }
        }
        throw new InvalidOperationException("FightGuy has no SpawnHitbox operation for the invalid-source self-test.");
    }

    private static Dictionary<string, byte[]> SnapshotDirectory(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static string Format(IEnumerable<CharacterDiagnostic> diagnostics)
        => string.Join("; ", diagnostics.Select(d => $"{d.Code} ({d.Path}): {d.Message}"));

    private static bool SameSnapshot(Dictionary<string, byte[]> current, Dictionary<string, byte[]> expected)
        => current.Count == expected.Count &&
            current.All(pair => expected.TryGetValue(pair.Key, out var bytes) && pair.Value.SequenceEqual(bytes));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
