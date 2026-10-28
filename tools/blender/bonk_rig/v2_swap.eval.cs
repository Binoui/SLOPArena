// Eval body (2026-10-28): make Sir Bonk v2 the live Bonk. Run after v2_import.eval.cs (body without the left lid,
// from lid_split.py) and v2_lid_import.eval.cs (the v2 lid prop).
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// - moves SirBonkV2Rig.prefab into the package and makes it the catalog _rig;
// - the v2 weapon config (live entries in v2 bone frames) gets the v2 lid: on his left shoulder (lid_import's offset
//   on mixamorig:LeftArm) and in his left hand for Q (same lid-centred frame, so the converted grip still holds);
// - the thrown lid (BonkPotLidProjectile) nests the v2 lid instead of the v1 one;
// - strips Cape_* curves from every editable clip the catalog binds: the clips keyed the v1 cape bones' frames and
//   BoneChainSpring takes its rest from the first animated frame, so v2's cape flipped. The spring drives the cape.
// Saving the catalog persists a cook of every package. Back up CharacterPackages/bonk, content-cooked/ and
// Resources/Generated/ first (2026-10-28: artifacts/sir-bonk-v2-20261028/backup/before-swap/).
const string Catalog = "Assets/CharacterPackages/bonk/CharacterAssetCatalog.asset";
const string LiveConfig = "Assets/CharacterPackages/bonk/BonkWeaponAttachConfig.asset";
const string CandidateConfig = "Assets/Art/Characters/bonk/SirBonkV2WeaponAttachConfig.asset";
const string RigSrc = "Assets/Art/Characters/bonk/Prefabs/SirBonkV2Rig.prefab";
const string RigDst = "Assets/CharacterPackages/bonk/Presentation/SirBonkV2Rig.prefab";
const string OldLid = "Assets/Art/Characters/bonk/Prefabs/sir_bonk_pot_lid.prefab";
const string NewLid = "Assets/Art/Characters/bonk/Prefabs/sir_bonk_v2_pot_lid.prefab";
const string Projectile = "Assets/CharacterPackages/bonk/Presentation/BonkPotLidProjectile.prefab";
var shoulderLidPosition = new Vector3(0.01429392f, 0.0137158483f, -0.0437799841f);   // v2_lid_import.eval.cs report
var shoulderLidRotation = new Vector3(43.90958f, 241.335114f, 75.30024f);
var report = new Dictionary<string, object>();

if (AssetDatabase.LoadAssetAtPath<GameObject>(RigDst) == null)
{
    string err = AssetDatabase.MoveAsset(RigSrc, RigDst);
    if (!string.IsNullOrEmpty(err)) throw new InvalidOperationException("Move rig: " + err);
}
var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigDst);
var oldLid = AssetDatabase.LoadAssetAtPath<GameObject>(OldLid);
var newLid = AssetDatabase.LoadAssetAtPath<GameObject>(NewLid);
if (rig == null || newLid == null || oldLid == null) throw new InvalidOperationException("missing rig or lid prefabs");

// Weapon config: v2 lid in both lid entries.
var candidate = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(CandidateConfig));
var entries = candidate.FindProperty("Entries");
var lidEntries = new List<object>();
for (int i = 0; i < entries.arraySize; i++)
{
    var e = entries.GetArrayElementAtIndex(i);
    var prefab = e.FindPropertyRelative("Prefab");
    if (prefab.objectReferenceValue != oldLid) continue;
    prefab.objectReferenceValue = newLid;
    string bone = e.FindPropertyRelative("BoneName").stringValue;
    if (bone == "mixamorig:LeftArm")
    {
        e.FindPropertyRelative("PositionOffset").vector3Value = shoulderLidPosition;
        e.FindPropertyRelative("RotationOffset").vector3Value = shoulderLidRotation;
    }
    lidEntries.Add(bone);
}
if (lidEntries.Count == 0) throw new InvalidOperationException("no lid entries in " + CandidateConfig);
candidate.ApplyModifiedPropertiesWithoutUndo();
AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(CandidateConfig));
report["lidEntries"] = lidEntries;

// The thrown lid.
var contents = PrefabUtility.LoadPrefabContents(Projectile);
int replaced = 0;
try
{
    foreach (var t in contents.GetComponentsInChildren<Transform>(true))
    {
        var go = t.gameObject;
        if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
        if (PrefabUtility.GetCorrespondingObjectFromSource(go) != oldLid) continue;
        PrefabUtility.ReplacePrefabAssetOfPrefabInstance(go, newLid, InteractionMode.AutomatedAction);
        replaced++;
    }
    if (replaced > 0) PrefabUtility.SaveAsPrefabAsset(contents, Projectile);
}
finally { PrefabUtility.UnloadPrefabContents(contents); }
report["projectileLidsReplaced"] = replaced;

// Clips: drop the cape keys.
var catalogAsset = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(Catalog);
var stripped = new List<object>();
var skipped = new List<string>();
foreach (var clip in catalogAsset.Bindings.Select(b => b.Clip).Where(c => c != null).Distinct())
{
    string path = AssetDatabase.GetAssetPath(clip);
    var cape = AnimationUtility.GetCurveBindings(clip).Where(b => b.path.Contains("Cape_")).ToArray();
    if (cape.Length == 0) continue;
    if (!path.EndsWith(".anim")) { skipped.Add(path); continue; }
    foreach (var b in cape) AnimationUtility.SetEditorCurve(clip, b, null);
    EditorUtility.SetDirty(clip);
    AssetDatabase.SaveAssetIfDirty(clip);
    stripped.Add(path + " (" + cape.Length + ")");
}
report["capeCurvesStripped"] = stripped;
report["capeCurvesSkippedReadOnly"] = skipped;

// Live config and catalog.
var live = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(LiveConfig));
live.CopyFromSerializedProperty(candidate.FindProperty("Entries"));
live.ApplyModifiedPropertiesWithoutUndo();
var catalog = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(Catalog));
var rigProperty = catalog.FindProperty("_rig");
report["rigBefore"] = AssetDatabase.GetAssetPath(rigProperty.objectReferenceValue);
rigProperty.objectReferenceValue = rig;
catalog.ApplyModifiedPropertiesWithoutUndo();
AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(LiveConfig));
AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(Catalog));
report["rigAfter"] = AssetDatabase.GetAssetPath(rigProperty.objectReferenceValue);
return report;
