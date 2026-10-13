using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace SlopArena.Client.Editor
{
    /// <summary>
    /// Regenerates local SlopArena movement-VFX variants from Cartoon FX Remaster Free.
    /// Cartoon FX is an optional Asset Store dependency and is never committed.
    /// </summary>
    public static class SetupCartoonMovementVfx
    {
        private const string Destination = "Assets/Resources/VFX/SlopArena";

        [MenuItem("Tools/SlopArena/Prototypes/Setup Cartoon Movement VFX")]
        public static void Setup()
        {
            string poofSource = FindPrefab("CFXR Magic Poof");
            string windSource = FindPrefab("CFXR4 Wind Trails");
            string ringSource = FindPrefab("CFXR Water Ripples");
            string groundSource = FindPrefab("CFXR2 Ground Hit");
            string smashSource = FindPrefab("CFXR Hit A (Red) + Text") ?? FindPrefab("CFXR _SMASH_");
            if (poofSource == null || windSource == null || ringSource == null || groundSource == null)
                throw new InvalidOperationException(
                    "Cartoon FX Remaster Free is missing. Import it from the Unity Asset Store first.");

            ConfigureShaders(poofSource);
            Directory.CreateDirectory(Destination);
            Copy(poofSource, "MovementMagicPoof");
            Copy(windSource, "MovementWindTrails");
            Copy(ringSource, "MovementAirRing");
            Copy(groundSource, "MovementGroundHit");
            if (smashSource != null)
                Copy(smashSource, "MatchTextSmash");
            TunePoof();
            TuneWind();
            TuneRing();
            TuneGroundRing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SlopArena] Cartoon movement VFX generated under {Destination}.");
        }


        private static string FindPrefab(string name)
        {
            return AssetDatabase.FindAssets($"{name} t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path) == name);
        }

        private static void ConfigureShaders(string sourcePrefab)
        {
            const string marker = "/CFXR Prefabs/";
            int markerIndex = sourcePrefab.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return;

            string packageRoot = sourcePrefab.Substring(0, markerIndex);
            string shaderDirectory = $"{packageRoot}/CFXR Assets/Shaders";
            string settingsPath = $"{shaderDirectory}/CFXR_SETTINGS.cginc";
            if (File.Exists(settingsPath))
            {
                string settings = File.ReadAllText(settingsPath);
                const string disabled = "// #define GLOBAL_DISABLE_SOFT_PARTICLES";
                if (settings.Contains(disabled))
                    File.WriteAllText(settingsPath,
                        settings.Replace(disabled, "#define GLOBAL_DISABLE_SOFT_PARTICLES"));
            }

            if (!Directory.Exists(shaderDirectory))
                return;
            foreach (string shader in Directory.GetFiles(shaderDirectory, "*.cfxrshader"))
                AssetDatabase.ImportAsset(shader, ImportAssetOptions.ForceUpdate);
        }

        private static void Copy(string source, string destinationName, bool removeRuntimeEffect = true)
        {
            string destination = $"{Destination}/{destinationName}.prefab";
            AssetDatabase.DeleteAsset(destination);
            if (!AssetDatabase.CopyAsset(source, destination))
                throw new InvalidOperationException($"Could not copy {source} to {destination}.");

            GameObject root = PrefabUtility.LoadPrefabContents(destination);
            if (removeRuntimeEffect)
            {
                foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true)
                             .Where(component => component != null
                                 && component.GetType().FullName == "CartoonFX.CFXR_Effect"))
                    UnityEngine.Object.DestroyImmediate(component, true);
            }
            PrefabUtility.SaveAsPrefabAsset(root, destination);
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static void TunePoof()
        {
            EditPrefab("MovementMagicPoof", root =>
            {
                foreach (string childName in new[] { "Stars", "Lines" })
                {
                    Transform child = root.transform.Find(childName);
                    if (child != null)
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                }

                ParticleSystem particles = root.GetComponent<ParticleSystem>();
                var main = particles.main;
                main.loop = false;
                main.duration = 0.35f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.56f);
                main.gravityModifier = 0.08f;
                main.maxParticles = 16;
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.18f;
                var emission = particles.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 10, 14) });
            });
        }

        private static void TuneWind()
        {
            EditPrefab("MovementWindTrails", root =>
            {
                ParticleSystem particles = root.GetComponent<ParticleSystem>();
                var main = particles.main;
                main.loop = false;
                main.duration = 0.16f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(0.68f, 0.86f, 1f, 0.8f),
                    new Color(0.9f, 0.96f, 1f, 1f));
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.9f, 0.15f, 0.1f);
                var emission = particles.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 12) });
                var trails = particles.trails;
                trails.lifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            });
        }

        private static void TuneRing()
        {
            EditPrefab("MovementAirRing", root =>
            {
                ParticleSystem particles = root.GetComponent<ParticleSystem>();
                var main = particles.main;
                main.loop = false;
                main.duration = 0.3f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.4f);
                main.startColor = new Color(0.45f, 0.9f, 1f, 0.9f);
            });
        }

        private static void TuneGroundRing()
        {
            EditPrefab("MovementGroundHit", root =>
            {
                ParticleSystem rootParticles = root.GetComponent<ParticleSystem>();
                if (rootParticles != null)
                    UnityEngine.Object.DestroyImmediate(rootParticles);
                ParticleSystemRenderer rootRenderer = root.GetComponent<ParticleSystemRenderer>();
                if (rootRenderer != null)
                    UnityEngine.Object.DestroyImmediate(rootRenderer);

                ParticleSystem ring = root.transform.Find("Ground ring")?.GetComponent<ParticleSystem>();
                if (ring == null)
                    return;
                var main = ring.main;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.25f);
                main.startColor = new Color(0.72f, 0.84f, 0.96f, 0.85f);
            });
        }


        private static void EditPrefab(string name, Action<GameObject> edit)
        {
            string path = $"{Destination}/{name}.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
