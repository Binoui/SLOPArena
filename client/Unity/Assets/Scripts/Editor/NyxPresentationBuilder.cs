#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Animation;
using SlopArena.Client.Combat;
using SlopArena.Client.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static SlopArena.EditorTools.PresentationFx;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class NyxPresentationBuilder
    {
        private const string Root = "Assets/CharacterPackages/nyx";
        private static PresentationBuilderKit _kit;
        private static Material _paper, _contact;
        private static readonly Dictionary<string, Mesh> Meshes = new(StringComparer.Ordinal);

        [CliCommand("sloparena.presentation.nyx.build", "Build Nyx's approved cut-paper shadows, crow, bats, Grasp, swaps and pink prop effects, preserving prop bindings and rig.",
            MainThreadRequired = true, Tags = new[] { "presentation", "nyx" })]
        public static object Build()
        {
            using var kit = new PresentationBuilderKit("nyx", "Nyx");
            _kit = kit;
            Meshes.Clear();
            _paper = Material("NyxPaper", false);
            _contact = Material("NyxPaperContact", true);
            var built = new List<(string id, GameObject prefab)>();
            void Bind(string id, GameObject prefab) => built.Add(("presentation.nyx." + id, prefab));
            // Finish replaces this package prefix. Include the three existing prop bindings explicitly.
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(Root + "/CharacterAssetCatalog.asset");
            foreach (var id in new[] { "pole-held", "phone-raise", "phone-drop" })
            {
                string semantic = "presentation.nyx.props." + id;
                var binding = catalog.Presentations.Single(x => x.SemanticId == semantic);
                if (binding.Prefab == null) throw new InvalidOperationException("Missing approved prop binding " + semantic);
                built.Add((semantic, binding.Prefab));
            }
            Bind("shadow-claws", Normal("NyxShadowClaws", "NyxShadowClaws", 8, 14));
            Bind("rising-hands", Normal("NyxRisingHands", "NyxRisingHands", 14, 25));
            Bind("floor-lash", Normal("NyxFloorLash", "NyxFloorLash", 24, 32));
            Bind("shadow-ring", Normal("NyxShadowRing", "NyxShadowRing", 8, 19, turn: 12f));
            Bind("up-claw", Normal("NyxUpClaw", "NyxUpClaw", 10, 18));
            Bind("spike-fist", Normal("NyxSpikeFist", "NyxSpikeFist", 20, 28));
            Bind("phone-slaps", Slaps());
            Bind("pole-uppercut", Normal("NyxPoleUppercut", "NyxPoleUpperArc", 16, 25));
            Bind("stomp-impact", Stomp());
            Bind("spin-arc", Normal("NyxSpinArc", "NyxSpinArc", 7, 17, turn: 20f));
            Bind("pole-overhead", Normal("NyxPoleOverhead", "NyxPoleOverheadArc", 12, 23));
            Bind("dropkick-spike", Normal("NyxDropkickSpike", "NyxDropkickSpike", 12, 25));
            Bind("ring-light", Normal("NyxRingLight", "NyxFlashCone", 10, 15));
            Bind("rotor-arcs", Rotor());
            Bind("selfie-pop", Selfie());
            Bind("selfie-release", Hit("NyxSelfieRelease", true, false));
            Bind("shadow-hit", Hit("NyxShadowHit", false, false));
            Bind("pink-hit", Hit("NyxPinkHit", true, false));
            Bind("crow-pop", Hit("NyxCrowPop", false, true));
            Bind("crow-launch", CrowLaunch());
            var crow = Crow();
            Bind("crow", crow);
            kit.BindProjectile("ground.A", crow);
            kit.BindProjectile("air.A", crow);
            Bind("bat-scatter", Bats());
            Bind("going-live", Swap(true));
            Bind("logging-off", Swap(false));
            var drag = GraspDrag();
            Bind("grasp-drag", drag);
            Bind("grasp-catch", GraspCatch(drag));
            Bind("grasp-warning", GraspWarning());
            return kit.Finish(built, new { meshes = Meshes.Count, noSmoke = true, noBloom = true, noCameraShake = true });
        }

        private static Material Material(string name, bool inFront)
        {
            string texturePath = Root + "/Presentation/Source/NyxPaperPalette.png";
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter
                ?? throw new InvalidOperationException("Run tools/nyx/build_paper_assets.py before this builder.");
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            var shader = Shader.Find("SlopArena/Nyx Paper") ?? throw new InvalidOperationException("Nyx Paper shader is missing.");
            string path = _kit.Root + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created) mat = new Material(shader);
            mat.shader = shader;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            // Closed volumes need self-occlusion, including contact bursts.
            mat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            mat.renderQueue = 3012 + (inFront ? 1 : 0);
            if (created) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        private static Mesh Mesh(string name)
        {
            if (Meshes.TryGetValue(name, out var result)) return result;
            string path = Root + "/Presentation/Source/" + name + ".obj";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter
                ?? throw new InvalidOperationException("Missing paper mesh " + path);
            importer.globalScale = 1f;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.SaveAndReimport();
            var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
            if (meshes.Length != 1) throw new InvalidOperationException("Expected one mesh at " + path);
            result = Object.Instantiate(meshes[0]);
            result.name = name + "Mesh";
            // Particle mesh rendering reads packed UNorm8 colours. Float32 white was
            // interpreted as bytes 00 00 80 3F (blue, 25% alpha) in its baked stream.
            result.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), result.vertexCount).ToArray();
            result = _kit.SaveMesh(result);
            // CopySerialized updates persisted mesh data but can leave an existing GPU buffer stale.
            // Reapply the streams before upload so an Editor rebuild shows the saved palette and geometry.
            result.vertices = result.vertices;
            result.normals = result.normals;
            result.uv = result.uv;
            result.colors32 = result.colors32;
            result.triangles = result.triangles;
            result.RecalculateBounds();
            result.UploadMeshData(false);
            EditorUtility.SetDirty(result);
            AssetDatabase.SaveAssetIfDirty(result);
            return Meshes[name] = result;
        }

        private static Transform Part(GameObject root, string name, string mesh, Vector3 at, Vector3 scale, Material material = null)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = at;
            part.transform.localScale = scale;
            part.GetComponent<MeshFilter>().sharedMesh = Mesh(mesh);
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material ?? _paper;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return part.transform;
        }

        private static NyxPaperCue Cue(string name, bool follow = true)
        {
            var root = _kit.NewRoot(name, attackBound: true);
            var cue = root.AddComponent<NyxPaperCue>();
            cue.FollowOwner = follow;
            return cue;
        }

        private static void Layer(NyxPaperCue cue, string name, string mesh, int start, int end, Vector3 at,
            Vector3? scale = null, float turn = 0f, Vector3? to = null, bool billboard = false, float roll = 0f, float viewOffset = 0f)
        {
            var part = Part(cue.gameObject, name, mesh, at, scale ?? Vector3.one);
            if (billboard) FaceViewer(part, roll, viewOffset);
            cue.Layers = cue.Layers.Append(new NyxPaperCue.Layer
            {
                Content = part, StartTick = start, EndTick = end, From = at, To = to ?? at,
                Scale = scale ?? Vector3.one, TurnDegreesPerTick = turn,
            }).ToArray();
            part.gameObject.SetActive(false);
        }

        // The layer owns its authored placement/clock. A child pivot turns the
        // mesh around its own centre, without orbiting it around the cue origin.
        private static void FaceViewer(Transform part, float roll = 0f, float viewOffset = 0f)
        {
            var filter = part.GetComponent<MeshFilter>();
            var renderer = part.GetComponent<MeshRenderer>();
            var pivot = new GameObject(part.name + " view pivot").transform;
            pivot.SetParent(part, false);
            pivot.localPosition = filter.sharedMesh.bounds.center;
            var face = new GameObject(part.name + " comic face", typeof(MeshFilter), typeof(MeshRenderer));
            face.transform.SetParent(pivot, false);
            face.transform.localRotation = Quaternion.Euler(0f, 0f, roll);
            face.transform.localPosition = face.transform.localRotation * -filter.sharedMesh.bounds.center;
            face.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var drawn = face.GetComponent<MeshRenderer>();
            drawn.sharedMaterial = renderer.sharedMaterial;
            drawn.shadowCastingMode = ShadowCastingMode.Off;
            drawn.receiveShadows = false;
            drawn.lightProbeUsage = LightProbeUsage.Off;
            drawn.reflectionProbeUsage = ReflectionProbeUsage.Off;
            pivot.gameObject.AddComponent<YawBillboard>().ViewRightOffset = viewOffset;
            Object.DestroyImmediate(filter);
            Object.DestroyImmediate(renderer);
        }

        private static GameObject Normal(string name, string mesh, int start, int end, float turn = 0f)
        {
            var cue = Cue(name);
            if (mesh == "NyxRisingHands")
            {
                Layer(cue, "Left rising palm", mesh, start, end, new Vector3(-.65f, 0f, 0f), billboard: true);
                Layer(cue, "Right rising palm", mesh, start, end, new Vector3(.65f, 0f, 0f), billboard: true);
            }
            else Layer(cue, "Active paper silhouette", mesh, start, end, Vector3.zero, turn: turn,
                billboard: mesh is "NyxUpClaw" or "NyxSpikeFist");
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Slaps()
        {
            var cue = Cue("NyxPhoneSlaps");
            int[] starts = { 8, 16, 26 }, ends = { 12, 20, 31 };
            for (int i = 0; i < starts.Length; i++)
            {
                var at = new Vector3(i == 1 ? -.15f : .15f, .1f, .9f);
                Layer(cue, "Slap " + i, "NyxPinkImpact", starts[i], ends[i], at);
                Layer(cue, "Slap heart " + i, "NyxHeart", starts[i], ends[i], at + new Vector3(.3f, .2f, 0));
            }
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Stomp()
        {
            var cue = Cue("NyxStompImpact");
            Layer(cue, "Boot contact", "NyxPinkImpact", 16, 23, new Vector3(0, -.23f, 1.25f), new Vector3(1.2f, .9f, 1));
            Layer(cue, "Boot sparkle", "NyxSparkle", 16, 23, new Vector3(.35f, -.05f, 1.3f));
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Rotor()
        {
            var cue = Cue("NyxRotorArcs");
            Layer(cue, "Rotor", "NyxRotorArcs", 12, 50, Vector3.zero, turn: 35f);
            for (int i = 0; i < 4; i++)
                Layer(cue, "Rotor sparkle " + i, "NyxSparkle", 12, 50,
                    new Vector3((i % 2 == 0 ? -1 : 1) * .42f, 1.2f, (i < 2 ? -1 : 1) * .4f), turn: -12f);
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Selfie()
        {
            var cue = Cue("NyxSelfiePop");
            cue.OriginBoneId = "bone.right-hand";
            Layer(cue, "Phone flash", "NyxPinkImpact", 44, 52, Vector3.zero, Vector3.one * 1.5f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * .5f;
                Layer(cue, "Selfie heart " + i, "NyxHeart", 44, 52,
                    new Vector3(Mathf.Cos(a) * .4f, Mathf.Sin(a) * .4f, 0));
            }
            return _kit.Save(cue.gameObject);
        }

        private static ParticleSystem Bits(GameObject root, string name, string mesh, int count, float life, float speed,
            bool contact = false, float scale = 1f)
        {
            var ps = Particles(root, name, contact ? _contact : _paper, Mathf.Max(.1f, life),
                ParticleSystemSimulationSpace.Local, contact ? ParticleSystemScalingMode.Hierarchy : ParticleSystemScalingMode.Local);
            Burst(ps, count);
            var main = ps.main;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = scale;
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.maxParticles = Math.Max(8, count);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Mesh(mesh);
            // The package shader consumes ordinary mesh streams, not procedural particle instances.
            renderer.enableGPUInstancing = false;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            Sphere(ps, .06f);
            SizeOverLife(ps, Curve((0f, 1f), (.2f, .8f), (1f, .05f)));
            if (contact) InFront(ps);
            return ps;
        }

        private static GameObject Hit(string name, bool fake, bool feathers)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            Bits(root, "Contact star", fake ? "NyxPinkImpact" : "NyxShadowImpact", 1, .09f, 0, true, feathers ? .45f : .35f);
            Bits(root, "Paper fragments", feathers ? "NyxFeather" : fake ? "NyxHeart" : "NyxShadowShard", feathers ? 7 : 4,
                .24f, 2.2f, true, .3f);
            if (fake) Bits(root, "Sparkles", "NyxSparkle", 3, .2f, 1.6f, true, .55f);
            return _kit.Save(root);
        }

        private static GameObject Crow()
        {
            var root = _kit.NewRoot("NyxHexCrow", attackBound: false);
            Part(root, "Cut-paper body", "NyxCrowBody", Vector3.zero, Vector3.one);
            // The bird faces +Z; beveled body and folded solid wings read through a full orbit.
            var wings = root.AddComponent<NyxCrowVisual>();
            wings.LeftWing = Part(root, "Left feathered wing", "NyxCrowWingLeft", Vector3.zero, Vector3.one);
            wings.RightWing = Part(root, "Right feathered wing", "NyxCrowWingRight", Vector3.zero, Vector3.one);
            return _kit.Save(root);
        }

        private static GameObject CrowLaunch()
        {
            var cue = Cue("NyxCrowLaunch");
            for (int i = 0; i < 5; i++)
            {
                var at = new Vector3((i - 2) * .1f, .45f + i * .05f, .6f + i * .07f);
                Layer(cue, "Launch feather " + i, "NyxFeather", 16, 24, at, to: at + new Vector3((i - 2) * .12f, .2f, .4f), billboard: true, roll: (i - 2) * 27f);
            }
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Bats()
        {
            var cue = Cue("NyxBatScatter");
            for (int i = 0; i < 28; i++)
            {
                float a = i * 2.399963f;
                var at = new Vector3(Mathf.Cos(a) * (.3f + i % 3 * .13f), -.65f + i / 27f * 1.6f, Mathf.Sin(a) * .42f);
                Layer(cue, "Travel bat " + i, "NyxBat", 0, 24, at, Vector3.one * (1f + i % 3 * .2f), billboard: true, roll: (i * 47f) % 70f - 35f);
                if (i >= 14) continue;
                Layer(cue, "Burst bat " + i, "NyxBat", 0, 5, at, to: at * 1.6f, billboard: true, roll: (i * 47f) % 70f - 35f);
                Layer(cue, "Re-form bat " + i, "NyxBat", 24, 30, at * 1.6f, to: at * .1f, billboard: true, roll: (i * 47f) % 70f - 35f);
            }
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Swap(bool goingLive)
        {
            var cue = Cue(goingLive ? "NyxGoingLive" : "NyxLoggingOff");
            int start = goingLive ? 9 : 10, end = start + 12;
            if (goingLive)
            {
                Layer(cue, "Main flash hoop", "NyxSpinArc", start, start + 6, new Vector3(0f, -.35f, .15f),
                    new Vector3(1.35f, .65f, 1.35f), turn: 16f);
                Layer(cue, "Outfit flash", "NyxPinkImpact", start, start + 4, new Vector3(0f, .8f, .15f),
                    Vector3.one * .65f, billboard: true, viewOffset: .5f);
            }
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16;
                var at = new Vector3(Mathf.Cos(a) * 1.1f, -.6f + i % 5 * .4f, .6f + Mathf.Sin(a) * 1.1f);
                var to = new Vector3(at.x * 2f, at.y * 1.4f, .6f + (at.z - .6f) * 2f);
                Layer(cue, "Swap scrap " + i, goingLive ? i % 2 == 0 ? "NyxHeart" : "NyxSparkle" : "NyxShadowShard",
                    start, end, at, Vector3.one * (goingLive ? 1.8f : 1.3f), to: to, billboard: true, roll: (i * 53f) % 100f - 50f);
            }
            return _kit.Save(cue.gameObject);
        }

        private static GameObject GraspWarning()
        {
            var cue = Cue("NyxGraspWarning", follow: false);
            Layer(cue, "Target feet mark", "NyxGraspWarning", 12, 24, Vector3.zero, turn: 4f);
            return _kit.Save(cue.gameObject);
        }

        private static GameObject GraspCatch(GameObject drag)
        {
            var cue = Cue("NyxGraspCatch", follow: false);
            Layer(cue, "Rising target hand", "NyxGraspHand", 24, 29, new Vector3(0f, .1f, 0f),
                Vector3.one * .48f, billboard: true, viewOffset: .6f);
            cue.gameObject.AddComponent<PlacedTetherVisual>().DragPrefab = drag;
            return _kit.Save(cue.gameObject);
        }

        private static GameObject GraspDrag()
        {
            var root = _kit.NewRoot("NyxGraspDrag", attackBound: false);
            FaceViewer(Part(root, "Held shadow hand", "NyxGraspHand", new Vector3(0f, .1f, 0f), Vector3.one * .48f), viewOffset: .6f);
            var visual = root.AddComponent<PlacedTetherVisual>();
            visual.Drag = Part(root, "Torn drag strokes", "NyxGraspDrag", Vector3.zero, Vector3.one);
            visual.Drag.gameObject.SetActive(false);
            return _kit.Save(root);
        }
    }
}
#endif
