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
    /// Applies the generated Table Manners kids' craft-room presentation (see
    /// tools/blender/table_manners) to the stage prefab. Presentation only: the authoritative
    /// source scene and baked arena are verified against the generator stamp and never written.
    /// </summary>
    public static class SlopArenaTableMannersArtCommands
    {
        private const string PrefabPath = "Assets/Resources/Stages/table_manners.prefab";
        private const string SourcePath = "Assets/Stages/table_manners/table_manners.unity";
        private const string ArtPath = "Assets/Stages/table_manners/Presentation/CraftRoom";
        private const string ModelPath = ArtPath + "/TableMannersCraftRoom.fbx";
        private const string StampPath = ArtPath + "/TableMannersCraftRoom.layout.json";
        private const string AtlasPath = ArtPath + "/TableMannersAtlas.png";
        private const string DetailPath = ArtPath + "/TableMannersDetail.png";
        private const string RootName = "Table Manners Craft Room";
        private const string LightsName = "Stage Lights";
        // Roots of the previous Cozy Interior living-room pass; the generated room replaces them.
        private static readonly string[] SupersededRoots = { "PlayableTable", "WindowTableFill", "SpawnMarkers", "WarmTableFill", "OversizedLivingRoom" };
        // Large enclosing or distant surfaces would shade the whole room under the overhead key light.
        private static readonly HashSet<string> NoShadowCasters = new HashSet<string> { "Room Walls", "Ceiling", "Garden", "Garden Far", "Sky" };

        [MenuItem("Tools/SLOPArena/Stages/Apply Table Manners Craft Room Art")]
        private static void ApplyMenu() => Apply();

        [CliCommand("sloparena.stage.art-table-manners",
            "Apply the generated Table Manners kids' craft-room presentation to the stage prefab; verifies and preserves baked gameplay.",
            MainThreadRequired = true, Tags = new[] { "authoring/stage" })]
        public static object Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Apply Table Manners art in stopped Edit Mode with the current scene saved.");
            if (!File.Exists(PrefabPath)) throw new InvalidOperationException("Missing " + PrefabPath + ".");
            foreach (string path in new[] { ModelPath, StampPath, AtlasPath, DetailPath })
                if (!File.Exists(path))
                    throw new InvalidOperationException("Missing generated art " + path + ". Run tools/blender/table_manners/build.py first.");

            string repository = Directory.GetParent(Path.GetFullPath(UnityCharacterAssetCooker.ProjectRoot()))?.Parent?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the repository root.");
            string arenaPath = Path.Combine(repository, "data", "arenas", "table_manners.arena");
            var stamp = JObject.Parse(File.ReadAllText(StampPath));
            string sourceHash = Sha256(SourcePath);
            string arenaHash = Sha256(arenaPath);
            if (!string.Equals(sourceHash, (string)stamp["sourceSha256"], StringComparison.OrdinalIgnoreCase)
                || !string.Equals(arenaHash, (string)stamp["arenaSha256"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated art was built from a different source scene or arena. Regenerate it with tools/blender/table_manners/build.py.");

            AssetDatabase.Refresh();
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
                    renderer.shadowCastingMode = NoShadowCasters.Contains(renderer.name) ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    renderer.receiveShadows = renderer.name != "Sky" && renderer.name != "Garden Far";
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    objects[renderer.name] = mesh == null ? 0 : (int)Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i) / 3);
                    if (renderer.sharedMaterials.Any(x => x == null))
                        throw new InvalidOperationException("Unmapped generated material on " + renderer.name);
                }

                var lights = new GameObject(LightsName).transform;
                lights.SetParent(root.transform, false);
                // The stage carries its key: high daylight from the north window whose soft shadows fall
                // close under the fighters. Arena_PvP has no lights, and Training switches its own off for a
                // stage with this rig, so these are the only lights in every mode.
                AddLight(lights, "Window Sun", LightType.Directional, Vector3.zero, Quaternion.Euler(62f, 165f, 0f), new Color(1f, 0.94f, 0.84f), 0.95f, 0f, 0.55f);
                AddLight(lights, "Window Glow", LightType.Point, new Vector3(0f, 18f, 56f), Quaternion.identity, new Color(1f, 0.92f, 0.80f), 1.6f, 75f);
                AddLight(lights, "Lantern", LightType.Point, new Vector3(0f, 38f, 0f), Quaternion.identity, new Color(1f, 0.94f, 0.86f), 0.6f, 60f);
                AddLight(lights, "Room Fill", LightType.Point, new Vector3(0f, 30f, -40f), Quaternion.identity, new Color(0.88f, 0.93f, 1f), 0.8f, 95f);
                AddLight(lights, "Desk Lamp", LightType.Point, new Vector3(-56f, 6f, 9.5f), Quaternion.identity, new Color(1f, 0.84f, 0.62f), 1.5f, 18f);

                var atmosphere = root.GetComponent<ArenaAtmosphere>() ?? root.AddComponent<ArenaAtmosphere>();
                // Bright afternoon indoors: sky-blue clear colour, warm wood bounce, a faint haze on the far garden only.
                atmosphere.Configure(new Color(0.62f, 0.78f, 0.96f), new Color(0.80f, 0.86f, 0.96f), new Color(0.62f, 0.52f, 0.42f), 0.9f,
                    true, new Color(0.80f, 0.88f, 0.97f), 400f, 3600f);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            int pruned = PruneOrphanedSubAssets();
            return new { success = true, prefab = PrefabPath, root = RootName, model = ModelPath, sourceHash, arenaHash,
                triangles = objects.Values.Sum(), objects, removedRoots = removed, prunedSubAssets = pruned,
                gameplay = "unchanged; presentation only" };
        }

        private static void AddLight(Transform parent, string name, LightType type, Vector3 position, Quaternion rotation, Color color, float intensity, float range, float shadowStrength = 0f)
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
            light.shadows = shadowStrength > 0f ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = shadowStrength;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        /// <summary>Remove meshes/materials/textures embedded by the previous living-room pass.</summary>
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
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(DetailPath);
            Material Make(string file, Shader shader, Action<Material> configure)
            {
                string path = ArtPath + "/" + file + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                material.SetTexture("_BaseMap", atlas);
                material.SetColor("_BaseColor", Color.white);
                configure(material);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                return material;
            }
            void Lit(Material m, float wind, CullMode cull)
            {
                // Preserve the pale gingham and paper under the stage's daylight;
                // untinted vertex colours clip to white in the gameplay camera.
                m.SetColor("_BaseColor", new Color(0.50f, 0.50f, 0.50f, 1f));
                m.SetTexture("_DetailMap", detail);
                m.SetFloat("_DetailScale", 0.6f);
                m.SetFloat("_DetailStrength", 0.22f);
                m.SetColor("_ShadowTint", new Color(0.78f, 0.74f, 0.88f));
                m.SetFloat("_Wrap", 0.4f);
                m.SetFloat("_Specular", 0.08f);
                m.SetFloat("_Gloss", 20f);
                m.SetFloat("_Emission", 0f);
                m.SetFloat("_Wind", wind);
                m.SetFloat("_WindFrequency", 1.1f);
                m.SetVector("_WindDirection", new Vector4(0.4f, 0f, -1f, 0f));
                m.SetFloat("_Cull", (float)cull);
                m.SetFloat("_ReceiveShadowsOff", 0f);
                m.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            }
            var result = new Dictionary<string, Material>
            {
                ["TM_Build"] = Make("TableMannersBuild", lit, m => Lit(m, 0f, CullMode.Back)),
                // Curtains, bunting, balloons and the hanging blanket sway; vertex alpha is the sway weight.
                ["TM_Cloth"] = Make("TableMannersCloth", lit, m => Lit(m, 0.45f, CullMode.Off)),
                // Glitter twinkle, glow stars, lamp bulbs and the toy train (StageNeon travel).
                ["TM_Glow"] = Make("TableMannersGlow", neon, m =>
                {
                    m.SetFloat("_Base", 0.8f);
                    m.SetFloat("_Ambient", 0.45f);
                    m.SetVector("_ShadeDirection", new Vector4(-0.15f, 0.8f, 0.6f, 0f));
                    m.SetFloat("_Shade", 0.4f);
                    m.SetFloat("_Glow", 1.3f);
                    m.SetFloat("_FogAmount", 1f);
                    m.SetFloat("_TimeScale", 1f);
                    m.SetFloat("_Cull", (float)CullMode.Back);
                }),
                ["TM_Sky"] = Make("TableMannersSky", neon, m =>
                {
                    m.SetFloat("_Base", 1f);
                    m.SetFloat("_Ambient", 0f);
                    m.SetFloat("_Shade", 0f);
                    m.SetFloat("_Glow", 1f);
                    m.SetFloat("_FogAmount", 0.35f);
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
            // Secondary UV generation would overwrite TEXCOORD1, which carries StageNeon animation.
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
