#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static SlopArena.EditorTools.PresentationFx;
using static SlopArena.EditorTools.ToonShapes;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Rebuilds Neo Mall's course feel (Wibou's course, docs/design/stages/wibou_mall.design.md):
    /// shop mannequins he dressed as agents for targets, which a cut splits into white halves
    /// and green code; agents riding the escalator, peeking over level 4 and clinging to the
    /// elevator tower's lip; four bosses that take five hits in four damage stages (the inflatable
    /// tube man, THE AGENT, FRANK the hot dog and OFFICER DOUG's cardboard cutout, modelled by
    /// tools/blender/wibou_mall/targets.py), each break a PWNED!; hacker-green checkpoint frames;
    /// red beams over missed agents; and the game store's purple beam with a NAT 20! and a shower
    /// of dice at the finish. Cuts reuse Wibou's own materials (CharacterPackages/wibou/Presentation),
    /// so the course and his kit slice things the same way. Writes Stages/wibou_mall/Presentation/Course
    /// and Resources/Courses/wibou_mall; tools/sfx/mall_course_clips.py makes the sounds
    /// (Audio/SFX/CourseMall). Lettering masks in Course/Source come from tools/glyph_mask.py:
    /// PWNED! and NAT 20! (Archivo Black, --fill 0.9 --bounce 5).
    /// </summary>
    public static class MallCoursePresentationBuilder
    {
        private const string Root = "Assets/Stages/wibou_mall/Presentation/Course";
        private const string SetPath = "Assets/Resources/" + CoursePresentationSet.ResourcesFolder + "/wibou_mall.asset";
        private const string WibouFx = "Assets/CharacterPackages/wibou/Presentation/";
        private const string MallArt = "Assets/Stages/wibou_mall/Presentation/Mall/";
        private const string TargetsModelPath = Root + "/NeoMallTargets.fbx";
        private const string SoundFolder = "Audio/SFX/CourseMall";
        // layout.py: mannequins (radius 0.55) stand 1.1 m under their centre; bosses (radius 1) 1.4 m.
        private const float TargetRadius = .55f, BossRadius = 1f;

        // Neo Mall and Wibou's own colours.
        private static readonly Color Ink = Hex(0x111214);
        private static readonly Color GreenDark = Hex(0x1f5c12);
        private static readonly Color Green = Hex(0x91ff69);
        private static readonly Color Pale = Hex(0xe9ffde);
        private static readonly Color RedDark = Hex(0x5c0d10);
        private static readonly Color Red = Hex(0xe62e38);
        private static readonly Color RedLight = Hex(0xff8a8f);
        private static readonly Color PurpleDark = Hex(0x2b1450);
        private static readonly Color Purple = Hex(0x9a5cf0);
        private static readonly Color PurpleLight = Hex(0xd9c2ff);
        private static readonly Color Cream = Hex(0xfaf2dc);
        private static readonly Color Mannequin = Hex(0xf2f0e8);
        private static readonly Color Suit = Hex(0x141416);
        private static readonly Color TubeRed = Hex(0xeb2929);
        private static readonly Color TubeYellow = Hex(0xffd12e);
        private static readonly Color Bun = Hex(0xdb994d);
        private static readonly Color Sausage = Hex(0xb33d29);
        private static readonly Color Cardboard = Hex(0xb88f61);
        private static readonly Color CardboardDark = Hex(0x80613d);

        private static PresentationBuilderKit _kit;
        private static Materials _m;
        private static Mesh _quad, _beamQuad;
        private static GameObject _targets;

        private sealed class Materials
        {
            public Material Halo, FrameGreen, FrameRed, RingGreen, RingRed, MoteGreen, MoteRed, BeamRed, BeamPurple,
                MannequinBits, TubeBits, FoodBits, CardBits, Dice, PwnedWord, NatWord;
            // Wibou's own (CharacterPackages/wibou/Presentation).
            public Material Glow, Glint, Pixel, Glyphs, Slash, Ring, Burst;
        }

        [CliCommand("sloparena.presentation.course.mall.build", "Rebuild Neo Mall's mannequin agent and boss targets, cuts, breaks, checkpoint, beam and finish prefabs and its Resources/Courses set in Edit Mode.", MainThreadRequired = true, Tags = new[] { "presentation", "arcade" })]
        public static object Build()
        {
            using var kit = PresentationBuilderKit.ForFolder(Root, audioFolder: "CourseMall");
            _kit = kit;
            _m = BuildMaterials();
            _quad = Quad("MallQuad", -.5f);
            _beamQuad = Quad("MallBeamQuad", 0f);
            _targets = TargetsModel();
            var set = AssetDatabase.LoadAssetAtPath<CoursePresentationSet>(SetPath);
            bool created = set == null;
            if (created) set = ScriptableObject.CreateInstance<CoursePresentationSet>();
            set.Target = Agent("MallAgent", -1.1f);
            set.TargetRadius = TargetRadius;
            set.Break = AgentBreak();
            set.Marker = CheckpointMarker();
            set.MissedBeacon = Beacon("MallMissedBeam", _m.BeamRed, new Vector2(.9f, 16f), _m.RingRed, 1.7f, floorRing: false,
                _m.MoteRed, 5f, .2f, new Vector2(1.4f, 2.6f), 2f);
            set.FinishBeacon = Beacon("MallGameStoreBeam", _m.BeamPurple, new Vector2(2.4f, 30f), _m.RingGreen, 5f, floorRing: true,
                _m.MoteGreen, 12f, .8f, new Vector2(2f, 4.5f), 3f);
            set.FinishBurst = FinishBurst();
            set.SoundFolder = SoundFolder;
            // A minor-pentatonic synth arpeggio climbing break by break.
            set.NoteSemitones = new[] { 0, 3, 5, 7, 10, 12, 15, 17, 19, 22 };
            set.TargetLooks = new[]
            {
                new CoursePresentationSet.TargetLook { IdPrefix = "escalator", Target = Agent("MallAgentRiding", -.8f), TargetRadius = TargetRadius },
                new CoursePresentationSet.TargetLook { IdPrefix = "peek", Target = Agent("MallAgentPeeking", -1.4f), TargetRadius = TargetRadius },
                new CoursePresentationSet.TargetLook { IdPrefix = "lip", Target = Hanging(), TargetRadius = TargetRadius },
                Look("tubeman", Boss("MallTubeman", "Tubeman", 180f, 9f), BossHit("MallTubemanHit", _m.TubeBits, 6, .4f),
                    BossBreak("MallTubemanBreak", 1f, (_m.TubeBits, 26, new Vector2(.2f, .4f), .35f)), "course_tubeman"),
                Look("agent", Boss("MallTheAgent", "Giant", 270f, 0f), BossHit("MallTheAgentHit", _m.MannequinBits, 6, 1.2f),
                    BossBreak("MallTheAgentBreak", 1.1f, (_m.MannequinBits, 24, new Vector2(.22f, .42f), 1.2f)), "course_agent"),
                Look("hotdog", Boss("MallFrank", "Frank", 180f, 2f), BossHit("MallFrankHit", _m.FoodBits, 7, 1f),
                    BossBreak("MallFrankBreak", 1f, (_m.FoodBits, 30, new Vector2(.16f, .34f), 1f)), "course_hotdog"),
                Look("cop", Boss("MallDoug", "Doug", 180f, 1.5f), BossHit("MallDougHit", _m.CardBits, 6, .6f),
                    BossBreak("MallDougBreak", 1f, (_m.CardBits, 28, new Vector2(.2f, .38f), .6f)), "course_cop"),
            };
            if (created)
            {
                PresentationBuilderKit.EnsureFolder("Assets/Resources/" + CoursePresentationSet.ResourcesFolder);
                AssetDatabase.CreateAsset(set, SetPath);
            }
            else EditorUtility.SetDirty(set);
            return kit.Close(new { set = SetPath, looks = set.TargetLooks.Select(l => l.IdPrefix).ToArray() });
        }

        [MenuItem("Tools/SLOPArena/Presentation/Rebuild Neo Mall Course Presentation")]
        private static void Menu() => Debug.Log("[MallCoursePresentationBuilder] " + Newtonsoft.Json.JsonConvert.SerializeObject(Build()));

        private static CoursePresentationSet.TargetLook Look(string prefix, GameObject target, GameObject hit, GameObject brk, string clips)
            => new() { IdPrefix = prefix, Target = target, TargetRadius = BossRadius, Hit = hit, Break = brk, HitClip = clips + "_hit", BreakClip = clips + "_break" };

        // ── Agents ───────────────────────────────────────────────────────────────

        /// <summary>
        /// A mannequin in a black suit and shades, turned to the camera. The Body child carries its
        /// feet <paramref name="feet"/> metres under the target centre (CourseLantern moves Model).
        /// A faint green glow sits behind it so it reads against the shopfronts.
        /// </summary>
        private static GameObject Agent(string name, float feet)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            var lantern = root.AddComponent<CourseLantern>();
            lantern.BobHeight = .012f;
            lantern.BobHz = .45f;
            lantern.SwayDegrees = 1.5f;
            var model = Child(root, "Model");
            lantern.Model = model.transform;
            var body = Child(model, "Body");
            body.transform.localPosition = new Vector3(0f, feet, 0f);
            Part(body, "Agent", Vector3.zero);
            var halo = Child(root, "Halo");
            halo.transform.localScale = new Vector3(1.6f, 2.6f, 1f);
            AddMesh(halo, _quad, _m.Halo);
            lantern.Halo = halo.transform;
            lantern.HaloDepth = .45f;
            return Save(root);
        }

        /// <summary>An agent clinging to the elevator tower's lip: authored facing, the ledge at +X.</summary>
        private static GameObject Hanging()
        {
            var root = _kit.NewRoot("MallAgentHanging", attackBound: false);
            var bonus = root.AddComponent<CourseBonusTarget>();
            var model = Child(root, "Model");
            bonus.Model = model.transform;
            bonus.JoltDegrees = 6f;
            bonus.IdleSwayDegrees = 2.5f;
            bonus.IdleSwayHz = .5f;
            bonus.JoltAxis = new Vector3(0f, 0f, 1f);
            bonus.Stages = new[] { Part(model, "AgentHang", Vector3.zero) };
            return Save(root);
        }

        /// <summary>A boss in its four damage stages, facing the way the player meets it.</summary>
        private static GameObject Boss(string name, string prop, float yaw, float sway)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var bonus = root.AddComponent<CourseBonusTarget>();
            // Pivot the jolt at the boss's feet, so a hit rocks it on its base.
            var pivot = new Vector3(0f, -1.4f, 0f);
            var model = Child(root, "Model");
            model.transform.localPosition = pivot;
            bonus.Model = model.transform;
            bonus.JoltDegrees = 7f;
            bonus.IdleSwayDegrees = sway;
            bonus.IdleSwayHz = .55f;
            bonus.Stages = Enumerable.Range(0, 4).Select(i => Part(model, $"{prop} {i}", -pivot)).ToArray();
            return Save(root);
        }

        private static GameObject Part(GameObject parent, string objectName, Vector3 offset)
        {
            var source = _targets.transform.Find(objectName)
                ?? throw new InvalidOperationException($"{TargetsModelPath} has no {objectName}; run tools/blender/wibou_mall/targets.py.");
            var part = Child(parent, objectName);
            part.transform.localPosition = offset + source.localPosition;
            part.transform.localRotation = source.localRotation;
            part.transform.localScale = source.localScale;
            part.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return part;
        }

        /// <summary>targets.py's FBX with the mall's own materials (the stage's atlas and glow).</summary>
        private static GameObject TargetsModel()
        {
            AssetDatabase.ImportAsset(TargetsModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(TargetsModelPath) as ModelImporter
                ?? throw new InvalidOperationException($"Missing {TargetsModelPath}; run tools/blender/wibou_mall/targets.py.");
            var materials = new Dictionary<string, string>
            {
                ["NM_Lit"] = "NeoMallLit", ["NM_Ground"] = "NeoMallGroundSurface", ["NM_Glass"] = "NeoMallGlass",
                ["NM_Glow"] = "NeoMallGlow", ["NM_Sky"] = "NeoMallSky",
            };
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
            importer.generateSecondaryUV = false;   // TEXCOORD1 carries StageNeon's animation channel
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var pair in materials)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(MallArt + pair.Value + ".mat")
                    ?? throw new InvalidOperationException($"Missing {MallArt}{pair.Value}.mat; apply the mall's stage art first.");
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), material);
            }
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(TargetsModelPath);
        }

        // ── Cuts and breaks ──────────────────────────────────────────────────────

        /// <summary>
        /// A mannequin cut down: Wibou's slash across it, a glint, the white halves and black suit
        /// scraps flying, and the agent dissolving into green pixels and falling code.
        /// </summary>
        private static GameObject AgentBreak()
        {
            var root = _kit.NewRoot("MallAgentBreak", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Slash(root, "Slash", 2.4f, .34f, 28f, 0f, scaled);
            Pop(root, "Burst", _m.Burst, 1.3f, .16f, scaled);
            Pop(root, "Glint", _m.Glint, .9f, .22f, scaled, delay: .05f);
            Bits(root, "Halves", _m.MannequinBits, 10, new Vector2(.28f, .48f), new Vector2(2.5f, 5f), 1.4f, 1.1f, scaled);
            Pixels(root, "Pixels", 26, .9f, scaled);
            Code(root, "Code", 18, 1.1f, scaled);
            return Save(root);
        }

        /// <summary>A hit that does not break the boss yet: a slash, Wibou's burst, pixels and a few of its own bits.</summary>
        private static GameObject BossHit(string name, Material bits, int count, float gravity)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Slash(root, "Slash", 2.6f, .36f, -30f, 0f, scaled);
            Pop(root, "Burst", _m.Burst, 1.6f, .15f, scaled);
            Bits(root, "Bits", bits, count, new Vector2(.14f, .26f), new Vector2(2.5f, 5f), gravity, .9f, scaled);
            Pixels(root, "Pixels", 12, .7f, scaled);
            return Save(root);
        }

        /// <summary>The fifth hit: two crossing slashes, a flash and ring, the boss's bits flying, a column of code and PWNED!</summary>
        private static GameObject BossBreak(string name, float size, params (Material material, int count, Vector2 sizes, float gravity)[] bits)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Pop(root, "Flash", _m.Glow, 4.2f * size, .18f, scaled);
            Slash(root, "Slash", 4.2f * size, .5f, 32f, 0f, scaled);
            Slash(root, "Cross slash", 4.2f * size, .5f, -36f, .05f, scaled);
            Pop(root, "Burst", _m.Burst, 2.6f * size, .22f, scaled);
            RingPop(root, "Ring", _m.Ring, 3.2f * size, .4f, 0f, scaled);
            int k = 0;
            foreach (var (material, count, sizes, gravity) in bits)
                Bits(root, "Bits " + ++k, material, count, sizes * size, new Vector2(3.5f, 7.5f), gravity, 1.8f, scaled);
            Pixels(root, "Pixels", 40, 1.4f * size, scaled);
            Code(root, "Code", 40, 1.6f * size, scaled);
            Word(root, "Pwned", _m.PwnedWord, 2.8f * size, 1.1f, .1f, .9f, scaled);
            return Save(root);
        }

        private static ParticleSystem Slash(GameObject root, string name, float length, float width, float degrees, float delay,
            ParticleSystemScalingMode scaling)
        {
            var slash = Particles(root, name, _m.Slash, delay + .35f, scaling: scaling);
            Burst(slash, 1);
            var main = slash.main;
            main.startDelay = delay;
            main.startLifetime = .2f;
            main.startSize3D = true;
            main.startSizeX = length;
            main.startSizeY = width;
            main.startSizeZ = 1f;
            main.startRotation = degrees * Mathf.Deg2Rad;
            SizeOverLife(slash, Curve((0f, .4f), (.2f, 1.05f), (1f, 1.1f)), Curve((0f, 1f), (.4f, 1f), (1f, .2f)));
            Fade(slash, .7f);
            InFront(slash);
            return slash;
        }

        private static ParticleSystem Pop(GameObject root, string name, Material material, float size, float lifetime,
            ParticleSystemScalingMode scaling, float delay = 0f)
        {
            var pop = Particles(root, name, material, delay + lifetime + .1f, scaling: scaling);
            Burst(pop, 1);
            var main = pop.main;
            main.startDelay = delay;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(pop, Curve((0f, .5f), (.25f, 1.05f), (1f, 1.15f)));
            Fade(pop, .5f);
            InFront(pop);
            return pop;
        }

        private static void RingPop(GameObject root, string name, Material material, float size, float lifetime, float delay,
            ParticleSystemScalingMode scaling)
        {
            var ring = Particles(root, name, material, delay + lifetime + .1f, scaling: scaling);
            Burst(ring, 1);
            var main = ring.main;
            main.startDelay = delay;
            main.startLifetime = lifetime;
            main.startSize = size;
            SizeOverLife(ring, Curve((0f, .3f), (1f, 1.25f)));
            Fade(ring, .4f);
            InFront(ring);
        }

        private static void Bits(GameObject root, string name, Material material, int count, Vector2 sizes, Vector2 speeds, float gravity,
            float lifetime, ParticleSystemScalingMode scaling)
        {
            var bits = Particles(root, name, material, lifetime + .2f, scaling: scaling);
            Burst(bits, count);
            var main = bits.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * .7f, lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(sizes.x, sizes.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speeds.x, speeds.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.maxParticles = Mathf.Max(count, 8);
            Sphere(bits, .3f);
            RandomFrame(bits, 2, 2);
            Spin(bits, 420f);
            Fade(bits, .7f);
        }

        /// <summary>Wibou's green pixel squares bursting out and falling.</summary>
        private static void Pixels(GameObject root, string name, int count, float speed, ParticleSystemScalingMode scaling)
        {
            var pixels = Particles(root, name, _m.Pixel, 1f, scaling: scaling);
            Burst(pixels, count);
            var main = pixels.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.07f, .14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 1.5f, speed * 4f);
            main.gravityModifier = .5f;
            main.maxParticles = Mathf.Max(count, 8);
            Sphere(pixels, .25f);
            Drag(pixels, .2f);
            RandomFrame(pixels, 2, 1);
            SizeOverLife(pixels, Steps(1f, 1f, .7f, .7f, .4f, 0f));
        }

        /// <summary>Flickering green glyphs falling out of the cut: the agent derezzes into code.</summary>
        private static void Code(GameObject root, string name, int count, float height, ParticleSystemScalingMode scaling)
        {
            var glyphs = Particles(root, name, _m.Glyphs, 1.2f, scaling: scaling);
            Burst(glyphs, count);
            var main = glyphs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.55f, .9f);
            main.startSize = new ParticleSystem.MinMaxCurve(.16f, .28f);
            main.startColor = new ParticleSystem.MinMaxGradient(Green, Pale);
            main.maxParticles = Mathf.Max(count, 8);
            var shape = glyphs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(.8f, height, .3f);
            Fall(glyphs, 2f, 3.5f);
            FlickerFrames(glyphs, 4, 4, 15f);
            Fade(glyphs, .6f);
        }

        private static ParticleSystem Word(GameObject root, string name, Material material, float size, float lifetime, float delay,
            float rise, ParticleSystemScalingMode scaling)
        {
            var word = Particles(root, name, material, lifetime + delay + .1f, scaling: scaling);
            Burst(word, 1);
            var main = word.main;
            main.startDelay = delay;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.12f, .12f);
            var velocity = word.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            var climb = Curve((0f, 1.6f), (.35f, .6f), (1f, .3f));
            velocity.x = new ParticleSystem.MinMaxCurve(0f, climb);
            velocity.y = new ParticleSystem.MinMaxCurve(rise, climb);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, climb);
            SizeOverLife(word, Curve((0f, .3f), (.1f, 1.18f), (.22f, 1f), (1f, 1.06f)));
            var color = word.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (.05f, 1f), (.72f, 1f), (1f, 0f) });
            InFront(word);
            word.GetComponent<ParticleSystemRenderer>().maxParticleSize = 6f;
            return word;
        }

        // ── Checkpoints, beams and the finish ────────────────────────────────────

        private static GameObject CheckpointMarker()
        {
            var root = _kit.NewRoot("MallCheckpointMarker", attackBound: false);
            var marker = root.AddComponent<CourseCheckpointMarker>();
            var frame = Child(root, "Frame");
            frame.transform.localPosition = new Vector3(0f, .1f, 0f);
            marker.Frame = frame.AddComponent<MeshFilter>();
            marker.FrameRenderer = frame.AddComponent<MeshRenderer>();
            Quiet(marker.FrameRenderer);
            marker.FrameRenderer.sharedMaterial = _m.FrameGreen;
            marker.FrameRenderer.enabled = false;
            marker.ArmedMaterial = _m.FrameGreen;
            marker.LockedMaterial = _m.FrameRed;
            marker.Width = .7f;
            marker.ArmedMotes = Motes(root, "Armed pixels", _m.MoteGreen, 12f);
            marker.LockedMotes = Motes(root, "Locked pixels", _m.MoteRed, 7f);
            return Save(root);
        }

        private static ParticleSystem Motes(GameObject root, string name, Material material, float rate)
        {
            var motes = Particles(root, name, material, 2f);
            var main = motes.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.1f, .18f);
            main.maxParticles = 48;
            var emission = motes.emission;
            emission.rateOverTime = rate;
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.BoxEdge;
            shape.scale = new Vector3(6f, .02f, 2.8f);
            Drift(motes, .1f, .8f, 1.6f);
            SizeOverLife(motes, Steps(1f, 1f, .7f, .7f, .4f, 0f));
            Fade(motes, .55f);
            return motes;
        }

        /// <summary>A beam of light that faces the camera, with a ripple ring and rising pixels.</summary>
        private static GameObject Beacon(string name, Material beam, Vector2 size, Material ring, float ringSize, bool floorRing,
            Material mote, float moteRate, float moteRadius, Vector2 moteSpeed, float moteLife)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            var beacon = root.AddComponent<CourseBeacon>();
            var pillar = Child(root, "Beam");
            pillar.transform.localScale = new Vector3(size.x, size.y, 1f);
            AddMesh(pillar, _beamQuad, beam);
            beacon.Beam = pillar.transform;

            var ripple = Particles(root, "Ripple", ring, 1f);
            Burst(ripple, 1);
            var main = ripple.main;
            main.loop = true;
            main.startLifetime = 1f;
            main.startSize = ringSize;
            main.maxParticles = 4;
            SizeOverLife(ripple, Curve((0f, .55f), (1f, 1.35f)));
            Fade(ripple, .25f);
            if (floorRing)
            {
                ripple.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                ripple.transform.localPosition = new Vector3(0f, .05f, 0f);
            }

            var motes = Particles(root, "Pixels", mote, 2f);
            main = motes.main;
            main.loop = true;
            main.startLifetime = moteLife;
            main.startSize = new ParticleSystem.MinMaxCurve(.08f, .16f);
            main.maxParticles = 64;
            var emission = motes.emission;
            emission.rateOverTime = moteRate;
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = moteRadius;
            shape.rotation = new Vector3(90f, 0f, 0f);
            Drift(motes, .06f, moteSpeed.x, moteSpeed.y);
            Fade(motes, .6f);
            return Save(root);
        }

        /// <summary>
        /// The game store door: Wibou's flash and ring, a column of code, a shower of dice and pixels,
        /// NAT 20! climbing out of it and floor rings.
        /// </summary>
        private static GameObject FinishBurst()
        {
            var root = _kit.NewRoot("MallFinishBurst", attackBound: false);
            var flash = Pop(root, "Flash", _m.Glow, 6f, .25f, ParticleSystemScalingMode.Local);
            flash.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Behind(flash);
            var ring = Particles(root, "Ring", _m.Ring, .6f);
            ring.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = .5f;
            main.startSize = 5f;
            SizeOverLife(ring, Curve((0f, .3f), (1f, 1.3f)));
            Fade(ring, .4f);

            var dice = Particles(root, "Dice", _m.Dice, 4.5f);
            dice.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            Burst(dice, 44);
            main = dice.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(.22f, .38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = .9f;
            main.maxParticles = 64;
            var shape = dice.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = .4f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            RandomFrame(dice, 2, 2);
            Spin(dice, 540f);
            Fade(dice, .8f);

            Pixels(root, "Pixels", 60, 1.6f, ParticleSystemScalingMode.Local);
            var code = Particles(root, "Code column", _m.Glyphs, 2f);
            code.transform.localPosition = new Vector3(0f, 3.4f, 0f);
            Burst(code, 50);
            main = code.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(.2f, .34f);
            main.startColor = new ParticleSystem.MinMaxGradient(Green, Pale);
            main.maxParticles = 64;
            shape = code.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(2.4f, 3f, .6f);
            Fall(code, 2f, 3.5f);
            FlickerFrames(code, 4, 4, 15f);
            Fade(code, .6f);

            var word = Word(root, "Nat 20", _m.NatWord, 4.6f, 1.6f, .1f, 1.1f, ParticleSystemScalingMode.Local);
            word.transform.localPosition = new Vector3(0f, 2.6f, 0f);

            var floor = Child(root, "Floor");
            floor.transform.localPosition = new Vector3(0f, -.85f, 0f);
            floor.AddComponent<SnapToFloor>().MaxDrop = 3f;
            FloorRing(floor, "Green ring", _m.RingGreen, 6f, 0f, .8f);
            FloorRing(floor, "Pixel ring", _m.Ring, 4f, .1f, .8f);
            return Save(root);
        }

        private static void FloorRing(GameObject parent, string name, Material material, float size, float delay, float life)
        {
            var ring = Particles(parent, name, material, delay + life + .1f);
            Burst(ring, 1);
            var main = ring.main;
            main.startDelay = delay;
            main.startLifetime = life;
            main.startSize = size;
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeOverLife(ring, Curve((0f, .25f), (.3f, .8f), (1f, 1f)));
            Fade(ring, .3f);
        }

        /// <summary>A unit quad in the XY plane, centred (bottom = -0.5) or standing on its base (bottom = 0).</summary>
        private static Mesh Quad(string name, float bottom)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(new[] { new Vector3(-.5f, bottom, 0f), new Vector3(.5f, bottom, 0f), new Vector3(.5f, bottom + 1f, 0f), new Vector3(-.5f, bottom + 1f, 0f) });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        // ── Materials and textures ───────────────────────────────────────────────

        private static Materials BuildMaterials()
        {
            var glow = _kit.BakeTexture("MallGlow", 128, 128, (u, v) => Alpha(Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f)));
            return new Materials
            {
                Halo = _kit.ParticleMaterial("MallAgentHalo", glow, new Color(Green.r, Green.g, Green.b, .22f)),
                FrameGreen = Mat("MallFrameGreen", 8, 64, (_, v) => FramePixel(v, GreenDark, Green, Pale)),
                FrameRed = Mat("MallFrameRed", 8, 64, (_, v) => FramePixel(v, RedDark, Red, RedLight)),
                RingGreen = Mat("MallRingGreen", 256, 256, (u, v) => RingPixel(P(u, v), GreenDark, Green, Pale)),
                RingRed = Mat("MallRingRed", 256, 256, (u, v) => RingPixel(P(u, v), RedDark, Red, RedLight)),
                MoteGreen = Mat("MallMoteGreen", 32, 32, (u, v) => PixelSquare(P(u, v), GreenDark, Green, Pale)),
                MoteRed = Mat("MallMoteRed", 32, 32, (u, v) => PixelSquare(P(u, v), RedDark, Red, RedLight)),
                BeamRed = Mat("MallBeamRed", 64, 256, (u, v) => BeamPixel(u, v, RedDark, Red, Cream)),
                BeamPurple = Mat("MallBeamPurple", 64, 256, (u, v) => BeamPixel(u, v, PurpleDark, Purple, PurpleLight)),
                MannequinBits = Mat("MallMannequinBits", 256, 256, (u, v) => Sheet(u, v, MannequinBitPixel)),
                TubeBits = Mat("MallTubeBits", 256, 256, (u, v) => Sheet(u, v, TubeBitPixel)),
                FoodBits = Mat("MallFoodBits", 256, 256, (u, v) => Sheet(u, v, FoodBitPixel)),
                CardBits = Mat("MallCardBits", 256, 256, (u, v) => Sheet(u, v, CardBitPixel)),
                Dice = Mat("MallDice", 256, 256, (u, v) => Sheet(u, v, DicePixel)),
                PwnedWord = WordMaterial("MallPwnedWord", Root + "/Source/Pwned.png", Ink, Green, Pale),
                NatWord = WordMaterial("MallNat20Word", Root + "/Source/Nat20.png", PurpleDark, Purple, Cream),
                Glow = Wibou("WibouGlowAdd"),
                Glint = Wibou("WibouGlint"),
                Pixel = Wibou("WibouPixel"),
                Glyphs = Wibou("WibouGlyphs"),
                Slash = Wibou("WibouSlash"),
                Ring = Wibou("WibouRing"),
                Burst = Wibou("WibouBurst"),
            };
        }

        private static Material Wibou(string name)
            => AssetDatabase.LoadAssetAtPath<Material>(WibouFx + name + ".mat")
               ?? throw new InvalidOperationException($"Wibou's {name}.mat is missing; run sloparena.presentation.wibou.build first.");

        /// <summary>A comic word from a lettering mask: dark outline, accent band, pale core.</summary>
        private static Material WordMaterial(string name, string maskPath, Color rim, Color accent, Color core)
        {
            var mask = new MaskField(maskPath);
            float scale = 512f / mask.Width;
            return Mat(name, 512, 512, (u, v) => Bands(mask.Sample(u, v) * scale - 7f, 3.5f, 6f, rim, accent, core));
        }

        /// <summary>Mannequin pieces: a white arm with a hand, a black suit scrap, the shades and a white head half.</summary>
        private static Color MannequinBitPixel(Vector2 p, int cell)
        {
            switch (cell)
            {
                case 0: return Bands(Lens(Rotate(p, 25f), .85f, .22f) * 64f, 2f, 3f, Ink, Mannequin, Color.white);
                case 1: return Bands(Polygon(p, Scrap) * 64f, 2f, 3f, Ink, Suit, Hex(0x34343a));
                case 2:
                {
                    float d = Mathf.Min(Box(p - new Vector2(-.38f, 0f), new Vector2(.3f, .18f)), Box(p - new Vector2(.38f, 0f), new Vector2(.3f, .18f)));
                    d = Mathf.Min(d, Box(p, new Vector2(.12f, .04f)));
                    return Bands(d * 64f, 2f, 99f, Ink, Suit, Suit);
                }
                default:
                {
                    float d = Mathf.Max(p.magnitude - .7f, p.x);
                    return Bands(d * 64f, 2f, 3f, Ink, Mannequin, Color.white);
                }
            }
        }

        /// <summary>Torn tube-man fabric: red and yellow scraps.</summary>
        private static Color TubeBitPixel(Vector2 p, int cell)
        {
            var q = Rotate(p, cell * 31f - 20f);
            float d = Polygon(q, cell % 2 == 0 ? Scrap : Scrap2);
            return Bands(d * 64f, 1.5f, 3f, Ink, cell < 2 ? TubeRed : TubeYellow, cell < 2 ? Hex(0xff7a7a) : Hex(0xffeea0));
        }

        /// <summary>Bun crumbs, a sausage coin, a mustard blob and a fiberglass chip.</summary>
        private static Color FoodBitPixel(Vector2 p, int cell)
        {
            switch (cell)
            {
                case 0: return Bands(Polygon(p, Scrap) * 64f, 2f, 3f, Ink, Bun, Hex(0xf2c98a));
                case 1: return Bands((p.magnitude - .62f) * 64f, 2f, 4f, Ink, Sausage, Hex(0xe0806a));
                case 2: return Bands((p.magnitude - .5f - .12f * Mathf.Sin(Mathf.Atan2(p.y, p.x) * 5f)) * 64f, 2f, 3f, Ink, Hex(0xffd21a), Hex(0xfff0a0));
                default: return Bands(Polygon(Rotate(p, 40f), Scrap2) * 64f, 2f, 3f, Ink, Cream, Color.white);
            }
        }

        /// <summary>Cardboard scraps, one with a bit of Doug's blue uniform, one with his gold badge.</summary>
        private static Color CardBitPixel(Vector2 p, int cell)
        {
            var q = Rotate(p, cell * 27f);
            float d = Polygon(q, cell % 2 == 0 ? Scrap : Scrap2);
            var color = Bands(d * 64f, 2f, 3f, Hex(0x3a2a18), Cardboard, Hex(0xd8b588));
            if (cell == 1 && d < -.05f && q.y < 0f) color = new Color(.12f, .16f, .32f, color.a);
            if (cell == 2 && d < -.05f && (q - new Vector2(.1f, .1f)).magnitude < .22f) color = new Color(.92f, .74f, .22f, color.a);
            if (cell == 3 && d < 0f) color = Color.Lerp(color, new Color(CardboardDark.r, CardboardDark.g, CardboardDark.b, color.a), .5f);
            return color;
        }

        /// <summary>Dice: a purple d20, a red d6, a green d20 and a cream d6 with pips.</summary>
        private static Color DicePixel(Vector2 p, int cell)
        {
            bool d20 = cell % 2 == 0;
            Color body = cell switch { 0 => Purple, 1 => Red, 2 => Green, _ => Cream };
            Color light = Color.Lerp(body, Color.white, .45f);
            if (d20)
            {
                var hex = new Vector2[6];
                for (int i = 0; i < 6; i++) hex[i] = new Vector2(Mathf.Cos(Mathf.PI / 6f + i * Mathf.PI / 3f), Mathf.Sin(Mathf.PI / 6f + i * Mathf.PI / 3f)) * .85f;
                float d = Polygon(p, hex);
                var color = Bands(d * 64f, 2f, 4f, Ink, body, light);
                var tri = new[] { new Vector2(0f, .55f), new Vector2(.48f, -.3f), new Vector2(-.48f, -.3f) };
                if (Mathf.Abs(Polygon(p, tri)) < .03f && d < 0f) color = new Color(Ink.r, Ink.g, Ink.b, color.a);
                return color;
            }
            float b = Box(p, new Vector2(.62f, .62f)) - .12f;
            var c = Bands(b * 64f, 2f, 4f, Ink, body, light);
            foreach (var pip in new[] { new Vector2(-.3f, .3f), new Vector2(0f, 0f), new Vector2(.3f, -.3f) })
                if ((p - pip).magnitude < .1f && b < 0f) c = new Color(Ink.r, Ink.g, Ink.b, c.a);
            return c;
        }

        private static readonly Vector2[] Scrap = { new(-.7f, -.5f), new(.5f, -.7f), new(.75f, .3f), new(.1f, .7f), new(-.6f, .4f) };
        private static readonly Vector2[] Scrap2 = { new(-.8f, .1f), new(.1f, -.75f), new(.8f, .3f), new(-.1f, .6f) };

        private static Color RingPixel(Vector2 p, Color dark, Color accent, Color light)
            => Bands((Mathf.Abs(p.magnitude - .8f) - .085f) * 128f, 2.5f, 4.5f, dark, accent, light);

        /// <summary>A pixel square: Wibou's hacker pixels.</summary>
        private static Color PixelSquare(Vector2 p, Color dark, Color accent, Color light)
            => Bands(Box(p, new Vector2(.62f, .62f)) * 32f, 2f, 4f, dark, accent, light);

        private static Color FramePixel(float v, Color dark, Color accent, Color light)
        {
            float x = Mathf.Abs(v - .5f);
            var line = Bands((x - .2f) * 64f, 1.5f, 8f, dark, accent, light);
            float glow = x > .2f ? .65f * Mathf.Pow(Mathf.Clamp01(1f - (x - .2f) / .3f), 1.6f) : 0f;
            return Over(new Color(accent.r, accent.g, accent.b, glow), line);
        }

        private static Color BeamPixel(float u, float v, Color edge, Color accent, Color core)
        {
            float x = Mathf.Abs(u - .5f) * 2f;
            Color color = Color.Lerp(accent, core, Step(.2f, .14f, x));
            color = Color.Lerp(color, edge, Step(.44f, .5f, x));
            float alpha = x < .2f ? .9f : x < .46f ? .7f : .55f * Mathf.Pow(Mathf.Clamp01(1f - (x - .46f) / .54f), 1.6f);
            color.a = alpha * Mathf.Pow(1f - v, .9f) * Step(0f, .03f, v);
            return color;
        }

        private static Material Mat(string name, int width, int height, Func<float, float, Color> pixel)
            => _kit.ParticleMaterial(name, _kit.BakeTexture(name, width, height, pixel));

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static Color Hex(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        private static Vector2 P(float u, float v) => new(u * 2f - 1f, v * 2f - 1f);

        /// <summary>A 2x2 sheet: each cell gets its own −1..1 coordinates and its index (0 top-left).</summary>
        private static Color Sheet(float u, float v, Func<Vector2, int, Color> cell)
        {
            int cx = u < .5f ? 0 : 1, cy = v < .5f ? 1 : 0;
            var p = new Vector2((u % .5f) * 4f - 1f, (v % .5f) * 4f - 1f);
            return cell(p, cy * 2 + cx);
        }

        private static Color Over(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0f) return new Color(over.r, over.g, over.b, 0f);
            return new Color((over.r * over.a + under.r * under.a * (1f - over.a)) / a,
                (over.g * over.a + under.g * under.a * (1f - over.a)) / a,
                (over.b * over.a + under.b * under.a * (1f - over.a)) / a, a);
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static void AddMesh(GameObject host, Mesh mesh, Material material)
        {
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            Quiet(renderer);
        }

        private static void Quiet(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Saves with unique object names so a rebuild keeps every fileID (see the VFX skill).</summary>
        private static GameObject Save(GameObject root)
        {
            var seen = new Dictionary<string, int>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                string name = transform.name;
                seen.TryGetValue(name, out int count);
                seen[name] = count + 1;
                if (count > 0) transform.name = $"{name} {count + 1}";
            }
            return _kit.Save(root);
        }
    }
}
#endif
