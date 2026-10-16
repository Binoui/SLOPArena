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
    /// Applies the generated Slop Pit underground fight-ring presentation (see tools/blender/slop_pit)
    /// to the stage prefab. Presentation only: the authoritative source scene and baked arena are
    /// verified against the generator stamp and never written.
    /// </summary>
    public static class SlopArenaSlopPitArtCommands
    {
        private const string PrefabPath = "Assets/Resources/Stages/slop_pit.prefab";
        private const string SourcePath = "Assets/Stages/slop_pit/slop_pit.unity";
        private const string ArtPath = "Assets/Stages/slop_pit/Presentation/Ring";
        private const string ModelPath = ArtPath + "/SlopPitArena.fbx";
        private const string StampPath = ArtPath + "/SlopPitArena.layout.json";
        private const string AtlasPath = ArtPath + "/SlopPitAtlas.png";
        private const string CanvasPath = ArtPath + "/SlopPitCanvas.png";
        private const string FloorPath = ArtPath + "/SlopPitFloor.png";
        private const string DetailPath = ArtPath + "/SlopPitDetail.png";
        private const string RootName = "Slop Pit Fight Ring";
        private const string LightsName = "Stage Lights";
        // Roots of the previous hand-built factory pass (vendor scaffolds, conveyors, scrap); the ring replaces them.
        private static readonly string[] SupersededRoots =
        {
            "Platform_A", "Platform_B", "ArtPass01", "PitScrapFill", "ConveyorSilhouettes",
            "DeckEdgeLighting", "PerimeterWalls", "MainDeck", "SparseScrap", "BlockoutFill",
        };
        // Only the fight surfaces and their tower take part in shadows; the venue is unlit StageNeon.
        private static readonly HashSet<string> Shadowed = new() { "Ring", "Ringside Floor", "Ring Tower" };

        [MenuItem("Tools/SLOPArena/Stages/Apply Slop Pit Fight Ring Art")]
        private static void ApplyMenu() => Apply();

        [CliCommand("sloparena.stage.art-slop-pit",
            "Apply the generated Slop Pit fight-ring presentation to the stage prefab; verifies and preserves baked gameplay.",
            MainThreadRequired = true, Tags = new[] { "authoring/stage" })]
        public static object Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Apply Slop Pit art in stopped Edit Mode with the current scene saved.");
            if (!File.Exists(PrefabPath)) throw new InvalidOperationException("Missing " + PrefabPath + ".");
            foreach (string path in new[] { ModelPath, StampPath, AtlasPath, CanvasPath, FloorPath, DetailPath })
                if (!File.Exists(path))
                    throw new InvalidOperationException("Missing generated art " + path + ". Run tools/blender/slop_pit/build.py first.");

            string repository = Directory.GetParent(Path.GetFullPath(UnityCharacterAssetCooker.ProjectRoot()))?.Parent?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the repository root.");
            string arenaPath = Path.Combine(repository, "data", "arenas", "slop_pit.arena");
            var stamp = JObject.Parse(File.ReadAllText(StampPath));
            string sourceHash = Sha256(SourcePath);
            string arenaHash = Sha256(arenaPath);
            if (!string.Equals(sourceHash, (string)stamp["sourceSha256"], StringComparison.OrdinalIgnoreCase)
                || !string.Equals(arenaHash, (string)stamp["arenaSha256"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated art was built from a different source scene or arena. Regenerate it with tools/blender/slop_pit/build.py.");

            AssetDatabase.Refresh();
            ConfigureTexture(AtlasPath, srgb: true, TextureWrapMode.Clamp, 2048);
            ConfigureTexture(CanvasPath, srgb: true, TextureWrapMode.Clamp, 2048);
            ConfigureTexture(FloorPath, srgb: true, TextureWrapMode.Clamp, 2048);
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
                    bool shadowed = Shadowed.Contains(renderer.name);
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
                // A high, warm-white key from the south-south-west: short readable fighter shadows on the canvas.
                var key = AddLight(lights, "Ring Key", LightType.Directional, Vector3.zero, Quaternion.Euler(64f, 22f, 0f),
                    new Color(1f, 0.92f, 0.80f), 1.05f, 0f);
                key.shadows = LightShadows.Soft;
                key.shadowStrength = 0.62f;
                // The ring's overhead rig: brighter canvas in the middle, falling off towards ringside.
                AddLight(lights, "Ring Overhead", LightType.Point, new Vector3(0f, 9f, 0f), Quaternion.identity,
                    new Color(1f, 0.86f, 0.66f), 28f, 26f);
                // Slop glow from below on the tower walls.
                AddLight(lights, "Slop Glow North", LightType.Point, new Vector3(0f, -18f, 22f), Quaternion.identity,
                    new Color(0.55f, 1f, 0.25f), 24f, 20f);
                AddLight(lights, "Slop Glow South", LightType.Point, new Vector3(0f, -18f, -22f), Quaternion.identity,
                    new Color(0.55f, 1f, 0.25f), 24f, 20f);

                var atmosphere = root.GetComponent<ArenaAtmosphere>() ?? root.AddComponent<ArenaAtmosphere>();
                // Fight night underground: near-black warm void, smoky orange haze that swallows the stands.
                atmosphere.Configure(new Color(0.035f, 0.022f, 0.016f), new Color(0.36f, 0.30f, 0.26f), new Color(0.10f, 0.08f, 0.07f), 1f,
                    true, new Color(0.30f, 0.16f, 0.07f), 24f, 190f);

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

        /// <summary>Remove meshes/materials/textures embedded by the previous hand-built pass.</summary>
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
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var canvas = AssetDatabase.LoadAssetAtPath<Texture2D>(CanvasPath);
            var floor = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorPath);
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
            void Lit(Material m, float detailStrength, float wrap, float specular)
            {
                m.SetTexture("_DetailMap", detail);
                m.SetFloat("_DetailScale", 0.5f);
                m.SetFloat("_DetailStrength", detailStrength);
                m.SetColor("_ShadowTint", new Color(0.62f, 0.55f, 0.52f));
                m.SetFloat("_Wrap", wrap);
                m.SetFloat("_Specular", specular);
                m.SetFloat("_Gloss", 22f);
                m.SetFloat("_Emission", 0f);
                m.SetFloat("_Wind", 0f);
                m.SetFloat("_Cull", (float)CullMode.Back);
                m.SetFloat("_ReceiveShadowsOff", 0f);
                m.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            }
            var result = new Dictionary<string, Material>
            {
                ["SP_Lit"] = Make("SlopPitLit", lit, atlas, m => Lit(m, 0.28f, 0.3f, 0.12f)),
                ["SP_Floor"] = Make("SlopPitFloor", lit, floor, m => Lit(m, 0.3f, 0.3f, 0.18f)),
                ["SP_Canvas"] = Make("SlopPitCanvas", lit, canvas, m => Lit(m, 0.12f, 0.35f, 0.05f)),
                // Glow stays near 1.4 so lamps and neon keep their hue under the match's ACES grade.
                ["SP_Venue"] = Make("SlopPitVenue", neon, atlas, m => Night(m, baseLevel: 0.42f, ambient: 0.5f, glow: 1.4f, fog: 1f)),
                // The orange wash behind the fans: fully lit, partly through the haze.
                ["SP_Haze"] = Make("SlopPitHaze", neon, atlas, m => Night(m, baseLevel: 0.42f, ambient: 0f, glow: 0.32f, fog: 0.55f)),
            };
            AssetDatabase.SaveAssets();
            return result;
        }

        private static void Night(Material m, float baseLevel, float ambient, float glow, float fog)
        {
            m.SetFloat("_Base", baseLevel);
            m.SetFloat("_Ambient", ambient);
            m.SetVector("_ShadeDirection", new Vector4(0.2f, 0.85f, -0.3f, 0f));
            m.SetFloat("_Shade", 0.35f);
            m.SetFloat("_Glow", glow);
            m.SetFloat("_FogAmount", fog);
            m.SetFloat("_TimeScale", 1f);
            m.SetFloat("_Cull", (float)CullMode.Back);
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
