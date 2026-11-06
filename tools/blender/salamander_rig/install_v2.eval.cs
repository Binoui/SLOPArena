// Eval body: install build_v2.py's model (the owner's Tripo v2, 2026-11-05) over Dusty's live FBX and rebuild the Unity side.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/salamander_rig/install_v2.eval.cs" --timeout 600000
// Then retarget_clips.eval.cs, sloparena.character.inspect and sloparena.character.cook --target salamander.
// - Models/salamander.fbx is replaced in place: its GUID, .meta import settings, the catalog's _rig (SalamanderRig.prefab)
//   and the prefab's fileIDs stay. The Avatar skeleton is regenerated from the new T-pose and the saved 46-bone Humanoid
//   mapping re-applied (body, thumb, index, middle and ring; the new pinky is unmapped).
// - Textures/Salamander_Body.png gets the new map; Salamander_Body.mat (the roster's URP Lit body settings) keeps pointing at it.
// - SalamanderRig.prefab renders the one body mesh with Salamander_Body.mat, drops the overrides of the removed
//   hat, jeans and poncho renderers, and gives Pinky1-3 a RotationConstraint on Ring1-3 (equal rest frames, no offset):
//   his clips leave the Humanoid Little finger at 0, so the pinky curls with the ring finger instead.
// - The poncho and jeans assets go (their GUIDs were grepped: only each other, this FBX's remaps and the prefab used them).
// - Before the swap it writes the live Hat, Mustache, Straw and Tail1-5 rest transforms to RestFile (once: the poncho
//   rig his clips were authored on), and every time to RestDir/rig-tail2-<Tail2 length>.txt (the rig it replaces), so
//   retarget_clips.eval.cs can tell which rig a clip's curves were last written for (the 1.25x size pass, 2026-11-05).
// - It saves only the assets it changes: AssetDatabase.SaveAssets() would also flush other threads' dirty assets.
const string Build = "/home/binoui/Documents/projects/SlopArena/artifacts/salamander-model-v2/build";
const string RestFile = "/home/binoui/Documents/projects/SlopArena/artifacts/salamander-model-v2/old_rest_locals.txt";
const string RestDir = "/home/binoui/Documents/projects/SlopArena/artifacts/salamander-model-v2/rests";
const string Dir = "Assets/Art/Characters/salamander";
const string Fbx = Dir + "/Models/salamander.fbx";
const string Tex = Dir + "/Textures/Salamander_Body.png";
const string Mat = Dir + "/Materials/Salamander_Body.mat";
const string PrefabPath = "Assets/CharacterPackages/salamander/Presentation/SalamanderRig.prefab";
string[] Generic = { "Hat", "Mustache", "Straw", "Tail1", "Tail2", "Tail3", "Tail4", "Tail5" };
string[] Delete =
{
    "Assets/CharacterPackages/salamander/Presentation/SalamanderRig_SpringPoncho.prefab",
    Dir + "/Models/Candidates/salamander_spring_poncho.fbx",
    Dir + "/Materials/Salamander_Poncho_TwoSided.mat",
    Dir + "/Materials/Salamander_Poncho.mat",
    Dir + "/Materials/Salamander_Jeans.mat",
    Dir + "/Textures/Salamander_Poncho.png",
    Dir + "/Textures/Salamander_Jeans.png",
};
var report = new Dictionary<string, object>();
string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
foreach (var source in new[] { "salamander.fbx", "textures/Salamander_Body.png" })
    if (!System.IO.File.Exists(System.IO.Path.Combine(Build, source)))
        throw new System.IO.FileNotFoundException("Missing build_v2.py output", source);

Transform FindDeep(Transform t, string n)
{
    if (t.name == n) return t;
    foreach (Transform c in t) { var r = FindDeep(c, n); if (r != null) return r; }
    return null;
}

string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

Dictionary<string, object> Probe()
{
    var probe = new Dictionary<string, object>();
    var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
    go.hideFlags = HideFlags.HideAndDontSave;
    try
    {
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        var a = go.GetComponentInChildren<Animator>();
        a.Rebind();
        probe["humanScale"] = a.humanScale;
        probe["avatarValid"] = a.avatar != null && a.avatar.isValid && a.avatar.isHuman;
        var meshes = new Dictionary<string, object>();
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in baked.vertices) { float y = smr.transform.localToWorldMatrix.MultiplyPoint3x4(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
            UnityEngine.Object.DestroyImmediate(baked);
            meshes[smr.name] = new Dictionary<string, object>
            {
                ["vertices"] = smr.sharedMesh.vertexCount, ["triangles"] = smr.sharedMesh.triangles.Length / 3,
                ["bones"] = smr.bones.Count(b => b != null),
                ["materials"] = smr.sharedMaterials.Select(m => m != null ? AssetDatabase.GetAssetPath(m) : null).ToArray(),
                ["minY"] = lo, ["maxY"] = hi,
            };
        }
        probe["meshes"] = meshes;
        probe["hipsY"] = a.GetBoneTransform(HumanBodyBones.Hips).position.y;
        probe["headY"] = a.GetBoneTransform(HumanBodyBones.Head).position.y;
        probe["transforms"] = go.GetComponentsInChildren<Transform>(true).Length;
        var pinky = new Dictionary<string, object>();
        foreach (var side in new[] { "Left", "Right" })
            for (int k = 1; k <= 3; k++)
            {
                var p = FindDeep(go.transform, $"mixamorig:{side}HandPinky{k}");
                var r = FindDeep(go.transform, $"mixamorig:{side}HandRing{k}");
                if (p == null || r == null) continue;
                var c = p.GetComponent<UnityEngine.Animations.RotationConstraint>();
                pinky[$"{side}{k}"] = new Dictionary<string, object>
                {
                    ["restAngleToRing"] = Quaternion.Angle(p.rotation, r.rotation), ["constraint"] = c != null,
                    ["source"] = c != null && c.sourceCount > 0 ? c.GetSource(0).sourceTransform?.name : null,
                    ["active"] = c != null && c.constraintActive,
                };
            }
        probe["pinky"] = pinky;
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
    return probe;
}

// 1. The live generic bones' rest transforms (local to their parents): once for the poncho rig, always for the rig replaced.
(List<string> lines, float tail2) LiveRests(string header)
{
    var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
    go.hideFlags = HideFlags.HideAndDontSave;
    try
    {
        var lines = new List<string> { header };
        foreach (var g in Generic)
        {
            var t = FindDeep(go.transform, g) ?? throw new InvalidOperationException("Live rig lacks " + g);
            Vector3 p = t.localPosition; Quaternion q = t.localRotation;
            lines.Add(string.Join(" ", g, F(p.x), F(p.y), F(p.z), F(q.x), F(q.y), F(q.z), F(q.w)));
        }
        return (lines, FindDeep(go.transform, "Tail2").localPosition.y);
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
}
if (!System.IO.File.Exists(RestFile))
{
    System.IO.File.WriteAllLines(RestFile, LiveRests("# bone px py pz qx qy qz qw (local to parent) from the live salamander.fbx before the Tripo v2 swap").lines);
    report["restFile"] = "written";
}
else report["restFile"] = "kept";
{
    var (lines, tail2) = LiveRests("# bone px py pz qx qy qz qw (local to parent) of the salamander rig install_v2.eval.cs replaced, " + System.DateTime.Now.ToString("s"));
    System.IO.Directory.CreateDirectory(RestDir);
    string file = System.IO.Path.Combine(RestDir, "rig-tail2-" + tail2.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
    System.IO.File.WriteAllLines(file, lines);
    report["replacedRigRests"] = file;
}

report["before"] = Probe();

// 2. Texture: the new map over the live one (GUID, importer settings and the material's reference stay).
System.IO.File.Copy(System.IO.Path.Combine(Build, "textures/Salamander_Body.png"), System.IO.Path.Combine(projectRoot, Tex), true);
AssetDatabase.ImportAsset(Tex, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
var t2d = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex);
report["texture"] = new Dictionary<string, object> { ["size"] = new[] { t2d.width, t2d.height }, ["maxTextureSize"] = ((TextureImporter)AssetImporter.GetAtPath(Tex)).maxTextureSize };
var mat = AssetDatabase.LoadAssetAtPath<Material>(Mat);
if (mat.GetTexture("_BaseMap") != t2d) { mat.SetTexture("_BaseMap", t2d); if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", t2d); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); }
report["material"] = new Dictionary<string, object> { ["shader"] = mat.shader.name, ["baseColor"] = mat.GetColor("_BaseColor").ToString(), ["smoothness"] = mat.GetFloat("_Smoothness"), ["cull"] = mat.GetFloat("_Cull") };

// 3. Model: in place; the Avatar skeleton from the new T-pose, the saved mapping re-applied.
var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
var saved = mi.humanDescription;
System.IO.File.Copy(System.IO.Path.Combine(Build, "salamander.fbx"), System.IO.Path.Combine(projectRoot, Fbx), true);
AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
foreach (var gone in new[] { "Salamander_Jeans", "Salamander_Poncho" })
    mi.RemoveRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), gone));
mi.humanDescription = new HumanDescription();
mi.autoGenerateAvatarMappingIfUnspecified = true;
mi.SaveAndReimport();
mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
var hd = mi.humanDescription;
var skeletonNames = new HashSet<string>(hd.skeleton.Select(s => s.name));
var missing = saved.human.Where(h => !skeletonNames.Contains(h.boneName)).Select(h => h.boneName).ToArray();
hd.human = saved.human.Where(h => skeletonNames.Contains(h.boneName)).ToArray();
hd.upperArmTwist = saved.upperArmTwist; hd.lowerArmTwist = saved.lowerArmTwist;
hd.upperLegTwist = saved.upperLegTwist; hd.lowerLegTwist = saved.lowerLegTwist;
hd.armStretch = saved.armStretch; hd.legStretch = saved.legStretch; hd.feetSpacing = saved.feetSpacing;
hd.hasTranslationDoF = saved.hasTranslationDoF;
mi.humanDescription = hd;
mi.SaveAndReimport();
report["mapped"] = hd.human.Length;
report["missingFromNewSkeleton"] = missing;
report["skeletonBones"] = hd.skeleton.Length;
if (missing.Length != 0) throw new InvalidOperationException("The new skeleton lacks mapped bones: " + string.Join(", ", missing));

// 4. Prefab: one body renderer and material, no stale overrides, pinky constraints.
var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
try
{
    var renderers = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    foreach (var r in renderers)
        r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
    report["prefabRenderers"] = renderers.Select(r => r.name).ToArray();
    var added = new List<string>();
    foreach (var side in new[] { "Left", "Right" })
        for (int k = 1; k <= 3; k++)
        {
            var p = FindDeep(contents.transform, $"mixamorig:{side}HandPinky{k}") ?? throw new InvalidOperationException("No pinky " + side + k);
            var ring = FindDeep(contents.transform, $"mixamorig:{side}HandRing{k}");
            var c = p.GetComponent<UnityEngine.Animations.RotationConstraint>();
            if (c == null) c = p.gameObject.AddComponent<UnityEngine.Animations.RotationConstraint>();
            while (c.sourceCount > 0) c.RemoveSource(0);
            c.AddSource(new UnityEngine.Animations.ConstraintSource { sourceTransform = ring, weight = 1f });
            c.weight = 1f;
            c.rotationAtRest = p.localEulerAngles;
            c.rotationOffset = Vector3.zero;  // build_v2.py gives each Pinky bone its Ring bone's rest frame
            c.rotationAxis = UnityEngine.Animations.Axis.X | UnityEngine.Animations.Axis.Y | UnityEngine.Animations.Axis.Z;
            c.locked = true;
            c.constraintActive = true;
            added.Add(p.name);
        }
    report["pinkyConstraints"] = added;
    PrefabUtility.RemoveUnusedOverrides(new[] { contents }, InteractionMode.AutomatedAction);
    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
}
finally { PrefabUtility.UnloadPrefabContents(contents); }

// 5. Poncho and jeans leftovers.
var deleted = new Dictionary<string, object>();
foreach (var path in Delete)
    deleted[path] = AssetDatabase.LoadMainAssetAtPath(path) != null ? (object)AssetDatabase.DeleteAsset(path) : "absent";
string candidates = Dir + "/Models/Candidates";
if (AssetDatabase.IsValidFolder(candidates) && AssetDatabase.FindAssets("", new[] { candidates }).Length == 0)
    deleted[candidates] = AssetDatabase.DeleteAsset(candidates);
report["deleted"] = deleted;

var after = Probe();
report["after"] = after;
if (!(bool)after["avatarValid"]) throw new InvalidOperationException("The new salamander Avatar is invalid.");
report["humanScaleChangePercent"] = ((float)after["humanScale"] / (float)((Dictionary<string, object>)report["before"])["humanScale"] - 1f) * 100f;
return report;
