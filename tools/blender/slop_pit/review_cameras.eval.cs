// Review cameras for Slop Pit (eval_file inside the sloparena.stage.views.* Play Mode batch).
// Hides the HUD documents and adds disabled named cameras that copy the gameplay camera (clear
// colour, culling, URP post-processing); capture each with
//   sloparena.capture.game-view --source camera --camera <name> --output tmp/<dir>/<name>.png
// SlopPitThumb is the stage-select thumbnail pose. Everything is discarded when Play Mode stops.
if (!UnityEngine.Application.isPlaying) throw new System.InvalidOperationException("Run inside the stage-views Play Mode session.");
UnityEngine.Camera main = UnityEngine.Camera.main;
if (main == null)
    foreach (var c in UnityEngine.Camera.allCameras)
        if (main == null || c.depth > main.depth) main = c;
if (main == null) throw new System.InvalidOperationException("No gameplay camera.");
int hidden = 0;
foreach (var doc in UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(UnityEngine.FindObjectsSortMode.None))
{
    if (doc.rootVisualElement == null) continue;
    doc.rootVisualElement.style.display = UnityEngine.UIElements.DisplayStyle.None;
    hidden++;
}
var poses = new System.Collections.Generic.List<(string name, UnityEngine.Vector3 pos, UnityEngine.Vector3 target, float fov)>
{
    ("SlopPitThumb", new UnityEngine.Vector3(-27f, 16f, -31f), new UnityEngine.Vector3(0f, -1.5f, 2.5f), 40f),
    ("SlopPitRingside", new UnityEngine.Vector3(2f, 1.75f, -14.5f), new UnityEngine.Vector3(0f, 1.9f, 0f), 50f),
    ("SlopPitRecoveryWest", new UnityEngine.Vector3(-27f, -3.5f, 9f), new UnityEngine.Vector3(-16f, -6.5f, 0f), 52f),
    ("SlopPitRecoveryCorner", new UnityEngine.Vector3(25f, -2f, -25f), new UnityEngine.Vector3(13.5f, -6f, -13.5f), 52f),
    ("SlopPitPitDown", new UnityEngine.Vector3(-3f, 5f, -21f), new UnityEngine.Vector3(2f, -22f, -31f), 55f),
    ("SlopPitHeroLow", new UnityEngine.Vector3(0f, 3.2f, -9f), new UnityEngine.Vector3(0f, 4.5f, 48f), 42f),
};
var src = main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
var made = new System.Collections.Generic.List<string>();
foreach (var p in poses)
{
    var go = new UnityEngine.GameObject(p.name);
    var cam = go.AddComponent<UnityEngine.Camera>();
    cam.CopyFrom(main);
    cam.transform.position = p.pos;
    cam.transform.LookAt(p.target);
    cam.fieldOfView = p.fov;
    cam.nearClipPlane = 0.1f;
    cam.farClipPlane = 600f;
    cam.enabled = false;
    var dst = go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()
        ?? go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
    if (src != null)
    {
        dst.renderPostProcessing = src.renderPostProcessing;
        dst.antialiasing = src.antialiasing;
        dst.volumeLayerMask = src.volumeLayerMask;
        dst.renderShadows = src.renderShadows;
    }
    made.Add(p.name);
}
return new { hidden, cameras = made, postProcessing = src != null && src.renderPostProcessing };
