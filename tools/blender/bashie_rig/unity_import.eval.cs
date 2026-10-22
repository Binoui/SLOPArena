// Eval body: import build_rig.py's Bashie into her package. The model, textures and materials go under
// Assets/Art/Characters/bashie/ (Models/bashie.fbx, Textures/<piece>.png, Materials/<piece>.mat) and the rig prefab
// into Assets/CharacterPackages/bashie/Presentation/BashieRig.prefab, which her catalog binds as its rig.
// Import settings, material and Humanoid mapping follow Manki's rig (same mixamorig names); the avatar skeleton is
// generated from Bashie's T-pose. Her shell, whistle and cord are rigidly skinned meshes on bones of their own
// (Shell, Whistle), each with its own material made from Manki's.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/bashie_rig/unity_import.eval.cs"
const string Build = "/home/binoui/Documents/projects/SlopArena/tmp/bashie-model/rig-build-v4";
const string Dir = "Assets/Art/Characters/bashie";
const string Fbx = Dir + "/Models/bashie.fbx";
// Her package owns the rig prefab, like MankiRig.prefab; the model, textures and materials stay under Art.
const string PrefabPath = "Assets/CharacterPackages/bashie/Presentation/BashieRig.prefab";
const string MankiRig = "Assets/CharacterPackages/manki/Presentation/MankiRig.prefab";
// Piece (object, material and texture name) -> largest texture size in game.
var pieces = new Dictionary<string, int> { ["Bashie_Body"] = 2048, ["Bashie_Shell"] = 2048, ["Bashie_Whistle"] = 512, ["Bashie_Cord"] = 32 };
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);
foreach (var source in new[] { "Bashie.fbx" }.Concat(pieces.Keys.Select(p => "textures/" + p + ".png")))
    if (!System.IO.File.Exists(System.IO.Path.Combine(Build, source)))
        throw new System.IO.FileNotFoundException("Missing build_rig.py output", source);
foreach (var folder in new[] { Dir, Dir + "/Models", Dir + "/Textures", Dir + "/Materials", "Assets/CharacterPackages/bashie/Presentation" })
    if (!AssetDatabase.IsValidFolder(folder))
        AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));

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
        // Bashie's shell, whistle and cord are skinned too: measure the body.
        var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(s => s.name.EndsWith("_Body"))
            ?? go.GetComponentInChildren<SkinnedMeshRenderer>();
        probe["humanScale"] = a.humanScale;
        probe["avatarValid"] = a.avatar != null && a.avatar.isValid && a.avatar.isHuman;
        probe["material"] = smr.sharedMaterial != null ? AssetDatabase.GetAssetPath(smr.sharedMaterial) : null;
        probe["mesh"] = AssetDatabase.GetAssetPath(smr.sharedMesh);
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
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
    return probe;
}

var manki = Probe(MankiRig);
report["manki"] = manki;
var mankiImporter = (ModelImporter)AssetImporter.GetAtPath((string)manki["mesh"]);
var saved = mankiImporter.humanDescription;
var mankiMat = AssetDatabase.LoadAssetAtPath<Material>((string)manki["material"]);
report["materialFrom"] = manki["material"];

// The first model's single colour map (2026-10-21) is replaced by one map per piece.
if (AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Textures/Bashie_BaseColor.png") != null)
    AssetDatabase.DeleteAsset(Dir + "/Textures/Bashie_BaseColor.png");

// One colour map and one material (Manki's shader and settings) per piece.
var materials = new Dictionary<string, Material>();
foreach (var piece in pieces)
{
    string tex = Dir + "/Textures/" + piece.Key + ".png";
    System.IO.File.Copy(System.IO.Path.Combine(Build, "textures", piece.Key + ".png"), System.IO.Path.Combine(root, tex), true);
    AssetDatabase.ImportAsset(tex, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    var ti = (TextureImporter)AssetImporter.GetAtPath(tex);
    ti.textureType = TextureImporterType.Default;
    ti.sRGBTexture = true;
    ti.mipmapEnabled = true;
    ti.maxTextureSize = piece.Value;
    ti.textureCompression = TextureImporterCompression.Compressed;
    ti.filterMode = FilterMode.Bilinear;
    ti.SaveAndReimport();
    string matPath = Dir + "/Materials/" + piece.Key + ".mat";
    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
    if (mat == null)
    {
        mat = new Material(mankiMat) { name = piece.Key };
        AssetDatabase.CreateAsset(mat, matPath);
    }
    var t2d = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", t2d);
    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", t2d);
    EditorUtility.SetDirty(mat);
    AssetDatabase.SaveAssetIfDirty(mat);
    materials[piece.Key] = mat;
}

// Model: Manki's import settings, Humanoid avatar from this T-pose, Manki's mapping where the bones exist.
System.IO.File.Copy(System.IO.Path.Combine(Build, "Bashie.fbx"), System.IO.Path.Combine(root, Fbx), true);
AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
mi.globalScale = mankiImporter.globalScale;
mi.useFileScale = mankiImporter.useFileScale;
mi.importBlendShapes = false;
mi.importCameras = false;
mi.importLights = false;
mi.importAnimation = false;
mi.optimizeGameObjects = false;
mi.isReadable = mankiImporter.isReadable;
mi.importNormals = ModelImporterNormals.Import;
mi.materialImportMode = mankiImporter.materialImportMode;
foreach (var m in materials)
    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.Key), m.Value);
mi.animationType = ModelImporterAnimationType.Human;
mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
mi.humanDescription = new HumanDescription();
mi.autoGenerateAvatarMappingIfUnspecified = true;
mi.SaveAndReimport();
mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
var hd = mi.humanDescription;
var skeletonNames = new HashSet<string>(hd.skeleton.Select(s => s.name));
var human = saved.human.Where(h => skeletonNames.Contains(h.boneName)).ToList();
var missing = saved.human.Where(h => !skeletonNames.Contains(h.boneName)).Select(h => h.humanName).ToArray();
hd.human = human.ToArray();
hd.upperArmTwist = saved.upperArmTwist; hd.lowerArmTwist = saved.lowerArmTwist;
hd.upperLegTwist = saved.upperLegTwist; hd.lowerLegTwist = saved.lowerLegTwist;
hd.armStretch = saved.armStretch; hd.legStretch = saved.legStretch; hd.feetSpacing = saved.feetSpacing;
hd.hasTranslationDoF = saved.hasTranslationDoF;
mi.humanDescription = hd;
mi.SaveAndReimport();
report["mapped"] = hd.human.Length;
report["unmapped"] = missing;
report["skeletonBones"] = hd.skeleton.Length;
var notFingers = missing.Where(n => !n.Contains("Thumb") && !n.Contains("Index") && !n.Contains("Middle") && !n.Contains("Ring") && !n.Contains("Little")).ToArray();
if (notFingers.Length != 0)
    throw new InvalidOperationException("Bashie's rig is missing mapped body bones: " + string.Join(", ", notFingers));

// Candidate prefab: a variant of the model, built in a preview scene so the open scene is not dirtied.
var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try
{
    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
    inst.name = "BashieRig";
    // Manki's importer brings no materials in (his prefab assigns his), so each piece gets its material here.
    var placed = new Dictionary<string, object>();
    foreach (var r in inst.GetComponentsInChildren<Renderer>(true).Where(r => r is SkinnedMeshRenderer || r is MeshRenderer))
    {
        var mat = materials.TryGetValue(r.name, out var m) ? m : materials["Bashie_Body"];
        r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
        placed[r.name] = new Dictionary<string, object>
        {
            ["material"] = mat.name,
            ["bones"] = r is SkinnedMeshRenderer smr ? smr.bones.Where(b => b != null).Select(b => b.name).ToArray().Length : 0,
            ["rootBone"] = r is SkinnedMeshRenderer smr2 && smr2.rootBone != null ? smr2.rootBone.name : r.transform.parent.name,
        };
    }
    report["renderers"] = placed;
    PrefabUtility.SaveAsPrefabAsset(inst, PrefabPath);
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }

var bashie = Probe(PrefabPath);
report["bashie"] = bashie;
if (!(bool)bashie["avatarValid"])
    throw new InvalidOperationException("The Bashie Avatar is invalid.");
report["heightVsMankiRest"] = ((float)bashie["restMaxY"] - (float)bashie["restMinY"]) / ((float)manki["restMaxY"] - (float)manki["restMinY"]);
return report;
