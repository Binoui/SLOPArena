#if UNITY_EDITOR
using System;
using System.Linq;
using SlopArena.Client.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using static SlopArena.EditorTools.PresentationFx;

namespace SlopArena.EditorTools
{
    public static class NyxManorCoursePresentationBuilder
    {
        private const string Root = "Assets/Stages/nyx_manor/Presentation/Course";
        private const string SetPath = "Assets/Resources/Courses/nyx_manor.asset";
        private const string ModelPath = Root + "/NyxManorTargets.fbx";
        private static readonly Color Pink = new(.98f, .16f, .48f);

        [CliCommand("sloparena.presentation.course.nyx-manor.build", "Build Two-Faced Manor's junk targets and comic pop effects in Edit Mode.", MainThreadRequired = true, Tags = new[] { "presentation", "arcade" })]
        public static object Build()
        {
            using var kit = PresentationBuilderKit.ForFolder(Root, "Course");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter
                ?? throw new InvalidOperationException("Run tools/blender/nyx_manor/targets.py first.");
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (string name in new[] { "ManorLit", "ManorGlow" })
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name),
                    TargetSurface(name == "ManorGlow"));
            importer.SaveAndReimport();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var portrait = PortraitMaterial();
            var hearts = kit.ParticleMaterial("NyxComicHearts", kit.BakeTexture("NyxComicHearts", 128, 128, HeartPixel));
            var scraps = kit.ParticleMaterial("NyxComicConfetti", kit.BakeTexture("NyxComicConfetti", 64, 64, (u,v) =>
            {
                float x = Mathf.Abs(u-.5f), y = Mathf.Abs(v-.5f);
                return x < .31f && y < .44f ? x > .23f || y > .36f ? new Color(.08f,.03f,.12f) : Pink : Color.clear;
            }));
            var pop = Effect(kit, "NyxInflatablePop", hearts, scraps, 22, 3f);
            var hit = Effect(kit, "NyxJunkHit", hearts, scraps, 5, 1.3f);
            var bigPop = Effect(kit, "NyxBonusPop", hearts, scraps, 44, 5f);
            var shards = kit.ParticleMaterial("NyxGothicShards", kit.BakeTexture("NyxGothicShards", 64, 64,
                (u,v) => Mathf.Abs(u-.5f)+Mathf.Abs(v-.5f) < .45f ? new Color(.55f,.42f,.72f) : Color.clear));
            var stoneBreak = Effect(kit, "NyxStoneBreak", shards, shards, 20, 2.5f);
            var stoneHit = Effect(kit, "NyxStoneHit", shards, shards, 5, 1.1f);
            var set = AssetDatabase.LoadAssetAtPath<CoursePresentationSet>(SetPath)
                ?? throw new InvalidOperationException("Missing nyx_manor course presentation.");
            var bat = Target(kit, source, "Bat", false, portrait);
            var skeleton = Target(kit, source, "Skeleton", false, portrait);
            var ring = Target(kit, source, "RingLight", false, portrait);
            set.Target = bat;
            set.TargetRadius = .6f;
            set.Break = pop;
            set.FinishBurst = Effect(kit, "NyxFinishHearts", hearts, scraps, 60, 6f);
            set.TargetLooks = new[]
            {
                Look("goth_gargoyle", Target(kit, source, "Gargoyle", true, portrait), 1f, stoneBreak, stoneHit),
                Look("goth_", Target(kit, source, "SkullCandle", false, portrait), .6f, stoneBreak, stoneHit),
                Look("yard_1", bat), Look("yard_2", skeleton), Look("yard_3", ring),
                Look("roof_1", bat), Look("roof_2", ring),
                Look("giant_bat", Target(kit, source, "GiantBat", true, portrait), 1f, bigPop, hit),
                Look("flamingo", Target(kit, source, "Flamingo", true, portrait), 1f, bigPop, hit),
                Look("cardboard_nyxxie", Target(kit, source, "Cardboard", true, portrait), 1f, bigPop, hit)
            };
            EditorUtility.SetDirty(set);
            // The existing marker, beacon, finish and sound cues remain the course's own.
            foreach (string guid in AssetDatabase.FindAssets("", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
            }
            AssetDatabase.SaveAssetIfDirty(set);
            return new { success = true, set = SetPath, looks = set.TargetLooks.Select(l => l.IdPrefix).ToArray(), colliders = 0 };
        }

        private static CoursePresentationSet.TargetLook Look(string prefix, GameObject target, float radius = .6f, GameObject brk = null, GameObject hit = null)
            => new() { IdPrefix = prefix, Target = target, TargetRadius = radius, Break = brk, Hit = hit };

        private static GameObject Target(PresentationBuilderKit kit, GameObject source, string name, bool bonus, Material portrait)
        {
            var root = kit.NewRoot("Nyx" + name, attackBound: false);
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            CopyPart(source, model, name, null);
            if (name == "Cardboard") CopyPart(source, model, "Portrait", portrait);
            if (bonus)
            {
                var actor = root.AddComponent<CourseBonusTarget>();
                actor.Model = model.transform;
                actor.Stages = new[] { model };
                actor.JoltDegrees = 10f;
                actor.IdleSwayDegrees = name == "Cardboard" ? .4f : 2f;
            }
            else
            {
                // The model faces -Z; CourseLantern's forward faces the camera.
                foreach (Transform part in model.transform) part.localRotation = Quaternion.Euler(0, 180, 0) * part.localRotation;
                var actor = root.AddComponent<CourseLantern>();
                actor.Model = model.transform;
                actor.BobHeight = .015f;
                actor.SwayDegrees = 2f;
            }
            return kit.Save(root);
        }

        private static void CopyPart(GameObject source, GameObject model, string name, Material overrideMaterial)
        {
            var part = source.transform.Find(name) ?? throw new InvalidOperationException("Missing target mesh " + name);
            var copy = new GameObject(name);
            copy.transform.SetParent(model.transform, false);
            copy.transform.localPosition = part.localPosition;
            copy.transform.localRotation = part.localRotation;
            copy.transform.localScale = part.localScale;
            copy.AddComponent<MeshFilter>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
            copy.AddComponent<MeshRenderer>().sharedMaterials = overrideMaterial != null
                ? new[] { overrideMaterial } : part.GetComponent<MeshRenderer>().sharedMaterials;
        }

        private static Material TargetSurface(bool glow)
        {
            string path = Root + (glow ? "/NyxTargetGlow.mat" : "/NyxTargetLit.mat");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            var shader = Shader.Find(glow ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Particles/Lit")
                ?? throw new InvalidOperationException("Missing vertex-colour target shader.");
            if (created) material = new Material(shader);
            else material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 0);
            material.SetFloat("_SrcBlend", 1);
            material.SetFloat("_DstBlend", 0);
            material.SetFloat("_ZWrite", 1);
            material.SetFloat("_ColorMode", 0);
            material.SetFloat("_SoftParticlesEnabled", 0);
            material.SetFloat("_CameraFadingEnabled", 0);
            if (!glow) material.SetFloat("_Smoothness", .12f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            material.SetOverrideTag("RenderType", "Opaque");
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        private static Material PortraitMaterial()
        {
            const string path = Root + "/NyxxiePortrait.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Portraits/Nyx_Fake.png")
                ?? throw new InvalidOperationException("Nyxxie's portrait is missing."));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Cull", 0);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        private static Color HeartPixel(float u, float v)
        {
            float x = (u-.5f)*2.4f, y = (v-.45f)*2.4f;
            float d = x*x+y*y-0.65f;
            float f = d*d*d-x*x*y*y*y;
            return f < 0 ? f > -.055f ? new Color(.08f,.03f,.12f) : Pink : Color.clear;
        }

        private static GameObject Effect(PresentationBuilderKit kit, string name, Material hearts, Material scraps, int count, float speed)
        {
            var root = kit.NewRoot(name, attackBound: false);
            foreach (var pair in new[] { ("Hearts", hearts), ("Confetti", scraps) })
            {
                var ps = Particles(root, pair.Item1, pair.Item2, 1.3f, scaling: ParticleSystemScalingMode.Hierarchy);
                Burst(ps, count);
                Sphere(ps, .15f);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.4f, 1.1f);
                main.startSize = new ParticleSystem.MinMaxCurve(.12f, .30f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed*.4f, speed);
                main.gravityModifier = .35f;
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI*2);
                Spin(ps, 160);
                Fade(ps, .65f);
            }
            return kit.Save(root);
        }
    }
}
#endif
