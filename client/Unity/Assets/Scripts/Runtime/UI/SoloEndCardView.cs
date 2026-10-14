using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Read-only Solo finish poster. The existing match result owns the outcome.</summary>
    internal sealed class SoloEndCardView
    {
        private const float Width = 640f;
        private readonly VisualElement _root;
        private readonly VisualElement _anchor;
        private readonly VisualElement _poster;
        private readonly VisualElement _players;
        private readonly Image _portrait;
        private readonly Image _otherPortrait;
        private readonly Label _name;
        private readonly Label _title;
        private readonly Label _print;
        private readonly Label _outcome;
        private float _age;

        public SoloEndCardView(VisualElement hud)
        {
            _root = hud.Q<VisualElement>("solo-end");
            _anchor = hud.Q<VisualElement>("solo-end-anchor");
            _poster = hud.Q<VisualElement>("solo-end-poster");
            _players = hud.Q<VisualElement>("player-billboard");
            _portrait = hud.Q<Image>("solo-end-portrait");
            _otherPortrait = hud.Q<Image>("solo-end-other-portrait");
            _name = hud.Q<Label>("solo-end-name");
            _title = hud.Q<Label>("solo-end-title");
            _print = hud.Q<Label>("solo-end-print");
            _outcome = hud.Q<Label>("solo-end-outcome");
            _root?.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
            Clear();
        }

        public void Show(bool playerWon, bool sharedVictory, CharacterClass player, CharacterClass cpu)
        {
            if (_root == null) return;
            CharacterClass winner = playerWon || sharedVictory ? player : cpu;
            _root.EnableInClassList("solo-end--shared", sharedVictory);
            _title.text = _print.text = sharedVictory ? "DOUBLE\nK.O.!" : "SLOP\nSETTLED.";
            _outcome.text = sharedVictory ? "SHARED VICTORY" : playerWon ? "YOU WIN!" : "CPU WINS!";
            _name.text = sharedVictory ? $"{player.ToString().ToUpperInvariant()}\n{cpu.ToString().ToUpperInvariant()}"
                : winner.ToString().ToUpperInvariant();
            _portrait.image = Resources.Load<Texture2D>($"UI/Portraits/{winner}");
            _otherPortrait.image = sharedVictory ? Resources.Load<Texture2D>($"UI/Portraits/{cpu}") : null;
            _portrait.scaleMode = _otherPortrait.scaleMode = ScaleMode.ScaleToFit;
            _age = 0f;
            _root.style.display = DisplayStyle.Flex;
            Tick(0f);
            UISFX.PlaySoloFinish();
        }

        public void Tick(float dt)
        {
            if (_root == null || _root.style.display == DisplayStyle.None) return;
            _age += dt;
            if (_age >= 2f) { Clear(); return; }
            float width = _root.parent.layout.width, height = _root.parent.layout.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0f || height <= 0f) return;
            float cardRight = _players != null ? _players.worldBound.xMax - _root.worldBound.xMin : 0f;
            float fit = Mathf.Min(1f, Mathf.Max(1f, width - cardRight - 56f) / Width, height * 0.45f / 240f);
            // Reserve the brief entrance scale and tilted corners as well as the settled field.
            float half = Width * fit * 0.575f;
            _anchor.style.left = Mathf.Clamp(Mathf.Max(width * 0.5f, cardRight + 28f + half), half + 20f, width - half - 20f);
            _anchor.style.top = height * 0.32f;
            float impact = ClientSettingsService.Instance.ReducedFlashing ? 0f : Mathf.Exp(-_age / 0.045f);
            _poster.style.scale = new Scale(Vector2.one * (fit * (1f + 0.12f * impact)));
            _poster.style.translate = new Translate(0f, -16f * impact, 0f);
        }

        public void Clear()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
        }
    }
}
