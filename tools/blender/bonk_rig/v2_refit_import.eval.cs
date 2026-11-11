// Eval body: swap in refit_parts.py's Sir Bonk v2 FBX (rigid parts on helper bones, no bare legs under the pants) and
// drive the helpers. Run with (absolute path):
//   bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>" --timeout 120000
// The model keeps its import settings and Avatar description. The script proves the swap changed no existing bone's
// rest transform, bind pose or the Avatar's human scale, and puts the old FBX back if it did. Then it gives the rig
// prefab a PartialFollowBones (each helper takes its share of its driver's turn) and moves Q's shoulder lid from
// LeftArm to Pauldron_Left in the weapon config (same rest frame, so the offsets stay). Saving the prefab and the
// config recooks bonk; run sloparena.character.verify and roster.refresh afterwards.
const string Src = "/home/binoui/Documents/projects/SlopArena/tmp/part-fit/bonk-refit/SirBonk.fbx";
const string Backup = "/home/binoui/Documents/projects/SlopArena/artifacts/sir-bonk-refit-20261111/backup/sir_bonk_v2.fbx";
const string Fbx = "Assets/Art/Characters/bonk/Models/sir_bonk_v2.fbx";
const string PrefabPath = "Assets/CharacterPackages/bonk/Presentation/SirBonkV2Rig.prefab";
const string WeaponConfig = "Assets/CharacterPackages/bonk/BonkWeaponAttachConfig.asset";
// helper, driver, share of the driver's turn (refit_parts.py's table)
var follows = new (string helper, string driver, float share)[]
{
    ("KneeGuard_Left", "mixamorig:LeftLeg", 0.5f), ("KneeGuard_Right", "mixamorig:RightLeg", 0.5f),
    ("ElbowCap_Right", "mixamorig:RightForeArm", 0.5f),
    ("Pauldron_Left", "mixamorig:LeftArm", 0.3f), ("Pauldron_Right", "mixamorig:RightArm", 0.3f),
};
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);
if (!System.IO.File.Exists(Src)) throw new System.IO.FileNotFoundException("Missing refit_parts.py output", Src);
if (!System.IO.File.Exists(Backup)) throw new System.IO.FileNotFoundException("Back up the live FBX first", Backup);

Dictionary<string, (Vector3 p, Quaternion r, Vector3 s)> Rest()
{
    var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
    return model.GetComponentsInChildren<Transform>(true).Where(t => t != model.transform)
        .GroupBy(t => t.name).ToDictionary(g => g.Key, g => (g.First().localPosition, g.First().localRotation, g.First().localScale));
}
Dictionary<string, Matrix4x4> Binds()
{
    var smr = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx).GetComponentInChildren<SkinnedMeshRenderer>(true);
    var d = new Dictionary<string, Matrix4x4>();
    for (int i = 0; i < smr.bones.Length; i++) d[smr.bones[i].name] = smr.sharedMesh.bindposes[i];
    return d;
}
(float humanScale, bool valid, int vertices, int bones, int springs) Probe()
{
    var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
    go.hideFlags = HideFlags.HideAndDontSave;
    try
    {
        var a = go.GetComponentInChildren<Animator>();
        a.Rebind();
        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
        return (a.humanScale, a.avatar != null && a.avatar.isValid && a.avatar.isHuman, smr.sharedMesh.vertexCount, smr.bones.Length,
            go.GetComponentsInChildren<SlopArena.Client.Entities.BoneChainSpring>(true).Length);
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
}

var restBefore = Rest();
var bindBefore = Binds();
var before = Probe();
bool alreadyRefit = restBefore.ContainsKey("KneeGuard_Left");
report["before"] = new { before.humanScale, before.valid, before.vertices, before.bones, before.springs, alreadyRefit };

System.IO.File.Copy(Src, System.IO.Path.Combine(root, Fbx), true);
AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var restAfter = Rest();
var bindAfter = Binds();
var after = Probe();
float worstPosition = 0f, worstAngle = 0f, worstScale = 0f, worstBind = 0f;
var missing = new List<string>();
foreach (var pair in restBefore)
{
    if (!restAfter.TryGetValue(pair.Key, out var now)) { missing.Add(pair.Key); continue; }
    worstPosition = Mathf.Max(worstPosition, (now.p - pair.Value.p).magnitude);
    worstAngle = Mathf.Max(worstAngle, Quaternion.Angle(now.r, pair.Value.r));
    worstScale = Mathf.Max(worstScale, (now.s - pair.Value.s).magnitude);
}
foreach (var pair in bindBefore)
{
    if (!bindAfter.TryGetValue(pair.Key, out var now)) { missing.Add("bind:" + pair.Key); continue; }
    for (int k = 0; k < 16; k++) worstBind = Mathf.Max(worstBind, Mathf.Abs(now[k] - pair.Value[k]));
}
var helpersInSkin = follows.Where(f => bindAfter.ContainsKey(f.helper)).Select(f => f.helper).ToArray();
report["after"] = new { after.humanScale, after.valid, after.vertices, after.bones, after.springs };
report["restDeviation"] = new { position = worstPosition, degrees = worstAngle, scale = worstScale, bindpose = worstBind, missing = missing.ToArray() };
report["helpersInSkin"] = helpersInSkin;
// Bind poses are in centimetres here (entries up to ~70), so 1e-2 is 0.1 mm.
bool same = missing.Count == 0 && worstPosition < 1e-4f && worstAngle < 0.01f && worstScale < 1e-5f && worstBind < 1e-2f
    && after.valid && Mathf.Abs(after.humanScale - before.humanScale) < 1e-5f && after.springs == before.springs
    && helpersInSkin.Length == follows.Length;
if (!same)
{
    System.IO.File.Copy(Backup, System.IO.Path.Combine(root, Fbx), true);
    AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    report["error"] = "the refit changed the rest pose, the Avatar or the skin bones; the old FBX is back";
    return report;
}

// Helpers: rest = the driver's bind-pose local rotation (the helper's own is the same).
var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
try
{
    var byName = contents.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
    var component = contents.GetComponent<SlopArena.Client.Entities.PartialFollowBones>();
    if (component == null) component = contents.AddComponent<SlopArena.Client.Entities.PartialFollowBones>();
    var so = new SerializedObject(component);
    var list = so.FindProperty("_follows");
    list.arraySize = follows.Length;
    float worstFrame = 0f;
    for (int i = 0; i < follows.Length; i++)
    {
        var helper = byName[follows[i].helper]; var driver = byName[follows[i].driver];
        if (helper.parent != driver.parent) throw new InvalidOperationException(follows[i].helper + " is not a sibling of " + follows[i].driver);
        worstFrame = Mathf.Max(worstFrame, Quaternion.Angle(helper.localRotation, driver.localRotation) + (helper.localPosition - driver.localPosition).magnitude);
        var e = list.GetArrayElementAtIndex(i);
        e.FindPropertyRelative("Helper").objectReferenceValue = helper;
        e.FindPropertyRelative("Driver").objectReferenceValue = driver;
        e.FindPropertyRelative("Share").floatValue = follows[i].share;
        e.FindPropertyRelative("Rest").quaternionValue = driver.localRotation;
    }
    so.ApplyModifiedPropertiesWithoutUndo();
    report["helperFrameDeviation"] = worstFrame;
    if (worstFrame > 0.01f) throw new InvalidOperationException("A helper's rest frame differs from its driver's.");
    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
}
finally { PrefabUtility.UnloadPrefabContents(contents); }

// Q's shoulder lid rides the left pauldron helper. The helper has LeftArm's rest frame, so the offsets carry over.
var config = AssetDatabase.LoadMainAssetAtPath(WeaponConfig);
var cso = new SerializedObject(config);
var entries = cso.FindProperty("Entries");
int moved = 0;
for (int i = 0; i < entries.arraySize; i++)
{
    var bone = entries.GetArrayElementAtIndex(i).FindPropertyRelative("BoneName");
    if (bone.stringValue == "mixamorig:LeftArm") { bone.stringValue = "Pauldron_Left"; moved++; }
}
cso.ApplyModifiedPropertiesWithoutUndo();
EditorUtility.SetDirty(config);
AssetDatabase.SaveAssetIfDirty(config);
report["lidEntriesMoved"] = moved;

var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
report["followers"] = saved.GetComponentsInChildren<SlopArena.Client.Entities.PartialFollowBones>(true).Length;
report["springsSaved"] = saved.GetComponentsInChildren<SlopArena.Client.Entities.BoneChainSpring>(true).Length;
return report;
