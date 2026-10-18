#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SlopArena.Client.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Applies the generated Slopball Night Court (see tools/blender/slopball) to the court's
    /// presentation prefab: a floodlit chain-link cage inside a graffiti warehouse. Presentation
    /// only: the source scene and baked arena are verified against the generator stamp and never
    /// written. The Ball Sender and its dressing stay; the club finish is replaced.
    /// </summary>
    public static class SlopArenaSlopballNightCourtCommands
    {
        private const string PrefabPath = "Assets/Resources/Stages/slopball.prefab";
        private const string SourcePath = "Assets/Stages/slopball/slopball.unity";
        private const string ArtPath = "Assets/Stages/slopball/Presentation/NightCourt";
        private const string ModelPath = ArtPath + "/SlopballNightCourt.fbx";
        private const string StampPath = ArtPath + "/SlopballNightCourt.layout.json";
        private const string AtlasPath = ArtPath + "/SlopballNightAtlas.png";
        private const string FloorPath = ArtPath + "/SlopballNightFloor.png";
        private const string DetailPath = ArtPath + "/SlopballNightDetail.png";
        private const string LightsName = "Stage Lights";
        // Baked light levels in the generator are divided by this; StageNeon's _Base restores them.
        private const float LightScale = 1.35f;
        // The earlier club finish; the base court surfaces stay for prepare/finish but are hidden.
        private static readonly string[] SupersededRoots = { "Club Finish", "ClubBlockout" };
        private static readonly string[] HiddenBase = { "CourtShell", "PlayerHalf", "SenderHalf", "LocalLighting" };

        [MenuItem("Tools/SLOPArena/Stages/Apply Slopball Night Court Art")]
        private static void ApplyMenu() => Apply();

        [CliCommand("sloparena.stage.art-slopball",
            "Apply the generated Slopball Night Court presentation to the court prefab; verifies and preserves baked gameplay.",
            MainThreadRequired = true, Tags = new[] { "arcade", "authoring/stage" })]
        public static object Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Apply the Night Court in stopped Edit Mode with the current scene saved.");
            if (!File.Exists(PrefabPath))
                throw new InvalidOperationException("Missing " + PrefabPath + ". Run sloparena.arcade.slopball.prepare first.");
            foreach (string path in new[] { ModelPath, StampPath, AtlasPath, FloorPath, DetailPath })
                if (!File.Exists(path))
                    throw new InvalidOperationException("Missing generated art " + path + ". Run tools/blender/slopball/build.py first.");

            string repository = Directory.GetParent(Path.GetFullPath(UnityCharacterAssetCooker.ProjectRoot()))?.Parent?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the repository root.");
            var stamp = JObject.Parse(File.ReadAllText(StampPath));
            string sourceHash = Sha256(SourcePath);
            string arenaHash = Sha256(Path.Combine(repository, "data", "arenas", "slopball.arena"));
            if (!string.Equals(sourceHash, (string)stamp["sourceSha256"], StringComparison.OrdinalIgnoreCase)
                || !string.Equals(arenaHash, (string)stamp["arenaSha256"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated art was built from a different source scene or arena. Regenerate it with tools/blender/slopball/build.py.");
            if ((int?)stamp["digitCell"] != SlopballCourtShow.DigitCellPixels || (int?)stamp["atlasSize"] != SlopballCourtShow.AtlasPixels)
                throw new InvalidOperationException("The generated combo-board atlas layout no longer matches SlopballCourtShow.");

            // Import only our own files: a project-wide refresh could pick up unrelated script edits.
            foreach (string path in new[] { AtlasPath, FloorPath, DetailPath, StampPath, ModelPath })
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureTexture(AtlasPath, srgb: true, TextureWrapMode.Clamp, 4096);
            ConfigureTexture(FloorPath, srgb: true, TextureWrapMode.Clamp, 2048);
            ConfigureTexture(DetailPath, srgb: false, TextureWrapMode.Repeat, 512);
            var materials = MakeMaterials();
            ConfigureModel(materials);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)
                ?? throw new InvalidOperationException("Unable to load " + ModelPath);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            var removed = new List<string>();
            var hidden = new List<string>();
            var objects = new Dictionary<string, int>();
            var cards = new Dictionary<string, Vector3>();
            try
            {
                if (root.transform.Find("Ball Sender") == null)
                    throw new InvalidOperationException("The court prefab lost its Ball Sender; rebuild it with sloparena.arcade.slopball.prepare.");
                foreach (string name in SupersededRoots.Concat(new[] { SlopballCourtShow.RootName, LightsName }))
                {
                    Transform previous;
                    while ((previous = root.transform.Find(name)) != null)
                    {
                        removed.Add(name);
                        Object.DestroyImmediate(previous.gameObject);
                    }
                }
                // The base court surfaces, net lines and old light stay in the prefab for the
                // earlier prepare/finish commands but no longer render.
                foreach (Transform child in root.transform)
                    if (HiddenBase.Contains(child.name) || child.GetComponent<LineRenderer>() != null)
                    {
                        if (child.gameObject.activeSelf) hidden.Add(child.name);
                        child.gameObject.SetActive(false);
                    }

                var art = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                art.name = SlopballCourtShow.RootName;
                art.transform.localPosition = Vector3.zero;
                art.transform.localRotation = Quaternion.identity;
                art.transform.localScale = Vector3.one;
                if (art.GetComponentsInChildren<Collider>(true).Length > 0)
                    throw new InvalidOperationException("Generated art must not contain Colliders.");
                foreach (var renderer in art.GetComponentsInChildren<MeshRenderer>(true))
                {
                    // Only the court takes shadows (the fighter's and the ball's); the art casts none.
                    bool court = renderer.name == "Court Floor";
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = court;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    objects[renderer.name] = mesh == null ? 0 : (int)Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i) / 3);
                    if (renderer.sharedMaterials.Any(x => x == null))
                        throw new InvalidOperationException("Unmapped generated material on " + renderer.name);
                    if (SlopballCourtShow.CardNames.Contains(renderer.name))
                        cards[renderer.name] = renderer.transform.localPosition;
                }
                if (cards.Count != SlopballCourtShow.CardNames.Length)
                    throw new InvalidOperationException("The generated combo board is missing digit cards.");

                var lights = new GameObject(LightsName).transform;
                lights.SetParent(root.transform, false);
                // The floods: a high cool-white key from just behind the receiving half, so the fighter's
                // back (the camera side) is lit and the ball's shadow falls almost straight down.
                var key = AddLight(lights, SlopballCourtShow.KeyLightName, LightType.Directional, Vector3.zero,
                    Quaternion.Euler(72f, 8f, 0f), new Color(0.90f, 0.95f, 1f), 1.25f, 0f);
                key.shadows = LightShadows.Soft;
                key.shadowStrength = 0.7f;
                // A warm fill behind the camera keeps the fighter readable against the dark hall.
                AddLight(lights, "Court Fill", LightType.Point, new Vector3(0f, 8f, -14f), Quaternion.identity,
                    new Color(1f, 0.93f, 0.84f), 9f, 26f);
                // One teal rim from the sender side, from the mural washers' direction.
                AddLight(lights, "Mural Rim", LightType.Point, new Vector3(0f, 3f, 17f), Quaternion.identity,
                    new Color(0.45f, 0.85f, 0.9f), 5f, 22f);

                var atmosphere = root.GetComponent<ArenaAtmosphere>();
                if (atmosphere == null) atmosphere = root.AddComponent<ArenaAtmosphere>();
                // After hours: blue-black warehouse, cool ambient, a thin haze that settles on the hall.
                atmosphere.Configure(new Color(0.012f, 0.014f, 0.022f), new Color(0.30f, 0.34f, 0.44f),
                    new Color(0.10f, 0.10f, 0.12f), 1f, true, new Color(0.030f, 0.036f, 0.052f), 22f, 120f);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            int pruned = PruneOrphanedSubAssets();
            return new
            {
                success = true, prefab = PrefabPath, root = SlopballCourtShow.RootName, model = ModelPath, sourceHash, arenaHash,
                triangles = objects.Values.Sum(), objects, cards = cards.ToDictionary(x => x.Key, x => new[] { x.Value.x, x.Value.y, x.Value.z }),
                removedRoots = removed, hiddenBase = hidden, prunedSubAssets = pruned, gameplay = "unchanged; presentation only"
            };
        }

        private static Light AddLight(Transform parent, string name, LightType type, Vector3 position, Quaternion rotation,
            Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            if (type != LightType.Directional) light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            return light;
        }

        /// <summary>Remove meshes/materials/textures embedded by the club finish and earlier passes.</summary>
        private static int PruneOrphanedSubAssets()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var used = new HashSet<Object>(EditorUtility.CollectDependencies(new Object[] { prefab }));
            int pruned = 0;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(PrefabPath))
            {
                if (asset == null || AssetDatabase.IsMainAsset(asset) || used.Contains(asset)) continue;
                if (!(asset is Mesh || asset is Material || asset is Texture || asset is UnityEngine.TextCore.Text.FontAsset)) continue;
                AssetDatabase.RemoveObjectFromAsset(asset);
                Object.DestroyImmediate(asset, true);
                pruned++;
            }
            if (pruned > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            }
            return pruned;
        }

        private static Dictionary<string, Material> MakeMaterials()
        {
            var lit = Shader.Find("SLOPArena/StageVertexLit") ?? throw new InvalidOperationException("Shader SLOPArena/StageVertexLit is missing.");
            var neon = Shader.Find("SLOPArena/StageNeon") ?? throw new InvalidOperationException("Shader SLOPArena/StageNeon is missing.");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? throw new InvalidOperationException("URP Particles/Unlit is unavailable.");
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var floor = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorPath);
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(DetailPath);
            Material Make(string file, Shader shader, Action<Material> configure)
            {
                string path = ArtPath + "/" + file + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                configure(material);
                EditorUtility.SetDirty(material);
                return material;
            }
            var result = new Dictionary<string, Material>
            {
                ["NC_Court"] = Make("SlopballNightCourt", lit, m =>
                {
                    m.SetTexture("_BaseMap", floor);
                    m.SetColor("_BaseColor", Color.white);
                    m.SetTexture("_DetailMap", detail);
                    m.SetFloat("_DetailScale", 0.5f);
                    m.SetFloat("_DetailStrength", 0.22f);
                    m.SetColor("_ShadowTint", new Color(0.50f, 0.56f, 0.70f));
                    m.SetFloat("_Wrap", 0.2f);
                    m.SetFloat("_Specular", 0.22f);
                    m.SetFloat("_Gloss", 40f);
                    m.SetFloat("_Emission", 0f);
                    m.SetFloat("_Wind", 0f);
                    m.SetFloat("_Cull", (float)CullMode.Back);
                    m.SetFloat("_ReceiveShadowsOff", 0f);
                    m.DisableKeyword("_RECEIVE_SHADOWS_OFF");
                    m.enableInstancing = true;
                }),
                // Baked night light lives in the vertex colours; glow stays near 1.6 so signs keep their hue.
                ["NC_Night"] = Make("SlopballNightWarehouse", neon, m =>
                {
                    m.SetTexture("_BaseMap", atlas);
                    m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_Base", LightScale);
                    m.SetFloat("_Ambient", 0.12f);
                    m.SetVector("_ShadeDirection", new Vector4(0.2f, 0.85f, -0.3f, 0f));
                    m.SetFloat("_Shade", 0.15f);
                    m.SetFloat("_Glow", SlopballCourtShow.FloodGlow);
                    m.SetFloat("_FogAmount", 1f);
                    m.SetFloat("_TimeScale", 1f);
                    m.SetFloat("_Cull", (float)CullMode.Back);
                    m.enableInstancing = true;
                }),
                // Additive light shafts; vertex colour carries their fade.
                ["NC_Beam"] = Make("SlopballNightBeams", particles, m =>
                {
                    m.SetTexture("_BaseMap", null);
                    m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_Surface", 1f);
                    m.SetFloat("_Blend", 2f);
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)BlendMode.One);
                    m.SetFloat("_SrcBlendAlpha", 1f);
                    m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
                    m.SetFloat("_ZWrite", 0f);
                    m.SetFloat("_Cull", 0f);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetFloat("_QueueOffset", 0f);
                    m.renderQueue = (int)RenderQueue.Transparent;
                    m.SetShaderPassEnabled("DepthOnly", false);
                    m.SetShaderPassEnabled("SHADOWCASTER", false);
                }),
            };
            AssetDatabase.SaveAssets();
            return result;
        }

        private static void ConfigureModel(Dictionary<string, Material> materials)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.addCollider = false;
            importer.indexFormat = ModelImporterIndexFormat.Auto;
            // Secondary UV generation would overwrite TEXCOORD1, which carries glow animation.
            importer.generateSecondaryUV = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var pair in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();
        }

        private static void ConfigureTexture(string path, bool srgb, TextureWrapMode wrap, int maxSize)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = wrap;
            importer.anisoLevel = 8;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
