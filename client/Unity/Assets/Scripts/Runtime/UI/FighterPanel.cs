using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    internal static class FighterPanel
    {
        public static VisualElement Build(int playerNumber, string name, string role, CharacterClass? fighter,
            string status, bool interactive = false, bool compact = false)
        {
            VisualElement panel = interactive ? new Button() : new VisualElement();
            panel.AddToClassList("fighter-panel");
            panel.EnableInClassList("fighter-panel--compact", compact);
            panel.EnableInClassList("fighter-panel--open", !fighter.HasValue && role == "OPEN");
            var colour = SeatColours.For(playerNumber);
            var heading = new VisualElement();
            heading.AddToClassList("fighter-panel__heading");
            heading.style.backgroundColor = colour;
            heading.Add(Text($"P{playerNumber}", "fighter-panel__number"));
            heading.Add(Text(role, "fighter-panel__role"));
            heading.Add(Text(name, "fighter-panel__name"));
            panel.Add(heading);
            if (fighter.HasValue)
            {
                panel.AddToClassList($"fighter-panel--{fighter.Value}");
                var art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.AddToClassList("fighter-panel__art");
                var portrait = Resources.Load<Texture2D>($"UI/Portraits/{fighter.Value}");
                if (portrait != null) art.style.backgroundImage = new StyleBackground(portrait);
                // Splash images are stylesheet dependencies, so they ship without Resources copies.
                if (fighter.Value is CharacterClass.Manki or CharacterClass.FightGuy or CharacterClass.Wibou or CharacterClass.Bonk)
                    art.style.backgroundImage = StyleKeyword.Null;
                if (portrait == null && fighter.Value is not (CharacterClass.Manki or CharacterClass.FightGuy or CharacterClass.Wibou or CharacterClass.Bonk))
                    art.Add(Text("?", "fighter-panel__placeholder"));
                panel.Add(art);
                panel.Add(Text(MenuRoster.Name(fighter.Value).ToUpperInvariant(), "fighter-panel__fighter"));
                if (!compact) panel.Add(Text(MenuRoster.Description(fighter.Value), "fighter-panel__description"));
            }
            else
                panel.Add(Text($"P{playerNumber} · {(role == "OPEN" ? "OPEN" : "PICKING")}", "fighter-panel__empty"));
            panel.Add(Text(status, "fighter-panel__status"));
            return panel;
        }

        public static void Frame(VisualElement element, int playerNumber)
        {
            var colour = SeatColours.For(playerNumber);
            element.style.borderTopColor = element.style.borderBottomColor =
                element.style.borderLeftColor = element.style.borderRightColor = colour;
        }

        private static Label Text(string text, string style)
        {
            var label = new Label(text) { enableRichText = false, pickingMode = PickingMode.Ignore };
            label.AddToClassList(style);
            return label;
        }
    }
}
