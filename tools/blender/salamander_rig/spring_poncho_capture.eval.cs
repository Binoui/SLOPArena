// Eval body template; spring_poncho_review.py fills @OUT@, @TAKE@ and @VIEW@ and runs one copy per take and view.
// Read-only Edit Mode capture of Dusty's live rig (rigid poncho) and the spring-poncho candidate, both placed like
// PlayerRenderer (root at capsuleHeight/2 + modelYOffset, scale visualScale, no root motion or foot IK) and driven by
// the same 60 fps timeline: <take>.timeline lines are "x y z yawDeg clip time weight [clip time weight]" (clip ids are
// his catalog's anim.salamander.<id>, or copter for the GenRA candidate). The spring only runs in Play Mode or with an
// explicit Step, so the candidate's is stepped once per frame after the pose; its first Step runs at the rest pose.
// Skinned renderers only re-skin on Editor ticks, never inside one eval, so every frame draws freshly baked meshes.
// Writes <out>/<take>/<view>/<rig>/f0000.jpg and metrics.json: per frame, the furthest hem vertex from where Spine2
// would carry its rest position, each chain's lift angle, and the deepest poncho vertex inside each limb capsule
// beyond its root joint (game metres; a rough proxy: the draped fabric rests inside these radii even when rigid).
const string Out = "@OUT@";
const string Take = "@TAKE@";
const string View = "@VIEW@";
const string CandidatePath = "Assets/CharacterPackages/salamander/Presentation/SalamanderRig_SpringPoncho.prefab";
const string CopterPath = "Assets/Art/Experiments/GenRA/salamander/salamander-ge-sombrero-copter-overhead.anim";
const int Width = 640, Height = 720;
const float Separation = 40f;
// Limbs for measurement: rig-metre skin radii (chains.json medians) and where along the bone measurement starts.
var limbs = new[] {
    ("thigh.L", "mixamorig:LeftUpLeg", "mixamorig:LeftLeg", .065f, .35f), ("thigh.R", "mixamorig:RightUpLeg", "mixamorig:RightLeg", .065f, .35f),
    ("shin.L", "mixamorig:LeftLeg", "mixamorig:LeftFoot", .043f, 0f), ("shin.R", "mixamorig:RightLeg", "mixamorig:RightFoot", .043f, 0f),
    ("upperarm.L", "mixamorig:LeftArm", "mixamorig:LeftForeArm", .04f, .3f), ("upperarm.R", "mixamorig:RightArm", "mixamorig:RightForeArm", .04f, .3f),
    ("forearm.L", "mixamorig:LeftForeArm", "mixamorig:LeftHand", .027f, 0f), ("forearm.R", "mixamorig:RightForeArm", "mixamorig:RightHand", .027f, 0f),
    ("hand.L", "mixamorig:LeftHand", "mixamorig:LeftHandMiddle2", .025f, 0f), ("hand.R", "mixamorig:RightHand", "mixamorig:RightHandMiddle2", .025f, 0f),
    ("tail", "Tail1", "Tail2", .064f, .3f) };
string json = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "CharacterPackages", "salamander", "character.json"));
System.Func<string, float> number = key => float.Parse(System.Text.RegularExpressions.Regex.Match(
    json, "\"" + key + "\"\\s*:\\s*(-?[0-9.eE+-]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
float visualScale = number("visualScale"), modelYOffset = number("modelYOffset"), capsuleHeight = number("capsuleHeight");
var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>("Assets/CharacterPackages/salamander/CharacterAssetCatalog.asset");
var frames = System.IO.File.ReadAllLines(System.IO.Path.Combine(Out, Take + ".timeline")).Where(l => l.Trim().Length > 0)
    .Select(l => l.Split(' ')).ToArray();
float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
var clipIds = frames.SelectMany(f => Enumerable.Range(0, (f.Length - 4) / 3).Select(i => f[4 + 3 * i])).Distinct().ToArray();
var clips = clipIds.ToDictionary(id => id, id => id == "copter" ? AssetDatabase.LoadAssetAtPath<AnimationClip>(CopterPath)
    : catalog.Bindings.First(b => b.SemanticId == "anim.salamander." + id).Clip);
string viewDir = System.IO.Path.Combine(Out, Take, View);
var rigs = new[] { ("rigid", catalog.Rig, -Separation), ("spring", AssetDatabase.LoadAssetAtPath<GameObject>(CandidatePath), Separation) };
var spawned = new List<GameObject>();
var graphs = new List<UnityEngine.Playables.PlayableGraph>();
var utility = new PreviewRenderUtility(true);
Material lineMaterial = null, groundMaterial = null;
var metrics = new List<object>();
try
{
    var camera = utility.camera;
    camera.clearFlags = CameraClearFlags.Color;
    camera.backgroundColor = new Color(0.20f, 0.22f, 0.26f, 1f);
    camera.nearClipPlane = 0.1f;
    camera.farClipPlane = 60f;
    utility.lights[0].type = LightType.Directional;
    utility.lights[0].intensity = 1.25f;
    utility.lights[0].transform.rotation = Quaternion.Euler(40f, 150f, 0f);
    utility.lights[1].type = LightType.Directional;
    utility.lights[1].intensity = 0.7f;
    utility.lights[1].transform.rotation = Quaternion.Euler(25f, -40f, 0f);
    utility.ambientColor = new Color(0.45f, 0.45f, 0.5f);
    var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
    lineMaterial = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
    lineMaterial.SetColor(lineMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", new Color(0.62f, 0.64f, 0.70f));
    groundMaterial = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
    groundMaterial.SetColor(groundMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", new Color(0.33f, 0.35f, 0.39f));
    var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

    var actors = new List<(string label, GameObject go, Dictionary<string, Transform> bones, UnityEngine.Playables.PlayableGraph graph, UnityEngine.Animations.AnimationMixerPlayable mixer,
        Dictionary<string, (UnityEngine.Animations.AnimationClipPlayable playable, int input)> inputs, SlopArena.Client.Entities.SkirtBoneSpring spring,
        SkinnedMeshRenderer poncho, Mesh baked, int[] hem, Vector3[] hemRest, float baseX, (SkinnedMeshRenderer skin, Mesh mesh)[] draws)>();
    foreach (var (label, prefab, baseX) in rigs)
    {
        var go = UnityEngine.Object.Instantiate(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        spawned.Add(go);
        go.transform.SetPositionAndRotation(new Vector3(baseX, capsuleHeight * 0.5f + modelYOffset, 0f), Quaternion.identity);
        go.transform.localScale = Vector3.one * visualScale;
        var bones = go.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        var spring = go.GetComponentInChildren<SlopArena.Client.Entities.SkirtBoneSpring>(true);
        if (spring != null) spring.Step(0f);
        var poncho = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Salamander_Poncho");
        // Hem vertices: the blanket's two short ends in its planar UV. Their rest positions ride Spine2.
        var uv = poncho.sharedMesh.uv;
        var hem = Enumerable.Range(0, uv.Length).Where(i => uv[i].y < .01f || uv[i].y > .99f).ToArray();
        var baked = new Mesh();
        poncho.BakeMesh(baked, true);
        var rest = baked.vertices;
        var spine = bones["mixamorig:Spine2"];
        var hemRest = hem.Select(i => spine.InverseTransformPoint(poncho.transform.localToWorldMatrix.MultiplyPoint3x4(rest[i]))).ToArray();
        var animator = go.GetComponentInChildren<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        var graph = UnityEngine.Playables.PlayableGraph.Create("dusty-poncho-" + label);
        graphs.Add(graph);
        graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
        var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
        var mixer = UnityEngine.Animations.AnimationMixerPlayable.Create(graph, clipIds.Length);
        var inputs = new Dictionary<string, (UnityEngine.Animations.AnimationClipPlayable, int)>();
        for (int i = 0; i < clipIds.Length; i++)
        {
            var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clips[clipIds[i]]);
            playable.SetApplyFootIK(false);
            graph.Connect(playable, 0, mixer, i);
            inputs[clipIds[i]] = (playable, i);
        }
        UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, mixer);
        graph.Play();
        utility.AddSingleGO(go);
        var draws = go.GetComponentsInChildren<SkinnedMeshRenderer>(false).Select(skin => (skin, skin == poncho ? baked : new Mesh())).ToArray();
        foreach (var draw in draws) draw.skin.enabled = false;
        actors.Add((label, go, bones, graph, mixer, inputs, spring, poncho, baked, hem, hemRest, baseX, draws));
    }
    foreach (var actor in actors) System.IO.Directory.CreateDirectory(System.IO.Path.Combine(viewDir, actor.label));

    var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
    for (int f = 0; f < frames.Length; f++)
    {
        var row = frames[f];
        var root = new Vector3(F(row[0]), F(row[1]), F(row[2]));
        float yaw = F(row[3]);
        var frameMetrics = new Dictionary<string, object> { ["frame"] = f };
        foreach (var actor in actors)
        {
            actor.go.transform.SetPositionAndRotation(new Vector3(actor.baseX + root.x, capsuleHeight * 0.5f + modelYOffset + root.y, root.z), Quaternion.Euler(0f, yaw, 0f));
            foreach (var input in actor.inputs.Values) UnityEngine.Playables.PlayableExtensions.SetInputWeight(actor.mixer, input.input, 0f);
            for (int i = 4; i + 2 < row.Length; i += 3)
            {
                var (playable, input) = actor.inputs[row[i]];
                var clip = clips[row[i]];
                float t = F(row[i + 1]);
                t = clip.isLooping ? Mathf.Repeat(t, clip.length) : Mathf.Min(t, clip.length);
                UnityEngine.Playables.PlayableExtensions.SetTime(playable, t);
                UnityEngine.Playables.PlayableExtensions.SetInputWeight(actor.mixer, input, F(row[i + 2]));
            }
            actor.graph.Evaluate(0f);
            if (actor.spring != null) actor.spring.Step(1f / 60f);

            actor.poncho.BakeMesh(actor.baked, true);
            var world = actor.baked.vertices;
            var m = actor.poncho.transform.localToWorldMatrix;
            for (int i = 0; i < world.Length; i++) world[i] = m.MultiplyPoint3x4(world[i]);
            var depths = new Dictionary<string, float>();
            foreach (var (name, start, end, radius, from) in limbs)
            {
                Vector3 a = actor.bones[start].position, axis = actor.bones[end].position - a;
                float r = radius * visualScale, deepest = 0f;
                foreach (var p in world)
                {
                    float t = Vector3.Dot(p - a, axis) / axis.sqrMagnitude;
                    if (t < from) continue;
                    float depth = r - Vector3.Distance(p, a + axis * Mathf.Min(t, 1f));
                    if (depth > deepest) deepest = depth;
                }
                depths[name] = (float)System.Math.Round(deepest, 4);
            }
            var spine = actor.bones["mixamorig:Spine2"];
            float hemTravel = 0f;
            for (int i = 0; i < actor.hem.Length; i++)
                hemTravel = Mathf.Max(hemTravel, Vector3.Distance(world[actor.hem[i]], spine.TransformPoint(actor.hemRest[i])));
            var lifts = actor.spring == null ? null : ((float[])typeof(SlopArena.Client.Entities.SkirtBoneSpring)
                .GetField("_lastLift", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(actor.spring))
                .Select(v => (float)System.Math.Round(v, 1)).ToArray();
            frameMetrics[actor.label] = new Dictionary<string, object> { ["limbDepth"] = depths, ["hemTravel"] = (float)System.Math.Round(hemTravel, 4),
                ["lift"] = lifts };

            var target = actor.go.transform.position - Vector3.up * (capsuleHeight * 0.5f + modelYOffset) + Vector3.up * (1.0f - 0.15f * root.y);
            if (View == "gameplay")
            {
                camera.fieldOfView = 40f;
                camera.transform.position = target + Quaternion.Euler(35f, 0f, 0f) * new Vector3(0f, 0f, -3.4f);
            }
            else
            {
                camera.fieldOfView = 34f;
                camera.transform.position = target + Quaternion.Euler(10f, 215f, 0f) * new Vector3(0f, 0f, -4.0f);
            }
            camera.transform.LookAt(target);
            utility.BeginPreview(new Rect(0, 0, Width, Height), GUIStyle.none);
            var floor = actor.go.transform.position; floor.y = 0f;
            utility.DrawMesh(cube, Matrix4x4.TRS(new Vector3(floor.x, -0.03f, floor.z), Quaternion.identity, new Vector3(16f, 0.04f, 16f)), groundMaterial, 0);
            foreach (var (skin, mesh) in actor.draws)
            {
                if (skin != actor.poncho) skin.BakeMesh(mesh, true);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    utility.DrawMesh(mesh, skin.transform.localToWorldMatrix, skin.sharedMaterials[Mathf.Min(sub, skin.sharedMaterials.Length - 1)], sub);
            }
            for (int k = -8; k <= 8; k++)
            {
                float gx = Mathf.Floor(floor.x) + k, gz = Mathf.Floor(floor.z) + k;
                utility.DrawMesh(cube, Matrix4x4.TRS(new Vector3(gx, -0.005f, floor.z), Quaternion.identity, new Vector3(0.025f, 0.01f, 16f)), lineMaterial, 0);
                utility.DrawMesh(cube, Matrix4x4.TRS(new Vector3(floor.x, -0.005f, gz), Quaternion.identity, new Vector3(16f, 0.01f, 0.025f)), lineMaterial, 0);
            }
            camera.Render();
            var texture = utility.EndPreview();
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = (RenderTexture)texture;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(viewDir, actor.label, $"f{f:0000}.jpg"), image.EncodeToJPG(92));
            }
            finally { RenderTexture.active = previous; }
        }
        metrics.Add(frameMetrics);
    }
    UnityEngine.Object.DestroyImmediate(image);
    foreach (var actor in actors) foreach (var draw in actor.draws) UnityEngine.Object.DestroyImmediate(draw.mesh);
}
finally
{
    foreach (var graph in graphs) if (graph.IsValid()) graph.Destroy();
    foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
    if (lineMaterial != null) UnityEngine.Object.DestroyImmediate(lineMaterial);
    if (groundMaterial != null) UnityEngine.Object.DestroyImmediate(groundMaterial);
    utility.Cleanup();
}
System.IO.File.WriteAllText(System.IO.Path.Combine(viewDir, "metrics.json"), Newtonsoft.Json.JsonConvert.SerializeObject(metrics));
return new { take = Take, view = View, frames = frames.Length, output = viewDir };
