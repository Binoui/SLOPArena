using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed record FighterCookField(string Name, string Type, bool Required, string Description);

/// <summary>The schema used by both the installed game and KitCheck.</summary>
public sealed class FighterCookDocument
{
    private FighterCookDocument() { }
    public static IReadOnlyList<FighterCookField> Fields { get; } = Array.AsReadOnly(new[] {
        new FighterCookField("model", "GLB filename", true, "One GLB directly in this folder."),
        new FighterCookField("animations", "map of semantic ID to nonempty clip name", true, "Exactly the animation IDs referenced by the compiled kit; names resolve against the GLB."),
        new FighterCookField("creator", "nonempty string", true, "Creator credited in the item manifest."),
        new FighterCookField("license", "nonempty string", true, "License for the item."),
        new FighterCookField("attribution", "string", true, "Required attribution; empty text is allowed."),
        new FighterCookField("aiAssisted", "boolean", true, "Whether AI assisted creation."),
        new FighterCookField("portraits", "array of image filename or null", false, "One entry per form; omitted art is rendered from the model."),
        new FighterCookField("icons", "array of icon maps", false, "One map per form; keys are icon.1, icon.2, icon.3, icon.a, icon.e, icon.r, icon.f; values are image filenames or null."),
        new FighterCookField("humanoid", "JSON filename", false, "Bone map filename; defaults to humanoid.json when that file exists."),
    });
    public string Model { get; private set; } = "";
    public string Creator { get; private set; } = "";
    public string License { get; private set; } = "";
    public string Attribution { get; private set; } = "";
    public bool AiAssisted { get; private set; }
    public string Humanoid { get; private set; } = "humanoid.json";
    public bool ExplicitHumanoid { get; private set; }
    public IReadOnlyDictionary<string, string> Animations { get; private set; } = new Dictionary<string, string>();
    public IReadOnlyList<string?>? Portraits { get; private set; }
    public IReadOnlyList<IReadOnlyDictionary<string, string?>>? Icons { get; private set; }
    public IEnumerable<string> Images => (Portraits ?? Array.Empty<string?>()).Concat((Icons ?? Array.Empty<IReadOnlyDictionary<string, string?>>()).SelectMany(x => x.Values)).Where(x => x != null).Select(x => x!).Distinct(StringComparer.Ordinal);
    internal static CharacterDiagnostic Error(string code, string path, string message) => MatchContentCatalogBuilder.Error(code, path, message);
    internal static bool FileName(string name) => name.Length > 0 && name != "." && name != ".." && name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0 && !Path.IsPathRooted(name);
    public static (FighterCookDocument? Document, IReadOnlyList<CharacterDiagnostic> Diagnostics) Read(byte[] bytes, IEnumerable<string>? required = null)
    {
        var d = new List<CharacterDiagnostic>(); var result = new FighterCookDocument();
        void Bad(string path, string message) => d.Add(Error("cook.value", "cook.json." + path, message));
        try
        {
            using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, new[] { Error("cook.type", "cook.json", "Expected a JSON object.") });
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in root.EnumerateObject())
            {
                if (!seen.Add(field.Name)) Bad(field.Name, "Duplicate field.");
                if (!Fields.Any(x => x.Name == field.Name)) Bad(field.Name, "Unknown field.");
            }
            foreach (var field in Fields.Where(x => x.Required && !seen.Contains(x.Name))) Bad(field.Name, "Required field is missing.");
            string Text(JsonElement v, string path, bool empty = false)
            {
                if (v.ValueKind != JsonValueKind.String || !empty && string.IsNullOrWhiteSpace(v.GetString())) { Bad(path, empty ? "Expected text; null is not text." : "Expected nonempty text."); return ""; }
                return v.GetString()!;
            }
            string Name(JsonElement v, string path, string[] suffixes)
            {
                string name = Text(v, path);
                if (!FileName(name) || !suffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal))) Bad(path, "Name a file directly in this folder with extension " + string.Join(" or ", suffixes) + ".");
                return name;
            }
            foreach (var p in root.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "model": result.Model = Name(p.Value, p.Name, new[] { ".glb" }); break;
                    case "creator": result.Creator = Text(p.Value, p.Name); break;
                    case "license": result.License = Text(p.Value, p.Name); break;
                    case "attribution": result.Attribution = Text(p.Value, p.Name, true); break;
                    case "humanoid": result.Humanoid = Name(p.Value, p.Name, new[] { ".json" }); result.ExplicitHumanoid = true; break;
                    case "aiAssisted":
                        if (p.Value.ValueKind != JsonValueKind.True && p.Value.ValueKind != JsonValueKind.False) Bad(p.Name, "Expected true or false."); else result.AiAssisted = p.Value.GetBoolean(); break;
                    case "animations":
                        if (p.Value.ValueKind != JsonValueKind.Object) { Bad(p.Name, "Expected an object mapping semantic animation IDs to GLB clip names."); break; }
                        var animations = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var a in p.Value.EnumerateObject())
                        {
                            string name = Text(a.Value, "animations." + a.Name);
                            if (animations.ContainsKey(a.Name)) Bad("animations." + a.Name, "Duplicate animation ID."); else animations.Add(a.Name, name);
                        }
                        result.Animations = animations; break;
                    case "portraits": case "icons":
                        if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() < 1 || p.Value.GetArrayLength() > 2) { Bad(p.Name, "Expected one entry per form, with one or two forms."); break; }
                        var portraits = new List<string?>(); var icons = new List<IReadOnlyDictionary<string, string?>>(); int form = 0;
                        foreach (var value in p.Value.EnumerateArray())
                        {
                            string path = p.Name + "[" + form++ + "]";
                            if (p.Name == "portraits") portraits.Add(value.ValueKind == JsonValueKind.Null ? null : Name(value, path, new[] { ".png", ".jpg" }));
                            else
                            {
                                var map = new Dictionary<string, string?>(StringComparer.Ordinal);
                                if (value.ValueKind != JsonValueKind.Object) Bad(path, "Expected a map of canonical icon IDs to image filenames or null.");
                                else foreach (var icon in value.EnumerateObject())
                                {
                                    if (!new[] { "icon.1", "icon.2", "icon.3", "icon.a", "icon.e", "icon.r", "icon.f" }.Contains(icon.Name)) Bad(path + "." + icon.Name, "Unknown icon ID.");
                                    if (map.ContainsKey(icon.Name)) Bad(path + "." + icon.Name, "Duplicate icon ID.");
                                    else map.Add(icon.Name, icon.Value.ValueKind == JsonValueKind.Null ? null : Name(icon.Value, path + "." + icon.Name, new[] { ".png", ".jpg" }));
                                }
                                icons.Add(map);
                            }
                        }
                        if (p.Name == "portraits") result.Portraits = portraits; else result.Icons = icons; break;
                }
            }
            if (required != null)
            {
                var expected = new HashSet<string>(required, StringComparer.Ordinal);
                foreach (string id in result.Animations.Keys.Where(id => !expected.Contains(id))) Bad("animations." + id, "The kit does not reference this semantic animation ID. Check its spelling.");
                foreach (string id in expected.Where(id => !result.Animations.ContainsKey(id))) Bad("animations." + id, "Bind this kit animation to a GLB clip name.");
            }
        }
        catch (JsonException e) { d.Add(Error("cook.json", "cook.json" + (e.Path == "$" ? "" : e.Path?.TrimStart('$')), e.Message)); }
        return (d.Count == 0 ? result : null, d);
    }
    public IReadOnlyList<CharacterDiagnostic> CheckKit(CookedCharacterDefinition definition)
    {
        var d = new List<CharacterDiagnostic>();
        var forms = definition.AlternateForm == null ? new[] { definition.Slots } : new[] { definition.Slots, definition.AlternateForm.Slots };
        if (Portraits != null && Portraits.Count != forms.Length) d.Add(Error("cook.forms", "cook.json.portraits", "Supply one portrait or null per form."));
        if (Icons != null)
        {
            if (Icons.Count != forms.Length) d.Add(Error("cook.forms", "cook.json.icons", "Supply one icon map per form."));
            for (int i = 0; i < Math.Min(Icons.Count, forms.Length); i++)
            {
                var used = new HashSet<string>(forms[i].Select(x => x.IconId), StringComparer.Ordinal);
                foreach (string id in Icons[i].Keys.Where(id => !used.Contains(id))) d.Add(Error("cook.icon", "cook.json.icons[" + i + "]." + id, "This form does not use this icon ID."));
            }
        }
        return d;
    }
    public static IReadOnlyList<CharacterDiagnostic> CheckFolder(string folder, CharacterCompileResult compiled, string? modelOverride = null)
    {
        var d = new List<CharacterDiagnostic>();
        string file = "cook.json";
        try
        {
            long total = 0;
            var required = compiled.CookedPackage == null ? null : CharacterPackageAssembler.RequiredAnimations(compiled.CookedPackage, d);
            var parsed = Read(PortableItemFiles.Read(Path.Combine(folder, file), 65536, ref total), required);
            d.AddRange(parsed.Diagnostics);
            if (parsed.Document is { } cook)
            {
                if (compiled.CookedPackage != null) d.AddRange(cook.CheckKit(compiled.CookedPackage.Definition));
                file = modelOverride ?? Path.Combine(folder, cook.Model);
                if (modelOverride != null || File.Exists(file))
                {
                    byte[] model = PortableItemFiles.Read(file, (int)WorkshopGlbLimits.Default.MaxFileBytes, ref total);
                    string mapPath = Path.Combine(folder, cook.Humanoid);
                    string? map = File.Exists(mapPath) ? Encoding.UTF8.GetString(PortableItemFiles.Read(mapPath, PortableItemFiles.HumanoidBytes, ref total)) : null;
                    d.AddRange(cook.CheckClips(model, map).Select(x => x.Path == cook.Model ? x with { Path = file } : x));
                }
            }
        }
        catch (Exception e) when (LocalFighterWorkspace.BoundaryFailure(e)) { d.Add(Error("workspace.read", file, e.Message)); }
        return d;
    }
    public IReadOnlyList<CharacterDiagnostic> CheckClips(byte[] glb, string? boneMap)
    {
        var d = new List<CharacterDiagnostic>();
        try
        {
            var rig = GlbMotionRig.Read(glb, boneMap);
            foreach (var a in Animations)
                try { rig.Clip(a.Value); }
                catch (GlbMotionException e) { d.Add(Error(e.Code, "cook.json.animations." + a.Key, e.Message)); }
        }
        catch (GlbMotionException e) { d.Add(Error(e.Code, Model, e.Message)); }
        return d;
    }
}
