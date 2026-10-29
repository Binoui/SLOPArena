const string Folder = "Assets/CharacterPackages/nyx/Presentation/";
var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>("Assets/CharacterPackages/nyx/CharacterAssetCatalog.asset");
var bindings = (catalog.Presentations ?? Array.Empty<SlopArena.Client.Animation.CharacterAssetCatalog.PresentationBinding>()).ToList();
var definitions = new[] {
    ("presentation.nyx.props.pole-held", "NyxPoleSwing", (byte)1, new[] { "Nyx_PoleHeld" }, new[] { "Nyx_BackpackPole" }),
    ("presentation.nyx.props.phone-raise", "NyxPhoneRaise", (byte)0, new[] { "Nyx_Phone" }, Array.Empty<string>()),
    ("presentation.nyx.props.phone-drop", "NyxPhoneDrop", (byte)1, Array.Empty<string>(), new[] { "Nyx_Phone" })
};
foreach (var definition in definitions)
{
    string path = Folder + definition.Item2 + ".prefab";
    var root = new GameObject(definition.Item2);
    try
    {
        var toggle = root.AddComponent<SlopArena.Client.World.PropVisibilityPresentation>();
        toggle.Configure(definition.Item3, definition.Item4, definition.Item5);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        var binding = bindings.SingleOrDefault(b => b.SemanticId == definition.Item1);
        if (binding == null) { binding = new SlopArena.Client.Animation.CharacterAssetCatalog.PresentationBinding { SemanticId = definition.Item1 }; bindings.Add(binding); }
        binding.Prefab = prefab;
    }
    finally { UnityEngine.Object.DestroyImmediate(root); }
}
catalog.Presentations = bindings.ToArray();
EditorUtility.SetDirty(catalog);
AssetDatabase.SaveAssetIfDirty(catalog);
return new { installed = definitions.Select(d => d.Item1).ToArray(), catalog = AssetDatabase.GetAssetPath(catalog) };
