using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// The finish poster, the bookend of the start flyer: Solo, couch and every online match end on it.
    /// Read-only; the match result owns the outcome.
    /// </summary>
    internal sealed class SoloEndCardView
    {
        private const float Duration = 2f;
        private const float SlamSeconds = 0.32f;
        private const float SlamScale = 1.7f;
        // Size before the first layout: the portrait slip and declaration at the reference resolution.
        private static readonly Vector2 FallbackSize = new(1000f, 330f);
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
        private float _duration = Duration;
        private VisualElement _standings;

        public void ShowMayhem(System.Collections.Generic.IReadOnlyList<MayhemPlayer> players, System.Func<ulong, string> name, bool online = false)
        {
            var ordered = new System.Collections.Generic.List<MayhemPlayer>(players);
            ordered.Sort((a, b) => a.Withdrawn != b.Withdrawn ? a.Withdrawn.CompareTo(b.Withdrawn)
                : a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.EntityId.CompareTo(b.EntityId));
            int winners = ordered.FindAll(p => p.Score == ordered[0].Score && !p.Withdrawn).Count;
            ShowPoster("SHOW'S\nOVER!", winners == 0 ? "MATCH ENDED" : winners > 1 ? "SHARED WIN!" : $"{name(ordered[0].EntityId)} WINS!",
                "MANKI MAYHEM", CharacterClass.None);
            _duration = float.PositiveInfinity;
            _standings = new VisualElement { name = "mayhem-standings", pickingMode = PickingMode.Ignore };
            _standings.style.position = Position.Absolute;
            _standings.style.left = Length.Percent(27); _standings.style.right = Length.Percent(27);
            _standings.style.top = Length.Percent(53);
            _standings.style.backgroundColor = new Color(.07f, .045f, .07f, .97f);
            _standings.style.paddingTop = _standings.style.paddingBottom = 14;
            _standings.style.paddingLeft = _standings.style.paddingRight = 22;
            foreach (var player in ordered)
            {
                int place = 1 + ordered.FindAll(p => !p.Withdrawn && p.Score > player.Score).Count;
                var row = new Label($"{(player.Withdrawn ? "OUT" : place.ToString())}.  {name(player.EntityId)}     {player.Score} POINTS\n     {player.RoundsSurvived} SURVIVED · {player.Bonuses} BONUS");
                row.style.fontSize = 22; row.style.color = !player.Withdrawn && player.Score == ordered[0].Score ? new Color(1f, .77f, .12f) : Color.white;
                row.style.marginBottom = 10; _standings.Add(row);
            }
            var hint = new Label(online ? "RESULTS" : "PAUSE TO LEAVE · STOP / PLAY TO REPLAY");
            hint.style.fontSize = 14; hint.style.color = Color.white; _standings.Add(hint);
            _root.Add(_standings);
        }

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

        public void Show(string winningSeat, CharacterClass winner, bool sharedVictory)
        {
            ShowPoster(sharedVictory ? "DOUBLE\nK.O.!" : "BEST\nSLOPPER",
                sharedVictory ? "SHARED VICTORY" : $"{winningSeat.ToUpperInvariant()} WINS!",
                winner.ToString().ToUpperInvariant(), winner, shared: sharedVictory);
        }

        /// <summary>
        /// Prints the finish poster: a two-line <paramref name="headline"/> over the <paramref name="outcome"/> bar,
        /// with a taped portrait slip of <paramref name="portrait"/> (and <paramref name="other"/> for a shared win).
        /// Without a portrait the poster is the declaration alone (team results such as Slopball).
        /// </summary>
        public void ShowPoster(string headline, string outcome, string name, CharacterClass portrait,
            CharacterClass other = CharacterClass.None, bool shared = false)
        {
            if (_root == null) return;
            _standings?.RemoveFromHierarchy();
            _duration = Duration;
            bool slip = portrait != CharacterClass.None;
            _root.EnableInClassList("solo-end--shared", shared && other != CharacterClass.None);
            _root.EnableInClassList("solo-end--team", !slip);
            _title.text = _print.text = headline;
            _outcome.text = outcome;
            _name.text = name;
            _portrait.image = slip ? Resources.Load<Texture2D>($"UI/Portraits/{portrait}") : null;
            _otherPortrait.image = other != CharacterClass.None ? Resources.Load<Texture2D>($"UI/Portraits/{other}") : null;
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
            if (_age >= _duration) { Clear(); return; }
            float width = _root.parent.layout.width, height = _root.parent.layout.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0f || height <= 0f) return;
            Rect laid = _poster.layout;
            Vector2 size = float.IsFinite(laid.width) && laid.width > 1f ? laid.size : FallbackSize;
            float cardRight = _players != null ? _players.worldBound.xMax - _root.worldBound.xMin : 0f;
            // Reserve the tilted corners, tape and hard shadow as well as the settled field.
            float fit = Mathf.Min(1f, Mathf.Max(1f, width - cardRight - 56f) / (size.x * 1.08f), height * 0.5f / size.y);
            bool mayhem = _standings != null && _standings.parent == _root;
            if (mayhem) fit *= .55f;
            float half = size.x * fit * 0.54f;
            _anchor.style.left = Mathf.Clamp(Mathf.Max(width * 0.5f, cardRight + 28f + half), half + 20f, width - half - 20f);
            _anchor.style.top = height * (mayhem ? .42f : .34f);

            // Slammed up like the start flyer, then torn off sideways.
            bool reduced = ClientSettingsService.Instance.ReducedFlashing;
            float slam = reduced ? 1f : Mathf.Clamp01(_age / SlamSeconds);
            float exit = reduced ? 0f : Mathf.Clamp01((_age - (_duration - 0.18f)) / 0.18f);
            _poster.style.scale = new Scale(Vector2.one * (fit * Mathf.LerpUnclamped(SlamScale, 1f, EaseOutBack(slam))));
            _poster.style.translate = new Translate(28f * exit, 0f, 0f);
            _root.style.opacity = Mathf.Min(1f, slam / 0.35f) * (1f - exit);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        public void Clear()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
        }
    }
}
