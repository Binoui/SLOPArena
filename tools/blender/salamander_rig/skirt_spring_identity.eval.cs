// Eval body: proves a SkirtBoneSpring change leaves Nyx's dress identical. Drives the saved NyxRig in a disposable
// preview scene through 360 scripted frames (leg strides, a high kick, a squat, raised arms, a run with a 180-degree
// turn and a jump, all on mapped bones and the root), steps her spring once per frame and records every skirt bone's
// world position and rotation. The first run writes the baseline; later runs compare against it, bit for bit.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/salamander_rig/skirt_spring_identity.eval.cs"
const string Baseline = "/home/binoui/Documents/projects/SlopArena/tmp/dusty-poncho/nyx-skirt-baseline.txt";
if (EditorApplication.isPlaying) throw new InvalidOperationException("Run the spring identity check with the Editor stopped.");
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var lines = new List<string>();
try
{
    var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CharacterPackages/nyx/Presentation/NyxRig.prefab"), scene);
    root.transform.localScale = Vector3.one * 1.0375f;
    root.GetComponentInChildren<Animator>().enabled = false;
    var spring = root.GetComponentInChildren<SlopArena.Client.Entities.SkirtBoneSpring>(true);
    var bones = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("mixamorig:")).ToDictionary(t => t.name);
    var skirt = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Nyx_Skirt_")).OrderBy(t => t.name).ToArray();
    var rest = bones.ToDictionary(p => p.Key, p => p.Value.localRotation);
    var restHips = bones["mixamorig:Hips"].localPosition;
    void Rotate(string name, Vector3 axis, float degrees) { var b = bones["mixamorig:" + name]; b.rotation = Quaternion.AngleAxis(degrees, axis) * b.rotation; }
    Vector3 position = Vector3.zero; float yaw = 0f, vy = 0f;
    for (int f = 0; f < 360; f++)
    {
        float t = f / 60f, phase = Mathf.Sin(t * 2f * Mathf.PI * 1.8f);
        foreach (var pair in rest) bones[pair.Key].localRotation = pair.Value;
        bones["mixamorig:Hips"].localPosition = restHips;
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        Rotate("LeftArm", root.transform.forward, 70f); Rotate("RightArm", root.transform.forward, -70f);
        if (f < 120) { Rotate("LeftUpLeg", root.transform.right, -50f * phase); Rotate("RightUpLeg", root.transform.right, 50f * phase); Rotate("LeftLeg", root.transform.right, 40f * Mathf.Max(0f, phase)); }
        else if (f < 160) Rotate("LeftUpLeg", root.transform.right, -95f * Mathf.Sin((f - 120) / 40f * Mathf.PI));
        else if (f < 200)
        {
            bones["mixamorig:Hips"].position += Vector3.down * .3f * Mathf.Sin((f - 160) / 40f * Mathf.PI);
            foreach (string side in new[] { "Left", "Right" }) { Rotate(side + "UpLeg", root.transform.right, -60f); Rotate(side + "Leg", root.transform.right, 100f); }
        }
        else if (f < 240) { Rotate("LeftArm", root.transform.forward, -140f); Rotate("RightArm", root.transform.forward, 140f); }
        if (f >= 60 && f < 240) position += Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * (9f / 60f);
        if (f == 150) yaw += 180f;
        if (f == 260) vy = 9f;
        if (f >= 260) { vy -= 22f / 60f; position.y = Mathf.Max(0f, position.y + vy / 60f); }
        spring.Step(1f / 60f);
        var sb = new System.Text.StringBuilder();
        foreach (var bone in skirt)
        {
            Vector3 p = bone.position; Quaternion q = bone.rotation;
            sb.Append(BitConverter.SingleToInt32Bits(p.x)).Append(' ').Append(BitConverter.SingleToInt32Bits(p.y)).Append(' ').Append(BitConverter.SingleToInt32Bits(p.z)).Append(' ')
              .Append(BitConverter.SingleToInt32Bits(q.x)).Append(' ').Append(BitConverter.SingleToInt32Bits(q.y)).Append(' ').Append(BitConverter.SingleToInt32Bits(q.z)).Append(' ')
              .Append(BitConverter.SingleToInt32Bits(q.w)).Append(' ');
        }
        lines.Add(sb.ToString());
    }
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
if (!System.IO.File.Exists(Baseline))
{
    System.IO.File.WriteAllLines(Baseline, lines);
    return new { recorded = Baseline, frames = lines.Count, values = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length };
}
var baseline = System.IO.File.ReadAllLines(Baseline);
int differing = Enumerable.Range(0, Math.Min(baseline.Length, lines.Count)).Count(i => baseline[i] != lines[i]);
return new { compared = Baseline, frames = lines.Count, baselineFrames = baseline.Length, differingFrames = differing, identical = differing == 0 && baseline.Length == lines.Count };
