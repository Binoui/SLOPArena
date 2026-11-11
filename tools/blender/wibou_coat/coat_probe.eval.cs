// Eval body: measures Wibou's coat against his thighs on his bound clips, with the coat spring stepped per frame.
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>" --timeout 120000
// Per clip it reports realInside (most skirt vertices more than 1 cm behind the nearest thigh vertex's surface in any
// sampled frame, with that frame and the deepest overlap), the same against a 0.116 m capsule around the thigh bone,
// the largest chain lift and thigh raise, and a per-frame `series`. Idle scores about 40 by itself (the test misfires
// near the crotch), so read the other clips against that. Nothing is saved; the prefab is posed in a preview scene.
// To try spring values without saving the prefab, write them to OverridePath as JSON, e.g. {"_legLift": 130}.
const string PrefabPath = "Assets/Resources/Characters/Wibou.prefab";
const string CatalogPath = "Assets/CharacterPackages/wibou/CharacterAssetCatalog.asset";
const float ThighRadius = 0.116f, Deep = 0.01f;
var ids = new[] { "anim.wibou.idle", "anim.wibou.run", "anim.wibou.jump", "anim.wibou.g3", "anim.wibou.r", "anim.wibou.a1", "anim.wibou.crouch" };
var catalog = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(CatalogPath));
var bindings = catalog.FindProperty("_bindings");
var clips = new Dictionary<string, AnimationClip>();
for (int i = 0; i < bindings.arraySize; i++)
{
    var b = bindings.GetArrayElementAtIndex(i);
    clips[b.FindPropertyRelative("_semanticId").stringValue] = b.FindPropertyRelative("_clip").objectReferenceValue as AnimationClip;
}
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var report = new Dictionary<string, object>();
try
{
    var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
    var animator = root.GetComponentInChildren<Animator>();
    animator.applyRootMotion = false;
    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    var spring = root.GetComponentInChildren<SlopArena.Client.Entities.SkirtBoneSpring>(true);
    const string OverridePath = "/home/binoui/Documents/projects/SlopArena/tmp/part-fit/unity/override.json";
    if (System.IO.File.Exists(OverridePath))
    {
        var o = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(OverridePath));
        var so = new SerializedObject(spring);
        foreach (var pair in o)
        {
            var prop = so.FindProperty(pair.Key);
            if (prop == null) throw new Exception("no property " + pair.Key);
            if (prop.propertyType == SerializedPropertyType.Float) prop.floatValue = (float)pair.Value;
            else if (prop.propertyType == SerializedPropertyType.Enum) prop.enumValueIndex = (int)pair.Value;
            else throw new Exception("unsupported " + pair.Key);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        report["override"] = o.ToString(Newtonsoft.Json.Formatting.None);
    }
    spring.Step(1f / 60f); spring.ResetSimulation();   // rest frame in the prefab pose, as Awake does in play
    var coat = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == "WibouCoat");
    var all = root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
    Transform hips = all["mixamorig:Hips"];
    var keys = new[] { "FL", "BL", "BR", "FR" };
    var pivots = keys.Select(k => all["Coat_" + k + "_1"]).ToArray();
    var tips = keys.Select(k => all["Coat_" + k + "_Tip"]).ToArray();
    var restDir = new Vector3[4];
    for (int c = 0; c < 4; c++) restDir[c] = hips.InverseTransformDirection((tips[c].position - pivots[c].position).normalized);
    var legs = new[] { (all["mixamorig:LeftUpLeg"], all["mixamorig:LeftLeg"]), (all["mixamorig:RightUpLeg"], all["mixamorig:RightLeg"]) };
    var baked = new Mesh();
    var verts = new List<Vector3>();
    // real surface test: coat vertices behind the nearest thigh vertex's normal
    var body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name != "WibouCoat");
    var bw = body.sharedMesh.boneWeights;
    var thighIdx = Enumerable.Range(0, bw.Length).Where(i => body.bones[bw[i].boneIndex0].name.EndsWith("UpLeg")).ToArray();
    var bodyBaked = new Mesh(); var bverts = new List<Vector3>(); var bnormals = new List<Vector3>();
    var cw = coat.sharedMesh.boneWeights;
    bool Skirt(int bone) => coat.bones[bone].name.StartsWith("Coat_") || coat.bones[bone].name.EndsWith("UpLeg");
    var skirtIdx = Enumerable.Range(0, cw.Length).Where(i => (cw[i].weight0 > 0 && Skirt(cw[i].boneIndex0)) || (cw[i].weight1 > 0 && Skirt(cw[i].boneIndex1)) || (cw[i].weight2 > 0 && Skirt(cw[i].boneIndex2))).ToArray();
    report["skirtVerts"] = skirtIdx.Length; report["thighVerts"] = thighIdx.Length;
    foreach (string id in ids)
    {
        if (!clips.TryGetValue(id, out var clip) || clip == null) { report[id] = "missing"; continue; }
        animator.Rebind();
        spring.ResetSimulation();
        var graph = UnityEngine.Playables.PlayableGraph.Create("probe");
        try
        {
            graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
            var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, playable);
            graph.Play();
            int frames = Mathf.Min(Mathf.RoundToInt(clip.length * 60f), 120);
            var series = new List<string>();
            int realWorst = 0, realFrame = 0; float realDeep = 0f; var realRows = new Dictionary<int, string>();
            int worstCount = 0, worstFrame = 0; float worstDepth = 0f, maxStep = 0f, maxLift = 0f, maxThigh = 0f; int stepFrame = 0;
            var last = new Vector3[4];
            int passes = id == "anim.wibou.idle" || id == "anim.wibou.run" ? 2 : 1;   // loops: settle one cycle first
            for (int pass = 0; pass < passes; pass++)
                for (int f = 0; f <= frames; f++)
                {
                    UnityEngine.Playables.PlayableExtensions.SetTime(playable, f / 60d);
                    graph.Evaluate(0f);
                    spring.Step(1f / 60f);
                    if (pass < passes - 1) continue;
                    coat.BakeMesh(baked, true);
                    baked.GetVertices(verts);
                    var l2w = coat.transform.localToWorldMatrix;
                    int count = 0; float deepest = 0f;
                    foreach (var v in verts)
                    {
                        Vector3 p = l2w.MultiplyPoint3x4(v);
                        foreach (var (a, b) in legs)
                        {
                            Vector3 axis = b.position - a.position;
                            float t = Mathf.Clamp01(Vector3.Dot(p - a.position, axis) / axis.sqrMagnitude);
                            float depth = ThighRadius - Vector3.Distance(p, a.position + axis * t);
                            if (depth > Deep) { count++; deepest = Mathf.Max(deepest, depth); break; }
                        }
                    }
                    if (f % 2 == 0)
                    {
                        body.BakeMesh(bodyBaked, true); bodyBaked.GetVertices(bverts); bodyBaked.GetNormals(bnormals);
                        var bl2w = body.transform.localToWorldMatrix;
                        var tp = new Vector3[thighIdx.Length]; var tn = new Vector3[thighIdx.Length];
                        for (int k = 0; k < thighIdx.Length; k++) { tp[k] = bl2w.MultiplyPoint3x4(bverts[thighIdx[k]]); tn[k] = bl2w.MultiplyVector(bnormals[thighIdx[k]]).normalized; }
                        int real = 0; float deep = 0f; var quad = new int[4];
                        foreach (int ci in skirtIdx)
                        {
                            Vector3 p = l2w.MultiplyPoint3x4(verts[ci]);
                            float best = 0.01f; int bk = -1;   // nearest thigh vertex within 10 cm
                            for (int k = 0; k < tp.Length; k++) { float d2 = (p - tp[k]).sqrMagnitude; if (d2 < best) { best = d2; bk = k; } }
                            if (bk < 0) continue;
                            float inside = -Vector3.Dot(p - tp[bk], tn[bk]);
                            if (inside > Deep) { real++; deep = Mathf.Max(deep, inside); Vector3 h = hips.InverseTransformPoint(p); quad[(h.x > 0 ? 0 : 1) + (Vector3.Dot(p - hips.position, root.transform.forward) > 0 ? 0 : 2)]++; }
                        }
                        if (real > realWorst) { realWorst = real; realFrame = f; }
                        realDeep = Mathf.Max(realDeep, deep);
                        realRows[f] = " REAL " + real + " [a " + quad[0] + " b " + quad[1] + " c " + quad[2] + " d " + quad[3] + "]";
                    }
                    if (count > worstCount) { worstCount = count; worstFrame = f; }
                    worstDepth = Mathf.Max(worstDepth, deepest);
                    Vector3 up = root.transform.up;
                    foreach (var (a, b) in legs) maxThigh = Mathf.Max(maxThigh, Vector3.Angle(b.position - a.position, -up));
                    var row = new System.Text.StringBuilder(f + ": in " + count + " hipsLean " + Vector3.Angle(hips.up, up).ToString("0") + " thighs");
                    foreach (var (a, b) in legs) row.Append(" " + Vector3.Angle(b.position - a.position, -hips.up).ToString("0"));
                    row.Append(" chains");
                    for (int c = 0; c < 4; c++) row.Append(" " + Vector3.Angle(hips.InverseTransformDirection((tips[c].position - pivots[c].position).normalized), restDir[c]).ToString("0"));
                    if (realRows.TryGetValue(f, out var rr)) row.Append(rr);
                    series.Add(row.ToString());
                    for (int c = 0; c < 4; c++)
                    {
                        Vector3 dir = hips.InverseTransformDirection((tips[c].position - pivots[c].position).normalized);
                        maxLift = Mathf.Max(maxLift, Vector3.Angle(dir, restDir[c]));
                        if (f > 0) { float step = Vector3.Angle(dir, last[c]); if (step > maxStep) { maxStep = step; stepFrame = f; } }
                        last[c] = dir;
                    }
                }
            report[id] = new { frames, realInside = realWorst, realFrame, realDeepCm = realDeep * 100f, vertsInside = worstCount, atFrame = worstFrame, deepestCm = worstDepth * 100f, maxChainLiftDeg = maxLift, maxThighRaiseDeg = maxThigh, maxOneFrameStepDeg = maxStep, stepFrame, series = series.ToArray() };
        }
        finally { graph.Destroy(); }
    }
    UnityEngine.Object.DestroyImmediate(baked);
    report["coatVerts"] = coat.sharedMesh.vertexCount;
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
return report;
