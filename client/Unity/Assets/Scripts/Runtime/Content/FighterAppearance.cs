#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Runtime.CompilerServices;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client
{
    public readonly struct UiImage
    {
        public readonly Texture2D? Texture;
        public readonly Sprite? Sprite;
        public readonly Color Tint;
        public UiImage(Texture2D? texture, Sprite? sprite, Color tint)
        { Texture = texture; Sprite = sprite; Tint = tint; }
    }

    public sealed class FighterAppearance
    {
        public MatchContentEntry Content { get; }
        public MatchContentIdentity Revision => Content.Identity;
        public FighterUiArt Art { get; }
        internal Entities.WeaponAttachConfig? LegacyWeapon { get; }
        internal FighterAppearance(MatchContentEntry content)
        {
            Content = content;
            var binding = content.IsBundled ? FighterPresentation.Binding(content.Identity.PackageId) : null;
            Art = !content.IsBundled && Workshop.MadeFighterPresentation.TryGet(content.Identity, out var made)
                ? made.Art : new FighterUiArt(content.Definition, binding);
            string? weapon = binding?.GetProperty("legacyWeapon").GetString();
            LegacyWeapon = string.IsNullOrEmpty(weapon) ? null : Resources.Load<Entities.WeaponAttachConfig>(weapon);
        }
    }

    public sealed class FighterUiArt
    {
        readonly string[] names;
        readonly Texture2D?[] portraits;
        readonly Dictionary<string, UiImage>[] icons;
        readonly bool[] neutralForms;
        // A made fighter's missing art falls back to neutral art, never to another fighter's.
        readonly bool madeFighter;
        public FighterCardText Card { get; }
        public string StyleAlias { get; }
        public bool HasForms => names.Length > 1;
        public bool HasSplash { get; }

        internal FighterUiArt(CharacterDefinition definition, JsonElement? binding)
            : this(new[] { definition.DisplayName, definition.ForForm(1).DisplayName }, definition.AlternateForm != null, binding) { }

        internal FighterUiArt(string[] names, bool forms, JsonElement? binding)
        {
            this.names = forms ? names : new[] { names[0] };
            neutralForms = new bool[this.names.Length];
            HasSplash = binding?.GetProperty("hasSplash").GetBoolean() ?? false;
            StyleAlias = binding?.GetProperty("alias").GetString() ?? "";
            Card = binding.HasValue ? FighterCardText.Read(binding.Value.GetProperty("card")) : FighterCardText.Empty;
            portraits = new Texture2D?[this.names.Length];
            icons = new Dictionary<string, UiImage>[this.names.Length];
            for (int form = 0; form < this.names.Length; form++)
            {
                bool available = binding.HasValue && form < binding.Value.GetProperty("portraits").GetArrayLength()
                    && form < binding.Value.GetProperty("icons").GetArrayLength();
                neutralForms[form] = !available;
                if (binding.HasValue && !available)
                    Debug.LogWarning($"[FighterPresentation] Missing UI form {form} for {names[0]}. Using neutral art. Run python3 tools/fighter-presentation/generate.py.");
                portraits[form] = available ? LoadTexture(binding!.Value.GetProperty("portraits")[form].GetString()) : NeutralPortrait;
                icons[form] = new Dictionary<string, UiImage>(StringComparer.Ordinal);
                if (available)
                    foreach (var icon in binding.Value.GetProperty("icons")[form].EnumerateObject())
                    {
                        var row = icon.Value;
                        var sprites = row.TryGetProperty("card", out var card) ? Resources.LoadAll<Sprite>(card.GetString()) : Array.Empty<Sprite>();
                        icons[form][icon.Name] = sprites.Length > 0 ? new UiImage(null, sprites[0], Color.white)
                            : new UiImage(row.TryGetProperty("icon", out var path) ? LoadTexture(path.GetString()) : null, null, Color.clear);
                    }
            }
        }

        /// <summary>A made fighter's art: its item images or rendered portrait, its card, neutral art for anything missing.</summary>
        internal FighterUiArt(CharacterDefinition definition, Texture2D?[] formPortraits, Dictionary<string, UiImage>[] formIcons, FighterCardText card)
        {
            names = definition.AlternateForm != null ? new[] { definition.DisplayName, definition.ForForm(1).DisplayName } : new[] { definition.DisplayName };
            neutralForms = new bool[names.Length];
            portraits = new Texture2D?[names.Length];
            icons = new Dictionary<string, UiImage>[names.Length];
            for (int form = 0; form < names.Length; form++)
            {
                portraits[form] = form < formPortraits.Length ? formPortraits[form] : null;
                icons[form] = form < formIcons.Length ? formIcons[form] : new Dictionary<string, UiImage>(StringComparer.Ordinal);
            }
            Card = card;
            StyleAlias = "";
            madeFighter = true;
        }

        int Form(byte form) => form < names.Length ? form : 0;
        public string Name(byte form = 0) => names[Form(form)];
        public Texture2D? Portrait(byte form = 0) => neutralForms[Form(form)] ? NeutralPortrait
            : madeFighter ? portraits[Form(form)] ?? NeutralPortrait : portraits[Form(form)];
        public UiImage MoveIcon(byte form, string? iconId, byte slot, bool normal)
        {
            if (neutralForms[Form(form)]) return new UiImage(NeutralGlyph(slot), null, Color.white);
            if (string.IsNullOrEmpty(iconId)) return default;
            if (madeFighter && (!icons[Form(form)].TryGetValue(iconId, out var own) || own.Texture == null))
                return new UiImage(NeutralGlyph(slot), null, Color.white);
            Color tint = normal ? new Color32(27, 26, 24, 255) : Color.white;
            return icons[Form(form)].TryGetValue(iconId, out var image) && image.Sprite != null
                ? image : new UiImage(image.Texture, null, tint);
        }
        static Texture2D? LoadTexture(string? path) => string.IsNullOrEmpty(path) ? null : Resources.Load<Texture2D>(path);
        static Texture2D? neutralPortrait;
        static readonly Dictionary<byte, Texture2D> glyphs = new();
        public static Texture2D NeutralPortrait => neutralPortrait != null ? neutralPortrait : neutralPortrait = Draw(true, 0);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterTextureCleanup()
        {
            ReleaseNeutralTextures();
            Application.quitting -= ReleaseNeutralTextures;
            Application.quitting += ReleaseNeutralTextures;
        }
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterEditorCleanup()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseNeutralTextures;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseNeutralTextures;
        }
#endif
        internal static void ReleaseNeutralTextures()
        {
            if (neutralPortrait != null) UnityEngine.Object.DestroyImmediate(neutralPortrait);
            neutralPortrait = null;
            foreach (var texture in glyphs.Values)
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            glyphs.Clear();
        }
        static Texture2D NeutralGlyph(byte slot)
        {
            if (!glyphs.TryGetValue(slot, out var image) || image == null) glyphs[slot] = image = Draw(false, slot);
            return image;
        }
        static Texture2D Draw(bool portrait, byte slot)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = portrait ? "Neutral fighter" : "Neutral slot " + slot, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int dx = x - 32, dy = y - 43;
                    bool ink = portrait ? dx * dx + dy * dy < 100 || y < 30 && dx * dx + (y - 12) * (y - 12) < 400
                        : Math.Abs(dx) + Math.Abs(y - 32) is > 20 and < 25 || y >= 27 && y <= 36 && x >= 15 && x < 15 + Math.Max(1, (int)slot) * 4;
                    pixels[y * size + x] = ink ? new Color32(180, 180, 180, 255) : new Color32(0, 0, 0, 0);
                }
            texture.SetPixels32(pixels); texture.Apply(); return texture;
        }
    }

    public sealed class FighterCardText
    {
        public string Description { get; }
        public bool IsWorkInProgress { get; }
        public (string Starter, string FollowUp, bool Jump)? TutorialHint { get; }
        public static readonly FighterCardText Empty = new("Admitted fighter with a style all its own.", false, null);
        FighterCardText(string description, bool wip, (string, string, bool)? hint)
        { Description = description; IsWorkInProgress = wip; TutorialHint = hint; }
        internal static FighterCardText Read(JsonElement card)
        {
            var hint = card.GetProperty("tutorialHint");
            return new FighterCardText(card.GetProperty("description").GetString()!, card.GetProperty("workInProgress").GetBoolean(),
                hint.ValueKind == JsonValueKind.Null ? null : (hint.GetProperty("starter").GetString()!, hint.GetProperty("followUp").GetString()!, hint.GetProperty("jump").GetBoolean()));
        }
    }

    public static class FighterPresentation
    {
        static Dictionary<string, JsonElement>? bindings;
        static readonly HashSet<string> warned = new(StringComparer.Ordinal);
        static void Missing(string packageId)
        {
            if (warned.Add(packageId)) Debug.LogWarning($"[FighterPresentation] Missing or invalid UI binding/card for '{packageId}'. Using neutral art. Run python3 tools/fighter-presentation/generate.py.");
        }
        static Dictionary<string, JsonElement> ReadBindings()
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            try
            {
                var asset = Resources.Load<TextAsset>("Generated/FighterUi");
                if (asset == null) { Missing("Generated/FighterUi"); return result; }
                using var document = JsonDocument.Parse(asset.text);
                foreach (var row in document.RootElement.GetProperty("fighters").EnumerateArray())
                {
                    string package = row.GetProperty("packageId").GetString()!;
                    try
                    {
                        if (row.GetProperty("names").GetArrayLength() == 0) throw new InvalidOperationException("No form names.");
                        row.GetProperty("portraits").GetArrayLength(); row.GetProperty("icons").GetArrayLength();
                        row.GetProperty("alias").GetString(); row.GetProperty("legacyWeapon").GetString();
                        row.GetProperty("hasSplash").GetBoolean(); FighterCardText.Read(row.GetProperty("card"));
                        result[package] = row.Clone();
                    }
                    catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
                    { Missing(package); }
                }
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
            { Missing("Generated/FighterUi"); }
            return result;
        }
        internal static JsonElement? Binding(string packageId)
        {
            bindings ??= ReadBindings();
            if (bindings.TryGetValue(packageId, out var row)) return row;
            Missing(packageId);
            return null;
        }
        static readonly ConditionalWeakTable<MatchContentEntry, FighterAppearance> resolved = new();
        public static FighterAppearance Resolve(MatchContentEntry entry) => resolved.GetValue(entry ?? throw new ArgumentNullException(nameof(entry)), content => new FighterAppearance(content));
        public static FighterUiArt Unavailable(string name) => new(new[] { name }, false, null);
        public static FighterUiArt Menu(string packageId, string fallbackName)
        {
            return UI.LocalFighterLibrary.Current.TryGetCatalog(out var catalog, out _) && catalog?.ResolvePackage(packageId) is { } entry
                ? Resolve(entry).Art : Unavailable(fallbackName);
        }
    }
}
