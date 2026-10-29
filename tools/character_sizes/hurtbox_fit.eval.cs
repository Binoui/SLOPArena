// Read-only Edit Mode diagnostic: how well each fighter's hurtboxes cover its rendered mesh.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/character_sizes/hurtbox_fit.eval.cs"
// Placement mirrors grounding.eval.cs. Hurtbox points follow HitboxGeometry.BakedPointToWorld: hips + scale x (bone - hips).
// Every 4th frame of every bound clip, each skinned vertex is tagged with the region of its dominant bone and
// scored against three hurtbox sets:
//   current  the shipped hurtboxBoneDefs spheres at hurtboxBoneScale
//   unscaled the same spheres at scale 1
//   capsules a 14-capsule skeleton (head, torso, upper arm, forearm, hand, thigh, shin, foot) whose radii are the
//            85th percentile of the idle frame 0 vertex distances to each segment
// Per region: area-weighted coverage (share of surface inside a volume) and the gap of uncovered surface (cm).
// "extra" collects vertices whose dominant bone is not a Mixamo body bone (dress, tail, coat, shell springs).
var catalogs = AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages" })
    .Select(g => AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(g)))
    .Where(c => c != null && c.Rig != null).OrderBy(c => c.PackageId, System.StringComparer.Ordinal).ToList();
var inv = System.Globalization.CultureInfo.InvariantCulture;
System.Func<string, string, float> number = (text, key) => float.Parse(System.Text.RegularExpressions.Regex.Match(
    text, "\"" + key + "\"\\s*:\\s*(-?[0-9.eE+-]+)").Groups[1].Value, inv);
var boneIds = new Dictionary<string, string> {
    ["bone.head"] = "Head", ["bone.spine2"] = "Spine2", ["bone.hips"] = "Hips",
    ["bone.right-hand"] = "RightHand", ["bone.left-hand"] = "LeftHand", ["bone.right-foot"] = "RightFoot", ["bone.left-foot"] = "LeftFoot" };
System.Func<string, string> regionOf = name =>
{
    if (!name.StartsWith("mixamorig:")) return "extra";
    string n = name.Substring(10);
    if (n.StartsWith("Head") || n.Contains("Eye") || n == "Neck") return "head";
    if (n.StartsWith("Hips") || n.StartsWith("Spine") || n.EndsWith("Shoulder")) return "torso";
    if (n.EndsWith("ForeArm")) return "forearm";
    if (n.EndsWith("Arm")) return "upperarm";
    if (n.Contains("Hand")) return "hand";
    if (n.EndsWith("UpLeg")) return "thigh";
    if (n.EndsWith("Leg")) return "shin";
    if (n.Contains("Foot") || n.Contains("Toe")) return "foot";
    return "extra";
};
System.Func<Vector3, Vector3, Vector3, float> segDist = (p, a, b) =>
{
    var ab = b - a; float len2 = ab.sqrMagnitude;
    float t = len2 < 1e-10f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
    return (p - (a + ab * t)).magnitude;
};
string[] regions = { "head", "torso", "upperarm", "forearm", "hand", "thigh", "shin", "foot", "extra" };
string[] sets = { "current", "unscaled", "capsules" };
var report = new List<object>();
foreach (var catalog in catalogs)
{
    string json = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "CharacterPackages", catalog.PackageId, "character.json"));
    float visualScale = number(json, "visualScale"), modelYOffset = number(json, "modelYOffset"), capsuleHeight = number(json, "capsuleHeight");
    float boneScale = number(json, "hurtboxBoneScale");
    var defs = new List<(string bone, float r)>();
    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json,
        "\"boneId\"\\s*:\\s*\"(bone\\.[a-z0-9-]+)\"[^}]*?\"radius\"\\s*:\\s*([0-9.]+)"))
        if (boneIds.ContainsKey(m.Groups[1].Value)) defs.Add((boneIds[m.Groups[1].Value], float.Parse(m.Groups[2].Value, inv)));
    defs = defs.Take(7).ToList();
    var go = UnityEngine.Object.Instantiate(catalog.Rig);
    go.hideFlags = HideFlags.HideAndDontSave;
    try
    {
        go.transform.SetPositionAndRotation(new Vector3(0f, capsuleHeight * 0.5f + modelYOffset, 0f), Quaternion.identity);
        go.transform.localScale = Vector3.one * visualScale;
        var animator = go.GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        var all = go.GetComponentsInChildren<Transform>(true);
        System.Func<string, Transform> bone = n => all.FirstOrDefault(t => t.name == "mixamorig:" + n);
        var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(s => s.enabled && s.sharedMesh != null).ToArray();
        // Per skin: vertex region and surface area share (a third of each adjacent triangle).
        var vRegion = new List<string[]>(); var vArea = new List<float[]>();
        var extraBones = new Dictionary<string, float>();
        foreach (var skin in skins)
        {
            var mesh = skin.sharedMesh; var w = mesh.boneWeights; var verts = mesh.vertices; var tris = mesh.triangles;
            var reg = new string[verts.Length]; var area = new float[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                int bi = w.Length > i ? w[i].boneIndex0 : -1;
                string bn = bi >= 0 && bi < skin.bones.Length && skin.bones[bi] != null ? skin.bones[bi].name : skin.rootBone != null ? skin.rootBone.name : "?";
                reg[i] = regionOf(bn);
            }
            for (int t = 0; t < tris.Length; t += 3)
            {
                float a = Vector3.Cross(verts[tris[t + 1]] - verts[tris[t]], verts[tris[t + 2]] - verts[tris[t]]).magnitude / 6f;
                area[tris[t]] += a; area[tris[t + 1]] += a; area[tris[t + 2]] += a;
            }
            for (int i = 0; i < verts.Length; i++)
                if (reg[i] == "extra")
                {
                    int bi = w.Length > i ? w[i].boneIndex0 : -1;
                    string bn = bi >= 0 && bi < skin.bones.Length && skin.bones[bi] != null ? skin.bones[bi].name : "?";
                    extraBones[bn] = (extraBones.TryGetValue(bn, out var e) ? e : 0f) + area[i];
                }
            vRegion.Add(reg); vArea.Add(area);
        }
        string[][] segBones = {
            new[] { "Head", "HeadTop_End" }, new[] { "Hips", "Neck" },
            new[] { "RightArm", "RightForeArm" }, new[] { "RightForeArm", "RightHand" }, new[] { "RightHand", "RightHandMiddle1" },
            new[] { "LeftArm", "LeftForeArm" }, new[] { "LeftForeArm", "LeftHand" }, new[] { "LeftHand", "LeftHandMiddle1" },
            new[] { "RightUpLeg", "RightLeg" }, new[] { "RightLeg", "RightFoot" }, new[] { "RightFoot", "RightToeBase" },
            new[] { "LeftUpLeg", "LeftLeg" }, new[] { "LeftLeg", "LeftFoot" }, new[] { "LeftFoot", "LeftToeBase" } };
        string[] segRegion = { "head", "torso", "upperarm", "forearm", "hand", "upperarm", "forearm", "hand", "thigh", "shin", "foot", "thigh", "shin", "foot" };
        float[] segRadius = null;
        var baked = new Mesh();
        var cover = sets.ToDictionary(s => s, s => regions.ToDictionary(r => r, r => 0.0));
        var gaps = sets.ToDictionary(s => s, s => regions.ToDictionary(r => r, r => new List<(float gap, float area)>()));
        var total = regions.ToDictionary(r => r, r => 0.0);
        var worstClips = sets.ToDictionary(s => s, s => new List<(string clip, double cov)>());
        string sanity = "";
        var bindings = catalog.Bindings.Where(b => b != null && b.Clip != null).ToList();
        var idle = bindings.FirstOrDefault(b => b.SemanticId == "idle") ?? bindings[0];
        bindings.Remove(idle); bindings.Insert(0, idle);
        foreach (var binding in bindings)
        {
            var graph = UnityEngine.Playables.PlayableGraph.Create("hurtbox-fit");
            try
            {
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
                var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, binding.Clip);
                playable.SetApplyFootIK(false);
                UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, playable);
                graph.Play();
                var clipCover = sets.ToDictionary(s => s, s => 0.0); double clipTotal = 0;
                for (int frame = 0; frame < Mathf.Max(1, Mathf.CeilToInt(binding.Clip.length * 60f)); frame += 4)
                {
                    UnityEngine.Playables.PlayableExtensions.SetTime(playable, frame / 60d);
                    graph.Evaluate(0f);
                    var pts = new List<(Vector3 p, string r, float a)>();
                    for (int s = 0; s < skins.Length; s++)
                    {
                        skins[s].BakeMesh(baked, true);
                        var m = skins[s].transform.localToWorldMatrix; var vs = baked.vertices;
                        for (int i = 0; i < vs.Length; i++) pts.Add((m.MultiplyPoint3x4(vs[i]), vRegion[s][i], vArea[s][i]));
                    }
                    var hips = bone("Hips").position;
                    System.Func<string, float, Vector3> at = (n, sc) => { var t = bone(n); return t == null ? hips : hips + (t.position - hips) * sc; };
                    var segs = new Dictionary<string, List<(Vector3 a, Vector3 b, float r)>>();
                    segs["current"] = defs.Select(d => (at(d.bone, boneScale), at(d.bone, boneScale), d.r)).ToList();
                    segs["unscaled"] = defs.Select(d => (at(d.bone, 1f), at(d.bone, 1f), d.r)).ToList();
                    var ends = segBones.Select(sb => (at(sb[0], 1f), bone(sb[1]) != null ? at(sb[1], 1f) : at(sb[0], 1f))).ToArray();
                    if (segRadius == null)
                    {
                        segRadius = new float[segBones.Length];
                        for (int k = 0; k < segBones.Length; k++)
                        {
                            var ds = pts.Where(q => q.r == segRegion[k]).Select(q => segDist(q.p, ends[k].Item1, ends[k].Item2)).Where(d => d < 0.6f).OrderBy(d => d).ToList();
                            // Left and right share a region; keep the side whose bone is nearer.
                            var sideDs = pts.Where(q => q.r == segRegion[k]).Where(q =>
                            {
                                float mine = segDist(q.p, ends[k].Item1, ends[k].Item2); float best = mine;
                                for (int o = 0; o < segBones.Length; o++) if (segRegion[o] == segRegion[k]) best = Mathf.Min(best, segDist(q.p, ends[o].Item1, ends[o].Item2));
                                return mine <= best;
                            }).Select(q => segDist(q.p, ends[k].Item1, ends[k].Item2)).OrderBy(d => d).ToList();
                            segRadius[k] = sideDs.Count > 0 ? sideDs[(int)(sideDs.Count * 0.85f)] : 0.05f;
                        }
                        float top = pts.Max(q => q.p.y);
                        sanity = $"idle top vertex {top:F2} m, Head bone {bone("Head").position.y:F2} m, Hips {hips.y:F2} m";
                    }
                    segs["capsules"] = ends.Select((e, k) => (e.Item1, e.Item2, segRadius[k])).ToList();
                    foreach (var q in pts)
                    {
                        total[q.r] += q.a; clipTotal += q.a;
                        foreach (var set in sets)
                        {
                            float gap = float.MaxValue;
                            foreach (var sg in segs[set]) gap = Mathf.Min(gap, segDist(q.p, sg.a, sg.b) - sg.r);
                            if (gap <= 0f) { cover[set][q.r] += q.a; clipCover[set] += q.a; }
                            else gaps[set][q.r].Add((gap, q.a));
                        }
                    }
                }
                foreach (var set in sets) worstClips[set].Add((binding.SemanticId, clipCover[set] / clipTotal));
            }
            finally { graph.Destroy(); }
        }
        UnityEngine.Object.DestroyImmediate(baked);
        System.Func<List<(float gap, float area)>, double, float> pct = (g, p) =>
        {
            if (g.Count == 0) return 0f; var o = g.OrderBy(x => x.gap).ToList(); double sum = o.Sum(x => (double)x.area), acc = 0;
            foreach (var x in o) { acc += x.area; if (acc >= sum * p) return x.gap * 100f; } return o[o.Count - 1].gap * 100f;
        };
        var bySet = new Dictionary<string, object>();
        foreach (var set in sets)
        {
            double cAll = regions.Sum(r => cover[set][r]), tAll = regions.Sum(r => total[r]);
            bySet[set] = new Dictionary<string, object>
            {
                ["coverage"] = Math.Round(100 * cAll / tAll, 1),
                ["regions"] = regions.Where(r => total[r] > 0).Select(r =>
                    $"{r} {(100 * cover[set][r] / total[r]):F0}% (area {(100 * total[r] / tAll):F0}%, gap p50 {pct(gaps[set][r], 0.5):F0} p90 {pct(gaps[set][r], 0.9):F0} cm)").ToList(),
                ["worstClips"] = worstClips[set].OrderBy(x => x.cov).Take(4).Select(x => $"{x.clip} {100 * x.cov:F0}%").ToList(),
            };
        }
        report.Add(new Dictionary<string, object>
        {
            ["package"] = catalog.PackageId, ["sanity"] = sanity, ["hurtboxBoneScale"] = boneScale,
            ["capsuleRadiiCm"] = string.Join(", ", segBones.Select((sb, k) => $"{sb[0]} {segRadius[k] * 100:F0}")),
            ["extraBones"] = extraBones.OrderByDescending(x => x.Value).Take(6).Select(x => x.Key).ToList(),
            ["sets"] = bySet,
        });
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
}
return report;
