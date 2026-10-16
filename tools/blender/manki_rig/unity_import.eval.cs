// Eval body: import a fresh export_unity_rig.py FBX over Manki's model and rebuild the Unity side of the rig.
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// (absolute path). Edit Src first. The Humanoid mapping and twist settings carry over from the current import.
// Importing the model reimports Manki's catalog dependency, which persists a cook (see the character docs).
const string Src = "/tmp/manki-rig-export/Manki2.fbx";
const string Fbx = "Assets/Art/Characters/manki/Models/Manki2.fbx";
const string PrefabPath = "Assets/CharacterPackages/manki/Presentation/MankiRig.prefab";
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);

// The avatar keeps the skeleton captured at its first import; after replacing the FBX, regenerate it from
// this model (empty description + auto mapping) and then re-apply the saved mapping.
var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
var saved = mi.humanDescription;
System.IO.File.Copy(Src, System.IO.Path.Combine(root, Fbx), true);
AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
mi.humanDescription = new HumanDescription();
mi.autoGenerateAvatarMappingIfUnspecified = true;
mi.SaveAndReimport();
mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
var hd = mi.humanDescription;
hd.human = saved.human;
hd.upperArmTwist = saved.upperArmTwist; hd.lowerArmTwist = saved.lowerArmTwist;
hd.upperLegTwist = saved.upperLegTwist; hd.lowerLegTwist = saved.lowerLegTwist;
hd.armStretch = saved.armStretch; hd.legStretch = saved.legStretch; hd.feetSpacing = saved.feetSpacing;
hd.hasTranslationDoF = saved.hasTranslationDoF;
mi.humanDescription = hd;
mi.SaveAndReimport();

// Skinned finger copies under <Side>FistDeform follow the mapped Humanoid finger anchors (world rotation);
// the tail chain gets the presentation spring.
var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
try
{
    var all = contents.GetComponentsInChildren<Transform>(true);
    int constraints = 0;
    foreach (var copy in all.Where(t => t.name.Contains("Skin")))
    {
        string side = copy.name.StartsWith("Left") ? "Left" : "Right";
        var src = all.First(t => t.name == "mixamorig:" + side + "Hand" + copy.name.Substring((side + "Skin").Length));
        var rc = copy.GetComponent<UnityEngine.Animations.RotationConstraint>();
        if (rc == null) rc = copy.gameObject.AddComponent<UnityEngine.Animations.RotationConstraint>();
        while (rc.sourceCount > 0) rc.RemoveSource(0);
        rc.AddSource(new UnityEngine.Animations.ConstraintSource { sourceTransform = src, weight = 1f });
        rc.rotationAtRest = copy.localEulerAngles;
        rc.rotationOffset = Vector3.zero;
        rc.weight = 1f;
        rc.locked = true;
        rc.constraintActive = true;
        constraints++;
    }
    var tail = all.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^Tail\d+$")).OrderBy(t => int.Parse(t.name.Substring(4))).ToArray();
    if (tail.Length > 0)
    {
        var spring = tail[0].parent.GetComponent<SlopArena.Client.Entities.BoneChainSpring>();
        if (spring == null) spring = tail[0].parent.gameObject.AddComponent<SlopArena.Client.Entities.BoneChainSpring>();
        spring.Configure(tail);
    }
    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
    report["constraints"] = constraints;
    report["tailBones"] = tail.Length;
}
finally { PrefabUtility.UnloadPrefabContents(contents); }

var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
go.hideFlags = HideFlags.HideAndDontSave;
try
{
    var a = go.GetComponentInChildren<Animator>(); a.Rebind();
    report["mapped"] = hd.human.Length;
    report["humanScale"] = a.humanScale; // keep equal to the previous import unless the size is meant to change
    report["avatarValid"] = a.avatar.isValid && a.avatar.isHuman;
    report["material"] = go.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial?.name;
}
finally { UnityEngine.Object.DestroyImmediate(go); }
return report;
