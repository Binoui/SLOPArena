// Eval body: import Sir Bonk's hobby horse (a Tripo prop) and render size options on the live Sir Bonk.
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// Edit SrcFbx/SrcTexture first. Writes sir_bonk_hobby_horse.fbx, SirBonkHobbyHorse_BaseColor.png and
// SirBonkHobbyHorse.mat (the plank's shader); nothing the live Bonk uses changes.
// The horse is found from its mesh: the bare stick is the longest thin run of a width profile along the main
// axis, and the head is the end reaching further past it. The model was generated upright, so its up axis gives
// the head's up. Each option rides it like a hobby horse: the stick through his crotch (between the thigh joints,
// a little lower), sloping up toward his front at the model's own stick angle (so the head stays level), at a
// fixed length. The match camera orbits behind him at 15 degrees, so the behind views are what his player sees.
// Output: tmp/bonk-kit/horse/unity-review/<time>/<size>_<clip>_<view>.png, the measured horse, and first-<view>.png:
// transparent first frames for tools/genra/video_ref.py (`studio` puts them on its studio floor).
const string SrcFbx = "/home/binoui/Documents/projects/SlopArena/tmp/bonk-kit/horse/tripo-v1/tripo_convert_3bbbcd4f-4e19-4143-ab4c-e678f1efbdb2.fbx";
const string SrcTexture = "/home/binoui/Documents/projects/SlopArena/tmp/bonk-kit/horse/tripo-v1/tripo_convert_3bbbcd4f-4e19-4143-ab4c-e678f1efbdb2.fbm/tripo_rgb_e8810d39-32f2-40d2-804a-8c8ce0d700d0.png";
const string Fbx = "Assets/Art/Characters/bonk/Models/sir_bonk_hobby_horse.fbx";
const string Tex = "Assets/Art/Characters/bonk/Textures/SirBonkHobbyHorse_BaseColor.png";
const string Mat = "Assets/Art/Characters/bonk/Materials/SirBonkHobbyHorse.mat";
const string PlankMat = "Assets/Art/Characters/bonk/Materials/SirBonkPlank.mat";
// horse length along the stick (game metres) and how far he tips the stick toward his left (degrees), which
// shows the head beside his left shoulder from behind
var variants = new[] { (1.3f, 0f), (1.6f, 0f), (1.9f, 0f), (1.6f, 20f), (1.9f, 20f) };
const float SaddleAlongStick = 0.3f;                       // crotch position on the bare stick, from the broom end
const float CrotchBelowThighs = 0.08f;
// First frames for video references: the horse he rides in them, and the views (yaw from his front, positive toward
// his right; pitch up; lead = share of the frame width left ahead of him).
const float FirstFrameLength = 1.9f, FirstFrameLean = 0f;
var FirstFrameViews = new[] { ("profile-left", -90f, 7f, 0.08f), ("front-three-quarter-left", -35f, 15f, 0f) };
// The prefab and the Joust rig-pack config (section 5).
const bool WritePrefab = true;
const string PrefabPath = "Assets/Art/Characters/bonk/Prefabs/sir_bonk_hobby_horse.prefab";
const string JoustPackConfig = "Assets/Art/Characters/bonk/SirBonkJoustWeaponAttachConfig.asset";
const float GripBelowHead = 0.03f;   // his left fist on the bare stick, this share of the length below the head
var shots = new[] { ("anim.idle", 0f), ("anim.run", 0.25f) };
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);
string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "..", ".."));
string outDir = System.IO.Path.Combine(repo, "tmp", "bonk-kit", "horse", "unity-review", System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
System.IO.Directory.CreateDirectory(outDir);

// 1. Assets.
foreach (var dir in new[] { "Models", "Textures", "Materials" })
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Assets/Art/Characters/bonk", dir));
System.IO.File.Copy(SrcTexture, System.IO.Path.Combine(root, Tex), true);
AssetDatabase.ImportAsset(Tex, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var ti = (TextureImporter)AssetImporter.GetAtPath(Tex);
ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.maxTextureSize = 2048;
ti.textureCompression = TextureImporterCompression.Compressed;
ti.SaveAndReimport();
System.IO.File.Copy(SrcFbx, System.IO.Path.Combine(root, Fbx), true);
AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
mi.animationType = ModelImporterAnimationType.None;
mi.importAnimation = false;
mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false;
mi.materialImportMode = ModelImporterMaterialImportMode.None;
mi.isReadable = true;
mi.importNormals = ModelImporterNormals.Import;
mi.SaveAndReimport();
var mesh = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Mesh>().First();
var mat = AssetDatabase.LoadAssetAtPath<Material>(Mat);
if (mat == null) { mat = new Material(AssetDatabase.LoadAssetAtPath<Material>(PlankMat)) { name = "SirBonkHobbyHorse" }; AssetDatabase.CreateAsset(mat, Mat); }
var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex);
if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);

// 2. The horse's axes in mesh space.
var verts = mesh.vertices;
Vector3 mean = verts.Aggregate(Vector3.zero, (a, v) => a + v) / verts.Length;
var cov = new float[3, 3];
foreach (var v in verts) { var q = v - mean; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) cov[i, j] += q[i] * q[j]; }
System.Func<Vector3, Vector3> mul = x => new Vector3(
    cov[0, 0] * x.x + cov[0, 1] * x.y + cov[0, 2] * x.z, cov[1, 0] * x.x + cov[1, 1] * x.y + cov[1, 2] * x.z,
    cov[2, 0] * x.x + cov[2, 1] * x.y + cov[2, 2] * x.z);
var axis = new Vector3(0.6f, 0.5f, 0.4f).normalized;
for (int k = 0; k < 80; k++) axis = mul(axis).normalized;
const int Bins = 40;
System.Func<Vector3, (float lo, float hi, Vector3[] centre, float[] radius, int[] count)> profile = d =>
{
    float lo = verts.Min(v => Vector3.Dot(v, d)), hi = verts.Max(v => Vector3.Dot(v, d));
    var sum = new Vector3[Bins]; var count = new int[Bins];
    foreach (var v in verts) { int b = Mathf.Clamp((int)((Vector3.Dot(v, d) - lo) / (hi - lo) * Bins), 0, Bins - 1); sum[b] += v; count[b]++; }
    var centre = new Vector3[Bins]; var radius = new float[Bins];
    for (int b = 0; b < Bins; b++) centre[b] = count[b] > 0 ? sum[b] / count[b] : Vector3.zero;
    foreach (var v in verts)
    {
        int b = Mathf.Clamp((int)((Vector3.Dot(v, d) - lo) / (hi - lo) * Bins), 0, Bins - 1);
        var off = v - centre[b]; off -= d * Vector3.Dot(off, d);
        radius[b] = Mathf.Max(radius[b], off.magnitude);
    }
    return (lo, hi, centre, radius, count);
};
// the bare stick: the longest run of thin (or empty) bins; refine the axis through its centres
System.Func<(float lo, float hi, Vector3[] centre, float[] radius, int[] count), (int, int)> thinRun = pr =>
{
    float thin = 0.05f * (pr.hi - pr.lo);
    int bestA = 0, bestB = -1, a = -1;
    for (int b = 0; b <= Bins; b++)
    {
        bool isThin = b < Bins && (pr.count[b] == 0 || pr.radius[b] < thin);
        if (isThin && a < 0) a = b;
        if (!isThin && a >= 0) { if (b - 1 - a > bestB - bestA) { bestA = a; bestB = b - 1; } a = -1; }
    }
    return (bestA, bestB);
};
for (int pass = 0; pass < 3; pass++)
{
    var pr = profile(axis);
    var (ra, rb) = thinRun(pr);
    var filled = Enumerable.Range(ra, rb - ra + 1).Where(b => pr.count[b] > 0).ToArray();
    var refined = (pr.centre[filled.Last()] - pr.centre[filled.First()]).normalized;
    if (Vector3.Dot(refined, axis) < 0f) refined = -refined;
    axis = refined;
}
var prof = profile(axis);
var (runFirst, runLast) = thinRun(prof);
(int first, int last) stickRun = (runFirst, runLast);
float binLength = (prof.hi - prof.lo) / Bins;
float stick0 = stickRun.first * binLength, stick1 = (stickRun.last + 1) * binLength, total = prof.hi - prof.lo;
// head = the end reaching further past the bare stick
bool headAtHigh = total - stick1 > stick0;
Vector3 dHead = headAtHigh ? axis : -axis;
float tBroom = 0f, tStick0 = headAtHigh ? stick0 : total - stick1, tStick1 = headAtHigh ? stick1 : total - stick0, tHead = total;
var stickBins = Enumerable.Range(stickRun.first, stickRun.last - stickRun.first + 1).Where(b => prof.count[b] > 0).ToArray();
Vector3 stickPoint = stickBins.Aggregate(Vector3.zero, (acc, b) => acc + prof.centre[b]) / stickBins.Length;
float lowEnd = headAtHigh ? prof.lo : -prof.hi;   // dHead coordinate of the broom end
float stickPointT = Vector3.Dot(stickPoint, dHead) - lowEnd;
System.Func<float, Vector3> onStick = t => stickPoint + dHead * (t - stickPointT);
// the head's up: the model's up, square to the stick
Vector3 headUp = (Vector3.up - dHead * Vector3.Dot(dHead, Vector3.up)).normalized;
Vector3 side = Vector3.Cross(dHead, headUp);
report["horseMesh"] = new Dictionary<string, object> {
    ["boundsSize"] = new[] { mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z }, ["verts"] = verts.Length,
    ["stickDirectionToHead"] = new[] { dHead.x, dHead.y, dHead.z }, ["side"] = new[] { side.x, side.y, side.z },
    ["stickDegreesAboveHorizontal"] = Mathf.Asin(Mathf.Clamp(dHead.y, -1f, 1f)) * Mathf.Rad2Deg,
    ["lengthAlongStick"] = total, ["bareStick"] = new[] { tStick0 / total, tStick1 / total } };

// 3. Sir Bonk, posed like PlayerRenderer with his plank, and the horse options.
var catalog = AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages/bonk" })
    .Select(g => AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(g))).First(c => c != null);
string json = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "CharacterPackages", "bonk", "character.json"));
System.Func<string, float> number = key => float.Parse(System.Text.RegularExpressions.Regex.Match(
    json, "\"" + key + "\"\\s*:\\s*(-?[0-9.eE+-]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
float visualScale = number("visualScale"), modelYOffset = number("modelYOffset"), capsuleHeight = number("capsuleHeight");
var weaponEntry = catalog.WeaponConfig.Entries.First(w => w != null && w.AttackSlot == 0 && w.Prefab != null);
const int Width = 900, Height = 900;
var spawned = new List<GameObject>();
var frames = new List<object>();
var utility = new PreviewRenderUtility(true);
UnityEngine.Playables.PlayableGraph graph = default;
Material floorMaterial = null;
try
{
    var camera = utility.camera;
    camera.clearFlags = CameraClearFlags.Color;
    camera.backgroundColor = new Color(0.20f, 0.22f, 0.26f, 1f);
    camera.nearClipPlane = 0.1f; camera.farClipPlane = 100f;
    utility.lights[0].type = LightType.Directional; utility.lights[0].intensity = 1.25f;
    utility.lights[0].transform.rotation = Quaternion.Euler(35f, 160f, 0f);
    utility.lights[1].type = LightType.Directional; utility.lights[1].intensity = 0.6f;
    utility.lights[1].transform.rotation = Quaternion.Euler(20f, -30f, 0f);
    utility.ambientColor = new Color(0.45f, 0.45f, 0.5f);
    floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { hideFlags = HideFlags.HideAndDontSave };
    floorMaterial.SetColor(floorMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", new Color(0.36f, 0.38f, 0.42f));
    var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

    var go = UnityEngine.Object.Instantiate(catalog.Rig);
    go.hideFlags = HideFlags.HideAndDontSave; spawned.Add(go);
    go.transform.SetPositionAndRotation(new Vector3(0f, capsuleHeight * 0.5f + modelYOffset, 0f), Quaternion.identity);
    go.transform.localScale = Vector3.one * visualScale;
    var animator = go.GetComponent<Animator>();
    animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
    graph = UnityEngine.Playables.PlayableGraph.Create("sir-bonk-horse-review");
    graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
    var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
    graph.Play();
    var weapon = UnityEngine.Object.Instantiate(weaponEntry.Prefab);
    weapon.hideFlags = HideFlags.HideAndDontSave; spawned.Add(weapon);
    var horse = new GameObject("hobby_horse") { hideFlags = HideFlags.HideAndDontSave }; spawned.Add(horse);
    horse.AddComponent<MeshFilter>().sharedMesh = mesh;
    horse.AddComponent<MeshRenderer>().sharedMaterial = mat;
    utility.AddSingleGO(go); utility.AddSingleGO(weapon); utility.AddSingleGO(horse);

    System.Action<string, float> pose = (semanticId, fraction) =>
    {
        var clipAsset = catalog.Bindings.First(b => b.SemanticId == semanticId).Clip;
        var previousClip = UnityEngine.Playables.PlayableOutputExtensions.GetSourcePlayable(output);
        var clip = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clipAsset);
        clip.SetApplyFootIK(false);
        UnityEngine.Playables.PlayableExtensions.SetTime(clip, clipAsset.length * fraction);
        UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, clip);
        if (UnityEngine.Playables.PlayableExtensions.IsValid(previousClip)) graph.DestroyPlayable(previousClip);
        graph.Evaluate(0f);
        var hand = go.GetComponentsInChildren<Transform>(true).First(t => t.name == weaponEntry.BoneName);
        weapon.transform.SetPositionAndRotation(hand.TransformPoint(weaponEntry.PositionOffset), hand.rotation * Quaternion.Euler(weaponEntry.RotationOffset));
    };
    // The horse ridden on the current pose: the stick through his crotch at the model's own angle, tipped `lean`
    // degrees toward his left. Returns the crotch.
    float slopeDegrees = Mathf.Asin(Mathf.Clamp(dHead.y, -1f, 1f)) * Mathf.Rad2Deg;
    float tSaddle = tStick0 + SaddleAlongStick * (tStick1 - tStick0);
    System.Func<float, float, Vector3> placeHorse = (length, lean) =>
    {
        var thighs = (animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position + animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position) * 0.5f;
        var crotch = thighs + Vector3.down * CrotchBelowThighs;
        var forward = go.transform.forward;
        var tip = Quaternion.AngleAxis(lean, forward);
        var level = Quaternion.AngleAxis(-slopeDegrees, go.transform.right) * forward;   // head end, up front
        var stickWorld = tip * level;
        var upWorld = tip * Vector3.Cross(level, go.transform.right).normalized;
        // rotation taking (dHead, headUp, side) to (stickWorld, upWorld, ...)
        var meshBasis = Matrix4x4.identity; meshBasis.SetColumn(0, dHead); meshBasis.SetColumn(1, headUp); meshBasis.SetColumn(2, Vector3.Cross(dHead, headUp));
        var target = Matrix4x4.identity; target.SetColumn(0, stickWorld); target.SetColumn(1, upWorld); target.SetColumn(2, Vector3.Cross(stickWorld, upWorld));
        var rm = target * meshBasis.transpose;
        var rotation = Quaternion.LookRotation(rm.GetColumn(2), rm.GetColumn(1));
        float scale = length / total;
        horse.transform.localScale = Vector3.one * scale;
        horse.transform.rotation = rotation;
        horse.transform.position = crotch - rotation * (onStick(tSaddle) * scale);
        return crotch;
    };
    System.Action<string, int, int, bool> renderTo = (path, w, h, floor) =>
    {
        utility.BeginPreview(new Rect(0, 0, w, h), GUIStyle.none);
        if (floor)
            utility.DrawMesh(cube, Matrix4x4.TRS(new Vector3(0f, -0.02f, 0f), Quaternion.identity, new Vector3(6f, 0.04f, 6f)), floorMaterial, 0);
        camera.Render();
        var texture = utility.EndPreview();
        var image = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = (RenderTexture)texture;
            image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            image.Apply();
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
    };

    foreach (var (semanticId, fraction) in shots)
    {
        pose(semanticId, fraction);
        foreach (var (length, lean) in variants)
        {
            var crotch = placeHorse(length, lean);
            var forward = go.transform.forward;
            var headTop = horse.transform.TransformPoint(onStick(tHead));
            var broomEnd = horse.transform.TransformPoint(onStick(tBroom));
            frames.Add(new Dictionary<string, object> { ["clip"] = semanticId, ["length"] = length, ["lean"] = lean,
                ["crotch"] = new[] { crotch.x, crotch.y, crotch.z }, ["headEndHeight"] = headTop.y, ["broomEndHeight"] = broomEnd.y,
                ["headEndAhead"] = Vector3.Dot(headTop - crotch, forward), ["broomEndBehind"] = -Vector3.Dot(broomEnd - crotch, forward),
                ["headTopOfBonk"] = animator.GetBoneTransform(HumanBodyBones.Head).position.y });
            string tag = length.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "m"
                + (lean != 0f ? "_lean" + lean.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "") + "_" + semanticId.Replace("anim.", "");
            foreach (var (view, yaw, pitch, orthographic) in new[] { ("side", 90f, 0f, true), ("front34", 35f, 8f, false),
                         ("behind", 180f, 15f, false), ("behind34", 145f, 15f, false) })
            {
                var direction = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
                camera.orthographic = orthographic;
                camera.orthographicSize = 1.35f;
                bool behind = view.StartsWith("behind");
                camera.fieldOfView = orthographic ? 24f : (behind ? 30f : 24f);
                var look = new Vector3(0f, 1.15f, 0.35f);
                camera.transform.position = look + direction * (orthographic ? 20f : (behind ? 8f : 7.5f));
                camera.transform.LookAt(look);
                renderTo(System.IO.Path.Combine(outDir, tag + "_" + view + ".png"), Width, Height, true);
            }
        }
    }

    // 4. First frames for video references (tools/genra/video_ref.py): the chosen horse on his idle, 1280x720
    //    orthographic views on a transparent background, framed on his body, plank and horse with `lead` of the width
    //    of room ahead of him. `video_ref.py studio` puts them on the studio floor.
    pose("anim.idle", 0f);
    placeHorse(FirstFrameLength, FirstFrameLean);
    var firstFrames = new List<string>();
    // framing on the posed vertices (renderer bounds are loose): his skinned meshes, the plank and the horse
    var points = new List<Vector3>();
    var baked = new Mesh();
    foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(r => r.enabled && r.sharedMesh != null))
    {
        skin.BakeMesh(baked, true);
        var m = skin.transform.localToWorldMatrix;
        var v = baked.vertices;
        for (int i = 0; i < v.Length; i += 5) points.Add(m.MultiplyPoint3x4(v[i]));
    }
    UnityEngine.Object.DestroyImmediate(baked);
    foreach (var filter in weapon.GetComponentsInChildren<MeshFilter>().Concat(horse.GetComponentsInChildren<MeshFilter>()))
    {
        var m = filter.transform.localToWorldMatrix;
        var v = filter.sharedMesh.vertices;
        for (int i = 0; i < v.Length; i += 5) points.Add(m.MultiplyPoint3x4(v[i]));
    }
    var centre = points.Aggregate(Vector3.zero, (a, q) => a + q) / points.Count;
    foreach (var (view, yaw, pitch, lead) in FirstFrameViews)
    {
        var direction = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;   // from him toward the camera
        var rotation = Quaternion.LookRotation(-direction, Vector3.up);
        var right = rotation * Vector3.right;
        var up = rotation * Vector3.up;
        float x0 = points.Min(c => Vector3.Dot(c, right)), x1 = points.Max(c => Vector3.Dot(c, right));
        float y0 = points.Min(c => Vector3.Dot(c, up)), y1 = points.Max(c => Vector3.Dot(c, up));
        const float Aspect = 1280f / 720f;
        float size = Mathf.Max((y1 - y0) / 0.6f / 2f, (x1 - x0) / 0.7f / 2f / Aspect);
        camera.orthographic = true;
        camera.orthographicSize = size;
        camera.transform.rotation = rotation;
        camera.transform.position = right * ((x0 + x1) / 2f - lead * 2f * size * Aspect) + up * ((y0 + y1) / 2f + 0.04f * size)
            + direction * (Vector3.Dot(centre, direction) + 20f);
        var oldBackground = camera.backgroundColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        string path = System.IO.Path.Combine(outDir, "first-" + view + ".png");
        renderTo(path, 1280, 720, false);
        camera.backgroundColor = oldBackground;
        firstFrames.Add(path);
    }
    report["firstFrames"] = firstFrames;

    // 5. The prefab and the Joust rig-pack config. sir_bonk_hobby_horse.prefab: the root at the saddle (where his
    //    crotch rides the stick), +Z along the stick toward the head, +Y the head's up, unit scale (WeaponAttach keeps
    //    props at world scale); the mesh at FirstFrameLength; anchors horseGrip (his left fist on the bare stick just
    //    under the head), horseMuzzle (the muzzle tip) and horseTail (the broom end). SirBonkJoustWeaponAttachConfig:
    //    the live plank entry plus the horse on mixamorig:Hips as it rides on his idle, both always visible, for the
    //    `sir-joust` GenRA pack only. The game never reads it: the live config gets the horse on slot R with R's clips.
    if (WritePrefab)
    {
        float s = FirstFrameLength / total;
        var toRoot = Quaternion.Inverse(Quaternion.LookRotation(dHead, headUp));   // dHead -> +Z, headUp -> +Y
        var meshOffset = -(toRoot * (onStick(tSaddle) * s));
        System.Func<Vector3, Vector3> rootPoint = v => toRoot * (v * s) + meshOffset;
        var muzzle = verts.Where(v => Vector3.Dot(v, dHead) - lowEnd > tStick1)
            .OrderByDescending(v => Vector3.Dot(v, new Vector3(dHead.x, 0f, dHead.z).normalized)).First();
        var anchors = new[] { ("horseGrip", rootPoint(onStick(tStick1 - GripBelowHead * total))), ("horseMuzzle", rootPoint(muzzle)),
                              ("horseTail", rootPoint(onStick(tBroom))) };
        var prefabScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            var rootGo = new GameObject("sir_bonk_hobby_horse");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootGo, prefabScene);
            var body = new GameObject("horse");
            body.transform.SetParent(rootGo.transform, false);
            body.transform.localPosition = meshOffset;
            body.transform.localRotation = toRoot;
            body.transform.localScale = Vector3.one * s;
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = mat;
            foreach (var (name, point) in anchors)
            {
                var anchor = new GameObject(name);
                anchor.transform.SetParent(rootGo.transform, false);
                anchor.transform.localPosition = point;
            }
            PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(prefabScene); }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        // where the root rides on his idle: the saddle at his crotch, the stick at the model's angle, not tipped
        pose("anim.idle", 0f);
        var crotch = placeHorse(FirstFrameLength, 0f);
        var rootRotation = horse.transform.rotation * Quaternion.Inverse(toRoot);
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var positionOffset = hips.InverseTransformPoint(crotch);
        var rotationOffset = (Quaternion.Inverse(hips.rotation) * rootRotation).eulerAngles;
        // the attach placement must reproduce the mesh this pass rendered
        var placedRotation = hips.rotation * Quaternion.Euler(rotationOffset);
        var placedMesh = hips.TransformPoint(positionOffset) + placedRotation * meshOffset;
        float placementError = (placedMesh - horse.transform.position).magnitude
            + Quaternion.Angle(placedRotation * toRoot, horse.transform.rotation) * Mathf.Deg2Rad;

        var config = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Entities.WeaponAttachConfig>(JoustPackConfig);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<SlopArena.Client.Entities.WeaponAttachConfig>();
            AssetDatabase.CreateAsset(config, JoustPackConfig);
        }
        var plank = JsonUtility.FromJson<SlopArena.Client.Entities.WeaponEntry>(JsonUtility.ToJson(weaponEntry));
        plank.Prefab = weaponEntry.Prefab;
        plank.TrailStylePrefab = weaponEntry.TrailStylePrefab;
        config.Entries = new[]
        {
            plank,
            new SlopArena.Client.Entities.WeaponEntry
            {
                AttackSlot = 0, BoneName = hips.name, Prefab = prefab,
                PositionOffset = positionOffset, RotationOffset = rotationOffset,
                TrailHiltAnchor = "horseGrip", TrailTipAnchor = "horseMuzzle",
            },
        };
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);
        report["prefab"] = new Dictionary<string, object> {
            ["path"] = PrefabPath, ["lengthMetres"] = FirstFrameLength, ["meshScale"] = s,
            ["anchors"] = anchors.ToDictionary(a => a.Item1, a => (object)new[] { a.Item2.x, a.Item2.y, a.Item2.z }),
            ["joustPackConfig"] = JoustPackConfig, ["hipsBone"] = hips.name,
            ["positionOffset"] = new[] { positionOffset.x, positionOffset.y, positionOffset.z },
            ["rotationOffset"] = new[] { rotationOffset.x, rotationOffset.y, rotationOffset.z },
            ["placementError"] = placementError };
    }
}
finally
{
    if (graph.IsValid()) graph.Destroy();
    foreach (var s in spawned) if (s != null) UnityEngine.Object.DestroyImmediate(s);
    if (floorMaterial != null) UnityEngine.Object.DestroyImmediate(floorMaterial);
    utility.Cleanup();
}
report["output"] = outDir;
report["frames"] = frames;
return report;
