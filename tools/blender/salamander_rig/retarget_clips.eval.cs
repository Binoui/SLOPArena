// Eval body: re-express the generic Hat, Mustache, Straw and Tail1-5 curves of Dusty's own clips for the Tripo v2 rest.
// Run after install_v2.eval.cs: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/salamander_rig/retarget_clips.eval.cs" --timeout 600000
// His clips carry those unmapped bones as local transform curves holding the live rig's rest values. Each bone now gets
// the live bone's rotation change relative to Head or Hips (the chain's Humanoid parent) applied to its own rest, and
// its position change added to its own rest position. Hat and Mustache keep the live rest rotations, so their rotation
// curves come out unchanged and their position curves move by the new pivot; Straw and the tail fit the new geometry.
// The rig a clip was last written for is found from its curves: the first key of every generic position curve is
// compared with each known rest (RestFile, the poncho rig the clips were authored on, and every rig install_v2.eval.cs
// replaced since, in RestDir). The best match is the "from" rig; a clip that already matches the new rig is skipped, so
// rerunning changes nothing, and a clip that matches none is reported and left alone. Chaining is exact: the 1.25x size
// pass re-expressed the v2 curves on the scaled rig. Humanoid muscle curves, events and clip settings are untouched;
// Borrowed/ (Manki's paths) is left alone.
const string RestFile = "/home/binoui/Documents/projects/SlopArena/artifacts/salamander-model-v2/old_rest_locals.txt";
const string RestDir = "/home/binoui/Documents/projects/SlopArena/artifacts/salamander-model-v2/rests";
const string PrefabPath = "Assets/CharacterPackages/salamander/Presentation/SalamanderRig.prefab";
const string AnimDir = "Assets/CharacterPackages/salamander/Animations";
string[][] Chains = { new[] { "Hat" }, new[] { "Mustache" }, new[] { "Straw" }, new[] { "Tail1", "Tail2", "Tail3", "Tail4", "Tail5" } };
var report = new Dictionary<string, object>();
var inv = System.Globalization.CultureInfo.InvariantCulture;

Transform FindDeep(Transform t, string n)
{
    if (t.name == n) return t;
    foreach (Transform c in t) { var r = FindDeep(c, n); if (r != null) return r; }
    return null;
}

var rests = new List<(string name, Dictionary<string, Vector3> pos, Dictionary<string, Quaternion> rot)>();
void LoadRests(string file)
{
    var pos = new Dictionary<string, Vector3>();
    var rot = new Dictionary<string, Quaternion>();
    foreach (var line in System.IO.File.ReadAllLines(file))
    {
        if (line.StartsWith("#") || line.Trim().Length == 0) continue;
        var f = line.Split(' ');
        var v = f.Skip(1).Select(x => float.Parse(x, inv)).ToArray();
        pos[f[0]] = new Vector3(v[0], v[1], v[2]);
        rot[f[0]] = new Quaternion(v[3], v[4], v[5], v[6]);
    }
    if (!rests.Any(r => r.pos.All(kv => pos.ContainsKey(kv.Key) && (pos[kv.Key] - kv.Value).magnitude < 1e-6f)))
        rests.Add((System.IO.Path.GetFileName(file), pos, rot));
}
LoadRests(RestFile);
if (System.IO.Directory.Exists(RestDir))
    foreach (var file in System.IO.Directory.GetFiles(RestDir, "*.txt").OrderBy(x => x, StringComparer.Ordinal)) LoadRests(file);
report["knownRests"] = rests.Select(r => r.name).ToArray();
var newPos = new Dictionary<string, Vector3>();
var newRot = new Dictionary<string, Quaternion>();
var paths = new Dictionary<string, string>();
var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
go.hideFlags = HideFlags.HideAndDontSave;
try
{
    foreach (var chain in Chains)
        foreach (var b in chain)
        {
            var t = FindDeep(go.transform, b) ?? throw new InvalidOperationException("New rig lacks " + b);
            newPos[b] = t.localPosition; newRot[b] = t.localRotation;
            paths[b] = AnimationUtility.CalculateTransformPath(t, go.transform);
        }
}
finally { UnityEngine.Object.DestroyImmediate(go); }
report["paths"] = paths;
report["tail2Rest"] = rests.ToDictionary(r => r.name, r => r.pos["Tail2"].y);
report["tail2RestNew"] = newPos["Tail2"].y;

EditorCurveBinding B(string path, string prop) => EditorCurveBinding.FloatCurve(path, typeof(Transform), prop);
string[] RotProps = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
string[] PosProps = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };

AnimationCurve Smooth(float[] times, float[] values)
{
    var keys = new Keyframe[times.Length];
    for (int i = 0; i < times.Length; i++)
    {
        int a = Math.Max(0, i - 1), b = Math.Min(times.Length - 1, i + 1);
        float slope = b > a ? (values[b] - values[a]) / (times[b] - times[a]) : 0f;
        keys[i] = new Keyframe(times[i], values[i], slope, slope);
    }
    return new AnimationCurve(keys);
}

var clips = new Dictionary<string, object>();
foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimDir }))
{
    string path = AssetDatabase.GUIDToAssetPath(guid);
    if (path.Contains("/Borrowed/")) continue;
    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    var bindings = AnimationUtility.GetCurveBindings(clip);
    var have = new HashSet<string>(bindings.Where(x => x.type == typeof(Transform)).Select(x => x.path));
    if (!Chains.SelectMany(c => c).Any(b => have.Contains(paths[b]))) { clips[path] = "no generic curves"; continue; }
    int Score(Dictionary<string, Vector3> rest)
    {
        int n = 0;
        foreach (var b in Chains.SelectMany(c => c))
        {
            var pcs = PosProps.Select(p => AnimationUtility.GetEditorCurve(clip, B(paths[b], p))).ToArray();
            if (pcs.Any(x => x == null || x.length == 0)) continue;
            var first = new Vector3(pcs[0].keys[0].value, pcs[1].keys[0].value, pcs[2].keys[0].value);
            if ((first - rest[b]).magnitude < 1e-4f) n++;
        }
        return n;
    }
    int newScore = Score(newPos);
    var best = rests.Select(r => (rest: r, score: Score(r.pos))).OrderByDescending(x => x.score).First();
    if (newScore > 0 && newScore >= best.score) { clips[path] = "already retargeted"; continue; }
    if (best.score == 0) { clips[path] = "matches no known rest: left alone"; continue; }
    var oldPos = best.rest.pos;
    var oldRot = best.rest.rot;
    var times = new SortedSet<float>();
    foreach (var x in bindings)
        if (x.type == typeof(Transform) && Chains.SelectMany(c => c).Any(b => paths[b] == x.path))
            foreach (var k in AnimationUtility.GetEditorCurve(clip, x).keys) times.Add(k.time);
    var T = times.ToArray();
    float maxRotChange = 0f, maxPosChange = 0f;
    foreach (var chain in Chains)
    {
        if (!chain.All(b => have.Contains(paths[b]))) continue;
        var rc = chain.ToDictionary(b => b, b => RotProps.Select(p => AnimationUtility.GetEditorCurve(clip, B(paths[b], p))).ToArray());
        var pc = chain.ToDictionary(b => b, b => PosProps.Select(p => AnimationUtility.GetEditorCurve(clip, B(paths[b], p))).ToArray());
        var outR = chain.ToDictionary(b => b, b => new float[4][].Select(_ => new float[T.Length]).ToArray());
        var outP = chain.ToDictionary(b => b, b => new float[3][].Select(_ => new float[T.Length]).ToArray());
        var prevQ = chain.ToDictionary(b => b, b => Quaternion.identity);
        for (int i = 0; i < T.Length; i++)
        {
            Quaternion A = Quaternion.identity, A0 = Quaternion.identity, Bn = Quaternion.identity, B0 = Quaternion.identity;
            foreach (var b in chain)
            {
                var c = rc[b];
                var q = c.All(x => x != null)
                    ? new Quaternion(c[0].Evaluate(T[i]), c[1].Evaluate(T[i]), c[2].Evaluate(T[i]), c[3].Evaluate(T[i])).normalized
                    : oldRot[b];
                A = A * q; A0 = A0 * oldRot[b];
                Quaternion delta = A * Quaternion.Inverse(A0);      // the live bone's change relative to Head or Hips
                B0 = B0 * newRot[b];
                Quaternion Bk = delta * B0;
                Quaternion local = Quaternion.Inverse(Bn) * Bk;
                Bn = Bk;
                if (i > 0 && Quaternion.Dot(local, prevQ[b]) < 0) local = new Quaternion(-local.x, -local.y, -local.z, -local.w);
                prevQ[b] = local;
                outR[b][0][i] = local.x; outR[b][1][i] = local.y; outR[b][2][i] = local.z; outR[b][3][i] = local.w;
                maxRotChange = Mathf.Max(maxRotChange, Quaternion.Angle(local, q));
                var p = pc[b];
                Vector3 pos = p.All(x => x != null) ? new Vector3(p[0].Evaluate(T[i]), p[1].Evaluate(T[i]), p[2].Evaluate(T[i])) : oldPos[b];
                Vector3 np = newPos[b] + (pos - oldPos[b]);
                outP[b][0][i] = np.x; outP[b][1][i] = np.y; outP[b][2][i] = np.z;
                maxPosChange = Mathf.Max(maxPosChange, (np - pos).magnitude);
            }
        }
        foreach (var b in chain)
        {
            for (int k = 0; k < 4; k++) AnimationUtility.SetEditorCurve(clip, B(paths[b], RotProps[k]), Smooth(T, outR[b][k]));
            for (int k = 0; k < 3; k++) AnimationUtility.SetEditorCurve(clip, B(paths[b], PosProps[k]), Smooth(T, outP[b][k]));
        }
    }
    EditorUtility.SetDirty(clip);
    AssetDatabase.SaveAssetIfDirty(clip);
    clips[path] = new Dictionary<string, object> { ["from"] = best.rest.name, ["matchedBones"] = best.score, ["keys"] = T.Length, ["maxRotChangeDeg"] = maxRotChange, ["maxPosChangeM"] = maxPosChange };
}
report["clips"] = clips;
return report;
