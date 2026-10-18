using System.Linq;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Night Court room reactions: the hand-flipped combo board shows the live combo and the
    /// floods pop on a return. Presentation only; it reads Slopball state and events through
    /// <see cref="SlopballMatch"/> and never feeds back. A court without the Night Court art
    /// (an older local prefab) simply has no reactions.
    /// </summary>
    public sealed class SlopballCourtShow
    {
        public const string RootName = "Slopball Night Court";
        public const string KeyLightName = "Flood Key";
        // The generator's atlas layout (tools/blender/slopball/textures.py): digit cards 0-9 and a
        // blank sit side by side, DigitCellPixels wide, in an AtlasPixels-square atlas.
        public const int DigitCellPixels = 128;
        public const int AtlasPixels = 4096;
        public const float FloodGlow = 1.6f;
        public static readonly string[] CardNames = { "Combo Hundreds", "Combo Tens", "Combo Ones" };
        private const int BlankCard = 10;
        private const float FlipSeconds = 0.16f;
        private const float FlashSeconds = 0.6f;
        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private sealed class Card
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 Scale;
            public int Face = -1, Target = -1;
            public float Flip = -1f;
        }

        private readonly Card[] _cards;
        private readonly Renderer _floods, _beams;
        private readonly Light _key;
        private readonly float _keyIntensity;
        private readonly MaterialPropertyBlock _block = new();
        private float _flash;
        private bool _lit;

        private SlopballCourtShow(Card[] cards, Renderer floods, Renderer beams, Light key)
        {
            _cards = cards;
            _floods = floods;
            _beams = beams;
            _key = key;
            _keyIntensity = key != null ? key.intensity : 0f;
        }

        public static SlopballCourtShow Find(Transform stage)
        {
            var root = stage != null ? stage.Find(RootName) : null;
            if (root == null) return null;
            var cards = CardNames.Select(name => root.Find(name)).ToArray();
            if (cards.Any(x => x == null || x.GetComponent<Renderer>() == null)) return null;
            var key = stage.Find("Stage Lights/" + KeyLightName);
            return new SlopballCourtShow(
                cards.Select(x => new Card { Transform = x, Renderer = x.GetComponent<Renderer>(), Scale = x.localScale }).ToArray(),
                root.Find("Flood Panels")?.GetComponent<Renderer>(),
                root.Find("Flood Beams")?.GetComponent<Renderer>(),
                key != null ? key.GetComponent<Light>() : null);
        }

        /// <summary>Show the live combo (capped at 999); changed cards flip over.</summary>
        public void Present(ulong combo)
        {
            ulong shown = combo > 999 ? 999 : combo;
            Show(_cards[0], shown >= 100 ? (int)(shown / 100) : BlankCard);
            Show(_cards[1], (int)(shown / 10 % 10));
            Show(_cards[2], (int)(shown % 10));
        }

        public void Returned() => _flash = 1f;

        public void Tick(float deltaTime)
        {
            foreach (var card in _cards)
            {
                if (card.Flip < 0f || card.Transform == null) continue;
                card.Flip += deltaTime / FlipSeconds;
                if (card.Flip >= 0.5f && card.Face != card.Target) SetFace(card, card.Target);
                if (card.Flip >= 1f) card.Flip = -1f;
                float squash = card.Flip < 0f ? 1f : Mathf.Abs(Mathf.Cos(card.Flip * Mathf.PI));
                card.Transform.localScale = new Vector3(card.Scale.x, card.Scale.y * Mathf.Max(0.02f, squash), card.Scale.z);
            }
            if (_flash <= 0f && !_lit) return;
            _flash = Mathf.Max(0f, _flash - deltaTime / FlashSeconds);
            float k = _flash * _flash;
            _lit = _flash > 0f;
            if (_floods != null)
            {
                _floods.GetPropertyBlock(_block);
                _block.SetFloat(GlowId, FloodGlow * (1f + 0.9f * k));
                _floods.SetPropertyBlock(_block);
                _block.Clear();
            }
            if (_beams != null)
            {
                _beams.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, Color.white * (1f + 0.5f * k));
                _beams.SetPropertyBlock(_block);
                _block.Clear();
            }
            if (_key != null) _key.intensity = _keyIntensity * (1f + 0.2f * k);
        }

        private void Show(Card card, int face)
        {
            if (card.Target == face) return;
            card.Target = face;
            if (card.Face < 0) { SetFace(card, face); return; } // first frame: no flip
            if (card.Flip < 0f) card.Flip = 0f;
        }

        private void SetFace(Card card, int face)
        {
            card.Face = face;
            card.Renderer.GetPropertyBlock(_block);
            _block.SetVector(BaseMapSt, new Vector4(1f, 1f, face * (float)DigitCellPixels / AtlasPixels, 0f));
            card.Renderer.SetPropertyBlock(_block);
            _block.Clear();
        }
    }
}
