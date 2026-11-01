using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using SlopArena.Client.Tools;

public static class CharacterPackageAuthoringSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Character Package Authoring")]
    public static void RunFightGuyAuthoringSelfTest()
    {
        using (var fixture = new CharacterPackageSelfTestScope("fightguy", "bonk"))
        {
            fixture.PrepareCookedPackage("fightguy");
            fixture.PrepareCookedPackage("bonk");
            RunAuthoringChecks();
        }
        UnityEngine.Debug.Log("[SLOPArena] Character package authoring self-test passed; fixture restored.");
    }

    private static void RunAuthoringChecks()
    {
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        string rosterPath = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "..", "..", "content-cooked/roster/manifest.json");
        BuiltInRosterManifest rosterBefore = BuiltInRosterManifestCodec.Load(rosterPath);
        BuiltInRosterEntry fightGuyBefore = rosterBefore.Resolve(CharacterClass.FightGuy)!;
        CharacterPackageInspectionResult initial = service.Inspect("fightguy");
        Require(initial.Success, "FightGuy inspection failed.");
        Require(initial.PackageId == "fightguy", "Inspection returned the wrong package ID.");
        Require(initial.Status == "valid", "FightGuy inspection did not report valid content.");
        Require(!initial.DirtyOrStale, "Fresh FightGuy inspection reported stale content.");
        Require(initial.Provenance != null &&
            initial.Provenance.PackageId == "fightguy" &&
            initial.Provenance.Payloads.Count == 3 &&
            initial.Provenance.UnityDependencies.Count > 0 &&
            initial.Provenance.CookStatus == "Valid",
            "Inspection did not expose verified provenance and cook metadata.");
        Require(initial.Slots.Count == CharacterPackageCompiler.CanonicalSlotIds.Count, "Inspection did not return all canonical slots.");
        var firstSourceSlot = initial.Source.Character.Slots.First(slot => slot.Id == "ground.1");
        Require(initial.Slots[0].Id == "ground.1" && initial.Slots[0].Name == firstSourceSlot.Name &&
            initial.Slots[0].StageCount == firstSourceSlot.Timeline.Stages.Count,
            "Inspection returned incorrect ground.1 metadata.");

        CharacterPackageCookResult directCook = service.Cook("fightguy");
        Require(directCook.Success, "Direct FightGuy cook failed.");
        Require(!string.IsNullOrEmpty(directCook.SourceHash) && !string.IsNullOrEmpty(directCook.CookedContentHash) && !string.IsNullOrEmpty(directCook.PackageHash),
            "Successful cook did not return hashes.");
        BuiltInRosterEntry fightGuyAfter = BuiltInRosterManifestCodec.Load(rosterPath).Resolve(CharacterClass.FightGuy)!;
        Require(fightGuyAfter.Selector == fightGuyBefore.Selector &&
            fightGuyAfter.Requirement.PackageId == "fightguy" &&
            fightGuyAfter.Requirement.Version == fightGuyBefore.Requirement.Version &&
            fightGuyAfter.Requirement.CookedContentHash == directCook.CookedContentHash &&
            fightGuyAfter.Requirement.PackageHash == directCook.PackageHash,
            "Admitted FightGuy cook did not refresh its roster requirement while preserving the selector.");
        byte[] rosterBeforeDryRun = File.ReadAllBytes(rosterPath);
        CharacterPackageCookResult dryRun = service.Cook("fightguy", true);
        Require(dryRun.Success && dryRun.DryRun && dryRun.Assembly != null && dryRun.ExpectedOutputs.Count > 0,
            "Dry-run did not return a complete non-mutating cook plan.");
        Require(File.ReadAllBytes(rosterPath).SequenceEqual(rosterBeforeDryRun),
            "Dry-run changed the roster manifest.");
        CharacterPackageVerificationResult verified = service.Verify("fightguy");
        Require(verified.Success && verified.Plan != null && verified.Plan.DryRun && verified.Inspection.Rostered,
            "Read-only package verification did not pass without changing roster state.");
        CharacterPackageInspectionResult commandInspect = SlopArenaCharacterCommands.Inspect("fightguy");
        Require(commandInspect.Success && commandInspect.PackageId == directCook.PackageId && commandInspect.SourceHash == directCook.SourceHash,
            "Inspect command did not delegate to the service result.");
        CharacterPackageCookResult commandCook = SlopArenaCharacterCommands.Cook("fightguy");
        Require(commandCook.Success && commandCook.SourceHash == directCook.SourceHash && commandCook.PackageHash == directCook.PackageHash,
            "Cook command did not delegate to the service result.");

        string projectRoot = UnityCharacterAssetCooker.ProjectRoot();
        string packageRoot = Path.Combine(projectRoot, "Assets/CharacterPackages/fightguy");
        string characterPath = Path.Combine(packageRoot, "character.json");
        string manifestPath = Path.Combine(packageRoot, "package.json");
        byte[] originalCharacter = File.ReadAllBytes(characterPath);
        string canonicalPath = Path.Combine(projectRoot, "..", "..", "content-cooked/fightguy");
        Dictionary<string, byte[]> canonical = SnapshotDirectory(canonicalPath);
        byte[] pose = File.ReadAllBytes(Path.Combine(projectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "poses.bin"));
        byte[] binding = File.ReadAllBytes(Path.Combine(projectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "client.bindings"));
        string statusPath = Path.Combine(projectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "cook-status.json");
        byte[] status = File.ReadAllBytes(statusPath);
        string generatedPath = Path.Combine(projectRoot, "Assets/Resources/Generated/CharacterPackages/fightguy/FightGuy_AnimationCatalog.asset");
        byte[] generated = File.ReadAllBytes(generatedPath);

        try
        {
            string invalidCharacter = MakeInvalidCharacter(
                File.ReadAllText(manifestPath),
                Encoding.UTF8.GetString(originalCharacter));
            File.WriteAllText(characterPath, invalidCharacter);

            CharacterPackageInspectionResult invalidInspect = service.Inspect("fightguy");
            Require(invalidInspect.Success && invalidInspect.Status == "invalid" && invalidInspect.DirtyOrStale,
                "Invalid source inspection did not report invalid content.");
            Require(invalidInspect.Diagnostics.Any(x => x.Code == "value.out-of-range" && x.Path == "character.operation.tick"),
                "Invalid source diagnostic was not propagated with its compiler path.");

            byte[] statusBeforeInvalidCook = File.ReadAllBytes(statusPath);
            CharacterPackageCookResult invalidCook = SlopArenaCharacterCommands.Cook("fightguy");
            Require(invalidCook.Diagnostics.Any(x => x.Code == "value.out-of-range" && x.Path == "character.operation.tick"),
                "Cook command did not return the compiler diagnostic.");
            Require(File.ReadAllBytes(Path.Combine(projectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "poses.bin")).SequenceEqual(pose),
                "Invalid cook changed pose output.");
            Require(File.ReadAllBytes(Path.Combine(projectRoot, CharacterCookOutput.FightGuy.IntermediateDirectory, "client.bindings")).SequenceEqual(binding),
                "Invalid cook changed binding output.");
            Require(File.ReadAllBytes(statusPath).SequenceEqual(statusBeforeInvalidCook), "Invalid cook changed persisted status.");
            Require(File.ReadAllBytes(generatedPath).SequenceEqual(generated), "Invalid cook changed generated catalog.");
            AssertDirectorySnapshot(canonicalPath, canonical);
        }
        finally
        {
            File.WriteAllBytes(characterPath, originalCharacter);
            AssetDatabase.Refresh();
        }
        RunBonkProbeSelfTest();
        RunPackageGenericPostprocessorSelfTest();
        RunIsolatedPackageBoundarySelfTest();
    }

    private static void RunIsolatedPackageBoundarySelfTest()
    {
        string packageId = "authoring-test-" + DateTime.UtcNow.Ticks.ToString("x");
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        CharacterPackageCreateResult created = service.NewPackage(packageId, "Authoring Test", "Self-test", "MIT", "Self-test fixture");
        Require(created.Success, "Isolated package creation failed.");
        try
        {
            CharacterPackageInspectionResult inspection = service.Inspect(packageId);
            Require(inspection.Success && inspection.PackageId == packageId && !inspection.Rostered &&
                inspection.Slots.Count == CanonicalSlotProjection.All.Count,
                "New package did not open as an unrostered canonical package.");
            string foreignClipPath = "Assets/CharacterPackages/" + packageId + "/foreign.anim";
            AssetDatabase.CreateAsset(new UnityEngine.AnimationClip(), foreignClipPath);
            CharacterPackageBindingResult foreign = service.Bind("bonk", "anim.run", foreignClipPath);
            Require(!foreign.Success && foreign.Dependency?.Classification == "foreign" &&
                foreign.Dependency.SourcePackageId == packageId &&
                foreign.Diagnostics.Any(diagnostic => diagnostic.Code == "asset-catalog.dependency.foreign"),
                "Foreign package binding was not rejected by the domain boundary.");
        }
        finally
        {
            AssetDatabase.DeleteAsset("Assets/CharacterPackages/" + packageId);
            AssetDatabase.Refresh();
        }
    }

    private static void RunBonkProbeSelfTest()
    {
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        CharacterPackageInspectionResult inspection = service.Inspect("bonk");
        Require(inspection.Success && inspection.PackageId == "bonk" && inspection.DisplayName == "Sir Bonk",
            "Bonk package was not discovered by ID.");
        Require(inspection.Status == "valid" && !inspection.DirtyOrStale &&
            inspection.Slots.Count == CanonicalSlotProjection.All.Count &&
            inspection.Slots.All(slot => slot.Present && slot.StageCount > 0),
            "Bonk probe did not resolve valid canonical package content.");
        Require(!inspection.Diagnostics.Any(x => x.Code == "asset-catalog.clip.missing"),
            "Bonk shared semantic bindings unexpectedly reported missing clips.");
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>("Assets/CharacterPackages/bonk/CharacterAssetCatalog.asset");
        var moveIds = new HashSet<string>(inspection.Source.Character.Slots
            .SelectMany(slot => slot.Timeline.Stages).SelectMany(stage => stage.AnimationIds), StringComparer.Ordinal);
        var moveBindings = catalog.Bindings.Where(binding => binding != null && moveIds.Contains(binding.SemanticId)).ToList();
        Require(moveBindings.Count == moveIds.Count && moveIds.SetEquals(moveBindings.Select(binding => binding.SemanticId)) &&
            moveIds.Contains("anim.bonk.a1") && moveIds.Contains("anim.bonk.a2"),
            "Bonk authored move animation IDs did not resolve to unique catalog bindings.");
        var beforeMoveClips = moveBindings.ToDictionary(binding => binding.SemanticId, binding => binding.Clip, StringComparer.Ordinal);
        try
        {
            Require(service.Bind("bonk", "anim.bonk.a1", AssetDatabase.GetAssetPath(beforeMoveClips["anim.bonk.a2"])).Success &&
                moveBindings.Where(binding => binding.SemanticId != "anim.bonk.a1").All(binding => binding.Clip == beforeMoveClips[binding.SemanticId]),
                "Bonk move binding replacement changed unrelated moves.");
        }
        finally
        {
            // A contributor may have selected a different clip. Restore that
            // exact binding, not the historical bonk_a_1 fixture.
            catalog.Bindings.First(binding => binding.SemanticId == "anim.bonk.a1").Clip = beforeMoveClips["anim.bonk.a1"];
            Require(CharacterPackageAuthoringService.PersistCatalog("Assets/CharacterPackages/bonk", catalog,
                out _, out _, out _), "Bonk original binding could not be restored.");
        }
        CharacterPackageCookResult cook = service.Cook("bonk");
        Require(cook.Success && !string.IsNullOrEmpty(cook.CookedContentHash) && !string.IsNullOrEmpty(cook.PackageHash),
            "Bonk probe did not cook with independent move bindings.");
        var preview = AbilityLabPackagePreviewLoader.Load("bonk");
        Require(preview.IsAvailable && preview.Identity != null && preview.Identity.PackageId == "bonk",
            "Cooked Bonk package preview did not load.");
    }

    private static void RunPackageGenericPostprocessorSelfTest()
    {
        var affected = CharacterCookAssetPostprocessor.FindAffectedPackages(new[]
        {
            "Assets/CharacterPackages/bonk/package.json",
            "Assets/CharacterPackages/bonk/character.json",
            "Assets/CharacterPackages/bonk/CharacterAssetCatalog.asset",
            "Assets/CharacterPackages/bonk/CharacterAssetCatalog.asset.meta",
        });
        Require(affected.Contains("bonk") && !affected.Contains("fightguy"),
            "Package-local dependency detection was not package-specific.");
        CharacterCookAssetPostprocessor.ResetQueueRequestCount();
        CharacterCookAssetPostprocessor.QueueRecook(new[] { "bonk" });
        CharacterCookAssetPostprocessor.QueueRecook(new[] { "bonk" });
        Require(CharacterCookAssetPostprocessor.QueueRequestCount == 1 &&
            CharacterCookAssetPostprocessor.PendingPackages.Contains("bonk"),
            "Repeated package recook requests were not coalesced.");
        CharacterCookAssetPostprocessor.ResetQueueRequestCount();
        Require(new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook("bonk").Success,
            "Could not restore Bonk after queue coalescing assertion.");
    }

    private static string MakeInvalidCharacter(string manifestJson, string characterJson)
    {
        CharacterPackageSource source = CharacterPackageSourceCodec.Load(manifestJson, characterJson).Source
            ?? throw new InvalidDataException("FightGuy source could not be loaded for the authoring self-test.");
        int slotIndex = source.Character.Slots.ToList().FindIndex(x => x.Id == "ground.1");
        if (slotIndex < 0) throw new InvalidDataException("FightGuy ground.1 source slot is missing.");
        CharacterSlotSource slot = source.Character.Slots[slotIndex];
        int stageIndex = slot.Timeline.Stages.ToList().FindIndex(stage => stage.Operations.Any(operation => operation is SpawnHitboxOperationSource));
        if (stageIndex < 0) throw new InvalidDataException("FightGuy ground.1 has no hitbox for the invalid-source test.");
        CharacterStageSource stage = slot.Timeline.Stages[stageIndex];
        int operationIndex = stage.Operations.ToList().FindIndex(operation => operation is SpawnHitboxOperationSource);
        var invalidOperation = ((SpawnHitboxOperationSource)stage.Operations[operationIndex]) with { Tick = stage.DurationTicks };
        var operations = stage.Operations.ToList();
        operations[operationIndex] = invalidOperation;
        var stages = slot.Timeline.Stages.ToList();
        stages[stageIndex] = stage with { Operations = operations };
        var slots = source.Character.Slots.ToList();
        slots[slotIndex] = slot with { Timeline = new CharacterTimelineSource(stages) };
        return CharacterPackageSourceCodec.SerializeCharacter(source.Character with { Slots = slots });
    }

    private static Dictionary<string, byte[]> SnapshotDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                x => x.Substring(directory.Length + 1).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);
    }

    private static void AssertDirectorySnapshot(string directory, Dictionary<string, byte[]> expected)
    {
        Dictionary<string, byte[]> actual = SnapshotDirectory(directory);
        Require(expected != null && actual != null && expected.Count == actual.Count, "Cooked package file set changed.");
        foreach (var entry in expected)
            Require(actual.TryGetValue(entry.Key, out var bytes) && bytes.SequenceEqual(entry.Value),
                "Cooked package changed after invalid cook: " + entry.Key);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
