if (!EditorApplication.isPlaying) throw new InvalidOperationException("Measure cloth in live Training.");
string output = "/home/binoui/Documents/projects/SlopArena/tmp/nyx-model/cloth-rebuild-v2/hem-motion.jsonl";
var cloth = UnityEngine.Object.FindObjectsByType<Cloth>(FindObjectsSortMode.None).Single(c => c.name == "Nyx_Real_Dress");
var skin = cloth.GetComponent<SkinnedMeshRenderer>();
var training = UnityEngine.Object.FindFirstObjectByType<SlopArena.Client.World.TrainingMatch>();
var root = skin.rootBone;
var coefficients = cloth.coefficients;
var pins = Enumerable.Range(0, coefficients.Length).Where(i => coefficients[i].maxDistance == 0).ToArray();
var raw = cloth.vertices;
var hem = Enumerable.Range(0,raw.Length).Where(i => coefficients[i].maxDistance > 1.03f).ToArray();
var stream = new System.IO.StreamWriter(output, false);
stream.AutoFlush = true;
int frames = 0, previous = -1;
double deadline = EditorApplication.timeSinceStartup + 240;
Action<UnityEngine.Rendering.ScriptableRenderContext, Camera[]> sample = null;
EditorApplication.CallbackFunction check = null;
Action cleanup = null;
cleanup = () => {
    UnityEngine.Rendering.RenderPipelineManager.endFrameRendering -= sample;
    EditorApplication.update -= check;
    stream.Dispose();
};
sample = (context,cameras) => {
    try
    {
        if (!EditorApplication.isPlaying || cloth == null || EditorApplication.timeSinceStartup > deadline) { cleanup(); return; }
        if (Time.frameCount == previous) return;
        previous = Time.frameCount;
        var vertices = cloth.vertices;
        Vector3 center = Vector3.zero;
        foreach (int i in pins) center += vertices[i];
        center /= pins.Length;
        float[] V(Vector3 v) => new[] { v.x,v.y,v.z };
        stream.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new {
            frame = Time.frameCount, tick = training.CaptureTick, time = Time.time,
            rootPosition = V(root.position), rootRotation = new[] { root.rotation.x,root.rotation.y,root.rotation.z,root.rotation.w },
            skinPosition = V(skin.transform.position), skinRotation = new[] { skin.transform.rotation.x,skin.transform.rotation.y,skin.transform.rotation.z,skin.transform.rotation.w },
            scale = V(skin.transform.lossyScale), pinnedCenter = V(center),
            hem = hem.Select(i => V(vertices[i])).ToArray(),
            pinned = pins.Take(12).Select(i => V(vertices[i])).ToArray(),
            boundsCenter = V(skin.bounds.center), boundsSize = V(skin.bounds.size)
        }));
        frames++;
    }
    catch (Exception ex) { cleanup(); Debug.LogError("Nyx cloth measurement stopped: " + ex); }
};
check = () => { if (!EditorApplication.isPlaying || cloth == null || EditorApplication.timeSinceStartup > deadline) cleanup(); };
UnityEngine.Rendering.RenderPipelineManager.endFrameRendering += sample;
EditorApplication.update += check;
return new { output, hemVertices = hem.Length, pinned = pins.Length, maxSeconds = 240 };
