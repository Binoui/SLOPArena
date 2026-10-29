if (!EditorApplication.isPlaying) throw new InvalidOperationException("Measure the skirt in live Training.");
string output = "/home/binoui/Documents/projects/SlopArena/tmp/nyx-model/spring-dress-v3/hem-motion.jsonl";
var spring = UnityEngine.Object.FindObjectsByType<SlopArena.Client.Entities.SkirtBoneSpring>(FindObjectsSortMode.None).Single();
var skin = spring.GetComponent<SkinnedMeshRenderer>();
var training = UnityEngine.Object.FindFirstObjectByType<SlopArena.Client.World.TrainingMatch>();
var hips = spring.GetComponentsInParent<Transform>(true).Last().GetComponentsInChildren<Transform>(true).Single(t => t.name == "mixamorig:Hips");
var mesh = new Mesh(); skin.BakeMesh(mesh, true);
var local = skin.sharedMesh.vertices;
// Sample the same vertices along the original hem, not bounds that change identity each frame.
float floor = local.Min(v => v.z);
var hem = Enumerable.Range(0,local.Length).Where(i => local[i].z < floor + skin.sharedMesh.bounds.size.z * .025f).Where((v,i) => i%4 == 0).ToArray();
var stream = new System.IO.StreamWriter(output, false); stream.AutoFlush = true;
int previous = -1;
double deadline = EditorApplication.timeSinceStartup + 240;
Action<UnityEngine.Rendering.ScriptableRenderContext, Camera[]> sample = null;
EditorApplication.CallbackFunction check = null;
Action cleanup = null;
bool cleaned = false;
cleanup = () => {
    if (cleaned) return; cleaned = true;
    UnityEngine.Rendering.RenderPipelineManager.endFrameRendering -= sample;
    EditorApplication.update -= check;
    stream.Dispose(); UnityEngine.Object.DestroyImmediate(mesh);
};
sample = (context,cameras) => {
    try
    {
        if (!EditorApplication.isPlaying || skin == null || EditorApplication.timeSinceStartup > deadline) { cleanup(); return; }
        if (Time.frameCount == previous) return;
        previous = Time.frameCount;
        skin.BakeMesh(mesh, true);
        var vertices = mesh.vertices;
        float[] V(Vector3 v) => new[] { v.x,v.y,v.z };
        stream.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new {
            frame = Time.frameCount, tick = training.CaptureTick, time = Time.time,
            hipsPosition = V(hips.position), hipsRotation = new[] { hips.rotation.x,hips.rotation.y,hips.rotation.z,hips.rotation.w },
            scale = V(hips.lossyScale), hemIndices = hem,
            hem = hem.Select(i => V(Quaternion.Inverse(hips.rotation) * (skin.transform.TransformPoint(vertices[i])-hips.position))).ToArray()
        }));
    }
    catch (Exception ex) { cleanup(); Debug.LogError("Nyx skirt measurement stopped: " + ex); }
};
check = () => { if (!EditorApplication.isPlaying || skin == null || EditorApplication.timeSinceStartup > deadline) cleanup(); };
UnityEngine.Rendering.RenderPipelineManager.endFrameRendering += sample;
EditorApplication.update += check;
return new { output, hemVertices = hem.Length, method = "Baked skin vertices transformed to the animated Hips frame", maxSeconds = 240 };
