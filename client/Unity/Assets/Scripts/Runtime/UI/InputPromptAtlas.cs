using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;

namespace SlopArena.Client.UI
{
    // Kenney Input Prompts 1.5: the Xbox PNG sheet and XML map are local, ignored assets.
    internal sealed class InputPromptAtlas
    {
        internal readonly struct Glyph
        {
            public readonly Texture2D Texture;
            public readonly Rect Uv;

            public Glyph(Texture2D texture, Rect uv)
            {
                Texture = texture;
                Uv = uv;
            }
        }

        private Dictionary<string, Glyph> _xbox;

        // Number-row binds are physical keys: AZERTY reports &/é/"/' as their
        // legends, but the HUD's canonical 1–4 prompts should keep their digits.
        internal static string KeyboardDisplayLabel(string path, string label)
        {
            const string prefix = "<Keyboard>/";
            if (path == null || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                path.Length != prefix.Length + 1) return label;
            return path[prefix.Length] switch
            {
                '0' => "0", '1' => "1", '2' => "2", '3' => "3", '4' => "4",
                '5' => "5", '6' => "6", '7' => "7", '8' => "8", '9' => "9",
                _ => label
            };
        }

        public bool TryXbox(string effectivePath, out Glyph glyph)
        {
            string name = XboxName(effectivePath);
            if (name != null && (_xbox ??= Load()).TryGetValue(name, out glyph)) return true;
            glyph = default;
            return false;
        }

        private static string XboxName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.EndsWith("/dpad/up", StringComparison.OrdinalIgnoreCase)) return "xbox_dpad_up";
            if (path.EndsWith("/dpad/down", StringComparison.OrdinalIgnoreCase)) return "xbox_dpad_down";
            if (path.EndsWith("/dpad/left", StringComparison.OrdinalIgnoreCase)) return "xbox_dpad_left";
            if (path.EndsWith("/dpad/right", StringComparison.OrdinalIgnoreCase)) return "xbox_dpad_right";
            if (path.EndsWith("/rightStick/up", StringComparison.OrdinalIgnoreCase)) return "xbox_stick_r_up";
            if (path.EndsWith("/rightStick/down", StringComparison.OrdinalIgnoreCase)) return "xbox_stick_r_down";
            if (path.EndsWith("/rightStick/left", StringComparison.OrdinalIgnoreCase)) return "xbox_stick_r_left";
            if (path.EndsWith("/rightStick/right", StringComparison.OrdinalIgnoreCase)) return "xbox_stick_r_right";
            return path.Substring(path.LastIndexOf('/') + 1) switch
            {
                "buttonSouth" => "xbox_button_color_a", "buttonEast" => "xbox_button_color_b",
                "buttonWest" => "xbox_button_color_x", "buttonNorth" => "xbox_button_color_y",
                "leftShoulder" => "xbox_lb", "rightShoulder" => "xbox_rb",
                "leftTrigger" => "xbox_lt", "rightTrigger" => "xbox_rt",
                "leftStickPress" => "xbox_ls", "rightStickPress" => "xbox_rs",
                "start" => "xbox_button_menu", "select" => "xbox_button_view",
                _ => null
            };
        }

        private static Dictionary<string, Glyph> Load()
        {
            var result = new Dictionary<string, Glyph>(StringComparer.Ordinal);
            var texture = Resources.Load<Texture2D>("InputPrompts/xbox");
            var xml = Resources.Load<TextAsset>("InputPrompts/xbox");
            if (texture == null || xml == null) return result;

            var document = new XmlDocument();
            document.LoadXml(xml.text);
            var cells = document.DocumentElement.ChildNodes;
            float sourceWidth = 0f;
            float sourceHeight = 0f;
            foreach (XmlNode node in cells)
            {
                var attributes = node.Attributes;
                float x = float.Parse(attributes["x"].Value, CultureInfo.InvariantCulture);
                float y = float.Parse(attributes["y"].Value, CultureInfo.InvariantCulture);
                float width = float.Parse(attributes["width"].Value, CultureInfo.InvariantCulture);
                float height = float.Parse(attributes["height"].Value, CultureInfo.InvariantCulture);
                sourceWidth = Mathf.Max(sourceWidth, x + width);
                sourceHeight = Mathf.Max(sourceHeight, y + height);
            }
            // The supplied sheets are vertically inverted relative to their XML rows;
            // Unity UVs start at the bottom, so XML y is already the UV row.
            foreach (XmlNode node in cells)
            {
                var attributes = node.Attributes;
                string name = attributes["name"].Value;
                float x = float.Parse(attributes["x"].Value, CultureInfo.InvariantCulture);
                float y = float.Parse(attributes["y"].Value, CultureInfo.InvariantCulture);
                float width = float.Parse(attributes["width"].Value, CultureInfo.InvariantCulture);
                float height = float.Parse(attributes["height"].Value, CultureInfo.InvariantCulture);
                result.Add(name, new Glyph(texture, new Rect(x / sourceWidth,
                    y / sourceHeight, width / sourceWidth, height / sourceHeight)));
            }
            return result;
        }
    }
}
