// Eval body: give the live Sir Bonk the lid-less body from lid_split.py and his pot lid as a shoulder prop (2026-10-22).
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// Run lid_import.eval.cs first and paste its weaponAttach offsets below. This eval renders his idle with the current
// body (before), copies the split body over sir_bonk.fbx, adds the lid to BonkWeaponAttachConfig on mixamorig:LeftArm
// (attack slot 0: always shown until Q's hide rules exist), checks the avatar and the mesh, and renders the idle again
// (after) plus placement options for the owner. Saving the weapon config cooks every package and repins the roster:
// back up CharacterPackages/bonk, content-cooked/ and Resources/Generated/ first, then cook and verify.
const string SplitBody = "/home/binoui/Documents/projects/SlopArena/tmp/bonk-q/rig/SirBonk.fbx";
const string BodyFbx = "Assets/Art/Characters/bonk/Models/sir_bonk.fbx";
const string LidPrefab = "Assets/Art/Characters/bonk/Prefabs/sir_bonk_pot_lid.prefab";
const string LiveConfig = "Assets/CharacterPackages/bonk/BonkWeaponAttachConfig.asset";
const string Bone = "mixamorig:LeftArm";
const bool Swap = false;   // true for the swap itself (done 2026-10-22); false re-renders the options
var LidPosition = new Vector3(-0.0350665674f, 0.007323575f, 0.009541101f);
var LidRotation = new Vector3(21.5911579f, 51.466198f, 73.6662445f);
const float VisualScale = 3.3f;
// Placement options for the owner, from the lid as generated, in his idle: (name, lift toward his head in metres,
// out from the shoulder in metres, tilt of the dome toward the sky in degrees, turn of its face toward his back in
// degrees, scale). The match camera sits behind him, where the lid as generated hardly shows.
var Options = new[] { ("A_as_generated", 0f, 0f, 0f, 0f, 1f), ("B_capping_the_shoulder", 0.05f, 0.01f, 25f, 0f, 1f),
    ("C_proud_and_bigger", 0.04f, 0.03f, 15f, 0f, 1.15f), ("D_turned_back", 0.05f, 0.02f, 20f, 45f, 1.1f) };
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);
string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "..", ".."));
string outDir = System.IO.Path.Combine(repo, "tmp", "bonk-q", "lid-swap", System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
System.IO.Directory.CreateDirectory(outDir);
var catalog = AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages/bonk" })
    .Select(g => AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(g))).First(c => c != null);
var lidPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LidPrefab);
var idleClip = catalog.Bindings.First(b => b.SemanticId == "anim.idle").Clip;
var plankEntry = catalog.WeaponConfig.Entries.First(w => w != null && w.AttackSlot == 0 && w.Prefab != null);

object Avatar()
{
    var animator = catalog.Rig.GetComponent<Animator>();
    var avatar = animator.avatar;
    return new { avatar = avatar != null ? avatar.name : null, valid = avatar != null && avatar.isValid, human = avatar != null && avatar.isHuman,
        humanScale = GetHumanScale(animator),
        verts = catalog.Rig.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh.vertexCount };
}
float GetHumanScale(Animator prefabAnimator)
{
    var probe = UnityEngine.Object.Instantiate(prefabAnimator.gameObject);
    probe.hideFlags = HideFlags.HideAndDontSave;
    try { var a = probe.GetComponent<Animator>(); a.Rebind(); return a.humanScale; }
    finally { UnityEngine.Object.DestroyImmediate(probe); }
}

// Renders his idle: the rig, the plank, and (with the lid-less body) the lid placed like WeaponAttach places it.
void Render(string tag, (string name, float lift, float outward, float tilt, float back, float scale)? lidOption)
{
    var utility = new PreviewRenderUtility(true);
    var spawned = new List<GameObject>();
    UnityEngine.Playables.PlayableGraph graph = default;
    try
    {
        var camera = utility.camera;
        camera.clearFlags = CameraClearFlags.Color;
        camera.backgroundColor = new Color(0.84f, 0.85f, 0.88f, 1f);
        camera.nearClipPlane = 0.05f; camera.farClipPlane = 50f;
        utility.lights[0].type = LightType.Directional; utility.lights[0].intensity = 1.25f;
        utility.lights[0].transform.rotation = Quaternion.Euler(35f, 160f, 0f);
        utility.lights[1].type = LightType.Directional; utility.lights[1].intensity = 0.6f;
        utility.lights[1].transform.rotation = Quaternion.Euler(20f, -30f, 0f);
        utility.ambientColor = new Color(0.5f, 0.5f, 0.55f);
        var go = UnityEngine.Object.Instantiate(catalog.Rig);
        go.hideFlags = HideFlags.HideAndDontSave; spawned.Add(go);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one * VisualScale;
        var animator = go.GetComponent<Animator>();
        animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
        graph = UnityEngine.Playables.PlayableGraph.Create("sir-bonk-lid");
        graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
        var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
        var clip = UnityEngine.Animations.AnimationClipPlayable.Create(graph, idleClip);
        clip.SetApplyFootIK(false);
        UnityEngine.Playables.PlayableExtensions.SetTime(clip, 0.0);
        UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, clip);
        graph.Play();
        graph.Evaluate(0f);
        var bones = go.GetComponentsInChildren<Transform>(true);
        Transform Find(string n) => bones.First(t => t.name == n);
        var plank = UnityEngine.Object.Instantiate(plankEntry.Prefab);
        plank.hideFlags = HideFlags.HideAndDontSave; spawned.Add(plank);
        var hand = Find(plankEntry.BoneName);
        plank.transform.SetPositionAndRotation(hand.TransformPoint(plankEntry.PositionOffset), hand.rotation * Quaternion.Euler(plankEntry.RotationOffset));
        utility.AddSingleGO(go); utility.AddSingleGO(plank);
        var bone = Find(Bone);
        if (lidOption.HasValue)
        {
            var o = lidOption.Value;
            var lid = UnityEngine.Object.Instantiate(lidPrefab);
            lid.hideFlags = HideFlags.HideAndDontSave; spawned.Add(lid);
            var position = bone.TransformPoint(LidPosition);
            var rotation = bone.rotation * Quaternion.Euler(LidRotation);
            var head = Find("mixamorig:Head").position;
            var outward = rotation * Vector3.up;   // the dome's outer face
            var lift = Vector3.ProjectOnPlane(head - position, go.transform.forward).normalized;
            position += lift * o.lift + outward * o.outward;
            rotation = Quaternion.AngleAxis(o.tilt, Vector3.Cross(outward, Vector3.up).normalized) * rotation;
            var flat = Vector3.ProjectOnPlane(rotation * Vector3.up, Vector3.up).normalized;
            rotation = Quaternion.FromToRotation(flat, Vector3.Slerp(flat, -go.transform.forward, o.back / 90f)) * rotation;
            lid.transform.SetPositionAndRotation(position, rotation);
            lid.transform.localScale = Vector3.one * o.scale;
            utility.AddSingleGO(lid);
            var offsetPosition = bone.InverseTransformPoint(position);
            var offsetRotation = (Quaternion.Inverse(bone.rotation) * rotation).eulerAngles;
            report["option_" + o.name] = new { positionOffset = new[] { offsetPosition.x, offsetPosition.y, offsetPosition.z },
                rotationOffset = new[] { offsetRotation.x, offsetRotation.y, offsetRotation.z }, scale = o.scale };
        }
        var focus = bone.position + Vector3.up * 0.15f;
        // "behind" is the match camera's angle (15 degrees up from behind him), framing his upper body.
        foreach (var (view, yaw, pitch, distance, fov) in new[] { ("front_left", -40f, 10f, 2.4f, 28f), ("behind", 180f, 15f, 4.5f, 30f),
                     ("behind_left", -155f, 15f, 4.5f, 30f), ("side_left", -90f, 5f, 2.6f, 28f), ("full_front_left", -35f, 8f, 7f, 30f) })
        {
            var direction = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            var target = view.StartsWith("full") ? new Vector3(0f, 1.1f, 0f) : (view.StartsWith("behind") ? new Vector3(0f, 1.3f, 0f) : focus);
            camera.fieldOfView = fov;
            camera.transform.position = target + direction * distance;
            camera.transform.LookAt(target);
            utility.BeginPreview(new Rect(0, 0, 900, 900), GUIStyle.none);
            camera.Render();
            var texture = utility.EndPreview();
            var image = new Texture2D(900, 900, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = (RenderTexture)texture;
                image.ReadPixels(new Rect(0, 0, 900, 900), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, $"{tag}_{view}.png"), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }
    }
    finally
    {
        if (graph.IsValid()) graph.Destroy();
        foreach (var s in spawned) if (s != null) UnityEngine.Object.DestroyImmediate(s);
        utility.Cleanup();
    }
}

report["before"] = Avatar();
Render("before", null);
if (Swap)
{
    System.IO.File.Copy(SplitBody, System.IO.Path.Combine(root, BodyFbx), true);
    AssetDatabase.ImportAsset(BodyFbx, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    var config = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Entities.WeaponAttachConfig>(LiveConfig);
    var entries = config.Entries.Where(e => e == null || e.Prefab != lidPrefab).ToList();
    entries.Add(new SlopArena.Client.Entities.WeaponEntry
    {
        AttackSlot = 0, BoneName = Bone, Prefab = lidPrefab, PositionOffset = LidPosition, RotationOffset = LidRotation,
        HitboxMotionTime = 0.12f, TrailBladeWidth = 1f, TrailHiltAnchor = "", TrailTipAnchor = "",
    });
    config.Entries = entries.ToArray();
    EditorUtility.SetDirty(config);
    AssetDatabase.SaveAssetIfDirty(config);
    report["after"] = Avatar();
    report["configEntries"] = config.Entries.Select(e => e.Prefab != null ? e.Prefab.name + " slot " + e.AttackSlot + " on " + e.BoneName : "null").ToArray();
}
foreach (var option in Options) Render("after_" + option.Item1, option);
report["output"] = outDir;
return report;
