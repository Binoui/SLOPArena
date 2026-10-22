// Eval body: place Sir Bonk's pot lid for Q (2026-10-22): the owner's shoulder placement B on the live config, and a
// frisbee grip in his left hand for the throw (a GenRA rig pack and, at wiring time, the in-hand lid during Q).
// Run with: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/<this file>"
// Step "grip" renders the candidate grips on his bind pose (arms out) and reports their offsets; step "pack" writes
// SirBonkLidWeaponAttachConfig.asset (pack-only: the plank and the lid in his left hand, both slot 0, the lid's
// anchors lidCentre/lidOut as joints). The live Bonk keeps the lid on his shoulder; slot A still plays Shoulder Bash.
// The grip: thumb on top of the rim and fingers under it, so the lid's face (+Y) points along the thumb side, its centre
// sits a radius out from the grip toward the palm, and its crown's up (+Z) runs along the fingers.
const string Step = "pack";   // "grip" (renders candidates), then "pack" with the chosen Palm sign
const float Palm = -1f;        // the lid's centre on the palm side (the grip renders of 2026-10-22: his T-pose palm faces down)
const string LiveConfig = "Assets/CharacterPackages/bonk/BonkWeaponAttachConfig.asset";
const string PackConfig = "Assets/Art/Characters/bonk/SirBonkLidWeaponAttachConfig.asset";
const string LidPrefab = "Assets/Art/Characters/bonk/Prefabs/sir_bonk_pot_lid.prefab";
const float VisualScale = 3.3f, LidRadius = 0.18f, GripAlongFingers = 0.05f;
// Placement B (capping the shoulder), lid_swap.eval.cs 2026-10-22, the owner's pick.
var ShoulderPosition = new Vector3(-0.034538608f, -0.00688988529f, 0.0119382422f);
var ShoulderRotation = new Vector3(24.8410225f, 77.3895645f, 91.06298f);
var report = new Dictionary<string, object>();
string root = System.IO.Path.GetDirectoryName(Application.dataPath);
string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "..", ".."));
var catalog = AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages/bonk" })
    .Select(g => AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(g))).First(c => c != null);
var lidPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LidPrefab);
var live = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Entities.WeaponAttachConfig>(LiveConfig);
var plankEntry = live.Entries.First(w => w != null && w.AttackSlot == 0 && w.Prefab != null && w.TrailStylePrefab != null);

// 1. The shoulder lid at B.
var shoulder = live.Entries.First(e => e != null && e.Prefab == lidPrefab);
if (shoulder.PositionOffset != ShoulderPosition || shoulder.RotationOffset != ShoulderRotation)
{
    shoulder.PositionOffset = ShoulderPosition;
    shoulder.RotationOffset = ShoulderRotation;
    EditorUtility.SetDirty(live);
    AssetDatabase.SaveAssetIfDirty(live);
    report["shoulder"] = "set to B";
}

// 2. Grip candidates on the bind pose, in the hand's frame.
var rig = UnityEngine.Object.Instantiate(catalog.Rig);
rig.hideFlags = HideFlags.HideAndDontSave;
var grips = new Dictionary<float, (Vector3 position, Vector3 rotation)>();
try
{
    rig.transform.localScale = Vector3.one * VisualScale;
    var bones = rig.GetComponentsInChildren<Transform>(true);
    Transform Find(string n) => bones.First(t => t.name == n);
    var hand = Find("mixamorig:LeftHand");
    var fingers = (Find("mixamorig:LeftHandMiddle1").position - hand.position).normalized;
    var thumbWay = Vector3.ProjectOnPlane(Find("mixamorig:LeftHandThumb1").position - hand.position, fingers).normalized;
    foreach (float palm in new[] { 1f, -1f })
    {
        var towardCentre = Vector3.Cross(fingers, thumbWay).normalized * palm;
        var grip = hand.position + fingers * GripAlongFingers;
        var centre = grip + towardCentre * LidRadius;
        var frame = Quaternion.LookRotation(fingers, thumbWay);   // +Z along the fingers, +Y (the face) the thumb side
        var position = hand.InverseTransformPoint(centre);
        var rotation = (Quaternion.Inverse(hand.rotation) * frame).eulerAngles;
        grips[palm] = (position, rotation);
        report["grip_" + (palm > 0 ? "plus" : "minus")] = new { positionOffset = new[] { position.x, position.y, position.z },
            rotationOffset = new[] { rotation.x, rotation.y, rotation.z } };
    }

    if (Step == "grip")
    {
        string outDir = System.IO.Path.Combine(repo, "tmp", "bonk-q", "lid-grip", System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        System.IO.Directory.CreateDirectory(outDir);
        foreach (var palm in new[] { 1f, -1f })
        {
            var utility = new PreviewRenderUtility(true);
            var lid = UnityEngine.Object.Instantiate(lidPrefab);
            lid.hideFlags = HideFlags.HideAndDontSave;
            var copy = UnityEngine.Object.Instantiate(rig);
            copy.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var copyHand = copy.GetComponentsInChildren<Transform>(true).First(t => t.name == "mixamorig:LeftHand");
                lid.transform.SetPositionAndRotation(copyHand.TransformPoint(grips[palm].position), copyHand.rotation * Quaternion.Euler(grips[palm].rotation));
                var camera = utility.camera;
                camera.clearFlags = CameraClearFlags.Color;
                camera.backgroundColor = new Color(0.84f, 0.85f, 0.88f, 1f);
                camera.nearClipPlane = 0.02f; camera.farClipPlane = 30f; camera.fieldOfView = 30f;
                utility.lights[0].type = LightType.Directional; utility.lights[0].intensity = 1.25f;
                utility.lights[0].transform.rotation = Quaternion.Euler(35f, 160f, 0f);
                utility.lights[1].type = LightType.Directional; utility.lights[1].intensity = 0.6f;
                utility.lights[1].transform.rotation = Quaternion.Euler(20f, -30f, 0f);
                utility.ambientColor = new Color(0.5f, 0.5f, 0.55f);
                utility.AddSingleGO(copy); utility.AddSingleGO(lid);
                var focus = copyHand.position;
                foreach (var (view, direction) in new[] { ("front", new Vector3(0f, 0.25f, 1f)), ("above", new Vector3(0f, 1f, 0.35f)),
                             ("side", new Vector3(-1f, 0.2f, 0.2f)) })
                {
                    camera.transform.position = focus + direction.normalized * 1.6f;
                    camera.transform.LookAt(focus);
                    utility.BeginPreview(new Rect(0, 0, 700, 700), GUIStyle.none);
                    camera.Render();
                    var texture = utility.EndPreview();
                    var image = new Texture2D(700, 700, TextureFormat.RGBA32, false);
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = (RenderTexture)texture;
                        image.ReadPixels(new Rect(0, 0, 700, 700), 0, 0);
                        image.Apply();
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, $"grip_{(palm > 0 ? "plus" : "minus")}_{view}.png"), image.EncodeToPNG());
                    }
                    finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(lid);
                UnityEngine.Object.DestroyImmediate(copy);
                utility.Cleanup();
            }
        }
        report["output"] = outDir;
    }
}
finally { UnityEngine.Object.DestroyImmediate(rig); }

// 3. The pack-only config: the plank and the lid in his left hand, both always attached.
if (Step == "pack")
{
    var config = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Entities.WeaponAttachConfig>(PackConfig);
    if (config == null)
    {
        config = ScriptableObject.CreateInstance<SlopArena.Client.Entities.WeaponAttachConfig>();
        AssetDatabase.CreateAsset(config, PackConfig);
    }
    var (position, rotation) = grips[Palm];
    config.Entries = new[]
    {
        plankEntry,
        new SlopArena.Client.Entities.WeaponEntry
        {
            AttackSlot = 0, BoneName = "mixamorig:LeftHand", Prefab = lidPrefab, PositionOffset = position, RotationOffset = rotation,
            HitboxMotionTime = 0.12f, TrailBladeWidth = 1f, TrailHiltAnchor = "lidCentre", TrailTipAnchor = "lidOut",
        },
    };
    EditorUtility.SetDirty(config);
    AssetDatabase.SaveAssetIfDirty(config);
    report["pack"] = PackConfig;
}
return report;
