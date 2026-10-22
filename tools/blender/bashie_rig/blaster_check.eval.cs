// Edit Mode eval body: render Bashie holding her water blaster the way WeaponAttach places it (hand bone, then her
// weapon config's slot-A offsets), at rest, on her idle and on the borrowed bazooka hold, from the front and her right.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/bashie_rig/blaster_check.eval.cs"
// Output: tmp/bashie-model/blaster/check/<time>.png
const string Package = "Assets/CharacterPackages/bashie";
string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", ".."));
string outDir = System.IO.Path.Combine(repo, "tmp", "bashie-model", "blaster", "check");
System.IO.Directory.CreateDirectory(outDir);
string outPath = System.IO.Path.Combine(outDir, System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(Package + "/CharacterAssetCatalog.asset");
var entry = catalog.WeaponConfig.Entries.Single(e => e.AttackSlot == 11);
AnimationClip Clip(string id) => catalog.Bindings.Single(b => b.SemanticId == id).Clip;
var bazooka = Clip("anim.bashie.gr");
var poses = new (string label, AnimationClip clip, float time)[]
{
    ("rest", null, 0f), ("idle", Clip("anim.bashie.idle"), 0f), ("bazooka", bazooka, bazooka.length * 0.4f),
};
var yaws = new[] { 180f, 120f };  // the camera looks down -Z: 180 shows her front, 120 her right side
const int Cell = 520;
var spawned = new List<UnityEngine.Object>();
var utility = new PreviewRenderUtility(true);
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
    for (int row = 0; row < yaws.Length; row++)
        for (int col = 0; col < poses.Length; col++)
        {
            var go = (GameObject)UnityEngine.Object.Instantiate(catalog.Rig);
            go.hideFlags = HideFlags.HideAndDontSave;
            spawned.Add(go);
            float x = (col - (poses.Length - 1) * 0.5f) * 1.2f, y = (yaws.Length - 1 - row) * 1.3f;
            go.transform.SetPositionAndRotation(new Vector3(-x, y, 0f), Quaternion.Euler(0f, yaws[row], 0f));
            // Into the preview scene before posing: the move resets the Animator.
            utility.AddSingleGO(go);
            var animator = go.GetComponentInChildren<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            if (poses[col].clip != null)
            {
                // Animancer, as PlayerRenderer plays clips.
                var animancer = go.AddComponent<Animancer.AnimancerComponent>();
                animancer.Animator = animator;
                var state = animancer.Play(poses[col].clip, 0f);
                state.Speed = 0f;
                state.Time = poses[col].time;
                animancer.Evaluate(0f);
            }
            var bone = go.GetComponentsInChildren<Transform>(true).Single(t => t.name == entry.BoneName);
            var prop = (GameObject)UnityEngine.Object.Instantiate(entry.Prefab);
            prop.hideFlags = HideFlags.HideAndDontSave;
            spawned.Add(prop);
            prop.transform.SetPositionAndRotation(bone.TransformPoint(entry.PositionOffset), bone.rotation * Quaternion.Euler(entry.RotationOffset));
            utility.AddSingleGO(prop);
        }
    camera.orthographic = true;
    camera.orthographicSize = 1.3f;
    camera.transform.position = new Vector3(0f, 1.15f, -20f);
    camera.transform.LookAt(new Vector3(0f, 1.15f, 0f));
    int width = Cell * poses.Length, height = Cell * yaws.Length;
    utility.BeginPreview(new Rect(0, 0, width, height), GUIStyle.none);
    camera.Render();
    var texture = utility.EndPreview();
    var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
    var previous = RenderTexture.active;
    try
    {
        RenderTexture.active = (RenderTexture)texture;
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        System.IO.File.WriteAllBytes(outPath, image.EncodeToPNG());
    }
    finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
}
finally
{
    foreach (var o in spawned) if (o != null) UnityEngine.Object.DestroyImmediate(o);
    utility.Cleanup();
}
return new Dictionary<string, object> { ["image"] = outPath, ["bazookaSeconds"] = bazooka.length };
