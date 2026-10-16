using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using System.IO;
using System.Text.Json;
using UnityEngine;
using SlopArena.Client.Animation;
using SlopArena.Shared;

public static class CharacterAssetCookerSelfTest
{
    private readonly struct LowPoseBakeContext
    {
        public readonly float VisualScale;
        public readonly float HurtboxBoneScale;
        public readonly float ModelYOffset;
        public readonly float CapsuleHeight;
        public readonly float HipHeight;
        public readonly string Crouch;
        public readonly string Slide;

        public LowPoseBakeContext(
            float visualScale,
            float hurtboxBoneScale,
            float modelYOffset,
            float capsuleHeight,
            float hipHeight,
            string crouch,
            string slide)
        {
            VisualScale = visualScale;
            HurtboxBoneScale = hurtboxBoneScale;
            ModelYOffset = modelYOffset;
            CapsuleHeight = capsuleHeight;
            HipHeight = hipHeight;
            Crouch = crouch;
            Slide = slide;
        }
    }

    [MenuItem("Tools/SLOPArena/Tests/Weapon Marker Poses")]
    public static void RunWeaponMarkerSelfTest()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>("Assets/CharacterPackages/bonk/CharacterAssetCatalog.asset");
        var entry = catalog.WeaponConfig.Entries.First(x => x != null);
        var lowContext = ReadLowPoseBakeContext(catalog);
        var animations = catalog.Bindings.Where(x => x.Clip != null)
            .Select(x => new DeterministicPoseTrackBaker.SampledAnimation
            {
                SemanticId = x.SemanticId,
                PoseTrackId = x.PoseTrackId,
                Clip = x.Clip,
                FrameCount = Mathf.CeilToInt(x.Clip.length * 60),
                IsLowPosture = string.Equals(x.SemanticId, lowContext.Crouch, StringComparison.Ordinal)
                    || string.Equals(x.SemanticId, lowContext.Slide, StringComparison.Ordinal),
            }).ToArray();
        var baked = BakedAnimationData.LoadFromBin(DeterministicPoseTrackBaker.Bake(
            catalog.Rig, animations, 60, catalog.WeaponConfig,
            lowContext.VisualScale, lowContext.HurtboxBoneScale,
            lowContext.ModelYOffset, lowContext.CapsuleHeight, lowContext.HipHeight));
        var rig = UnityEngine.Object.Instantiate(catalog.Rig);
        var weapon = UnityEngine.Object.Instantiate(entry.Prefab);
        rig.hideFlags = weapon.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var hand = rig.GetComponentsInChildren<Transform>(true).First(x => x.name == entry.BoneName);
            var hips = rig.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
            var markers = weapon.GetComponentsInChildren<Transform>(true);
            var hilt = markers.Single(x => x.name == "bladeHilt");
            var tip = markers.Single(x => x.name == "bladeEnd");
            int hiltIndex = Array.IndexOf(baked.BoneNames, "_weapon_hilt");
            int tipIndex = Array.IndexOf(baked.BoneNames, "_weapon_tip");
            int frames = 0;
            Vector3 ExpectedMarkerPosition(Transform marker, bool lowPosture)
                => lowPosture
                    ? BakeLowPose(rig.transform.InverseTransformPoint(marker.position), lowContext)
                    : marker.position - hips.position;

            foreach (var animation in animations)
            {
                for (int frame = 0; frame < animation.FrameCount; frame++)
                {
                    animation.Clip.SampleAnimation(rig, frame / 60f);
                    // Place an actual prefab instance as WeaponAttach does, including its scale.
                    weapon.transform.position = hand.TransformPoint(entry.PositionOffset);
                    weapon.transform.rotation = hand.rotation * Quaternion.Euler(entry.RotationOffset);
                    AssertMarkerPose(hiltIndex, ExpectedMarkerPosition(hilt, animation.IsLowPosture), "_weapon_hilt");
                    AssertMarkerPose(tipIndex, ExpectedMarkerPosition(tip, animation.IsLowPosture), "_weapon_tip");
                    frames++;

                    void AssertMarkerPose(int boneIndex, Vector3 expected, string bone)
                    {
                        if (!baked.GetBonePosition(animation.PoseTrackId, frame, boneIndex, out float x, out float y, out float z))
                            throw new InvalidOperationException($"Missing {bone} at {animation.SemanticId}:{frame}.");
                        float error = Vector3.Distance(expected, new Vector3(x, y, z));
                        if (error > 0.0001f)
                            throw new InvalidOperationException($"{bone} differs from rendered marker by {error:F6}m at {animation.SemanticId}:{frame}.");
                    }
                }
            }
            Debug.Log($"[SLOPArena] Weapon marker poses passed: {animations.Length} clips, {frames} frames, both endpoints within 0.1mm.");
        }

        finally
        {
            UnityEngine.Object.DestroyImmediate(weapon);
            UnityEngine.Object.DestroyImmediate(rig);
        }
    }
    private static LowPoseBakeContext ReadLowPoseBakeContext(CharacterAssetCatalog catalog)
    {
        string path = UnityCharacterAssetCooker.ResolveFile(
            $"Assets/CharacterPackages/{catalog.PackageId}", "character.json");
        var loaded = CharacterPackageSourceCodec.Load(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(path), "package.json")),
            File.ReadAllText(path));
        if (!loaded.IsValid)
            throw new InvalidOperationException("Weapon-marker source did not load.");
        var character = loaded.Source.Character;
        var presentation = character.Presentation;
        return new LowPoseBakeContext(
            presentation.VisualScale, presentation.HurtboxBoneScale,
            presentation.ModelYOffset, character.CapsuleHeight, character.HipHeight,
            presentation.Crouch, presentation.Slide);
    }

    private static Vector3 BakeLowPose(Vector3 rootLocalPosition, LowPoseBakeContext context)
        => new(
            rootLocalPosition.x * context.VisualScale / context.HurtboxBoneScale,
            (rootLocalPosition.y * context.VisualScale + context.ModelYOffset
                + context.CapsuleHeight * 0.5f - context.HipHeight) / context.HurtboxBoneScale,
            rootLocalPosition.z * context.VisualScale / context.HurtboxBoneScale);

    [MenuItem("Tools/SLOPArena/Tests/Character Asset Cooker")]
    public static void RunFightGuySelfTest()
    {
        using (var fixture = new CharacterPackageSelfTestScope("fightguy"))
        {
            fixture.PrepareCookedPackage("fightguy");
            RunFightGuyChecks();
        }
        Debug.Log("[SLOPArena] FightGuy cooker self-test passed; fixture restored.");
    }

    private static void RunFightGuyChecks()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>("Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset");
        if (catalog == null) throw new InvalidOperationException("FightGuy catalog is missing.");
        string[] expectedIds = catalog.Bindings.Select(binding => binding.SemanticId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        CharacterAssetCookResult first = Cook(catalog);
        CharacterAssetCookResult second = Cook(catalog);
        CharacterPackageAssemblyResult firstPackage = CharacterPackageAssembler.Assemble(UnityCharacterAssetCooker.BuildPackageInput(first));
        CharacterPackageAssemblyResult secondPackage = CharacterPackageAssembler.Assemble(UnityCharacterAssetCooker.BuildPackageInput(second));
        AssertEqual(first.SourceHash, second.SourceHash, "unchanged source hash");
        AssertBytes(first.PoseBytes, second.PoseBytes, "unchanged pose bytes");
        AssertBytes(first.BindingBytes, second.BindingBytes, "unchanged binding bytes");
        AssertBytes(firstPackage.ManifestBytes, secondPackage.ManifestBytes, "unchanged manifest bytes");
        AssertBytes(firstPackage.RuntimeBytes, secondPackage.RuntimeBytes, "unchanged runtime bytes");
        AssertEqual(firstPackage.PackageHash, secondPackage.PackageHash, "unchanged package hash");
        AssertEqual(expectedIds, first.Animations.Select(x => x.SemanticId).OrderBy(x => x, StringComparer.Ordinal).ToArray(), "semantic IDs");
        var baked = BakedAnimationData.LoadFromBin(first.PoseBytes);
        AssertEqual(expectedIds.Length, baked.Animations.Length, "pose animation count");
        foreach (string id in expectedIds)
            if (!first.Animations.Any(x => x.SemanticId == id)) throw new InvalidOperationException($"Missing generated binding: {id}");
        var committedFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [CharacterPackageAssembler.ManifestPath] = firstPackage.ManifestBytes,
            [CharacterPackageAssembler.RuntimePath] = firstPackage.RuntimeBytes,
            [CharacterPackageAssembler.PosePath] = firstPackage.PoseBytes,
            [CharacterPackageAssembler.BindingPath] = firstPackage.BindingBytes,
        };
        if (!CharacterPackageAssembler.Verify(committedFiles).IsValid)
            throw new InvalidOperationException("Assembled FightGuy package did not verify.");
        committedFiles[CharacterPackageAssembler.PosePath] = firstPackage.PoseBytes.Concat(new byte[] { 0 }).ToArray();
        if (CharacterPackageAssembler.Verify(committedFiles).IsValid)
            throw new InvalidOperationException("Cross-payload mismatch unexpectedly verified.");
        var generated = AssetDatabase.LoadAssetAtPath<CharacterAnimationCatalog>(
            "Assets/Resources/Generated/CharacterPackages/fightguy/FightGuy_AnimationCatalog.asset");
        if (generated == null || generated.Animations.Length != expectedIds.Length ||
            generated.Animations.Any(x => x == null || x.Clip == null))
            throw new InvalidOperationException("Generated catalog did not resolve every animation binding.");
        var invalidRig = new GameObject("FightGuyCookerInvalidRig");
        try
        {
            ExpectCode(catalog, c => c.Rig = invalidRig, "asset-catalog.rig.invalid");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(invalidRig);
        }
        var incompatibleRig = new GameObject("FightGuyCookerIncompatibleRig");
        incompatibleRig.AddComponent<Animator>();
        try
        {
            ExpectCode(catalog, c => c.Rig = incompatibleRig, "asset-catalog.rig.incompatible");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(incompatibleRig);
        }
        ExpectCode(catalog, c => c.SampleRate = 30, "asset-catalog.sample.invalid");
        ExpectCode(catalog, c => c.Bindings[0].Clip = null, "asset-catalog.clip.missing");
        ExpectCode(catalog, c => c.Bindings[0].Clip = new AnimationClip(), "asset-catalog.clip.unsupported");
        ExpectCode(catalog, c => c.Bindings[0].Clip = new AnimationClip(), "asset-catalog.clip.unresolved");
        ExpectCode(catalog, c => c.Bindings[1].SemanticId = c.Bindings[0].SemanticId, "asset-catalog.id.duplicate");
        ExpectCode(catalog, c => c.Bindings[0].PoseTrackId = c.Bindings[1].PoseTrackId, "asset-catalog.id.duplicate");
        ExpectCode(catalog, c => c.Bindings[0].PoseTrackId = "", "asset-catalog.id.invalid");
        ExpectCode(catalog, c => c.Bindings = c.Bindings.Where(x => x.SemanticId != "anim.cyclone-kick").ToArray(), "reference.animation.unresolved");

        var alternate = catalog.Bindings.FirstOrDefault(x => x.SemanticId != "anim.cyclone-kick" && x.Clip != null)?.Clip;
        if (alternate != null)
        {
            CharacterAssetCookResult changed = Cook(catalog, c => c.Bindings.First(x => x.SemanticId == "anim.cyclone-kick").Clip = alternate);
            if (changed.SourceHash == first.SourceHash) throw new InvalidOperationException("Changing the R clip did not change source hash.");
            if (changed.BindingBytes.SequenceEqual(first.BindingBytes)) throw new InvalidOperationException("Changing the R clip did not change bindings.");
            if (changed.PoseBytes.SequenceEqual(first.PoseBytes)) throw new InvalidOperationException("Changing the R clip did not change poses.");
            if (!changed.Animations.Any(x => x.SemanticId == "anim.idle")) throw new InvalidOperationException("Unrelated binding was lost.");
        }
        AssertDependencyIdentityInvalidatesHash(catalog, first);
        AssertFailedCookPreservesLastValidOutput(catalog);
        CharacterCookAssetPostprocessor.ResetQueueRequestCount();
        CharacterCookAssetPostprocessor.QueueRecook();
        CharacterCookAssetPostprocessor.QueueRecook();
        if (CharacterCookAssetPostprocessor.QueueRequestCount != 1)
            throw new InvalidOperationException("Repeated postprocessor notifications were not coalesced.");
        var finalCook = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook("fightguy");
        if (!finalCook.Success) throw new InvalidOperationException("Final FightGuy cook failed.");
    }
    private static void AssertDependencyIdentityInvalidatesHash(CharacterAssetCatalog catalog, CharacterAssetCookResult result)
    {
        var altered = result.Dependencies.Select(x => new CharacterCookDependencyRecord
        {
            Kind = x.Kind,
            Identity = x.Identity,
            Guid = x.Guid,
            DependencyHash = x.DependencyHash + "-changed",
            MetaHash = x.MetaHash,
            ImporterSettings = x.ImporterSettings,
        }).ToList();
        string packageJson = System.IO.File.ReadAllText(UnityCharacterAssetCooker.ResolveFile("Assets/CharacterPackages/fightguy", "package.json"));
        string characterJson = System.IO.File.ReadAllText(UnityCharacterAssetCooker.ResolveFile("Assets/CharacterPackages/fightguy", "character.json"));
        string alteredHash = CharacterCookDependencyTracker.ComputeSourceHash(
            packageJson, characterJson, catalog, altered, result.Animations);
        if (alteredHash == result.SourceHash)
            throw new InvalidOperationException("Changing a dependency identity did not invalidate source hash.");
    }

    private static void AssertFailedCookPreservesLastValidOutput(CharacterAssetCatalog catalog)
    {
        string root = UnityCharacterAssetCooker.ProjectRoot();
        string posePath = System.IO.Path.Combine(root, CharacterCookOutput.FightGuy.IntermediateDirectory, "poses.bin");
        string bindingPath = System.IO.Path.Combine(root, CharacterCookOutput.FightGuy.IntermediateDirectory, "client.bindings");
        string generatedPath = System.IO.Path.Combine(root, "Assets/Resources/Generated/CharacterPackages/fightguy/FightGuy_AnimationCatalog.asset");
        string statusPath = System.IO.Path.Combine(root, CharacterCookOutput.FightGuy.IntermediateDirectory, "cook-status.json");
        byte[] status = System.IO.File.ReadAllBytes(statusPath);
        string canonicalPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "../../content-cooked/fightguy"));
        byte[] pose = System.IO.File.ReadAllBytes(posePath);
        byte[] binding = System.IO.File.ReadAllBytes(bindingPath);
        byte[] generated = System.IO.File.ReadAllBytes(generatedPath);
        var canonical = Directory.Exists(canonicalPath)
            ? Directory.GetFiles(canonicalPath).ToDictionary(x => System.IO.Path.GetFileName(x), x => System.IO.File.ReadAllBytes(x), StringComparer.Ordinal)
            : null;
        int sampleRate = catalog.SampleRate;
        try
        {
            catalog.SampleRate = 30;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            byte[] statusBeforeFailedCook = System.IO.File.ReadAllBytes(statusPath);
            var failedCook = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook("fightguy");
            if (failedCook.Success)
                throw new InvalidOperationException("Invalid sample rate unexpectedly cooked.");
            if (!System.IO.File.ReadAllBytes(posePath).SequenceEqual(pose) ||
                !System.IO.File.ReadAllBytes(bindingPath).SequenceEqual(binding) ||
                !System.IO.File.ReadAllBytes(generatedPath).SequenceEqual(generated))
                throw new InvalidOperationException("Failed recook replaced last-valid output.");
            if (canonical != null)
            {
                var after = Directory.GetFiles(canonicalPath).ToDictionary(x => System.IO.Path.GetFileName(x), x => System.IO.File.ReadAllBytes(x), StringComparer.Ordinal);
                if (after.Count != canonical.Count || canonical.Any(x => !after.TryGetValue(x.Key, out var bytes) || !bytes.SequenceEqual(x.Value)))
                    throw new InvalidOperationException("Failed recook replaced last-valid canonical package.");
            }
            if (!System.IO.File.ReadAllBytes(statusPath).SequenceEqual(statusBeforeFailedCook))
                throw new InvalidOperationException("Failed recook changed the last-valid status.");
        }
        finally
        {
            catalog.SampleRate = sampleRate;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }
    }


    private static CharacterAssetCookResult Cook(CharacterAssetCatalog catalog, Action<CharacterAssetCatalog> mutate = null)
    {
        var copy = Clone(catalog);
        try
        {
            mutate?.Invoke(copy);
            return UnityCharacterAssetCooker.Cook("Assets/CharacterPackages/fightguy", copy, CharacterCookOutput.FightGuy, CharacterCookProfile.TrustedBuiltIn);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    private static void ExpectCode(CharacterAssetCatalog source, Action<CharacterAssetCatalog> mutate, string code)
    {
        CharacterAssetCookResult result = Cook(source, mutate);
        if (!result.Diagnostics.Any(x => x.Code == code && x.Severity == CharacterDiagnosticSeverity.Error))
            throw new InvalidOperationException($"Expected diagnostic {code}.");
    }

    private static CharacterAssetCatalog Clone(CharacterAssetCatalog source)
    {
        return UnityEngine.Object.Instantiate(source);
    }

    private static void AssertBytes(byte[] left, byte[] right, string label)
    {
        if (!left.SequenceEqual(right)) throw new InvalidOperationException($"{label} differ.");
    }

    private static void AssertEqual(string left, string right, string label)
    {
        if (!StringComparer.Ordinal.Equals(left, right)) throw new InvalidOperationException($"{label} differ.");
    }

    private static void AssertEqual(int left, int right, string label)
    {
        if (left != right) throw new InvalidOperationException($"{label} differ: {left} vs {right}.");
    }

    private static void AssertEqual(string[] left, string[] right, string label)
    {
        if (!left.SequenceEqual(right, StringComparer.Ordinal)) throw new InvalidOperationException($"{label} differ.");
    }
}
