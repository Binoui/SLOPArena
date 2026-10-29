const string Package = "Assets/CharacterPackages/nyx";
var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(Package + "/CharacterAssetCatalog.asset");
var donor = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>("Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset");
string OwnId(string id) => "anim.nyx." + id.Replace("anim.fightguy.", "").Replace("anim.", "");
var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "CharacterPackages/nyx/character.json")));
var ids = json.Descendants().OfType<Newtonsoft.Json.Linq.JValue>().Where(v => v.Type == Newtonsoft.Json.Linq.JTokenType.String)
    .Select(v => (string)v).Where(id => id.StartsWith("anim.nyx.")).Distinct().OrderBy(id => id).ToList();
var originals = donor.Bindings.Where(b => b?.Clip != null).ToDictionary(b => OwnId(b.SemanticId));
var copies = new Dictionary<AnimationClip, string>();
foreach (var binding in donor.Bindings)
{
    if (binding?.Clip == null || copies.ContainsKey(binding.Clip)) continue;
    string path = Package + "/Animations/Borrowed/" + binding.SemanticId.Replace("anim.fightguy.", "").Replace("anim.", "") + ".anim";
    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) copies[binding.Clip] = path;
}
var report = new Dictionary<string, object>();
var bindings = new List<SlopArena.Client.Animation.CharacterAssetCatalog.AnimationBinding>();
foreach (var id in ids)
{
    if (!originals.TryGetValue(id, out var source) || !copies.TryGetValue(source.Clip, out var path))
        throw new InvalidOperationException("No approved borrowed clip for " + id);
    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    bindings.Add(new SlopArena.Client.Animation.CharacterAssetCatalog.AnimationBinding
    { SemanticId = id, PoseTrackId = id, Clip = clip, Extrapolation = source.Extrapolation });
    report[id] = path;
}
catalog.PackageId = "nyx";
catalog.Rig = AssetDatabase.LoadAssetAtPath<GameObject>(Package + "/Presentation/NyxRig.prefab");
if (catalog.Rig == null) throw new InvalidOperationException("Nyx rig missing");
catalog.SampleRate = 60;
catalog.Bindings = bindings.ToArray();
EditorUtility.SetDirty(catalog);
AssetDatabase.SaveAssetIfDirty(catalog);
AssetDatabase.ImportAsset(Package + "/character.json", ImportAssetOptions.ForceSynchronousImport);
return new { bindings = bindings.Count, rig = AssetDatabase.GetAssetPath(catalog.Rig), clips = report };
