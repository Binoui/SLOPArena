// Eval body: import the Sir Bonk v2 build (tmp/bonk-sir/tripo-v2/scripts/build_rig.py, 2026-10-28) as a candidate
// beside the live Sir Bonk. The live rig (CharacterPackages/bonk/Presentation/SirBonkRig.prefab, the catalog _rig),
// its model and the catalog are untouched, so no cook runs.
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// (absolute path). Writes sir_bonk_v2.fbx, SirBonkV2_BaseColor.png, SirBonkV2_Body.mat,
// Art/Characters/bonk/Prefabs/SirBonkV2Rig.prefab (three cape BoneChainSprings on Spine2) and
// Art/Characters/bonk/SirBonkV2WeaponAttachConfig.asset (the live weapon placements in the v2 bone frames).
// Import settings, material and Humanoid mapping are the live Sir Bonk's (same mixamorig names, UpperChest = Spine2);
// the avatar skeleton is generated from this model's T-pose. The v2 body still wears its left pot lid: Q's lid prop
// split (lid_split.py) waits for the swap.
const string Build = "/home/binoui/Documents/projects/SlopArena/tmp/bonk-sir/tripo-v2/build-v1-lid";  // lid_split.py output: body without the left pot lid
const string Fbx = "Assets/Art/Characters/bonk/Models/sir_bonk_v2.fbx";
const string Tex = "Assets/Art/Characters/bonk/Textures/SirBonkV2_BaseColor.png";
const string Mat = "Assets/Art/Characters/bonk/Materials/SirBonkV2_Body.mat";
const string FbxMaterialName = "SirBonk_Body";
const string OldFbx = "Assets/Art/Characters/bonk/Models/sir_bonk.fbx";
const string LivePrefab = "Assets/CharacterPackages/bonk/Presentation/SirBonkRig.prefab";
const string PrefabPath = "Assets/Art/Characters/bonk/Prefabs/SirBonkV2Rig.prefab";
const string LiveWeaponConfig = "Assets/CharacterPackages/bonk/BonkWeaponAttachConfig.asset";
const string CandidateWeaponConfig = "Assets/Art/Characters/bonk/SirBonkV2WeaponAttachConfig.asset";
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);
foreach (var source in new[] { "SirBonk.fbx", "textures/SirBonk_BaseColor.png" })
    if (!System.IO.File.Exists(System.IO.Path.Combine(Build, source)))
        throw new System.IO.FileNotFoundException("Missing build_rig.py output", source);

Dictionary<string, object> Probe(string path)
{
    var probe = new Dictionary<string, object>();
    var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
    go.hideFlags = HideFlags.HideAndDontSave;
    try
    {
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        var a = go.GetComponentInChildren<Animator>();
        a.Rebind();
        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
        probe["humanScale"] = a.humanScale;
        probe["avatarValid"] = a.avatar != null && a.avatar.isValid && a.avatar.isHuman;
        probe["material"] = smr.sharedMaterial != null ? AssetDatabase.GetAssetPath(smr.sharedMaterial) : null;
        probe["bones"] = smr.bones.Length;
        probe["vertices"] = smr.sharedMesh.vertexCount;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in baked.vertices) { float y = smr.transform.localToWorldMatrix.MultiplyPoint3x4(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
        UnityEngine.Object.DestroyImmediate(baked);
        probe["restMinY"] = lo;
        probe["restMaxY"] = hi;
        probe["hipsY"] = a.GetBoneTransform(HumanBodyBones.Hips).position.y;
        probe["headY"] = a.GetBoneTransform(HumanBodyBones.Head).position.y;
        var hand = go.GetComponentsInChildren<Transform>(true).First(t => t.name == "mixamorig:RightHand");
        probe["rightHandRotation"] = new[] { hand.rotation.x, hand.rotation.y, hand.rotation.z, hand.rotation.w };
        probe["rightHandLossyScale"] = hand.lossyScale.x;
        probe["springs"] = go.GetComponentsInChildren<SlopArena.Client.Entities.BoneChainSpring>(true).Length;
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
    return probe;
}

var live = Probe(LivePrefab);
report["live"] = live;
var oldImporter = (ModelImporter)AssetImporter.GetAtPath(OldFbx);
var saved = oldImporter.humanDescription;

// Colour map with Bonk's body texture settings.
System.IO.File.Copy(System.IO.Path.Combine(Build, "textures/SirBonk_BaseColor.png"), System.IO.Path.Combine(root, Tex), true);
AssetDatabase.ImportAsset(Tex, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var ti = (TextureImporter)AssetImporter.GetAtPath(Tex);
ti.textureType = TextureImporterType.Default;
ti.sRGBTexture = true;
ti.mipmapEnabled = true;
ti.maxTextureSize = 2048;
ti.textureCompression = TextureImporterCompression.Compressed;
ti.filterMode = FilterMode.Bilinear;
ti.SaveAndReimport();

// Material: the live Bonk's material (shader, tint, settings) with the new base map.
var oldMat = AssetDatabase.LoadAssetAtPath<Material>((string)live["material"]);
var mat = AssetDatabase.LoadAssetAtPath<Material>(Mat);
if (mat == null)
{
    mat = new Material(oldMat) { name = FbxMaterialName };
    AssetDatabase.CreateAsset(mat, Mat);
}
var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex);
if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
EditorUtility.SetDirty(mat);
AssetDatabase.SaveAssetIfDirty(mat);
report["materialFrom"] = live["material"];

// Model: import settings like the live Bonk, Humanoid avatar from this T-pose, the live mapping + UpperChest.
System.IO.File.Copy(System.IO.Path.Combine(Build, "SirBonk.fbx"), System.IO.Path.Combine(root, Fbx), true);
AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
mi.globalScale = oldImporter.globalScale;
mi.useFileScale = oldImporter.useFileScale;
mi.importBlendShapes = false;
mi.importCameras = false;
mi.importLights = false;
mi.importAnimation = false;
mi.optimizeGameObjects = false;
mi.isReadable = oldImporter.isReadable;
mi.importNormals = ModelImporterNormals.Import;
mi.materialImportMode = oldImporter.materialImportMode;
mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), FbxMaterialName), mat);
mi.animationType = ModelImporterAnimationType.Human;
mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
mi.humanDescription = new HumanDescription();
mi.autoGenerateAvatarMappingIfUnspecified = true;
mi.SaveAndReimport();
mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
var hd = mi.humanDescription;
var skeletonNames = new HashSet<string>(hd.skeleton.Select(s => s.name));
var human = saved.human.Where(h => skeletonNames.Contains(h.boneName)).ToList();
// The v2 Mixamo rig has index fingers only; the live rig's other finger bones are optional in Humanoid.
var fingerNames = new[] { "Thumb", "Index", "Middle", "Ring", "Little" };
var missingAll = saved.human.Where(h => !skeletonNames.Contains(h.boneName)).ToArray();
var missing = missingAll.Where(h => !fingerNames.Any(f => h.humanName.Contains(f))).Select(h => h.boneName).ToArray();
report["missingFingers"] = missingAll.Where(h => fingerNames.Any(f => h.humanName.Contains(f))).Select(h => h.humanName).ToArray();
if (!human.Any(h => h.humanName == "UpperChest") && skeletonNames.Contains("mixamorig:Spine2"))
    human.Add(new HumanBone { boneName = "mixamorig:Spine2", humanName = "UpperChest", limit = new HumanLimit { useDefaultValues = true } });
hd.human = human.ToArray();
hd.upperArmTwist = saved.upperArmTwist; hd.lowerArmTwist = saved.lowerArmTwist;
hd.upperLegTwist = saved.upperLegTwist; hd.lowerLegTwist = saved.lowerLegTwist;
hd.armStretch = saved.armStretch; hd.legStretch = saved.legStretch; hd.feetSpacing = saved.feetSpacing;
hd.hasTranslationDoF = saved.hasTranslationDoF;
mi.humanDescription = hd;
mi.SaveAndReimport();
report["mapped"] = hd.human.Length;
report["missingFromNewSkeleton"] = missing;
report["skeletonBones"] = hd.skeleton.Length;

// Candidate prefab: a variant of the model, root like the live prefab, cape springs. Built in a preview scene
// so the open scene is not dirtied.
var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try
{
    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
    inst.name = "SirBonkV2Rig";
    inst.transform.localPosition = new Vector3(0f, -1f, 0f);
    inst.transform.localScale = Vector3.one * 3f;
    var all = inst.GetComponentsInChildren<Transform>(true);
    var spine2 = all.First(t => t.name == "mixamorig:Spine2");
    int chains = 0;
    foreach (var column in new[] { "L", "C", "R" })
    {
        var chain = Enumerable.Range(1, 4).Select(i => all.FirstOrDefault(t => t.name == "Cape_" + column + i)).ToArray();
        if (chain.Any(t => t == null)) continue;
        var spring = spine2.gameObject.AddComponent<SlopArena.Client.Entities.BoneChainSpring>();
        spring.Configure(chain);
        chains++;
    }
    report["capeChains"] = chains;
    PrefabUtility.SaveAsPrefabAsset(inst, PrefabPath);
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }

var candidate = Probe(PrefabPath);
report["candidate"] = candidate;
// The gateway serializes plain values only, so rotations travel as float[4].
System.Func<object, Quaternion> quat = o => { var f = (float[])o; return new Quaternion(f[0], f[1], f[2], f[3]); };
report["rightHandFrameDegrees"] = Quaternion.Angle(quat(live["rightHandRotation"]), quat(candidate["rightHandRotation"]));
report["humanScaleChangePercent"] = ((float)candidate["humanScale"] / (float)live["humanScale"] - 1f) * 100f;
report["missingFromNewSkeleton"] = missing;
if (missing.Length != 0 || !(bool)candidate["avatarValid"])
    throw new InvalidOperationException("The Sir Bonk v2 Avatar is invalid or is missing mapped bones.");

// Candidate weapon config: the live entries with offsets converted to the candidate's bone frames, so each weapon
// keeps the live rig's T-pose placement in world space (both rigs' bones have unit scale). Kept outside the package
// folder so the live rig pack's config scan never finds it; the genra candidate pack names it explicitly.
Quaternion RestRotation(string prefabPath, string boneName)
{
    var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
    go.hideFlags = HideFlags.HideAndDontSave;
    try
    {
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        return go.GetComponentsInChildren<Transform>(true).First(t => t.name == boneName).rotation;
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
}
if (AssetDatabase.LoadMainAssetAtPath(CandidateWeaponConfig) == null)
    AssetDatabase.CopyAsset(LiveWeaponConfig, CandidateWeaponConfig);
var liveConfig = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(LiveWeaponConfig));
var candidateConfig = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(CandidateWeaponConfig));
candidateConfig.CopyFromSerializedProperty(liveConfig.FindProperty("Entries"));
var entries = candidateConfig.FindProperty("Entries");
var converted = new List<object>();
for (int i = 0; i < entries.arraySize; i++)
{
    var e = entries.GetArrayElementAtIndex(i);
    string bone = e.FindPropertyRelative("BoneName").stringValue;
    var turn = Quaternion.Inverse(RestRotation(PrefabPath, bone)) * RestRotation(LivePrefab, bone);
    foreach (var (pos, rot) in new[] { ("PositionOffset", "RotationOffset"), ("FirePositionOffset", "FireRotationOffset") })
    {
        var p = e.FindPropertyRelative(pos);
        var r = e.FindPropertyRelative(rot);
        p.vector3Value = turn * p.vector3Value;
        r.vector3Value = (turn * Quaternion.Euler(r.vector3Value)).eulerAngles;
    }
    var po = e.FindPropertyRelative("PositionOffset").vector3Value;
    var ro = e.FindPropertyRelative("RotationOffset").vector3Value;
    converted.Add(new Dictionary<string, object> { ["bone"] = bone, ["position"] = new[] { po.x, po.y, po.z }, ["rotation"] = new[] { ro.x, ro.y, ro.z } });
}
candidateConfig.ApplyModifiedPropertiesWithoutUndo();
AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(CandidateWeaponConfig));
report["weaponConfig"] = CandidateWeaponConfig;
report["weaponEntries"] = converted;
return report;
