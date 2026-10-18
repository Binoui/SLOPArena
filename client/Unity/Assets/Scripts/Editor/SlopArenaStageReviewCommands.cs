#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Free review cameras and the stage-select thumbnail. Poses live in the stage's
    /// presentation data (tools/blender/&lt;stage&gt;/presentation.py, "cameras"), so low,
    /// recovery, hero and thumbnail shots are repeatable rather than eval scripts per stage.
    /// Captures run inside a sloparena.stage.views.* Play Mode session; the thumbnail is
    /// installed afterwards in Edit Mode.
    /// </summary>
    public static class SlopArenaStageReviewCommands
    {
        private const int ThumbnailWidth = 1280, ThumbnailHeight = 720;

        [CliCommand("sloparena.stage.views.cameras",
            "Capture the stage's named review cameras (presentation \"cameras\") without the HUD, inside a stage-views Play Mode session.",
            MainThreadRequired = true, Tags = new[] { "stage", "capture" })]
        public static async Task<object> Cameras(
            [CliArg("stage", "Stage key of the active stage-views session.", Required = true)] string stage,
            [CliArg("output", "Fresh repository-relative directory below tmp/, .impeccable/, .ability-lab-cache/ or client/Unity/Temp/.", Required = true)] string output,
            [CliArg("names", "Comma list of camera names; defaults to every camera in the presentation.")] string names = null)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run inside a stage-views Play Mode session (sloparena.stage.views.prepare, editor_play).");
            var cameras = ReadCameras(stage);
            var wanted = string.IsNullOrWhiteSpace(names) ? null : names.Split(',').Select(x => x.Trim()).ToHashSet();
            if (wanted != null && wanted.Except(cameras.Select(c => c.name)).FirstOrDefault() is string missing)
                throw new ArgumentException("No camera '" + missing + "' in the " + stage + " presentation.");
            output = (output ?? "").TrimEnd('/');
            var main = Camera.main ?? Camera.allCameras.OrderByDescending(c => c.depth).FirstOrDefault()
                ?? throw new InvalidOperationException("No gameplay camera to copy.");
            var mainData = main.GetComponent<UniversalAdditionalCameraData>();
            // Hide the HUD documents for the session; anything world-space stays as the player sees it.
            var hidden = new List<(VisualElement element, StyleEnum<DisplayStyle> display)>();
            foreach (var document in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (document.rootVisualElement != null)
                {
                    hidden.Add((document.rootVisualElement, document.rootVisualElement.style.display));
                    document.rootVisualElement.style.display = DisplayStyle.None;
                }
            var captures = new List<object>();
            try
            {
                foreach (var pose in cameras.Where(c => wanted == null || wanted.Contains(c.name)))
                {
                    var go = new GameObject("Stage Review " + pose.name);
                    try
                    {
                        var camera = go.AddComponent<Camera>();
                        camera.CopyFrom(main);
                        camera.transform.position = pose.position;
                        camera.transform.LookAt(pose.target);
                        camera.fieldOfView = pose.fov;
                        camera.nearClipPlane = 0.1f;
                        camera.farClipPlane = Mathf.Max(main.farClipPlane, 600f);
                        camera.enabled = false;
                        var data = go.GetComponent<UniversalAdditionalCameraData>() ?? go.AddComponent<UniversalAdditionalCameraData>();
                        if (mainData != null)
                        {
                            data.renderPostProcessing = mainData.renderPostProcessing;
                            data.antialiasing = mainData.antialiasing;
                            data.volumeLayerMask = mainData.volumeLayerMask;
                            data.renderShadows = mainData.renderShadows;
                        }
                        var capture = await SlopArenaCaptureCommands.Capture($"{output}/{pose.name}.png", "camera", pose.width, pose.height, go.name);
                        captures.Add(new { camera = pose.name, position = new[] { pose.position.x, pose.position.y, pose.position.z },
                            target = new[] { pose.target.x, pose.target.y, pose.target.z }, pose.fov, capture });
                    }
                    finally { Object.Destroy(go); }
                }
            }
            finally
            {
                foreach (var (element, display) in hidden) element.style.display = display;
            }
            return new { success = true, stage, output, hudDocumentsHidden = hidden.Count, postProcessing = mainData != null && mainData.renderPostProcessing, captures };
        }

        [CliCommand("sloparena.stage.views.frametime",
            "Sample Play Mode frame times for a few seconds inside a stage-views session: mean, median, 95th percentile and worst, with the quality level and Game View size.",
            MainThreadRequired = true, Tags = new[] { "stage", "capture" })]
        public static async Task<object> FrameTime(
            [CliArg("seconds", "Sampling time in seconds (1-30).")] float seconds = 5f,
            [CliArg("uncapped", "Lift vsync and the target frame rate while sampling (restored afterwards) so the time measures the frame's cost, not the cap.")] bool uncapped = true)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run inside a stage-views Play Mode session (sloparena.stage.views.prepare, editor_play).");
            seconds = Mathf.Clamp(seconds, 1f, 30f);
            int vSync = QualitySettings.vSyncCount, target = Application.targetFrameRate;
            var samples = new List<float>();
            try
            {
                if (uncapped) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }
                // Let the first frames after Play Mode entry and any capture settle before sampling.
                double start = EditorApplication.timeSinceStartup + 1.0, end = start + seconds;
                int lastFrame = Time.frameCount;
                while (EditorApplication.timeSinceStartup < end)
                {
                    await Task.Yield();
                    if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode stopped while sampling.");
                    if (Time.frameCount == lastFrame) continue;
                    lastFrame = Time.frameCount;
                    if (EditorApplication.timeSinceStartup >= start) samples.Add(Time.unscaledDeltaTime * 1000f);
                }
            }
            finally { QualitySettings.vSyncCount = vSync; Application.targetFrameRate = target; }
            if (samples.Count == 0) throw new InvalidOperationException("No frames rendered while sampling; is the Game View open?");
            samples.Sort();
            float Percentile(float p) => samples[Mathf.Clamp(Mathf.CeilToInt(p * samples.Count) - 1, 0, samples.Count - 1)];
            float mean = samples.Average();
            return new
            {
                success = true, frames = samples.Count, seconds, uncapped, cap = new { vSyncCount = vSync, targetFrameRate = target },
                meanMs = Math.Round(mean, 2), medianMs = Math.Round(Percentile(0.5f), 2), p95Ms = Math.Round(Percentile(0.95f), 2),
                worstMs = Math.Round(samples[samples.Count - 1], 2), meanFps = Math.Round(1000f / mean, 1),
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()], screen = new[] { Screen.width, Screen.height },
                note = "Editor Play Mode timing: compare stages with each other; a packaged player measures lower.",
            };
        }

        [CliCommand("sloparena.stage.thumbnail",
            "Install a 1280x720 stage-select thumbnail (from sloparena.stage.views.cameras \"thumbnail\") as Resources/UI/Stages/<stage>.png with the shared import settings, in Edit Mode.",
            MainThreadRequired = true, Tags = new[] { "stage", "authoring/stage" })]
        public static object Thumbnail(
            [CliArg("stage", "Stage key.", Required = true)] string stage,
            [CliArg("from", "Repository-relative PNG captured by the stage's \"thumbnail\" review camera.", Required = true)] string from)
        {
            if (!Regex.IsMatch(stage ?? "", "^[a-z][a-z0-9_]{0,99}$")) throw new ArgumentException("Invalid stage key.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install the thumbnail in stopped Edit Mode, after the stage-views session.");
            string repository = Directory.GetParent(Path.GetFullPath(UnityCharacterAssetCooker.ProjectRoot()))?.Parent?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the repository root.");
            string source = Path.GetFullPath(Path.Combine(repository, from ?? ""));
            if (!File.Exists(source)) throw new InvalidOperationException("Missing " + from);
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(source))) throw new InvalidOperationException(from + " is not a PNG.");
                if (image.width != ThumbnailWidth || image.height != ThumbnailHeight)
                    throw new InvalidOperationException($"Thumbnails are {ThumbnailWidth}x{ThumbnailHeight}; {from} is {image.width}x{image.height}. Capture the stage's \"thumbnail\" camera.");
                // Opaque RGB: the card shows the picture, never a transparent backdrop.
                var opaque = new Texture2D(ThumbnailWidth, ThumbnailHeight, TextureFormat.RGB24, false);
                opaque.SetPixels(image.GetPixels());
                opaque.Apply();
                string path = "Assets/Resources/UI/Stages/" + stage + ".png";
                File.WriteAllBytes(path, opaque.EncodeToPNG());
                Object.DestroyImmediate(opaque);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = false;
                // Keep the exact 16:9 picture: no rescale to a power of two.
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.isReadable = false;
                importer.SaveAndReimport();
                return new { success = true, stage, thumbnail = path, width = ThumbnailWidth, height = ThumbnailHeight, source = from };
            }
            finally { Object.DestroyImmediate(image); }
        }

        private sealed class Pose
        {
            public string name;
            public Vector3 position, target;
            public float fov;
            public int width, height;
        }

        private static List<Pose> ReadCameras(string stage)
        {
            if (!Regex.IsMatch(stage ?? "", "^[a-z][a-z0-9_]{0,99}$")) throw new ArgumentException("Invalid stage key.");
            string root = "Assets/Stages/" + stage + "/Presentation";
            var found = Directory.Exists(root) ? Directory.GetFiles(root, "*.presentation.json", SearchOption.AllDirectories) : Array.Empty<string>();
            if (found.Length != 1) throw new InvalidOperationException("Expected one presentation in " + root + "; apply the stage with sloparena.stage.art-apply first.");
            var cameras = JObject.Parse(File.ReadAllText(found[0]))["cameras"] as JArray;
            if (cameras == null || cameras.Count == 0)
                throw new InvalidOperationException("The " + stage + " presentation has no review cameras; add \"cameras\" to tools/blender/" + stage + "/presentation.py and re-apply.");
            Vector3 V(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);
            var poses = new List<Pose>();
            foreach (var c in cameras)
            {
                string name = (string)c["name"];
                if (!Regex.IsMatch(name ?? "", "^[a-z0-9-]{1,40}$")) throw new InvalidOperationException("Camera names are lowercase words joined by '-': " + name);
                bool thumbnail = name == "thumbnail";
                poses.Add(new Pose
                {
                    name = name, position = V(c["position"]), target = V(c["target"]), fov = (float?)c["fov"] ?? 40f,
                    width = thumbnail ? ThumbnailWidth : (int?)c["width"] ?? 1920,
                    height = thumbnail ? ThumbnailHeight : (int?)c["height"] ?? 1080,
                });
            }
            return poses;
        }
    }
}
#endif
