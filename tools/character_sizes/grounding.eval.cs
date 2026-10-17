// Read-only Edit Mode diagnostic: foot contact of every bound clip, as the runtime plays it.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/character_sizes/grounding.eval.cs"
// For each Character Package the rig is placed like a grounded fighter (root scale = visualScale, root at
// floor + CapsuleHeight/2 + modelYOffset), each clip is evaluated through Playables without root motion
// or foot IK, and every skinned mesh is baked on every second frame. Per clip it reports the lowest
// vertex above the floor in cm: min (deepest contact), median and max. Grounded clips should have a
// min near 0; a positive min floats, a negative min sinks. Airborne clips are expected to float.
var catalogs = AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages" })
    .Select(g => AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(g)))
    .Where(c => c != null && c.Rig != null).OrderBy(c => c.PackageId, System.StringComparer.Ordinal).ToList();
System.Func<string, string, float> number = (text, key) => float.Parse(System.Text.RegularExpressions.Regex.Match(
    text, "\"" + key + "\"\\s*:\\s*(-?[0-9.eE+-]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
var report = new List<object>();
foreach (var catalog in catalogs)
{
    string json = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "CharacterPackages", catalog.PackageId, "character.json"));
    float visualScale = number(json, "visualScale"), modelYOffset = number(json, "modelYOffset"), capsuleHeight = number(json, "capsuleHeight");
    var go = UnityEngine.Object.Instantiate(catalog.Rig);
    go.hideFlags = HideFlags.HideAndDontSave;
    var clips = new List<string>();
    try
    {
        go.transform.SetPositionAndRotation(new Vector3(0f, capsuleHeight * 0.5f + modelYOffset, 0f), Quaternion.identity);
        go.transform.localScale = Vector3.one * visualScale;
        var animator = go.GetComponent<Animator>();
        animator.applyRootMotion = false;
        // Unseen rigs skip evaluation under CullUpdateTransforms; this instance is never saved.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(s => s.enabled && s.sharedMesh != null).ToArray();
        var baked = new Mesh();
        foreach (var binding in catalog.Bindings.Where(b => b != null && b.Clip != null))
        {
            var graph = UnityEngine.Playables.PlayableGraph.Create("character-grounding");
            try
            {
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
                var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, binding.Clip);
                playable.SetApplyFootIK(false);
                UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, playable);
                graph.Play();
                var lows = new List<float>();
                for (int frame = 0; frame < Mathf.Max(1, Mathf.CeilToInt(binding.Clip.length * 60f)); frame += 2)
                {
                    UnityEngine.Playables.PlayableExtensions.SetTime(playable, frame / 60d);
                    graph.Evaluate(0f);
                    float lowest = float.MaxValue;
                    foreach (var skin in skins)
                    {
                        skin.BakeMesh(baked, true);
                        var m = skin.transform.localToWorldMatrix;
                        foreach (var v in baked.vertices) lowest = Mathf.Min(lowest, m.MultiplyPoint3x4(v).y);
                    }
                    lows.Add(lowest * 100f);
                }
                lows.Sort();
                clips.Add($"{binding.SemanticId} ({binding.Clip.name}): min {lows[0]:F1} median {lows[lows.Count / 2]:F1} max {lows[lows.Count - 1]:F1} cm");
            }
            finally { graph.Destroy(); }
        }
        UnityEngine.Object.DestroyImmediate(baked);
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
    report.Add(new Dictionary<string, object> { ["package"] = catalog.PackageId, ["visualScale"] = visualScale, ["modelYOffset"] = modelYOffset, ["clips"] = clips });
}
return report;
