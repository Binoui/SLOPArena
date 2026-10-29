#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SlopArena.EditorTools
{
    public readonly struct FrameSize
    {
        public readonly int Width, Height;
        public FrameSize(int width, int height) { Width = width; Height = height; }
        public int[] ToArray() => new[] { Width, Height };
    }

    public static class CaptureBudget
    {
        public const double MaxMegapixelsPerCommand = 1400;
        public const int MaxViews = 10, MaxTicks = 600;
        public static readonly FrameSize MinSize = new(640, 360);

        public static FrameSize Fit(FrameSize requested, int views, int frames)
        {
            if (views < 1 || views > MaxViews || frames < 1 || frames > MaxTicks
                || requested.Width < MinSize.Width || requested.Height < MinSize.Height
                || requested.Width > 3840 || requested.Height > 2160
                || requested.Width % 2 != 0 || requested.Height % 2 != 0)
                throw new ArgumentException("Use 1–10 views, 1–600 ticks and even sizes from 640x360 to 3840x2160.");
            int a = requested.Width, b = requested.Height;
            while (b != 0) { int next = a % b; a = b; b = next; }
            int dx = requested.Width / a * 2, dy = requested.Height / a * 2;
            int width = requested.Width, height = requested.Height;
            while (views * (double)frames * width * height / 1e6 > MaxMegapixelsPerCommand)
            {
                width -= dx; height -= dy;
                if (width < MinSize.Width || height < MinSize.Height)
                    throw new ArgumentException("This aspect ratio cannot fit the capture budget above 640x360; use 16:9.");
            }
            return new FrameSize(width, height);
        }
    }

    public sealed class CaptureView : IDisposable
    {
        public string Name { get; }
        public UnityEngine.Camera Camera { get; }
        public RenderTexture Target { get; }
        private readonly Texture2D _pixels;

        private CaptureView(string name, UnityEngine.Camera template, FrameSize size)
        {
            Name = name;
            Camera = new GameObject($"Capture Camera {name} (temporary)").AddComponent<UnityEngine.Camera>();
            Camera.CopyFrom(template);
            Camera.tag = "Untagged";
            var data = Camera.GetUniversalAdditionalCameraData();
            var source = template.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = source.renderPostProcessing;
            data.antialiasing = source.antialiasing;
            data.antialiasingQuality = source.antialiasingQuality;
            data.SetRenderer(new UnityEditor.SerializedObject(source).FindProperty("m_RendererIndex").intValue);
            Target = new RenderTexture(size.Width, size.Height, 24) { antiAliasing = 4, name = "Capture " + name };
            Target.Create();
            Camera.targetTexture = Target;
            Camera.aspect = (float)size.Width / size.Height;
            Camera.enabled = true;
            _pixels = new Texture2D(size.Width, size.Height, TextureFormat.RGB24, false);
        }

        public static CaptureView Create(string name, UnityEngine.Camera template, FrameSize size)
            => new(name, template, size);

        public void Place(Vector3 position, Vector3 look, float fov)
        {
            Camera.orthographic = false;
            Camera.fieldOfView = fov;
            Camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position, Vector3.up));
        }

        public RectInt Project(Bounds bounds)
        {
            Vector2 min = new(Target.width, Target.height), max = Vector2.zero;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 q = Camera.WorldToViewportPoint(p);
                if (q.z <= 0) continue;
                var pixel = new Vector2(q.x * Target.width, (1f - q.y) * Target.height);
                min = Vector2.Min(min, pixel); max = Vector2.Max(max, pixel);
            }
            int x0 = Mathf.Clamp(Mathf.FloorToInt(min.x), 0, Target.width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(min.y), 0, Target.height - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(max.x), x0 + 1, Target.width);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(max.y), y0 + 1, Target.height);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        public void WriteJpg(string path, int quality)
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = Target;
                _pixels.ReadPixels(new Rect(0, 0, Target.width, Target.height), 0, 0, false);
                File.WriteAllBytes(path, _pixels.EncodeToJPG(quality));
            }
            finally { RenderTexture.active = previous; }
        }

        public void Dispose()
        {
            if (Camera != null) { Camera.enabled = false; UnityEngine.Object.Destroy(Camera.gameObject); }
            if (Target != null) { Target.Release(); UnityEngine.Object.Destroy(Target); }
            if (_pixels != null) UnityEngine.Object.Destroy(_pixels);
        }
    }
}
#endif
