const string Build = "/home/binoui/Documents/projects/SlopArena/tmp/nyx-model/spine-fix";
const string Dir = "Assets/Art/Characters/nyx";
const string Fbx = Dir + "/Models/Nyx.fbx";
const string PrefabPath = "Assets/CharacterPackages/nyx/Presentation/NyxRig.prefab";
var maps = new Dictionary<string, int> { ["Nyx_Fake"] = 2048, ["Nyx_Real"] = 2048, ["Nyx_Tights"] = 256, ["Nyx_Phone"] = 512 };
string project = System.IO.Path.GetDirectoryName(Application.dataPath);
var prior = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
if (prior == null) throw new InvalidOperationException("Build the initial shared Nyx prefab before replacing its real lower body.");
var priorAnimator = prior.GetComponentInChildren<Animator>(true);
var priorAvatar = priorAnimator.avatar;
float priorScale = priorAnimator.humanScale;
var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
string priorHumanDescription = JsonUtility.ToJson(mi.humanDescription);
var template = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Materials/Nyx_Real.mat");
var materials = new Dictionary<string, Material>();
foreach (var map in maps)
{
    string tex = Dir + "/Textures/" + map.Key + ".png";
    string destination = System.IO.Path.Combine(project, tex);
    byte[] bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(Build, "textures", map.Key + ".png"));
    if (!System.IO.File.Exists(destination) || !System.IO.File.ReadAllBytes(destination).SequenceEqual(bytes))
    {
        System.IO.File.WriteAllBytes(destination, bytes);
        AssetDatabase.ImportAsset(tex, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    }
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
        mat = new Material(template) { name = map.Key };
        AssetDatabase.CreateAsset(mat, matPath);
    }
    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
    if (map.Key == "Nyx_Dress")
    {
        mat.SetFloat("_Cull", 0);
        mat.doubleSidedGI = true;
    }
    EditorUtility.SetDirty(mat);
    AssetDatabase.SaveAssetIfDirty(mat);
    materials[map.Key] = mat;
}
var dressMaterial = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Materials/Nyx_Dress.mat");
if (dressMaterial == null) { dressMaterial = new Material(template) { name = "Nyx_Dress" }; AssetDatabase.CreateAsset(dressMaterial, Dir + "/Materials/Nyx_Dress.mat"); }
var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Textures/Nyx_Real.png");
if (dressMaterial.HasProperty("_BaseMap")) dressMaterial.SetTexture("_BaseMap", atlas);
if (dressMaterial.HasProperty("_MainTex")) dressMaterial.SetTexture("_MainTex", atlas);
dressMaterial.SetFloat("_Cull", 0); dressMaterial.doubleSidedGI = true;
EditorUtility.SetDirty(dressMaterial); AssetDatabase.SaveAssetIfDirty(dressMaterial);
materials["Nyx_Dress"] = dressMaterial;
System.IO.File.Copy(System.IO.Path.Combine(Build, "Nyx.fbx"), System.IO.Path.Combine(project, Fbx), true);
AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
mi.isReadable = true;
foreach (var m in materials)
    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.Key), m.Value);
mi.SaveAndReimport();
if (JsonUtility.ToJson(mi.humanDescription) != priorHumanDescription)
    throw new InvalidOperationException("Mesh import changed the existing Humanoid description.");
var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
var root = PrefabUtility.LoadPrefabContents(PrefabPath);
var report = new Dictionary<string, object>();
try
{
    var bones = root.GetComponentsInChildren<Transform>(true).Where(t => t.GetComponent<Renderer>() == null)
        .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
    var importedBones = model.GetComponentsInChildren<Transform>(true).Where(t => t.GetComponent<Renderer>() == null &&
        (t.name.StartsWith("mixamorig:") || t.name.StartsWith("Nyx_Skirt_") || t.name == "Nyx_Backpack" || t.name == "Nyx_PoleHeld" || t.name == "Nyx_PhoneBone"))
        .OrderBy(t => t.GetComponentsInParent<Transform>(true).Length).ToArray();
    int preservedBones = 0;
    foreach (var bone in importedBones)
    {
        if (bones.TryGetValue(bone.name, out var retained))
        {
            if (retained.parent.name != bone.parent.name || Vector3.Distance(retained.localPosition, bone.localPosition) > 0.00001f || Quaternion.Angle(retained.localRotation, bone.localRotation) > .001f || Vector3.Distance(retained.localScale, bone.localScale) > .00001f)
                throw new InvalidOperationException("Bone hierarchy or rest transform changed: " + bone.name);
            preservedBones++;
            continue;
        }
        if (bone.name != "Nyx_PhoneBone") throw new InvalidOperationException("Unexpected added bone: " + bone.name);
        var added = new GameObject(bone.name).transform;
        added.SetParent(bones[bone.parent.name], false);
        added.localPosition = bone.localPosition;
        added.localRotation = bone.localRotation;
        added.localScale = bone.localScale;
        bones[added.name] = added;
    }
    foreach (var oldCloth in root.GetComponentsInChildren<Cloth>(true)) UnityEngine.Object.DestroyImmediate(oldCloth);
    foreach (var old in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("NyxDressCollider_")).ToArray())
        UnityEngine.Object.DestroyImmediate(old.gameObject);
    foreach (var source in model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name.StartsWith("Nyx_")))
    {
        var destination = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SingleOrDefault(r => r.name == source.name);
        if (destination == null)
        {
            var go = new GameObject(source.name);
            go.transform.SetParent(root.transform, false);
            destination = go.AddComponent<SkinnedMeshRenderer>();
        }
        var oldCloth = destination.GetComponent<Cloth>();
        if (oldCloth != null) UnityEngine.Object.DestroyImmediate(oldCloth);
        destination.transform.localPosition = source.transform.localPosition;
        destination.transform.localRotation = source.transform.localRotation;
        destination.transform.localScale = source.transform.localScale;
        destination.sharedMesh = source.sharedMesh;
        destination.bones = source.bones.Select(b => bones[b.name]).ToArray();
        destination.rootBone = bones[source.rootBone.name];
        string key = source.name == "Nyx_Real_Dress" ? "Nyx_Dress" : source.name == "Nyx_Real_Tights" ? "Nyx_Tights" : source.name == "Nyx_Phone" ? "Nyx_Phone" : source.name.StartsWith("Nyx_Real") ? "Nyx_Real" : "Nyx_Fake";
        destination.sharedMaterials = Enumerable.Repeat(materials[key], source.sharedMaterials.Length).ToArray();
        destination.updateWhenOffscreen = true;
        destination.quality = SkinQuality.Bone4;
        destination.localBounds = new Bounds(new Vector3(0,.9f,0), new Vector3(4,4,4));
        destination.gameObject.SetActive(source.name.StartsWith("Nyx_Real"));
        report[source.name] = new { vertices = source.sharedMesh.vertexCount, bones = source.bones.Length, material = key };
    }
    var animator = root.GetComponentInChildren<Animator>(true);
    if (animator.avatar != priorAvatar || !animator.avatar.isValid || !animator.avatar.isHuman || Mathf.Abs(animator.humanScale-priorScale) > .000001f)
        throw new InvalidOperationException("The existing Avatar or human scale changed.");
    var forms = root.GetComponentInChildren<SlopArena.Client.Entities.FormPresentation>(true);
    if (forms == null) throw new InvalidOperationException("Nyx requires FormPresentation.");
    var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    forms.Configure(skins.Where(r => r.name.StartsWith("Nyx_Real")).Select(r => r.gameObject).ToArray(),
        skins.Where(r => r.name == "Nyx_Fake" || r.name == "Nyx_Backpack" || r.name == "Nyx_BackpackPole" || r.name == "Nyx_Phone").Select(r => r.gameObject).ToArray(),
        skins.Where(r => r.name == "Nyx_PoleHeld").Select(r => r.gameObject).ToArray());
    forms.Show(0);
    var dress = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Nyx_Real_Dress");
    var spring = dress.GetComponent<SlopArena.Client.Entities.SkirtBoneSpring>();
    if (spring == null) spring = dress.gameObject.AddComponent<SlopArena.Client.Entities.SkirtBoneSpring>();
    var settings = new SerializedObject(spring);
    settings.FindProperty("_hips").objectReferenceValue = bones["mixamorig:Hips"];
    settings.FindProperty("_modelRoot").objectReferenceValue = root.transform;
    var chains = settings.FindProperty("_chains"); chains.arraySize = 12;
    for (int c=0; c<12; c++)
    {
        var chain = chains.GetArrayElementAtIndex(c).FindPropertyRelative("Bones"); chain.arraySize = 4;
        for (int j=0; j<4; j++) chain.GetArrayElementAtIndex(j).objectReferenceValue = bones[$"Nyx_Skirt_{c:00}_{j:00}"];
    }
    var capsuleDefinitions = new[] { ("LeftUpLeg", "LeftLeg", .08f), ("RightUpLeg", "RightLeg", .08f),
        ("LeftLeg", "LeftFoot", .06f), ("RightLeg", "RightFoot", .06f),
        ("LeftFoot", "LeftToeBase", .075f), ("RightFoot", "RightToeBase", .075f) };
    var capsules = settings.FindProperty("_capsules"); capsules.arraySize = capsuleDefinitions.Length;
    for (int i=0; i<capsuleDefinitions.Length; i++)
    {
        var capsule = capsules.GetArrayElementAtIndex(i); var definition = capsuleDefinitions[i];
        capsule.FindPropertyRelative("Start").objectReferenceValue = bones["mixamorig:" + definition.Item1];
        capsule.FindPropertyRelative("End").objectReferenceValue = bones["mixamorig:" + definition.Item2];
        capsule.FindPropertyRelative("Radius").floatValue = definition.Item3;
    }
    settings.FindProperty("_stiffness").floatValue = .22f;
    settings.FindProperty("_damping").floatValue = .3f;
    settings.FindProperty("_neighbourBlend").floatValue = .35f;
    settings.FindProperty("_maxHemTravel").floatValue = .2f;
    settings.FindProperty("_fabricClearance").floatValue = .04f;
    settings.ApplyModifiedPropertiesWithoutUndo();
    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
    var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    if (saved.GetComponentsInChildren<Cloth>(true).Length != 0 || saved.GetComponentsInChildren<SlopArena.Client.Entities.SkirtBoneSpring>(true).Length != 1)
        throw new InvalidOperationException("Spring dress failed prefab persistence.");
    report["renderers"] = saved.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => new { name = r.name, active = r.gameObject.activeSelf }).ToArray();
    report["phoneBone"] = new { parent = bones["Nyx_PhoneBone"].parent.name, leaf = bones["Nyx_PhoneBone"].childCount == 0 };
    report["skirt"] = new { chains = 12, segments = 4, capsules = capsuleDefinitions.Length, clothComponents = 0 };
    report["avatar"] = new { valid = animator.avatar.isValid, human = animator.avatar.isHuman, humanScale = animator.humanScale, preserved = true, checkedBones = preservedBones };
}
finally { PrefabUtility.UnloadPrefabContents(root); }
return report;
