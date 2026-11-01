// Read-only Edit Mode review (v2, 2026-10-28): the live Sir Bonk rig and the Sir Bonk v2 candidate side by side, posed by Bonk's
// bound clips at a few times, placed like PlayerRenderer (root at CapsuleHeight/2 + modelYOffset, scale
// visualScale, no root motion or foot IK) and holding the sword. The candidate's hand frame differs from the
// old rig's, so its sword offsets are converted to keep the old T-pose grip (rotation and offset in world).
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/bonk_rig/v2_review.eval.cs"
// Output: tmp/bonk-sir/tripo-v2/unity-review/<time>/<clip>_<t>_<view>.png and per-frame lowest/top/head heights.
const string CandidatePath = "Assets/Art/Characters/bonk/Prefabs/SirBonkV2Rig.prefab";
var shots = new[] { ("anim.idle", 0f), ("anim.idle", 0.5f), ("anim.run", 0.25f), ("anim.bonk.g1", 0.4f), ("anim.bonk.g2", 0.45f), ("anim.bonk.gr-gallop", 0.3f), ("anim.bonk.ge", 0.5f), ("anim.jump", 0.5f), ("anim.hit-hard", 0.3f) };
string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", ".."));
string outDir = System.IO.Path.Combine(repo, "tmp", "bonk-sir", "tripo-v2", "unity-review", System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
System.IO.Directory.CreateDirectory(outDir);
var catalog = AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages/bonk" })
    .Select(g => AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(g))).First(c => c != null);
string json = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "CharacterPackages", "bonk", "character.json"));
System.Func<string, float> number = key => float.Parse(System.Text.RegularExpressions.Regex.Match(
    json, "\"" + key + "\"\\s*:\\s*(-?[0-9.eE+-]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
float visualScale = number("visualScale"), modelYOffset = number("modelYOffset"), capsuleHeight = number("capsuleHeight");
var rigs = new[] { ("live", catalog.Rig), ("sir", AssetDatabase.LoadAssetAtPath<GameObject>(CandidatePath)) };
if (rigs[1].Item2 == null) throw new System.IO.FileNotFoundException("Run v2_import.eval.cs first", CandidatePath);
var weaponEntry = catalog.WeaponConfig.Entries.First(w => w != null && w.AttackSlot == 0 && w.Prefab != null);

Quaternion RestHand(GameObject prefab)
{
    var go = UnityEngine.Object.Instantiate(prefab);
    go.hideFlags = HideFlags.HideAndDontSave;
    try
    {
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        return go.GetComponentsInChildren<Transform>(true).First(t => t.name == weaponEntry.BoneName).rotation;
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
}
var qOld = RestHand(rigs[0].Item2);
var qNew = RestHand(rigs[1].Item2);
var offsets = new Dictionary<string, (Vector3, Quaternion)>
{
    ["live"] = (weaponEntry.PositionOffset, Quaternion.Euler(weaponEntry.RotationOffset)),
    ["sir"] = (Quaternion.Inverse(qNew) * (qOld * weaponEntry.PositionOffset), Quaternion.Inverse(qNew) * qOld * Quaternion.Euler(weaponEntry.RotationOffset)),
};
var converted = offsets["sir"];

System.Func<GameObject, float[]> extent = go =>
{
    float lo = float.MaxValue, hi = float.MinValue;
    var baked = new Mesh();
    foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(s => s.enabled && s.sharedMesh != null))
    {
        skin.BakeMesh(baked, true);
        var m = skin.transform.localToWorldMatrix;
        foreach (var v in baked.vertices) { float y = m.MultiplyPoint3x4(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
    }
    UnityEngine.Object.DestroyImmediate(baked);
    return new[] { lo, hi };
};
const int Width = 1100, Height = 900;
const float Spacing = 2.1f;
var rows = new List<object>();
var spawned = new List<GameObject>();
var graphs = new List<UnityEngine.Playables.PlayableGraph>();
var utility = new PreviewRenderUtility(true);
var capeRest = new Dictionary<Transform, Quaternion>();
Material lineMaterial = null;
try
{
    var camera = utility.camera;
    camera.clearFlags = CameraClearFlags.Color;
    camera.backgroundColor = new Color(0.20f, 0.22f, 0.26f, 1f);
    camera.nearClipPlane = 0.1f;
    camera.farClipPlane = 100f;
    utility.lights[0].type = LightType.Directional;
    utility.lights[0].intensity = 1.25f;
    utility.lights[0].transform.rotation = Quaternion.Euler(35f, 160f, 0f);
    utility.lights[1].type = LightType.Directional;
    utility.lights[1].intensity = 0.6f;
    utility.lights[1].transform.rotation = Quaternion.Euler(20f, -30f, 0f);
    utility.ambientColor = new Color(0.45f, 0.45f, 0.5f);
    lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { hideFlags = HideFlags.HideAndDontSave };
    lineMaterial.SetColor(lineMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", new Color(0.55f, 0.58f, 0.64f));
    var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

    var animators = new List<(string, GameObject, Animator, UnityEngine.Playables.PlayableGraph, GameObject, UnityEngine.Animations.AnimationPlayableOutput)>();
    for (int i = 0; i < rigs.Length; i++)
    {
        var (label, prefab) = rigs[i];
        float x = (i - 0.5f) * Spacing;
        var go = UnityEngine.Object.Instantiate(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        spawned.Add(go);
        go.transform.SetPositionAndRotation(new Vector3(-x, capsuleHeight * 0.5f + modelYOffset, 0f), Quaternion.identity);
        go.transform.localScale = Vector3.one * visualScale;
        var animator = go.GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("Cape_")) capeRest[t] = t.localRotation;
        var graph = UnityEngine.Playables.PlayableGraph.Create("sir-bonk-review");
        graphs.Add(graph);
        graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
        var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
        graph.Play();
        var weapon = UnityEngine.Object.Instantiate(weaponEntry.Prefab);
        weapon.hideFlags = HideFlags.HideAndDontSave;
        spawned.Add(weapon);
        utility.AddSingleGO(go);
        utility.AddSingleGO(weapon);
        animators.Add((label, go, animator, graph, weapon, output));
    }

    foreach (var (semanticId, fraction) in shots)
    {
        var clipAsset = catalog.Bindings.First(b => b.SemanticId == semanticId).Clip;
        float time = clipAsset.length * fraction;
        var frame = new Dictionary<string, object> { ["clip"] = semanticId, ["time"] = time };
        foreach (var (label, go, animator, graph, weapon, output) in animators)
        {
            var previousClip = UnityEngine.Playables.PlayableOutputExtensions.GetSourcePlayable(output);
            var clip = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clipAsset);
            clip.SetApplyFootIK(false);
            UnityEngine.Playables.PlayableExtensions.SetTime(clip, time);
            UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, clip);
            if (UnityEngine.Playables.PlayableExtensions.IsValid(previousClip)) graph.DestroyPlayable(previousClip);
            graph.Evaluate(0f);
            // Bonk's clips key the cape chains in the live rig's bone frames; in Play Mode the BoneChainSpring drives
            // them. Show the cape at rest, as it will be once the clips stop keying Cape_* (needed at the v2 swap).
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Cape_") && capeRest.TryGetValue(t, out var rest)) t.localRotation = rest;
            var hand = go.GetComponentsInChildren<Transform>(true).First(t => t.name == weaponEntry.BoneName);
            var (pos, rot) = offsets[label];
            weapon.transform.SetPositionAndRotation(hand.TransformPoint(pos), hand.rotation * rot);
            var e = extent(go);
            frame[label] = new Dictionary<string, object>
            {
                ["lowest"] = System.Math.Round(e[0], 3), ["top"] = System.Math.Round(e[1], 3),
                ["head"] = System.Math.Round(animator.GetBoneTransform(HumanBodyBones.Head).position.y, 3),
                ["hips"] = System.Math.Round(animator.GetBoneTransform(HumanBodyBones.Hips).position.y, 3),
            };
        }
        string tag = semanticId.Replace("anim.", "").Replace(".", "-") + "_" + fraction.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (name, yaw, orthographic) in new[] { ("front", 0f, true), ("threequarter", 35f, false) })
        {
            var direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            camera.orthographic = orthographic;
            camera.orthographicSize = 1.9f;
            camera.fieldOfView = 24f;
            camera.transform.position = new Vector3(0f, orthographic ? 1.45f : 2.2f, 0f) + direction * (orthographic ? 20f : 12.5f);
            camera.transform.LookAt(new Vector3(0f, orthographic ? 1.45f : 1.25f, 0f));
            utility.BeginPreview(new Rect(0, 0, Width, Height), GUIStyle.none);
            if (orthographic)
                for (int step = 1; step <= 13; step++)
                    utility.DrawMesh(cube, Matrix4x4.TRS(new Vector3(0f, step * 0.25f, -1.5f), Quaternion.identity,
                        new Vector3(6f, step % 2 == 0 ? 0.012f : 0.005f, 0.01f)), lineMaterial, 0);
            utility.DrawMesh(cube, Matrix4x4.TRS(new Vector3(0f, -0.02f, 0f), Quaternion.identity, new Vector3(6f, 0.04f, 3f)), lineMaterial, 0);
            camera.Render();
            var texture = utility.EndPreview();
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = (RenderTexture)texture;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, tag + "_" + name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
        rows.Add(frame);
    }
}
finally
{
    foreach (var graph in graphs) if (graph.IsValid()) graph.Destroy();
    foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
    if (lineMaterial != null) UnityEngine.Object.DestroyImmediate(lineMaterial);
    utility.Cleanup();
}
return new Dictionary<string, object>
{
    ["output"] = outDir, ["visualScale"] = visualScale, ["modelYOffset"] = modelYOffset, ["frames"] = rows,
    ["sirSwordPositionOffset"] = new[] { converted.Item1.x, converted.Item1.y, converted.Item1.z },
    ["sirSwordRotationOffset"] = new[] { converted.Item2.eulerAngles.x, converted.Item2.eulerAngles.y, converted.Item2.eulerAngles.z },
};
