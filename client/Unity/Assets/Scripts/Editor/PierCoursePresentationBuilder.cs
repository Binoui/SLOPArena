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
    /// Rebuilds Fight Night Pier's course feel (FightGuy's course, docs/design/stages/fightguy_pier.design.md):
    /// gold foil 肉 balloons bobbing on red ribbons that pop into foil, stars and a rising 肉; four
    /// bonus targets that take eight hits (the contender's car, the bull piñata, the PUNCH-O-METER
    /// and the champ's heavy bag, modelled by tools/blender/fightguy_pier/bonus.py), each hit
    /// showing the next damage stage and the last one a BONUS! burst; gold checkpoint frames; red
    /// spotlights over missed balloons; and title night at the ring: a gold spotlight, then
    /// CHAMP!, his seal, camera flashes, confetti and foam fingers. Pops and hits reuse FightGuy's
    /// own materials (CharacterPackages/fightguy/Presentation), so the course and his kit hit
    /// things the same way. Writes Stages/fightguy_pier/Presentation/Course and
    /// Resources/Courses/fightguy_pier; tools/sfx/pier_course_clips.py makes the sounds
    /// (Audio/SFX/CoursePier). Lettering masks in Course/Source come from tools/glyph_mask.py:
    /// POP! (Baloo 2 800, --fill 0.86 --bounce 8), BONUS! and CHAMP! (Archivo Black, --fill 0.9 --bounce 5).
    /// </summary>
    public static class PierCoursePresentationBuilder
    {
        private const string Root = "Assets/Stages/fightguy_pier/Presentation/Course";
        private const string SetPath = "Assets/Resources/" + CoursePresentationSet.ResourcesFolder + "/fightguy_pier.asset";
        private const string FightGuyFx = "Assets/CharacterPackages/fightguy/Presentation/";
        private const string PierArt = "Assets/Stages/fightguy_pier/Presentation/Pier/";
        private const string BonusModelPath = Root + "/FightNightPierBonus.fbx";
        private const string NikuMaskPath = FightGuyFx + "Source/Niku.png";
        private const string SoundFolder = "Audio/SFX/CoursePier";
        // The pier's balloons (layout.py balloon_radius); CharacterCoursePresentation scales the prefabs to others.
        private const float TargetRadius = .55f;
        // The balloon body: a lathe profile (radius, height) that nearly fills the hit sphere, the
        // knot under it and the ribbon hanging to RibbonEnd. Its texture is a front projection.
        private static readonly Vector2[] BalloonProfile =
        {
            new(0f, -.5f), new(.08f, -.47f), new(.2f, -.41f), new(.32f, -.3f), new(.42f, -.15f), new(.47f, .02f),
            new(.47f, .14f), new(.43f, .29f), new(.34f, .41f), new(.2f, .49f), new(0f, .53f),
        };
        private const float BalloonHalfWidth = .5f, BalloonBottom = -.55f, BalloonTop = .55f, RibbonEnd = -1.3f;
        // bonus.py's punch machine meter column (prop space, its front facing +Z).
        private const float MeterX0 = .07f, MeterX1 = .21f, MeterY0 = .42f, MeterY1 = 1.02f, MeterZ = -.045f;

        // Fight Night Pier (tools/blender/fightguy_pier/palette.py) and FightGuy's own colours.
        private static readonly Color Ink = new(.08f, .07f, .08f);
        private static readonly Color Cream = new(.95f, .91f, .81f);
        private static readonly Color GoldRim = new(.33f, .17f, 0f);
        private static readonly Color GoldDeep = new(.72f, .47f, .06f);
        private static readonly Color Gold = new(1f, .77f, .19f);
        private static readonly Color GoldLight = new(1f, .9f, .5f);
        private static readonly Color GoldPale = new(1f, .97f, .8f);
        private static readonly Color RedDark = new(.48f, .07f, .06f);
        private static readonly Color Red = new(.84f, .16f, .14f);
        private static readonly Color RedLight = new(1f, .45f, .38f);
        private static readonly Color KiRim = new(.04f, .13f, .36f);
        private static readonly Color KiBlue = new(.25f, .66f, 1f);
        private static readonly Color Teal = new(.1f, .55f, .55f);
        private static readonly Color TealLight = new(.42f, .78f, .74f);
        private static readonly Color Pink = new(.93f, .45f, .62f);
        private static readonly Color Orange = new(.95f, .42f, .17f);
        private static readonly Color Steel = new(.62f, .65f, .7f);
        private static readonly Color Chrome = new(.86f, .87f, .9f);
        private static readonly Color Sand = new(.84f, .7f, .48f);
        private static readonly Color Smoke = new(.42f, .4f, .44f);
        private static readonly Vector3 Light = new Vector3(-.45f, .62f, .64f).normalized;

        private static PresentationBuilderKit _kit;
        private static Materials _m;
        private static Mesh _quad, _beamQuad, _frontQuad;
        private static GameObject _bonusModel;

        private sealed class Materials
        {
            public Material Balloon, Outline, Halo, Pool, PopWord, BonusWord, ChampWord, Foil, Confetti, Candy, Fringe, CarBits,
                Coins, Leather, Spark, RingRed, MoteRed, FrameGold, FrameRed, BeamGold, BeamRed, MeterBar, MeterGlass, Spotlight;
            // FightGuy's own (CharacterPackages/fightguy/Presentation).
            public Material Flash, Impact, ImpactGold, Ring, RingGold, Star, Chalk, Niku, Seal, Foam;
        }

        [CliCommand("sloparena.presentation.course.pier.build", "Rebuild Fight Night Pier's balloon and bonus targets, pops, breaks, checkpoint, spotlight and finish prefabs and its Resources/Courses set in Edit Mode.", MainThreadRequired = true, Tags = new[] { "presentation", "arcade" })]
        public static object Build()
        {
            using var kit = PresentationBuilderKit.ForFolder(Root, audioFolder: "CoursePier");
            _kit = kit;
            _m = BuildMaterials();
            _quad = Quad("PierQuad", -.5f, towardPlusZ: false);
            _beamQuad = Quad("PierBeamQuad", 0f, towardPlusZ: false);
            _frontQuad = Quad("PierFrontQuad", 0f, towardPlusZ: true);
            _bonusModel = BonusModel();
            var set = AssetDatabase.LoadAssetAtPath<CoursePresentationSet>(SetPath);
            bool created = set == null;
            if (created) set = ScriptableObject.CreateInstance<CoursePresentationSet>();
            set.Target = Balloon();
            set.TargetRadius = TargetRadius;
            set.Break = BalloonPop();
            set.Marker = CheckpointMarker();
            set.MissedBeacon = Beacon("PierMissedSpotlight", _m.BeamRed, new Vector2(.9f, 16f), _m.RingRed, 1.7f, floorRing: false,
                _m.MoteRed, 5f, .2f, new Vector2(1.4f, 2.6f), 2f);
            set.FinishBeacon = Beacon("PierTitleSpotlight", _m.BeamGold, new Vector2(2.6f, 44f), _m.RingGold, 5.5f, floorRing: true,
                _m.Star, 12f, .9f, new Vector2(2f, 4.5f), 3f);
            set.FinishBurst = FinishBurst();
            set.SoundFolder = SoundFolder;
            // A fairground organ climbing the major scale, break by break.
            set.NoteSemitones = new[] { 0, 2, 4, 5, 7, 9, 11, 12, 14, 16 };
            set.TargetLooks = new[]
            {
                Look("car", Car(), 1.1f, BonusHit("PierCarHit", _m.CarBits, 6, 1.3f, Smoke), BonusBreak("PierCarBreak", 1.3f, Smoke,
                    (_m.CarBits, 22, new Vector2(.24f, .46f), 1.3f), (_m.Spark, 18, new Vector2(.08f, .16f), .8f)), "course_car"),
                Look("pinata", Pinata(), .9f, BonusHit("PierPinataHit", _m.Candy, 5, 1.1f, null, _m.Fringe), BonusBreak("PierPinataBreak", 1.1f, Cream,
                    (_m.Candy, 32, new Vector2(.14f, .24f), 1.1f), (_m.Fringe, 26, new Vector2(.16f, .3f), .3f)), "course_pinata"),
                Look("punch", PunchMachine(), .75f, BonusHit("PierPunchHit", _m.Spark, 9, .7f, null), BonusBreak("PierPunchBreak", 1f, Smoke,
                    (_m.Coins, 28, new Vector2(.12f, .2f), 1.2f), (_m.Spark, 20, new Vector2(.08f, .16f), .8f)), "course_punch"),
                Look("bag", HeavyBag(), .75f, BonusHit("PierBagHit", null, 0, 0f, Sand), BonusBreak("PierBagBreak", 1f, Sand,
                    (_m.Leather, 18, new Vector2(.18f, .32f), 1.2f)), "course_bag"),
            };
            if (created)
            {
                PresentationBuilderKit.EnsureFolder("Assets/Resources/" + CoursePresentationSet.ResourcesFolder);
                AssetDatabase.CreateAsset(set, SetPath);
            }
            else EditorUtility.SetDirty(set);
            return kit.Close(new { set = SetPath, looks = set.TargetLooks.Select(l => l.IdPrefix).ToArray() });
        }

        [MenuItem("Tools/SLOPArena/Presentation/Rebuild Fight Night Pier Course Presentation")]
        private static void Menu() => Debug.Log("[PierCoursePresentationBuilder] " + Newtonsoft.Json.JsonConvert.SerializeObject(Build()));

        private static CoursePresentationSet.TargetLook Look(string prefix, GameObject target, float radius, GameObject hit, GameObject brk, string clips)
            => new() { IdPrefix = prefix, Target = target, TargetRadius = radius, Hit = hit, Break = brk, HitClip = clips + "_hit", BreakClip = clips + "_break" };

        // ── Balloons ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A gold foil balloon with a red 肉 sticker, a dark outline and a curly red ribbon,
        /// glinting now and then, with a gold glow behind it and a pool of light on the deck.
        /// </summary>
        private static GameObject Balloon()
        {
            var root = _kit.NewRoot("PierBalloon", attackBound: false);
            var lantern = root.AddComponent<CourseLantern>();
            lantern.BobHeight = .07f;
            lantern.BobHz = .36f;
            lantern.SwayDegrees = 5f;
            var model = Child(root, "Model");
            lantern.Model = model.transform;
            AddMesh(Child(model, "Balloon"), BalloonMesh(), _m.Balloon);
            AddMesh(Child(model, "Outline"), BalloonOutlineMesh(), _m.Outline);

            var glints = Particles(model, "Glints", _m.Star, 1f, ParticleSystemSimulationSpace.Local);
            var main = glints.main;
            main.loop = true;
            main.startLifetime = .45f;
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 4;
            var emission = glints.emission;
            emission.rateOverTime = 1.2f;
            var shape = glints.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = .3f;
            glints.transform.localPosition = new Vector3(0f, .08f, .5f);
            SizeOverLife(glints, Curve((0f, 0f), (.35f, 1f), (1f, 0f)));
            InFront(glints);

            var halo = Child(root, "Halo");
            halo.transform.localScale = Vector3.one * 2.3f;
            AddMesh(halo, _quad, _m.Halo);
            lantern.Halo = halo.transform;
            var pool = Child(root, "Light pool");
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pool.transform.localScale = Vector3.one * 1.8f;
            var snap = pool.AddComponent<SnapToFloor>();
            snap.MaxDrop = 4f;
            snap.Lift = .04f;
            AddMesh(pool, _quad, _m.Pool);
            return Save(root);
        }

        /// <summary>
        /// The pop: FightGuy's flash and a comic POP!, gold foil shreds fluttering down with
        /// the ribbon, his gold ring and stars, and his gold 肉 floating up out of it.
        /// </summary>
        private static GameObject BalloonPop()
        {
            var root = _kit.NewRoot("PierBalloonPop", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var flash = Particles(root, "Flash", _m.Flash, .3f, scaling: scaled);
            Burst(flash, 1);
            var main = flash.main;
            main.startLifetime = .13f;
            main.startSize = 2.3f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(flash, Curve((0f, .6f), (.3f, 1f), (1f, 1.1f)));
            Fade(flash, .3f);
            Behind(flash);

            Word(root, "Pop", _m.PopWord, 1.9f, .5f, 0f, .6f, scaled);
            RingPop(root, "Ring", _m.RingGold, 1.6f, .32f, 0f, scaled);

            var foil = Particles(root, "Foil", _m.Foil, 1.9f, scaling: scaled);
            Burst(foil, 16);
            main = foil.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(.14f, .27f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = .5f;
            Sphere(foil, .3f);
            Drag(foil, .3f);
            RandomFrame(foil, 2, 2);
            Spin(foil, 520f);
            Fade(foil, .7f);

            Sparkles(root, 7, .28f, scaled);
            Rising(root, "Niku", _m.Niku, .85f, .08f, .95f, .9f, scaled);
            return Save(root);
        }

        // ── Bonus targets ────────────────────────────────────────────────────────

        /// <summary>The contender's sedan in the pen by the gate, its driver's side to the start.</summary>
        private static GameObject Car()
        {
            var (root, bonus) = BonusTarget("PierCar", "Car", -90f, new Vector3(0f, -.9f, 0f));
            bonus.JoltDegrees = 4f;
            bonus.JoltSeconds = .3f;
            bonus.JoltAxis = new Vector3(1f, 0f, 0f);
            Pool(root, 4.6f, 1.6f);
            return Save(root);
        }

        /// <summary>The bull piñata swinging on the derrick's rope, its flank to the landing.</summary>
        private static GameObject Pinata()
        {
            var (root, bonus) = BonusTarget("PierPinata", "Pinata", 90f, new Vector3(0f, 1.1f, 0f));
            bonus.JoltDegrees = 16f;
            bonus.JoltSeconds = .9f;
            bonus.JoltAxis = new Vector3(1f, 0f, .25f);
            bonus.IdleSwayDegrees = 4f;
            bonus.IdleSwayHz = .35f;
            Pool(root, 2.6f, 2.4f);
            return Save(root);
        }

        /// <summary>
        /// The PUNCH-O-METER on the VIP tower, its pad to the stairs side. Its meter climbs from
        /// WIMP to FIGHTGUY with the hits: a glowing bar behind ringed glass.
        /// </summary>
        private static GameObject PunchMachine()
        {
            var pivot = new Vector3(0f, -1f, 0f);
            var (root, bonus) = BonusTarget("PierPunchMachine", "Punch", -90f, pivot);
            bonus.JoltDegrees = 3f;
            bonus.JoltSeconds = .25f;
            bonus.JoltAxis = new Vector3(1f, 0f, 0f);
            var model = bonus.Model.gameObject;
            var meter = Child(model, "Meter");
            meter.transform.localPosition = new Vector3((MeterX0 + MeterX1) / 2f, MeterY0, MeterZ) - pivot;
            var bar = Child(meter, "Bar");
            bar.transform.localScale = new Vector3(MeterX1 - MeterX0 - .04f, MeterY1 - MeterY0, 1f);
            AddMesh(bar, _frontQuad, _m.MeterBar);
            bonus.Meter = meter.transform;
            bonus.MeterEmpty = .06f;
            var glass = Child(model, "Meter glass");
            glass.transform.localPosition = meter.transform.localPosition + new Vector3(0f, 0f, .006f);
            glass.transform.localScale = new Vector3(MeterX1 - MeterX0 - .02f, MeterY1 - MeterY0, 1f);
            AddMesh(glass, _frontQuad, _m.MeterGlass);
            Pool(root, 2.4f, 1.6f);
            return Save(root);
        }

        /// <summary>The champ's heavy bag on the bag frame by the ring, its 肉 patch to the pier head.</summary>
        private static GameObject HeavyBag()
        {
            var (root, bonus) = BonusTarget("PierHeavyBag", "Bag", 0f, new Vector3(0f, 1f, 0f));
            bonus.JoltDegrees = 12f;
            bonus.JoltSeconds = .8f;
            bonus.JoltAxis = new Vector3(1f, 0f, 0f);
            bonus.IdleSwayDegrees = 1.5f;
            bonus.IdleSwayHz = .3f;
            Pool(root, 2.2f, 2f);
            return Save(root);
        }

        /// <summary>
        /// A bonus target root turned to its spot, with its Model at the swing or rock pivot and
        /// bonus.py's four damage stages under it, moved back so prop space lands on the root.
        /// </summary>
        private static (GameObject root, CourseBonusTarget bonus) BonusTarget(string name, string prop, float yaw, Vector3 pivot)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var bonus = root.AddComponent<CourseBonusTarget>();
            var model = Child(root, "Model");
            model.transform.localPosition = pivot;
            bonus.Model = model.transform;
            bonus.Stages = Enumerable.Range(0, 4).Select(i => Stage(model, $"{prop} {i}", -pivot)).ToArray();
            return (root, bonus);
        }

        private static GameObject Stage(GameObject model, string objectName, Vector3 offset)
        {
            var source = _bonusModel.transform.Find(objectName)
                ?? throw new InvalidOperationException($"{BonusModelPath} has no {objectName}; run tools/blender/fightguy_pier/bonus.py.");
            var stage = Child(model, objectName);
            stage.transform.localPosition = offset + source.localPosition;
            stage.transform.localRotation = source.localRotation;
            stage.transform.localScale = source.localScale;
            stage.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var renderer = stage.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
            // Solid props on a lit pier: they cast and take the dusk sun's shadows.
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return stage;
        }

        /// <summary>A gold pool of light on the deck under a bonus target: it is on tonight's card.</summary>
        private static void Pool(GameObject root, float size, float maxDrop)
        {
            var pool = Child(root, "Light pool");
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pool.transform.localScale = Vector3.one * size;
            var snap = pool.AddComponent<SnapToFloor>();
            snap.MaxDrop = maxDrop;
            snap.Lift = .04f;
            AddMesh(pool, _quad, _m.Pool);
        }

        /// <summary>bonus.py's FBX with the pier's own materials (the stage's atlas and glow).</summary>
        private static GameObject BonusModel()
        {
            AssetDatabase.ImportAsset(BonusModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(BonusModelPath) as ModelImporter
                ?? throw new InvalidOperationException($"Missing {BonusModelPath}; run tools/blender/fightguy_pier/bonus.py.");
            var materials = new Dictionary<string, string>
            {
                ["FNP_Lit"] = "FightNightPierLit", ["FNP_Ground"] = "FightNightPierGroundSurface", ["FNP_Glow"] = "FightNightPierGlow",
                ["FNP_Water"] = "FightNightPierWater", ["FNP_Sky"] = "FightNightPierSky",
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
                var material = AssetDatabase.LoadAssetAtPath<Material>(PierArt + pair.Value + ".mat")
                    ?? throw new InvalidOperationException($"Missing {PierArt}{pair.Value}.mat; apply the pier's stage art first.");
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), material);
            }
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(BonusModelPath);
        }

        /// <summary>
        /// A hit that does not break yet: FightGuy's blue impact and flash, a chalk puff, a few
        /// stars and a handful of the target's own bits (glass, candy, sparks or sand).
        /// </summary>
        private static GameObject BonusHit(string name, Material bits, int count, float gravity, Color? dust, Material flutter = null)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Pop(root, "Impact", _m.Impact, 1.3f, .17f, scaled);
            var flash = Pop(root, "Flash", _m.Flash, .9f, .1f, scaled);
            Behind(flash);
            if (bits != null && count > 0)
                Bits(root, "Bits", bits, count, new Vector2(.12f, .22f), new Vector2(2.5f, 5f), gravity, .9f, scaled);
            if (flutter != null)
                Bits(root, "Flutter", flutter, 5, new Vector2(.14f, .24f), new Vector2(1.5f, 3f), .3f, 1.2f, scaled);
            Puffs(root, "Dust", _m.Chalk, dust.HasValue ? 5 : 2, new Vector2(.35f, .6f), dust ?? Color.white, scaled);
            Sparkles(root, 3, .2f, scaled);
            return Save(root);
        }

        /// <summary>
        /// The eighth hit: a big flash, FightGuy's gold impact and ring, the target's bits flying,
        /// smoke, paper dust or sand, a shower of stars and BONUS! climbing out of it.
        /// </summary>
        private static GameObject BonusBreak(string name, float size, Color smoke, params (Material material, int count, Vector2 sizes, float gravity)[] bits)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var flash = Pop(root, "Flash", _m.Flash, 3.4f * size, .16f, scaled);
            Behind(flash);
            Pop(root, "Impact", _m.ImpactGold, 2.6f * size, .3f, scaled);
            RingPop(root, "Ring", _m.RingGold, 2.6f * size, .4f, 0f, scaled);
            RingPop(root, "Ki ring", _m.Ring, 1.8f * size, .35f, .06f, scaled);
            int k = 0;
            foreach (var (material, count, sizes, gravity) in bits)
                Bits(root, "Bits " + ++k, material, count, sizes * size, new Vector2(3.5f, 7.5f), gravity, 1.8f, scaled);
            Puffs(root, "Smoke", _m.Chalk, 10, new Vector2(.7f, 1.1f) * size, smoke, scaled);
            Sparkles(root, 12, .4f * size, scaled);
            Word(root, "Bonus", _m.BonusWord, 2.6f * size, 1.1f, .08f, .9f, scaled);
            return Save(root);
        }

        // ── Checkpoints, spotlights and the finish ───────────────────────────────

        /// <summary>A floor frame sized at runtime: gold when crossing it will save, red when it cannot.</summary>
        private static GameObject CheckpointMarker()
        {
            var root = _kit.NewRoot("PierCheckpointMarker", attackBound: false);
            var marker = root.AddComponent<CourseCheckpointMarker>();
            var frame = Child(root, "Frame");
            frame.transform.localPosition = new Vector3(0f, .08f, 0f);
            marker.Frame = frame.AddComponent<MeshFilter>();
            marker.FrameRenderer = frame.AddComponent<MeshRenderer>();
            Quiet(marker.FrameRenderer);
            marker.FrameRenderer.sharedMaterial = _m.FrameGold;
            marker.FrameRenderer.enabled = false;
            marker.ArmedMaterial = _m.FrameGold;
            marker.LockedMaterial = _m.FrameRed;
            marker.Width = .7f;
            marker.ArmedMotes = Motes(root, "Armed motes", _m.Star, 12f);
            marker.LockedMotes = Motes(root, "Locked motes", _m.MoteRed, 7f);
            return Save(root);
        }

        private static ParticleSystem Motes(GameObject root, string name, Material material, float rate)
        {
            var motes = Particles(root, name, material, 2f);
            var main = motes.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .2f);
            main.maxParticles = 48;
            var emission = motes.emission;
            emission.rateOverTime = rate;
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.BoxEdge;
            shape.scale = new Vector3(6f, .02f, 2.8f);
            Drift(motes, .1f, .8f, 1.6f);
            Spin(motes, 160f);
            SizeOverLife(motes, Curve((0f, .4f), (.2f, 1f), (1f, .5f)));
            Fade(motes, .55f);
            return motes;
        }

        /// <summary>A spotlight beam that faces the camera, with a ripple ring and rising motes.</summary>
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

            var motes = Particles(root, "Motes", mote, 2f);
            main = motes.main;
            main.loop = true;
            main.startLifetime = moteLife;
            main.startSize = new ParticleSystem.MinMaxCurve(.1f, .2f);
            main.maxParticles = 64;
            var emission = motes.emission;
            emission.rateOverTime = moteRate;
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = moteRadius;
            shape.rotation = new Vector3(90f, 0f, 0f);
            Drift(motes, .06f, moteSpeed.x, moteSpeed.y);
            Spin(motes, 200f);
            Fade(motes, .6f);
            return Save(root);
        }

        /// <summary>
        /// Into the ring: title night. FightGuy's flash, a giant CHAMP!, his gold seal rising,
        /// camera flashes popping round the ring, confetti in his colours, foam #1 fingers thrown
        /// up from the crowd, stars and gold and blue rings across the canvas.
        /// </summary>
        private static GameObject FinishBurst()
        {
            var root = _kit.NewRoot("PierFinishBurst", attackBound: false);
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            var flash = Particles(root, "Flash", _m.Flash, .4f);
            flash.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Burst(flash, 1);
            var main = flash.main;
            main.startLifetime = .2f;
            main.startSize = 6.5f;
            SizeOverLife(flash, Curve((0f, .5f), (.3f, 1f), (1f, 1.1f)));
            Fade(flash, .3f);
            Behind(flash);

            var word = Word(root, "Champ", _m.ChampWord, 5.8f, 1.7f, .05f, 1.2f, local);
            word.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            var seal = Rising(root, "Seal", _m.Seal, 2.4f, .4f, 1.6f, 1.1f, local);
            seal.transform.localPosition = new Vector3(0f, 1.2f, 0f);

            var cameras = Particles(root, "Camera flashes", _m.Flash, 1.8f);
            cameras.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            main = cameras.main;
            main.startDelay = .25f;
            main.startLifetime = .12f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.1f, 1.9f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = cameras.emission;
            emission.rateOverTime = 5f;
            var shape = cameras.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 3.4f;
            shape.radiusThickness = .2f;
            SizeOverLife(cameras, Curve((0f, .5f), (.3f, 1f), (1f, 1.1f)));
            Fade(cameras, .3f);

            foreach (var (label, material, count, sizes, gravity) in new[]
                     {
                         ("Confetti", _m.Confetti, 90, new Vector2(.18f, .32f), .4f),
                         ("Foam fingers", _m.Foam, 5, new Vector2(.7f, .9f), .9f),
                     })
            {
                var bits = Particles(root, label, material, 4.5f);
                bits.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                Burst(bits, count);
                main = bits.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(sizes.x, sizes.y);
                main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 11f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.gravityModifier = gravity;
                main.maxParticles = 96;
                shape = bits.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 42f;
                shape.radius = .4f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                if (material == _m.Confetti) RandomFrame(bits, 2, 2);
                Spin(bits, material == _m.Foam ? 260f : 500f);
                Fade(bits, .8f);
            }
            Sparkles(root, 24, .8f, local, 1.4f);

            var floor = Floor(root, 3f, -.85f);
            FloorRing(floor, "Gold ring", _m.RingGold, 7.5f, 0f, .8f);
            FloorRing(floor, "Ki ring", _m.Ring, 4.6f, .12f, .8f);
            return Save(root);
        }

        // ── Shared effect pieces ─────────────────────────────────────────────────

        /// <summary>A single shape that pops large, settles and fades (FightGuy's Pop).</summary>
        private static ParticleSystem Pop(GameObject root, string name, Material material, float size, float lifetime, ParticleSystemScalingMode scaling)
        {
            var pop = Particles(root, name, material, .5f, scaling: scaling);
            Burst(pop, 1);
            var main = pop.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(pop, Curve((0f, .5f), (.12f, 1.12f), (.4f, 1f), (1f, .85f)));
            Fade(pop, .7f);
            InFront(pop);
            return pop;
        }

        private static ParticleSystem RingPop(GameObject root, string name, Material material, float size, float lifetime, float delay,
            ParticleSystemScalingMode scaling)
        {
            var ring = Particles(root, name, material, .5f, scaling: scaling);
            Burst(ring, 1);
            var main = ring.main;
            main.startDelay = delay;
            main.startLifetime = lifetime;
            main.startSize = size;
            SizeOverLife(ring, Curve((0f, .3f), (1f, 1.6f)));
            Fade(ring, .35f);
            return ring;
        }

        /// <summary>A comic word that slams in, wobbles a little and drifts up as it fades.</summary>
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
            Climb(word, rise);
            SizeOverLife(word, Curve((0f, .3f), (.1f, 1.18f), (.22f, 1f), (1f, 1.06f)));
            var color = word.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (.05f, 1f), (.72f, 1f), (1f, 0f) });
            InFront(word);
            word.GetComponent<ParticleSystemRenderer>().maxParticleSize = 6f;
            return word;
        }

        /// <summary>A single sprite floating up out of a burst (his 肉, his seal).</summary>
        private static ParticleSystem Rising(GameObject root, string name, Material material, float size, float delay, float lifetime,
            float rise, ParticleSystemScalingMode scaling)
        {
            var sprite = Particles(root, name, material, delay + lifetime + .1f, scaling: scaling);
            Burst(sprite, 1);
            var main = sprite.main;
            main.startDelay = delay;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.1f, .1f);
            Climb(sprite, rise);
            SizeOverLife(sprite, Curve((0f, .35f), (.12f, 1.08f), (.25f, 1f), (1f, 1.1f)));
            var color = sprite.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (.08f, 1f), (.65f, 1f), (1f, 0f) });
            sprite.GetComponent<ParticleSystemRenderer>().sortingFudge = -2f;
            return sprite;
        }

        /// <summary>Straight up in world space, fast at first and easing off.</summary>
        private static void Climb(ParticleSystem system, float speed)
        {
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // x, y and z share one curve mode; a zero multiplier keeps it from drifting sideways.
            var rise = Curve((0f, 1.6f), (.35f, .6f), (1f, .3f));
            velocity.x = new ParticleSystem.MinMaxCurve(0f, rise);
            velocity.y = new ParticleSystem.MinMaxCurve(speed, rise);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, rise);
        }

        private static void Bits(GameObject root, string name, Material material, int count, Vector2 sizes, Vector2 speeds, float gravity,
            float life, ParticleSystemScalingMode scaling)
        {
            var bits = Particles(root, name, material, life + .2f, scaling: scaling);
            Burst(bits, count);
            var main = bits.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * .65f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(sizes.x, sizes.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speeds.x, speeds.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.maxParticles = Mathf.Max(64, count);
            Sphere(bits, .3f);
            if (gravity < .6f) Drag(bits, .3f);
            if (material != _m.Spark) RandomFrame(bits, 2, 2);
            if (material == _m.Spark) Stretch(bits, .04f, 1.6f);
            else Spin(bits, 540f);
            Fade(bits, .75f);
        }

        private static void Puffs(GameObject root, string name, Material material, int count, Vector2 sizes, Color tint,
            ParticleSystemScalingMode scaling)
        {
            var puffs = Particles(root, name, material, 1.4f, scaling: scaling);
            Burst(puffs, count);
            var main = puffs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizes.x, sizes.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 2.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = tint;
            main.gravityModifier = -.05f;
            Sphere(puffs, .3f);
            Drag(puffs, .15f);
            RandomFrame(puffs, 2, 2);
            SizeOverLife(puffs, Curve((0f, .5f), (.3f, 1f), (1f, 1.25f)));
            Fade(puffs, .5f);
            Behind(puffs);
        }

        private static void Sparkles(GameObject root, int count, float radius, ParticleSystemScalingMode scaling, float scale = 1f)
        {
            var sparkles = Particles(root, "Stars", _m.Star, .9f, scaling: scaling);
            Burst(sparkles, count);
            var main = sparkles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .65f);
            main.startSize = new ParticleSystem.MinMaxCurve(.1f * scale, .2f * scale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f * scale, 3.8f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = .4f;
            Sphere(sparkles, radius);
            Drag(sparkles, .25f);
            Spin(sparkles, 300f);
            SizeOverLife(sparkles, Curve((0f, 1f), (1f, .2f)));
            Fade(sparkles, .6f);
            InFront(sparkles);
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

        // ── Balloon geometry ─────────────────────────────────────────────────────

        private static float ProfileRadius(float y)
        {
            for (int i = 0; i < BalloonProfile.Length - 1; i++)
            {
                Vector2 a = BalloonProfile[i], b = BalloonProfile[i + 1];
                if (y >= a.y && y <= b.y) return Mathf.Lerp(a.x, b.x, Mathf.InverseLerp(a.y, b.y, y));
            }
            return 0f;
        }

        /// <summary>Front projection: u grows toward the viewer's right (-X, as CourseLantern turns +Z to the camera), v up.</summary>
        private static Vector2 BalloonUv(Vector3 p) => new(.5f - p.x / (2f * BalloonHalfWidth), Mathf.InverseLerp(BalloonBottom, BalloonTop, p.y));

        // The atlas's bottom strip holds the knot (left) and the ribbon (right).
        private static readonly Vector2 KnotUv = new(.03f, .02f), RibbonUv = new(.97f, .02f);

        /// <summary>The balloon body, the knot under it and a curly ribbon hanging from the knot.</summary>
        private static Mesh BalloonMesh()
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            Lathe(vertices, uvs, triangles, BalloonProfile, 20, BalloonUv, inward: false);
            Lathe(vertices, uvs, triangles, new[] { new Vector2(.035f, -.505f), new Vector2(.075f, -.55f), new Vector2(.03f, -.585f), new Vector2(0f, -.59f) },
                10, _ => KnotUv, inward: false);
            // The ribbon: a curly strip in the XY plane, both sides drawn.
            const int steps = 18;
            const float width = .022f;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float y = Mathf.Lerp(-.58f, RibbonEnd, t);
                float x = .045f * Mathf.Sin(t * Mathf.PI * 3.2f) * (.4f + t);
                vertices.Add(new Vector3(x - width, y, 0f));
                vertices.Add(new Vector3(x + width, y, 0f));
                uvs.Add(RibbonUv);
                uvs.Add(RibbonUv);
                if (i == 0) continue;
                int a = vertices.Count - 4, b = a + 1, c = a + 2, d = a + 3;
                triangles.AddRange(new[] { a, b, c, b, d, c, a, c, b, b, c, d });
            }
            return SaveMesh("PierBalloonMesh", vertices, uvs, triangles);
        }

        /// <summary>An inverted hull a little larger than the body: the balloon's dark outline.</summary>
        private static Mesh BalloonOutlineMesh()
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var hull = BalloonProfile.Select(p => new Vector2(p.x * 1.07f + (p.x > 0f ? .006f : 0f), .015f + (p.y - .015f) * 1.065f)).ToArray();
            Lathe(vertices, uvs, triangles, hull, 20, _ => Vector2.zero, inward: true);
            return SaveMesh("PierBalloonOutlineMesh", vertices, uvs, triangles);
        }

        /// <summary>
        /// A surface of revolution about +Y. Unity front faces are clockwise seen from outside;
        /// an inward lathe winds the other way, so only its far side draws (an outline hull).
        /// </summary>
        private static void Lathe(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, Vector2[] profile, int segments,
            Func<Vector3, Vector2> uv, bool inward)
        {
            int first = vertices.Count, columns = segments + 1;
            foreach (var p in profile)
                for (int k = 0; k <= segments; k++)
                {
                    float angle = k / (float)segments * Mathf.PI * 2f;
                    var v = new Vector3(Mathf.Cos(angle) * p.x, p.y, Mathf.Sin(angle) * p.x);
                    vertices.Add(v);
                    uvs.Add(uv(v));
                }
            for (int ring = 0; ring < profile.Length - 1; ring++)
                for (int k = 0; k < segments; k++)
                {
                    int a = first + ring * columns + k, b = a + 1, c = a + columns, d = c + 1;
                    triangles.AddRange(inward ? new[] { a, b, c, b, d, c } : new[] { a, c, b, b, c, d });
                }
        }

        private static Mesh SaveMesh(string name, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        /// <summary>A unit quad in the XY plane on its base (bottom = 0) or centred (-0.5), facing -Z or +Z.</summary>
        private static Mesh Quad(string name, float bottom, bool towardPlusZ)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(new[] { new Vector3(-.5f, bottom, 0f), new Vector3(.5f, bottom, 0f), new Vector3(.5f, bottom + 1f, 0f), new Vector3(-.5f, bottom + 1f, 0f) });
            // Seen from +Z, +X is on the viewer's left, so u runs from +X to -X on a +Z face.
            mesh.SetUVs(0, towardPlusZ
                ? new[] { new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) }
                : new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            mesh.SetTriangles(towardPlusZ ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        // ── Materials and textures ───────────────────────────────────────────────

        private static Materials BuildMaterials()
        {
            var niku = new MaskField(NikuMaskPath);
            var glow = _kit.BakeTexture("PierGlow", 128, 128, (u, v) => Alpha(Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f)));
            return new Materials
            {
                Balloon = Unlit("PierBalloon", _kit.BakeTexture("PierBalloonAtlas", 512, 512, (u, v) => BalloonPixel(u, v, niku)), new Color(1.05f, 1.05f, 1.05f)),
                Outline = Unlit("PierBalloonOutline", null, Ink),
                Halo = _kit.ParticleMaterial("PierBalloonHalo", glow, new Color(Gold.r * 1.2f, Gold.g * 1.1f, Gold.b, .42f)),
                Pool = _kit.ParticleMaterial("PierPool", glow, new Color(Gold.r, Gold.g * .9f, Gold.b * .8f, .3f)),
                PopWord = WordMaterial("PierPopWord", Root + "/Source/Pop.png", RedDark, Red, Cream),
                BonusWord = WordMaterial("PierBonusWord", Root + "/Source/Bonus.png", GoldRim, Gold, GoldPale),
                ChampWord = WordMaterial("PierChampWord", Root + "/Source/Champ.png", Ink, Gold, GoldPale),
                Foil = Mat("PierFoil", 256, 256, (u, v) => Sheet(u, v, (p, cell) => FoilPixel(p, cell, niku))),
                Confetti = Mat("PierConfetti", 256, 256, (u, v) => Sheet(u, v, ConfettiPixel)),
                Candy = Mat("PierCandy", 256, 256, (u, v) => Sheet(u, v, CandyPixel)),
                Fringe = Mat("PierFringe", 256, 256, (u, v) => Sheet(u, v, FringePixel)),
                CarBits = Mat("PierCarBits", 256, 256, (u, v) => Sheet(u, v, CarBitsPixel)),
                Coins = Mat("PierCoins", 256, 256, (u, v) => Sheet(u, v, (p, cell) => CoinPixel(p, cell, niku))),
                Leather = Mat("PierLeather", 256, 256, (u, v) => Sheet(u, v, (p, cell) => LeatherPixel(p, cell, niku))),
                Spark = Mat("PierSpark", 64, 32, (u, v) => Bands(Lens(new Vector2(u * 2f - 1f, (v * 2f - 1f) * .5f), .95f, .2f) * 32f, 1.5f, 2.5f, GoldDeep, Gold, GoldPale)),
                RingRed = Mat("PierRingRed", 256, 256, (u, v) => RingPixel(P(u, v), RedDark, Red, RedLight)),
                MoteRed = Mat("PierMoteRed", 64, 64, (u, v) => MotePixel(P(u, v), RedDark, Red, RedLight)),
                FrameGold = Mat("PierFrameGold", 8, 64, (_, v) => FramePixel(v, GoldRim, Gold, GoldPale)),
                FrameRed = Mat("PierFrameRed", 8, 64, (_, v) => FramePixel(v, RedDark, Red, RedLight)),
                BeamGold = Mat("PierBeamGold", 64, 256, (u, v) => BeamPixel(u, v, GoldDeep, Gold, GoldPale)),
                BeamRed = Mat("PierBeamRed", 64, 256, (u, v) => BeamPixel(u, v, RedDark, Red, Cream)),
                MeterBar = Unlit("PierMeterBar", _kit.BakeTexture("PierMeterBar", 32, 8, (u, _) => MeterPixel(u)), new Color(1.25f, 1.2f, 1.15f)),
                MeterGlass = Mat("PierMeterGlass", 64, 256, MeterGlassPixel),
                Flash = FightGuy("FightGuyFlash"),
                Impact = FightGuy("FightGuyImpact"),
                ImpactGold = FightGuy("FightGuyImpactGold"),
                Ring = FightGuy("FightGuyRing"),
                RingGold = FightGuy("FightGuyRingGold"),
                Star = FightGuy("FightGuyStar"),
                Chalk = FightGuy("FightGuyChalk"),
                Niku = FightGuy("FightGuyNiku"),
                Seal = FightGuy("FightGuySeal"),
                Foam = FightGuy("FightGuyFoamFinger"),
            };
        }

        private static Material FightGuy(string name)
            => AssetDatabase.LoadAssetAtPath<Material>(FightGuyFx + name + ".mat")
               ?? throw new InvalidOperationException($"FightGuy's {name}.mat is missing; run sloparena.presentation.fightguy.build first.");

        /// <summary>
        /// The balloon, front-projected: toon gold foil lit from the upper left with a dark rim
        /// at the silhouette and a white glint, the 肉 sticker (red, cream border, ink edge) in
        /// the middle shaded with the foil, and the knot and ribbon colours along the bottom.
        /// </summary>
        private static Color BalloonPixel(float u, float v, MaskField niku)
        {
            if (v < .04f) return u < .5f ? GoldDeep : Red;
            float y = Mathf.Lerp(BalloonBottom, BalloonTop, v);
            float x = (.5f - u) * 2f * BalloonHalfWidth;
            float radius = Mathf.Max(.03f, ProfileRadius(y));
            // Viewer space: right is -X.
            float px = Mathf.Clamp(-x / radius, -1f, 1f);
            float py = Mathf.Clamp((y - .02f) / .53f, -1f, 1f);
            float across = Mathf.Sqrt(Mathf.Max(0f, 1f - py * py));
            var normal = new Vector3(px * across, py, Mathf.Sqrt(Mathf.Max(0f, 1f - px * px)) * across).normalized;
            float lit = Vector3.Dot(normal, Light);
            Color foil = Color.Lerp(GoldDeep, Gold, Step(.02f, .1f, lit));
            foil = Color.Lerp(foil, GoldLight, Step(.66f, .74f, lit));
            foil = Color.Lerp(foil, GoldRim * 1.6f, Step(.9f, .97f, Mathf.Abs(px)) * .7f);
            // Foil crinkle: faint diagonal sheen bands.
            foil = Color.Lerp(foil, GoldLight, .12f * Step(.55f, .9f, ValueNoise(u * 1.4f + v, v * 3f, 5f, 7)));
            var glintP = Rotate(new Vector2(px + .42f, py - .42f), 40f);
            float glint = new Vector2(glintP.x / .16f, glintP.y / .06f).magnitude - 1f;
            foil = Color.Lerp(foil, Color.white, Mathf.Clamp01(.5f - glint * 6f) * .95f);
            foil.a = 1f;
            // The 肉 sticker: the mask square spans 0.56 of the width, 0.05 m above the middle.
            const float glyphSpan = .56f;
            float gu = .5f + (u - .5f) / glyphSpan;
            float gv = .5f + (y - .05f) / (glyphSpan * 2f * BalloonHalfWidth);
            if (gu < 0f || gu > 1f || gv < 0f || gv > 1f) return foil;
            float d = niku.Sample(gu, gv) * glyphSpan * 512f / niku.Width;
            var sticker = Bands(d - 7f, 1.8f, 5.5f, Ink, Cream, Red);
            float shade = Mathf.Lerp(.72f, 1f, Step(.02f, .1f, lit)) * Mathf.Lerp(1f, 1.08f, Step(.66f, .74f, lit));
            sticker = new Color(sticker.r * shade, sticker.g * shade, sticker.b * shade, sticker.a);
            return Over(foil, sticker);
        }

        /// <summary>A comic word from a lettering mask: dark outline, accent band, pale core.</summary>
        private static Material WordMaterial(string name, string maskPath, Color rim, Color accent, Color core)
        {
            var mask = new MaskField(maskPath);
            float scale = 512f / mask.Width;
            return Mat(name, 512, 512, (u, v) => Bands(mask.Sample(u, v) * scale - 7f, 3.5f, 6f, rim, accent, core));
        }

        private static readonly Vector2[][] Shards =
        {
            new[] { new Vector2(-.7f, -.4f), new Vector2(.6f, -.7f), new Vector2(.75f, .2f), new Vector2(-.2f, .75f) },
            new[] { new Vector2(-.8f, .1f), new Vector2(.1f, -.75f), new Vector2(.8f, .3f), new Vector2(-.1f, .6f) },
            new[] { new Vector2(-.6f, -.7f), new Vector2(.7f, -.3f), new Vector2(.4f, .75f), new Vector2(-.75f, .4f) },
        };

        /// <summary>Gold foil shreds with a sheen, a curl of red ribbon, and a shred with a bit of the red 肉.</summary>
        private static Color FoilPixel(Vector2 p, int cell, MaskField niku)
        {
            if (cell == 2)
            {
                float curl = Mathf.Abs(p.y - .35f * Mathf.Sin(p.x * 4.2f)) - .1f;
                return Bands(Mathf.Max(curl, Mathf.Abs(p.x) - .85f) * 64f, 1.6f, 2.5f, RedDark, Red, RedLight);
            }
            var shard = Shards[cell == 3 ? 2 : cell];
            var color = Bands(Polygon(p, shard) * 64f, 1.6f, 2.5f, GoldRim, GoldDeep, Gold);
            float sheen = Mathf.Abs(Rotate(p, 30f).x + .1f) - .14f;
            if (color.a > 0f) color = Color.Lerp(color, new Color(GoldPale.r, GoldPale.g, GoldPale.b, color.a), Mathf.Clamp01(.5f - sheen * 64f) * .8f);
            if (cell == 3 && color.a > 0f)
            {
                float d = niku.Sample(.5f + p.x * .7f, .62f + p.y * .7f) * .7f * 64f / niku.Width * 2f;
                if (d < 0f) color = Color.Lerp(color, new Color(Red.r, Red.g, Red.b, color.a), Mathf.Clamp01(.5f - d));
            }
            return color;
        }

        /// <summary>Paper confetti in his colours: gold, headband red, ki blue and cream.</summary>
        private static Color ConfettiPixel(Vector2 p, int cell)
        {
            var q = Rotate(p, cell * 21f - 25f);
            float d = Box(q, new Vector2(.82f, .3f)) * 64f;
            return cell switch
            {
                0 => Bands(d, 1.5f, 3f, GoldRim, Gold, GoldLight),
                1 => Bands(d, 1.5f, 3f, RedDark, Red, RedLight),
                2 => Bands(d, 1.5f, 3f, KiRim, KiBlue, new Color(.6f, .85f, 1f)),
                _ => Bands(d, 1.5f, 3f, GoldDeep, Cream, Color.white),
            };
        }

        /// <summary>Wrapped candies: a round sweet with twisted ends and a white swirl.</summary>
        private static Color CandyPixel(Vector2 p, int cell)
        {
            Color[] colors = { Pink, Teal, Gold, Orange };
            var c = colors[cell];
            var q = Rotate(p, cell * 37f);
            float body = q.magnitude - .42f;
            float ends = Mathf.Min(Polygon(q, new[] { new Vector2(-.36f, 0f), new Vector2(-.88f, -.3f), new Vector2(-.88f, .3f) }),
                Polygon(q, new[] { new Vector2(.36f, 0f), new Vector2(.88f, .3f), new Vector2(.88f, -.3f) }));
            var color = Bands(Mathf.Min(body, ends) * 64f, 1.8f, 3f, c * .45f, c, Color.Lerp(c, Color.white, .35f));
            float swirl = Mathf.Abs(Mathf.Sin(Mathf.Atan2(q.y, q.x) * 2f + q.magnitude * 9f));
            if (body < -.04f && swirl > .8f) color = new Color(1f, 1f, 1f, color.a);
            return color;
        }

        /// <summary>Tissue fringe strips with notched ends, in the piñata's colours.</summary>
        private static Color FringePixel(Vector2 p, int cell)
        {
            Color[] colors = { Pink, Gold, Teal, Orange };
            var c = colors[cell];
            var q = Rotate(p, cell * 30f - 40f);
            float strip = Box(q, new Vector2(.25f, .85f));
            float notch = Mathf.Abs(Mathf.Repeat(q.x * 6f, 1f) - .5f) * .3f;
            strip = Mathf.Max(strip, q.y - .85f + notch);
            return Bands(strip * 64f, 1.4f, 3f, c * .5f, c, Color.Lerp(c, Color.white, .25f));
        }

        /// <summary>The car coming apart: a whitewall tyre, a hubcap, a teal panel with its cream spear, a glass shard.</summary>
        private static Color CarBitsPixel(Vector2 p, int cell)
        {
            switch (cell)
            {
                case 0:
                {
                    float r = p.magnitude;
                    var color = Bands((r - .85f) * 64f, 2f, 99f, Ink, new Color(.14f, .13f, .14f), new Color(.14f, .13f, .14f));
                    if (r < .62f) color = new Color(Cream.r, Cream.g, Cream.b, color.a);
                    if (r < .44f) color = new Color(.14f, .13f, .14f, color.a);
                    if (r < .3f) color = new Color(Chrome.r, Chrome.g, Chrome.b, color.a);
                    return color;
                }
                case 1:
                {
                    var color = Bands((p.magnitude - .8f) * 64f, 2f, 5f, Ink, Steel, Chrome);
                    if (p.magnitude < .2f) color = new Color(Red.r, Red.g, Red.b, color.a);
                    return color;
                }
                case 2:
                {
                    var color = Bands(Polygon(p, Shards[0]) * 64f, 2f, 4f, Ink, Teal, TealLight);
                    if (color.a > 0f && Mathf.Abs(Rotate(p, -12f).y + .05f) < .09f) color = new Color(Cream.r, Cream.g, Cream.b, color.a);
                    return color;
                }
                default:
                    return Bands(Polygon(p, new[] { new Vector2(-.6f, -.7f), new Vector2(.75f, -.2f), new Vector2(-.1f, .8f) }) * 64f, 1.6f, 3f,
                        new Color(.2f, .33f, .42f), new Color(.55f, .78f, .88f), new Color(.9f, .97f, 1f));
            }
        }

        /// <summary>The jackpot: a gold 肉 coin face on, one edge on, a marquee bulb and a red pad scrap.</summary>
        private static Color CoinPixel(Vector2 p, int cell, MaskField niku)
        {
            switch (cell)
            {
                case 0:
                {
                    var color = Bands((p.magnitude - .82f) * 64f, 2f, 5f, GoldRim, GoldDeep, Gold);
                    float d = niku.Sample(.5f + p.x * .5f / .55f, .5f + p.y * .5f / .55f) * .55f * 64f / (niku.Width * .5f);
                    if (color.a > 0f && d < 0f) color = Color.Lerp(color, new Color(GoldRim.r, GoldRim.g, GoldRim.b, color.a), Mathf.Clamp01(.5f - d) * .85f);
                    return color;
                }
                case 1:
                    return Bands((new Vector2(p.x / .3f, p.y / .82f).magnitude - 1f) * .3f * 64f, 2f, 3f, GoldRim, GoldDeep, Gold);
                case 2:
                {
                    var color = Bands(((p - new Vector2(0f, .15f)).magnitude - .55f) * 64f, 2f, 4f, GoldDeep, new Color(1f, .9f, .6f), Color.white);
                    return Over(color, Bands(Box(p - new Vector2(0f, -.62f), new Vector2(.24f, .2f)) * 64f, 2f, 99f, Ink, Steel, Steel));
                }
                default:
                    return Bands(Polygon(p, Shards[1]) * 64f, 2f, 4f, RedDark, Red, RedLight);
            }
        }

        /// <summary>The bag bursting: two red leather scraps, a gold strap piece and the cream 肉 patch.</summary>
        private static Color LeatherPixel(Vector2 p, int cell, MaskField niku)
        {
            switch (cell)
            {
                case 0: return Bands(Polygon(p, Shards[0]) * 64f, 2f, 4f, RedDark, Red, RedLight);
                case 1: return Bands(Polygon(p, Shards[2]) * 64f, 2f, 4f, RedDark, Red * .85f, Red);
                case 2: return Bands(Box(Rotate(p, 20f), new Vector2(.85f, .2f)) * 64f, 2f, 4f, GoldRim, GoldDeep, Gold);
                default:
                {
                    var color = Bands(Box(Rotate(p, -10f), new Vector2(.62f, .62f)) * 64f, 2f, 99f, GoldDeep, Cream, Cream);
                    float d = niku.Sample(.5f + p.x * .5f / .5f, .5f + p.y * .5f / .5f) * .5f * 64f / (niku.Width * .5f);
                    if (color.a > 0f && d < 0f) color = Color.Lerp(color, new Color(Red.r, Red.g, Red.b, color.a), Mathf.Clamp01(.5f - d));
                    return color;
                }
            }
        }

        private static Color RingPixel(Vector2 p, Color dark, Color accent, Color light)
            => Bands((Mathf.Abs(p.magnitude - .8f) - .085f) * 128f, 2.5f, 4.5f, dark, accent, light);

        /// <summary>A four-point spark star.</summary>
        private static Color MotePixel(Vector2 p, Color dark, Color accent, Color light)
            => Bands(Mathf.Min(Rhombus(p, new Vector2(.95f, .28f)), Rhombus(p, new Vector2(.28f, .95f))) * 32f, 2f, 4f, dark, accent, light);

        /// <summary>Across the frame band: a solid accent line with dark edges and a pale centre, glowing either side.</summary>
        private static Color FramePixel(float v, Color dark, Color accent, Color light)
        {
            float x = Mathf.Abs(v - .5f);
            var line = Bands((x - .2f) * 64f, 1.5f, 8f, dark, accent, light);
            float glow = x > .2f ? .65f * Mathf.Pow(Mathf.Clamp01(1f - (x - .2f) / .3f), 1.6f) : 0f;
            return Over(new Color(accent.r, accent.g, accent.b, glow), line);
        }

        /// <summary>A spotlight beam: a pale core, the accent, then a soft edge; it fades toward the top.</summary>
        private static Color BeamPixel(float u, float v, Color edge, Color accent, Color core)
        {
            float x = Mathf.Abs(u - .5f) * 2f;
            Color color = Color.Lerp(accent, core, Step(.2f, .14f, x));
            color = Color.Lerp(color, edge, Step(.44f, .5f, x));
            float alpha = x < .2f ? .85f : x < .46f ? .65f : .5f * Mathf.Pow(Mathf.Clamp01(1f - (x - .46f) / .54f), 1.6f);
            color.a = alpha * Mathf.Pow(1f - v, .9f) * Step(0f, .03f, v);
            return color;
        }

        /// <summary>The meter bar across its width: a white-hot core in orange-gold. Uniform along V, so filling never squashes it.</summary>
        private static Color MeterPixel(float u)
        {
            float x = Mathf.Abs(u - .5f) * 2f;
            var color = Color.Lerp(GoldPale, Gold, Step(.2f, .45f, x));
            return Color.Lerp(color, Orange, Step(.6f, .95f, x));
        }

        /// <summary>The meter's glass: ten dark rungs, a highlight stripe, clear between.</summary>
        private static Color MeterGlassPixel(float u, float v)
        {
            float rung = Mathf.Abs(Mathf.Repeat(v * 10f, 1f) - .5f);
            var color = new Color(Ink.r, Ink.g, Ink.b, Step(.44f, .47f, rung) * .9f);
            float stripe = Mathf.Abs(u - .3f);
            return Over(color, new Color(1f, 1f, 1f, Step(.08f, .04f, stripe) * .3f));
        }

        private static Material Mat(string name, int width, int height, Func<float, float, Color> pixel)
            => _kit.ParticleMaterial(name, _kit.BakeTexture(name, width, height, pixel));

        /// <summary>An opaque URP Unlit material: the balloons keep their painted shade under the dusk light.</summary>
        private static Material Unlit(string name, Texture texture, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? throw new InvalidOperationException("URP Unlit is unavailable.");
            string path = $"{_kit.Root}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(shader);
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", color);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

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

        /// <summary>A child moved onto the stage floor on spawn (<see cref="SnapToFloor"/>), or hidden.</summary>
        private static GameObject Floor(GameObject root, float maxDrop, float fallbackY)
        {
            var floor = Child(root, "Floor");
            floor.transform.localPosition = new Vector3(0f, fallbackY, 0f);
            floor.AddComponent<SnapToFloor>().MaxDrop = maxDrop;
            return floor;
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
