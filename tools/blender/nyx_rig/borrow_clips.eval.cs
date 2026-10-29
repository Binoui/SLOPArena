const string Package = "Assets/CharacterPackages/nyx";
const string Rig = Package + "/Presentation/NyxRig.prefab";
float SinkMetres = float.NaN;
foreach (var folder in new[] { Package + "/Animations", Package + "/Animations/Borrowed" })
    if (!AssetDatabase.IsValidFolder(folder))
        AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Rig);
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var mesh = new Mesh();
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
        float lo = float.MaxValue;
        foreach (var part in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name == "Nyx_Real"))
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
    var fightguy = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>("Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset");
    var bySource = new Dictionary<AnimationClip, string>();
    var borrowed = new Dictionary<string, object>();
    var order = fightguy.Bindings.Where(b => b != null).OrderBy(b => b.SemanticId == "anim.idle" ? 0 : 1).ToList();
    foreach (var binding in order)
    {
        var source = binding.Clip ?? throw new InvalidOperationException("FightGuy's catalog has no clip for " + binding.SemanticId);
        if (bySource.TryGetValue(source, out var shared))
        {
            borrowed[binding.SemanticId] = new Dictionary<string, object> { ["copy"] = shared, ["sameClipAs"] = "an earlier ID" };
            continue;
        }
        string name = binding.SemanticId.Replace("anim.fightguy.", "").Replace("anim.", "");
        string path = Package + "/Animations/Borrowed/" + name + ".anim";
        var copy = UnityEngine.Object.Instantiate(source);
        copy.name = name;
        int events = AnimationUtility.GetAnimationEvents(copy).Length;
        AnimationUtility.SetAnimationEvents(copy, new AnimationEvent[0]);
        var before = Sample(copy, 0f);
        float hs = before.humanScale;
        if (float.IsNaN(SinkMetres)) SinkMetres = -before.lowest;     // FightGuy's idle comes first: stand it on the floor
        float target = before.lowest + SinkMetres;
        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        string method = settings.keepOriginalPositionY ? "original basis" : "basis centre of mass -> original";
        settings.keepOriginalPositionY = true;
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        if (AnimationUtility.GetEditorCurve(copy, Root("y")) == null)
        {
            foreach (var axis in new[] { "x", "y", "z" }) AnimationUtility.SetEditorCurve(copy, Root(axis), Flat(axis == "y" ? 1f : 0f, copy.length));
            method += ", root curves added";
        }
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
        var previous = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (previous != null) { EditorUtility.CopySerialized(copy, previous); EditorUtility.SetDirty(previous); AssetDatabase.SaveAssetIfDirty(previous); UnityEngine.Object.DestroyImmediate(copy); }
        else AssetDatabase.CreateAsset(copy, path);
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
