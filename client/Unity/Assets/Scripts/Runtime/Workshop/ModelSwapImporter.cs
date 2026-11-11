#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animancer;
using GLTFast;
using GLTFast.Loading;
using GLTFast.Logging;
using GLTFast.Materials;
using SlopArena.Client.Entities;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Material = UnityEngine.Material;

namespace SlopArena.Client.Workshop
{
    public enum ModelSwapRefusalReason
    {
        BaseFighterNotReady,
        FileUnreadable,
        FileLimits,
        GltfLoadFailed,
        NoSkinnedMesh,
        BoneMapInvalid,
        RequiredBonesMissing,
        RestPoseUnreadable,
        AvatarInvalid,
        ImportFailed,
    }

    public sealed class ModelSwapRefusal
    {
        public ModelSwapRefusal(ModelSwapRefusalReason reason, string message)
        {
            Reason = reason;
            Message = message;
        }

        public ModelSwapRefusalReason Reason { get; }
        public string Message { get; }
        public override string ToString() => $"{Reason}: {Message}";
    }

    /// <summary>What the importer matches on the built-in fighter the model dresses.</summary>
    public readonly struct ModelSwapTarget
    {
        public ModelSwapTarget(float humanScale, Color rim, AnimatorCullingMode culling)
        {
            HumanScale = humanScale;
            Rim = rim;
            Culling = culling;
        }

        /// <summary>The built-in avatar's human scale (Mecanim's hips height); the size rule matches it.</summary>
        public float HumanScale { get; }
        public Color Rim { get; }
        public AnimatorCullingMode Culling { get; }
    }

    /// <summary>Where the import spent its time and texture memory, for the log and the Workshop checks.</summary>
    public sealed class ModelSwapImportStats
    {
        public double LoadMs, DecodeMs, TexturesMs, WorstTextureMs, InstantiateMs, UprightMs, AvatarMs, InkMs, TotalMs;
        public long TextureBytesLoaded, TextureBytesKept;
        public int Textures, Vertices, Skins, HumanBones;
        public float Fit, TurnDegrees;
        public string BoneMapSource = "";

        public override string ToString() =>
            $"total {TotalMs:0} ms (load {LoadMs:0} incl. worker decode {DecodeMs:0}, textures {TexturesMs:0}, worst texture frame {WorstTextureMs:0}, instantiate {InstantiateMs:0}, " +
            $"upright {UprightMs:0}, avatar {AvatarMs:0}, ink {InkMs:0}); {Skins} skins {Vertices} vertices; {HumanBones} human bones from " +
            $"{BoneMapSource}; turned {TurnDegrees:0} deg, fit x{Fit:0.000}; {Textures} textures {TextureBytesLoaded / 1048576.0:0.0} MB -> " +
            $"{TextureBytesKept / 1048576.0:0.0} MB";
    }

    /// <summary>
    /// A model ready to stand in for a built-in fighter's model: a humanoid avatar built from its own rest pose, the
    /// match's fighter material, and textures inside the budget. It owns everything the import created; dispose it
    /// when the model is dropped. The renderer that wears <see cref="Root"/> destroys that object when it takes it off.
    /// </summary>
    public sealed class ImportedSwapModel : IDisposable
    {
        internal GltfImport? Gltf;
        internal Avatar? Avatar;
        internal readonly List<Texture2D> Textures = new();
        internal readonly List<Material> Materials = new();
        internal readonly List<Mesh> InkedSources = new();

        public GameObject? Root { get; internal set; }
        public Animator? Animator { get; internal set; }
        public AnimancerComponent? Animancer { get; internal set; }
        public ModelSwapImportStats Stats { get; } = new();

        /// <summary>Draw the model; it stays hidden while it loads away from the fighter.</summary>
        public void Show()
        {
            if (Root == null) return;
            foreach (var renderer in Root.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = false;
        }

        public void Dispose()
        {
            foreach (var mesh in InkedSources) FighterInk.Release(mesh);
            InkedSources.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
            if (Avatar != null) UnityEngine.Object.Destroy(Avatar);
            Avatar = null;
            foreach (var material in Materials) if (material != null) UnityEngine.Object.Destroy(material);
            Materials.Clear();
            foreach (var texture in Textures) if (texture != null) UnityEngine.Object.Destroy(texture);
            Textures.Clear();
            Gltf?.Dispose();
            Gltf = null;
        }
    }

    public sealed class ModelSwapImportResult
    {
        private ModelSwapImportResult(ImportedSwapModel? model, ModelSwapRefusal? refusal)
        {
            Model = model;
            Refusal = refusal;
        }

        public ImportedSwapModel? Model { get; }
        public ModelSwapRefusal? Refusal { get; }

        internal static ModelSwapImportResult Accept(ImportedSwapModel model) => new(model, null);
        internal static ModelSwapImportResult Refuse(ModelSwapRefusalReason reason, string message) => new(null, new ModelSwapRefusal(reason, message));
    }

    /// <summary>
    /// Turns a creator's GLB into a model for a built-in fighter's kit (Workshop roadmap step 1.2, ADR-0065 path B).
    /// glTFast loads it over several frames; four rules read only the file: the bone map, upright and grounded,
    /// size, and palms. Nothing here touches the simulation.
    /// </summary>
    public static class ModelSwapImporter
    {
        /// <summary>The texture budget: the largest side a swap texture keeps. 0 keeps glTFast's textures as loaded.</summary>
        public static int TextureMaxSide = 1024;

        public static async Task<ModelSwapImportResult> ImportAsync(
            byte[] glb, string? boneMapJson, ModelSwapTarget target, CancellationToken cancel)
        {
            if (FilePrecheck(glb) is { } limits)
                return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.FileLimits, limits);
            var model = new ImportedSwapModel();
            try
            {
                var result = await Build(model, glb, boneMapJson, target, cancel);
                if (result.Model == null) model.Dispose();
                return result;
            }
            catch (OperationCanceledException)
            {
                model.Dispose();
                throw;
            }
            catch (Exception exception)
            {
                model.Dispose();
                Debug.LogException(exception);
                return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.ImportFailed, exception.Message);
            }
        }

        // The file-level limits (size, triangles, bones, texture size) land as WorkshopGlbPrecheck.Check(glb,
        // WorkshopGlbLimits.Default) in Shared; it must run here, before glTFast parses a byte.
        private static string? FilePrecheck(byte[] glb) => glb.Length == 0 ? "The file is empty." : null;

        private static async Task<ModelSwapImportResult> Build(
            ImportedSwapModel model, byte[] glb, string? boneMapJson, ModelSwapTarget target, CancellationToken cancel)
        {
            var stats = model.Stats;
            var total = Stopwatch.StartNew();
            var shader = MatchVisualStyle.FighterShader;
            var materials = new SwapMaterialGenerator(shader, target.Rim, model.Materials);
            var logger = new CollectingLogger();
            var gltf = model.Gltf = new GltfImport(NoDownloads.Instance, null, materials, logger);
            if (TextureMaxSide > 0) gltf.AddImportAddonInstance(new ModelSwapTextures.PngLoader(TextureMaxSide, stats));
            var settings = new ImportSettings
            {
                AnimationMethod = AnimationMethod.None,
                NodeNameMethod = NameImportMethod.OriginalUnique,
                GenerateMipMaps = false,
            };
            var watch = Stopwatch.StartNew();
            if (!await gltf.Load(glb, null, settings, cancel))
                return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.GltfLoadFailed, FirstError(logger, "glTFast could not read the file."));
            stats.LoadMs = watch.Elapsed.TotalMilliseconds;

            watch.Restart();
            await KeepTexturesInBudget(model, gltf, cancel);
            stats.TexturesMs = watch.Elapsed.TotalMilliseconds;

            var container = new GameObject("Model swap (loading)");
            model.Root = container;
            var fit = new GameObject("Fit").transform;
            fit.SetParent(container.transform, false);
            var instantiator = new GameObjectInstantiator(gltf, fit, logger, new InstantiationSettings
            {
                Mask = ComponentType.Mesh,
                SceneObjectCreation = SceneObjectCreation.Always,
            });
            watch.Restart();
            if (!await gltf.InstantiateMainSceneAsync(instantiator, cancel) || instantiator.SceneTransform == null)
                return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.GltfLoadFailed, FirstError(logger, "The file has no scene to show."));
            stats.InstantiateMs = watch.Elapsed.TotalMilliseconds;
            Transform root = instantiator.SceneTransform;
            foreach (var renderer in container.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = true;

            var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => x.sharedMesh != null).ToArray();
            if (skins.Length == 0)
                return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.NoSkinnedMesh,
                    "The model has no skinned mesh, so it cannot follow the fighter's moves.");
            stats.Skins = skins.Length;
            stats.Vertices = skins.Sum(x => x.sharedMesh.vertexCount);

            var nodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var node in root.GetComponentsInChildren<Transform>(true)) nodes.TryAdd(node.name, node);
            HumanoidBoneMap map;
            if (boneMapJson != null)
            {
                if (!HumanoidBoneMap.TryParseMapFile(boneMapJson, out var parsed, out string error))
                    return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.BoneMapInvalid, error);
                map = parsed!;
                stats.BoneMapSource = "humanoid.json";
            }
            else
            {
                var joints = skins.SelectMany(x => x.bones).Where(x => x != null).Select(x => x.name);
                map = HumanoidBoneMap.FromMixamoNames(joints.Concat(nodes.Keys));
                stats.BoneMapSource = "Mixamo names";
            }
            if (map.MissingRequired.Count > 0)
                return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.RequiredBonesMissing,
                    $"The rig has no {string.Join(", ", map.MissingRequired)}. Name its bones as Mixamo does, or add a humanoid.json beside the GLB.");
            foreach (var pair in map.NodeByHuman)
                if (!nodes.ContainsKey(pair.Value))
                    return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.BoneMapInvalid,
                        $"humanoid.json gives {pair.Key} to node \"{pair.Value}\", which the GLB does not have.");
            Transform Bone(string human) => map.NodeFor(human) is { } name && nodes.TryGetValue(name, out var t) ? t : null!;
            stats.HumanBones = map.NodeByHuman.Count;
            await NextFrame(cancel);

            watch.Restart();
            if (!StandUpright(root, Bone, skins, out float turned, out string upright))
                return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.RestPoseUnreadable, upright);
            stats.UprightMs = watch.Elapsed.TotalMilliseconds;
            stats.TurnDegrees = turned;
            await NextFrame(cancel);

            watch.Restart();
            var description = Describe(SkeletonOf(root, Bone("Hips")), map);
            RollPalms(description, Bone, container.transform.forward);
            var avatar = model.Avatar = AvatarBuilder.BuildHumanAvatar(root.gameObject, description);
            avatar.name = "Model swap avatar";
            if (!avatar.isValid || !avatar.isHuman)
                return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.AvatarInvalid,
                    "Unity could not build a humanoid avatar from the rest pose. The rig needs a T-pose rest with its bones in a humanoid chain.");
            var animator = root.gameObject.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = target.Culling;
            if (!animator.isHuman || !(animator.humanScale > 0f))
                return ModelSwapImportResult.Refuse(ModelSwapRefusalReason.AvatarInvalid, "The avatar has no human scale to match the fighter's size.");
            var animancer = root.gameObject.AddComponent<AnimancerComponent>();
            animancer.Animator = animator;
            stats.Fit = target.HumanScale / animator.humanScale;
            fit.localScale = Vector3.one * stats.Fit;
            stats.AvatarMs = watch.Elapsed.TotalMilliseconds;
            await NextFrame(cancel);

            watch.Restart();
            if (StageLook.Active.Fighters.Ink > 0f)
                foreach (var skin in skins)
                {
                    var source = skin.sharedMesh;
                    if (FighterInk.Apply(skin)) model.InkedSources.Add(source);
                }
            stats.InkMs = watch.Elapsed.TotalMilliseconds;

            model.Animator = animator;
            model.Animancer = animancer;
            stats.TotalMs = total.Elapsed.TotalMilliseconds;
            return ModelSwapImportResult.Accept(model);
        }

        private static string FirstError(CollectingLogger logger, string fallback)
        {
            var error = logger.Items?.FirstOrDefault(x => x.Type is LogType.Error or LogType.Exception);
            return error != null ? $"glTFast: {error}" : fallback;
        }

        // Unity's continuation runs on a later frame, so each step below costs its own frame instead of one long one.
        private static async Task NextFrame(CancellationToken cancel)
        {
            await Task.Yield();
            cancel.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Bring every texture the PNG loader did not already shrink inside the budget, point the materials at the
        /// copies and free the originals. One texture per frame.
        /// </summary>
        private static async Task KeepTexturesInBudget(ImportedSwapModel model, GltfImport gltf, CancellationToken cancel)
        {
            var stats = model.Stats;
            stats.Textures = gltf.TextureCount;
            var copies = new Dictionary<Texture, Texture2D>();
            for (int i = 0; i < gltf.TextureCount; i++)
            {
                var source = gltf.GetTexture(i);
                if (source == null) continue;
                bool shrunkOnLoad = TextureMaxSide > 0 && ModelSwapTextures.WithinBudget(source, TextureMaxSide);
                if (!shrunkOnLoad) stats.TextureBytesLoaded += ModelSwapTextures.EstimateBytes(source);
                if (TextureMaxSide <= 0 || shrunkOnLoad)
                {
                    stats.TextureBytesKept += ModelSwapTextures.EstimateBytes(source);
                    continue;
                }
                await NextFrame(cancel);
                var watch = Stopwatch.StartNew();
                var copy = ModelSwapTextures.Shrink(source, TextureMaxSide);
                stats.WorstTextureMs = Math.Max(stats.WorstTextureMs, watch.Elapsed.TotalMilliseconds);
                model.Textures.Add(copy);
                copies[source] = copy;
                stats.TextureBytesKept += ModelSwapTextures.EstimateBytes(copy);
            }
            if (copies.Count == 0) return;
            foreach (var material in model.Materials)
                if (material != null && material.HasProperty(BaseMapId) && material.GetTexture(BaseMapId) is { } map
                    && copies.TryGetValue(map, out var copy))
                    material.SetTexture(BaseMapId, copy);
            foreach (var source in copies.Keys) UnityEngine.Object.Destroy(source);
        }

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        /// <summary>
        /// Upright rule: up runs from the hips to the head and right from the left thigh to the right thigh, read from
        /// the rest skeleton. A node under the scene root turns that frame onto +Y up and +Z forward, puts the hips
        /// over the origin and the lowest skinned vertex on y = 0. Covers Y-up, Z-up and rests stored lying down.
        /// </summary>
        private static bool StandUpright(Transform root, Func<string, Transform> bone, SkinnedMeshRenderer[] skins,
            out float turnedDegrees, out string reason)
        {
            turnedDegrees = 0f;
            Transform hips = bone("Hips"), head = bone("Head"), leftThigh = bone("LeftUpperLeg"), rightThigh = bone("RightUpperLeg");
            Vector3 P(Transform t) => root.InverseTransformPoint(t.position);
            Vector3 up = P(head) - P(hips);
            Vector3 right = P(rightThigh) - P(leftThigh);
            if (up.sqrMagnitude < 1e-10f)
            {
                reason = "The rest pose puts the head on the hips, so the model cannot be stood upright.";
                return false;
            }
            up.Normalize();
            right -= Vector3.Dot(right, up) * up;
            if (right.sqrMagnitude < 1e-10f)
            {
                reason = "The rest pose lines the thighs up with the spine, so the model's facing cannot be read.";
                return false;
            }
            right.Normalize();
            var upright = new GameObject("Upright").transform;
            upright.SetParent(root, false);
            foreach (Transform child in root.Cast<Transform>().ToList())
                if (child != upright) child.SetParent(upright, true);
            var turn = Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(right, up), up));
            upright.localRotation = turn;
            turnedDegrees = Quaternion.Angle(Quaternion.identity, turn);
            Vector3 hipsOver = P(hips);
            float sole = LowestSkinnedPoint(root, skins);
            upright.localPosition = new Vector3(-hipsOver.x, -sole, -hipsOver.z);
            reason = "";
            return true;
        }

        /// <summary>
        /// The lowest skinned vertex in <paramref name="frame"/>'s space, skinned here from the file's weights and
        /// bind poses: before the first render, BakeMesh returns glTFast's unskinned mesh (Workshop step 0.1b).
        /// </summary>
        private static float LowestSkinnedPoint(Transform frame, SkinnedMeshRenderer[] skins)
        {
            float lowest = float.PositiveInfinity;
            var toFrame = frame.worldToLocalMatrix;
            foreach (var skin in skins)
            {
                var mesh = skin.sharedMesh;
                var bones = skin.bones;
                if (mesh.isReadable && bones.Length > 0)
                {
                    var vertices = mesh.vertices;
                    var weights = mesh.boneWeights;
                    var bind = mesh.bindposes;
                    if (weights.Length == vertices.Length)
                    {
                        var matrices = new Matrix4x4[bones.Length];
                        for (int i = 0; i < bones.Length; i++)
                            matrices[i] = bones[i] != null && i < bind.Length ? toFrame * bones[i].localToWorldMatrix * bind[i] : Matrix4x4.zero;
                        for (int v = 0; v < vertices.Length; v++)
                        {
                            var w = weights[v];
                            var x = vertices[v];
                            float y = w.weight0 * matrices[w.boneIndex0].MultiplyPoint3x4(x).y
                                + w.weight1 * matrices[w.boneIndex1].MultiplyPoint3x4(x).y
                                + w.weight2 * matrices[w.boneIndex2].MultiplyPoint3x4(x).y
                                + w.weight3 * matrices[w.boneIndex3].MultiplyPoint3x4(x).y;
                            if (y < lowest) lowest = y;
                        }
                        continue;
                    }
                }
                foreach (var b in bones)
                    if (b != null) lowest = Mathf.Min(lowest, frame.InverseTransformPoint(b.position).y);
            }
            return float.IsInfinity(lowest) ? 0f : lowest;
        }

        /// <summary>The scene root, the chain from it down to the hips, and every bone below the hips; mesh nodes stay out.</summary>
        private static Transform[] SkeletonOf(Transform root, Transform hips)
        {
            var chain = new List<Transform>();
            for (Transform t = hips.parent; t != null && t != root; t = t.parent) chain.Insert(0, t);
            var skeleton = new List<Transform> { root };
            skeleton.AddRange(chain);
            skeleton.AddRange(hips.GetComponentsInChildren<Transform>(true));
            return skeleton.ToArray();
        }

        private static SkeletonBone Rest(Transform t) => new()
        {
            name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
        };

        /// <summary>The model's own rest pose is the avatar's reference pose (path B needs a T-pose rest).</summary>
        private static HumanDescription Describe(Transform[] skeleton, HumanoidBoneMap map)
        {
            var human = new List<HumanBone>();
            foreach (var pair in map.NodeByHuman)
            {
                if (!Enum.TryParse(pair.Key, out HumanBodyBones bone) || bone == HumanBodyBones.LastBone) continue;
                var entry = new HumanBone { humanName = HumanTrait.BoneName[(int)bone], boneName = pair.Value };
                entry.limit.useDefaultValues = true;
                human.Add(entry);
            }
            return new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.Select(Rest).ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
        }

        /// <summary>
        /// Palm rule: Humanoid reads the reference pose as each muscle's zero and expects palms down, thumbs forward.
        /// Each thumb's direction around its forearm is measured in the rest, and the reference pose (not the mesh) is
        /// rolled by that angle, half on the forearm and half on the hand, so the fighter's clips turn the palms down.
        /// </summary>
        private static void RollPalms(HumanDescription description, Func<string, Transform> bone, Vector3 forward)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < description.skeleton.Length; i++) index.TryAdd(description.skeleton[i].name, i);
            foreach (string side in new[] { "Left", "Right" })
            {
                Transform lower = bone(side + "LowerArm"), hand = bone(side + "Hand"), thumb = bone(side + "ThumbProximal");
                if (lower == null || hand == null || thumb == null) continue;
                Vector3 axis = (hand.position - lower.position).normalized;
                Vector3 thumbDirection = Vector3.ProjectOnPlane(thumb.position - hand.position, axis);
                float angle = Vector3.SignedAngle(thumbDirection, Vector3.ProjectOnPlane(forward, axis), axis);
                Quaternion lowerRotation = lower.rotation, handRotation = hand.rotation;
                Quaternion half = Quaternion.AngleAxis(angle * 0.5f, axis);
                lower.rotation = half * lowerRotation;
                hand.rotation = half * hand.rotation;
                if (index.TryGetValue(lower.name, out int l)) description.skeleton[l] = Rest(lower);
                if (index.TryGetValue(hand.name, out int h)) description.skeleton[h] = Rest(hand);
                lower.rotation = lowerRotation;
                hand.rotation = handRotation;
            }
        }

        /// <summary>
        /// A swap file must hold all its data: an external buffer or image URI would let a downloaded item fetch from
        /// any server, or read any file, as the player. glTFast still decodes data URIs and the GLB's own chunk.
        /// </summary>
        private sealed class NoDownloads : IDownloadProvider
        {
            public static readonly NoDownloads Instance = new();

            public Task<IDownload> Request(Uri url) => Task.FromResult<IDownload>(Refused.Instance);

            public Task<ITextureDownload> RequestTexture(Uri url, bool nonReadable) => Task.FromResult<ITextureDownload>(Refused.Instance);

            private sealed class Refused : ITextureDownload
            {
                public static readonly Refused Instance = new();
                public bool Success => false;
                public string Error => "A model swap GLB must embed its buffers and images.";
                public byte[] Data => Array.Empty<byte>();
                public string Text => "";
                public bool? IsBinary => null;
                public Texture2D Texture => null!;
                public void Dispose() { }
            }
        }

        /// <summary>
        /// Builds the match's fighter material straight from the glTF material: base map, base colour and culling, as
        /// <see cref="MatchVisualStyle"/> copies them from a built-in fighter. Alpha and specular settings are ignored;
        /// a fighter draws opaque.
        /// </summary>
        private sealed class SwapMaterialGenerator : IMaterialGenerator
        {
            private readonly Shader? _shader;
            private readonly Color _rim;
            private readonly List<Material> _created;

            public SwapMaterialGenerator(Shader? shader, Color rim, List<Material> created)
            {
                _shader = shader;
                _rim = rim;
                _created = created;
            }

            public Material GetDefaultMaterial(bool pointsSupport = false) => Make("glTF default", null, false, Color.white, 2f);

            public Material GenerateMaterial(GLTFast.Schema.MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false)
            {
                var pbr = gltfMaterial.PbrMetallicRoughness;
                var info = pbr?.BaseColorTexture;
                Texture2D? texture = null;
                bool flipY = false;
                if (info != null && info.index >= 0)
                {
                    texture = gltf.GetTexture(info.index);
                    flipY = gltf.IsTextureYFlipped(info.index);
                }
                return Make(gltfMaterial.name, texture, flipY, pbr != null ? pbr.BaseColor.gamma : Color.white, gltfMaterial.doubleSided ? 0f : 2f);
            }

            public void SetLogger(ICodeLogger logger) { }

            private Material Make(string name, Texture? baseMap, bool flipY, Color baseColor, float cull)
            {
                var material = _shader != null
                    ? MatchVisualStyle.CreateFighterMaterial(_shader, name + " (model swap)", _rim)
                    : new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name + " (model swap)" };
                if (baseMap != null) material.SetTexture(BaseMapId, baseMap);
                if (flipY)
                {
                    material.SetTextureScale(BaseMapId, new Vector2(1f, -1f));
                    material.SetTextureOffset(BaseMapId, new Vector2(0f, 1f));
                }
                material.SetColor("_BaseColor", baseColor);
                material.SetFloat("_Cull", cull);
                _created.Add(material);
                return material;
            }
        }
    }
}
