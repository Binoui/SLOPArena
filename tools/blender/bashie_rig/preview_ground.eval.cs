// Play Mode eval body, after preview_swap.eval.cs and a few frames: puts Bashie's lowest point in her current pose on
// her fighter's floor line (the capsule bottom: root - ModelYOffset - CapsuleHeight/2) and reports the correction.
// Run it twice; the second run should report a sink near zero.
if (!Application.isPlaying) throw new InvalidOperationException("Run inside Play Mode after preview_swap.eval.cs.");
var renderer = UnityEngine.Object.FindObjectsByType<SlopArena.Client.Entities.PlayerRenderer>(FindObjectsSortMode.None)
    .FirstOrDefault(r => r.GetComponentsInChildren<Animator>().Any(a => a.avatar != null && a.avatar.name.StartsWith("bashie")))
    ?? throw new InvalidOperationException("No fighter presents Bashie; run preview_swap.eval.cs first.");
var animator = renderer.GetComponentsInChildren<Animator>().First(a => a.avatar != null && a.avatar.name.StartsWith("bashie"));
float lo = float.MaxValue, hi = float.MinValue;
var baked = new Mesh();
foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(s => s.enabled && s.sharedMesh != null))
{
    skin.BakeMesh(baked, true);
    var m = skin.transform.localToWorldMatrix;
    foreach (var v in baked.vertices) { float y = m.MultiplyPoint3x4(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
}
UnityEngine.Object.DestroyImmediate(baked);
float floorY = renderer.transform.position.y - renderer.ModelYOffset - renderer.CapsuleHeight * 0.5f;
float sink = floorY - lo;
float before = renderer.ModelYOffset;
renderer.ModelYOffset = before + sink;
return new Dictionary<string, object>
{
    ["floorY"] = floorY, ["lowest"] = lo, ["top"] = hi, ["height"] = hi - lo, ["sinkCorrected"] = sink,
    ["modelYOffsetBefore"] = before, ["modelYOffsetAfter"] = renderer.ModelYOffset, ["capsuleHeight"] = renderer.CapsuleHeight,
    ["modelScale"] = animator.transform.localScale.x,
};
