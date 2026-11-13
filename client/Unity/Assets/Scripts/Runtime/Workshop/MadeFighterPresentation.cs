#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Workshop
{
    /// <summary>
    /// How an admitted portable fighter is drawn: its GLB model as a hidden template with one GLB motion state per bound
    /// animation, a runtime animation catalog, and its UI art. Built once per revision from the admission's verified
    /// bytes and motion, then found by exact revision wherever a built-in fighter's generated assets would be. Every
    /// user (the menu that offers it, each Match that draws it) holds a <see cref="Lease"/>; the last one to let go
    /// destroys the revision's assets.
    /// </summary>
    public sealed class MadeFighterPresentation
    {
        private static readonly Dictionary<MatchContentIdentity, MadeFighterPresentation> Prepared = new();
        private static readonly Dictionary<MatchContentIdentity, Task<MadeFighterPresentation>> Pending = new();
        private static GameObject? holder;
        private int _users;
        private bool _destroyed;

        public MatchContentIdentity Identity { get; }
        public GameObject Template { get; }
        public CharacterAnimationCatalog Catalog { get; }
        public FighterUiArt Art { get; }
        /// <summary>The portrait rendered from the model, or null when the item supplies portraits or rendering failed.</summary>
        public Texture2D? RenderedPortrait { get; }
        public PortableFighterMotion Motion { get; }
        private readonly ImportedSwapModel _model;
        private readonly List<UnityEngine.Object> _owned;

        private MadeFighterPresentation(MatchContentIdentity identity, ImportedSwapModel model, CharacterAnimationCatalog catalog,
            FighterUiArt art, Texture2D? rendered, PortableFighterMotion motion, List<UnityEngine.Object> owned)
        {
            Identity = identity; _model = model; Template = model.Root!; Catalog = catalog; Art = art; RenderedPortrait = rendered;
            Motion = motion; _owned = owned;
        }

        /// <summary>Live leases; zero once the revision's assets are destroyed.</summary>
        public int Users => _users;
        private bool Alive => !_destroyed && Template != null && Catalog != null;

        /// <summary>The revision's presentation while its Unity objects exist; a stale entry is dropped, never returned.</summary>
        public static bool TryGet(MatchContentIdentity identity, out MadeFighterPresentation presentation)
        {
            if (Prepared.TryGetValue(identity, out presentation!) && presentation.Alive) return true;
            if (presentation != null) presentation.Teardown();
            presentation = null!;
            return false;
        }

        /// <summary>One more user for this revision; dispose the lease when that user no longer draws or offers it.</summary>
        public Lease Acquire()
        {
            if (!Alive) throw new ObjectDisposedException(nameof(MadeFighterPresentation), $"{Identity.PackageId} was released.");
            _users++;
            return new Lease(this);
        }

        public sealed class Lease : IDisposable
        {
            private MadeFighterPresentation? _presentation;
            internal Lease(MadeFighterPresentation presentation) { _presentation = presentation; }
            public MadeFighterPresentation Presentation => _presentation ?? throw new ObjectDisposedException(nameof(Lease));
            public void Dispose()
            {
                var presentation = _presentation;
                _presentation = null;
                presentation?.Leave();
            }
        }

        private void Leave()
        {
            if (--_users <= 0) Teardown();
        }

        private void Teardown()
        {
            if (_destroyed) return;
            _destroyed = true;
            _users = 0;
            if (Prepared.TryGetValue(Identity, out var current) && current == this) Prepared.Remove(Identity);
            foreach (var asset in _owned) DestroyAsset(asset);
            _model.Dispose();
        }

        private static void DestroyAsset(UnityEngine.Object? asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
            else UnityEngine.Object.DestroyImmediate(asset);
        }

        // An Editor Play without a domain reload keeps statics; DontSave assets from the last session would leak and its
        // destroyed templates would be served. Each session starts empty, as the fighter art does.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaySession()
        {
            ReleaseAll();
            Application.quitting -= ReleaseAll;

        }

        private static void ReleaseAll()
        {
            foreach (var presentation in Prepared.Values.ToArray()) presentation.Teardown();
            Prepared.Clear();
            Pending.Clear();
            holder = null;
        }

        /// <summary>
        /// Import the item's model, bind its clips and render a portrait if it has none, once per revision; the caller
        /// receives a lease for itself.
        /// </summary>
        public static async Task<Lease> PrepareAsync(MatchContentEntry entry, FighterItemSnapshot item, CancellationToken cancel)
        {
            if (TryGet(entry.Identity, out var existing)) return existing.Acquire();
            if (!Pending.TryGetValue(entry.Identity, out var build)) Pending[entry.Identity] = build = BuildAsync(entry, item, cancel);
            try { return (await build).Acquire(); }
            finally { if (Pending.TryGetValue(entry.Identity, out var current) && current == build) Pending.Remove(entry.Identity); }
        }

        private static async Task<MadeFighterPresentation> BuildAsync(MatchContentEntry entry, FighterItemSnapshot item, CancellationToken cancel)
        {
            var motion = item.Motion;
            var import = await ModelSwapImporter.ImportFighterAsync(item.ReadPayload(item.ModelFile), motion.Rig.Root, Color.white, cancel);
            var model = import.Model ?? throw new InvalidOperationException("Fighter model refused: " + import.Refusal);
            var owned = new List<UnityEngine.Object>();
            try
            {
                cancel.ThrowIfCancellationRequested();
                if (holder == null)
                {
                    holder = new GameObject("Made fighter templates");
                    holder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(holder);
                }
                var root = model.Root!;
                root.name = entry.Identity.PackageId;

                var written = motion.Animations.SelectMany(a => a.Clip.AnimatedNodes).Distinct().OrderBy(n => n).ToArray();
                var paths = written.Select(n => (byte)motion.Animations.Aggregate(0, (mask, a) => mask | a.Clip.Paths(n))).ToArray();
                var sampled = new Dictionary<GlbClip, GlbMotionClip>();
                var keys = new List<(AnimationClip Key, GlbMotionClip Motion)>();
                var entries = new List<CharacterAnimationCatalog.AnimationEntry>();
                foreach (var animation in motion.Animations)
                {
                    if (!sampled.TryGetValue(animation.Clip, out var clip)) sampled[animation.Clip] = clip = new GlbMotionClip(motion.Rig, animation.Clip, written);
                    var key = GlbMotionBinder.Key(animation.SemanticId, clip.DurationSeconds, clip.Loop);
                    owned.Add(key);
                    keys.Add((key, clip));
                    entries.Add(new CharacterAnimationCatalog.AnimationEntry
                    {
                        SemanticId = animation.SemanticId, PoseTrackId = animation.PoseTrackId, Clip = key,
                        FrameCount = clip.FrameCount, SampleRate = 60, Extrapolation = ExtrapolationMode.None,
                    });
                }
                root.AddComponent<GlbMotionBinder>().Configure(written.Select(n => model.Nodes[n]!).ToArray(), paths, keys);

                var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
                owned.Add(catalog);
                catalog.name = entry.Identity.PackageId + " (made fighter)";
                catalog.hideFlags = HideFlags.DontSave;
                catalog.PackageId = entry.Identity.PackageId;
                catalog.SourceHash = entry.Identity.SourceHash;
                catalog.CatalogSchemaVersion = 3;
                catalog.BindingSchemaVersion = 3;
                catalog.SampleRate = 60;
                catalog.Rig = root;
                catalog.Animations = entries.ToArray();
                catalog.SetPresentationProfile(new CharacterPresentationProfile());

                Texture2D? rendered = null;
                var portraits = Images(item, item.Bindings.GetProperty("portraits").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null).ToArray(), owned);
                if (portraits.Any(x => x == null))
                {
                    try
                    {
                        rendered = MadeFighterPortrait.Render(root, model.Nodes, motion, entry.Definition.IdleAnim);
                        owned.Add(rendered);
                    }
                    catch (Exception error) { Debug.LogWarning($"[MadeFighter] Portrait render failed for {entry.Identity.PackageId}; using neutral art. {error.Message}"); }
                }
                var icons = item.Bindings.GetProperty("icons").EnumerateArray().Select(form =>
                {
                    var map = new Dictionary<string, UiImage>(StringComparer.Ordinal);
                    foreach (var icon in form.EnumerateObject())
                        if (icon.Value.ValueKind == JsonValueKind.String && Images(item, new[] { icon.Value.GetString() }, owned)[0] is { } texture)
                            map[icon.Name] = new UiImage(texture, null, Color.white);
                    return map;
                }).ToArray();
                root.transform.SetParent(holder.transform, false);
                model.Show();
                var art = new FighterUiArt(entry.Definition, portraits.Select(x => x ?? rendered).ToArray(), icons, FighterCardText.Read(item.Card));
                var presentation = new MadeFighterPresentation(entry.Identity, model, catalog, art, rendered, motion, owned);
                cancel.ThrowIfCancellationRequested();
                Prepared[entry.Identity] = presentation;
                return presentation;
            }
            catch
            {
                foreach (var asset in owned) DestroyAsset(asset);
                model.Dispose();
                throw;
            }
        }

        private static Texture2D?[] Images(FighterItemSnapshot item, string?[] files, List<UnityEngine.Object> owned)
            => files.Select(file =>
            {
                if (file == null) return null;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = file, hideFlags = HideFlags.DontSave };
                if (!texture.LoadImage(item.ReadPayload(file), true))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                owned.Add(texture);
                return texture;
            }).ToArray();
    }

    /// <summary>
    /// A made fighter's portrait when its creator gives none: idle frame 0 posed by the Shared sampler, seen from the
    /// front by an orthographic camera framed on the head and upper chest, like the built-in bust portraits. The freshly
    /// imported model is posed, drawn alone on a spare layer and put back exactly as it was.
    /// </summary>
    public static class MadeFighterPortrait
    {
        public const int Size = 512;
        private const int Layer = 30;

        public static Texture2D Render(GameObject root, Transform?[] nodes, PortableFighterMotion motion, string idleAnimation)
        {
            var rig = motion.Rig;
            var idle = motion.Animations.FirstOrDefault(a => a.SemanticId == idleAnimation)?.Clip;
            var poses = new GlbNodePose[rig.NodeCount];
            if (idle != null) rig.SampleLocal(idle, 0, poses); else rig.SampleRest(poses);
            var saved = new List<(Transform Node, Vector3 Position, Quaternion Rotation, Vector3 Scale)>();
            var layers = root.GetComponentsInChildren<Transform>(true).Select(t => (t, t.gameObject.layer)).ToArray();
            var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => (r, r.updateWhenOffscreen)).ToArray();
            var renderers = root.GetComponentsInChildren<Renderer>(true).Select(r => (r, r.forceRenderingOff)).ToArray();
            Vector3 place = root.transform.position;
            var cameraObject = new GameObject("Made fighter portrait camera");
            var lightObject = new GameObject("Made fighter portrait light");
            var target = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                root.transform.position = new Vector3(0f, -10000f, 0f);
                foreach (int n in rig.SceneOrder)
                    if (n < nodes.Length && nodes[n] is { } node && !rig.IsMatrixNode(n))
                    {
                        saved.Add((node, node.localPosition, node.localRotation, node.localScale));
                        var p = poses[n];
                        node.localPosition = new Vector3(p.TX, p.TY, p.TZ);
                        node.localRotation = new Quaternion(p.QX, p.QY, p.QZ, p.QW).normalized;
                        node.localScale = new Vector3(p.SX, p.SY, p.SZ);
                    }
                foreach (var (t, _) in layers) t.gameObject.layer = Layer;
                foreach (var (r, _) in skins) r.updateWhenOffscreen = true;
                foreach (var (r, _) in renderers) r.forceRenderingOff = false;

                Transform? Bone(string human) => rig.BoneMap.NodeFor(human) is { } name && rig.FindNode(name) is var index and >= 0 ? nodes[index] : null;
                Vector3 head = (Bone("Head") ?? root.transform).position;
                Vector3 chest = (Bone("UpperChest") ?? Bone("Chest") ?? Bone("Spine") ?? root.transform).position;
                float span = Mathf.Max(0.05f, Vector3.Distance(head, chest));
                Vector3 centre = Vector3.Lerp(chest, head, 0.6f);

                var camera = cameraObject.AddComponent<UnityEngine.Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << Layer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f, .14f, .19f, 1f);
                camera.orthographic = true;
                camera.orthographicSize = span * 1.7f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 50f;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                cameraObject.transform.SetPositionAndRotation(centre + root.transform.forward * 10f, Quaternion.LookRotation(-root.transform.forward, Vector3.up));
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.cullingMask = 1 << Layer;
                light.intensity = 1.1f;
                lightObject.transform.rotation = Quaternion.LookRotation(new Vector3(-.4f, -.5f, -1f));
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = root.name + " portrait", hideFlags = HideFlags.DontSave };
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                image.Apply(false, false);
                return image;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(cameraObject);
                UnityEngine.Object.Destroy(lightObject);
                foreach (var (node, position, rotation, scale) in saved) { node.localPosition = position; node.localRotation = rotation; node.localScale = scale; }
                foreach (var (t, layer) in layers) t.gameObject.layer = layer;
                foreach (var (r, offscreen) in skins) r.updateWhenOffscreen = offscreen;
                foreach (var (r, off) in renderers) r.forceRenderingOff = off;
                root.transform.position = place;
            }
        }
    }
}
