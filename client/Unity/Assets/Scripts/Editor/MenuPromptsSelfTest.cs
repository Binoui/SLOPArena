#nullable enable
using System;
using System.Linq;
using System.Reflection;
using SlopArena.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools
{
    public static class MenuPromptsSelfTest
    {
        [MenuItem("Tools/SLOPArena/Tests/Controller Menu Prompts")]
        public static void RunAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run menu prompt regressions in Edit Mode.");
            ActionsResolve();
            StickersReplace();
            SectionsWrap();
            Debug.Log("[MenuPromptsSelfTest] All six actions resolve with and without the atlas; stickers and flanks are idempotent; sections wrap in both directions.");
        }

        private static void ActionsResolve()
        {
            var atlas = typeof(MenuPrompts).GetField("Atlas", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var cache = atlas.GetType().GetField("_xbox", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (MenuAction action in Enum.GetValues(typeof(MenuAction)))
            {
                Require(MenuPrompts.Path(action).StartsWith("<Gamepad>/", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(MenuPrompts.Fallback(action)), "Every action needs a pad path and fallback.");
                var host = new Button();
                MenuPrompts.Stick(host, action);
                Require(host.Q<VisualElement>(className: "menu-prompt-glyph") != null
                    || host.Q<Label>(className: "menu-prompt-fallback")?.text == MenuPrompts.Fallback(action),
                    action + " has neither glyph nor readable fallback.");
            }
            var saved = cache.GetValue(atlas);
            try
            {
                cache.SetValue(atlas, Activator.CreateInstance(cache.FieldType));
                foreach (MenuAction action in Enum.GetValues(typeof(MenuAction)))
                {
                    var host = new Button();
                    MenuPrompts.Stick(host, action);
                    Require(host.Q<Label>(className: "menu-prompt-fallback")?.text == MenuPrompts.Fallback(action),
                        action + " lost its label without the ignored sheet.");
                }
            }
            finally { cache.SetValue(atlas, saved); }
        }

        private static void StickersReplace()
        {
            var host = new Button { text = "CONTINUE" };
            var content = new Label("Existing child");
            host.Add(content);
            var nested = new VisualElement();
            host.Add(nested);
            MenuPrompts.Stick(nested, MenuAction.Chat);
            MenuPrompts.Stick(host, MenuAction.Back);
            MenuPrompts.Stick(host, MenuAction.Forward);
            MenuPrompts.Stick(host, MenuAction.Forward);
            Require(host.Children().Count(child => child.name == "menu-prompt-sticker") == 1
                && nested.Q("menu-prompt-sticker") != null
                && host.Contains(content) && host.text == "CONTINUE", "Re-sticking duplicates prompts or changes host content.");
            foreach (var element in host.Q<VisualElement>("menu-prompt-sticker").Query<VisualElement>().ToList())
                Require(!element.focusable && element.pickingMode == PickingMode.Ignore,
                    "A sticker must not receive focus or pointer input.");
            var row = new VisualElement();
            var tab = new Button();
            row.Add(tab);
            MenuPrompts.Flank(row, MenuAction.PrevSection, MenuAction.NextSection);
            MenuPrompts.Flank(row, MenuAction.PrevSection, MenuAction.NextSection);
            Require(row.childCount == 3 && row[0] == tab
                && row.Q("menu-prompt-prev") != null && row.Q("menu-prompt-next") != null,
                "Re-flanking must preserve tabs and produce exactly two prompts.");
        }

        private static void SectionsWrap()
        {
            var adjacent = typeof(FrontendFocusRouter).GetMethod("AdjacentSection", BindingFlags.Static | BindingFlags.NonPublic)!;
            var bar = new VisualElement();
            var names = new[] { "shell-nav-training", "shell-nav-local", "shell-nav-online" };
            var tabs = names.Select(name => new Button { name = name }).ToArray();
            foreach (var tab in tabs) bar.Add(tab);
            Button Next(int step) => (Button)adjacent.Invoke(null, new object[] { bar, step })!;
            Require(Next(1) == tabs[0] && Next(-1) == tabs[2], "Home must enter Training to the right and Online to the left.");
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].AddToClassList("shell-nav-tab--selected");
                Require(Next(1) == tabs[(i + 1) % tabs.Length] && Next(-1) == tabs[(i + 2) % tabs.Length],
                    names[i] + " does not cycle in the approved order.");
                tabs[i].RemoveFromClassList("shell-nav-tab--selected");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
