// Eval body: bind Bashie's package catalog after borrow_clips.eval.cs. The rig is her package's BashieRig.prefab; every
// animation ID her character.json uses gets one binding with its own pose track. Preserve accepted package clips,
// use the named special clips below, and borrow Manki clips only for unbound IDs. Her whistle gets a separate idle
// copy until its GenRA clip replaces it. Keep her weapon config and leave presentation bindings to
// BashiePresentationBuilder (sloparena.presentation.bashie.build); she borrows none of Manki's props or effects.
// Saving the catalog makes Unity import it, and the cook postprocessor then cooks her package (only hers: no other
// package's source changes). Run sloparena.character.cook afterwards for the reported, explicit cook.
const string Package = "Assets/CharacterPackages/bashie";
var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(Package + "/CharacterAssetCatalog.asset");
var manki = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>("Assets/CharacterPackages/manki/CharacterAssetCatalog.asset");
var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "CharacterPackages/bashie/character.json")));
var ids = json.SelectTokens("$.presentation.*").Concat(json.SelectTokens("$.slots[*].aimAnimationId")).Concat(json.SelectTokens("$.slots[*].timeline.stages[*].animationIds[*]"))
    .Select(t => (string)t).Where(id => !string.IsNullOrEmpty(id) && id.StartsWith("anim.bashie.")).Distinct().OrderBy(id => id).ToList();
if (ids.Contains("anim.bashie.whistle") && AssetDatabase.LoadAssetAtPath<AnimationClip>(Package + "/Animations/bashie_whistle.anim") == null)
{
    if (!AssetDatabase.CopyAsset(Package + "/Animations/bashie_idle.anim", Package + "/Animations/bashie_whistle.anim"))
        throw new InvalidOperationException("Could not create the whistle stand-in.");
}
var own = new Dictionary<string, string>
{
    ["anim.bashie.idle"] = Package + "/Animations/bashie_idle.anim", ["anim.bashie.run"] = Package + "/Animations/bashie_run.anim",
    // Hose Down (A), from hose_clips.eval.cs. The stage's own clip never shows (the spray starts at the release), and
    // her idle stands in for the end clip until hers exists.
    ["anim.bashie.hose-pump"] = Package + "/Animations/bashie_hose_pump.anim",
    ["anim.bashie.hose"] = Package + "/Animations/bashie_hose_spray.anim",
    ["anim.bashie.hose-spray"] = Package + "/Animations/bashie_hose_spray.anim",
    ["anim.bashie.hose-end"] = Package + "/Animations/bashie_idle.anim",
    // Shell Slide (R), her own GenRA clips (briefs bashie-sr-*), written by unity_clip.py into Animations/.
    ["anim.bashie.gr"] = Package + "/Animations/bashie_sr_dive.anim",
    ["anim.bashie.gr-slide"] = Package + "/Animations/bashie_sr_slide.anim",
    ["anim.bashie.gr-pop"] = Package + "/Animations/bashie_sr_pop.anim",
    ["anim.bashie.gr-flip"] = Package + "/Animations/bashie_sr_flip.anim",
    ["anim.bashie.gr-lookdown"] = Package + "/Animations/bashie_sr_lookdown.anim",
    ["anim.bashie.whistle"] = Package + "/Animations/bashie_whistle.anim",
};
// Her own moves' IDs with no clip yet would borrow a Manki clip until theirs exists.
var standIns = new Dictionary<string, string>();
var mankiBindings = manki.Bindings.Where(b => b != null).ToDictionary(b => b.SemanticId);
var existing = catalog.Bindings.Where(b => b?.Clip != null).ToDictionary(b => b.SemanticId);
var copies = new Dictionary<AnimationClip, string>();  // Manki's clip -> the borrowed copy made from it (first ID wins)
foreach (var b in manki.Bindings)
{
    if (b?.Clip == null || copies.ContainsKey(b.Clip)) continue;
    string path = Package + "/Animations/Borrowed/" + b.SemanticId.Replace("anim.manki.", "") + ".anim";
    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) copies[b.Clip] = path;
}
var bindings = new List<SlopArena.Client.Animation.CharacterAssetCatalog.AnimationBinding>();
var report = new Dictionary<string, object>();
foreach (var id in ids)
{
    string mankiId = standIns.TryGetValue(id, out var standIn) ? standIn : id.Replace("anim.bashie.", "anim.manki.");
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
catalog.PackageId = "bashie";
catalog.Rig = AssetDatabase.LoadAssetAtPath<GameObject>(Package + "/Presentation/BashieRig.prefab") ?? throw new InvalidOperationException("Missing rig prefab");
catalog.WeaponConfig = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Entities.WeaponAttachConfig>(Package + "/Presentation/BashieWeaponAttachments.asset");
catalog.SampleRate = 60;
catalog.Bindings = bindings.ToArray();
EditorUtility.SetDirty(catalog);
AssetDatabase.SaveAssetIfDirty(catalog);
AssetDatabase.ImportAsset(Package + "/character.json", ImportAssetOptions.ForceSynchronousImport);
return new Dictionary<string, object> { ["bindings"] = bindings.Count, ["rig"] = AssetDatabase.GetAssetPath(catalog.Rig), ["clips"] = report };
