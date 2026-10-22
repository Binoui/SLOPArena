// Eval body: copy Hose Down's GenRA clips from Assets/Art/Experiments/GenRA/bashie/ into her package's Animations/, as
// her catalog binds them (bind_catalog.eval.cs). Run after each GenRA write that the owner should see in game; until
// the motion pass lands, the copies are its key poses. Returns what it copied.
const string From = "Assets/Art/Experiments/GenRA/bashie/";
const string To = "Assets/CharacterPackages/bashie/Animations/";
var clips = new Dictionary<string, string>
{
    ["bashie-hose-pump-keys.anim"] = "bashie_hose_pump.anim",
    ["bashie-hose-spray-keys.anim"] = "bashie_hose_spray.anim",
};
var copied = new Dictionary<string, object>();
foreach (var pair in clips)
{
    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(From + pair.Key) == null) continue;
    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(To + pair.Value) != null) AssetDatabase.DeleteAsset(To + pair.Value);
    if (!AssetDatabase.CopyAsset(From + pair.Key, To + pair.Value)) throw new InvalidOperationException("Could not copy " + pair.Key);
    copied[pair.Key] = To + pair.Value;
}
return copied;
