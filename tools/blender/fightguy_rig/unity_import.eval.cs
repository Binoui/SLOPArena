// Eval body: import a build_rig.py / paint_texture.py result over FightGuy's model and rebuild the Unity side.
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// (absolute path). Edit Build first. The FBX is replaced in place, so Resources/Characters/FightGuy.prefab and
// the catalog's _rig reference keep their GUID/fileIDs. The Humanoid mapping and twist settings carry over from
// the current import; the avatar skeleton is regenerated from the new T-pose.
// Importing the model reimports a catalog dependency, which persists a cook of every package (see the docs).
const string Build = "/home/binoui/Documents/projects/SlopArena/artifacts/fightguy-v2-20261016/build";
const string Fbx = "Assets/Art/Characters/fightguy/Models/fightguy.fbx";
const string Tex = "Assets/Art/Characters/fightguy/Textures/FightGuy2_BaseColor.png";
const string Mat = "Assets/Art/Characters/fightguy/Materials/FightGuy2_Body.mat";
const string FbxMaterialName = "FightGuy2_Body";
const string PrefabPath = "Assets/Resources/Characters/FightGuy.prefab";
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);
foreach (var source in new[] { "FightGuy2.fbx", "textures/FightGuy2_BaseColor.png" })
    if (!System.IO.File.Exists(System.IO.Path.Combine(Build, source)))
        throw new System.IO.FileNotFoundException("Missing prepared FightGuy input", source);

Dictionary<string, object> Probe(string label)
{
    var probe = new Dictionary<string, object>();
    var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
    go.hideFlags = HideFlags.HideAndDontSave;
    try
    {
        var a = go.GetComponentInChildren<Animator>();
        a.Rebind();
        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
        probe["humanScale"] = a.humanScale;
        probe["avatarValid"] = a.avatar != null && a.avatar.isValid && a.avatar.isHuman;
        probe["material"] = smr.sharedMaterial != null ? smr.sharedMaterial.name : null;
        probe["materialPath"] = smr.sharedMaterial != null ? AssetDatabase.GetAssetPath(smr.sharedMaterial) : null;
        probe["bones"] = smr.bones.Length;
        probe["vertices"] = smr.sharedMesh.vertexCount;
        probe["triangles"] = smr.sharedMesh.triangles.Length / 3;
        var b = smr.bounds;
        probe["boundsSize"] = new[] { b.size.x, b.size.y, b.size.z };
        probe["boundsMinY"] = b.min.y;
        var thumb = a.GetBoneTransform(HumanBodyBones.RightThumbProximal);
        probe["rightThumbProximal"] = thumb != null ? thumb.name : null;
        probe["rightIndexProximal"] = a.GetBoneTransform(HumanBodyBones.RightIndexProximal)?.name;
        probe["hipsY"] = a.GetBoneTransform(HumanBodyBones.Hips).position.y - go.transform.position.y;
        probe["headY"] = a.GetBoneTransform(HumanBodyBones.Head).position.y - go.transform.position.y;
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
    report[label] = probe;
    return probe;
}

var before = Probe("before");
var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
var saved = mi.humanDescription;
report["savedMapping"] = saved.human.Length;

// Texture with the old body texture's import settings.
System.IO.File.Copy(System.IO.Path.Combine(Build, "textures/FightGuy2_BaseColor.png"), System.IO.Path.Combine(root, Tex), true);
AssetDatabase.ImportAsset(Tex, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var ti = (TextureImporter)AssetImporter.GetAtPath(Tex);
ti.textureType = TextureImporterType.Default;
ti.sRGBTexture = true;
ti.mipmapEnabled = true;
ti.maxTextureSize = 2048;
ti.textureCompression = TextureImporterCompression.Compressed;
ti.filterMode = FilterMode.Bilinear;
ti.SaveAndReimport();

// Material: same shader and settings as the material the current model renders with, new base map.
var oldMat = AssetDatabase.LoadAssetAtPath<Material>((string)before["materialPath"]);
var mat = AssetDatabase.LoadAssetAtPath<Material>(Mat);
if (mat == null)
{
    mat = new Material(oldMat) { name = FbxMaterialName };
    AssetDatabase.CreateAsset(mat, Mat);
}
var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex);
if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
// The v2 texture is far lighter than the old one; 0.45 (maintainer's pick) keeps him near the roster's
// brightness. MatchVisualStyle carries _BaseColor into the match shader.
var tint = new Color(0.45f, 0.45f, 0.45f, 1f);
if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
EditorUtility.SetDirty(mat);
AssetDatabase.SaveAssetIfDirty(mat);
report["materialFrom"] = oldMat != null ? AssetDatabase.GetAssetPath(oldMat) : null;

// Model: replace in place, remap its material, regenerate the avatar skeleton, re-apply the saved mapping.
System.IO.File.Copy(System.IO.Path.Combine(Build, "FightGuy2.fbx"), System.IO.Path.Combine(root, Fbx), true);
AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), FbxMaterialName), mat);
mi.humanDescription = new HumanDescription();
mi.autoGenerateAvatarMappingIfUnspecified = true;
mi.SaveAndReimport();
mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
var hd = mi.humanDescription;
var skeletonNames = new HashSet<string>(hd.skeleton.Select(s => s.name));
var missing = saved.human.Where(h => !skeletonNames.Contains(h.boneName)).Select(h => h.boneName).ToArray();
hd.human = saved.human.Where(h => skeletonNames.Contains(h.boneName)).ToArray();
hd.upperArmTwist = saved.upperArmTwist; hd.lowerArmTwist = saved.lowerArmTwist;
hd.upperLegTwist = saved.upperLegTwist; hd.lowerLegTwist = saved.lowerLegTwist;
hd.armStretch = saved.armStretch; hd.legStretch = saved.legStretch; hd.feetSpacing = saved.feetSpacing;
hd.hasTranslationDoF = saved.hasTranslationDoF;
mi.humanDescription = hd;
mi.SaveAndReimport();
report["mapped"] = hd.human.Length;
report["missingFromNewSkeleton"] = missing;
report["skeletonBones"] = hd.skeleton.Length;

var after = Probe("after");
if (missing.Length != 0 || !(bool)after["avatarValid"])
    throw new InvalidOperationException("The replacement FightGuy Avatar is invalid or is missing mapped bones.");
// Humanoid derives humanScale from the whole skeleton: equal hip heights do not
// imply equal retargeting scales when body proportions change.
report["humanScaleChangePercent"] = ((float)after["humanScale"] / (float)before["humanScale"] - 1f) * 100f;
if (Mathf.Abs((float)after["hipsY"] - (float)before["hipsY"]) > 0.02f)
    throw new InvalidOperationException("FightGuy rest-pose hip height changed unexpectedly; review the prepared model scale.");
return report;
