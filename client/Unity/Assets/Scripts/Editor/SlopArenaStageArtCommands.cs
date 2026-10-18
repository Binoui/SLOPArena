#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
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
    /// Applies a stage's generated presentation (tools/blender/&lt;stage&gt;) to its Resources
    /// prefab from the generator's &lt;Stem&gt;.presentation.json: texture imports, materials, the
    /// FBX, shadow flags, the Stage Lights rig, atmosphere and optional poster faces. A stage needs
    /// data, not a command of its own. Presentation only: the source scene and baked arena are
    /// verified against the generator stamp and never written. Existing objects are updated in
    /// place, so re-applying unchanged data saves an identical prefab.
    /// </summary>
    public static class SlopArenaStageArtCommands
    {
        private const string LightsName = "Stage Lights";

        [CliCommand("sloparena.stage.art-apply",
            "Apply a stage's generated presentation (<Stem>.presentation.json) to its Resources prefab, creating it for a baked stage without one; verifies the generator stamp and preserves baked gameplay.",
            MainThreadRequired = true, Tags = new[] { "authoring/stage" })]
        public static object Apply(
            [CliArg("stage", "Lowercase snake_case stage key with a generator under tools/blender/<stage>.", Required = true)] string stage,
            [CliArg("from", "Generator output directory (absolute or repository-relative) to copy into the art folder first. Only files that differ are copied; the FBX and its stamp travel together.")] string from = null)
        {
            if (!Regex.IsMatch(stage ?? "", "^[a-z][a-z0-9_]{0,99}$")) throw new ArgumentException("Invalid stage key.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Apply stage art in stopped Edit Mode with the current scene saved.");
            string repository = Directory.GetParent(Path.GetFullPath(UnityCharacterAssetCooker.ProjectRoot()))?.Parent?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the repository root.");
            string prefabPath = "Assets/Resources/Stages/" + stage + ".prefab";
            // A baked stage's first pass (or a fresh checkout before its art is committed) starts from an empty root.
            bool createPrefab = !File.Exists(prefabPath);
            if (createPrefab && !File.Exists(Path.Combine(repository, "data", "arenas", stage + ".arena")))
                throw new InvalidOperationException("Missing " + prefabPath + " and data/arenas/" + stage + ".arena; create and bake the stage first.");

            // Read and verify from the source of the files before anything is copied.
            string sourceDirectory = from == null ? null : ResolveDirectory(repository, from);
            string specPath = FindPresentation(sourceDirectory ?? ArtRoot(stage), stage);
            var spec = JsonConvert.DeserializeObject<Presentation>(File.ReadAllText(specPath))
                ?? throw new InvalidOperationException("Unreadable presentation " + specPath);
            Validate(spec, stage);
            string folder = spec.folder.TrimEnd('/');
            string stampSource = Path.Combine(sourceDirectory ?? folder, spec.stamp);
            if (sourceDirectory != null && File.Exists(Path.Combine(sourceDirectory, spec.model)) != File.Exists(stampSource))
                throw new InvalidOperationException("The generated FBX and its stamp travel together; copy both or neither.");
            if (!File.Exists(stampSource)) stampSource = Path.Combine(folder, spec.stamp);
            if (!File.Exists(stampSource)) throw new InvalidOperationException("Missing generator stamp " + spec.stamp + ". Run tools/blender/" + stage + "/build.py first.");
            var stamp = JObject.Parse(File.ReadAllText(stampSource));
            string sourceScene = "Assets/Stages/" + stage + "/" + stage + ".unity";
            string sourceHash = Sha256(sourceScene);
            string arenaHash = Sha256(Path.Combine(repository, "data", "arenas", stage + ".arena"));
            if (!string.Equals(sourceHash, (string)stamp["sourceSha256"], StringComparison.OrdinalIgnoreCase)
                || !string.Equals(arenaHash, (string)stamp["arenaSha256"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated art was built from a different source scene or arena. Regenerate it with tools/blender/" + stage + "/build.py.");

            var copied = sourceDirectory == null ? new List<string>() : CopyChanged(sourceDirectory, folder, Files(spec, specPath));
            foreach (string file in Files(spec, specPath))
            {
                string path = folder + "/" + file;
                if (File.Exists(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                else if (!spec.textures.Any(t => t.file == file && t.optional))
                    throw new InvalidOperationException("Missing generated art " + path + ". Run tools/blender/" + stage + "/build.py first.");
            }

            var reimported = new List<string>();
            foreach (var texture in spec.textures)
                if (File.Exists(folder + "/" + texture.file) && ConfigureTexture(folder + "/" + texture.file, texture))
                    reimported.Add(texture.file);
            var materials = spec.materials.ToDictionary(m => m.slot, m => MakeMaterial(folder, m, setInstancing: true));
            string modelPath = folder + "/" + spec.model;
            if (ConfigureModel(modelPath, materials)) reimported.Add(spec.model);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath)
                ?? throw new InvalidOperationException("Unable to load " + modelPath);
            if (createPrefab)
            {
                var empty = new GameObject(stage);
                try { PrefabUtility.SaveAsPrefabAsset(empty, prefabPath); }
                finally { Object.DestroyImmediate(empty); }
            }
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            var removed = new List<string>();
            var objects = new Dictionary<string, int>();
            var posters = new List<object>();
            try
            {
                foreach (string name in spec.supersededRoots ?? Array.Empty<string>())
                {
                    Transform previous;
                    while ((previous = root.transform.Find(name)) != null)
                    {
                        removed.Add(name);
                        Object.DestroyImmediate(previous.gameObject);
                    }
                }
                if (spec.removeRootLights)
                    foreach (var light in root.GetComponents<Light>())
                    {
                        removed.Add("root Light");
                        Object.DestroyImmediate(light);
                    }

                var art = KeepOne(root.transform, spec.root, child => PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == model, removed);
                if (art == null)
                {
                    art = ((GameObject)PrefabUtility.InstantiatePrefab(model, root.transform)).transform;
                    art.name = spec.root;
                }
                art.localPosition = Vector3.zero;
                art.localRotation = Quaternion.identity;
                art.localScale = Vector3.one;
                if (art.GetComponentsInChildren<Collider>(true).Length > 0)
                    throw new InvalidOperationException("Generated art must not contain Colliders.");
                foreach (var renderer in art.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (spec.posters.Any(p => p.name == renderer.name)) continue;
                    var shadow = spec.shadows.objects != null && spec.shadows.objects.TryGetValue(renderer.name, out var own) ? own : spec.shadows;
                    renderer.shadowCastingMode = shadow.cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    renderer.receiveShadows = shadow.receive;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    objects[renderer.name] = mesh == null ? 0 : (int)Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i) / 3);
                    if (renderer.sharedMaterials.Any(x => x == null))
                        throw new InvalidOperationException("Unmapped generated material on " + renderer.name);
                }

                var rig = KeepOne(root.transform, LightsName, _ => true, removed);
                if (rig == null)
                {
                    rig = new GameObject(LightsName).transform;
                    rig.SetParent(root.transform, false);
                }
                ApplyLights(rig, spec.lights);

                // TryGetComponent: GetComponent returns the Editor's fake null for a missing component, which ?? keeps.
                var atmosphere = root.TryGetComponent<ArenaAtmosphere>(out var existing) ? existing : root.AddComponent<ArenaAtmosphere>();
                var air = spec.atmosphere;
                atmosphere.Configure(ToColor(air.background), ToColor(air.ambientSky), ToColor(air.ambientGround), air.ambientIntensity,
                    air.fog, ToColor(air.fogColor), air.fogStart, air.fogEnd);

                foreach (var poster in spec.posters)
                    if (File.Exists(folder + "/" + poster.texture))
                        posters.Add(InstallPoster(root, art, folder, poster));
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            int pruned = PruneOrphanedSubAssets(prefabPath);
            return new { success = true, stage, prefab = prefabPath, createdPrefab = createPrefab, presentation = specPath, root = spec.root, model = modelPath, sourceHash, arenaHash,
                copied, reimported, triangles = objects.Values.Sum(), objects, lights = spec.lights.Length, posters, removedRoots = removed,
                prunedSubAssets = pruned, gameplay = "unchanged; presentation only" };
        }

        // ------------------------------------------------------------------ spec

        private sealed class Presentation
        {
            public int format;
            public string stage, folder, model, stamp, root;
            public string[] supersededRoots;
            public bool removeRootLights;
            public TextureSpec[] textures;
            public MaterialSpec[] materials;
            public ShadowSpec shadows;
            public LightSpec[] lights;
            public AtmosphereSpec atmosphere;
            public PosterSpec[] posters = Array.Empty<PosterSpec>();
        }

        private sealed class TextureSpec
        {
            public string file, wrap = "clamp";
            public bool srgb = true, optional;
            public int maxSize = 2048, aniso = 8;
        }

        private sealed class MaterialSpec
        {
            public string slot, asset, shader, baseMap;
            public float[] baseColor;
            public Dictionary<string, string> textures;
            public Dictionary<string, float> floats;
            public Dictionary<string, float[]> colors, vectors;
            public Dictionary<string, bool> keywords;
        }

        private class ShadowSpec
        {
            public bool cast = true, receive = true;
            public Dictionary<string, ShadowSpec> objects;
        }

        private sealed class LightSpec
        {
            public string name, type = "point", shadows = "none";
            public float[] position, rotation, color;
            public SunSpec sun;
            public float intensity = 1f, range = 10f;
            public float? shadowStrength;
        }

        private sealed class SunSpec
        {
            public float azimuth, elevation;
        }

        private sealed class AtmosphereSpec
        {
            public float[] background, ambientSky, ambientGround, fogColor;
            public float ambientIntensity = 1f, fogStart = 35f, fogEnd = 100f;
            public bool fog;
        }

        private sealed class PosterSpec
        {
            public string name, texture, mesh;
            public float[] uvPixels;
            public float atlasSize, offset;
            public MaterialSpec material;
        }

        private static void Validate(Presentation spec, string stage)
        {
            if (spec.format != 1) throw new InvalidOperationException("Unsupported presentation format " + spec.format + "; expected 1.");
            if (spec.stage != stage) throw new InvalidOperationException("The presentation describes '" + spec.stage + "', not '" + stage + "'.");
            string prefix = "Assets/Stages/" + stage + "/Presentation/";
            if (spec.folder == null || !spec.folder.StartsWith(prefix, StringComparison.Ordinal) || spec.folder.Contains(".."))
                throw new InvalidOperationException("The presentation folder must be below " + prefix);
            foreach (string file in new[] { spec.model, spec.stamp }.Concat(spec.textures?.Select(t => t.file) ?? Enumerable.Empty<string>())
                .Concat(spec.posters?.Select(p => p.texture) ?? Enumerable.Empty<string>()))
                if (string.IsNullOrEmpty(file) || file.Contains('/') || file.Contains('\\'))
                    throw new InvalidOperationException("Presentation file names must be plain names inside the art folder.");
            if (string.IsNullOrWhiteSpace(spec.root) || spec.root == LightsName) throw new InvalidOperationException("The presentation needs its own root name.");
            if (spec.textures == null || spec.materials == null || spec.materials.Length == 0 || spec.shadows == null || spec.lights == null || spec.atmosphere == null)
                throw new InvalidOperationException("The presentation needs textures, materials, shadows, lights and atmosphere.");
            spec.posters ??= Array.Empty<PosterSpec>();
            var names = new HashSet<string>(spec.textures.Select(t => t.file));
            foreach (var material in spec.materials.Concat(spec.posters.Select(p => p.material)))
                foreach (string texture in new[] { material.baseMap }.Concat(material.textures?.Values ?? Enumerable.Empty<string>()))
                    if (texture != null && !names.Contains(texture) && !spec.posters.Any(p => p.texture == texture))
                        throw new InvalidOperationException("Material " + (material.slot ?? material.asset) + " uses " + texture + ", which the presentation does not list.");
            if (spec.lights.Select(l => l.name).Distinct().Count() != spec.lights.Length) throw new InvalidOperationException("Stage light names must be unique.");
        }

        private static IEnumerable<string> Files(Presentation spec, string specPath)
            => new[] { spec.model, spec.stamp, Path.GetFileName(specPath) }.Concat(spec.textures.Select(t => t.file)).Distinct();

        private static string ArtRoot(string stage) => "Assets/Stages/" + stage + "/Presentation";

        private static string FindPresentation(string directory, string stage)
        {
            if (!Directory.Exists(directory)) throw new InvalidOperationException("Missing " + directory + ". Run tools/blender/" + stage + "/build.py first.");
            var found = Directory.GetFiles(directory, "*.presentation.json", directory.EndsWith("/Presentation") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            if (found.Length != 1)
                throw new InvalidOperationException((found.Length == 0 ? "No" : "More than one") + " *.presentation.json in " + directory
                    + ". Write it with tools/blender/" + stage + "/build.py or tools/blender/stagekit/presentation.py.");
            return found[0].Replace('\\', '/');
        }

        private static string ResolveDirectory(string repository, string from)
        {
            string path = Path.GetFullPath(Path.IsPathRooted(from) ? from : Path.Combine(repository, from));
            if (!Directory.Exists(path)) throw new InvalidOperationException("Missing generator output directory " + from);
            return path;
        }

        // --------------------------------------------------------------- assets

        private static List<string> CopyChanged(string source, string folder, IEnumerable<string> files)
        {
            Directory.CreateDirectory(folder);
            var copied = new List<string>();
            foreach (string file in files)
            {
                string from = Path.Combine(source, file), to = Path.Combine(folder, file);
                if (!File.Exists(from) || (File.Exists(to) && Sha256(from) == Sha256(to))) continue;
                File.Copy(from, to, overwrite: true);
                copied.Add(file);
            }
            return copied;
        }

        /// <summary>Returns true when the import settings changed and the texture was reimported.</summary>
        private static bool ConfigureTexture(string path, TextureSpec spec)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path)
                ?? throw new InvalidOperationException("Unity did not import " + path);
            var wrap = spec.wrap == "repeat" ? TextureWrapMode.Repeat : spec.wrap == "clamp" ? TextureWrapMode.Clamp
                : throw new InvalidOperationException("Texture wrap must be clamp or repeat: " + spec.file);
            if (importer.textureType == TextureImporterType.Default && importer.sRGBTexture == spec.srgb
                && importer.alphaSource == TextureImporterAlphaSource.FromInput && !importer.alphaIsTransparency
                && importer.mipmapEnabled && importer.wrapMode == wrap && importer.anisoLevel == spec.aniso
                && importer.maxTextureSize == spec.maxSize && importer.textureCompression == TextureImporterCompression.CompressedHQ)
                return false;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = spec.srgb;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = wrap;
            importer.anisoLevel = spec.aniso;
            importer.maxTextureSize = spec.maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return true;
        }

        private static Material MakeMaterial(string folder, MaterialSpec spec, bool setInstancing)
        {
            var shader = Shader.Find(spec.shader) ?? throw new InvalidOperationException("Shader " + spec.shader + " is missing.");
            string path = folder + "/" + spec.asset + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = spec.asset };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            void Require(string property)
            {
                if (!material.HasProperty(property))
                    throw new InvalidOperationException("Material " + spec.asset + ": shader " + spec.shader + " has no property " + property + ".");
            }
            if (spec.baseMap != null) { Require("_BaseMap"); material.SetTexture("_BaseMap", LoadTexture(folder, spec.baseMap)); }
            if (spec.baseColor != null) { Require("_BaseColor"); material.SetColor("_BaseColor", ToColor(spec.baseColor)); }
            foreach (var pair in spec.textures ?? new Dictionary<string, string>()) { Require(pair.Key); material.SetTexture(pair.Key, LoadTexture(folder, pair.Value)); }
            foreach (var pair in spec.floats ?? new Dictionary<string, float>()) { Require(pair.Key); material.SetFloat(pair.Key, pair.Value); }
            foreach (var pair in spec.colors ?? new Dictionary<string, float[]>()) { Require(pair.Key); material.SetColor(pair.Key, ToColor(pair.Value)); }
            foreach (var pair in spec.vectors ?? new Dictionary<string, float[]>()) { Require(pair.Key); material.SetVector(pair.Key, ToVector(pair.Value)); }
            foreach (var pair in spec.keywords ?? new Dictionary<string, bool>())
                if (pair.Value) material.EnableKeyword(pair.Key);
                else material.DisableKeyword(pair.Key);
            if (setInstancing) material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static Texture2D LoadTexture(string folder, string file)
            => AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + file) ?? throw new InvalidOperationException("Unable to load " + folder + "/" + file);

        /// <summary>Returns true when the importer changed and the model was reimported.</summary>
        private static bool ConfigureModel(string path, Dictionary<string, Material> materials)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("Unity did not import " + path);
            var remaps = importer.GetExternalObjectMap();
            bool unchanged = importer.globalScale == 1f && importer.useFileScale && importer.importNormals == ModelImporterNormals.Import
                && importer.importTangents == ModelImporterTangents.None && !importer.importBlendShapes && !importer.importVisibility
                && !importer.importCameras && !importer.importLights && importer.animationType == ModelImporterAnimationType.None
                && !importer.importAnimation && !importer.isReadable && importer.meshCompression == ModelImporterMeshCompression.Off
                && !importer.addCollider && importer.indexFormat == ModelImporterIndexFormat.Auto && !importer.generateSecondaryUV
                && importer.materialImportMode == ModelImporterMaterialImportMode.ImportViaMaterialDescription
                && importer.materialLocation == ModelImporterMaterialLocation.InPrefab
                && materials.All(pair => remaps.TryGetValue(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), out var mapped) && mapped == pair.Value);
            if (unchanged) return false;
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
            return true;
        }

        // --------------------------------------------------------------- prefab

        /// <summary>Keep the first direct child with this name that matches; destroy the others.</summary>
        private static Transform KeepOne(Transform root, string name, Func<Transform, bool> matches, List<string> removed)
        {
            Transform kept = null;
            foreach (var child in root.Cast<Transform>().Where(x => x.name == name).ToArray())
            {
                if (kept == null && matches(child)) { kept = child; continue; }
                removed.Add(name);
                Object.DestroyImmediate(child.gameObject);
            }
            return kept;
        }

        private static void ApplyLights(Transform rig, LightSpec[] lights)
        {
            foreach (var child in rig.Cast<Transform>().Where(x => lights.All(l => l.name != x.name)).ToArray())
                Object.DestroyImmediate(child.gameObject);
            for (int i = 0; i < lights.Length; i++)
            {
                var spec = lights[i];
                var go = rig.Find(spec.name)?.gameObject;
                if (go == null) { go = new GameObject(spec.name); go.transform.SetParent(rig, false); }
                go.transform.SetSiblingIndex(i);
                var type = spec.type switch
                {
                    "directional" => LightType.Directional,
                    "point" => LightType.Point,
                    "spot" => LightType.Spot,
                    _ => throw new InvalidOperationException("Light " + spec.name + ": type must be directional, point or spot."),
                };
                go.transform.localPosition = spec.position == null ? Vector3.zero : ToVector(spec.position);
                if (spec.sun != null)
                {
                    // Shine from the sky's sun bearing: azimuth clockwise from +Z (north), elevation above the horizon.
                    var towardsSun = Quaternion.Euler(-spec.sun.elevation, spec.sun.azimuth, 0f) * Vector3.forward;
                    go.transform.localRotation = Quaternion.LookRotation(-towardsSun);
                }
                else
                    go.transform.localRotation = spec.rotation == null ? Quaternion.identity : Quaternion.Euler(spec.rotation[0], spec.rotation[1], spec.rotation[2]);
                var light = go.TryGetComponent<Light>(out var existing) ? existing : go.AddComponent<Light>();
                light.type = type;
                light.color = ToColor(spec.color ?? new[] { 1f, 1f, 1f });
                light.intensity = spec.intensity;
                if (type != LightType.Directional) light.range = spec.range;
                light.shadows = spec.shadows switch
                {
                    "none" => LightShadows.None,
                    "soft" => LightShadows.Soft,
                    "hard" => LightShadows.Hard,
                    _ => throw new InvalidOperationException("Light " + spec.name + ": shadows must be none, soft or hard."),
                };
                light.shadowStrength = spec.shadowStrength ?? 1f;
                light.renderMode = LightRenderMode.ForcePixel;
            }
        }

        /// <summary>
        /// Replace the two atlas triangles of a named UV cell with a separate face carrying a
        /// high-resolution texture, nudged along the face normal; the generated frame, AO and every
        /// other atlas cell stay as built.
        /// </summary>
        private static object InstallPoster(GameObject root, Transform art, string folder, PosterSpec spec)
        {
            float size = spec.atlasSize;
            float u0 = spec.uvPixels[0] / size, u1 = spec.uvPixels[1] / size, v0 = spec.uvPixels[2] / size, v1 = spec.uvPixels[3] / size;
            bool InCell(Vector2 p) => p.x >= u0 - .00001f && p.x <= u1 + .00001f && p.y >= v0 - .00001f && p.y <= v1 + .00001f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            Transform parent = null;
            foreach (var filter in art.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name == spec.name || filter.sharedMesh == null) continue;
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
                            throw new InvalidOperationException("More than one source mesh uses the " + spec.name + " cell.");
                        parent = filter.transform;
                        for (int k = 0; k < 3; k++)
                        {
                            int index = triangles[i + k];
                            Vector3 normal = sourceNormals[index];
                            vertices.Add(sourceVertices[index] + normal * spec.offset);
                            normals.Add(normal);
                            uv.Add(new Vector2((sourceUv[index].x - u0) / (u1 - u0), (sourceUv[index].y - v0) / (v1 - v0)));
                        }
                    }
                }
            }
            if (vertices.Count != 6 || parent == null)
                throw new InvalidOperationException($"Expected one two-triangle {spec.name} cell, found {vertices.Count / 3} triangles. Inspect the generated atlas before applying.");
            var texture = LoadTexture(folder, spec.texture);
            string meshPath = folder + "/" + spec.mesh + ".asset";
            var face = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (face == null) { face = new Mesh { name = spec.name }; AssetDatabase.CreateAsset(face, meshPath); }
            face.Clear(); face.SetVertices(vertices); face.SetNormals(normals); face.SetUVs(0, uv);
            // StageNeon reads these channels for light animation and travel; the poster is steady and still.
            var stationary = Enumerable.Repeat(Vector2.zero, vertices.Count).ToArray();
            for (int channel = 1; channel <= 3; channel++) face.SetUVs(channel, stationary);
            face.colors = Enumerable.Repeat(Color.white, 6).ToArray();
            face.triangles = Enumerable.Range(0, 6).ToArray(); face.RecalculateBounds();
            EditorUtility.SetDirty(face);
            var material = MakeMaterial(folder, spec.material, setInstancing: false);
            material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);
            var existing = root.GetComponentsInChildren<MeshFilter>(true).Where(x => x.name == spec.name).ToArray();
            var poster = existing.FirstOrDefault(x => x.transform.parent == parent)?.gameObject;
            foreach (var old in existing.Where(x => x.gameObject != poster)) Object.DestroyImmediate(old.gameObject);
            if (poster == null) { poster = new GameObject(spec.name); poster.transform.SetParent(parent, false); }
            (poster.GetComponent<MeshFilter>() ?? poster.AddComponent<MeshFilter>()).sharedMesh = face;
            var renderer = poster.GetComponent<MeshRenderer>() ?? poster.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            AssetDatabase.SaveAssetIfDirty(face);
            AssetDatabase.SaveAssetIfDirty(material);
            var center = renderer.bounds.center; var extent = renderer.bounds.size;
            return new { name = spec.name, texture = spec.texture, resolution = new[] { texture.width, texture.height }, triangles = 2,
                center = new[] { center.x, center.y, center.z }, size = new[] { extent.x, extent.y, extent.z } };
        }

        /// <summary>Remove meshes/materials/textures embedded by an earlier hand-built pass.</summary>
        private static int PruneOrphanedSubAssets(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var used = new HashSet<Object>(EditorUtility.CollectDependencies(new Object[] { prefab }));
            int pruned = 0;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(prefabPath))
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
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            }
            return pruned;
        }

        private static Color ToColor(float[] value)
        {
            if (value == null || (value.Length != 3 && value.Length != 4)) throw new InvalidOperationException("A colour needs three or four components.");
            return new Color(value[0], value[1], value[2], value.Length == 4 ? value[3] : 1f);
        }

        private static Vector4 ToVector(float[] value)
        {
            if (value == null || value.Length < 3 || value.Length > 4) throw new InvalidOperationException("A vector needs three or four components.");
            return new Vector4(value[0], value[1], value[2], value.Length == 4 ? value[3] : 0f);
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
