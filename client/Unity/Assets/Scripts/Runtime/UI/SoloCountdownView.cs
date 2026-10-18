using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Solo's printed start stamps. The match owns every beat and admission tick.</summary>
    internal sealed class SoloCountdownView
    {
        private const float MaxStampWidth = 468f;
        private readonly VisualElement _root;
        private readonly VisualElement _anchor;
        private readonly VisualElement _stamp;
        private readonly VisualElement _players;
        private readonly Label _title;
        private readonly Label _print;
        private float _age;
        private float _duration;
        private float _tilt;

        public SoloCountdownView(VisualElement hud)
        {
            _root = hud.Q<VisualElement>("solo-countdown");
            _anchor = hud.Q<VisualElement>("solo-countdown-anchor");
            _stamp = hud.Q<VisualElement>("solo-countdown-stamp");
            _players = hud.Q<VisualElement>("player-billboard");
            _title = hud.Q<Label>("solo-countdown-title");
            _print = hud.Q<Label>("solo-countdown-print");
            _root?.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
            Clear();
        }

        /// <summary>
        /// Prints <paramref name="text"/>. <paramref name="wide"/> uses the gold start card for other
        /// headlines (the Character Course's ALL CLEAR!), which bring their own sound.
        /// </summary>
        public void Show(string text, float seconds, bool wide = false, bool sound = true)
        {
            if (_root == null || _stamp == null || _title == null || _print == null) return;
            bool number = text is "3" or "2" or "1";
            bool start = wide || text == "SLOP IT OUT";
            _root.EnableInClassList("solo-countdown--number", number);
            _root.EnableInClassList("solo-countdown--start", start);
            _title.text = _print.text = text;
            _age = 0f;
            _duration = Mathf.Max(0.2f, seconds);
            _tilt = text switch { "3" => -7f, "2" => 4f, "1" => -3f, _ => -4f };
            _root.style.display = DisplayStyle.Flex;
            Tick(0f);
            if (sound) UISFX.PlayCountdownBeat(text);
        }

        public void Tick(float dt)
        {
            if (_root == null || _root.style.display == DisplayStyle.None) return;
            _age += dt;
            if (_age >= _duration) { Clear(); return; }

            // The hidden overlay has no layout; its live HUD parent already does.
            float width = _root.parent.layout.width, height = _root.parent.layout.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0f || height <= 0f) return;
            float cardRight = _players != null ? _players.worldBound.xMax - _root.worldBound.xMin : 0f;
            float available = Mathf.Max(1f, width - cardRight - 48f);
            float fit = Mathf.Min(1f, available / MaxStampWidth, height * 0.4f / 190f);
            float half = MaxStampWidth * fit * 0.5f;
            float centre = Mathf.Clamp(Mathf.Max(width * 0.5f, cardRight + 24f + half), half + 18f, width - half - 18f);
            _anchor.style.left = centre;
            _anchor.style.top = height * 0.32f;

            bool reduced = ClientSettingsService.Instance.ReducedFlashing;
            float impact = reduced ? 0f : Mathf.Exp(-_age / 0.035f);
            float exit = reduced ? 0f : Mathf.Clamp01((_age - (_duration - 0.16f)) / 0.16f);
            _stamp.style.scale = new Scale(Vector2.one * (fit * (1f + 0.16f * impact)));
            _stamp.style.rotate = new Rotate(new Angle(_tilt, AngleUnit.Degree));
            _stamp.style.translate = new Translate(18f * exit, -12f * impact, 0f);
            _root.style.opacity = 1f - exit;
        }

        public void Clear()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
        }
    }
}
