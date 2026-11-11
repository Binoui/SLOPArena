// Eval body: swap in refit_coat.py's tapered WibouCoat.fbx and hang it from a SkirtBoneSpring.
// Run with (absolute path):
//   bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>" --timeout 120000
// The coat's texture, material and import settings stay. The script refuses unless the coat's bind poses still equal
// the body's, then in Resources/Characters/Wibou.prefab it moves the four Coat_* chains to the FBX's rest transforms,
// removes their BoneChainSprings and gives the coat renderer one SkirtBoneSpring (LegPose lift per side, thigh
// capsules, hang). BoneChainSpring only pulled each chain back to its rest shape in the hips' frame: the coat neither
// hung when he leaned nor moved off a raised thigh. Saving the prefab recooks wibou (bindings and manifest only);
// run sloparena.character.verify and roster.refresh afterwards.
const string SrcFbx = "/home/binoui/Documents/projects/SlopArena/tmp/part-fit/coat-refit-0.8/WibouCoat.fbx";
const string Backup = "/home/binoui/Documents/projects/SlopArena/artifacts/wibou-coat-refit-20261111/backup/wibou_coat.fbx";
const string Fbx = "Assets/Art/Characters/wibou/Models/wibou_coat.fbx";
const string PrefabPath = "Assets/Resources/Characters/Wibou.prefab";
const string CoatName = "WibouCoat";
// A thigh raised 10 degrees from straight down starts to lift the chains in front of it, and the lift grows 1.2
// degrees per degree up to a knee drawn to the chest (the dash lunge reaches 135): the chain pivots sit 14 cm above
// the hip joint, so the fabric must turn a little further than the thigh to lie on it. Each front panel follows its
// own thigh only. Measured on his clips with these values: about a quarter of the overlap the Clearance model left
// in the jump, ground 3 and air 1, half in the run, and no change in the dash lunge, where the thigh meets his belt.
const float LegLiftFrom = -80f, LegLiftFull = 45f, LegLift = 150f, LegLiftWidth = .11f;
const float ThighRadius = .085f;
// start bone, end bone, radius in rig metres. Thighs only: with shin capsules LegPose also lifts the hem over a raised
// foot and holds it up in a crouch, which suits a long skirt and flipped this hip-length coat up his back.
// The pants measure 0.116 around the thigh bone; the capsule is smaller because the coat rests 11 cm from that
// bone and must not be pushed at rest.
var capsuleDefinitions = new[] { ("LeftUpLeg", "LeftLeg", ThighRadius), ("RightUpLeg", "RightLeg", ThighRadius) };
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);
if (!System.IO.File.Exists(SrcFbx)) throw new System.IO.FileNotFoundException("Missing refit_coat.py output", SrcFbx);
if (!System.IO.File.Exists(Backup)) throw new System.IO.FileNotFoundException("Back up the live coat FBX first", Backup);

System.IO.File.Copy(SrcFbx, System.IO.Path.Combine(root, Fbx), true);
AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var coatModel = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
var coatSmr = coatModel.GetComponentInChildren<SkinnedMeshRenderer>(true);
var coatMesh = coatSmr.sharedMesh;
report["coatMesh"] = coatMesh.name + " verts " + coatMesh.vertexCount + " bones " + coatSmr.bones.Length + " readable " + coatMesh.isReadable;

var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
try
{
    var body = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name != CoatName);
    var byName = new Dictionary<string, Transform>();
    foreach (var t in contents.GetComponentsInChildren<Transform>(true))
        if (!byName.ContainsKey(t.name)) byName[t.name] = t;

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
    // Bind poses are in centimetres here, so 1e-2 is 0.1 mm (the FBX round trip measured 0.0014).
    if (maxDev > 1e-2f || !coatMesh.isReadable)
    {
        System.IO.File.Copy(Backup, System.IO.Path.Combine(root, Fbx), true);
        AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        report["error"] = "the refit coat's bind poses differ from the body's, or its mesh is not readable; the old coat is back";
        return report;
    }

    // Chains: the FBX's rest transforms under Hips.
    float chainMove = 0f;
    foreach (var src in coatModel.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Coat_")).OrderBy(t => Depth(t)))
    {
        var bone = byName[src.name];
        chainMove = Mathf.Max(chainMove, (bone.localPosition - src.localPosition).magnitude);
        bone.localPosition = src.localPosition;
        bone.localRotation = src.localRotation;
        bone.localScale = src.localScale;
    }
    report["largestChainMoveLocal"] = chainMove;

    var coat = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == CoatName);
    coat.sharedMesh = coatMesh;
    coat.bones = coatSmr.bones.Select(b => byName[b.name]).ToArray();
    coat.localBounds = coatSmr.localBounds;

    int removed = 0;
    foreach (var old in contents.GetComponentsInChildren<SlopArena.Client.Entities.BoneChainSpring>(true).Where(s => s.name.StartsWith("Coat_")).ToArray())
    {
        UnityEngine.Object.DestroyImmediate(old);
        removed++;
    }
    report["boneChainSpringsRemoved"] = removed;

    var spring = coat.GetComponent<SlopArena.Client.Entities.SkirtBoneSpring>();
    if (spring == null) spring = coat.gameObject.AddComponent<SlopArena.Client.Entities.SkirtBoneSpring>();
    var settings = new SerializedObject(spring);
    settings.FindProperty("_hips").objectReferenceValue = byName["mixamorig:Hips"];
    settings.FindProperty("_modelRoot").objectReferenceValue = contents.transform;
    var keys = new[] { "FL", "BL", "BR", "FR" };  // around the ring, so neighbours are adjacent
    var chains = settings.FindProperty("_chains"); chains.arraySize = keys.Length;
    for (int c = 0; c < keys.Length; c++)
    {
        var chain = chains.GetArrayElementAtIndex(c).FindPropertyRelative("Bones"); chain.arraySize = 2;
        for (int j = 0; j < 2; j++) chain.GetArrayElementAtIndex(j).objectReferenceValue = byName["Coat_" + keys[c] + "_" + (j + 1)];
    }
    var capsules = settings.FindProperty("_capsules"); capsules.arraySize = capsuleDefinitions.Length;
    for (int i = 0; i < capsuleDefinitions.Length; i++)
    {
        var capsule = capsules.GetArrayElementAtIndex(i); var definition = capsuleDefinitions[i];
        capsule.FindPropertyRelative("Start").objectReferenceValue = byName["mixamorig:" + definition.Item1];
        capsule.FindPropertyRelative("End").objectReferenceValue = byName["mixamorig:" + definition.Item2];
        capsule.FindPropertyRelative("Radius").floatValue = definition.Item3;
    }
    settings.FindProperty("_liftModel").enumValueIndex = (int)SlopArena.Client.Entities.SkirtBoneSpring.LiftModel.LegPose;
    settings.FindProperty("_legLiftFrom").floatValue = LegLiftFrom;
    settings.FindProperty("_legLiftFull").floatValue = LegLiftFull;
    settings.FindProperty("_legLift").floatValue = LegLift;
    settings.FindProperty("_legLiftWidth").floatValue = LegLiftWidth;
    settings.FindProperty("_stiffness").floatValue = .22f;
    settings.FindProperty("_damping").floatValue = .3f;
    settings.FindProperty("_neighbourBlend").floatValue = .35f;
    settings.FindProperty("_maxHemTravel").floatValue = .1f;
    settings.FindProperty("_fabricClearance").floatValue = .02f;
    settings.FindProperty("_hang").floatValue = .8f;
    settings.FindProperty("_maxHang").floatValue = 40f;
    settings.ApplyModifiedPropertiesWithoutUndo();

    // The Clearance model reads "up" from the hips at Awake, in the prefab's own pose: it must be the bind pose's up.
    var hips = byName["mixamorig:Hips"];
    int hipsIndex = Array.FindIndex(body.bones, b => b == hips);
    var bindWorld = body.transform.localToWorldMatrix * body.sharedMesh.bindposes[hipsIndex].inverse;
    report["prefabHipsVsBindDegrees"] = Quaternion.Angle(hips.rotation, bindWorld.rotation);
    report["rootLossyScale"] = contents.transform.lossyScale.x;
    report["hipsToHeadMetres"] = (byName["mixamorig:Head"].position - hips.position).magnitude;
    report["thighBoneMetres"] = (byName["mixamorig:LeftLeg"].position - byName["mixamorig:LeftUpLeg"].position).magnitude;
    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
}
finally { PrefabUtility.UnloadPrefabContents(contents); }

var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
report["skirtSprings"] = saved.GetComponentsInChildren<SlopArena.Client.Entities.SkirtBoneSpring>(true).Length;
report["boneChainSpringsLeft"] = saved.GetComponentsInChildren<SlopArena.Client.Entities.BoneChainSpring>(true).Length;
var go = UnityEngine.Object.Instantiate(saved);
go.hideFlags = HideFlags.HideAndDontSave;
try
{
    var a = go.GetComponentInChildren<Animator>();
    if (a != null) { a.Rebind(); report["humanScale"] = a.humanScale; report["avatarValid"] = a.avatar != null && a.avatar.isValid && a.avatar.isHuman; }
}
finally { UnityEngine.Object.DestroyImmediate(go); }
return report;

int Depth(Transform t) { int d = 0; while (t.parent != null) { d++; t = t.parent; } return d; }
