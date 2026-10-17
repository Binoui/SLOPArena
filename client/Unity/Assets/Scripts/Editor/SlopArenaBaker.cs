using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System;
using System.Text;
using System.Linq;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;

/// <summary>
/// Bakes skeleton bone positions per animation frame into a .bin file.
/// Replaces the Godot headless_bake.gd script.
///
/// Usage: Tools -> SLOPArena -> Legacy -> Bake Skeleton...
/// Select a character prefab (FBX model with Animator), this will sample all animations
/// at each frame and write bone positions to a .bin file.
///
/// Output format (matches BakedAnimationData.LoadFromBin):
///   SKEL magic (4 bytes)
///   uint version = 1
///   uint boneCount
///   uint animCount
///   [boneNames]: uint nameLen + UTF-8 name
///   [anims]: uint nameLen + UTF-8 name + uint frameCount + float x/y/z per bone per frame
///
/// Positions are Hips-relative (subtract Hips, without inverse-Hips rotation)
/// so they stay attached to the entity regardless of animation root motion. HitboxGeometry
/// applies scale, facing yaw, and Y remapping when consuming this data.
/// </summary>
public class SlopArenaBaker : EditorWindow
{
    private GameObject _model;
    private CharacterAnimationConfig _animConfig;
    private WeaponAttachConfig _weaponConfig;
    private string _outputPath = "data/";
    private float _sampleRate = 60f;

    [MenuItem("Tools/SLOPArena/Legacy/Bake Skeleton...")]
    public static void ShowWindow()
    {
        GetWindow<SlopArenaBaker>("Bake Skeleton");
    }

    private void OnGUI()
    {
        _model = (GameObject)EditorGUILayout.ObjectField("Character Prefab", _model, typeof(GameObject), false);
        _animConfig = (CharacterAnimationConfig)EditorGUILayout.ObjectField("Anim Config", _animConfig, typeof(CharacterAnimationConfig), false);
        _weaponConfig = (WeaponAttachConfig)EditorGUILayout.ObjectField("Weapon Config", _weaponConfig, typeof(WeaponAttachConfig), false);
        _outputPath = EditorGUILayout.TextField("Output Path", _outputPath);
        _sampleRate = EditorGUILayout.FloatField("Sample Rate (fps)", _sampleRate);

        if (GUILayout.Button("Bake") && _model != null && _animConfig != null)
            BakeSkeleton(_model, _animConfig, _sampleRate, _outputPath, _weaponConfig);
        else if (GUILayout.Button("Bake") && (_model == null || _animConfig == null))
        {
            if (_model == null) Debug.LogError("Select a Character Prefab first");
            if (_animConfig == null) Debug.LogError("Select an Anim Config first");
        }
    }

    private void BakeSkeleton(GameObject model, CharacterAnimationConfig animConfig, float sampleRate = 60f, string outputPath = "data/", WeaponAttachConfig? weaponConfig = null)
    {
        if ((model != null && model.name.IndexOf("fightguy", StringComparison.OrdinalIgnoreCase) >= 0) ||
            (animConfig != null && animConfig.name.IndexOf("fightguy", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            Debug.LogError("[SlopArenaBaker] FightGuy is owned by CharacterAssetCatalog; use Tools/SLOPArena/Cook FightGuy.");
            return;
        }

        var animator = model.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("Model has no Animator component");
            return;
        }

        var avatar = animator.avatar;
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError("Model needs a valid Humanoid Avatar");
            return;
        }

        // Build clip list by enumerating all clips in the CharacterAnimationConfig
        var clips = new List<(string name, AnimationClip clip)>();

        void AddClip(string name, AnimationClip? clip)
        {
            if (clip != null) clips.Add((name, clip));
        }

        // Standard clips (named fields on the config)
        AddClip("idle", animConfig.Idle);
        AddClip("run", animConfig.Run);
        AddClip("jump_up", animConfig.JumpUp);
        AddClip("jump_down", animConfig.JumpDown);
        AddClip("jump", animConfig.JumpUp ?? animConfig.JumpDown);
        AddClip("fall", animConfig.Fall);
        AddClip("dash", animConfig.Dash);
        AddClip("hit_small", animConfig.HitSmall);
        AddClip("hit_medium", animConfig.HitMedium);
        AddClip("hit_hard", animConfig.HitHard);
        AddClip("death", animConfig.Death);
        AddClip("tumble", animConfig.Tumble);

        // Ability clips (character-specific, from the AbilityClips list)
        foreach (var entry in animConfig.AbilityClips)
        {
            if (!string.IsNullOrEmpty(entry.Name) && entry.Clip != null)
                clips.Add((entry.Name, entry.Clip));
        }

        if (clips.Count == 0)
        {
            Debug.LogError("No animation clips found in the Anim Config");
            return;
        }
        Debug.Log($"Found {clips.Count} animation clips from config");
        if (weaponConfig == null)
        {
            var sampled = clips.Select(x => new DeterministicPoseTrackBaker.SampledAnimation
            {
                SemanticId = x.name,
                PoseTrackId = x.name,
                Clip = x.clip,
                FrameCount = Mathf.CeilToInt(x.clip.length * 60f),
            }).ToArray();
            try
            {
                // Legacy prefabs carry their size on the root, so bake at that scale.
                byte[] bytes = DeterministicPoseTrackBaker.Bake(model, sampled, 60,
                    visualScale: model.transform.localScale.x);
                string deterministicOutputFile = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "..", "..", "..",
                    outputPath.TrimEnd('/'), model.name.ToLowerInvariant() + "_skeleton.bin"));
                Directory.CreateDirectory(Path.GetDirectoryName(deterministicOutputFile)!);
                File.WriteAllBytes(deterministicOutputFile, bytes);
                MirrorToStreamingAssets(deterministicOutputFile, "data");
                MirrorToStreamingAssets(deterministicOutputFile, Path.Combine("Server", "data"));
                AssetDatabase.Refresh();
                Debug.Log($"Baked {bytes.Length} bytes with deterministic baker -> {deterministicOutputFile}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SlopArenaBaker] Deterministic bake failed: {ex.Message}");
            }
            return;
        }


        // Bake order (hurtbox and hitbox bones resolve by name, not by index):
        //   0=Head, 1=Spine2(UpperChest), 2=Hips, 3=RightHand, 4=LeftHand,
        //   5=RightFoot, 6=LeftFoot, 7=RightToes, 8=LeftToes
        var humanBones = new[]
        {
            HumanBodyBones.Head,         // 0
            HumanBodyBones.UpperChest,   // 1 "mixamorig:Spine2" in Mixamo
            HumanBodyBones.Hips,         // 2
            HumanBodyBones.RightHand,    // 3
            HumanBodyBones.LeftHand,     // 4
            HumanBodyBones.RightFoot,    // 5
            HumanBodyBones.LeftFoot,     // 6
            HumanBodyBones.RightToes,    // 7
            HumanBodyBones.LeftToes,     // 8
        };

        var boneNames = new List<string>();
        var transforms = new List<Transform>();
        foreach (var bone in humanBones)
        {
            var t = animator.GetBoneTransform(bone);
            if (t != null)
            {
                boneNames.Add(t.name);
                transforms.Add(t);
            }
        }
        Debug.Log($"Found {transforms.Count} bones from humanoid avatar");

        // ── Blade source (weapon characters only) ──
        // Resolve the sword tip + hilt ONCE before the clip loop, EXACTLY from the weapon
        // prefab's mesh vertices (prefab-local space; the prefab root = the hand grip):
        // bladeAxis = the longest local-AABB span (the sword is straight), _weapon_tip =
        // the vertex with max projection on that axis, _weapon_hilt = the vertex with min
        // projection (the pommel). Both are hand-relative offsets transformed per frame by
        // the same WeaponEntry pose used by WeaponAttach.Update, so server hit geometry and
        // the rendered blade share one animation-specific attachment track.
        WeaponEntry? weaponEntry = null;
        Vector3 tipLocal = new Vector3(0f, 0f, 1.5f);
        Vector3 hiltLocal = Vector3.zero;
        bool bladeWarned = false;
        if (weaponConfig != null && weaponConfig.Entries != null && weaponConfig.Entries.Length > 0)
        {
            weaponEntry = Array.Find(weaponConfig.Entries, e => e.BoneName == "mixamorig:RightHand");
            if (weaponEntry == null) weaponEntry = weaponConfig.Entries[0];

            bool derived = false;
            if (weaponEntry.Prefab != null)
            {
                var verts = new List<Vector3>();
                foreach (var mf in weaponEntry.Prefab.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    Matrix4x4 toPrefab = weaponEntry.Prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    var v = mesh.vertices;
                    for (int i = 0; i < v.Length; i++)
                        verts.Add(toPrefab.MultiplyPoint3x4(v[i]));
                }
                if (verts.Count > 0)
                {
                    Vector3 min = verts[0], max = verts[0];
                    foreach (var p in verts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                    Vector3 extent = max - min;
                    float span = Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z));
                    if (span > 0.01f)
                    {
                        Vector3 axis = extent.x >= extent.y && extent.x >= extent.z ? Vector3.right
                            : extent.y >= extent.z ? Vector3.up : Vector3.forward;
                        float tipProj = float.MinValue, hiltProj = float.MaxValue;
                        foreach (var p in verts)
                        {
                            float pr = Vector3.Dot(p, axis);
                            if (pr > tipProj) { tipProj = pr; tipLocal = p; }
                            if (pr < hiltProj) { hiltProj = pr; hiltLocal = p; }
                        }
                        derived = true;
                    }
                }
            }
            if (!derived)
            {
                tipLocal = new Vector3(0f, 0f, 1.5f);
                hiltLocal = Vector3.zero;
                if (!bladeWarned)
                {
                    bladeWarned = true;
                    Debug.LogWarning($"[SlopArenaBaker] Could not derive blade from weapon prefab mesh vertices " +
                        $"({weaponEntry.Prefab?.name ?? "null"}) — falling back to tip (0,0,1.5) / hilt (0,0,0).");
                }
            }
            // Synthetic points appended AFTER the real bones (order: tip, hilt).
            boneNames.Add("_weapon_tip");
            boneNames.Add("_weapon_hilt");
        }

        // Create a copy to sample without affecting the original
        var tempGO = Instantiate(model);
        var tempAnimator = tempGO.GetComponent<Animator>();
        var tempHips = tempAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (tempHips == null)
        {
            Debug.LogError("Model has no Hips bone cannot compute bone positions");
            DestroyImmediate(tempGO);
            return;
        }

        string outputFile = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "..", "..",
            outputPath.TrimEnd('/'), model.name.ToLowerInvariant() + "_skeleton.bin"));
        string outputDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        void WriteVec(Stream s, Vector3 v)
        {
            s.Write(BitConverter.GetBytes(v.x), 0, 4);
            s.Write(BitConverter.GetBytes(v.y), 0, 4);
            s.Write(BitConverter.GetBytes(v.z), 0, 4);
        }

        using (var stream = new FileStream(outputFile, FileMode.Create))
        {
            // Header: magic + version
            stream.Write(Encoding.ASCII.GetBytes("SKEL"), 0, 4);
            stream.Write(BitConverter.GetBytes(1u), 0, 4);

            // Bone count + animation count (order MUST match BakedAnimationData.LoadFromBin)
            // Indices 0..transforms.Count-1 stay the real bones; the weapon synthetic points
            // (_weapon_tip, _weapon_hilt — when present) are appended last. LoadFromBin is
            // length-driven, so no version bump.
            stream.Write(BitConverter.GetBytes((uint)(transforms.Count + (weaponEntry != null ? 2 : 0))), 0, 4);
            stream.Write(BitConverter.GetBytes((uint)clips.Count), 0, 4);

            // Bone names (length-prefixed)
            foreach (var name in boneNames)
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(name);
                stream.Write(BitConverter.GetBytes((uint)nameBytes.Length), 0, 4);
                stream.Write(nameBytes, 0, nameBytes.Length);
            }

            // Sample each clip
            var tempBoneTransforms = new Transform[transforms.Count];
            for (int b = 0; b < transforms.Count; b++)
            {
                if (b < humanBones.Length)
                    tempBoneTransforms[b] = tempAnimator.GetBoneTransform(humanBones[b]);
                if (tempBoneTransforms[b] == null)
                    tempBoneTransforms[b] = tempGO.transform;
            }



            // Sample each clip
            foreach (var (logicalName, clip) in clips)
            {
                byte[] clipNameBytes = Encoding.UTF8.GetBytes(logicalName);
                stream.Write(BitConverter.GetBytes((uint)clipNameBytes.Length), 0, 4);
                stream.Write(clipNameBytes, 0, clipNameBytes.Length);

                float frameTime = 1f / sampleRate;
                int frameCount = Mathf.CeilToInt(clip.length * sampleRate);
                stream.Write(BitConverter.GetBytes((uint)frameCount), 0, 4);

                for (int frame = 0; frame < frameCount; frame++)
                {
                    float time = frame * frameTime;
                    clip.SampleAnimation(tempGO, time);
                    Vector3 hipsPos = tempHips.position;

                    for (int b = 0; b < transforms.Count; b++)
                    {
                        Transform tempT = tempBoneTransforms[b];
                        Vector3 worldPos = tempT.position;
                        Vector3 localPos = worldPos - hipsPos;
                        stream.Write(BitConverter.GetBytes(localPos.x), 0, 4);
                        stream.Write(BitConverter.GetBytes(localPos.y), 0, 4);
                        stream.Write(BitConverter.GetBytes(localPos.z), 0, 4);
                    }

                    // Weapon tip + hilt: same Hips-relative space as the real bones, using
                    // the same bind-space attachment transform as WeaponAttach.Update.
                    if (weaponEntry != null)
                    {
                        Transform rightHand = tempAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                        if (rightHand != null)
                        {
                            Vector3 bladePosition = rightHand.TransformPoint(weaponEntry.PositionOffset);
                            Quaternion bladeRotation = rightHand.rotation
                                * Quaternion.Euler(weaponEntry.RotationOffset);
                            WriteVec(stream, bladePosition + bladeRotation * tipLocal - hipsPos);
                            WriteVec(stream, bladePosition + bladeRotation * hiltLocal - hipsPos);
                        }
                        else
                        {
                            WriteVec(stream, Vector3.zero);
                            WriteVec(stream, Vector3.zero);
                            Debug.LogWarning("[SlopArenaBaker] No RightHand transform — baking degenerate weapon points (0,0,0).");
                        }
                    }
                }
            }

            DestroyImmediate(tempGO);
        }

        // Mirror into the Unity project's StreamingAssets: the runtime resolves
        // StreamingAssets/data BEFORE the repo-root data/ fallback
        // (BakedContentPaths.FirstExisting), so without this the Editor and any
        // direct Unity build keep reading a stale bin until build-release.sh
        // re-stages it. build-release.sh copies from the repo-root file, so the
        // repo-root write above stays the committed source of truth.
        if (Path.GetExtension(outputFile).Equals(".bin", StringComparison.OrdinalIgnoreCase))
        {
            MirrorToStreamingAssets(outputFile, "data");
            MirrorToStreamingAssets(outputFile, Path.Combine("Server", "data"));
        }

        AssetDatabase.Refresh();
        Debug.Log($"Baked {transforms.Count + (weaponEntry != null ? 2 : 0)} bones x {clips.Count} clips -> {outputFile} ({new FileInfo(outputFile).Length} bytes)");
    }

    private void MirrorToStreamingAssets(string sourceFile, string subDir)
    {
        try
        {
            string destDir = Path.Combine(Application.dataPath, "StreamingAssets", subDir);
            Directory.CreateDirectory(destDir);
            string destFile = Path.Combine(destDir, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, destFile, overwrite: true);
            Debug.Log($"[SlopArenaBaker] Mirrored {Path.GetFileName(sourceFile)} -> {destFile}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SlopArenaBaker] Failed to mirror {Path.GetFileName(sourceFile)} to StreamingAssets/{subDir}: {ex.Message}");
        }
    }

    [MenuItem("Tools/SLOPArena/Legacy/Bake All Skeletons")]
    public static void BakeAllCharacters()
    {
        float sampleRate = 60f;

        // Collect configs from both Art/Characters and Resources/AnimationConfigs
        var configGuids = new List<string>(
            AssetDatabase.FindAssets("t:CharacterAnimationConfig", new[] { "Assets/Art/Characters" }));
        configGuids.AddRange(
            AssetDatabase.FindAssets("t:CharacterAnimationConfig", new[] { "Assets/Resources/AnimationConfigs" }));

        int baked = 0;
        var bakedNames = new HashSet<string>();
        foreach (var guid in configGuids)
        {
            string configPath = AssetDatabase.GUIDToAssetPath(guid);
            var config = AssetDatabase.LoadAssetAtPath<CharacterAnimationConfig>(configPath);
            if (config == null) continue;
            if (config.name.IndexOf("fightguy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log("[BakeAll] Skipping FightGuy; package catalog owns its assets.");
                continue;
            }


            string configDir = Path.GetDirectoryName(configPath);
            string charDir = Path.GetDirectoryName(configDir);

            // Derive character name from config directory structure
            string charName = Path.GetFileName(charDir);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{charDir}/Models/{charName}.fbx");
            if (model == null)
            {
                // Try config dir as char root: config was at {name}/.../{name}_AnimConfig.asset
                charName = Path.GetFileName(configDir);
                model = AssetDatabase.LoadAssetAtPath<GameObject>($"{configDir}/Models/{charName}.fbx");
            }

            // Fallback: derive charName from config asset filename.
            if (model == null && config.name.EndsWith("_AnimConfig"))
            {
                charName = config.name.Replace("_AnimConfig", "").ToLowerInvariant();
                model = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/Characters/{charName}/Models/{charName}.fbx");
            }

            if (model == null)
            {
                Debug.LogWarning($"[BakeAll] No model FBX found for config {configPath}");
                continue;
            }
            if (!bakedNames.Add(charName))
            {
                Debug.Log($"[BakeAll] Skipping {charName} (already baked)");
                continue;
            }

            Debug.Log($"[BakeAll] Baking {charName}...");
            var baker = CreateInstance<SlopArenaBaker>();
            // Optional weapon: characters with a WeaponConfigs/<name> asset get the
            // synthetic _weapon_tip point baked (fist/staff characters have none).
            var weapon = Resources.Load<WeaponAttachConfig>($"WeaponConfigs/{charName}");
            try
            {
                // BakeSkeleton is an instance method, but this is a temporary worker,
                // not a user-facing editor window. Destroy it after each character so
                // orphaned windows cannot survive into play-mode layout finalization.
                baker.BakeSkeleton(model, config, sampleRate, weaponConfig: weapon);
            }
            finally
            {
                DestroyImmediate(baker);
            }
            baked++;
        }
        Debug.Log($"[BakeAll] Done — baked {baked} character(s)");
        AssetDatabase.Refresh();
    }
}
