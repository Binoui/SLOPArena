// Read-only Edit Mode diagnostic: renders every Character Package fighter side by side in idle frame 0,
// exactly as the runtime places a grounded fighter, and reports their heights.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/character_sizes/lineup.eval.cs"
// Output: tmp/character-sizes/<time>/lineup-front.png (orthographic, 0.25 m lines) and lineup-threequarter.png.
// Placement mirrors PlayerRenderer: rig root scale = visualScale, root at floor (y = 0) + CapsuleHeight/2 +
// modelYOffset, idle evaluated through Playables without root motion or foot IK (Animancer's defaults).
// "lowest" is the lowest skinned vertex above the floor: positive floats, negative sinks.
string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", ".."));
string outDir = System.IO.Path.Combine(repo, "tmp", "character-sizes", System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
System.IO.Directory.CreateDirectory(outDir);
var catalogs = AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages" })
    .Select(g => AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(g)))
    .Where(c => c != null && c.Rig != null).OrderBy(c => c.PackageId, System.StringComparer.Ordinal).ToList();
System.Func<string, string, float> number = (text, key) => float.Parse(System.Text.RegularExpressions.Regex.Match(
    text, "\"" + key + "\"\\s*:\\s*(-?[0-9.eE+-]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
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
const float Spacing = 1.45f;
const int Width = 1800, Height = 900;
var rows = new List<object>();
var spawned = new List<GameObject>();
var graphs = new List<UnityEngine.Playables.PlayableGraph>();
var utility = new PreviewRenderUtility(true);
Material lineMaterial = null;
try
{
    for (int i = 0; i < catalogs.Count; i++)
    {
        var catalog = catalogs[i];
        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "CharacterPackages", catalog.PackageId, "character.json"));
        float visualScale = number(json, "visualScale"), modelYOffset = number(json, "modelYOffset");
        float capsuleHeight = number(json, "capsuleHeight"), hipHeight = number(json, "hipHeight");
        string idleId = System.Text.RegularExpressions.Regex.Match(json, "\"idle\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
        var idle = catalog.Bindings.First(b => b.SemanticId == idleId).Clip;
        float x = (i - (catalogs.Count - 1) * 0.5f) * Spacing;
        var go = UnityEngine.Object.Instantiate(catalog.Rig);
        go.hideFlags = HideFlags.HideAndDontSave;
        spawned.Add(go);
        // Fighters face +Z and the front camera looks from +Z, so screen-left is world +X.
        go.transform.SetPositionAndRotation(new Vector3(-x, capsuleHeight * 0.5f + modelYOffset, 0f), Quaternion.identity);
        go.transform.localScale = Vector3.one * visualScale;
        var animator = go.GetComponent<Animator>();
        animator.applyRootMotion = false;
        // Unseen rigs skip evaluation under CullUpdateTransforms; this instance is never saved.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        var graph = UnityEngine.Playables.PlayableGraph.Create("character-size-lineup");
        graphs.Add(graph);
        graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
        var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
        var clip = UnityEngine.Animations.AnimationClipPlayable.Create(graph, idle);
        clip.SetApplyFootIK(false);
        UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, clip);
        graph.Play();
        graph.Evaluate(0f);
        var e = extent(go);
        rows.Add(new Dictionary<string, object>
        {
            ["package"] = catalog.PackageId, ["visualScale"] = visualScale, ["modelYOffset"] = modelYOffset, ["hipHeight"] = hipHeight,
            ["lowest"] = System.Math.Round(e[0], 3), ["top"] = System.Math.Round(e[1], 3),
            ["head"] = System.Math.Round(animator.GetBoneTransform(HumanBodyBones.Head).position.y, 3),
            ["hips"] = System.Math.Round(animator.GetBoneTransform(HumanBodyBones.Hips).position.y, 3),
        });
        utility.AddSingleGO(go);
        if (catalog.WeaponConfig != null)
            foreach (var entry in catalog.WeaponConfig.Entries.Where(w => w != null && w.AttackSlot == 0 && w.Prefab != null))
            {
                var bone = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == entry.BoneName);
                if (bone == null) continue;
                var weapon = UnityEngine.Object.Instantiate(entry.Prefab);
                weapon.hideFlags = HideFlags.HideAndDontSave;
                spawned.Add(weapon);
                weapon.transform.SetPositionAndRotation(bone.TransformPoint(entry.PositionOffset), bone.rotation * Quaternion.Euler(entry.RotationOffset));
                utility.AddSingleGO(weapon);
            }
    }

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
    float width = Spacing * catalogs.Count + 1f;
    foreach (var (name, yaw, orthographic) in new[] { ("front", 0f, true), ("threequarter", 30f, false) })
    {
        var direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        camera.orthographic = orthographic;
        camera.orthographicSize = Mathf.Max(1.6f, width / 2f / (Width / (float)Height));
        camera.fieldOfView = 22f;
        camera.transform.position = new Vector3(0f, orthographic ? 1.25f : 2.45f, 0f) + direction * (orthographic ? 20f : 13.5f);
        camera.transform.LookAt(new Vector3(0f, orthographic ? 1.25f : 1f, 0f));
        utility.BeginPreview(new Rect(0, 0, Width, Height), GUIStyle.none);
        if (orthographic)
            for (int step = 1; step <= 11; step++)
                utility.DrawMesh(cube, Matrix4x4.TRS(new Vector3(0f, step * 0.25f, -0.9f), Quaternion.identity,
                    new Vector3(width, step % 2 == 0 ? 0.012f : 0.005f, 0.01f)), lineMaterial, 0);
        utility.DrawMesh(cube, Matrix4x4.TRS(new Vector3(0f, -0.02f, 0f), Quaternion.identity, new Vector3(width, 0.04f, 2.5f)), lineMaterial, 0);
        camera.Render();
        var texture = utility.EndPreview();
        var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = (RenderTexture)texture;
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, "lineup-" + name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
finally
{
    foreach (var graph in graphs) if (graph.IsValid()) graph.Destroy();
    foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
    if (lineMaterial != null) UnityEngine.Object.DestroyImmediate(lineMaterial);
    utility.Cleanup();
}
return new Dictionary<string, object> { ["output"] = outDir, ["fighters"] = rows };
