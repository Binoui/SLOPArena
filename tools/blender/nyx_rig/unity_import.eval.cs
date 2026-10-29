const string Build = "/home/binoui/Documents/projects/SlopArena/tmp/nyx-model/rig-build-v1";
const string Dir = "Assets/Art/Characters/nyx";
const string Fbx = Dir + "/Models/Nyx.fbx";
const string PrefabPath = "Assets/CharacterPackages/nyx/Presentation/NyxRig.prefab";
const string MankiRig = "Assets/CharacterPackages/manki/Presentation/MankiRig.prefab";
var maps = new Dictionary<string, int> { ["Nyx_Fake"] = 2048, ["Nyx_Real"] = 2048 };
var rendererMap = new Dictionary<string, string>
{
    ["Nyx_Fake"] = "Nyx_Fake", ["Nyx_Real"] = "Nyx_Real", ["Nyx_Real_Dress"] = "Nyx_Real",
    ["Nyx_Backpack"] = "Nyx_Fake", ["Nyx_PoleHeld"] = "Nyx_Fake",
};
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);
foreach (var source in new[] { "Nyx.fbx" }.Concat(maps.Keys.Select(p => "textures/" + p + ".png")))
    if (!System.IO.File.Exists(System.IO.Path.Combine(Build, source)))
        throw new System.IO.FileNotFoundException("Missing build_rig.py output", source);
foreach (var folder in new[] { Dir, Dir + "/Models", Dir + "/Textures", Dir + "/Materials", "Assets/CharacterPackages/nyx/Presentation" })
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
        var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(s => s.name == "Nyx_Real")
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

var materials = new Dictionary<string, Material>();
foreach (var map in maps)
{
    string tex = Dir + "/Textures/" + map.Key + ".png";
    System.IO.File.Copy(System.IO.Path.Combine(Build, "textures", map.Key + ".png"), System.IO.Path.Combine(root, tex), true);
    AssetDatabase.ImportAsset(tex, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    var ti = (TextureImporter)AssetImporter.GetAtPath(tex);
    ti.textureType = TextureImporterType.Default;
    ti.sRGBTexture = true;
    ti.mipmapEnabled = true;
    ti.maxTextureSize = map.Value;
    ti.textureCompression = TextureImporterCompression.Compressed;
    ti.filterMode = FilterMode.Bilinear;
    ti.SaveAndReimport();
    string matPath = Dir + "/Materials/" + map.Key + ".mat";
    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
    if (mat == null)
    {
        mat = new Material(mankiMat) { name = map.Key };
        AssetDatabase.CreateAsset(mat, matPath);
    }
    var t2d = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", t2d);
    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", t2d);
    EditorUtility.SetDirty(mat);
    AssetDatabase.SaveAssetIfDirty(mat);
    materials[map.Key] = mat;
}

System.IO.File.Copy(System.IO.Path.Combine(Build, "Nyx.fbx"), System.IO.Path.Combine(root, Fbx), true);
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
if (missing.Length != 0 || human.Count != 46)
    throw new InvalidOperationException("Nyx is missing mapped bones: " + string.Join(", ", missing));

var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try
{
    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
    PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
    inst.name = "NyxRig";
    var placed = new Dictionary<string, object>();
    foreach (var r in inst.GetComponentsInChildren<Renderer>(true).Where(r => r is SkinnedMeshRenderer || r is MeshRenderer))
    {
        var mat = materials[rendererMap.TryGetValue(r.name, out var key) ? key : "Nyx_Body"];
        r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
        placed[r.name] = new Dictionary<string, object>
        {
            ["material"] = mat.name,
            ["bones"] = r is SkinnedMeshRenderer smr ? smr.bones.Count(b => b != null) : 0,
            ["vertices"] = r is SkinnedMeshRenderer smr3 ? smr3.sharedMesh.vertexCount : 0,
        };
    }
    report["renderers"] = placed;
    foreach (var name in new[] { "Nyx_Fake", "Nyx_Backpack", "Nyx_PoleHeld" })
    {
        var part = inst.GetComponentsInChildren<Transform>(true).Single(t => t.name == name && t.GetComponent<Renderer>() != null);
        part.gameObject.SetActive(false);
    }
    foreach (var skin in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        skin.updateWhenOffscreen = true;
        skin.localBounds = new Bounds(new Vector3(0, 0.9f, 0), new Vector3(4, 4, 4));
        skin.quality = SkinQuality.Bone4;
    }
    var dress = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Nyx_Real_Dress");
    const string DressMaterialPath = "Assets/Art/Characters/nyx/Materials/Nyx_Dress.mat";
    var dressMaterial = AssetDatabase.LoadAssetAtPath<Material>(DressMaterialPath);
    if (dressMaterial == null)
    {
        dressMaterial = new Material(dress.sharedMaterial) { name = "Nyx_Dress" };
        AssetDatabase.CreateAsset(dressMaterial, DressMaterialPath);
    }
    dressMaterial.SetFloat("_Cull", 0);
    dressMaterial.doubleSidedGI = true;
    EditorUtility.SetDirty(dressMaterial);
    AssetDatabase.SaveAssetIfDirty(dressMaterial);
    dress.sharedMaterial = dressMaterial;
    var cloth = dress.gameObject.AddComponent<Cloth>();
    cloth.stretchingStiffness = 1f;
    cloth.bendingStiffness = 1f;
    cloth.damping = 0.7f;
    cloth.useGravity = true;
    cloth.worldVelocityScale = 0.15f;
    cloth.worldAccelerationScale = 0.15f;
    cloth.friction = 0.15f;
    cloth.collisionMassScale = 0.2f;
    cloth.useTethers = true;
    cloth.clothSolverFrequency = 180f;
    cloth.enableContinuousCollision = true;
    Transform Bone(string n) => inst.GetComponentsInChildren<Transform>(true).Single(t => t.name == "mixamorig:" + n);
    var coeff = new ClothSkinningCoefficient[cloth.vertices.Length];
    var rootY = dress.rootBone.position.y;
    int pinned = 0;
    for (int i = 0; i < coeff.Length; i++)
    {
        float y = cloth.vertices[i].y + rootY;
        float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01((1.05f - y) / 0.65f));
        coeff[i].maxDistance = 0.65f * t;
        coeff[i].collisionSphereDistance = float.MaxValue;
        if (t == 0) pinned++;
    }
    cloth.coefficients = coeff;
    cloth.selfCollisionDistance = 0.025f;
    cloth.selfCollisionStiffness = 1f;
    cloth.SetSelfAndInterCollisionIndices(Enumerable.Range(0, coeff.Length).Select(i => (uint)i).ToList());
    var capsules = new List<CapsuleCollider>();
    foreach (var entry in new[] { ("Hips", "Spine", 0.13f), ("LeftUpLeg", "LeftLeg", 0.10f),
        ("RightUpLeg", "RightLeg", 0.10f), ("LeftLeg", "LeftFoot", 0.08f), ("RightLeg", "RightFoot", 0.08f) })
    {
        var bone = Bone(entry.Item1); var end = Bone(entry.Item2);
        var go = new GameObject("NyxDressCollider_" + entry.Item1);
        go.transform.SetParent(bone, false);
        var local = bone.InverseTransformPoint(end.position);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, local.normalized);
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true; capsule.direction = 1;
        capsule.center = new Vector3(0, local.magnitude / 2, 0);
        capsule.radius = entry.Item3 / Mathf.Abs(bone.lossyScale.x);
        capsule.height = local.magnitude + 2 * capsule.radius;
        capsules.Add(capsule);
    }
    cloth.capsuleColliders = capsules.ToArray();
    report["dressCloth"] = new { vertices = coeff.Length, pinned, colliders = capsules.Count };

    PrefabUtility.SaveAsPrefabAsset(inst, PrefabPath);
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }

var persistedRig = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
var persistedCloth = persistedRig.GetComponentInChildren<Cloth>(true);
if (persistedCloth == null) throw new InvalidOperationException("Nyx dress Cloth was not serialized into the saved prefab.");
report["persistedClothVertices"] = persistedCloth.coefficients.Length;
var sal = Probe(PrefabPath);
report["nyx"] = sal;
if (!(bool)sal["avatarValid"] || (float)sal["humanScale"] < 0.5f || (float)sal["humanScale"] > 2f)
    throw new InvalidOperationException("The nyx Avatar is invalid.");
var fg = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>("Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset");
if (fg?.Rig != null) report["fightguy"] = Probe(AssetDatabase.GetAssetPath(fg.Rig));
return report;
