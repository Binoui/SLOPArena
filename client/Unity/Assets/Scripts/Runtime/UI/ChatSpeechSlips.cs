#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using SlopArena.Client.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Room chat from match participants, printed as a paper speech slip over
    /// the speaker's fighter. When the fighter is off-screen, hidden or out of
    /// the match, the slip sticks out of their fight card instead; the local
    /// player's own lines always use their card. Presentation only: the HUD
    /// decides who is in the match, the chat overlay decides which messages
    /// become slips.
    /// </summary>
    internal sealed class ChatSpeechSlips
    {
        private const int MaxTextElements = 80;
        private const int MaxNameElements = 16;
        private const float MinSeconds = 3f;
        private const float MaxSeconds = 7f;
        private const float SecondsPerElement = 0.06f;
        private const float FadeSeconds = 0.3f;
        private const float PopSeconds = 0.08f;
        private const float TailHeight = 9f;
        private const float EdgeMargin = 8f;
        private const float HeadClearanceWorld = 0.22f;

        private sealed class Slip
        {
            public ulong EntityId;
            public VisualElement Root = null!;
            public float Age;
            public float Duration;
            public bool OnCard;
            public bool PreferCard;
        }

        private readonly VisualElement _layer;
        private readonly Func<ulong, VisualElement?> _cardFor;
        private readonly Func<VisualElement?> _lockTag;
        private readonly Dictionary<ulong, Slip> _slips = new();
        private readonly Dictionary<ulong, PlayerRenderer> _renderers = new();
        private readonly Dictionary<ulong, Transform?> _heads = new();
        private readonly List<ulong> _expired = new();

        public ChatSpeechSlips(VisualElement hudRoot, VisualElement? after,
            Func<ulong, VisualElement?> cardFor, Func<VisualElement?> lockTag)
        {
            _cardFor = cardFor;
            _lockTag = lockTag;
            _layer = new VisualElement { name = "chat-speech-slips", pickingMode = PickingMode.Ignore };
            _layer.AddToClassList("chat-speech-layer");
            // Just above the fight cards, below the pause menu and chat added later.
            var parent = after?.parent ?? hudRoot;
            parent.Insert(after?.parent != null ? parent.IndexOf(after) + 1 : parent.childCount, _layer);
        }

        /// <summary>
        /// Show one line for a fighter, replacing that fighter's previous slip.
        /// <paramref name="onCard"/> keeps it on the fight card: the local
        /// fighter stands under the camera, where a slip would cover the fight.
        /// </summary>
        public void Show(ulong entityId, string name, Color identity, string text, bool onCard = false)
        {
            if (_slips.TryGetValue(entityId, out var previous))
                previous.Root.RemoveFromHierarchy();

            string line = Shorten(text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), MaxTextElements);
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.AddToClassList("chat-slip");
            var shadow = new VisualElement { pickingMode = PickingMode.Ignore };
            shadow.AddToClassList("chat-slip__shadow");
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("chat-slip__box");
            var who = new Label(Shorten(name, MaxNameElements)) { enableRichText = false, pickingMode = PickingMode.Ignore };
            who.AddToClassList("chat-slip__who");
            who.style.backgroundColor = identity;
            var body = new Label(line) { enableRichText = false, pickingMode = PickingMode.Ignore };
            body.AddToClassList("chat-slip__text");
            var tail = new VisualElement { pickingMode = PickingMode.Ignore };
            tail.AddToClassList("chat-slip__tail");
            box.Add(body);
            box.Add(who);
            root.Add(shadow);
            root.Add(box);
            root.Add(tail);
            root.style.visibility = Visibility.Hidden;
            _layer.Add(root);

            int elements = new StringInfo(line).LengthInTextElements;
            _slips[entityId] = new Slip
            {
                EntityId = entityId,
                Root = root,
                Duration = Mathf.Clamp(MinSeconds + elements * SecondsPerElement, MinSeconds, MaxSeconds),
                PreferCard = onCard,
            };
        }

        public void Clear()
        {
            foreach (var slip in _slips.Values)
                slip.Root.RemoveFromHierarchy();
            _slips.Clear();
            _renderers.Clear();
            _heads.Clear();
        }

        /// <summary>Clear and remove the layer before the HUD rebuilds its roster.</summary>
        public void Detach()
        {
            Clear();
            _layer.RemoveFromHierarchy();
        }

        /// <summary>Advance lifetimes and place every slip after the cameras have moved.</summary>
        public void LateTick(float dt, UnityEngine.Camera? camera, bool reducedFlashing)
        {
            if (_slips.Count == 0)
                return;
            _expired.Clear();
            foreach (var slip in _slips.Values)
            {
                slip.Age += dt;
                if (slip.Age >= slip.Duration)
                {
                    _expired.Add(slip.EntityId);
                    continue;
                }
                float fade = Mathf.Clamp01((slip.Duration - slip.Age) / FadeSeconds);
                slip.Root.style.opacity = fade;
                float pop = reducedFlashing || slip.Age >= PopSeconds
                    ? 1f
                    : Mathf.Lerp(0.9f, 1f, slip.Age / PopSeconds);
                slip.Root.style.scale = new Scale(new Vector3(pop, pop, 1f));
                Place(slip, camera);
            }
            foreach (ulong id in _expired)
            {
                _slips[id].Root.RemoveFromHierarchy();
                _slips.Remove(id);
            }
        }

        private void Place(Slip slip, UnityEngine.Camera? camera)
        {
            float width = slip.Root.resolvedStyle.width;
            float height = slip.Root.resolvedStyle.height;
            float layerWidth = _layer.layout.width;
            float layerHeight = _layer.layout.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0f || height <= 0f ||
                !float.IsFinite(layerWidth) || !float.IsFinite(layerHeight) || layerWidth <= 0f)
                return;

            if (!slip.PreferCard && TryFighterAnchor(slip.EntityId, camera, layerWidth, layerHeight, out var anchor))
            {
                SetOnCard(slip, false);
                float left = Mathf.Clamp(anchor.x - width * 0.5f, EdgeMargin, Mathf.Max(EdgeMargin, layerWidth - width - EdgeMargin));
                float top = Mathf.Max(EdgeMargin, anchor.y - TailHeight - height);
                slip.Root.style.left = left;
                slip.Root.style.top = top;
                slip.Root.style.visibility = Visibility.Visible;
                return;
            }

            var card = _cardFor(slip.EntityId);
            if (card?.panel == null)
            {
                slip.Root.style.visibility = Visibility.Hidden;
                return;
            }
            SetOnCard(slip, true);
            Rect bounds = card.worldBound;
            Vector2 cardEdge = _layer.WorldToLocal(new Vector2(bounds.xMax, bounds.center.y));
            slip.Root.style.left = cardEdge.x + TailHeight + 6f;
            slip.Root.style.top = Mathf.Max(EdgeMargin, cardEdge.y - height * 0.5f);
            slip.Root.style.visibility = Visibility.Visible;
        }

        private static void SetOnCard(Slip slip, bool onCard)
        {
            if (slip.OnCard == onCard)
                return;
            slip.OnCard = onCard;
            slip.Root.EnableInClassList("chat-slip--card", onCard);
        }

        /// <summary>
        /// The point just above the speaker's head in layer coordinates, kept
        /// clear of the locked-target tag. False sends the slip to the card.
        /// </summary>
        private bool TryFighterAnchor(ulong entityId, UnityEngine.Camera? camera,
            float layerWidth, float layerHeight, out Vector2 anchor)
        {
            anchor = default;
            var card = _cardFor(entityId);
            if (card != null && card.ClassListContains("eliminated"))
                return false;
            if (camera == null || _layer.panel == null || !TryRenderer(entityId, out var renderer))
                return false;
            if (!renderer.isActiveAndEnabled || !renderer.gameObject.activeInHierarchy)
                return false;

            if (!_heads.TryGetValue(entityId, out var head))
                _heads[entityId] = head = renderer.ResolvePresentationBone("bone.head");
            // Just over the presented head; the capsule top is only a fallback.
            Vector3 top = head != null
                ? head.position + Vector3.up * HeadClearanceWorld
                : renderer.transform.position + Vector3.up * renderer.CapsuleHeight;
            Vector3 screen = camera.WorldToScreenPoint(top);
            if (screen.z <= 0.001f || !float.IsFinite(screen.x) || !float.IsFinite(screen.y))
                return false;
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(_layer.panel,
                new Vector2(screen.x, Screen.height - screen.y));
            anchor = _layer.WorldToLocal(panelPoint);
            if (anchor.x < 0f || anchor.x > layerWidth || anchor.y < 0f || anchor.y > layerHeight)
                return false;

            // The locked-target tag sits over a head too: stack the slip above it.
            var lockTag = _lockTag();
            if (lockTag?.panel != null && lockTag.ClassListContains("visible") &&
                lockTag.resolvedStyle.visibility == Visibility.Visible)
            {
                Rect tag = lockTag.worldBound;
                Vector2 tagTop = _layer.WorldToLocal(new Vector2(tag.center.x, tag.yMin));
                Vector2 tagBottom = _layer.WorldToLocal(new Vector2(tag.center.x, tag.yMax));
                bool sameColumn = Mathf.Abs(tagTop.x - anchor.x) < Mathf.Max(tag.width, 40f);
                bool overlapsSlip = tagBottom.y > anchor.y - 90f && tagTop.y < anchor.y + 24f;
                if (sameColumn && overlapsSlip)
                    anchor.y = Mathf.Min(anchor.y, tagTop.y - 4f);
            }
            return true;
        }

        private bool TryRenderer(ulong entityId, out PlayerRenderer renderer)
        {
            if (_renderers.TryGetValue(entityId, out renderer) && renderer != null)
                return true;
            _renderers.Remove(entityId);
            _heads.Remove(entityId);
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<PlayerRenderer>(FindObjectsSortMode.None))
            {
                if (candidate != null && candidate.EntityId == entityId)
                {
                    _renderers[entityId] = renderer = candidate;
                    return true;
                }
            }
            renderer = null!;
            return false;
        }

        /// <summary>Shorten by whole text elements so surrogate pairs and emoji stay intact.</summary>
        internal static string Shorten(string text, int maxElements)
        {
            var info = new StringInfo(text ?? string.Empty);
            if (info.LengthInTextElements <= maxElements)
                return info.String;
            return info.SubstringByTextElements(0, Math.Max(1, maxElements - 1)).TrimEnd() + "…";
        }
    }
}
