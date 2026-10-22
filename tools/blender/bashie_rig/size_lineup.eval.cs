// Edit Mode eval body: an orthographic size lineup of the live roster and the Bashie candidate, each in an idle pose
// at time 0 with their lowest point on the floor, lines every 0.25 m. Roster fighters use their package visualScale
// and their own idle; Bashie wears Manki's idle at BashieScale, as preview_swap/preview_ground place her in Play Mode.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/bashie_rig/size_lineup.eval.cs"
// Output: tmp/bashie-model/lineup/<time>.png and the top/lowest heights per fighter.
const float BashieScale = 1.10f;
const string BashiePrefab = "Assets/CharacterPackages/bashie/Presentation/BashieRig.prefab";
string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", ".."));
string outDir = System.IO.Path.Combine(repo, "tmp", "bashie-model", "lineup");
System.IO.Directory.CreateDirectory(outDir);
string outPath = System.IO.Path.Combine(outDir, System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");

SlopArena.Client.Animation.CharacterAssetCatalog Catalog(string package) =>
    AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages/" + package })
        .Select(g => AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(g)))
        .First(c => c != null);
float Number(string package, string key)
{
    string json = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "CharacterPackages", package, "character.json"));
    return float.Parse(System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?[0-9.eE+-]+)").Groups[1].Value,
        System.Globalization.CultureInfo.InvariantCulture);
}
float[] Extent(GameObject go)
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
}

AnimationClip Idle(string package) => Catalog(package).Bindings
    .First(b => b.SemanticId == "anim.idle" || b.SemanticId == "anim." + package + ".idle").Clip;
var mankiIdle = Idle("manki");
var cast = new List<(string label, GameObject prefab, AnimationClip clip, float scale, float rootY, bool snap)>();
foreach (var package in new[] { "bonk", "fightguy", "wibou", "manki" })
{
    var catalog = Catalog(package);
    cast.Add((package, catalog.Rig, Idle(package), Number(package, "visualScale"),
        0f, true));
}
cast.Add(("bashie", AssetDatabase.LoadAssetAtPath<GameObject>(BashiePrefab), mankiIdle, BashieScale * Number("manki", "visualScale"), 0f, true));

const int Width = 1800, Height = 900;
const float Spacing = 1.25f;
var spawned = new List<GameObject>();
var graphs = new List<UnityEngine.Playables.PlayableGraph>();
var rows = new List<object>();
var utility = new PreviewRenderUtility(true);
Material lineMaterial = null;
try
{
    var camera = utility.camera;
    camera.clearFlags = CameraClearFlags.Color;
    camera.backgroundColor = new Color(0.86f, 0.84f, 0.80f, 1f);
    camera.nearClipPlane = 0.1f; camera.farClipPlane = 100f;
    utility.lights[0].type = LightType.Directional; utility.lights[0].intensity = 1.2f;
    utility.lights[0].transform.rotation = Quaternion.Euler(35f, 160f, 0f);
    utility.lights[1].type = LightType.Directional; utility.lights[1].intensity = 0.6f;
    utility.lights[1].transform.rotation = Quaternion.Euler(20f, -30f, 0f);
    utility.ambientColor = new Color(0.5f, 0.5f, 0.55f);
    lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { hideFlags = HideFlags.HideAndDontSave };
    lineMaterial.SetColor(lineMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", new Color(0.35f, 0.34f, 0.32f));
    var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
    for (int i = 0; i < cast.Count; i++)
    {
        var (label, prefab, clip, scale, rootY, snap) = cast[i];
        float x = (i - (cast.Count - 1) * 0.5f) * Spacing;
        var go = UnityEngine.Object.Instantiate(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        spawned.Add(go);
        go.transform.SetPositionAndRotation(new Vector3(-x, rootY, 0f), Quaternion.identity);
        go.transform.localScale = Vector3.one * scale;
        var animator = go.GetComponentInChildren<Animator>();
        animator.applyRootMotion = false;
        animator.Rebind();
        var graph = UnityEngine.Playables.PlayableGraph.Create("bashie-lineup");
        graphs.Add(graph);
        graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
        var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
        var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(false);
        UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, playable);
        graph.Evaluate(0f);
        var e = Extent(go);
        if (snap)
        {
            go.transform.position += Vector3.up * -e[0];
            graph.Evaluate(0f);
            e = Extent(go);
        }
        utility.AddSingleGO(go);
        rows.Add(new Dictionary<string, object> { ["fighter"] = label, ["top"] = System.Math.Round(e[1], 3), ["lowest"] = System.Math.Round(e[0], 3), ["scale"] = scale });
    }
    camera.orthographic = true;
    camera.orthographicSize = 1.45f;
    camera.transform.position = new Vector3(0f, 1.2f, 20f);
    camera.transform.LookAt(new Vector3(0f, 1.2f, 0f));
    utility.BeginPreview(new Rect(0, 0, Width, Height), GUIStyle.none);
    for (int step = 1; step <= 10; step++)
        utility.DrawMesh(cube, Matrix4x4.TRS(new Vector3(0f, step * 0.25f, -1.5f), Quaternion.identity,
            new Vector3(9f, step % 4 == 0 ? 0.014f : 0.006f, 0.01f)), lineMaterial, 0);
    utility.DrawMesh(cube, Matrix4x4.TRS(new Vector3(0f, -0.02f, 0f), Quaternion.identity, new Vector3(9f, 0.04f, 3f)), lineMaterial, 0);
    camera.Render();
    var texture = utility.EndPreview();
    var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
    var previous = RenderTexture.active;
    try
    {
        RenderTexture.active = (RenderTexture)texture;
        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        image.Apply();
        System.IO.File.WriteAllBytes(outPath, image.EncodeToPNG());
    }
    finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
}
finally
{
    foreach (var graph in graphs) if (graph.IsValid()) graph.Destroy();
    foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
    if (lineMaterial != null) UnityEngine.Object.DestroyImmediate(lineMaterial);
    utility.Cleanup();
}
return new Dictionary<string, object> { ["output"] = outPath, ["order"] = "left to right as listed (the camera looks back at them)", ["fighters"] = rows };
