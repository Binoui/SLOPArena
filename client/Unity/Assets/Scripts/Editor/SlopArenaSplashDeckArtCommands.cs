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
    /// Applies the generated Splash Deck fishing-harbour presentation (see tools/blender/splash_deck)
    /// to the stage prefab. Presentation only: the authoritative source scene and baked arena are
    /// verified against the generator stamp and never written.
    /// </summary>
    public static class SlopArenaSplashDeckArtCommands
    {
        private const string PrefabPath = "Assets/Resources/Stages/splash_deck.prefab";
        private const string SourcePath = "Assets/Stages/splash_deck/splash_deck.unity";
        private const string ArtPath = "Assets/Stages/splash_deck/Presentation/Harbour";
        private const string ModelPath = ArtPath + "/SplashDeckHarbour.fbx";
        private const string StampPath = ArtPath + "/SplashDeckHarbour.layout.json";
        private const string DeckPath = ArtPath + "/SplashDeckDeck.png";
        private const string HullPath = ArtPath + "/SplashDeckHull.png";
        private const string AtlasPath = ArtPath + "/SplashDeckAtlas.png";
        private const string DetailPath = ArtPath + "/SplashDeckDetail.png";
        private const string RipplesPath = ArtPath + "/SplashDeckRipples.png";
        private const string RootName = "Splash Deck Harbour";
        private const string LightsName = "Stage Lights";
        // Roots of the previous moored-barge pass (vendor tropical port, trims, preview boardwalk).
        private static readonly string[] SupersededRoots =
        {
            "Playable Geometry", "North Trim", "South Trim", "East Trim", "West Trim", "Spawn Points",
            "Distant tropical port", "Boardwalk Surface (preview)", "Authoritative recovery hull presentation",
            "Barge warm deck fill",
        };
        // Sky, distant or emissive objects neither cast nor receive shadows; the sea only receives.
        private static readonly HashSet<string> Unshadowed = new() { "Sky", "Clouds", "Gulls", "Far Islands", "Lighthouse Lamp" };
        private static readonly HashSet<string> ReceiveOnly = new() { "Sea" };

        [MenuItem("Tools/SLOPArena/Stages/Apply Splash Deck Harbour Art")]
        private static void ApplyMenu() => Apply();

        [CliCommand("sloparena.stage.art-splash-deck",
            "Apply the generated Splash Deck fishing-harbour presentation to the stage prefab; verifies and preserves baked gameplay.",
            MainThreadRequired = true, Tags = new[] { "authoring/stage" })]
        public static object Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Apply Splash Deck art in stopped Edit Mode with the current scene saved.");
            if (!File.Exists(PrefabPath)) throw new InvalidOperationException("Missing " + PrefabPath + ".");
            foreach (string path in new[] { ModelPath, StampPath, DeckPath, HullPath, AtlasPath, DetailPath, RipplesPath })
                if (!File.Exists(path))
                    throw new InvalidOperationException("Missing generated art " + path + ". Run tools/blender/splash_deck/build.py first.");

            string repository = Directory.GetParent(Path.GetFullPath(UnityCharacterAssetCooker.ProjectRoot()))?.Parent?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the repository root.");
            string arenaPath = Path.Combine(repository, "data", "arenas", "splash_deck.arena");
            var stamp = JObject.Parse(File.ReadAllText(StampPath));
            string sourceHash = Sha256(SourcePath);
            string arenaHash = Sha256(arenaPath);
            if (!string.Equals(sourceHash, (string)stamp["sourceSha256"], StringComparison.OrdinalIgnoreCase)
                || !string.Equals(arenaHash, (string)stamp["arenaSha256"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated art was built from a different source scene or arena. Regenerate it with tools/blender/splash_deck/build.py.");

            AssetDatabase.Refresh();
            ConfigureTexture(DeckPath, srgb: true, TextureWrapMode.Clamp, 2048);
            ConfigureTexture(HullPath, srgb: true, TextureWrapMode.Clamp, 8192);
            ConfigureTexture(AtlasPath, srgb: true, TextureWrapMode.Clamp, 2048);
            ConfigureTexture(DetailPath, srgb: false, TextureWrapMode.Repeat, 512);
            ConfigureTexture(RipplesPath, srgb: false, TextureWrapMode.Repeat, 512);
            var materials = MakeMaterials();
            ConfigureModel(materials);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)
                ?? throw new InvalidOperationException("Unable to load " + ModelPath);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            var removed = new List<string>();
            var objects = new Dictionary<string, int>();
            try
            {
                foreach (string name in SupersededRoots.Concat(new[] { RootName, LightsName }))
                {
                    Transform previous;
                    while ((previous = root.transform.Find(name)) != null)
                    {
                        removed.Add(name);
                        Object.DestroyImmediate(previous.gameObject);
                    }
                }
                // The previous pass kept a light on the root object itself.
                foreach (var light in root.GetComponents<Light>())
                {
                    removed.Add("root Light");
                    Object.DestroyImmediate(light);
                }

                var art = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                art.name = RootName;
                art.transform.localPosition = Vector3.zero;
                art.transform.localRotation = Quaternion.identity;
                art.transform.localScale = Vector3.one;
                if (art.GetComponentsInChildren<Collider>(true).Length > 0)
                    throw new InvalidOperationException("Generated art must not contain Colliders.");
                foreach (var renderer in art.GetComponentsInChildren<MeshRenderer>(true))
                {
                    bool unshadowed = Unshadowed.Contains(renderer.name);
                    bool receiveOnly = ReceiveOnly.Contains(renderer.name);
                    renderer.shadowCastingMode = unshadowed || receiveOnly ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    renderer.receiveShadows = !unshadowed;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    objects[renderer.name] = mesh == null ? 0 : (int)Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i) / 3);
                    if (renderer.sharedMaterials.Any(x => x == null))
                        throw new InvalidOperationException("Unmapped generated material on " + renderer.name);
                }

                var lights = new GameObject(LightsName).transform;
                lights.SetParent(root.transform, false);
                // Late-morning sun from the east-south-east, on the bearing of the sky's sun disc.
                var sunStamp = (JObject)stamp["sun"] ?? throw new InvalidOperationException("The generator stamp has no sun.");
                float sunAzimuth = (float)sunStamp["azimuth"], sunElevation = (float)sunStamp["elevation"];
                var towardsSun = Quaternion.Euler(-sunElevation, sunAzimuth, 0f) * Vector3.forward;
                var sun = AddLight(lights, "Morning Sun", LightType.Directional, Vector3.zero, Quaternion.LookRotation(-towardsSun),
                    new Color(1f, 0.96f, 0.88f), 1.3f, 0f);
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;
                // Soft sky bounce over the deck keeps the shaded side of fighters readable.
                AddLight(lights, "Deck Sky Fill", LightType.Point, new Vector3(2.5f, 24f, -6f), Quaternion.identity,
                    new Color(0.82f, 0.9f, 1f), 0.55f, 60f);

                var atmosphere = root.GetComponent<ArenaAtmosphere>() ?? root.AddComponent<ArenaAtmosphere>();
                // Clear morning: blue sky light, warm ground bounce, pale sea haze towards the horizon.
                atmosphere.Configure(new Color(0.62f, 0.78f, 0.94f), new Color(0.70f, 0.80f, 0.95f), new Color(0.56f, 0.50f, 0.42f), 1f,
                    true, new Color(0.80f, 0.87f, 0.92f), 170f, 1500f);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            int pruned = PruneOrphanedSubAssets();
            return new { success = true, prefab = PrefabPath, root = RootName, model = ModelPath, sourceHash, arenaHash,
                triangles = objects.Values.Sum(), objects, removedRoots = removed, prunedSubAssets = pruned,
                gameplay = "unchanged; presentation only" };
        }

        private static Light AddLight(Transform parent, string name, LightType type, Vector3 position, Quaternion rotation, Color color, float intensity, float range)
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

        /// <summary>Remove meshes/materials/textures embedded by the previous pass.</summary>
        private static int PruneOrphanedSubAssets()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var used = new HashSet<Object>(EditorUtility.CollectDependencies(new Object[] { prefab }));
            int pruned = 0;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(PrefabPath))
            {
                if (asset == null || AssetDatabase.IsMainAsset(asset) || used.Contains(asset)) continue;
                if (!(asset is Mesh || asset is Material || asset is Texture)) continue;
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
            var water = Shader.Find("SLOPArena/StageWater") ?? throw new InvalidOperationException("Shader SLOPArena/StageWater is missing.");
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var deck = AssetDatabase.LoadAssetAtPath<Texture2D>(DeckPath);
            var hull = AssetDatabase.LoadAssetAtPath<Texture2D>(HullPath);
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(DetailPath);
            var ripples = AssetDatabase.LoadAssetAtPath<Texture2D>(RipplesPath);
            Material Make(string file, Shader shader, Texture2D baseMap, Action<Material> configure, float tint = 1f)
            {
                string path = ArtPath + "/" + file + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                if (baseMap != null) material.SetTexture("_BaseMap", baseMap);
                material.SetColor("_BaseColor", new Color(tint, tint, tint, 1f));
                configure(material);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                return material;
            }
            void Lit(Material m, float detailStrength, float detailScale, float wrap, float specular, float wind, float windFrequency, CullMode cull)
            {
                m.SetTexture("_DetailMap", detail);
                m.SetFloat("_DetailScale", detailScale);
                m.SetFloat("_DetailStrength", detailStrength);
                m.SetColor("_ShadowTint", new Color(0.66f, 0.72f, 0.86f));
                m.SetFloat("_Wrap", wrap);
                m.SetFloat("_Specular", specular);
                m.SetFloat("_Gloss", 28f);
                m.SetFloat("_Emission", 0f);
                m.SetFloat("_Wind", wind);
                m.SetFloat("_WindFrequency", windFrequency);
                m.SetVector("_WindDirection", new Vector4(-1f, 0f, 0.3f, 0f)); // the sea breeze blows ashore, westwards
                m.SetFloat("_Cull", (float)cull);
                m.SetFloat("_ReceiveShadowsOff", 0f);
                m.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            }
            void Neon(Material m, float baseLevel, float ambient, float shade, float glow, float fog, CullMode cull)
            {
                m.SetFloat("_Base", baseLevel);
                m.SetFloat("_Ambient", ambient);
                m.SetVector("_ShadeDirection", new Vector4(0.6f, 0.75f, -0.25f, 0f));
                m.SetFloat("_Shade", shade);
                m.SetFloat("_Glow", glow);
                m.SetFloat("_FogAmount", fog);
                m.SetFloat("_TimeScale", 1f);
                m.SetFloat("_Cull", (float)cull);
            }
            // Lit tints below one keep the deck and hills from washing out when Training's own
            // key/fill/rim lights add to the stage sun; the sun is raised to keep PvP's sunlit look.
            var result = new Dictionary<string, Material>
            {
                ["SD_Deck"] = Make("SplashDeckDeck", lit, deck, m => Lit(m, 0.16f, 0.45f, 0.3f, 0.12f, 0f, 1f, CullMode.Back), 0.88f),
                ["SD_Hull"] = Make("SplashDeckHull", lit, hull, m => Lit(m, 0.2f, 0.45f, 0.35f, 0.25f, 0f, 1f, CullMode.Back), 0.9f),
                ["SD_Wood"] = Make("SplashDeckWood", lit, atlas, m => Lit(m, 0.25f, 0.45f, 0.35f, 0.1f, 0f, 1f, CullMode.Back), 0.9f),
                ["SD_Land"] = Make("SplashDeckLand", lit, atlas, m => Lit(m, 0.4f, 0.25f, 0.35f, 0f, 0f, 1f, CullMode.Back), 0.82f),
                ["SD_Foliage"] = Make("SplashDeckFoliage", lit, atlas, m => Lit(m, 0.1f, 0.45f, 0.5f, 0f, 0.3f, 1.1f, CullMode.Off), 0.85f),
                ["SD_Cloth"] = Make("SplashDeckCloth", lit, atlas, m => Lit(m, 0.05f, 0.45f, 0.5f, 0f, 0.22f, 1.7f, CullMode.Off), 0.9f),
                ["SD_Water"] = Make("SplashDeckWater", water, null, m =>
                {
                    m.SetTexture("_RippleMap", ripples);
                    m.SetColor("_SkyColor", new Color(0.40f, 0.62f, 0.90f));
                    m.SetColor("_HorizonColor", new Color(0.80f, 0.88f, 0.94f));
                }),
                // Gulls and the lighthouse lamp: softly shaded, lightly glowing where vertex alpha marks it.
                ["SD_Glow"] = Make("SplashDeckGlow", neon, atlas, m => Neon(m, 0.85f, 0.55f, 0.35f, 1.2f, 1f, CullMode.Off)),
                ["SD_Sky"] = Make("SplashDeckSky", neon, atlas, m => Neon(m, 1f, 0f, 0f, 1.4f, 0f, CullMode.Off)),
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
            // Secondary UV generation would overwrite TEXCOORD1, which carries animation channels.
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
