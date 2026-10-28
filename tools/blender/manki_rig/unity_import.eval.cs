// Eval body: import a fresh export_unity_rig.py FBX over Manki's model and rebuild the Unity side of the rig.
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// (absolute path). Edit Src first. The Humanoid mapping and twist settings carry over from the current import.
// Importing the model reimports Manki's catalog dependency, which persists a cook (see the character docs).
const string Src = "/tmp/manki-rig-export/Manki2.fbx";
const string SrcTexture = "/tmp/manki-rig-export/textures/Manki2_BaseColor.png";  // face_paint.py output (2048)
const string Fbx = "Assets/Art/Characters/manki/Models/Manki2.fbx";
const string Texture = "Assets/Art/Characters/manki/Textures/Manki2_BaseColor.png";
const string PrefabPath = "Assets/CharacterPackages/manki/Presentation/MankiRig.prefab";
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);

// Colour map first (same GUID, so Manki2_Body.mat keeps its reference). The default platform used to cap it
// at 1024, which would silently halve the repainted face.
System.IO.File.Copy(SrcTexture, System.IO.Path.Combine(root, Texture), true);
AssetDatabase.ImportAsset(Texture, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var ti = (TextureImporter)AssetImporter.GetAtPath(Texture);
if (ti.maxTextureSize < 2048)
{
    ti.maxTextureSize = 2048;
    ti.SaveAndReimport();
}
var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Texture);
report["texture"] = tex.width + "x" + tex.height + " " + tex.format;

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
// Keep only mappings whose bone exists in the new skeleton (a Mixamo rig with fewer finger chains, like v5's
// first index-only download, would otherwise leave dangling finger bones and an invalid avatar).
var bones = new HashSet<string>(hd.skeleton.Select(s => s.name));
var kept = saved.human.Where(h => bones.Contains(h.boneName)).ToList();
report["droppedMappings"] = saved.human.Length - kept.Count;
// Finger bones the saved mapping lacks (it lost them while v5's first rig had only index chains) are mapped by their
// Mixamo names. Unity's auto-mapping does not write back into the importer, so it cannot fill them.
var mappedHuman = new HashSet<string>(kept.Select(h => h.humanName));
var added = new List<HumanBone>();
foreach (var side in new[] { "Left", "Right" })
    foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring" })
    {
        var segments = new[] { "Proximal", "Intermediate", "Distal" };
        for (int k = 0; k < 3; k++)
        {
            string human = side + " " + finger + " " + segments[k], bone = "mixamorig:" + side + "Hand" + finger + (k + 1);
            if (mappedHuman.Contains(human) || !bones.Contains(bone)) continue;
            var hb = new HumanBone { humanName = human, boneName = bone };
            hb.limit.useDefaultValues = true;
            added.Add(hb);
        }
    }
kept.AddRange(added);
hd.human = kept.ToArray();
report["fingerMappingsAdded"] = string.Join(",", added.Select(h => h.humanName));
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
