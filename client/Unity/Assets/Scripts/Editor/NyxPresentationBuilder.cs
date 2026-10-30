#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        // Layer ticks are absolute attack ticks. They come from the cues in character.json, so a retime
        // (tools/nyx/retime.py) followed by a rebuild keeps every layer inside its cue's lifetime.
        private static readonly Dictionary<string, List<(int Tick, int Duration)>> Emits = new(StringComparer.Ordinal);
        private static int _thrustTicks, _rotorStartupTicks;

        [CliCommand("sloparena.presentation.nyx.build", "Build Nyx's approved cut-paper shadows, crow, bats, Grasp, swaps and pink prop effects, preserving prop bindings and rig.",
            MainThreadRequired = true, Tags = new[] { "presentation", "nyx" })]
        public static object Build()
        {
            using var kit = new PresentationBuilderKit("nyx", "Nyx");
            _kit = kit;
            Meshes.Clear();
            LoadCueTiming();
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
            Bind("shadow-claws", Normal("NyxShadowClaws", "NyxShadowClaws", "shadow-claws"));
            Bind("rising-hands", Normal("NyxRisingHands", "NyxRisingHands", "rising-hands"));
            Bind("floor-lash", Normal("NyxFloorLash", "NyxFloorLash", "floor-lash"));
            Bind("shadow-ring", Normal("NyxShadowRing", "NyxShadowRing", "shadow-ring", turn: 12f));
            Bind("up-claw", Normal("NyxUpClaw", "NyxUpClaw", "up-claw"));
            Bind("spike-fist", Normal("NyxSpikeFist", "NyxSpikeFist", "spike-fist"));
            Bind("phone-slaps", Slaps());
            Bind("pole-uppercut", Normal("NyxPoleUppercut", "NyxPoleUpperArc", "pole-uppercut"));
            Bind("stomp-impact", Stomp());
            Bind("spin-arc", Normal("NyxSpinArc", "NyxSpinArc", "spin-arc", turn: 20f));
            Bind("pole-overhead", Normal("NyxPoleOverhead", "NyxPoleOverheadArc", "pole-overhead"));
            Bind("dropkick-spike", Normal("NyxDropkickSpike", "NyxDropkickSpike", "dropkick-spike"));
            Bind("ring-light", RingLight());
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
            var result = kit.Finish(built, new { meshes = Meshes.Count, noSmoke = true, noBloom = true, noCameraShake = true });
            // Reload the saved meshes from disk. A rebuild that changes a mesh's vertex or index count can
            // otherwise keep drawing the old GPU buffers for the rest of the Editor session.
            foreach (var mesh in Meshes.Values)
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(mesh), ImportAssetOptions.ForceUpdate);
            return result;
        }

        private static void LoadCueTiming()
        {
            Emits.Clear();
            _thrustTicks = _rotorStartupTicks = -1;
            void Add(string presentationId, int tick, int duration)
            {
                const string prefix = "presentation.nyx.";
                if (presentationId == null || !presentationId.StartsWith(prefix, StringComparison.Ordinal)) return;
                string id = presentationId.Substring(prefix.Length);
                if (!Emits.TryGetValue(id, out var list)) Emits[id] = list = new List<(int, int)>();
                if (!list.Contains((tick, duration))) list.Add((tick, duration));
            }
            using var document = JsonDocument.Parse(File.ReadAllText(Root + "/character.json"));
            var character = document.RootElement;
            foreach (var slot in character.GetProperty("slots").EnumerateArray()
                         .Concat(character.GetProperty("alternateForm").GetProperty("slots").EnumerateArray()))
            {
                int stageStart = 0;
                foreach (var stage in slot.GetProperty("timeline").GetProperty("stages").EnumerateArray())
                {
                    foreach (var op in stage.GetProperty("operations").EnumerateArray())
                    {
                        int tick = stageStart + op.GetProperty("tick").GetInt32();
                        switch (op.GetProperty("kind").GetString())
                        {
                            case "emitPresentation":
                                Add(op.GetProperty("presentationId").GetString(), tick,
                                    op.GetProperty("placement").GetProperty("durationTicks").GetInt32());
                                break;
                            case "startCapability":
                                var parameters = op.GetProperty("parameters");
                                if (parameters.TryGetProperty("thrustTicks", out var thrust)) _thrustTicks = thrust.GetInt32();
                                if (parameters.TryGetProperty("startupTicks", out var startup)) _rotorStartupTicks = startup.GetInt32();
                                if (parameters.TryGetProperty("atTarget", out var at))
                                {
                                    // TetherAbility warns at throwTick and raises the catch when the warning ends.
                                    int warn = tick + parameters.GetProperty("throwTick").GetInt32();
                                    int warning = at.GetProperty("warningTicks").GetInt32();
                                    Add(at.GetProperty("warningPresentationId").GetString(), warn, warning);
                                    Add(at.GetProperty("catchPresentationId").GetString(), warn + warning, at.GetProperty("catchTicks").GetInt32());
                                }
                                break;
                        }
                    }
                    stageStart += stage.GetProperty("durationTicks").GetInt32();
                }
            }
            foreach (var list in Emits.Values) list.Sort();
            if (_thrustTicks < 0 || _rotorStartupTicks < 0)
                throw new InvalidOperationException("character.json lost Bat Scatter's thrustTicks or Ring Light Rise's startupTicks.");
        }

        [CliCommand("sloparena.presentation.nyx.verify-timing", "Check that every Nyx paper layer shows inside its cue's lifetime in character.json; run after a retime.",
            MainThreadRequired = true, Tags = new[] { "presentation", "nyx" })]
        public static object VerifyTiming()
        {
            LoadCueTiming();
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(Root + "/CharacterAssetCatalog.asset");
            var problems = new List<string>();
            int checkedLayers = 0;
            foreach (var binding in catalog.Presentations)
            {
                const string prefix = "presentation.nyx.";
                var cue = binding.Prefab != null ? binding.Prefab.GetComponent<NyxPaperCue>() : null;
                if (cue == null || !binding.SemanticId.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!Emits.TryGetValue(binding.SemanticId.Substring(prefix.Length), out var windows))
                {
                    problems.Add(binding.SemanticId + ": no cue in character.json");
                    continue;
                }
                foreach (var layer in cue.Layers)
                {
                    checkedLayers++;
                    // A layer must start inside a cue's lifetime and end by its end, or part of it never shows.
                    if (!windows.Any(w => layer.StartTick >= w.Tick && layer.EndTick <= w.Tick + w.Duration))
                        problems.Add($"{binding.SemanticId} '{(layer.Content != null ? layer.Content.name : "?")}' ticks {layer.StartTick}-{layer.EndTick} fall outside every cue window");
                }
            }
            if (problems.Count > 0)
                throw new InvalidOperationException("Nyx paper layers drifted from character.json; run sloparena.presentation.nyx.build. " + string.Join("; ", problems));
            return new { success = true, checkedLayers };
        }

        private static (int Tick, int Duration) Emit(string id, int index = 0) =>
            Emits.TryGetValue(id, out var list) && index < list.Count ? list[index]
                : throw new InvalidOperationException("character.json has no cue " + index + " for presentation.nyx." + id);

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
            Vector3? scale = null, float turn = 0f, Vector3? to = null, bool billboard = false, float roll = 0f, float viewOffset = 0f,
            Vector3? scaleTo = null, float viewBack = 0f)
        {
            var part = Part(cue.gameObject, name, mesh, at, scale ?? Vector3.one);
            if (billboard) FaceViewer(part, roll, viewOffset, viewBack);
            cue.Layers = cue.Layers.Append(new NyxPaperCue.Layer
            {
                Content = part, StartTick = start, EndTick = end, From = at, To = to ?? at,
                Scale = scale ?? Vector3.one, ScaleTo = scaleTo ?? scale ?? Vector3.one, TurnDegreesPerTick = turn,
            }).ToArray();
            part.gameObject.SetActive(false);
        }

        // The layer owns its authored placement/clock. A child pivot turns the
        // mesh around its own centre, without orbiting it around the cue origin.
        private static void FaceViewer(Transform part, float roll = 0f, float viewOffset = 0f, float viewBack = 0f)
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
            var billboard = pivot.gameObject.AddComponent<YawBillboard>();
            billboard.ViewRightOffset = viewOffset;
            billboard.ViewBackOffset = viewBack;
            Object.DestroyImmediate(filter);
            Object.DestroyImmediate(renderer);
        }

        private static GameObject Normal(string name, string mesh, string id, float turn = 0f)
        {
            var cue = Cue(name);
            var (start, duration) = Emit(id);
            int end = start + duration;
            if (mesh == "NyxRisingHands")
            {
                // Two shadow hands rise beside her and close in above her head.
                Layer(cue, "Left rising palm", mesh, start, end, new Vector3(-.8f, -.15f, 0f), Vector3.one * .9f,
                    to: new Vector3(-.55f, .2f, 0f), billboard: true, roll: -8f, scaleTo: Vector3.one * 1.05f);
                Layer(cue, "Right rising palm", mesh, start, end, new Vector3(.8f, -.15f, 0f), Vector3.one * .9f,
                    to: new Vector3(.55f, .2f, 0f), billboard: true, roll: 8f, scaleTo: Vector3.one * 1.05f);
            }
            else
            {
                // Flat claw marks and the fist face the camera; arcs, rings and the lash keep their 3D sweep.
                bool faced = mesh is "NyxShadowClaws" or "NyxUpClaw" or "NyxSpikeFist";
                var at = mesh == "NyxShadowClaws" ? new Vector3(0f, .15f, 1.05f) : mesh == "NyxSpikeFist" ? new Vector3(0f, 0f, .35f) : Vector3.zero;
                Layer(cue, "Active paper silhouette", mesh, start, end, at, turn: turn, billboard: faced,
                    scaleTo: Vector3.one * .9f);
            }
            return _kit.Save(cue.gameObject);
        }

        private static GameObject RingLight()
        {
            var cue = Cue("NyxRingLight");
            var (start, duration) = Emit("ring-light");
            int end = start + duration;
            Layer(cue, "Light hoop and rays", "NyxFlashCone", start, end, Vector3.zero, Vector3.one * .9f, scaleTo: Vector3.one * 1.1f);
            Layer(cue, "Lens flash", "NyxPinkBurst", start, start + 4, new Vector3(0f, .1f, .62f), Vector3.one * .5f,
                billboard: true, scaleTo: Vector3.one * .85f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f + .4f;
                var from = new Vector3(Mathf.Cos(a) * .2f, .1f + Mathf.Sin(a) * .2f, .9f);
                var to = new Vector3(Mathf.Cos(a) * .6f, .1f + Mathf.Sin(a) * .55f, 2.1f + (i % 2) * .25f);
                bool heart = i % 3 == 0;
                Layer(cue, (heart ? "Beam heart " : "Beam sparkle ") + i, heart ? "NyxHeart" : "NyxSparkle", start, end, from,
                    Vector3.one * 1.3f, to: to, billboard: true, roll: (i * 41f) % 60f - 30f, scaleTo: Vector3.one * 1.7f);
            }
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Slaps()
        {
            var cue = Cue("NyxPhoneSlaps");
            for (int i = 0; i < 3; i++)
            {
                var (start, duration) = Emit("phone-slaps", i);
                var at = new Vector3(i == 1 ? -.2f : .2f, .15f, .95f);
                float size = i == 2 ? .75f : .55f;
                Layer(cue, "Slap " + i, "NyxPinkBurst", start, start + duration, at, Vector3.one * size,
                    billboard: true, roll: i * 25f, scaleTo: Vector3.one * size * 1.4f);
                Layer(cue, "Slap heart " + i, "NyxHeart", start, start + duration, at + new Vector3(.3f, .2f, 0),
                    to: at + new Vector3(.5f, .55f, 0), billboard: true, scaleTo: Vector3.one * 1.4f);
            }
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Stomp()
        {
            var cue = Cue("NyxStompImpact");
            var (start, duration) = Emit("stomp-impact");
            Layer(cue, "Boot contact", "NyxPinkBurst", start, start + duration, new Vector3(0, -.1f, 1.3f), Vector3.one * .8f,
                billboard: true, scaleTo: Vector3.one * 1.15f);
            Layer(cue, "Boot sparkle", "NyxSparkle", start, start + duration, new Vector3(.35f, -.05f, 1.3f),
                to: new Vector3(.6f, .35f, 1.4f), billboard: true, scaleTo: Vector3.one * 1.5f);
            Layer(cue, "Boot heart", "NyxHeart", start, start + duration, new Vector3(-.3f, .05f, 1.3f),
                to: new Vector3(-.55f, .4f, 1.35f), billboard: true, roll: -20f, scaleTo: Vector3.one * 1.5f);
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Rotor()
        {
            var cue = Cue("NyxRotorArcs");
            var (emit, duration) = Emit("rotor-arcs");
            int start = _rotorStartupTicks, end = emit + duration;
            Layer(cue, "Rotor", "NyxRotorArcs", start, end, Vector3.zero, turn: 35f);
            for (int i = 0; i < 4; i++)
                Layer(cue, "Rotor sparkle " + i, "NyxSparkle", start, end,
                    new Vector3((i % 2 == 0 ? -1 : 1) * .55f, 1.2f, (i < 2 ? -1 : 1) * .5f), Vector3.one * 1.3f, turn: -12f);
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Selfie()
        {
            var cue = Cue("NyxSelfiePop");
            cue.OriginBoneId = "bone.right-hand";
            var (start, duration) = Emit("selfie-pop");
            int end = start + duration;
            // The selfie flash pops just above the raised phone, beside both faces rather than over them.
            var flash = new Vector3(0f, .22f, 0f);
            Layer(cue, "Phone flash", "NyxPinkBurst", start, start + 7, flash, Vector3.one * .55f, billboard: true,
                scaleTo: Vector3.one * .9f);
            Layer(cue, "Flash rays", "NyxPinkRays", start, start + 5, flash, Vector3.one * .28f, billboard: true,
                scaleTo: Vector3.one * .42f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f + .3f;
                bool heart = i % 2 == 0;
                Layer(cue, (heart ? "Selfie heart " : "Selfie sparkle ") + i, heart ? "NyxHeart" : "NyxSparkle", start, end,
                    flash + new Vector3(Mathf.Cos(a) * .2f, Mathf.Sin(a) * .2f, 0f), Vector3.one * 1.1f,
                    to: flash + new Vector3(Mathf.Cos(a) * .75f, Mathf.Sin(a) * .6f + .2f, 0f), billboard: true,
                    roll: (i * 37f) % 60f - 30f, scaleTo: Vector3.one * 1.6f);
            }
            return _kit.Save(cue.gameObject);
        }

        private static ParticleSystem Bits(GameObject root, string name, string mesh, int count, float life, float speed,
            bool contact = false, float scale = 1f, bool faced = false)
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
            // Flat comic bursts face the camera; solid fragments keep their own tumble.
            renderer.alignment = faced ? ParticleSystemRenderSpace.View : ParticleSystemRenderSpace.Local;
            Sphere(ps, .06f);
            SizeOverLife(ps, Curve((0f, 1f), (.2f, .8f), (1f, .05f)));
            if (contact) InFront(ps);
            return ps;
        }

        // A world-space stream of paper shapes left behind a moving cue or projectile.
        private static ParticleSystem Trail(GameObject root, string name, string mesh, float perMeter, float life, float size)
        {
            var ps = Particles(root, name, _paper, 1f);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * .7f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .7f, size * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-.6f, .6f);
            main.maxParticles = 96;
            var emission = ps.emission;
            emission.rateOverDistance = perMeter;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Mesh(mesh);
            renderer.enableGPUInstancing = false;
            renderer.alignment = ParticleSystemRenderSpace.View;
            Sphere(ps, .25f);
            SizeOverLife(ps, Curve((0f, 1f), (.6f, .8f), (1f, .1f)));
            return ps;
        }

        private static GameObject Hit(string name, bool fake, bool feathers)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            var burst = Bits(root, "Contact burst", fake ? "NyxPinkBurst" : "NyxShadowBurst", 1, feathers ? .22f : .1f, 0, true,
                feathers ? .9f : .42f, faced: true);
            SizeOverLife(burst, Curve((0f, .7f), (.3f, 1.05f), (1f, .25f)));
            Bits(root, "Paper fragments", feathers ? "NyxFeather" : fake ? "NyxHeart" : "NyxShadowShard", feathers ? 9 : fake ? 3 : 5,
                .26f, 2.4f, true, .38f);
            // The crow pop is the kit's payoff: its feathers come with a ring of shadow shards, and its burst
            // draws in front of the perch so the crow and the victim's head cannot hide it.
            if (feathers)
            {
                Bits(root, "Pop shards", "NyxShadowShard", 8, .3f, 3.2f, true, .45f);
                burst.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, 0f, .8f);
            }
            if (fake) Bits(root, "Sparkles", "NyxSparkle", 2, .22f, 1.8f, true, .55f);
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
            // The concept's trail of violet shards behind the flying crow; a perched crow leaves none.
            Trail(root, "Shard trail", "NyxShadowShard", 7f, .35f, .45f);
            return _kit.Save(root);
        }

        private static GameObject CrowLaunch()
        {
            var cue = Cue("NyxCrowLaunch");
            int start = Emit("crow-launch").Tick, end = start + 10;
            Layer(cue, "Launch puff", "NyxShadowBurst", start, start + 5, new Vector3(0f, .45f, .6f), Vector3.one * .35f,
                billboard: true, scaleTo: Vector3.one * .65f);
            for (int i = 0; i < 5; i++)
            {
                var at = new Vector3((i - 2) * .1f, .45f + i * .05f, .6f + i * .07f);
                Layer(cue, "Launch feather " + i, "NyxFeather", start, end, at, to: at + new Vector3((i - 2) * .12f, .2f, .4f), billboard: true, roll: (i - 2) * 27f);
            }
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Bats()
        {
            var cue = Cue("NyxBatScatter");
            // The body hides for the thrust (PlayerRenderer.HidesThrustTravel); bats re-form as it returns.
            int emit = Emit("bat-scatter").Tick, travel = emit + _thrustTicks;
            // Bats left behind along the flight make the stream; the swarm itself stays a tight cloud.
            var stream = Trail(cue.gameObject, "Bat stream", "NyxBat", 9f, .32f, 1.1f);
            cue.Layers = cue.Layers.Append(new NyxPaperCue.Layer
            {
                Content = stream.transform, StartTick = emit, EndTick = travel,
                Scale = Vector3.one, ScaleTo = Vector3.one,
            }).ToArray();
            stream.gameObject.SetActive(false);
            for (int i = 0; i < 28; i++)
            {
                float a = i * 2.399963f;
                var at = new Vector3(Mathf.Cos(a) * (.3f + i % 3 * .15f), -.35f + i / 27f * 1.1f, Mathf.Sin(a) * .45f);
                Layer(cue, "Travel bat " + i, "NyxBat", emit, travel, at, Vector3.one * (1f + i % 3 * .2f), billboard: true, roll: (i * 47f) % 70f - 35f);
                if (i >= 14) continue;
                Layer(cue, "Burst bat " + i, "NyxBat", emit, emit + 6, at, to: at * 1.6f, billboard: true, roll: (i * 47f) % 70f - 35f);
                Layer(cue, "Re-form bat " + i, "NyxBat", travel, travel + 8, at * 1.6f, to: at * .1f, billboard: true, roll: (i * 47f) % 70f - 35f);
            }
            return _kit.Save(cue.gameObject);
        }

        private static GameObject Swap(bool goingLive)
        {
            var cue = Cue(goingLive ? "NyxGoingLive" : "NyxLoggingOff");
            int start = Emit(goingLive ? "going-live" : "logging-off").Tick, end = start + 15;
            // The outfit swaps inside one big flash drawn behind her: a pink burst with focus lines going live,
            // a violet shatter and a jagged floor crown logging off.
            if (goingLive)
            {
                Layer(cue, "Live burst", "NyxPinkBurst", start, start + 6, new Vector3(0f, .3f, 0f), Vector3.one * 1.2f,
                    billboard: true, viewBack: .3f, scaleTo: Vector3.one * 1.6f);
                Layer(cue, "Focus lines", "NyxPinkRays", start, start + 9, new Vector3(0f, .45f, 0f), Vector3.one * .85f,
                    billboard: true, viewBack: .2f, scaleTo: Vector3.one * 1.25f);
            }
            else
            {
                Layer(cue, "Shatter burst", "NyxShadowBurst", start, start + 6, new Vector3(0f, .3f, 0f), Vector3.one * 1.2f,
                    billboard: true, viewBack: .3f, scaleTo: Vector3.one * 1.7f);
                Layer(cue, "Floor crown", "NyxFloorCrown", start, start + 12, new Vector3(0f, -.84f, 0f), new Vector3(.8f, .9f, .8f),
                    scaleTo: new Vector3(2.3f, 1.1f, 2.3f));
            }
            // Fewer, wider-spread scraps keep both fighters readable through the swap.
            const int count = 12;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                float r = goingLive ? 1.1f : .9f + i % 3 * .2f;
                var at = new Vector3(Mathf.Cos(a) * r, -.6f + i % 5 * .4f, .6f + Mathf.Sin(a) * r);
                var to = new Vector3(at.x * 2f, at.y * 1.4f, .6f + (at.z - .6f) * 2f);
                Layer(cue, "Swap scrap " + i, goingLive ? i % 2 == 0 ? "NyxHeart" : "NyxSparkle" : "NyxShadowShard",
                    start, end, at, Vector3.one * (goingLive ? 1.8f : .95f + i % 3 * .25f), to: to, billboard: true, roll: (i * 53f) % 100f - 50f);
            }
            return _kit.Save(cue.gameObject);
        }

        private static GameObject GraspWarning()
        {
            var cue = Cue("NyxGraspWarning", follow: false);
            var (warn, warning) = Emit("grasp-warning");
            Layer(cue, "Target feet mark", "NyxGraspWarning", warn, warn + warning, Vector3.zero, turn: 4f);
            return _kit.Save(cue.gameObject);
        }

        private static GameObject GraspCatch(GameObject drag)
        {
            var cue = Cue("NyxGraspCatch", follow: false);
            var (catchTick, catchTicks) = Emit("grasp-catch");
            int end = catchTick + catchTicks;
            // A torn shadow pool opens under the target. The drag prefab's hand grips them from behind,
            // so the caught fighter stays drawn in front of it.
            Layer(cue, "Shadow pool", "NyxGraspSplash", catchTick, end, new Vector3(0f, .02f, 0f), Vector3.one * .55f,
                scaleTo: Vector3.one * 1.15f);
            cue.gameObject.AddComponent<PlacedTetherVisual>().DragPrefab = drag;
            return _kit.Save(cue.gameObject);
        }

        private static GameObject GraspDrag()
        {
            var root = _kit.NewRoot("NyxGraspDrag", attackBound: false);
            Part(root, "Dragged shadow pool", "NyxGraspSplash", new Vector3(0f, .02f, 0f), Vector3.one * .6f);
            FaceViewer(Part(root, "Held shadow hand", "NyxGraspHand", new Vector3(0f, -.2f, 0f), Vector3.one * .75f), viewBack: .3f);
            var visual = root.AddComponent<PlacedTetherVisual>();
            visual.Drag = Part(root, "Torn drag strokes", "NyxGraspDrag", Vector3.zero, Vector3.one);
            visual.Drag.gameObject.SetActive(false);
            return _kit.Save(root);
        }
    }
}
#endif
