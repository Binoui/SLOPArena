// Eval body: bind the salamander's package catalog after borrow_clips.eval.cs. The rig is his package's
// SalamanderRig.prefab; every animation ID his character.json uses gets one binding with its own pose track: his own clip
// from clips.json (beside this script) when that file exists in the package, else a package clip already bound, else the
// borrowed copy of Manki's clip for the same ID. He borrows
// none of Manki's props or effects (no weapon config, no presentation bindings).
// Saving the catalog makes Unity import it, and the cook postprocessor then cooks his package. Run
// sloparena.character.cook afterwards for the reported, explicit cook.
// Run: bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/blender/salamander_rig/bind_catalog.eval.cs"
const string Package = "Assets/CharacterPackages/salamander";
var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(Package + "/CharacterAssetCatalog.asset");
var manki = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>("Assets/CharacterPackages/manki/CharacterAssetCatalog.asset");
var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "CharacterPackages/salamander/character.json")));
var ids = json.SelectTokens("$.presentation.*").Concat(json.SelectTokens("$.slots[*].aimAnimationId")).Concat(json.SelectTokens("$.slots[*].timeline.stages[*].animationIds[*]"))
    .Concat(json.SelectTokens("$..animationId")).Concat(json.SelectTokens("$..endAnimationId"))
    .Where(t => t.Type == Newtonsoft.Json.Linq.JTokenType.String)
    .Select(t => (string)t).Where(id => !string.IsNullOrEmpty(id) && id.StartsWith("anim.salamander.")).Distinct().OrderBy(id => id).ToList();
var mankiBindings = manki.Bindings.Where(b => b != null).ToDictionary(b => b.SemanticId);
var existing = (catalog.Bindings ?? new SlopArena.Client.Animation.CharacterAssetCatalog.AnimationBinding[0])
    .Where(b => b?.Clip != null && !AssetDatabase.GetAssetPath(b.Clip).Contains("/Animations/Borrowed/")).ToDictionary(b => b.SemanticId);
var copies = new Dictionary<AnimationClip, string>();  // Manki's clip -> the borrowed copy made from it (first ID wins)
foreach (var b in manki.Bindings)
{
    if (b?.Clip == null || copies.ContainsKey(b.Clip)) continue;
    string path = Package + "/Animations/Borrowed/" + b.SemanticId.Replace("anim.manki.", "") + ".anim";
    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) copies[b.Clip] = path;
}
var ownJson = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("/home/binoui/Documents/projects/SlopArena/tools/blender/salamander_rig/clips.json"));
var own = ownJson.Properties().Where(p => AssetDatabase.LoadAssetAtPath<AnimationClip>((string)p.Value) != null)
    .ToDictionary(p => p.Name, p => (string)p.Value);
var bindings = new List<SlopArena.Client.Animation.CharacterAssetCatalog.AnimationBinding>();
var report = new Dictionary<string, object>();
foreach (var id in ids)
{
    string mankiId = id.Replace("anim.salamander.", "anim.manki.");
    if (!own.ContainsKey(id) && !existing.ContainsKey(id) && !mankiBindings.ContainsKey(mankiId))
        mankiId = "anim.manki.idle";   // a new ID with no clip yet stands in on his idle until its clip exists
    string path = own.TryGetValue(id, out var mine) ? mine
        : existing.TryGetValue(id, out var current) ? AssetDatabase.GetAssetPath(current.Clip)
        : mankiBindings.TryGetValue(mankiId, out var mb) && mb.Clip != null && copies.TryGetValue(mb.Clip, out var copy) ? copy
        : throw new InvalidOperationException("No clip for " + id);
    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path) ?? throw new InvalidOperationException("Missing " + path);
    bindings.Add(new SlopArena.Client.Animation.CharacterAssetCatalog.AnimationBinding
    {
        SemanticId = id, PoseTrackId = id, Clip = clip,
        Extrapolation = existing.TryGetValue(id, out var previous) ? previous.Extrapolation
            : mankiBindings.TryGetValue(mankiId, out var src) ? src.Extrapolation : default,
    });
    report[id] = path;
}
catalog.PackageId = "salamander";
catalog.Rig = AssetDatabase.LoadAssetAtPath<GameObject>(Package + "/Presentation/SalamanderRig.prefab") ?? throw new InvalidOperationException("Missing rig prefab");
catalog.SampleRate = 60;
catalog.Bindings = bindings.ToArray();
EditorUtility.SetDirty(catalog);
AssetDatabase.SaveAssetIfDirty(catalog);
AssetDatabase.ImportAsset(Package + "/character.json", ImportAssetOptions.ForceSynchronousImport);
return new Dictionary<string, object> { ["bindings"] = bindings.Count, ["rig"] = AssetDatabase.GetAssetPath(catalog.Rig), ["clips"] = report };
