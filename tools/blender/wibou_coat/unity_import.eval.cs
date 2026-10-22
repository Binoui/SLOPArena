// Eval body: bind build_coat.py's WibouCoat.fbx onto Wibou's rig prefab as an extra skinned renderer.
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// --timeout 120000 (absolute path). Edit Src* first. Set Write = false for a dry run that only checks alignment.
// Wibou's own model (wibou.fbx), Avatar and clips are untouched. Saving the prefab, which Wibou's catalog
// references, makes the cook postprocessor persist a cook of the roster (see the character docs).
const bool Write = true;
const string SrcFbx = "/tmp/wibou-coat-build/WibouCoat.fbx";
const string SrcTexture = "/tmp/wibou-coat-build/textures/WibouCoat_BaseColor.png";
const string Fbx = "Assets/Art/Characters/wibou/Models/wibou_coat.fbx";
const string Texture = "Assets/Art/Characters/wibou/Textures/WibouCoat_BaseColor.png";
const string MaterialPath = "Assets/Art/Characters/wibou/Materials/WibouCoat.mat";
const string BodyMaterialPath = "Assets/Art/Characters/wibou/Materials/Material.002.mat";
const string PrefabPath = "Assets/Resources/Characters/Wibou.prefab";
const string CoatName = "WibouCoat";
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);

if (Write)
{
    System.IO.File.Copy(SrcTexture, System.IO.Path.Combine(root, Texture), true);
    AssetDatabase.ImportAsset(Texture, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    var ti = (TextureImporter)AssetImporter.GetAtPath(Texture);
    if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; ti.SaveAndReimport(); }

    System.IO.File.Copy(SrcFbx, System.IO.Path.Combine(root, Fbx), true);
    AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
    bool dirty = false;
    // Generic without an Avatar keeps the skin; None imports the coat as an unskinned MeshRenderer.
    if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; dirty = true; }
    if (mi.avatarSetup != ModelImporterAvatarSetup.NoAvatar) { mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar; dirty = true; }
    if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
    if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
    if (mi.importCameras || mi.importLights || mi.importBlendShapes) { mi.importCameras = mi.importLights = mi.importBlendShapes = false; dirty = true; }
    if (dirty) mi.SaveAndReimport();
}
var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Texture);
var coatModel = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
if (tex == null || coatModel == null) { report["error"] = "coat texture or model missing; run with Write = true first"; return report; }
report["texture"] = tex.width + "x" + tex.height + " " + tex.format;

// Material: a copy of the body's (same shader and surface settings) with the coat's colour map.
var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
var bodyMat = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
if (Write)
{
    if (mat == null) { mat = new Material(bodyMat) { name = "WibouCoat" }; AssetDatabase.CreateAsset(mat, MaterialPath); }
    else mat.CopyPropertiesFromMaterial(bodyMat);
    mat.shader = bodyMat.shader;
    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
    // Save only this asset: AssetDatabase.SaveAssets() would also flush other threads' unsaved edits in the shared Editor.
    EditorUtility.SetDirty(mat);
    AssetDatabase.SaveAssetIfDirty(mat);
}
report["material"] = mat != null ? mat.shader.name : "missing";

var coatSmr = coatModel.GetComponentInChildren<SkinnedMeshRenderer>(true);
if (coatSmr == null) { report["error"] = "the coat FBX imported without a SkinnedMeshRenderer; check its rig import settings"; return report; }
var coatMesh = coatSmr.sharedMesh;
report["coatMesh"] = coatMesh.name + " verts " + coatMesh.vertexCount + " tris " + (coatMesh.triangles.Length / 3) + " bones " + coatSmr.bones.Length;

var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
try
{
    var body = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name != CoatName);
    var byName = new Dictionary<string, Transform>();
    foreach (var t in contents.GetComponentsInChildren<Transform>(true))
        if (!byName.ContainsKey(t.name)) byName[t.name] = t;
    report["body"] = body.name + " bones " + body.bones.Length;

    // The coat was exported in Wibou's bind space; prove it: shared bones must have the body's bind poses.
    var bodyBind = new Dictionary<string, Matrix4x4>();
    for (int i = 0; i < body.bones.Length; i++) bodyBind[body.bones[i].name] = body.sharedMesh.bindposes[i];
    float maxDev = 0f; int shared = 0;
    for (int i = 0; i < coatSmr.bones.Length; i++)
    {
        if (!bodyBind.TryGetValue(coatSmr.bones[i].name, out var bb)) continue;
        shared++;
        var cb = coatMesh.bindposes[i];
        for (int k = 0; k < 16; k++) maxDev = Mathf.Max(maxDev, Mathf.Abs(cb[k] - bb[k]));
    }
    report["sharedBones"] = shared;
    report["bindposeMaxDeviation"] = maxDev;
    var coatNode = coatSmr.transform;
    report["meshNode"] = coatNode.parent.name + " pos " + coatNode.localPosition.ToString("F4") + " rot " + coatNode.localRotation.eulerAngles.ToString("F2") + " scale " + coatNode.localScale.ToString("F4");
    report["bodyNode"] = body.transform.parent.name + " pos " + body.transform.localPosition.ToString("F4") + " rot " + body.transform.localRotation.eulerAngles.ToString("F2") + " scale " + body.transform.localScale.ToString("F4");
    if (maxDev > 1e-3f)
    {
        report["error"] = "coat bind poses differ from the body's; not binding";
        return report;
    }

    // Sway chains: same local rest transforms under Hips as in the coat FBX.
    var coatTransforms = coatModel.GetComponentsInChildren<Transform>(true);
    var chainSources = coatTransforms.Where(t => t.name.StartsWith("Coat_")).OrderBy(t => t.name.Length).ThenBy(t => t.name).ToList();
    var created = new List<string>();
    foreach (var src in chainSources.OrderBy(t => Depth(t)))
    {
        var parent = byName[src.parent.name];
        var existing = parent.Find(src.name);
        var bone = existing != null ? existing : new GameObject(src.name).transform;
        if (existing == null) { bone.SetParent(parent, false); created.Add(src.name); }
        bone.localPosition = src.localPosition;
        bone.localRotation = src.localRotation;
        bone.localScale = src.localScale;
        byName[src.name] = bone;
    }
    report["chainBones"] = chainSources.Count;
    report["chainBonesCreated"] = created.Count;

    // Renderer next to the body, with the coat FBX's mesh node transform.
    var coatTf = body.transform.parent.Find(CoatName);
    if (coatTf == null)
    {
        coatTf = new GameObject(CoatName).transform;
        coatTf.SetParent(body.transform.parent, false);
    }
    coatTf.localPosition = coatNode.localPosition;
    coatTf.localRotation = coatNode.localRotation;
    coatTf.localScale = coatNode.localScale;
    coatTf.gameObject.layer = body.gameObject.layer;
    var smr = coatTf.GetComponent<SkinnedMeshRenderer>();
    if (smr == null) smr = coatTf.gameObject.AddComponent<SkinnedMeshRenderer>();
    smr.sharedMesh = coatMesh;
    smr.sharedMaterials = new[] { mat };
    smr.bones = coatSmr.bones.Select(b => byName[b.name]).ToArray();
    smr.rootBone = byName[coatSmr.rootBone.name];
    smr.localBounds = coatSmr.localBounds;
    smr.quality = body.quality;
    smr.updateWhenOffscreen = body.updateWhenOffscreen;
    smr.shadowCastingMode = body.shadowCastingMode;
    smr.receiveShadows = body.receiveShadows;
    smr.skinnedMotionVectors = body.skinnedMotionVectors;

    // Presentation-only sway (Play Mode, or BoneChainSpring.Step in offline renders).
    int springs = 0;
    foreach (var key in new[] { "FL", "BL", "BR", "FR" })
    {
        if (!byName.TryGetValue("Coat_" + key + "_1", out var b1) || !byName.TryGetValue("Coat_" + key + "_2", out var b2)) continue;
        var spring = b1.GetComponent<SlopArena.Client.Entities.BoneChainSpring>();
        if (spring == null) spring = b1.gameObject.AddComponent<SlopArena.Client.Entities.BoneChainSpring>();
        spring.Configure(new[] { b1, b2 });
        springs++;
    }
    report["springs"] = springs;
    if (Write) PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
}
finally { PrefabUtility.UnloadPrefabContents(contents); }

// Check the bound result in the bind pose: the coat must sit where build_coat.py put it around the body.
var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
go.hideFlags = HideFlags.HideAndDontSave;
try
{
    var coat = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == CoatName);
    if (coat != null)
    {
        var l2w = coat.transform.localToWorldMatrix;
        Vector3 BindPos(string bone)
        {
            int i = Array.FindIndex(coat.bones, b => b.name == bone);
            return l2w.MultiplyPoint3x4(coat.sharedMesh.bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero));
        }
        var verts = coat.sharedMesh.vertices;
        var c = Vector3.zero;
        foreach (var v in verts) c += l2w.MultiplyPoint3x4(v);
        c /= verts.Length;
        report["centroidToHips"] = (c - BindPos("mixamorig:Hips")).magnitude;
        report["centroidToNeck"] = (c - BindPos("mixamorig:Neck")).magnitude;
        report["coatRendererBones"] = coat.bones.Length;
        report["springsOnInstance"] = go.GetComponentsInChildren<SlopArena.Client.Entities.BoneChainSpring>(true).Length;
    }
    var a = go.GetComponentInChildren<Animator>();
    if (a != null) { a.Rebind(); report["humanScale"] = a.humanScale; report["avatarValid"] = a.avatar != null && a.avatar.isValid && a.avatar.isHuman; }
}
finally { UnityEngine.Object.DestroyImmediate(go); }
return report;

int Depth(Transform t) { int d = 0; while (t.parent != null) { d++; t = t.parent; } return d; }
