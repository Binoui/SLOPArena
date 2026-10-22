// Eval body: import Sir Bonk's plank (a Tripo prop) as the candidate's sword, sized and placed from the old sword.
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// Edit SrcFbx/SrcTexture first. The plank prefab keeps the old sword's frame conventions (root at the weapon
// attach point, unit scale), so SirBonkWeaponAttachConfig's converted offsets still apply:
// - its grip (the red wrap) sits in Sir Bonk's closed right fist (the GenRA idle's first frame, at visualScale),
//   along the old blade's direction, its flat facing the old blade's flat;
// - it is scaled so the fist-to-tip reach equals the old sword's (the plank's long handle would otherwise
//   change every move's reach);
// - bladeHilt sits where the blade leaves the crossguard and bladeEnd at the tip (trails and the cooked blade).
// Writes sir_bonk_plank.fbx, SirBonkPlank_BaseColor.png, SirBonkPlank.mat, Prefabs/sir_bonk_plank.prefab, and points
// SirBonkWeaponAttachConfig at the prefab. Nothing the live Bonk uses changes.
const string SrcFbx = "/home/binoui/Documents/projects/SlopArena/tmp/bonk-sir/plank-v1/tripo_convert_aea08d73-8090-48ba-810f-1094a31626b3.fbx";
const string SrcTexture = "/home/binoui/Documents/projects/SlopArena/tmp/bonk-sir/plank-v1/tripo_convert_aea08d73-8090-48ba-810f-1094a31626b3.fbm/tripo_rgb_193e117c-c02e-41d2-af66-cb72cd9662e3.png";
const string Fbx = "Assets/Art/Characters/bonk/Models/sir_bonk_plank.fbx";
const string Tex = "Assets/Art/Characters/bonk/Textures/SirBonkPlank_BaseColor.png";
const string Mat = "Assets/Art/Characters/bonk/Materials/SirBonkPlank.mat";
const string PrefabPath = "Assets/Art/Characters/bonk/Prefabs/sir_bonk_plank.prefab";
const string OldSword = "Assets/Art/Characters/bonk/Prefabs/bonk_sword.prefab";
const string Candidate = "Assets/CharacterPackages/bonk/Presentation/SirBonkRig.prefab";
const string CandidateConfig = "Assets/Art/Characters/bonk/SirBonkWeaponAttachConfig.asset";
const string IdleClip = "Assets/Art/Experiments/GenRA/bonk/bonk-sir-idle-hero.anim";
const float VisualScale = 3.3f;   // character.json presentation.visualScale
// Fractions along the plank from its tip, measured on the Tripo mesh (width profile): blade 0-0.63, crossguard
// 0.63-0.72, red grip 0.72-0.92, door-knob pommel 0.95-1.
const float GripFromTip = 0.82f, BladeHiltFromTip = 0.63f;
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);

// 1. Assets.
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
var oldSword = AssetDatabase.LoadAssetAtPath<GameObject>(OldSword);
var oldMat = oldSword.GetComponentInChildren<MeshRenderer>().sharedMaterial;
var mat = AssetDatabase.LoadAssetAtPath<Material>(Mat);
if (mat == null) { mat = new Material(oldMat) { name = "SirBonkPlank" }; AssetDatabase.CreateAsset(mat, Mat); }
var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex);
if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);

// 2. The plank's own axes: longest bounds axis = length (tip at the sparse end), shortest = thickness (the flat's normal).
var verts = mesh.vertices;
var b = mesh.bounds;
int[] order = new[] { 0, 1, 2 }.OrderByDescending(i => b.size[i]).ToArray();
int longAxis = order[0], thickAxis = order[2];
float lo = b.min[longAxis], hi = b.max[longAxis], length = hi - lo;
int nearLo = verts.Count(v => v[longAxis] < lo + 0.15f * length), nearHi = verts.Count(v => v[longAxis] > hi - 0.15f * length);
bool tipAtLo = nearLo < nearHi;  // the hilt end carries the dense crossguard, grip and pommel
Vector3 uLong = Vector3.zero; uLong[longAxis] = tipAtLo ? -1f : 1f;   // hilt -> tip
Vector3 uThick = Vector3.zero; uThick[thickAxis] = 1f;
Vector3 tipPoint = b.center; tipPoint[longAxis] = tipAtLo ? lo : hi;
Vector3 gripPoint = tipPoint - uLong * (GripFromTip * length);
report["plankMesh"] = new Dictionary<string, object> { ["boundsSize"] = new[] { b.size.x, b.size.y, b.size.z }, ["longAxis"] = longAxis, ["thickAxis"] = thickAxis,
    ["tipAtLow"] = tipAtLo, ["vertsNearLow"] = nearLo, ["vertsNearHigh"] = nearHi };

// 3. Sir Bonk's closed right fist at game scale (idle frame 0), the old sword placed by the candidate's config.
var configObject = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(CandidateConfig));
var entry = configObject.FindProperty("Entries").GetArrayElementAtIndex(0);
var posOffset = entry.FindPropertyRelative("PositionOffset").vector3Value;
var rotOffset = Quaternion.Euler(entry.FindPropertyRelative("RotationOffset").vector3Value);
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
Vector3 fistRoot, hiltOldRoot, tipOldRoot, flatOldRoot;
try
{
    var actor = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Candidate));
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor, scene);
    actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    actor.transform.localScale = Vector3.one * VisualScale;
    var animator = actor.GetComponentInChildren<Animator>();
    animator.applyRootMotion = false;
    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    var animancer = actor.AddComponent<Animancer.AnimancerComponent>();
    animancer.Animator = animator;
    var state = animancer.Play(AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClip), 0f);
    state.Speed = 0f; state.Time = 0f; animancer.Evaluate(0f);
    var all = actor.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t);
    var hand = all["mixamorig:RightHand"];
    var fingers = new[] { "Index1", "Middle1", "Pinky1", "Index3", "Middle3", "Pinky3" }.Select(n => all["mixamorig:RightHand" + n].position).ToArray();
    var fist = fingers.Aggregate(Vector3.zero, (a, p) => a + p) / fingers.Length;
    var weaponPos = hand.TransformPoint(posOffset);
    var weaponRot = hand.rotation * rotOffset;
    var inv = Quaternion.Inverse(weaponRot);
    fistRoot = inv * (fist - weaponPos);
    // old sword anchors and flat in its unit-scale root frame
    var old = (GameObject)UnityEngine.Object.Instantiate(oldSword);
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(old, scene);
    old.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    var oldAll = old.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t);
    hiltOldRoot = oldAll["bladeHilt"].position;
    tipOldRoot = oldAll["bladeEnd"].position;
    var oldMesh = old.GetComponentInChildren<MeshFilter>().sharedMesh;
    int oldThin = new[] { 0, 1, 2 }.OrderBy(i => oldMesh.bounds.size[i] * old.transform.lossyScale[i]).First();
    flatOldRoot = Vector3.zero; flatOldRoot[oldThin] = 1f;
    report["fistInWeaponFrame"] = new[] { fistRoot.x, fistRoot.y, fistRoot.z };
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }

// 4. Placement: the plank's axis through the fist along the old blade direction, the old fist-to-tip reach.
var d = (tipOldRoot - hiltOldRoot).normalized;
var n = (flatOldRoot - Vector3.Dot(flatOldRoot, d) * d).normalized;
float offAxis = Vector3.Cross(fistRoot - hiltOldRoot, d).magnitude;
float reach = Vector3.Dot(tipOldRoot - fistRoot, d);
float scale = reach / (GripFromTip * length);
// rotation taking (uLong, uThick, uLong x uThick) to (d, n, d x n)
var meshBasis = new Matrix4x4(); meshBasis.SetColumn(0, uLong); meshBasis.SetColumn(1, uThick); meshBasis.SetColumn(2, Vector3.Cross(uLong, uThick)); meshBasis[3, 3] = 1f;
var target = new Matrix4x4(); target.SetColumn(0, d); target.SetColumn(1, n); target.SetColumn(2, Vector3.Cross(d, n)); target[3, 3] = 1f;
var rm = target * meshBasis.transpose;
var rotation = Quaternion.LookRotation(rm.GetColumn(2), rm.GetColumn(1));
var position = fistRoot - rotation * (gripPoint * scale);
var bladeHilt = fistRoot + d * ((GripFromTip - BladeHiltFromTip) * length * scale);
var bladeEnd = fistRoot + d * reach;
report["placement"] = new Dictionary<string, object> {
    ["oldBladeMetres"] = (tipOldRoot - hiltOldRoot).magnitude, ["oldReachFromFistMetres"] = reach, ["fistOffOldAxisMetres"] = offAxis,
    ["plankScale"] = scale, ["plankLengthMetres"] = length * scale, ["newBladeMetres"] = (bladeEnd - bladeHilt).magnitude };

// 5. Prefab: unit-scale root (the attach point), the plank mesh as a child, the trail/blade anchors.
var prefabScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try
{
    var rootGo = new GameObject("sir_bonk_plank");
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootGo, prefabScene);
    var plank = new GameObject("plank");
    plank.transform.SetParent(rootGo.transform, false);
    plank.transform.localPosition = position;
    plank.transform.localRotation = rotation;
    plank.transform.localScale = Vector3.one * scale;
    plank.AddComponent<MeshFilter>().sharedMesh = mesh;
    plank.AddComponent<MeshRenderer>().sharedMaterial = mat;
    foreach (var (name, p) in new[] { ("bladeHilt", bladeHilt), ("bladeEnd", bladeEnd) })
    {
        var anchor = new GameObject(name);
        anchor.transform.SetParent(rootGo.transform, false);
        anchor.transform.localPosition = p;
    }
    PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(prefabScene); }
entry.FindPropertyRelative("Prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
configObject.ApplyModifiedPropertiesWithoutUndo();
AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(CandidateConfig));
report["prefab"] = PrefabPath;
report["config"] = CandidateConfig;
return report;
