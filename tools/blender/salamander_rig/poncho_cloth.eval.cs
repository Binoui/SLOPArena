// Eval body: make the salamander's poncho cloth (owner, 2026-10-27: "let it flow"). Run after unity_import.eval.cs, which
// rewrites the prefab. The build skins the poncho to his spine, neck, shoulders and hips only (build_rig.py
// poncho_torso=1); Unity's Cloth moves the rest. Around the collar the cloth is pinned to the skin (max distance 0); below
// it may swing further from the skinned position the further it hangs. Capsules on his arms, chest, hips and thighs push
// it, so a raised arm lifts the poncho. Presentation only: the cook bakes bones, not the mesh. The capsules stay on the
// Default layer, which the camera and aim raycasts don't query (they look only at the stage proxy's layer).
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/salamander_rig/poncho_cloth.eval.cs"
const string PrefabPath = "Assets/CharacterPackages/salamander/Presentation/SalamanderRig.prefab";
const float PinRadius = 0.11f;    // rig metres from the neck joint: pinned
const float FreeRadius = 0.30f;   // fully free from here on
const float MaxSwing = 0.06f;     // largest max distance (rig metres)
var root = PrefabUtility.LoadPrefabContents(PrefabPath);
var report = new Dictionary<string, object>();
try
{
    var smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name.EndsWith("_Poncho"));
    Transform Bone(string n) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == "mixamorig:" + n);
    var cloth = smr.GetComponent<Cloth>();
    if (cloth == null) cloth = smr.gameObject.AddComponent<Cloth>();   // Unity's fake null defeats ??
    cloth.stretchingStiffness = 1f;
    cloth.bendingStiffness = 0.7f;
    cloth.damping = 0.25f;
    cloth.useGravity = true;
    cloth.worldVelocityScale = 0.4f;
    cloth.worldAccelerationScale = 0.6f;
    cloth.friction = 0f;
    cloth.collisionMassScale = 0.1f;
    cloth.useTethers = true;
    cloth.clothSolverFrequency = 120;
    // Coefficients per cloth vertex (Unity welds the mesh's split vertices), from the rest pose in the poncho's space.
    // Cloth.vertices are world-scaled and centred on the renderer's root bone (Hips), measured here in the rest pose at
    // scale 1, so world metres are rig metres.
    var neck = Bone("Neck").position - smr.rootBone.position;
    var verts = cloth.vertices;
    var coeff = new ClothSkinningCoefficient[verts.Length];
    int pinned = 0; float dMin = float.MaxValue, dMax = 0f;
    for (int i = 0; i < verts.Length; i++)
    {
        float d = Vector3.Distance(verts[i], neck);
        dMin = Mathf.Min(dMin, d); dMax = Mathf.Max(dMax, d);
        float t = Mathf.Clamp01((d - PinRadius) / (FreeRadius - PinRadius));
        t = t * t * (3 - 2 * t);
        coeff[i].maxDistance = t * MaxSwing;
        coeff[i].collisionSphereDistance = float.MaxValue;
        if (t <= 0f) pinned++;
    }
    cloth.coefficients = coeff;
    // Capsules along the bones that push the cloth.
    var capsules = new List<CapsuleCollider>();
    foreach (var old in root.GetComponentsInChildren<CapsuleCollider>(true).Where(c => c.name.StartsWith("ClothCapsule_")).ToArray())
        UnityEngine.Object.DestroyImmediate(old.gameObject);
    foreach (var (bone, child, radius) in new[] {
        ("LeftArm", "LeftForeArm", 0.03f), ("LeftForeArm", "LeftHand", 0.025f), ("RightArm", "RightForeArm", 0.03f), ("RightForeArm", "RightHand", 0.025f),
        ("Spine", "Spine2", 0.06f), ("Spine2", "Neck", 0.055f), ("Hips", "Spine", 0.065f),
        ("LeftUpLeg", "LeftLeg", 0.055f), ("RightUpLeg", "RightLeg", 0.055f) })
    {
        var a = Bone(bone); var b = Bone(child);
        var go = new GameObject("ClothCapsule_" + bone);
        go.transform.SetParent(a, false);
        var cap = go.AddComponent<CapsuleCollider>();
        var local = a.InverseTransformPoint(b.position);
        float len = local.magnitude;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, local / len);
        cap.direction = 1;
        cap.center = new Vector3(0, len / 2, 0);
        cap.radius = radius / a.lossyScale.x;
        cap.height = len + 2 * cap.radius;
        capsules.Add(cap);
    }
    cloth.capsuleColliders = capsules.ToArray();
    report["clothVertices"] = verts.Length;
    report["pinned"] = pinned;
    report["neckDistance"] = new[] { dMin, dMax };
    report["meshScale"] = smr.transform.lossyScale.x;
    report["capsules"] = capsules.Count;
    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
}
finally { PrefabUtility.UnloadPrefabContents(root); }
return report;
