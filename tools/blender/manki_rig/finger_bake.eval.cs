// Eval body: bake Manki's authored GenRA finger poses (generic curves on the old rig's skinned finger copies)
// into Humanoid finger muscle curves of the same clips, read through a temporary Avatar that maps those copies.
// The legacy Manki avatar maps no fingers, so its playback ignores these muscles.
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>" (absolute path)
const string OldRig = "Assets/CharacterPackages/manki/Presentation/Legacy/MankiDeformRig.prefab";
string[] Clips = { "Assets/CharacterPackages/manki/Animations/manki_air3_double_fist.anim", "Assets/CharacterPackages/manki/Animations/manki_ground4_head_slam.anim" };
bool write = false; // dry run first: set true to write the curves
var report = new Dictionary<string, object>();
var oldGo = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(OldRig)); oldGo.hideFlags = HideFlags.HideAndDontSave;
try
{
    var oldAnim = oldGo.GetComponentInChildren<Animator>(); oldAnim.Rebind();
    var oldAll = oldGo.GetComponentsInChildren<Transform>(true);
    // A: samples the clip (original names, so the copy-finger curves bind). B: same rig with the skinned
    // copies renamed and a temporary Avatar that maps them as fingers; A's local pose is copied onto B.
    var bGo = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(OldRig)); bGo.hideFlags = HideFlags.HideAndDontSave;
    var aT = oldGo.GetComponentsInChildren<Transform>(true);
    var bT = bGo.GetComponentsInChildren<Transform>(true);
    bool UnderFist(Transform t) { for (var p = t.parent; p != null; p = p.parent) if (p.name.EndsWith("FistDeform")) return true; return false; }
    foreach (var t in bT) if (UnderFist(t)) t.name = t.name + "_skin";
    var oldMi = (ModelImporter)AssetImporter.GetAtPath("Assets/Art/Characters/manki/Candidates/ShoulderRepair199/Manki.fbx");
    var hdOld = oldMi.humanDescription;
    var human = hdOld.human.ToList();
    foreach (var side in new[] { "Left", "Right" })
        foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            for (int j = 1; j <= 3; j++)
            {
                var hb = new HumanBone { humanName = side + " " + finger + " " + new[] { "Proximal", "Intermediate", "Distal" }[j - 1], boneName = "mixamorig:" + side + "Hand" + finger + j + "_skin" };
                hb.limit.useDefaultValues = true;
                human.Add(hb);
            }
    var bAnim = bGo.GetComponentInChildren<Animator>(); bAnim.Rebind();
    var hdTemp = hdOld;
    hdTemp.human = human.ToArray();
    hdTemp.skeleton = bT.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
    var tempAvatar = AvatarBuilder.BuildHumanAvatar(bGo, hdTemp);
    report["tempAvatar"] = tempAvatar.isValid && tempAvatar.isHuman;
    var handler = new HumanPoseHandler(tempAvatar, bGo.transform);
    var pose = new HumanPose();
    // Map HumanTrait muscle names ("Right Index 1 Stretched") to clip attributes ("RightHand.Index.1 Stretched").
    var fingerMuscles = new List<(int index, string attribute)>();
    for (int m = 0; m < HumanTrait.MuscleCount; m++)
    {
        string n = HumanTrait.MuscleName[m];
        var parts = n.Split(' ');
        if (parts.Length < 3 || (parts[0] != "Left" && parts[0] != "Right")) continue;
        if (!new[] { "Thumb", "Index", "Middle", "Ring" }.Contains(parts[1])) continue;
        string attr = parts[0] + "Hand." + parts[1] + "." + string.Join(" ", parts.Skip(2));
        attr = attr.Replace(".Spread", ".Spread"); // "Right Index Spread" -> "RightHand.Index.Spread"
        if (parts[2] == "Spread") attr = parts[0] + "Hand." + parts[1] + ".Spread";
        fingerMuscles.Add((m, attr));
    }
    report["muscles"] = fingerMuscles.Count;
        foreach (var path in Clips)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        var existing = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Animator)).Select(b => b.propertyName).ToHashSet();
        var missing = fingerMuscles.Where(f => !existing.Contains(f.attribute)).Select(f => f.attribute).ToList();
        int frames = Mathf.RoundToInt(clip.length * clip.frameRate);
        var curves = fingerMuscles.ToDictionary(f => f.attribute, f => new List<Keyframe>());
        var before = new Dictionary<string, float>();
        for (int fr = 0; fr <= frames; fr++)
        {
            float t = Mathf.Min(fr / clip.frameRate, clip.length);
            oldAnim.Rebind();
            clip.SampleAnimation(oldGo, t);
            for (int k = 0; k < aT.Length; k++) { bT[k].localPosition = aT[k].localPosition; bT[k].localRotation = aT[k].localRotation; bT[k].localScale = aT[k].localScale; }
            handler.GetHumanPose(ref pose);
            foreach (var f in fingerMuscles)
                curves[f.attribute].Add(new Keyframe(t, pose.muscles[f.index]));
        }
        var summary = new List<string>();
        foreach (var f in fingerMuscles.Where(x => x.attribute.StartsWith("RightHand.Index") || x.attribute.StartsWith("RightHand.Thumb.1")))
        {
            var ks = curves[f.attribute];
            var b = EditorCurveBinding.FloatCurve("", typeof(Animator), f.attribute);
            var old = AnimationUtility.GetEditorCurve(clip, b);
            summary.Add(f.attribute + " new[min " + ks.Min(k => k.value).ToString("F2") + " max " + ks.Max(k => k.value).ToString("F2") + "] old@mid " + (old == null ? "none" : old.Evaluate(clip.length * 0.4f).ToString("F2")));
        }
        if (write)
        {
            foreach (var f in fingerMuscles)
            {
                var curve = new AnimationCurve(curves[f.attribute].ToArray());
                for (int k = 0; k < curve.length; k++) { AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto); }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), f.attribute), curve);
            }
            EditorUtility.SetDirty(clip);
        }
        report[System.IO.Path.GetFileNameWithoutExtension(path)] = new { frames, missing, summary };
    }
    if (write) AssetDatabase.SaveAssets();
    report["written"] = write;
    handler.Dispose();
    UnityEngine.Object.DestroyImmediate(bGo);
    UnityEngine.Object.DestroyImmediate(tempAvatar);
}
finally { UnityEngine.Object.DestroyImmediate(oldGo); }
return report;
