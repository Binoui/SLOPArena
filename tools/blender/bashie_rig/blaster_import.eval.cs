// Eval body: import build_blaster.py's water blaster as Bashie's Hose Down (slot A) prop. The model, texture and
// material go under Assets/Art/Characters/bashie/ (Models/BashieBlaster.fbx, Textures/Bashie_Blaster.png,
// Materials/Bashie_Blaster.mat); her package gets Presentation/BashieBlaster.prefab and
// Presentation/BashieWeaponAttachments.asset (one entry, slot A on her right hand), which her catalog binds.
// The grip is a pistol grip: her knuckles line up with the raked grip, her metacarpals point along the barrel and
// the prop's origin (the middle of the grip's upper half) sits in her fist. Measured from her rig's own hand bones.
// The prefab has two anchors, Pump (the middle of the pump grip under the barrel, where her left hand holds) and Muzzle
// (the barrel's tip). The weapon entry names them as its hilt and tip anchors, so the GenRA rig pack gives them joints
// (Weapon0/Pump, Weapon0/Muzzle) that briefs can aim the hands and the stream at. It sets no trail style, so the game
// draws no trail.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/bashie_rig/blaster_import.eval.cs"
const string Build = "/home/binoui/Documents/projects/SlopArena/tmp/bashie-model/blaster/build";
const string Dir = "Assets/Art/Characters/bashie";
const string Fbx = Dir + "/Models/BashieBlaster.fbx";
const string Tex = Dir + "/Textures/Bashie_Blaster.png";
const string MatPath = Dir + "/Materials/Bashie_Blaster.mat";
const string Package = "Assets/CharacterPackages/bashie";
const string PrefabPath = Package + "/Presentation/BashieBlaster.prefab";
const string ConfigPath = Package + "/Presentation/BashieWeaponAttachments.asset";
const byte SlotA = 11;               // CharacterState.AttackSlot of A
const float RakeDegrees = 20f;       // the grip leans back from upright (build_blaster.py's review render)
const float FistAlongHand = 0.6f;    // the fist's middle, as a share of wrist -> middle knuckle
string root = System.IO.Path.GetDirectoryName(Application.dataPath);
var report = new Dictionary<string, object>();

// Colour map and material (her body material's shader and settings).
System.IO.File.Copy(System.IO.Path.Combine(Build, "textures", "Bashie_Blaster.png"), System.IO.Path.Combine(root, Tex), true);
AssetDatabase.ImportAsset(Tex, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var ti = (TextureImporter)AssetImporter.GetAtPath(Tex);
ti.textureType = TextureImporterType.Default;
ti.sRGBTexture = true;
ti.mipmapEnabled = true;
ti.maxTextureSize = 1024;
ti.textureCompression = TextureImporterCompression.Compressed;
ti.filterMode = FilterMode.Bilinear;
ti.SaveAndReimport();
var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
if (mat == null)
{
    mat = new Material(AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Materials/Bashie_Body.mat")) { name = "Bashie_Blaster" };
    AssetDatabase.CreateAsset(mat, MatPath);
}
var t2d = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex);
if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", t2d);
if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", t2d);
EditorUtility.SetDirty(mat);
AssetDatabase.SaveAssetIfDirty(mat);

// Model: a static mesh with her body's scale settings (the same Blender export).
var body = (ModelImporter)AssetImporter.GetAtPath(Dir + "/Models/bashie.fbx");
System.IO.File.Copy(System.IO.Path.Combine(Build, "BashieBlaster.fbx"), System.IO.Path.Combine(root, Fbx), true);
AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
mi.globalScale = body.globalScale;
mi.useFileScale = body.useFileScale;
mi.importBlendShapes = false;
mi.importCameras = false;
mi.importLights = false;
mi.importAnimation = false;
mi.animationType = ModelImporterAnimationType.None;
mi.materialImportMode = ModelImporterMaterialImportMode.None;
mi.importNormals = ModelImporterNormals.Import;
mi.SaveAndReimport();

// Prefab: the model under an empty root, with a Muzzle anchor at the tip of the barrel.
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try
{
    var prop = new GameObject("BashieBlaster");
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(prop, scene);
    var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx), scene);
    model.name = "Model";
    model.transform.SetParent(prop.transform, false);
    var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
    foreach (var r in renderers) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
    var bounds = renderers[0].bounds;
    foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
    if (bounds.size.z < bounds.size.x || bounds.size.z < bounds.size.y || bounds.max.z < -bounds.min.z)
        throw new InvalidOperationException("The barrel should point along +Z from the grip: bounds " + bounds);
    var filter = model.GetComponentInChildren<MeshFilter>(true);
    var toProp = prop.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
    var vertices = filter.sharedMesh.vertices.Select(v => toProp.MultiplyPoint3x4(v)).ToArray();
    // The muzzle: the middle of the barrel's front end.
    var tip = vertices.Where(v => v.z > bounds.max.z - 0.02f).ToArray();
    var muzzle = new GameObject("Muzzle").transform;
    muzzle.SetParent(prop.transform, false);
    muzzle.localPosition = new Vector3(0f, (tip.Min(v => v.y) + tip.Max(v => v.y)) / 2f, bounds.max.z);
    // The pump grip: the lowest part under the front half of the barrel, ahead of the trigger guard.
    var front = vertices.Where(v => v.z > 0.30f * bounds.max.z && v.z < 0.75f * bounds.max.z).ToArray();
    float bottom = front.Min(v => v.y);
    var grip = front.Where(v => v.y < bottom + 0.05f).ToArray();
    var pump = new GameObject("Pump").transform;
    pump.SetParent(prop.transform, false);
    pump.localPosition = new Vector3(0f, grip.Average(v => v.y), grip.Average(v => v.z));
    report["pump"] = pump.localPosition.ToString("F3");
    report["muzzle"] = muzzle.localPosition.ToString("F3");
    report["boundsMin"] = bounds.min.ToString("F3");
    report["boundsMax"] = bounds.max.ToString("F3");
    report["triangles"] = model.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.triangles.Length / 3);
    PrefabUtility.SaveAsPrefabAsset(prop, PrefabPath);
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }

// Grip: measured on her rig at rest.
var rig = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Package + "/Presentation/BashieRig.prefab"));
rig.hideFlags = HideFlags.HideAndDontSave;
Vector3 positionOffset, rotationOffset;
try
{
    rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    var animator = rig.GetComponentInChildren<Animator>();
    animator.Rebind();
    Transform Bone(HumanBodyBones b) => animator.GetBoneTransform(b) ?? throw new InvalidOperationException("No " + b);
    var hand = Bone(HumanBodyBones.RightHand);
    Vector3 middle = Bone(HumanBodyBones.RightMiddleProximal).position;
    // Her cartoon hand has four fingers: the ring finger is the last knuckle.
    Vector3 knuckles = Bone(HumanBodyBones.RightIndexProximal).position - Bone(HumanBodyBones.RightRingProximal).position;
    Vector3 along = (middle - hand.position).normalized;                         // wrist -> knuckles: the barrel
    Vector3 grip = (knuckles - Vector3.Dot(knuckles, along) * along).normalized;   // ring -> index: up the grip
    float rake = RakeDegrees * Mathf.Deg2Rad;
    var gripInProp = new Vector3(0f, Mathf.Cos(rake), Mathf.Sin(rake));
    var alongInProp = new Vector3(0f, -Mathf.Sin(rake), Mathf.Cos(rake));
    var world = Quaternion.LookRotation(along, grip) * Quaternion.Inverse(Quaternion.LookRotation(alongInProp, gripInProp));
    rotationOffset = (Quaternion.Inverse(hand.rotation) * world).eulerAngles;
    positionOffset = hand.InverseTransformPoint(hand.position + along * FistAlongHand * Vector3.Distance(middle, hand.position));
    report["hand"] = hand.name;
    report["handLossyScale"] = hand.lossyScale.ToString("F3");
}
finally { UnityEngine.Object.DestroyImmediate(rig); }

var config = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Entities.WeaponAttachConfig>(ConfigPath);
if (config == null)
{
    config = ScriptableObject.CreateInstance<SlopArena.Client.Entities.WeaponAttachConfig>();
    AssetDatabase.CreateAsset(config, ConfigPath);
}
config.Entries = new[]
{
    new SlopArena.Client.Entities.WeaponEntry
    {
        AttackSlot = SlotA, BoneName = "mixamorig:RightHand",
        Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),
        PositionOffset = positionOffset, RotationOffset = rotationOffset,
        TrailHiltAnchor = "Pump", TrailTipAnchor = "Muzzle",
    },
};
EditorUtility.SetDirty(config);
AssetDatabase.SaveAssetIfDirty(config);
var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(Package + "/CharacterAssetCatalog.asset");
catalog.WeaponConfig = config;
EditorUtility.SetDirty(catalog);
AssetDatabase.SaveAssetIfDirty(catalog);
report["positionOffset"] = positionOffset.ToString("F4");
report["rotationOffset"] = rotationOffset.ToString("F2");
return report;
