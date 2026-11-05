// Eval body: import spring_poncho.py's candidate beside the live rig. Writes the candidate model
// Assets/Art/Characters/salamander/Models/Candidates/salamander_spring_poncho.fbx and the candidate prefab
// Assets/CharacterPackages/salamander/Presentation/SalamanderRig_SpringPoncho.prefab, a variant of that model with the
// live prefab's materials and a SkirtBoneSpring on the poncho. The live FBX, its import settings, the live prefab and
// the catalog are only read. The candidate is imported with the live importer's settings and Humanoid description, so
// the check below is the one an install would face: every live bone keeps its name, parent and rest, the only new
// transforms are the poncho chains, and the Avatar is valid with the live humanScale.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/salamander_rig/spring_poncho_import.eval.cs"
const string Build = "/home/binoui/Documents/projects/SlopArena/tmp/dusty-poncho/build-v1";
const string LiveFbx = "Assets/Art/Characters/salamander/Models/salamander.fbx";
const string LivePrefab = "Assets/CharacterPackages/salamander/Presentation/SalamanderRig.prefab";
const string Folder = "Assets/Art/Characters/salamander/Models/Candidates";
const string CandidateFbx = Folder + "/salamander_spring_poncho.fbx";
const string CandidatePrefab = "Assets/CharacterPackages/salamander/Presentation/SalamanderRig_SpringPoncho.prefab";
// The poncho is one layer of fabric; the live material culls back faces, so its inside showed as holes and slivers
// under the arms. The candidate uses a two-sided copy, as Nyx's dress does; the live material is untouched.
const string TwoSidedMaterial = "Assets/Art/Characters/salamander/Materials/Salamander_Poncho_TwoSided.mat";
const int Chains = 10, Segments = 4;
// Rig-scale radii from chains.json's skin measurements (median, rounded up).
var capsuleDefinitions = new[] {
    ("mixamorig:LeftUpLeg", "mixamorig:LeftLeg", .07f), ("mixamorig:RightUpLeg", "mixamorig:RightLeg", .07f),
    ("mixamorig:LeftLeg", "mixamorig:LeftFoot", .045f), ("mixamorig:RightLeg", "mixamorig:RightFoot", .045f),
    ("mixamorig:LeftArm", "mixamorig:LeftForeArm", .042f), ("mixamorig:RightArm", "mixamorig:RightForeArm", .042f),
    ("mixamorig:LeftForeArm", "mixamorig:LeftHand", .03f), ("mixamorig:RightForeArm", "mixamorig:RightHand", .03f),
    ("mixamorig:LeftHand", "mixamorig:LeftHandMiddle2", .03f), ("mixamorig:RightHand", "mixamorig:RightHandMiddle2", .03f),
    ("Tail1", "Tail2", .065f), ("Tail2", "Tail3", .065f),
    ("PonchoCollider_BellyLow", "PonchoCollider_BellyHigh", .06f), ("PonchoCollider_BellyHigh", "PonchoCollider_Chest", .055f) };
// His belly and chest sit 8.4-9.8 cm in front of the spine and only 3.5-6 cm behind it, so the torso capsules run
// between unmapped helpers in front of Hips, Spine1 and Spine2. Without them, running pushed his belly and chest out
// through the front panel.
var helpers = new[] { ("PonchoCollider_BellyLow", "mixamorig:Hips", .04f), ("PonchoCollider_BellyHigh", "mixamorig:Spine1", .04f),
    ("PonchoCollider_Chest", "mixamorig:Spine2", .03f) };
string project = System.IO.Path.GetDirectoryName(Application.dataPath);
var report = new Dictionary<string, object>();
var live = (ModelImporter)AssetImporter.GetAtPath(LiveFbx);
string liveDescription = JsonUtility.ToJson(live.humanDescription);
var liveModel = AssetDatabase.LoadAssetAtPath<GameObject>(LiveFbx);
var livePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LivePrefab);
if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/Characters/salamander/Models", "Candidates");
System.IO.File.Copy(System.IO.Path.Combine(Build, "salamander_spring_poncho.fbx"), System.IO.Path.Combine(project, CandidateFbx), true);
AssetDatabase.ImportAsset(CandidateFbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var mi = (ModelImporter)AssetImporter.GetAtPath(CandidateFbx);
mi.globalScale = live.globalScale;
mi.useFileScale = live.useFileScale;
mi.importBlendShapes = live.importBlendShapes;
mi.importCameras = live.importCameras;
mi.importLights = live.importLights;
mi.importAnimation = live.importAnimation;
mi.optimizeGameObjects = live.optimizeGameObjects;
// The spring's skin-contact pass reads the poncho's vertices and weights, which players only allow on readable meshes.
mi.isReadable = true;
mi.importNormals = live.importNormals;
mi.materialImportMode = live.materialImportMode;
mi.animationType = ModelImporterAnimationType.Human;
mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
mi.humanDescription = live.humanDescription;
mi.SaveAndReimport();
mi = (ModelImporter)AssetImporter.GetAtPath(CandidateFbx);
if (JsonUtility.ToJson(mi.humanDescription) != liveDescription)
    throw new InvalidOperationException("The candidate import changed the live Humanoid description.");
var model = AssetDatabase.LoadAssetAtPath<GameObject>(CandidateFbx);

Dictionary<string, Transform> Named(GameObject go) => go.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.Single());
// Bones in each model's own frame (root at the origin, scale 1), as an install would import them.
// The model roots are named after their files; everything under them must match by name.
var liveBones = Named(liveModel); liveBones.Remove(liveModel.name);
var candidateBones = Named(model); candidateBones.Remove(model.name);
string ParentName(Transform t, GameObject root) => t.parent == root.transform ? "<root>" : t.parent.name;
float worstPosition = 0f, worstAngle = 0f;
foreach (var pair in liveBones)
{
    if (!candidateBones.TryGetValue(pair.Key, out var bone))
        throw new InvalidOperationException("The candidate lost " + pair.Key);
    if (ParentName(bone, model) != ParentName(pair.Value, liveModel))
        throw new InvalidOperationException("The candidate reparented " + pair.Key);
    if (pair.Value.GetComponent<Renderer>() != null) continue;
    worstPosition = Mathf.Max(worstPosition, Vector3.Distance(pair.Value.position, bone.position));
    worstAngle = Mathf.Max(worstAngle, Quaternion.Angle(pair.Value.rotation, bone.rotation));
}
var added = candidateBones.Keys.Except(liveBones.Keys).OrderBy(n => n).ToArray();
var expected = Enumerable.Range(0, Chains).SelectMany(c => Enumerable.Range(0, Segments).Select(j => $"Salamander_Poncho_{c:00}_{j:00}")).OrderBy(n => n).ToArray();
if (!added.SequenceEqual(expected))
    throw new InvalidOperationException("Unexpected added transforms: " + string.Join(", ", added.Except(expected)));
foreach (string name in added)
{
    var bone = candidateBones[name];
    bool root = name.EndsWith("_00");
    if (root ? bone.parent.name != "mixamorig:Spine2" : !bone.parent.name.StartsWith("Salamander_Poncho_"))
        throw new InvalidOperationException(name + " is not a poncho leaf chain under Spine2.");
}
if (worstPosition > 1e-5f || worstAngle > .01f)
    throw new InvalidOperationException($"Rest moved: {worstPosition} m, {worstAngle} deg.");
report["bones"] = new { live = liveBones.Count(p => p.Value.GetComponent<Renderer>() == null), added = added.Length, worstPositionMetres = worstPosition, worstAngleDegrees = worstAngle };

var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try
{
    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
    inst.name = "SalamanderRig_SpringPoncho";
    var liveRenderers = livePrefab.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r.name);
    foreach (var renderer in inst.GetComponentsInChildren<Renderer>(true))
        renderer.sharedMaterials = liveRenderers[renderer.name].sharedMaterials;
    var liveFabric = liveRenderers["Salamander_Poncho"].sharedMaterial;
    var twoSided = AssetDatabase.LoadAssetAtPath<Material>(TwoSidedMaterial);
    if (twoSided == null) { twoSided = new Material(liveFabric); AssetDatabase.CreateAsset(twoSided, TwoSidedMaterial); }
    else twoSided.CopyPropertiesFromMaterial(liveFabric);
    twoSided.name = "Salamander_Poncho_TwoSided";
    twoSided.SetFloat("_Cull", 0f);
    twoSided.doubleSidedGI = true;
    EditorUtility.SetDirty(twoSided);
    AssetDatabase.SaveAssetIfDirty(twoSided);
    var bones = Named(inst);
    foreach (var (name, parent, forward) in helpers)
    {
        var helper = new GameObject(name).transform;
        helper.SetParent(bones[parent], false);
        helper.position = bones[parent].position + inst.transform.forward * forward;
        bones[name] = helper;
    }
    var poncho = (SkinnedMeshRenderer)bones["Salamander_Poncho"].GetComponent<Renderer>();
    poncho.sharedMaterial = twoSided;
    var spring = poncho.gameObject.AddComponent<SlopArena.Client.Entities.SkirtBoneSpring>();
    var settings = new SerializedObject(spring);
    settings.FindProperty("_hips").objectReferenceValue = bones["mixamorig:Spine2"];
    settings.FindProperty("_modelRoot").objectReferenceValue = inst.transform;
    var chains = settings.FindProperty("_chains"); chains.arraySize = Chains;
    for (int c = 0; c < Chains; c++)
    {
        var chain = chains.GetArrayElementAtIndex(c).FindPropertyRelative("Bones"); chain.arraySize = Segments;
        for (int j = 0; j < Segments; j++) chain.GetArrayElementAtIndex(j).objectReferenceValue = bones[$"Salamander_Poncho_{c:00}_{j:00}"];
    }
    var capsules = settings.FindProperty("_capsules"); capsules.arraySize = capsuleDefinitions.Length;
    for (int i = 0; i < capsuleDefinitions.Length; i++)
    {
        var capsule = capsules.GetArrayElementAtIndex(i);
        capsule.FindPropertyRelative("Start").objectReferenceValue = bones[capsuleDefinitions[i].Item1];
        capsule.FindPropertyRelative("End").objectReferenceValue = bones[capsuleDefinitions[i].Item2];
        capsule.FindPropertyRelative("Radius").floatValue = capsuleDefinitions[i].Item3;
    }
    // Nyx's response; distances shrink with his smaller rig (1.08 m tall at scale 1, Nyx 1.7 m).
    settings.FindProperty("_stiffness").floatValue = .22f;
    settings.FindProperty("_damping").floatValue = .3f;
    settings.FindProperty("_neighbourBlend").floatValue = .35f;
    settings.FindProperty("_maxHemTravel").floatValue = .12f;
    settings.FindProperty("_fabricClearance").floatValue = .01f;
    // The poncho hangs above his legs: swing each chain just clear of legs, arms and tail instead of Nyx's skirt lift.
    settings.FindProperty("_liftModel").enumValueIndex = (int)SlopArena.Client.Entities.SkirtBoneSpring.LiftModel.Clearance;
    settings.FindProperty("_maxLift").floatValue = 70f;
    settings.FindProperty("_liftReach").floatValue = .03f;
    settings.ApplyModifiedPropertiesWithoutUndo();
    PrefabUtility.SaveAsPrefabAsset(inst, CandidatePrefab);
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }

var saved = AssetDatabase.LoadAssetAtPath<GameObject>(CandidatePrefab);
var probe = UnityEngine.Object.Instantiate(saved);
probe.hideFlags = HideFlags.HideAndDontSave;
try
{
    var animator = probe.GetComponentInChildren<Animator>();
    animator.Rebind();
    var liveProbe = UnityEngine.Object.Instantiate(livePrefab);
    liveProbe.hideFlags = HideFlags.HideAndDontSave;
    float liveScale;
    try { var a = liveProbe.GetComponentInChildren<Animator>(); a.Rebind(); liveScale = a.humanScale; }
    finally { UnityEngine.Object.DestroyImmediate(liveProbe); }
    if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman || Mathf.Abs(animator.humanScale - liveScale) > 1e-6f)
        throw new InvalidOperationException($"Candidate Avatar invalid or humanScale {animator.humanScale} != live {liveScale}.");
    var springs = probe.GetComponentsInChildren<SlopArena.Client.Entities.SkirtBoneSpring>(true);
    if (springs.Length != 1 || probe.GetComponentsInChildren<Cloth>(true).Length != 0)
        throw new InvalidOperationException("The candidate prefab must carry exactly one poncho spring and no Cloth.");
    report["avatar"] = new { path = AssetDatabase.GetAssetPath(animator.avatar), valid = animator.avatar.isValid, human = animator.avatar.isHuman,
        humanScale = animator.humanScale, liveHumanScale = liveScale, humanDescriptionEqual = true };
    report["renderers"] = probe.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => new { name = r.name, vertices = r.sharedMesh.vertexCount,
        bones = r.bones.Length, material = r.sharedMaterial != null ? r.sharedMaterial.name : null }).ToArray();
}
finally { UnityEngine.Object.DestroyImmediate(probe); }
report["candidateFbx"] = CandidateFbx;
report["candidatePrefab"] = CandidatePrefab;
report["liveFbxUntouched"] = AssetDatabase.GetAssetPath(livePrefab.GetComponentInChildren<Animator>().avatar) == LiveFbx;
return report;
