// Eval body: make Sir Bonk's left pauldron (the pot lid he throws for Q) a prop of its own (2026-10-22).
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// Input: lid_split.py's <name>_Lid.fbx (the lid alone, at its place in the model). Run it while the live body still has
// the lid: it imports the lid as sir_bonk_lid.fbx, checks that its vertices land on the body's own lid vertices in his
// bind pose, and writes Prefabs/sir_bonk_pot_lid.prefab, a lid-centred prop: +Y the dome's outer face, +Z the crown's
// up, anchors lidCentre/lidOut/lidCrownUp. It reports the WeaponAttach offset that puts the prop back exactly where the
// lid was, on mixamorig:LeftArm (the lid rode 80% LeftArm, 20% LeftShoulder). Nothing the live Bonk uses changes.
const string SrcLidFbx = "/home/binoui/Documents/projects/SlopArena/tmp/bonk-q/rig/SirBonk_Lid.fbx";
const string LidFbx = "Assets/Art/Characters/bonk/Models/sir_bonk_lid.fbx";
const string PrefabPath = "Assets/Art/Characters/bonk/Prefabs/sir_bonk_pot_lid.prefab";
const string Bone = "mixamorig:LeftArm";
const float VisualScale = 3.3f;   // character.json presentation.visualScale
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);

System.IO.File.Copy(SrcLidFbx, System.IO.Path.Combine(root, LidFbx), true);
AssetDatabase.ImportAsset(LidFbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var mi = (ModelImporter)AssetImporter.GetAtPath(LidFbx);
mi.animationType = ModelImporterAnimationType.None;
mi.importAnimation = false;
mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false;
mi.materialImportMode = ModelImporterMaterialImportMode.None;
mi.importNormals = ModelImporterNormals.Import;
mi.isReadable = true;
mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = false;   // as sir_bonk.fbx
mi.SaveAndReimport();
var lidAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LidFbx);

var catalog = AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages/bonk" })
    .Select(g => AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(g))).First(c => c != null);
var spawned = new List<GameObject>();
var previewScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try
{
    var rig = UnityEngine.Object.Instantiate(catalog.Rig);
    rig.hideFlags = HideFlags.HideAndDontSave; spawned.Add(rig);
    rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    rig.transform.localScale = Vector3.one * VisualScale;
    var skin = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).First();
    var transforms = rig.GetComponentsInChildren<Transform>(true);
    var bone = transforms.First(t => t.name == Bone);
    var hips = transforms.First(t => t.name == "mixamorig:Hips");
    // The body FBX's root: the nearest ancestor of the mesh node that also holds the skeleton.
    Transform fbxRoot = skin.transform.parent;
    while (fbxRoot != null && !hips.IsChildOf(fbxRoot)) fbxRoot = fbxRoot.parent;
    if (fbxRoot == null) throw new System.Exception("no common root for the body mesh and the skeleton");
    report["bodyMesh"] = skin.sharedMesh.name;
    report["bodyVertsBefore"] = skin.sharedMesh.vertexCount;
    report["fbxRoot"] = fbxRoot.name;

    var lid = UnityEngine.Object.Instantiate(lidAsset);
    lid.hideFlags = HideFlags.HideAndDontSave; spawned.Add(lid);
    // A one-object FBX imports as that object, so the lid's root carries the file's axis and unit conversion
    // (270 degrees on x, x100): keep it, under a holder where the body's FBX root is.
    var holder = new GameObject("lid holder") { hideFlags = HideFlags.HideAndDontSave };
    spawned.Add(holder);
    holder.transform.SetPositionAndRotation(fbxRoot.position, fbxRoot.rotation);
    holder.transform.localScale = fbxRoot.lossyScale;
    lid.transform.SetParent(holder.transform, false);
    var lidFilter = lid.GetComponentsInChildren<MeshFilter>(true).First();
    var lidWorld = lidFilter.sharedMesh.vertices.Select(v => lidFilter.transform.TransformPoint(v)).ToArray();

    // In the bind pose the skinned body draws its mesh through the renderer's own transform.
    var bodyWorld = skin.sharedMesh.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
    var gaps = new List<float>();
    foreach (var p in lidWorld)
    {
        float best = float.MaxValue;
        foreach (var q in bodyWorld) { float d = (p - q).sqrMagnitude; if (d < best) best = d; }
        gaps.Add(Mathf.Sqrt(best));
    }
    gaps.Sort();
    report["alignmentMetres"] = new { median = gaps[gaps.Count / 2], max = gaps[gaps.Count - 1], verts = gaps.Count };

    // The lid's frame: centre, outward normal (least spread, away from the shoulder joint), the crown's up.
    Vector3 centre = lidWorld.Aggregate(Vector3.zero, (a, v) => a + v) / lidWorld.Length;
    var cov = new float[3, 3];
    foreach (var v in lidWorld) { var d = v - centre; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) cov[i, j] += d[i] * d[j]; }
    System.Func<Vector3, Vector3> mul = x => new Vector3(cov[0, 0] * x.x + cov[0, 1] * x.y + cov[0, 2] * x.z,
        cov[1, 0] * x.x + cov[1, 1] * x.y + cov[1, 2] * x.z, cov[2, 0] * x.x + cov[2, 1] * x.y + cov[2, 2] * x.z);
    // the least-spread axis: power iteration on (trace I - C), whose largest eigenvector is C's smallest
    float trace = cov[0, 0] + cov[1, 1] + cov[2, 2];
    var normal = new Vector3(0.3f, 0.6f, 0.7f).normalized;
    for (int k = 0; k < 200; k++) normal = (normal * trace - mul(normal)).normalized;
    if (Vector3.Dot(normal, centre - bone.position) < 0f) normal = -normal;
    var crownUp = Vector3.ProjectOnPlane(Vector3.up, normal);
    if (crownUp.sqrMagnitude < 1e-4f) crownUp = Vector3.ProjectOnPlane(rig.transform.forward, normal);
    crownUp.Normalize();
    var frame = Quaternion.LookRotation(crownUp, normal);   // +Z the crown's up, +Y the outer face
    var local = lidWorld.Select(v => Quaternion.Inverse(frame) * (v - centre)).ToArray();
    report["lidSizeMetres"] = new { across = local.Max(v => v.x) - local.Min(v => v.x), tall = local.Max(v => v.z) - local.Min(v => v.z),
        thick = local.Max(v => v.y) - local.Min(v => v.y) };

    // The prefab: root at the lid's centre in its own frame, the mesh child keeping the lid's place relative to it.
    var rootGo = new GameObject("sir_bonk_pot_lid");
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootGo, previewScene);
    spawned.Add(rootGo);
    rootGo.transform.SetPositionAndRotation(centre, frame);
    var mesh = new GameObject("lid");
    mesh.transform.SetParent(rootGo.transform, false);
    mesh.transform.SetPositionAndRotation(lidFilter.transform.position, lidFilter.transform.rotation);
    mesh.transform.localScale = lidFilter.transform.lossyScale;
    mesh.AddComponent<MeshFilter>().sharedMesh = lidFilter.sharedMesh;
    var renderer = mesh.AddComponent<MeshRenderer>();
    renderer.sharedMaterial = skin.sharedMaterial;
    renderer.shadowCastingMode = skin.shadowCastingMode;
    foreach (var (name, at) in new[] { ("lidCentre", Vector3.zero), ("lidOut", Vector3.up * 0.15f), ("lidCrownUp", Vector3.forward * 0.15f) })
    {
        var anchor = new GameObject(name);
        anchor.transform.SetParent(rootGo.transform, false);
        anchor.transform.localPosition = at;
    }
    var positionOffset = bone.InverseTransformPoint(centre);
    var rotationOffset = (Quaternion.Inverse(bone.rotation) * frame).eulerAngles;
    rootGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
    report["prefab"] = PrefabPath;
    report["material"] = AssetDatabase.GetAssetPath(skin.sharedMaterial);
    report["weaponAttach"] = new { bone = Bone, positionOffset = new[] { positionOffset.x, positionOffset.y, positionOffset.z },
        rotationOffset = new[] { rotationOffset.x, rotationOffset.y, rotationOffset.z } };
    report["boneLossyScale"] = new[] { bone.lossyScale.x, bone.lossyScale.y, bone.lossyScale.z };
}
finally
{
    foreach (var g in spawned) if (g != null) UnityEngine.Object.DestroyImmediate(g);
    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);
}
return report;
