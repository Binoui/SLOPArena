#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class NyxAnimationIntegration
    {
        private const string Root = "Assets/CharacterPackages/nyx";

        [CliCommand("sloparena.animation.nyx.integrate", "Bind Nyx's 24 owner-approved package clips and remap Bat Scatter and swaps onto unchanged gameplay clocks.",
            MainThreadRequired = true, Tags = new[] { "animation", "nyx" })]
        public static object Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Integrate in compiled, stopped Edit Mode.");
            var plan = JObject.Parse(File.ReadAllText(Root + "/Animations/NyxApprovedClips.json"));
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(Root + "/CharacterAssetCatalog.asset")
                ?? throw new InvalidOperationException("Nyx catalog is missing.");
            var rows = catalog.Bindings.ToDictionary(x => x.SemanticId, StringComparer.Ordinal);
            var report = new List<object>();
            foreach (JObject row in plan["clips"])
            {
                string sourcePath = (string)row["source"], path = (string)row["asset"], id = (string)row["animationId"];
                var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath)
                    ?? throw new InvalidOperationException("Missing approved package clip " + sourcePath);
                var remap = (JArray)row["remap"];
                var clip = remap.Count == 0 ? source : Remap(source, path, remap, (int)row["durationTicks"]);
                int expected = remap.Count == 0 ? (int)row["authoredTicks"] : (int)row["durationTicks"];
                int frames = Mathf.CeilToInt(clip.length * 60);
                if (frames != expected)
                    throw new InvalidOperationException($"{id}: {frames} cooked samples, expected {expected}; length {clip.length:R}.");
                rows[id] = new CharacterAssetCatalog.AnimationBinding
                {
                    SemanticId = id, PoseTrackId = (string)row["poseTrackId"], Clip = clip,
                    Extrapolation = ExtrapolationMode.None,
                };
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long fileId);
                report.Add(new { move = (string)row["move"], id, path, guid, fileId, frames,
                    speed = (float)row["speed"], poseTrack = rows[id].PoseTrackId });
            }
            var document = JObject.Parse(File.ReadAllText(Root + "/character.json"));
            var required = new HashSet<string>(document.Descendants().OfType<JValue>()
                .Where(v => v.Type == JTokenType.String).Select(v => (string)v)
                .Where(v => v.StartsWith("anim.nyx.", StringComparison.Ordinal)), StringComparer.Ordinal);
            var missing = required.Where(id => !rows.ContainsKey(id)).ToArray();
            if (missing.Length != 0) throw new InvalidOperationException("Unbound IDs: " + string.Join(",", missing));
            catalog.Bindings = rows.Values.Where(x => required.Contains(x.SemanticId))
                .OrderBy(x => x.SemanticId, StringComparer.Ordinal).ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            return new { success = true, clips = report, bindings = catalog.Bindings.Length, gameplayChanged = false };
        }

        private static AnimationClip Remap(AnimationClip source, string path, JArray mapping, int ticks)
        {
            var generated = Object.Instantiate(source);
            generated.name = Path.GetFileNameWithoutExtension(path);
            generated.ClearCurves();
            generated.frameRate = 60;
            float SourceTick(float tick)
            {
                for (int i = 1; i < mapping.Count; i++)
                {
                    float end = (float)mapping[i]["tick"];
                    if (tick > end) continue;
                    float start = (float)mapping[i - 1]["tick"];
                    return Mathf.Lerp((float)mapping[i - 1]["sourceTick"], (float)mapping[i]["sourceTick"],
                        (tick - start) / (end - start));
                }
                return (float)mapping.Last["sourceTick"];
            }
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var original = AnimationUtility.GetEditorCurve(source, binding);
                var keys = new Keyframe[ticks + 1];
                for (int tick = 0; tick <= ticks; tick++)
                    keys[tick] = new Keyframe(tick / 60f, original.Evaluate(SourceTick(tick) / 60f));
                var curve = new AnimationCurve(keys) { preWrapMode = original.preWrapMode, postWrapMode = original.postWrapMode };
                for (int i = 0; i < keys.Length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(generated, binding, curve);
            }
            if (AnimationUtility.GetObjectReferenceCurveBindings(source).Length != 0)
                throw new InvalidOperationException("Unexpected object curves in approved take " + source.name);
            AnimationUtility.SetAnimationEvents(generated, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.startTime = 0;
            settings.stopTime = ticks / 60f;
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(generated, settings);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) AssetDatabase.CreateAsset(generated, path);
            else
            {
                EditorUtility.CopySerialized(generated, existing);
                Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(existing);
                generated = existing;
            }
            AssetDatabase.SaveAssetIfDirty(generated);
            return generated;
        }
    }
}
#endif
