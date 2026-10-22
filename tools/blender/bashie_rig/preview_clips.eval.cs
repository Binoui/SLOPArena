// Play Mode eval body, after preview_swap.eval.cs: Bashie's own candidate idle and run replace Manki's on the swapped
// fighter, in memory only. The fighter gets a runtime copy of its animation catalog with those two clips changed; the
// catalog asset is untouched, and stopping Play Mode discards the copy. The renderer replays the locomotion clip every
// frame, so the change shows at once. Run preview_ground.eval.cs again afterwards: her own idle stands on the floor,
// where Manki's sank her.
const string IdleClip = "Assets/Art/Experiments/GenRA/bashie/bashie-idle-on-duty.anim";
const string RunClip = "Assets/Art/Experiments/GenRA/bashie/bashie-run-not-running.anim";
if (!Application.isPlaying) throw new InvalidOperationException("Run inside Play Mode, after preview_swap.eval.cs.");
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var rendererType = typeof(SlopArena.Client.Entities.PlayerRenderer);
var defField = rendererType.GetField("_charDef", flags);
var renderer = UnityEngine.Object.FindObjectsByType<SlopArena.Client.Entities.PlayerRenderer>(FindObjectsSortMode.None)
    .FirstOrDefault(r => (defField.GetValue(r) as SlopArena.Shared.CharacterDefinition)?.Class == SlopArena.Shared.CharacterClass.Manki)
    ?? throw new InvalidOperationException("No Manki fighter in this session.");
var def = (SlopArena.Shared.CharacterDefinition)defField.GetValue(renderer);
var original = (SlopArena.Client.Animation.CharacterAnimationCatalog)rendererType.GetField("_animationCatalog", flags).GetValue(renderer)
    ?? throw new InvalidOperationException("The fighter has no animation catalog to copy.");
var copy = UnityEngine.Object.Instantiate(original);
copy.name = original.name + " (Bashie preview)";
copy.hideFlags = HideFlags.DontSave;
var swaps = new Dictionary<string, string> { [def.IdleAnim] = IdleClip, [def.RunAnim] = RunClip };
var swapped = new Dictionary<string, object>();
foreach (var entry in copy.Animations)
{
    if (entry == null || !swaps.TryGetValue(entry.SemanticId, out var path)) continue;
    entry.Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
        ?? throw new InvalidOperationException("Missing candidate clip " + path + " (unity_clip.py write it first).");
    swapped[entry.SemanticId] = path;
}
if (swapped.Count != swaps.Count)
    throw new InvalidOperationException("The catalog lacks " + string.Join(", ", swaps.Keys.Where(k => !swapped.ContainsKey(k))));
renderer.SetAnimationCatalog(copy);
return new Dictionary<string, object> { ["swapped"] = swapped, ["catalog"] = original.name };
