#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Client.Animation;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// One rebuild of a character package's generated combat presentation
    /// (skills/sloparena-character-vfx-pass). It writes textures, materials, meshes and prefabs
    /// under <c>CharacterPackages/&lt;package&gt;/Presentation</c>, keeping their GUIDs and,
    /// for unchanged inputs, their bytes. It then binds them to the package catalog, the
    /// catalog's weapon-trail style and the projectile visual config. Effects are built in a
    /// preview scene that closes on <see cref="Finish"/> or disposal.
    /// Pair with <see cref="PresentationFx"/> and <see cref="ToonShapes"/>.
    /// </summary>
    public sealed class PresentationBuilderKit : IDisposable
    {
        public const string ProjectileConfigPath = "Assets/Resources/VFXConfigs/ProjectileVisuals.asset";
        private const string MixerPath = "Assets/Resources/Settings/SlopArena.mixer";

        public string PackageId { get; }
        public string PackageRoot { get; }
        /// <summary>Generated assets; rebuilds overwrite them in place.</summary>
        public string Root { get; }
        /// <summary>Local-only clips (Resources/Audio is ignored by Git).</summary>
        public string AudioRoot { get; }
        public List<string> Warnings { get; } = new();

        private readonly Scene _scene;
        private bool _sceneOpen;

        public PresentationBuilderKit(string packageId, string audioFolder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException($"Build {packageId}'s presentation in stopped, compiled Edit Mode.");
            PackageId = packageId;
            PackageRoot = $"Assets/CharacterPackages/{packageId}";
            Root = $"{PackageRoot}/Presentation";
            AudioRoot = $"Assets/Resources/Audio/SFX/{audioFolder}";
            if (!AssetDatabase.IsValidFolder(PackageRoot))
                throw new InvalidOperationException($"No character package at {PackageRoot}.");
            if (!AssetDatabase.IsValidFolder(Root))
                AssetDatabase.CreateFolder(PackageRoot, "Presentation");
            _scene = EditorSceneManager.NewPreviewScene();
            _sceneOpen = true;
        }

        // ── Assets ───────────────────────────────────────────────────────────────

        /// <summary>Bakes <paramref name="pixel"/>(u, v) at texel centres into a clamped sRGB PNG.</summary>
        public Texture2D BakeTexture(string name, int width, int height, Func<float, float, Color> pixel)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = pixel((x + 0.5f) / width, (y + 0.5f) / height);
            texture.SetPixels(pixels);
            texture.Apply();
            string path = $"{Root}/{name}.png";
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// URP Particles/Unlit, alpha-blended by default: toon shapes stay readable on bright
        /// stages, where additive glow washes out to white. Keep additive for soft halos. The
        /// slight HDR tint lets white cores catch the match bloom. Both render just after the
        /// regular transparent queue, so sword-trail ribbons never cover a cut or a hit; additive
        /// halos go one step earlier to stay under the shapes.
        /// </summary>
        public Material ParticleMaterial(string name, Texture texture, Color? tint = null, bool additive = false)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? throw new InvalidOperationException("URP Particles/Unlit is unavailable.");
            string path = $"{Root}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(shader);
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", tint ?? new Color(1.2f, 1.2f, 1.2f, 1f));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_SrcBlendAlpha", 1f);
            material.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent + (additive ? 9 : 10);
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("SHADOWCASTER", false);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Saves a generated mesh as <c>&lt;mesh.name&gt;.asset</c>, updating an existing asset in place.</summary>
        public Mesh SaveMesh(Mesh mesh)
        {
            string path = $"{Root}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ── Prefabs ──────────────────────────────────────────────────────────────

        /// <summary>
        /// A prefab root in the preview scene. Attack-bound roots end with their attack
        /// (interruption, replacement, owner removal); use them for bone-attached cues.
        /// </summary>
        public GameObject NewRoot(string name, bool attackBound)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, _scene);
            if (attackBound) root.AddComponent<AttackBoundPresentation>();
            return root;
        }

        /// <summary>
        /// Nests a model asset under <paramref name="root"/>, or warns and returns null when the
        /// asset is missing (character art under Assets/Art is local-only).
        /// </summary>
        public GameObject NestModel(GameObject root, string assetPath, string name)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (model == null)
            {
                Warnings.Add($"{root.name}: missing local model {assetPath}; it builds without the model.");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, _scene);
            instance.name = name;
            instance.transform.SetParent(root.transform, false);
            return instance;
        }

        /// <summary>
        /// A one-shot 2D AudioSource on the SFX mixer group. It plays when the cue spawns and dies
        /// with the instance, so the cue's durationTicks must outlast the clip.
        /// </summary>
        public void Sound(GameObject root, string clipName, float volume, float pitch)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioRoot}/{clipName}.ogg");
            if (clip == null)
            {
                // Resources/Audio is a local-only folder; a missing clip leaves the effect silent.
                Warnings.Add($"{root.name}: missing local clip {AudioRoot}/{clipName}.ogg");
                return;
            }
            var source = root.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = true;
            source.loop = false;
            source.volume = volume;
            source.pitch = pitch;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = AssetDatabase.LoadAllAssetsAtPath(MixerPath)
                .OfType<AudioMixerGroup>().FirstOrDefault(group => group.name == "SFX");
            if (source.outputAudioMixerGroup == null)
                Warnings.Add($"{root.name}: SFX mixer group not found in {MixerPath}");
        }

        public GameObject Save(GameObject root)
        {
            // Unity serializes a MonoBehaviour only through a script file named after its class;
            // anything else saves as a missing script and silently does nothing at runtime.
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (MonoScript.FromMonoBehaviour(behaviour) == null)
                    throw new InvalidOperationException(
                        $"{root.name}: {behaviour.GetType().Name} cannot be saved in a prefab; move it to {behaviour.GetType().Name}.cs.");
            string path = $"{Root}/{root.name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            Object.DestroyImmediate(root);
            if (!saved || prefab == null)
                throw new InvalidOperationException($"Could not save {path}.");
            return prefab;
        }

        // ── Bindings ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Points every trail-carrying entry of the catalog's weapon config at
        /// <paramref name="style"/>, a SwordSweepTrail style prefab (root ParticleSystem material,
        /// tint and fade; optional "Dots", "Lines" and "Background slash" children).
        /// </summary>
        public void BindWeaponTrail(GameObject style)
        {
            var weapon = LoadCatalog().WeaponConfig
                ?? throw new InvalidOperationException($"{PackageId}'s catalog has no weapon config.");
            int bound = 0;
            foreach (var entry in weapon.Entries ?? Array.Empty<WeaponEntry>())
            {
                if (entry?.TrailStylePrefab == null) continue;
                entry.TrailStylePrefab = style;
                bound++;
            }
            if (bound == 0) Warnings.Add($"{AssetDatabase.GetAssetPath(weapon)}: no entry carries a sword trail.");
            EditorUtility.SetDirty(weapon);
        }

        /// <summary>
        /// Points the ground and air projectile visuals of one slot at <paramref name="prefab"/>.
        /// ProjectileVFXManager places the root on the Shared projectile; orientation and scale
        /// rules are in its OrientProjectile and ScaleProjectileVisual.
        /// </summary>
        public void BindProjectile(CharacterClass character, byte slot, GameObject prefab, float scale = 1f)
        {
            var config = AssetDatabase.LoadAssetAtPath<ProjectileVFXConfig>(ProjectileConfigPath)
                ?? throw new InvalidOperationException("The projectile visual config is missing.");
            int bound = 0;
            foreach (var entry in config.ProjectileEntries ?? Array.Empty<ProjectileVisualEntry>())
            {
                if (entry == null || entry.Character != character || entry.AttackSlot != slot) continue;
                entry.Prefab = prefab;
                entry.Scale = scale;
                bound++;
            }
            if (bound == 0) Warnings.Add($"{ProjectileConfigPath}: no {character} slot {slot} entry.");
            EditorUtility.SetDirty(config);
        }

        /// <summary>
        /// Replaces the catalog's <c>presentation.&lt;package&gt;.*</c> bindings with
        /// <paramref name="built"/> and saves. The refresh imports an edited character.json with
        /// the catalog, so the package cooks once.
        /// </summary>
        public object Finish(IReadOnlyList<(string id, GameObject prefab)> built, object outputs = null)
        {
            CloseScene();
            string prefix = $"presentation.{PackageId}.";
            foreach (var (id, _) in built)
                if (!id.StartsWith(prefix, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Presentation id '{id}' must start with '{prefix}'.");
            var catalog = LoadCatalog();
            var bindings = (catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
                .Where(binding => binding != null && !binding.SemanticId.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            foreach (var (id, prefab) in built)
                bindings.Add(new CharacterAssetCatalog.PresentationBinding { SemanticId = id, Prefab = prefab });
            catalog.Presentations = bindings.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return new
            {
                success = true, root = Root,
                bindings = built.Select(x => new { x.id, prefab = AssetDatabase.GetAssetPath(x.prefab) }).ToArray(),
                outputs, warnings = Warnings.ToArray()
            };
        }

        public void Dispose() => CloseScene();

        private CharacterAssetCatalog LoadCatalog()
            => AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>($"{PackageRoot}/CharacterAssetCatalog.asset")
                ?? throw new InvalidOperationException($"{PackageId}'s CharacterAssetCatalog is missing.");

        private void CloseScene()
        {
            if (!_sceneOpen) return;
            _sceneOpen = false;
            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }
}
#endif
