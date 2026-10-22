// Eval body: make the Sir Bonk candidate the live Bonk (catalog _rig), bind his accepted GenRA idle and run, and
// give the live weapon config the candidate's plank placement. Every other move keeps its bound clip.
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// Saving the catalog and weapon config persists a cook of every package and repins the roster
// (CharacterCookAssetPostprocessor). Back up CharacterPackages/bonk, content-cooked/ and Resources/Generated/
// first; to revert, restore those files (the catalog's old _rig is Resources/Characters/bonk.prefab).
const string Catalog = "Assets/CharacterPackages/bonk/CharacterAssetCatalog.asset";
const string LiveConfig = "Assets/CharacterPackages/bonk/BonkWeaponAttachConfig.asset";
const string CandidateConfig = "Assets/Art/Characters/bonk/SirBonkWeaponAttachConfig.asset";
const string Rig = "Assets/CharacterPackages/bonk/Presentation/SirBonkRig.prefab";
var promote = new[] {
    ("anim.idle", "Assets/Art/Experiments/GenRA/bonk/bonk-sir-idle-hero.anim", "Assets/CharacterPackages/bonk/Animations/bonk_sir_idle.anim"),
    ("anim.run", "Assets/Art/Experiments/GenRA/bonk/bonk-sir-run-charge.anim", "Assets/CharacterPackages/bonk/Animations/bonk_sir_run.anim"),
};
var report = new Dictionary<string, object>();

// Clips: package copies, so the package does not depend on experiment assets.
foreach (var (_, src, dst) in promote)
{
    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(dst) == null && !AssetDatabase.CopyAsset(src, dst))
        throw new InvalidOperationException("Could not copy " + src + " to " + dst);
}

// Weapon config: the candidate's entries (plank prefab, offsets converted to Sir Bonk's hand frame).
var live = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(LiveConfig));
var candidate = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(CandidateConfig));
var before = live.FindProperty("Entries").GetArrayElementAtIndex(0);
report["weaponBefore"] = new Dictionary<string, object> {
    ["prefab"] = AssetDatabase.GetAssetPath(before.FindPropertyRelative("Prefab").objectReferenceValue),
    ["position"] = before.FindPropertyRelative("PositionOffset").vector3Value.ToString("F4"),
    ["rotation"] = before.FindPropertyRelative("RotationOffset").vector3Value.ToString("F3") };
live.CopyFromSerializedProperty(candidate.FindProperty("Entries"));
live.ApplyModifiedPropertiesWithoutUndo();
var after = live.FindProperty("Entries").GetArrayElementAtIndex(0);
report["weaponAfter"] = new Dictionary<string, object> {
    ["prefab"] = AssetDatabase.GetAssetPath(after.FindPropertyRelative("Prefab").objectReferenceValue),
    ["position"] = after.FindPropertyRelative("PositionOffset").vector3Value.ToString("F4"),
    ["rotation"] = after.FindPropertyRelative("RotationOffset").vector3Value.ToString("F3") };

// Catalog: rig and the two bindings.
var catalog = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(Catalog));
var rigProperty = catalog.FindProperty("_rig");
report["rigBefore"] = AssetDatabase.GetAssetPath(rigProperty.objectReferenceValue);
rigProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Rig);
var bindings = catalog.FindProperty("_bindings");
var rebound = new List<object>();
foreach (var (semantic, _, dst) in promote)
{
    bool found = false;
    for (int i = 0; i < bindings.arraySize; i++)
    {
        var entry = bindings.GetArrayElementAtIndex(i);
        if (entry.FindPropertyRelative("_semanticId").stringValue != semantic) continue;
        var clip = entry.FindPropertyRelative("_clip");
        rebound.Add(semantic + ": " + AssetDatabase.GetAssetPath(clip.objectReferenceValue) + " -> " + dst);
        clip.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
        found = true;
    }
    if (!found) throw new InvalidOperationException("No binding for " + semantic);
}
catalog.ApplyModifiedPropertiesWithoutUndo();
AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(LiveConfig));
AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(Catalog));
report["rigAfter"] = AssetDatabase.GetAssetPath(rigProperty.objectReferenceValue);
report["rebound"] = rebound;
return report;
