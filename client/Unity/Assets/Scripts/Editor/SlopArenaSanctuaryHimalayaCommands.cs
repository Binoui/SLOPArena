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
    /// Applies the generated Himalayan Sanctuary presentation (see
    /// tools/blender/fightguy_sanctuary) to the course prefab. Presentation only: the
    /// authoritative source scene, baked arena and gameplay scene are never touched.
    /// </summary>
    public static class SlopArenaSanctuaryHimalayaCommands
    {
        private const string PrefabPath = "Assets/Resources/Stages/fightguy_sanctuary.prefab";
        private const string SourcePath = "Assets/Stages/fightguy_sanctuary/fightguy_sanctuary.unity";
        private const string ArtPath = "Assets/Stages/fightguy_sanctuary/Presentation/Himalaya";
        private const string ModelPath = ArtPath + "/SanctuaryHimalaya.fbx";
        private const string StampPath = ArtPath + "/SanctuaryHimalaya.layout.json";
        private const string AtlasPath = ArtPath + "/SanctuaryAtlas.png";
        private const string DetailPath = ArtPath + "/SanctuaryDetail.png";
        private const string RootName = "Himalayan Sanctuary";
        private const string SupersededSample = "Cloudtop First Crossing";
        private const string ShaderName = "SlopArena/StageVertexLit";
        // Gameplay cues stay owned by the course greybox presentation.
        private static readonly string[] CueNames = { "Checkpoint 1", "Checkpoint 2", "Checkpoint 3", "Finish", "Route Arrow" };
        private static readonly string[] BackdropObjects = { "Backdrop Peaks", "Backdrop Spires", "Mountain Floor", "Cloud Sea" };

        [MenuItem("Tools/SlopArena/Arcade/Phase 5/Apply Himalayan Sanctuary Art")]
        private static void ApplyMenu() => Apply();

        [CliCommand("sloparena.arcade.course.art-himalaya",
            "Apply the generated Himalayan Sanctuary presentation to the existing course prefab; preserve baked gameplay.",
            MainThreadRequired = true, Tags = new[] { "arcade", "authoring/stage" })]
        public static object Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Apply sanctuary art in stopped Edit Mode with the current scene saved.");
            if (!File.Exists(PrefabPath)) throw new InvalidOperationException("Prepare the course before applying its art.");
            foreach (string path in new[] { ModelPath, StampPath, AtlasPath, DetailPath })
                if (!File.Exists(path))
                    throw new InvalidOperationException("Missing generated art " + path + ". Run tools/blender/fightguy_sanctuary/build.py first.");
            string sourceHash = Sha256(SourcePath);
            string stampHash = (string)JObject.Parse(File.ReadAllText(StampPath))["sourceSha256"];
            if (!string.Equals(sourceHash, stampHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated art was built from a different source scene. Regenerate it with tools/blender/fightguy_sanctuary/build.py.");

            AssetDatabase.Refresh();
            ConfigureTexture(AtlasPath, srgb: true, TextureWrapMode.Clamp, 2048);
            ConfigureTexture(DetailPath, srgb: false, TextureWrapMode.Repeat, 512);
            var materials = MakeMaterials();
            ConfigureModel(materials);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)
                ?? throw new InvalidOperationException("Unable to load " + ModelPath);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                foreach (string name in new[] { SupersededSample, RootName })
                {
                    var previous = root.transform.Find(name);
                    if (previous != null) Object.DestroyImmediate(previous.gameObject);
                }
                var hidden = new List<string>();
                foreach (Transform child in root.transform)
                {
                    if (CueNames.Contains(child.name)) continue;
                    var renderer = child.GetComponent<MeshRenderer>();
                    if (renderer == null || !renderer.enabled) continue;
                    renderer.enabled = false;
                    hidden.Add(child.name);
                }

                var art = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                art.name = RootName;
                art.transform.localPosition = Vector3.zero;
                art.transform.localRotation = Quaternion.identity;
                art.transform.localScale = Vector3.one;
                if (art.GetComponentsInChildren<Collider>(true).Length > 0)
                    throw new InvalidOperationException("Generated art must not contain Colliders.");
                var objects = new Dictionary<string, int>();
                foreach (var renderer in art.GetComponentsInChildren<MeshRenderer>(true))
                {
                    bool backdrop = BackdropObjects.Contains(renderer.name);
                    renderer.shadowCastingMode = backdrop ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    renderer.receiveShadows = renderer.name != "Cloud Sea";
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    objects[renderer.name] = mesh == null ? 0 : (int)Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i) / 3);
                    if (renderer.sharedMaterials.Any(x => x == null))
                        throw new InvalidOperationException("Unmapped generated material on " + renderer.name);
                }

                var atmosphere = root.GetComponent<ArenaAtmosphere>() ?? root.AddComponent<ArenaAtmosphere>();
                // Clear high-altitude morning: deep sky, cool haze that swallows the far ranges.
                atmosphere.Configure(new Color(0.50f, 0.68f, 0.88f), new Color(0.60f, 0.68f, 0.80f), new Color(0.28f, 0.26f, 0.25f), 0.85f,
                    true, new Color(0.72f, 0.80f, 0.90f), 60f, 520f);
                var localLight = root.transform.Find("LocalLighting")?.GetComponent<Light>();
                if (localLight != null) { localLight.intensity = 0.22f; localLight.color = new Color(1f, 0.90f, 0.76f); }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return new { success = true, prefab = PrefabPath, root = RootName, model = ModelPath, sourceHash,
                    triangles = objects.Values.Sum(), objects, hiddenGreybox = hidden, supersededSample = SupersededSample,
                    gameplay = "unchanged; presentation only" };
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Dictionary<string, Material> MakeMaterials()
        {
            var shader = Shader.Find(ShaderName) ?? throw new InvalidOperationException("Shader " + ShaderName + " is missing.");
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(DetailPath);
            Material Make(string slot, string file, Action<Material> configure)
            {
                string path = ArtPath + "/" + file + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                material.SetTexture("_BaseMap", atlas);
                material.SetTexture("_DetailMap", detail);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_DetailScale", 0.45f);
                material.SetFloat("_DetailStrength", 0.34f);
                material.SetColor("_ShadowTint", new Color(0.70f, 0.73f, 0.83f));
                material.SetFloat("_Wrap", 0.2f);
                material.SetFloat("_Specular", 0f);
                material.SetFloat("_Gloss", 24f);
                material.SetFloat("_Emission", 0f);
                material.SetFloat("_Wind", 0f);
                material.SetFloat("_Cull", (float)CullMode.Back);
                material.SetFloat("_ReceiveShadowsOff", 0f);
                material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
                configure(material);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                return material;
            }
            var result = new Dictionary<string, Material>
            {
                ["SA_Stone"] = Make("SA_Stone", "HimalayaStone", m => { }),
                ["SA_Gloss"] = Make("SA_Gloss", "HimalayaGilt", m => { m.SetFloat("_DetailStrength", 0.1f); m.SetFloat("_Specular", 1.1f); m.SetFloat("_Gloss", 36f); m.SetFloat("_Wrap", 0.1f); }),
                ["SA_Cloth"] = Make("SA_Cloth", "HimalayaCloth", m => { m.SetFloat("_DetailStrength", 0.12f); m.SetFloat("_Wrap", 0.5f); m.SetFloat("_Wind", 0.16f); m.SetFloat("_Cull", (float)CullMode.Off); }),
                ["SA_Foliage"] = Make("SA_Foliage", "HimalayaFoliage", m => { m.SetFloat("_DetailStrength", 0.2f); m.SetFloat("_Wrap", 0.4f); m.SetFloat("_Wind", 0.05f); }),
                ["SA_Glow"] = Make("SA_Glow", "HimalayaGlow", m => { m.SetFloat("_Emission", 2.2f); m.SetFloat("_DetailStrength", 0f); }),
                ["SA_Cloud"] = Make("SA_Cloud", "HimalayaCloud", m =>
                {
                    m.SetFloat("_DetailScale", 0.03f); m.SetFloat("_DetailStrength", 0f); m.SetFloat("_Wrap", 0.85f);
                    m.SetColor("_ShadowTint", new Color(0.86f, 0.9f, 1f)); m.SetFloat("_ReceiveShadowsOff", 1f); m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
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
            importer.mipmapEnabled = true;
            importer.wrapMode = wrap;
            importer.anisoLevel = 4;
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
