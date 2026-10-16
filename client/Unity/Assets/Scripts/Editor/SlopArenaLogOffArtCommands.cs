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
    /// Applies the generated Log Off campsite presentation (see tools/blender/log_off) to the
    /// stage prefab. Presentation only: the authoritative source scene and baked arena are
    /// verified against the generator stamp and never written.
    /// </summary>
    public static class SlopArenaLogOffArtCommands
    {
        private const string PrefabPath = "Assets/Resources/Stages/log_off.prefab";
        private const string SourcePath = "Assets/Stages/log_off/log_off.unity";
        private const string ArtPath = "Assets/Stages/log_off/Presentation/Campsite";
        private const string ModelPath = ArtPath + "/LogOffCampsite.fbx";
        private const string StampPath = ArtPath + "/LogOffCampsite.layout.json";
        private const string TopPath = ArtPath + "/LogOffStumpTop.png";
        private const string AtlasPath = ArtPath + "/LogOffAtlas.png";
        private const string DetailPath = ArtPath + "/LogOffDetail.png";
        private const string RootName = "Log Off Campsite";
        private const string LightsName = "Stage Lights";
        // Roots of the previous woodland pass (vendor trees, hideout); the campsite replaces them.
        private static readonly string[] SupersededRoots = { "ApprovedStump", "WoodlandWorld", "WarmAfternoonSun" };
        // Distant or emissive objects neither cast nor receive shadows.
        private static readonly HashSet<string> Unshadowed = new() { "Sky", "Fireflies", "Far Forest", "Camp Glow", "Canopy" };

        [MenuItem("Tools/SlopArena/Stages/Apply Log Off Campsite Art")]
        private static void ApplyMenu() => Apply();

        [CliCommand("sloparena.stage.art-log-off",
            "Apply the generated Log Off campsite presentation to the stage prefab; verifies and preserves baked gameplay.",
            MainThreadRequired = true, Tags = new[] { "authoring/stage" })]
        public static object Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Apply Log Off art in stopped Edit Mode with the current scene saved.");
            if (!File.Exists(PrefabPath)) throw new InvalidOperationException("Missing " + PrefabPath + ".");
            foreach (string path in new[] { ModelPath, StampPath, TopPath, AtlasPath, DetailPath })
                if (!File.Exists(path))
                    throw new InvalidOperationException("Missing generated art " + path + ". Run tools/blender/log_off/build.py first.");

            string repository = Directory.GetParent(Path.GetFullPath(UnityCharacterAssetCooker.ProjectRoot()))?.Parent?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the repository root.");
            string arenaPath = Path.Combine(repository, "data", "arenas", "log_off.arena");
            var stamp = JObject.Parse(File.ReadAllText(StampPath));
            string sourceHash = Sha256(SourcePath);
            string arenaHash = Sha256(arenaPath);
            if (!string.Equals(sourceHash, (string)stamp["sourceSha256"], StringComparison.OrdinalIgnoreCase)
                || !string.Equals(arenaHash, (string)stamp["arenaSha256"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated art was built from a different source scene or arena. Regenerate it with tools/blender/log_off/build.py.");

            AssetDatabase.Refresh();
            ConfigureTexture(TopPath, srgb: true, TextureWrapMode.Clamp, 2048);
            ConfigureTexture(AtlasPath, srgb: true, TextureWrapMode.Clamp, 2048);
            ConfigureTexture(DetailPath, srgb: false, TextureWrapMode.Repeat, 512);
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

                var art = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                art.name = RootName;
                art.transform.localPosition = Vector3.zero;
                art.transform.localRotation = Quaternion.identity;
                art.transform.localScale = Vector3.one;
                if (art.GetComponentsInChildren<Collider>(true).Length > 0)
                    throw new InvalidOperationException("Generated art must not contain Colliders.");
                foreach (var renderer in art.GetComponentsInChildren<MeshRenderer>(true))
                {
                    bool shadowed = !Unshadowed.Contains(renderer.name);
                    renderer.shadowCastingMode = shadowed ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    renderer.receiveShadows = shadowed;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    objects[renderer.name] = mesh == null ? 0 : (int)Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i) / 3);
                    if (renderer.sharedMaterials.Any(x => x == null))
                        throw new InvalidOperationException("Unmapped generated material on " + renderer.name);
                }

                var lights = new GameObject(LightsName).transform;
                lights.SetParent(root.transform, false);
                // Low warm sun from the west-south-west (the sky's sun sits on the same bearing).
                const float sunAzimuth = 252f, sunElevation = 22f;
                var towardsSun = Quaternion.Euler(-sunElevation, sunAzimuth, 0f) * Vector3.forward;
                var sun = AddLight(lights, "Sunset Sun", LightType.Directional, Vector3.zero, Quaternion.LookRotation(-towardsSun),
                    new Color(1f, 0.71f, 0.47f), 1.25f, 0f);
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.55f;
                var camp = (JObject)stamp["lights"];
                AddLight(lights, "Campfire", LightType.Point, Point(camp, "campfire"), Quaternion.identity, new Color(1f, 0.55f, 0.22f), 6f, 190f);
                AddLight(lights, "Lantern", LightType.Point, Point(camp, "lantern"), Quaternion.identity, new Color(1f, 0.8f, 0.45f), 3f, 70f);
                AddLight(lights, "Tent Glow", LightType.Point, Point(camp, "tent"), Quaternion.identity, new Color(1f, 0.65f, 0.35f), 2f, 55f);
                // Keeps the fight surface warm and readable while the sun grazes it.
                AddLight(lights, "Stump Fill", LightType.Point, new Vector3(0f, 26f, -4f), Quaternion.identity, new Color(1f, 0.88f, 0.74f), 0.9f, 50f);

                var atmosphere = root.GetComponent<ArenaAtmosphere>() ?? root.AddComponent<ArenaAtmosphere>();
                // Dusk under the canopy: lavender sky light, warm earth bounce, rosy haze in the far trunks.
                atmosphere.Configure(new Color(0.62f, 0.50f, 0.56f), new Color(0.52f, 0.46f, 0.64f), new Color(0.22f, 0.16f, 0.11f), 1f,
                    true, new Color(0.66f, 0.50f, 0.50f), 90f, 720f);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            int pruned = PruneOrphanedSubAssets();
            return new { success = true, prefab = PrefabPath, root = RootName, model = ModelPath, sourceHash, arenaHash,
                triangles = objects.Values.Sum(), objects, removedRoots = removed, prunedSubAssets = pruned,
                gameplay = "unchanged; presentation only" };
        }

        private static Vector3 Point(JObject lights, string name)
        {
            var p = (JArray)lights?[name] ?? throw new InvalidOperationException("The generator stamp has no '" + name + "' light.");
            return new Vector3((float)p[0], (float)p[1], (float)p[2]);
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
            var lit = Shader.Find("SlopArena/StageVertexLit") ?? throw new InvalidOperationException("Shader SlopArena/StageVertexLit is missing.");
            var neon = Shader.Find("SlopArena/StageNeon") ?? throw new InvalidOperationException("Shader SlopArena/StageNeon is missing.");
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var top = AssetDatabase.LoadAssetAtPath<Texture2D>(TopPath);
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(DetailPath);
            Material Make(string file, Shader shader, Texture2D baseMap, Action<Material> configure)
            {
                string path = ArtPath + "/" + file + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                material.SetTexture("_BaseMap", baseMap);
                material.SetColor("_BaseColor", Color.white);
                configure(material);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                return material;
            }
            void Lit(Material m, float detailStrength, float wrap, float wind, CullMode cull)
            {
                m.SetTexture("_DetailMap", detail);
                m.SetFloat("_DetailScale", 0.45f);
                m.SetFloat("_DetailStrength", detailStrength);
                m.SetColor("_ShadowTint", new Color(0.62f, 0.56f, 0.78f));
                m.SetFloat("_Wrap", wrap);
                m.SetFloat("_Specular", 0f);
                m.SetFloat("_Emission", 0f);
                m.SetFloat("_Wind", wind);
                m.SetFloat("_WindFrequency", 1.3f);
                m.SetFloat("_Cull", (float)cull);
                m.SetFloat("_ReceiveShadowsOff", 0f);
                m.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            }
            var result = new Dictionary<string, Material>
            {
                ["LO_Wood"] = Make("LogOffWood", lit, atlas, m => Lit(m, 0.3f, 0.35f, 0f, CullMode.Back)),
                ["LO_Stump"] = Make("LogOffStumpTop", lit, top, m => Lit(m, 0.18f, 0.45f, 0f, CullMode.Back)),
                ["LO_Foliage"] = Make("LogOffFoliage", lit, atlas, m => Lit(m, 0.1f, 0.5f, 0.8f, CullMode.Off)),
                ["LO_Cloth"] = Make("LogOffCloth", lit, atlas, m => Lit(m, 0.12f, 0.5f, 0.35f, CullMode.Off)),
                // Glow stays modest so fire and fireflies keep their hue under the match's ACES grade.
                ["LO_Glow"] = Make("LogOffGlow", neon, atlas, m =>
                {
                    m.SetFloat("_Base", 0.6f);
                    m.SetFloat("_Ambient", 0.4f);
                    m.SetVector("_ShadeDirection", new Vector4(-0.35f, 0.8f, -0.2f, 0f));
                    m.SetFloat("_Shade", 0.2f);
                    m.SetFloat("_Glow", 1.6f);
                    m.SetFloat("_FogAmount", 0.6f);
                    m.SetFloat("_TimeScale", 1f);
                    m.SetFloat("_Cull", (float)CullMode.Back);
                }),
                ["LO_Sky"] = Make("LogOffSky", neon, atlas, m =>
                {
                    m.SetFloat("_Base", 1f);
                    m.SetFloat("_Ambient", 0f);
                    m.SetFloat("_Shade", 0f);
                    m.SetFloat("_Glow", 1.2f);
                    m.SetFloat("_FogAmount", 0f);
                    m.SetFloat("_TimeScale", 1f);
                    m.SetFloat("_Cull", (float)CullMode.Off);
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
