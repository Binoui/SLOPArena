// Eval body: fill the salamander's package with Manki's clips as temporary stand-ins (owner, 2026-10-26: Unity import
// "with a package borrowing another fighter's moves"), like Bashie's (bashie_rig/borrow_clips.eval.cs). He has no clip of
// his own yet, so his idle and run are borrowed too. Each copy goes under Animations/Borrowed/, named after Manki's
// semantic ID, owned by his package, with its animation events dropped (they call Manki's components).
// Copies are raised for his body:
// - Most borrowed clips bake the body height from the centre of mass, which recomputes it from his proportions and
//   ignores RootT.y; they switch to the authored root (Based Upon: Original). Clips without root curves get constant ones.
// - Then RootT is shifted, measured through Animancer as PlayerRenderer plays clips, until his lowest point at frame 0
//   is Sink higher than Manki's clip left it, with the hips where they were across the floor. Sink is measured on
//   Manki's idle: the amount that puts his idle's lowest point on the floor (prefab scale 1).
// Re-running overwrites the copies. It returns his lowest point at frame 0 before and after, per clip.
const string Package = "Assets/CharacterPackages/salamander";
const string Rig = Package + "/Presentation/SalamanderRig.prefab";
float SinkMetres = float.NaN;
var own = new Dictionary<string, string>();
foreach (var folder in new[] { Package + "/Animations", Package + "/Animations/Borrowed" })
    if (!AssetDatabase.IsValidFolder(folder))
        AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Rig);
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var mesh = new Mesh();
// Her lowest skinned point and hips position at time t, on a fresh actor so no Animancer state outlives a clip edit.
(float lowest, Vector3 hips, float humanScale) Sample(AnimationClip clip, float t)
{
    var actor = (GameObject)UnityEngine.Object.Instantiate(prefab);
    try
    {
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor, scene);
        actor.hideFlags = HideFlags.HideAndDontSave;
        actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var animator = actor.GetComponentInChildren<Animator>(true);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var animancer = actor.AddComponent<Animancer.AnimancerComponent>();
        animancer.Animator = animator;
        var state = animancer.Play(clip, 0f); state.Speed = 0f; state.Time = t; animancer.Evaluate(0f);
        // his lowest point: body (boots) and jeans
        float lo = float.MaxValue;
        foreach (var part in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.EndsWith("_Body") || s.name.EndsWith("_Jeans")))
        {
            part.BakeMesh(mesh, true);
            var m = part.transform.localToWorldMatrix;
            foreach (var v in mesh.vertices) lo = Mathf.Min(lo, m.MultiplyPoint3x4(v).y);
        }
        return (lo, animator.GetBoneTransform(HumanBodyBones.Hips).position, animator.humanScale);
    }
    finally { UnityEngine.Object.DestroyImmediate(actor); }
}
AnimationCurve Flat(float value, float length) => new AnimationCurve(new Keyframe(0f, value), new Keyframe(Mathf.Max(length, 1f / 60f), value));
EditorCurveBinding Root(string axis) => EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT." + axis);

var report = new Dictionary<string, object>();
try
{
    var copied = new Dictionary<string, string>();
    foreach (var pair in own)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(pair.Value) != null) AssetDatabase.DeleteAsset(pair.Value);
        if (!AssetDatabase.CopyAsset(pair.Key, pair.Value)) throw new InvalidOperationException("Could not copy " + pair.Key);
        copied[pair.Key] = pair.Value;
    }
    report["own"] = copied;

    var manki = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>("Assets/CharacterPackages/manki/CharacterAssetCatalog.asset");
    var bySource = new Dictionary<AnimationClip, string>();
    var borrowed = new Dictionary<string, object>();
    var order = manki.Bindings.Where(b => b != null).OrderBy(b => b.SemanticId == "anim.manki.idle" ? 0 : 1).ToList();
    foreach (var binding in order)
    {
        var source = binding.Clip ?? throw new InvalidOperationException("Manki's catalog has no clip for " + binding.SemanticId);
        if (bySource.TryGetValue(source, out var shared))
        {
            borrowed[binding.SemanticId] = new Dictionary<string, object> { ["copy"] = shared, ["sameClipAs"] = "an earlier ID" };
            continue;
        }
        string name = binding.SemanticId.Replace("anim.manki.", "");
        string path = Package + "/Animations/Borrowed/" + name + ".anim";
        var copy = UnityEngine.Object.Instantiate(source);
        copy.name = name;
        int events = AnimationUtility.GetAnimationEvents(copy).Length;
        AnimationUtility.SetAnimationEvents(copy, new AnimationEvent[0]);
        var before = Sample(copy, 0f);
        float hs = before.humanScale;
        if (float.IsNaN(SinkMetres)) SinkMetres = -before.lowest;     // Manki's idle comes first: stand it on the floor
        float target = before.lowest + SinkMetres;
        // Bake the height from the authored root (Based Upon: Original): the centre-of-mass basis most borrowed clips
        // use recomputes it from his proportions and ignores RootT.y. Clips without root curves get constant ones.
        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        string method = settings.keepOriginalPositionY ? "original basis" : "basis centre of mass -> original";
        settings.keepOriginalPositionY = true;
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        if (AnimationUtility.GetEditorCurve(copy, Root("y")) == null)
        {
            foreach (var axis in new[] { "x", "y", "z" }) AnimationUtility.SetEditorCurve(copy, Root(axis), Flat(axis == "y" ? 1f : 0f, copy.length));
            method += ", root curves added";
        }
        // The lowest point then moves 1:1 with RootT.y x humanScale: shift the curves so it ends SinkMetres higher than
        // Manki's clip left it, and put the hips back where they were across the floor.
        var probe = Sample(copy, 0f);
        var shift = new Vector3(before.hips.x - probe.hips.x, target - probe.lowest, before.hips.z - probe.hips.z) / hs;
        foreach (var (axis, delta) in new[] { ("x", shift.x), ("y", shift.y), ("z", shift.z) })
        {
            var curve = AnimationUtility.GetEditorCurve(copy, Root(axis));
            if (curve == null || Mathf.Abs(delta) < 1e-6f) continue;
            var keys = curve.keys;
            for (int k = 0; k < keys.Length; k++) keys[k].value += delta;
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(copy, Root(axis), curve);
        }
        var after = Sample(copy, 0f);
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(copy, path);
        bySource[source] = path;
        borrowed[binding.SemanticId] = new Dictionary<string, object>
        {
            ["source"] = AssetDatabase.GetAssetPath(source) + " (" + source.name + ")", ["copy"] = path, ["method"] = method,
            ["lowestAt0Before"] = Math.Round(before.lowest, 3), ["lowestAt0After"] = Math.Round(after.lowest, 3), ["target"] = Math.Round(target, 3),
            ["hipsShift"] = (after.hips - before.hips).ToString("F3"),
            ["seconds"] = Math.Round(copy.length, 3), ["loop"] = AnimationUtility.GetAnimationClipSettings(copy).loopTime, ["droppedEvents"] = events,
        };
    }
    report["borrowed"] = borrowed;
    report["sinkMetres"] = Math.Round(SinkMetres, 3);
}
finally
{
    UnityEngine.Object.DestroyImmediate(mesh);
    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
}
return report;
