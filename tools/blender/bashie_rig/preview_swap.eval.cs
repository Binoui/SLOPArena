// Play Mode eval body: presents the Manki fighter of a live session with Bashie's candidate rig, in memory only
// (stopping Play Mode discards it). Manki's simulation, kit, clips and props stay; Humanoid retargeting plays his
// clips on her. She gets a fixed scale (a little smaller than Manki), her feet go on the capsule bottom, and the
// Game View gets a fixed even size (the session restore puts the saved one back).
// - CPU showcase (sloparena.showcase.prepare, editor_play, showcase.run --ticks 1, this, showcase.record): adds a
//   temporary follow camera over the film camera.
// - Tutorial (sloparena.tutorial.open --fighter manki, editor_play, tutorial.camera, this, photo.live-begin,
//   tutorial.run, photo.live-end): hides the HUD so the captured frames show only the game.
const string PrefabPath = "Assets/CharacterPackages/bashie/Presentation/BashieRig.prefab";
// "A little bit smaller than Manki": on his idle in size_lineup.eval.cs she tops out at 1.10 m against his 1.16 m.
// The first model needed 1.15 for that; the redesigned one (2026-10-22, bucket hat) is 4.5% taller, so 1.10.
// A height measured live is unreliable: Manki may be mid-crouch. preview_ground.eval.cs then puts her feet down.
const float ModelScale = 1.10f;
if (!Application.isPlaying) throw new InvalidOperationException("Run inside the showcase's Play Mode.");
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var defField = typeof(SlopArena.Client.Entities.PlayerRenderer).GetField("_charDef", flags);
var renderer = UnityEngine.Object.FindObjectsByType<SlopArena.Client.Entities.PlayerRenderer>(FindObjectsSortMode.None)
    .FirstOrDefault(r => (defField.GetValue(r) as SlopArena.Shared.CharacterDefinition)?.Class == SlopArena.Shared.CharacterClass.Manki)
    ?? throw new InvalidOperationException("No Manki fighter in this session.");
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
    ?? throw new InvalidOperationException("Import Bashie first (unity_import.eval.cs).");
var def = (SlopArena.Shared.CharacterDefinition)defField.GetValue(renderer);

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

var before = Extent(renderer.gameObject);
float mankiHeight = before[1] - before[0];
var animationCatalog = (SlopArena.Client.Animation.CharacterAnimationCatalog)typeof(SlopArena.Client.Entities.PlayerRenderer)
    .GetField("_animationCatalog", flags).GetValue(renderer);
if (!renderer.Configure(def, renderer.BakedData, animationCatalog, prefab, out var configurationError))
    throw new InvalidOperationException(configurationError);
// Play Mode destroys Manki's old model at the end of the frame, so measure Bashie's own instance only.
var animator = renderer.GetComponentsInChildren<Animator>().FirstOrDefault(a => a.gameObject.name != "Manki" && a.avatar != null && a.avatar.name.StartsWith("bashie"))
    ?? renderer.GetComponentsInChildren<Animator>().Last();
if (animator == null || !animator.isHuman) throw new InvalidOperationException("Bashie's rig did not load as Humanoid.");
var model = animator.transform;
float scale = ModelScale * def.VisualScale;
model.localScale = Vector3.one * scale;
typeof(SlopArena.Client.Entities.PlayerRenderer).GetField("_modelVisualScale", flags).SetValue(renderer, scale);
var after = Extent(model.gameObject);

// The recorder needs even viewport dimensions: pick a fixed even Game View size (restore puts the saved one back).
var gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
var gv = Resources.FindObjectsOfTypeAll(gvType).OfType<EditorWindow>().First();
var sizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
var sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
var group = sizesType.GetProperty("currentGroup").GetValue(sizes);
int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
int pick = -1; string picked = null;
foreach (var (w, h) in new[] { (1920, 1080), (1280, 720), (1600, 900) })
{
    for (int i = 0; i < count && pick < 0; i++)
    {
        var size = group.GetType().GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
        var t = size.GetType();
        if ((int)t.GetProperty("width").GetValue(size) == w && (int)t.GetProperty("height").GetValue(size) == h
            && t.GetProperty("sizeType").GetValue(size).ToString() == "FixedResolution")
        { pick = i; picked = (string)t.GetProperty("baseText").GetValue(size) + $" {w}x{h}"; }
    }
    if (pick >= 0) break;
}
if (pick < 0) throw new InvalidOperationException("No fixed 1920x1080, 1280x720 or 1600x900 Game View size exists.");
var callback = gvType.GetMethod("SizeSelectionCallback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
callback.Invoke(gv, callback.GetParameters().Select(p => p.ParameterType == typeof(int) ? (object)pick : null).ToArray());
gv.Repaint();
float yOffsetBefore = renderer.ModelYOffset;
// First guess: her lowest point on the capsule bottom; preview_ground.eval.cs corrects it once the pose settles.
float floorY = renderer.transform.position.y - yOffsetBefore - renderer.CapsuleHeight * 0.5f;
renderer.ModelYOffset = yOffsetBefore + (floorY - after[0]);

bool showcase = GameObject.Find("CPU Showcase (temporary)") != null;
int hiddenHud = 0;
if (!showcase)
{
    foreach (var document in UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsSortMode.None))
        if (document.enabled) { document.enabled = false; hiddenHud++; }
}
// Showcase close-up: a temporary camera drawn over the film camera, following her fighter's root with
// built-in constraints only (no script). Stopping Play Mode removes it.
var film = showcase ? Camera.main : null;
if (film != null) {
var camGo = new GameObject("Bashie Follow Camera (temporary)");
var cam = camGo.AddComponent<Camera>();
cam.CopyFrom(film);
cam.depth = film.depth + 10;
cam.fieldOfView = 34f;
var follow = camGo.AddComponent<UnityEngine.Animations.PositionConstraint>();
follow.AddSource(new UnityEngine.Animations.ConstraintSource { sourceTransform = renderer.transform, weight = 1f });
follow.translationOffset = new Vector3(1.7f, 0.75f, -2.6f);
follow.locked = true;
follow.constraintActive = true;
var look = camGo.AddComponent<UnityEngine.Animations.LookAtConstraint>();
look.AddSource(new UnityEngine.Animations.ConstraintSource { sourceTransform = renderer.transform, weight = 1f });
look.locked = true;
look.constraintActive = true;
}
return new Dictionary<string, object>
{
    ["mankiHeight"] = mankiHeight, ["bashieHeight"] = after[1] - after[0], ["ratio"] = (after[1] - after[0]) / mankiHeight,
    ["modelScale"] = scale, ["defVisualScale"] = def.VisualScale,
    ["modelYOffsetBefore"] = yOffsetBefore, ["modelYOffsetAfter"] = renderer.ModelYOffset,
    ["humanScale"] = animator.humanScale, ["gameViewSize"] = picked, ["session"] = showcase ? "showcase" : "other", ["hiddenHud"] = hiddenHud,
};
