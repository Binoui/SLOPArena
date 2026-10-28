using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.Camera
{
    /// <summary>
    /// Steady camera (ADR-0043): an edge chip for each opponent the view cannot hold,
    /// like Smash's off-screen bubbles. Shows height above the floor (or distance) and points
    /// toward the subject.
    /// </summary>
    internal sealed class OffscreenBubbles : IDisposable
    {
        private const float Size = 46f;
        private readonly List<(VisualElement chip, VisualElement pointer, Label text)> _pool = new();
        private VisualElement? _root;
        private int _used;

        public void Begin(VisualElement root)
        {
            if (_root != root)
            {
                Dispose();
                _root = root;
            }
            _used = 0;
        }

        public void Show(UnityEngine.Camera camera, Vector3 world, float heightAboveFloor, Vector3 player, Color color)
        {
            if (_root == null) return;
            Vector3 viewport = camera.WorldToViewportPoint(world);
            bool behind = viewport.z < 0f;
            if (!behind && viewport.x is > 0.02f and < 0.98f && viewport.y is > 0.02f and < 0.98f) return;
            Vector2 point = new(viewport.x - .5f, viewport.y - .5f);
            if (behind) point = -point;
            if (point.sqrMagnitude < 0.0001f) point = Vector2.down;
            // Push the direction out to the screen edge, keeping the chip fully visible.
            float width = _root.layout.width, height = _root.layout.height;
            if (float.IsNaN(width) || width <= 0f || height <= 0f) return;
            float scale = Mathf.Min((.5f - Size / 2f / width) / Mathf.Max(1e-4f, Mathf.Abs(point.x)),
                (.5f - Size / 2f / height) / Mathf.Max(1e-4f, Mathf.Abs(point.y)));
            Vector2 edge = point * Mathf.Min(1f, scale);
            if (behind) edge = point * scale;
            float x = (edge.x + .5f) * width, y = (.5f - edge.y) * height;
            // Keep clear of the fighter cards in the top corners.
            if ((x < width * .22f || x > width * .78f) && y < height * .27f) y = height * .27f;

            var (chip, pointer, text) = Take();
            chip.style.left = x - Size / 2f;
            chip.style.top = y - Size / 2f;
            chip.style.borderTopColor = chip.style.borderBottomColor =
                chip.style.borderLeftColor = chip.style.borderRightColor = color;
            pointer.style.backgroundColor = color;
            float angle = Mathf.Atan2(-point.y, point.x) * Mathf.Rad2Deg;
            pointer.style.rotate = new Rotate(new Angle(angle + 45f));
            float radius = Size / 2f + 2f;
            pointer.style.left = Size / 2f - 6f + Mathf.Cos(angle * Mathf.Deg2Rad) * radius;
            pointer.style.top = Size / 2f - 6f + Mathf.Sin(angle * Mathf.Deg2Rad) * radius;
            Vector3 flat = world - player;
            flat.y = 0f;
            text.text = heightAboveFloor > 1.5f ? $"{heightAboveFloor:0}m\nUP" : $"{flat.magnitude:0}m";
            chip.style.display = DisplayStyle.Flex;
        }

        public void End()
        {
            for (int i = _used; i < _pool.Count; i++)
                _pool[i].chip.style.display = DisplayStyle.None;
        }

        public void Hide()
        {
            _used = 0;
            End();
        }

        private (VisualElement, VisualElement, Label) Take()
        {
            if (_used < _pool.Count) return _pool[_used++];
            var chip = new VisualElement { name = "offscreen-bubble", pickingMode = PickingMode.Ignore };
            chip.style.position = Position.Absolute;
            chip.style.width = chip.style.height = Size;
            chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius =
                chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = Size / 2f;
            chip.style.borderTopWidth = chip.style.borderBottomWidth =
                chip.style.borderLeftWidth = chip.style.borderRightWidth = 3f;
            chip.style.backgroundColor = new Color(0.08f, 0.075f, 0.065f, 0.9f);
            chip.style.justifyContent = Justify.Center;
            chip.style.alignItems = Align.Center;
            var pointer = new VisualElement { pickingMode = PickingMode.Ignore };
            pointer.style.position = Position.Absolute;
            pointer.style.width = pointer.style.height = 12f;
            var text = new Label { pickingMode = PickingMode.Ignore };
            text.style.color = Color.white;
            text.style.fontSize = 12f;
            text.style.unityFontStyleAndWeight = FontStyle.Bold;
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.style.paddingLeft = text.style.paddingRight = 0f;
            chip.Add(pointer);
            chip.Add(text);
            _root!.Add(chip);
            _pool.Add((chip, pointer, text));
            _used++;
            return (chip, pointer, text);
        }

        public void Dispose()
        {
            foreach (var entry in _pool) entry.chip.RemoveFromHierarchy();
            _pool.Clear();
            _used = 0;
            _root = null;
        }
    }
}
