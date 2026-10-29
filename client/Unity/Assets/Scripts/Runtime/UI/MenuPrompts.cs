#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    public enum MenuAction { Back, Forward, PrevSection, NextSection, Menu, Chat }

    public static class MenuPrompts
    {
        private static readonly (string Path, string Fallback, bool Monochrome)[] Actions =
        {
            ("<Gamepad>/buttonEast", "B", false),
            ("<Gamepad>/start", "≡", true),
            ("<Gamepad>/leftShoulder", "LB", true),
            ("<Gamepad>/rightShoulder", "RB", true),
            ("<Gamepad>/select", "VIEW", true),
            ("<Gamepad>/buttonNorth", "Y", false)
        };
        private static readonly MenuGlyphSheet Atlas = new();
        private static StyleSheet? _style;

        public static string Path(MenuAction action) => Actions[(int)action].Path;
        public static string Fallback(MenuAction action) => Actions[(int)action].Fallback;

        public static void Stick(VisualElement host, MenuAction action)
        {
            Remove(host);
            host.AddToClassList("menu-prompt-host");
            host.Add(Create(action, "menu-prompt-sticker"));
        }

        public static void Remove(VisualElement host) => RemoveChild(host, "menu-prompt-sticker");

        public static void Flank(VisualElement tabRow, MenuAction prev, MenuAction next)
        {
            RemoveChild(tabRow, "menu-prompt-prev");
            RemoveChild(tabRow, "menu-prompt-next");
            tabRow.AddToClassList("menu-prompt-tab-row");
            var left = Create(prev, "menu-prompt-prev");
            left.AddToClassList("menu-prompt-sticker--prev");
            var right = Create(next, "menu-prompt-next");
            right.AddToClassList("menu-prompt-sticker--next");
            tabRow.Add(left);
            tabRow.Add(right);
        }

        public static void ApplyInputMode(VisualElement root)
        {
            _style ??= Resources.Load<StyleSheet>("UI/MenuPrompts");
            if (_style != null && !root.styleSheets.Contains(_style))
                root.styleSheets.Add(_style);
            root.EnableInClassList("pad-prompts", MenuInputMode.PadActive);
        }

        private static void RemoveChild(VisualElement host, string name)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
                if (host[i].name == name) host[i].RemoveFromHierarchy();
        }

        private static VisualElement Create(MenuAction action, string name)
        {
            var sticker = new VisualElement { name = name, pickingMode = PickingMode.Ignore, focusable = false };
            sticker.AddToClassList("menu-prompt-sticker");
            var entry = Actions[(int)action];
            if (Atlas.TryXbox(entry.Path, out var glyph))
            {
                var icon = new VisualElement { pickingMode = PickingMode.Ignore, focusable = false };
                icon.AddToClassList("menu-prompt-glyph");
                icon.EnableInClassList("menu-prompt-glyph--mono", entry.Monochrome);
                icon.style.backgroundImage = new StyleBackground(glyph);
                sticker.Add(icon);
            }
            else
            {
                var label = new Label(entry.Fallback) { pickingMode = PickingMode.Ignore, focusable = false };
                label.AddToClassList("menu-prompt-fallback");
                label.EnableInClassList("menu-prompt-fallback--word", entry.Fallback.Length > 2);
                sticker.Add(label);
            }
            return sticker;
        }
    }

    public static class MenuInputMode
    {
        private static int _frame = -1;
        private static bool _padActive;

        public static bool PadActive
        {
            get
            {
                if (_frame == Time.frameCount) return _padActive;
                _frame = Time.frameCount;
                var mouse = Mouse.current;
                if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f
                    || mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                    || mouse.middleButton.wasPressedThisFrame)
                    || Keyboard.current?.anyKey.wasPressedThisFrame == true)
                    _padActive = false;
                foreach (var pad in Gamepad.all)
                    if (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
                        || pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame
                        || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame
                        || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame
                        || pad.dpad.ReadValue().sqrMagnitude > 0.25f
                        || pad.leftStick.ReadValue().sqrMagnitude > 0.25f
                        || pad.rightStick.ReadValue().sqrMagnitude > 0.25f)
                        _padActive = true;
                return _padActive;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _frame = -1;
            _padActive = false;
        }
    }

    internal sealed class MenuGlyphSheet
    {
        private Dictionary<string, Sprite>? _xbox;

        public bool TryXbox(string path, out Sprite glyph)
        {
            string? name = path.Substring(path.LastIndexOf('/') + 1) switch
            {
                "buttonEast" => "xbox_button_color_b",
                "start" => "xbox_button_menu",
                "leftShoulder" => "xbox_lb",
                "rightShoulder" => "xbox_rb",
                "select" => "xbox_button_view",
                "buttonNorth" => "xbox_button_color_y",
                _ => null
            };
            if (name != null && (_xbox ??= Load()).TryGetValue(name, out glyph)) return true;
            glyph = null!;
            return false;
        }

        private static Dictionary<string, Sprite> Load()
        {
            var result = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var texture = Resources.Load<Texture2D>("InputPrompts/xbox");
            var xml = Resources.Load<TextAsset>("InputPrompts/xbox");
            if (texture == null || xml == null) return result;
            var document = new XmlDocument();
            document.LoadXml(xml.text);
            var cells = document.DocumentElement!.ChildNodes;
            float sourceWidth = 0f, sourceHeight = 0f;
            foreach (XmlNode node in cells)
            {
                var rect = Cell(node);
                sourceWidth = Mathf.Max(sourceWidth, rect.xMax);
                sourceHeight = Mathf.Max(sourceHeight, rect.yMax);
            }
            // The local sheet is inverted relative to XML rows, so XML y is already the bottom-up UV row.
            foreach (XmlNode node in cells)
            {
                var rect = Cell(node);
                rect = new Rect(rect.x * texture.width / sourceWidth, rect.y * texture.height / sourceHeight,
                    rect.width * texture.width / sourceWidth, rect.height * texture.height / sourceHeight);
                var sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f));
                sprite.hideFlags = HideFlags.HideAndDontSave;
                result.Add(node.Attributes!["name"]!.Value, sprite);
            }
            return result;
        }

        private static Rect Cell(XmlNode node)
        {
            var a = node.Attributes!;
            return new Rect(float.Parse(a["x"]!.Value, CultureInfo.InvariantCulture),
                float.Parse(a["y"]!.Value, CultureInfo.InvariantCulture),
                float.Parse(a["width"]!.Value, CultureInfo.InvariantCulture),
                float.Parse(a["height"]!.Value, CultureInfo.InvariantCulture));
        }
    }
}
