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
    /// Applies the generated After Hours neon-city presentation (see tools/blender/after_hours)
    /// to the stage prefab. Presentation only: the authoritative source scene and baked arena
    /// are verified against the generator stamp and never written.
    /// </summary>
    public static class SlopArenaAfterHoursArtCommands
    {
        private const string PrefabPath = "Assets/Resources/Stages/after_hours.prefab";
        private const string SourcePath = "Assets/Stages/after_hours/after_hours.unity";
        private const string ArtPath = "Assets/Stages/after_hours/Presentation/Neon";
        private const string ModelPath = ArtPath + "/AfterHoursNeon.fbx";
        private const string StampPath = ArtPath + "/AfterHoursNeon.layout.json";
        private const string AtlasPath = ArtPath + "/AfterHoursAtlas.png";
        private const string DetailPath = ArtPath + "/AfterHoursDetail.png";
        private const string PosterPath = ArtPath + "/AfterHoursFightNight.png";
        private const string PosterName = "After Hours Fight Night Billboard";
        private const string RootName = "After Hours Neon City";
        private const string LightsName = "Stage Lights";
        // Roots of the previous hand-built pass; the generated city replaces them.
        private static readonly string[] SupersededRoots = { "PlayableRoof", "City", "SpawnMarkers", "WarmRoofFill", "CoolRoofFill" };

        [MenuItem("Tools/SlopArena/Stages/Apply After Hours Neon Art")]
        private static void ApplyMenu() => Apply();

        [CliCommand("sloparena.stage.art-after-hours",
            "Apply the generated After Hours neon-city presentation to the stage prefab; verifies and preserves baked gameplay.",
            MainThreadRequired = true, Tags = new[] { "authoring/stage" })]
        public static object Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Apply After Hours art in stopped Edit Mode with the current scene saved.");
            if (!File.Exists(PrefabPath)) throw new InvalidOperationException("Missing " + PrefabPath + ".");
            foreach (string path in new[] { ModelPath, StampPath, AtlasPath, DetailPath })
                if (!File.Exists(path))
                    throw new InvalidOperationException("Missing generated art " + path + ". Run tools/blender/after_hours/build.py first.");

            string repository = Directory.GetParent(Path.GetFullPath(UnityCharacterAssetCooker.ProjectRoot()))?.Parent?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the repository root.");
            string arenaPath = Path.Combine(repository, "data", "arenas", "after_hours.arena");
            var stamp = JObject.Parse(File.ReadAllText(StampPath));
            string sourceHash = Sha256(SourcePath);
            string arenaHash = Sha256(arenaPath);
            if (!string.Equals(sourceHash, (string)stamp["sourceSha256"], StringComparison.OrdinalIgnoreCase)
                || !string.Equals(arenaHash, (string)stamp["arenaSha256"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated art was built from a different source scene or arena. Regenerate it with tools/blender/after_hours/build.py.");

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
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    objects[renderer.name] = mesh == null ? 0 : (int)Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i) / 3);
                    if (renderer.sharedMaterials.Any(x => x == null))
                        throw new InvalidOperationException("Unmapped generated material on " + renderer.name);
                }

                var lights = new GameObject(LightsName).transform;
                lights.SetParent(root.transform, false);
                AddLight(lights, "Moonlight", LightType.Directional, Vector3.zero, Quaternion.Euler(52f, 155f, 0f), new Color(0.66f, 0.70f, 1f), 0.3f, 0f);
                AddLight(lights, "DJ Violet", LightType.Point, new Vector3(10.5f, 4f, 0f), Quaternion.identity, new Color(0.62f, 0.38f, 1f), 4f, 14f);
                AddLight(lights, "Bar Pink", LightType.Point, new Vector3(-10.5f, 4f, 0f), Quaternion.identity, new Color(1f, 0.38f, 0.68f), 4f, 14f);
                AddLight(lights, "Dance Floor Glow", LightType.Point, new Vector3(0f, 3.5f, 0f), Quaternion.identity, new Color(1f, 0.45f, 0.8f), 2.5f, 12f);
                AddLight(lights, "Neon Row Spill", LightType.Point, new Vector3(0f, 10f, 30f), Quaternion.identity, new Color(1f, 0.72f, 0.48f), 3f, 45f);
                AddLight(lights, "River Spill", LightType.Point, new Vector3(0f, 10f, -30f), Quaternion.identity, new Color(0.45f, 0.78f, 1f), 2f, 45f);

                var atmosphere = root.GetComponent<ArenaAtmosphere>() ?? root.AddComponent<ArenaAtmosphere>();
                // Neon night: violet ambient, magenta haze that swallows the far skyline.
                atmosphere.Configure(new Color(0.06f, 0.045f, 0.12f), new Color(0.42f, 0.36f, 0.62f), new Color(0.10f, 0.08f, 0.14f), 1f,
                    true, new Color(0.22f, 0.10f, 0.24f), 30f, 650f);

                if (File.Exists(PosterPath)) InstallPoster(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            int pruned = PruneOrphanedSubAssets();
            return new { success = true, prefab = PrefabPath, root = RootName, model = ModelPath, sourceHash, arenaHash,
                triangles = objects.Values.Sum(), objects, removedRoots = removed, prunedSubAssets = pruned,
                gameplay = "unchanged; presentation only" };
        }

        [CliCommand("sloparena.stage.billboard-after-hours", "Replace only the existing After Hours arena screen face with the high-resolution roster poster; preserves its frame and gameplay.",
            MainThreadRequired = true, Tags = new[] { "authoring/stage" })]
        public static object ApplyBillboard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Apply the billboard in stopped Edit Mode with saved scenes.");
            if (!File.Exists(PosterPath)) throw new InvalidOperationException("Missing " + PosterPath);
            ConfigureTexture(PosterPath, true, TextureWrapMode.Clamp, 2048);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var result = InstallPoster(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return result;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static object InstallPoster(GameObject root)
        {
            // The named arena_tonight cell in textures.py: a 256 px screen at (1024,256)
            // in the 2048 atlas, with its authored two-texel UV inset. Extract only those
            // two triangles; retain the generated city, AO, frame and all other atlas cells.
            const float u0 = 1026f / 2048f, u1 = 1278f / 2048f;
            const float v0 = 1538f / 2048f, v1 = 1790f / 2048f;
            bool InCell(Vector2 p) => p.x >= u0 - .00001f && p.x <= u1 + .00001f
                && p.y >= v0 - .00001f && p.y <= v1 + .00001f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            Transform parent = null;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name == PosterName || filter.sharedMesh == null) continue;
                var mesh = filter.sharedMesh;
                var sourceUv = mesh.uv;
                if (sourceUv.Length != mesh.vertexCount) continue;
                var sourceVertices = mesh.vertices;
                var sourceNormals = mesh.normals;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] triangles = mesh.GetTriangles(sub);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        if (!InCell(sourceUv[triangles[i]]) || !InCell(sourceUv[triangles[i + 1]]) || !InCell(sourceUv[triangles[i + 2]])) continue;
                        if (parent != null && parent != filter.transform)
                            throw new InvalidOperationException("More than one source mesh uses the arena screen cell.");
                        parent = filter.transform;
                        for (int k = 0; k < 3; k++)
                        {
                            int index = triangles[i + k];
                            Vector3 normal = sourceNormals[index];
                            vertices.Add(sourceVertices[index] + normal * .015f);
                            normals.Add(normal);
                            uv.Add(new Vector2((sourceUv[index].x - u0) / (u1 - u0), (sourceUv[index].y - v0) / (v1 - v0)));
                        }
                    }
                }
            }
            if (vertices.Count != 6 || parent == null)
                throw new InvalidOperationException($"Expected one two-triangle arena screen, found {vertices.Count / 3} triangles. Inspect the generated atlas before applying.");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PosterPath)
                ?? throw new InvalidOperationException("Import the fight-night poster texture first.");
            string meshPath = ArtPath + "/AfterHoursFightNightFace.asset";
            var face = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (face == null) { face = new Mesh { name = PosterName }; AssetDatabase.CreateAsset(face, meshPath); }
            face.Clear(); face.SetVertices(vertices); face.SetNormals(normals); face.SetUVs(0, uv);
            // StageNeon reads these channels for light animation and traffic travel.
            // Supply explicit steady/stationary values on this fixed poster face.
            var stationary = Enumerable.Repeat(Vector2.zero, vertices.Count).ToArray();
            for (int channel = 1; channel <= 3; channel++) face.SetUVs(channel, stationary);
            face.colors = Enumerable.Repeat(Color.white, 6).ToArray();
            face.triangles = Enumerable.Range(0, 6).ToArray(); face.RecalculateBounds();
            EditorUtility.SetDirty(face);
            string materialPath = ArtPath + "/AfterHoursFightNight.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("SlopArena/StageNeon")) { name = "AfterHoursFightNight" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
            Night(material, glow: .75f, fog: 1f);
            material.SetFloat("_TimeScale", 0f);
            EditorUtility.SetDirty(material);
            foreach (var old in root.GetComponentsInChildren<MeshFilter>(true).Where(x => x.name == PosterName).ToArray())
                Object.DestroyImmediate(old.gameObject);
            var poster = new GameObject(PosterName);
            poster.transform.SetParent(parent, false);
            poster.AddComponent<MeshFilter>().sharedMesh = face;
            var renderer = poster.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            AssetDatabase.SaveAssets();
            var center = renderer.bounds.center; var size = renderer.bounds.size;
            return new { success = true, texture = PosterPath, resolution = new[] { texture.width, texture.height }, triangles = 2,
                center = new[] { center.x, center.y, center.z }, size = new[] { size.x, size.y, size.z } };
        }

        private static void AddLight(Transform parent, string name, LightType type, Vector3 position, Quaternion rotation, Color color, float intensity, float range)
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
            var lit = Shader.Find("SlopArena/StageVertexLit") ?? throw new InvalidOperationException("Shader SlopArena/StageVertexLit is missing.");
            var neon = Shader.Find("SlopArena/StageNeon") ?? throw new InvalidOperationException("Shader SlopArena/StageNeon is missing.");
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
            var result = new Dictionary<string, Material>
            {
                ["AH_Build"] = Make("AfterHoursBuild", lit, m =>
                {
                    m.SetTexture("_DetailMap", detail);
                    m.SetFloat("_DetailScale", 0.5f);
                    m.SetFloat("_DetailStrength", 0.25f);
                    m.SetColor("_ShadowTint", new Color(0.55f, 0.5f, 0.75f));
                    m.SetFloat("_Wrap", 0.35f);
                    m.SetFloat("_Specular", 0.2f);
                    m.SetFloat("_Gloss", 30f);
                    m.SetFloat("_Emission", 0f);
                    m.SetFloat("_Wind", 0f);
                    m.SetFloat("_Cull", (float)CullMode.Back);
                    m.SetFloat("_ReceiveShadowsOff", 1f);
                    m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                }),
                // Glow stays near 1.4 so neon keeps its hue under the match's ACES grade.
                ["AH_Night"] = Make("AfterHoursNight", neon, m => Night(m, glow: 1.4f, fog: 1f)),
                ["AH_Far"] = Make("AfterHoursFar", neon, m => Night(m, glow: 1.6f, fog: 0.45f)),
                ["AH_Sky"] = Make("AfterHoursSky", neon, m =>
                {
                    m.SetFloat("_Base", 1f);
                    m.SetFloat("_Ambient", 0f);
                    m.SetFloat("_Shade", 0f);
                    m.SetFloat("_Glow", 1f);
                    m.SetFloat("_FogAmount", 0f);
                    m.SetFloat("_TimeScale", 1f);
                    m.SetFloat("_Cull", (float)CullMode.Off);
                }),
            };
            AssetDatabase.SaveAssets();
            return result;
        }

        private static void Night(Material m, float glow, float fog)
        {
            m.SetFloat("_Base", 0.42f);
            m.SetFloat("_Ambient", 0.5f);
            m.SetVector("_ShadeDirection", new Vector4(0.35f, 0.8f, 0.45f, 0f));
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
            // Secondary UV generation would overwrite TEXCOORD1, which carries sign animation.
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
