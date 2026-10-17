#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static SlopArena.EditorTools.PresentationFx;
using static SlopArena.EditorTools.ToonShapes;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Rebuilds FightGuy's package-owned combat presentation from code (docs/characters/fightguy.md):
    /// "The Champ's Show". Blue ki is his real skill, champion gold is his ego, and 肉 from his
    /// gi is his brand. Chalk dust marks his strikes; the gags (paparazzi flashes, a foam #1
    /// finger, dizzy stars) only ever embarrass him. Workflow and readability rules are in
    /// skills/sloparena-character-vfx-pass; plumbing is in <see cref="PresentationBuilderKit"/>.
    /// </summary>
    public static class FightGuyPresentationBuilder
    {
        private const string NikuMaskPath = "Assets/CharacterPackages/fightguy/Presentation/Source/Niku.png";

        private static readonly Color KiRim = new(0.04f, 0.13f, 0.36f);
        private static readonly Color KiBlue = new(0.25f, 0.66f, 1f);
        private static readonly Color KiPale = new(0.59f, 0.84f, 1f);
        private static readonly Color GoldRim = new(0.33f, 0.17f, 0f);
        private static readonly Color Gold = new(1f, 0.77f, 0.19f);
        private static readonly Color GoldPale = new(1f, 0.97f, 0.8f);
        private static readonly Color InkRed = new(0.81f, 0.11f, 0.17f);
        private static readonly Color InkDark = new(0.5f, 0.05f, 0.09f);
        private static readonly Color ChalkRim = new(0.46f, 0.42f, 0.38f);
        private static readonly Color Chalk = new(0.93f, 0.91f, 0.86f);
        private static readonly Color FoamRim = new(0.36f, 0.2f, 0f);
        private static readonly Color Foam = new(1f, 0.81f, 0.24f);
        private static readonly Color ClothRim = new(0.05f, 0.12f, 0.33f);
        private static readonly Color Cloth = new(0.18f, 0.44f, 0.91f);
        private static readonly Color WarmEdge = new(1f, 0.8f, 0.36f);
        private static readonly Color WarmPale = new(1f, 0.96f, 0.85f);

        private static PresentationBuilderKit _kit;
        private static Materials _m;

        private sealed class Materials
        {
            public Material Glow, Flash, KiBall, Niku, Stamp, Impact, ImpactGold, Pow, Ring, RingGold, Swoosh, SwooshGold,
                Ribbon, RibbonGold, Chalk, Foam, Fist, Star, Spotlight, Seal;
            public Mesh Helix;
        }

        [CliCommand("sloparena.presentation.fightguy.build", "Rebuild FightGuy's package presentation prefabs, textures, meshes, materials and bindings in Edit Mode. Saving the catalog triggers the usual package cook.", MainThreadRequired = true, Tags = new[] { "presentation", "character" })]
        public static object Build()
        {
            using var kit = new PresentationBuilderKit("fightguy", audioFolder: "FightGuy");
            _kit = kit;
            _m = BuildMaterials();
            var built = new List<(string id, GameObject prefab)>
            {
                ("presentation.fightguy.normal.strike", Swing("FightGuyNormalStrike", 0.15f, 0.1f, gold: false, puffs: 4)),
                ("presentation.fightguy.normal.sweep", Swing("FightGuyNormalSweep", 0.22f, 0.12f, gold: false, puffs: 5)),
                ("presentation.fightguy.normal.heavy", Swing("FightGuyHeavySwing", 0.3f, 0.14f, gold: true, puffs: 7)),
                ("presentation.fightguy.normal.hit", NormalHit(heavy: false)),
                ("presentation.fightguy.normal.heavy-hit", NormalHit(heavy: true)),
                ("presentation.fightguy.ki-shot.charge", KiCharge()),
                ("presentation.fightguy.ki-shot.release", KiRelease()),
                ("presentation.fightguy.ki-shot.hit", KiHit()),
                ("presentation.fightguy.rising-dragon.fist", DragonFist()),
                ("presentation.fightguy.rising-dragon.spiral", Spiral("FightGuyDragonSpiral", rising: true)),
                ("presentation.fightguy.rising-dragon.apex", DragonApex()),
                ("presentation.fightguy.rising-dragon.hit", DragonHit()),
                ("presentation.fightguy.cyclone-kick.spiral", Spiral("FightGuyCycloneHelix", rising: false)),
                ("presentation.fightguy.cyclone-kick.dust", CycloneDust()),
                ("presentation.fightguy.cyclone-kick.dizzy", Dizzy()),
                ("presentation.fightguy.cyclone-kick.hit", CycloneHit()),
                ("presentation.fightguy.fist-of-fury.focus", FuryFocus()),
                ("presentation.fightguy.fist-of-fury.glint", FuryGlint()),
                ("presentation.fightguy.fist-of-fury.ghost", FuryGhost()),
                ("presentation.fightguy.fist-of-fury.hit", FuryHit()),
            };
            var ball = KiBall();
            kit.BindProjectile(CharacterClass.FightGuy, AbilitySlots.A, ball);
            return kit.Finish(built, new { projectile = AssetDatabase.GetAssetPath(ball) });
        }

        [MenuItem("Tools/SLOPArena/Presentation/Rebuild FightGuy Presentation")]
        private static void Menu() => Debug.Log("[FightGuyPresentationBuilder] " + Newtonsoft.Json.JsonConvert.SerializeObject(Build()));

        // ── Normals ──────────────────────────────────────────────────────────────

        /// <summary>A toon ribbon behind the striking limb and a puff of chalk off the wraps.</summary>
        private static GameObject Swing(string name, float width, float emitFor, bool gold, int puffs)
        {
            var root = _kit.NewRoot(name, attackBound: true);
            var frame = Frame(root);
            Ribbon(frame, "Ribbon", gold ? _m.RibbonGold : _m.Ribbon, width, emitFor);
            ChalkPuffs(frame, puffs, gold ? 0.26f : 0.18f, 1.1f);
            return _kit.Save(root);
        }

        /// <summary>A round blue "pow" and speed lines; heavies add a gold star behind and chalk specks.</summary>
        private static GameObject NormalHit(bool heavy)
        {
            var root = _kit.NewRoot(heavy ? "FightGuyHeavyHit" : "FightGuyNormalHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, heavy ? 1.1f : 0.9f, 0.12f, KiBlue, scaled);
            if (heavy) Pop(root, "Gold star", _m.ImpactGold, 0.8f, 0.16f, scaled, spin: true);
            Pop(root, "Pow", _m.Pow, heavy ? 0.6f : 0.62f, heavy ? 0.14f : 0.12f, scaled, spin: true);
            SpeedLines(root, heavy ? 10 : 7, heavy ? 10f : 8f, 0.07f, scaled);
            if (heavy) ChalkSpecks(root, 8, scaled);
            return _kit.Save(root);
        }

        // ── Ki Shot (A) ──────────────────────────────────────────────────────────

        /// <summary>
        /// Charge: ki motes pulled into the palm while he holds. Each new charge tier pops a gold
        /// ring and paparazzi camera flashes around him (<see cref="ChargeTierCues"/>).
        /// </summary>
        private static GameObject KiCharge()
        {
            var root = _kit.NewRoot("FightGuyKiCharge", attackBound: true);
            var frame = Frame(root, upright: true);
            var motes = Particles(frame, "Motes", _m.KiBall, 1f, ParticleSystemSimulationSpace.Local);
            var main = motes.main;
            main.loop = true;
            main.startLifetime = 0.24f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.maxParticles = 48;
            var emission = motes.emission;
            emission.rateOverTime = 36f;
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.75f;
            shape.radiusThickness = 0f;
            var velocity = motes.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.radial = new ParticleSystem.MinMaxCurve(-3.2f);
            SizeOverLife(motes, Curve((0f, 0.6f), (0.7f, 1f), (1f, 0.2f)));
            Fade(motes, 0.8f);

            var tier2 = TierCue(frame, "Tier 2", flashes: 3, ring: 1.7f);
            var tier3 = TierCue(frame, "Tier 3", flashes: 5, ring: 2.4f);
            var cues = root.AddComponent<ChargeTierCues>();
            cues.Slot = AbilitySlots.A;
            cues.Tier2 = tier2;
            cues.Tier3 = tier3;
            cues.Charging = motes;
            return _kit.Save(root);
        }

        private static ParticleSystem TierCue(GameObject root, string name, int flashes, float ring)
        {
            var pops = Particles(root, name, _m.Flash, 0.3f);
            var main = pops.main;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.26f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.95f, 1.35f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            var bursts = new[] { new ParticleSystem.Burst(0f, (short)(flashes / 2)), new ParticleSystem.Burst(0.07f, (short)(flashes - flashes / 2)) };
            pops.emission.SetBursts(bursts);
            // Around his head and shoulders: the hand rests at hip height while charging.
            var shape = pops.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.05f;
            shape.radiusThickness = 0f;
            shape.position = new Vector3(0f, 0.6f, 0f);
            SizeOverLife(pops, Curve((0f, 0.3f), (0.15f, 1.15f), (0.35f, 1f), (1f, 0f)));
            InFront(pops);

            var halo = Particles(pops.gameObject, "Bloom", _m.Glow, 0.3f);
            main = halo.main;
            main.playOnAwake = false;
            main.startLifetime = 0.2f;
            main.startSize = 1.4f;
            main.startColor = new Color(1f, 1f, 1f, 0.55f);
            halo.emission.SetBursts(bursts);
            var haloShape = halo.shape;
            haloShape.enabled = false;
            Fade(halo, 0.2f);
            Behind(halo);

            var pop = Particles(pops.gameObject, "Ring", _m.RingGold, 0.3f, ParticleSystemSimulationSpace.Local);
            main = pop.main;
            main.playOnAwake = false;
            main.startLifetime = 0.28f;
            main.startSize = ring;
            Burst(pop, 1);
            SizeOverLife(pop, Curve((0f, 0.45f), (1f, 1.15f)));
            Fade(pop, 0.45f);
            InFront(pop);
            return pops;
        }

        /// <summary>Release: two shockwave rings facing the throw, a white flash and blue sparks.</summary>
        private static GameObject KiRelease()
        {
            var root = _kit.NewRoot("FightGuyKiReleaseRings", attackBound: false);
            var frame = Frame(root);
            for (int i = 0; i < 2; i++)
                RingPop(frame, i == 0 ? "Ring" : "Ring 2", _m.Ring, i == 0 ? 1f : 0.65f, 0.26f, i * 0.05f, ParticleSystemScalingMode.Local);
            Halo(frame, 1.1f, 0.12f, KiPale, ParticleSystemScalingMode.Local);
            var sparks = Particles(frame, "Sparks", _m.Swoosh, 0.3f);
            Burst(sparks, 8);
            var main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.startSize = 0.07f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
            var shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.1f;
            Stretch(sparks, 0.04f, 2f);
            Fade(sparks, 0.5f);
            return _kit.Save(root);
        }

        /// <summary>
        /// The flying ki ball: a toon blue sphere with a spinning gold 肉, a soft glow, a comet tail
        /// and shed motes. ProjectileVFXManager scales the root by charge radius / base radius, so
        /// sizes here are for the 0.5 m tier-1 hitbox and grow with Hierarchy scaling.
        /// </summary>
        private static GameObject KiBall()
        {
            var root = _kit.NewRoot("FightGuyKiBall", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var glow = Steady(root, "Glow", _m.Glow, 1.15f, scaled);
            var main = glow.main;
            main.startColor = new Color(0.45f, 0.72f, 1f, 0.6f);
            Behind(glow);
            Steady(root, "Ball", _m.KiBall, 0.7f, scaled);
            var niku = Steady(root, "Niku", _m.Niku, 0.52f, scaled);
            Spin(niku, 90f);
            InFront(niku);

            var tail = new GameObject("Tail").AddComponent<TrailRenderer>();
            tail.transform.SetParent(root.transform, false);
            tail.time = 0.16f;
            tail.minVertexDistance = 0.05f;
            tail.widthMultiplier = 0.6f;
            tail.widthCurve = Curve((0f, 1f), (1f, 0f));
            tail.colorGradient = GradientOf(new[] { (0f, Color.white), (0.5f, KiBlue), (1f, KiBlue) },
                new[] { (0f, 0.95f), (0.6f, 0.75f), (1f, 0f) }).gradient;
            tail.textureMode = LineTextureMode.Stretch;
            tail.alignment = LineAlignment.View;
            tail.sharedMaterial = _m.Ribbon;
            tail.shadowCastingMode = ShadowCastingMode.Off;
            tail.receiveShadows = false;

            var motes = Particles(root, "Motes", _m.KiBall, 1f);
            main = motes.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.maxParticles = 80;
            var emission = motes.emission;
            emission.rateOverDistance = 7f;
            Sphere(motes, 0.25f);
            Drift(motes, 0.4f, -0.2f, 0.5f);
            SizeOverLife(motes, Curve((0f, 1f), (1f, 0.2f)));
            Fade(motes, 0.5f);
            return _kit.Save(root);
        }

        /// <summary>
        /// Ki Shot contact: a blue impact with a small gold 肉 spark. A fully charged shot also
        /// slaps the red 肉 "inspection stamp" on the victim (<see cref="ChargeTierVariant"/>).
        /// </summary>
        private static GameObject KiHit()
        {
            var root = _kit.NewRoot("FightGuyKiHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, 1.4f, 0.12f, KiBlue, scaled);
            Pop(root, "Impact", _m.Impact, 1.0f, 0.15f, scaled, spin: true);
            var spark = Particles(root, "Gold niku", _m.Niku, 0.5f, scaling: scaled);
            Burst(spark, 1);
            var main = spark.main;
            main.startLifetime = 0.42f;
            main.startSize = 0.3f;
            main.startSpeed = 1.4f;
            var shape = spark.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.01f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Drag(spark, 0.2f);
            SizeOverLife(spark, Curve((0f, 0.3f), (0.15f, 1.15f), (0.3f, 1f), (1f, 0.9f)));
            Fade(spark, 0.6f);
            InFront(spark);
            Sparkles(root, 6, scaled);

            var stamp = new GameObject("Stamp");
            stamp.transform.SetParent(root.transform, false);
            var ink = Particles(stamp, "Ink", _m.Stamp, 0.6f, scaling: scaled);
            Burst(ink, 1);
            main = ink.main;
            main.startLifetime = 0.55f;
            main.startSize = 0.75f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, -0.15f);
            SizeOverLife(ink, Curve((0f, 1.6f), (0.06f, 0.92f), (0.12f, 1f), (1f, 1f)));
            Fade(ink, 0.7f);
            ink.GetComponent<ParticleSystemRenderer>().sortingFudge = -20f;
            stamp.SetActive(false);
            var variant = root.AddComponent<ChargeTierVariant>();
            variant.Slot = AbilitySlots.A;
            variant.MinimumTier = 2;
            variant.Variant = stamp;
            return _kit.Save(root);
        }

        // ── Rising Dragon (E) ────────────────────────────────────────────────────

        /// <summary>A blue ki glow on the uppercut fist that sheds gold sparkles and motes.</summary>
        private static GameObject DragonFist()
        {
            var root = _kit.NewRoot("FightGuyDragonFist", attackBound: true);
            var frame = Frame(root);
            var glow = Steady(frame, "Glow", _m.Glow, 0.75f, ParticleSystemScalingMode.Local);
            var main = glow.main;
            main.startColor = new Color(0.45f, 0.72f, 1f, 0.7f);
            Behind(glow);
            Steady(frame, "Ki", _m.KiBall, 0.32f, ParticleSystemScalingMode.Local);
            var trail = Particles(frame, "Sparkles", _m.Star, 1f);
            main = trail.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.16f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 60;
            var emission = trail.emission;
            emission.rateOverDistance = 9f;
            Sphere(trail, 0.15f);
            Spin(trail, 240f);
            SizeOverLife(trail, Curve((0f, 1f), (1f, 0.2f)));
            Fade(trail, 0.5f);
            return _kit.Save(root);
        }

        /// <summary>
        /// Twin blue and gold helices around the body. Rising Dragon's grow upward with the jump;
        /// Cyclone Kick's spin fast at full height and shed wind rings along the path.
        /// </summary>
        private static GameObject Spiral(string name, bool rising)
        {
            var root = _kit.NewRoot(name, attackBound: true);
            var frame = Frame(root, upright: true);
            for (int i = 0; i < 2; i++)
            {
                var helix = Particles(frame, i == 0 ? "Blue helix" : "Gold helix", i == 0 ? _m.Ribbon : _m.RibbonGold,
                    0.4f, ParticleSystemSimulationSpace.Local);
                helix.transform.localPosition = new Vector3(0f, -0.8f, 0f);
                Burst(helix, 1);
                var main = helix.main;
                main.startLifetime = rising ? 0.45f : 0.75f;
                main.startSize3D = true;
                main.startSizeX = rising ? 0.95f : 1.15f;
                main.startSizeY = rising ? 1.9f : 1.45f;
                main.startSizeZ = rising ? 0.95f : 1.15f;
                main.startRotation3D = true;
                main.startRotationX = 0f;
                main.startRotationY = i * Mathf.PI;
                main.startRotationZ = 0f;
                var rotation = helix.rotationOverLifetime;
                rotation.enabled = true;
                rotation.separateAxes = true;
                rotation.x = 0f;
                rotation.y = (rising ? 640f : 1100f) * Mathf.Deg2Rad;
                rotation.z = 0f;
                var renderer = helix.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = _m.Helix;
                renderer.alignment = ParticleSystemRenderSpace.Local;
                if (rising)
                    SizeOverLife(helix, Curve((0f, 1f), (1f, 1f)), Curve((0f, 0.15f), (0.45f, 1f), (1f, 1.05f)));
                Fade(helix, 0.7f);
            }
            if (!rising)
            {
                var rings = Particles(frame, "Wind rings", _m.Ring, 0.8f);
                rings.transform.localPosition = new Vector3(0f, -0.45f, 0f);
                var emission = rings.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 8, 0.09f) });
                var main = rings.main;
                main.startLifetime = 0.3f;
                main.startSize = 1.6f;
                main.startColor = new Color(0.82f, 0.92f, 1f, 0.9f);
                rings.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                SizeOverLife(rings, Curve((0f, 0.6f), (1f, 1.3f)));
                Fade(rings, 0.3f);
            }
            return _kit.Save(root);
        }

        /// <summary>
        /// The apex gag: a spotlight finds him and his raised fist pops into a giant foam "#1"
        /// finger with a star glint. Not attack-bound, so the beat finishes after the move.
        /// </summary>
        private static GameObject DragonApex()
        {
            var root = _kit.NewRoot("FightGuyDragonApex", attackBound: false);
            var frame = Frame(root, upright: true);
            var spot = Particles(frame, "Spotlight", _m.Spotlight, 0.7f);
            spot.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            Burst(spot, 1);
            var main = spot.main;
            main.startLifetime = 0.62f;
            main.startSize3D = true;
            main.startSizeX = 2.1f;
            main.startSizeY = 4.6f;
            main.startSizeZ = 1f;
            spot.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.VerticalBillboard;
            var color = spot.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (0.12f, 1f), (0.7f, 0.9f), (1f, 0f) });
            Behind(spot);

            var finger = Particles(frame, "Foam finger", _m.Foam, 0.7f, ParticleSystemSimulationSpace.Local);
            finger.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            Burst(finger, 1);
            main = finger.main;
            main.startLifetime = 0.6f;
            main.startSize = 1.05f;
            finger.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.VerticalBillboard;
            SizeOverLife(finger, Curve((0f, 0.15f), (0.1f, 1.2f), (0.2f, 0.95f), (0.3f, 1f), (0.85f, 1f), (1f, 0.6f)));
            Fade(finger, 0.8f);
            InFront(finger);

            var glint = Particles(frame, "Glint", _m.Flash, 0.7f, ParticleSystemSimulationSpace.Local);
            glint.transform.localPosition = new Vector3(0.15f, 0.8f, 0f);
            glint.emission.SetBursts(new[] { new ParticleSystem.Burst(0.1f, 1) });
            main = glint.main;
            main.startLifetime = 0.22f;
            main.startSize = 0.7f;
            SizeOverLife(glint, Curve((0f, 0.2f), (0.2f, 1.15f), (1f, 0f)));
            Spin(glint, 160f);
            glint.GetComponent<ParticleSystemRenderer>().sortingFudge = -15f;
            Sparkles(frame, 8, ParticleSystemScalingMode.Local, 0.45f);
            return _kit.Save(root);
        }

        /// <summary>Uppercut contact: gold behind blue, launched upward, with rising speed lines.</summary>
        private static GameObject DragonHit()
        {
            var root = _kit.NewRoot("FightGuyDragonHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, 1.1f, 0.12f, KiBlue, scaled);
            Pop(root, "Gold star", _m.ImpactGold, 0.82f, 0.16f, scaled, spin: true);
            Pop(root, "Impact", _m.Impact, 0.62f, 0.14f, scaled, spin: true);
            var lines = Particles(root, "Up lines", _m.Swoosh, 0.3f, scaling: scaled);
            Burst(lines, 9);
            var main = lines.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
            main.startSize = 0.08f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 13f);
            var shape = lines.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Stretch(lines, 0.05f, 2.2f);
            Fade(lines, 0.5f);
            InFront(lines);
            return _kit.Save(root);
        }

        // ── Cyclone Kick (R) ─────────────────────────────────────────────────────

        /// <summary>Chalk puffs kicked up along a foot's path while he spins forward.</summary>
        private static GameObject CycloneDust()
        {
            var root = _kit.NewRoot("FightGuyCycloneDust", attackBound: true);
            var dust = Particles(Frame(root), "Chalk", _m.Chalk, 1f);
            var main = dust.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.36f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 80;
            var emission = dust.emission;
            emission.rateOverDistance = 3.5f;
            Sphere(dust, 0.1f);
            Drift(dust, 0.4f, 0.1f, 0.6f);
            RandomFrame(dust, 2, 2);
            SizeOverLife(dust, Curve((0f, 0.5f), (0.3f, 1f), (1f, 1.25f)));
            Fade(dust, 0.4f);
            return _kit.Save(root);
        }

        /// <summary>
        /// Gold stars circling his head after the spin. Attack-bound, so they vanish exactly when
        /// he can act again: the gag doubles as an honest punish window.
        /// </summary>
        private static GameObject Dizzy()
        {
            var root = _kit.NewRoot("FightGuyCycloneDizzy", attackBound: true);
            var frame = Frame(root, upright: true);
            var stars = Particles(frame, "Stars", _m.Star, 2f, ParticleSystemSimulationSpace.Local);
            stars.transform.localPosition = new Vector3(0f, 0.24f, 0f);
            Burst(stars, 4);
            var main = stars.main;
            main.startLifetime = 2f;
            main.startSize = 0.24f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = stars.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.34f;
            shape.radiusThickness = 0f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var velocity = stars.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(7f);
            Spin(stars, 200f);
            SizeOverLife(stars, Curve((0f, 0.3f), (0.06f, 1.1f), (0.12f, 1f), (1f, 1f)));
            InFront(stars);
            var orbit = Particles(frame, "Orbit", _m.Ring, 2f, ParticleSystemSimulationSpace.Local);
            orbit.transform.localPosition = stars.transform.localPosition;
            Burst(orbit, 1);
            main = orbit.main;
            main.startLifetime = 2f;
            main.startSize = 0.76f;
            main.startColor = new Color(1f, 1f, 1f, 0.45f);
            orbit.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            return _kit.Save(root);
        }

        /// <summary>Spin-kick contact: a blue impact inside a gold wind ring, with chalk specks.</summary>
        private static GameObject CycloneHit()
        {
            var root = _kit.NewRoot("FightGuyCycloneHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, 1.05f, 0.12f, KiBlue, scaled);
            Pop(root, "Impact", _m.Impact, 0.7f, 0.14f, scaled, spin: true);
            var swirl = Particles(root, "Swirl", _m.RingGold, 0.4f, scaling: scaled);
            Burst(swirl, 1);
            var main = swirl.main;
            main.startLifetime = 0.22f;
            main.startSize = 0.85f;
            SizeOverLife(swirl, Curve((0f, 0.4f), (1f, 1.3f)));
            Fade(swirl, 0.4f);
            InFront(swirl);
            ChalkSpecks(root, 6, scaled);
            return _kit.Save(root);
        }

        // ── Fist of Fury (F) — kept serious ──────────────────────────────────────

        /// <summary>He locks in: gold power lines rising around him and a gold ring at his feet.</summary>
        private static GameObject FuryFocus()
        {
            var root = _kit.NewRoot("FightGuyFuryFocus", attackBound: true);
            var lines = Particles(root, "Power lines", _m.SwooshGold, 0.5f);
            var main = lines.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.13f);
            main.maxParticles = 48;
            var emission = lines.emission;
            emission.rateOverTime = 48f;
            var shape = lines.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.6f;
            shape.radiusThickness = 0.15f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var velocity = lines.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Stretch(lines, 0.07f, 2.6f);
            Fade(lines, 0.5f);

            var ring = Particles(root, "Floor ring", _m.RingGold, 0.6f);
            Burst(ring, 1);
            main = ring.main;
            main.startLifetime = 0.6f;
            main.startSize = 1.7f;
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeOverLife(ring, Curve((0f, 0.5f), (0.25f, 1f), (1f, 1.15f)));
            Fade(ring, 0.55f);
            return _kit.Save(root);
        }

        /// <summary>A glint in his eye as he locks in.</summary>
        private static GameObject FuryGlint()
        {
            var root = _kit.NewRoot("FightGuyFuryGlint", attackBound: true);
            var glint = Particles(Frame(root, upright: true), "Glint", _m.Flash, 0.3f, ParticleSystemSimulationSpace.Local);
            glint.transform.localPosition = new Vector3(0.06f, 0.06f, 0.12f);
            Burst(glint, 1);
            var main = glint.main;
            main.startLifetime = 0.22f;
            main.startSize = 0.42f;
            SizeOverLife(glint, Curve((0f, 0.2f), (0.25f, 1.15f), (1f, 0f)));
            Spin(glint, 140f);
            InFront(glint);
            return _kit.Save(root);
        }

        /// <summary>
        /// Ghost fists thrown forward with each punch. World-placed in front of him along his facing,
        /// so local +Z is the punch direction; stretched billboards keep each fist pointing along it.
        /// </summary>
        private static GameObject FuryGhost()
        {
            var root = _kit.NewRoot("FightGuyFuryGhost", attackBound: false);
            var fists = Particles(root, "Fists", _m.Fist, 0.2f);
            fists.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.035f, 1) });
            var main = fists.main;
            main.startLifetime = 0.14f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.34f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 8f);
            main.startColor = new Color(1f, 1f, 1f, 0.8f);
            Stretch(fists, 0f, 1f);
            Fade(fists, 0.35f);
            var streak = Particles(root, "Streak", _m.Swoosh, 0.2f);
            Burst(streak, 1);
            main = streak.main;
            main.startLifetime = 0.1f;
            main.startSize = 0.12f;
            main.startSpeed = 9f;
            main.startColor = new Color(1f, 1f, 1f, 0.7f);
            Stretch(streak, 0f, 5f);
            Fade(streak, 0.3f);
            return _kit.Save(root);
        }

        /// <summary>
        /// Fist of Fury contact. Punches (light, small contact scale): a small blue impact and a
        /// gold 肉. The finisher kick (contact scale 1.2 and up): a gold-and-blue starburst stamped
        /// with a gold 肉 seal, a shockwave ring along the attack and radial speed lines.
        /// </summary>
        private static GameObject FuryHit()
        {
            var root = _kit.NewRoot("FightGuyFuryHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var regular = new GameObject("Punch");
            regular.transform.SetParent(root.transform, false);
            Halo(regular, 0.9f, 0.1f, KiBlue, scaled);
            Pop(regular, "Impact", _m.Impact, 0.85f, 0.12f, scaled, spin: true);
            var niku = Pop(regular, "Niku", _m.Niku, 0.5f, 0.32f, scaled, spin: false);
            var nikuMain = niku.main;
            nikuMain.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);

            var heavy = new GameObject("Finisher");
            heavy.transform.SetParent(root.transform, false);
            Halo(heavy, 1.3f, 0.16f, Gold, scaled);
            Pop(heavy, "Gold burst", _m.ImpactGold, 0.92f, 0.24f, scaled, spin: true);
            Pop(heavy, "Blue burst", _m.Impact, 0.66f, 0.2f, scaled, spin: true);
            var seal = Pop(heavy, "Seal", _m.Seal, 0.46f, 0.42f, scaled, spin: false);
            seal.GetComponent<ParticleSystemRenderer>().sortingFudge = -20f;
            var shock = RingPop(heavy, "Shockwave", _m.Ring, 0.5f, 0.3f, 0f, scaled);
            var shockSize = shock.sizeOverLifetime;
            shockSize.size = new ParticleSystem.MinMaxCurve(1f, Curve((0f, 0.4f), (1f, 3.6f)));
            SpeedLines(heavy, 14, 13f, 0.08f, scaled);
            regular.SetActive(false);
            heavy.SetActive(false);
            var variant = root.AddComponent<DamageScaleVariant>();
            variant.HeavyFromScale = 1.2f;
            variant.Regular = regular;
            variant.Heavy = heavy;
            return _kit.Save(root);
        }

        // ── Shared layers ────────────────────────────────────────────────────────

        /// <summary>
        /// The content child of a bone-attached cue. It cancels the bone's scale (FightGuy's rig
        /// carries 1.5×) so offsets and local-space particles stay in metres.
        /// </summary>
        private static GameObject Frame(GameObject root, bool upright = false)
        {
            var frame = new GameObject("Frame");
            frame.transform.SetParent(root.transform, false);
            var component = frame.AddComponent<PresentationFrame>();
            component.UnitScale = true;
            component.Upright = upright;
            return frame;
        }

        private static ParticleSystem Halo(GameObject root, float size, float lifetime, Color color, ParticleSystemScalingMode scaling)
        {
            var halo = Particles(root, "Halo", _m.Glow, 0.3f, scaling: scaling);
            Burst(halo, 1);
            var main = halo.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startColor = new Color(color.r, color.g, color.b, 0.55f);
            SizeOverLife(halo, Curve((0f, 0.4f), (0.2f, 1f), (1f, 0.2f)));
            Fade(halo, 0.2f);
            Behind(halo);
            return halo;
        }

        /// <summary>A single shape that pops large, settles and fades: impacts, stars and seals.</summary>
        private static ParticleSystem Pop(GameObject root, string name, Material material, float size, float lifetime,
            ParticleSystemScalingMode scaling, bool spin)
        {
            var pop = Particles(root, name, material, 0.5f, scaling: scaling);
            Burst(pop, 1);
            var main = pop.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            if (spin) main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(pop, Curve((0f, 0.5f), (0.12f, 1.12f), (0.4f, 1f), (1f, 0.85f)));
            Fade(pop, 0.7f);
            InFront(pop);
            return pop;
        }

        /// <summary>A steady single particle for the life of a projectile or cue.</summary>
        private static ParticleSystem Steady(GameObject root, string name, Material material, float size, ParticleSystemScalingMode scaling)
        {
            var system = Particles(root, name, material, 1f, ParticleSystemSimulationSpace.Local, scaling);
            Burst(system, 1);
            var main = system.main;
            main.startLifetime = 60f;
            main.startSize = size;
            return system;
        }

        /// <summary>An expanding shockwave ring that faces the camera.</summary>
        private static ParticleSystem RingPop(GameObject root, string name, Material material, float size, float lifetime, float delay,
            ParticleSystemScalingMode scaling)
        {
            var ring = Particles(root, name, material, 0.5f, ParticleSystemSimulationSpace.Local, scaling);
            Burst(ring, 1);
            var main = ring.main;
            main.startDelay = delay;
            main.startLifetime = lifetime;
            main.startSize = size;
            SizeOverLife(ring, Curve((0f, 0.3f), (1f, 1.6f)));
            Fade(ring, 0.35f);
            InFront(ring);
            return ring;
        }

        private static void SpeedLines(GameObject root, int count, float speed, float size, ParticleSystemScalingMode scaling)
        {
            var lines = Particles(root, "Speed lines", _m.Swoosh, 0.3f, scaling: scaling);
            Burst(lines, count);
            var main = lines.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
            main.startSize = size;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed);
            Sphere(lines, 0.15f);
            Stretch(lines, 0.035f, 2.6f);
            Fade(lines, 0.5f);
            InFront(lines);
        }

        private static void Sparkles(GameObject root, int count, ParticleSystemScalingMode scaling, float radius = 0.1f)
        {
            var sparkles = Particles(root, "Sparkles", _m.Star, 0.5f, scaling: scaling);
            Burst(sparkles, count);
            var main = sparkles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.4f;
            Sphere(sparkles, radius);
            Drag(sparkles, 0.25f);
            Spin(sparkles, 300f);
            SizeOverLife(sparkles, Curve((0f, 1f), (1f, 0.2f)));
            Fade(sparkles, 0.6f);
            InFront(sparkles);
        }

        private static void ChalkPuffs(GameObject root, int count, float size, float speed)
        {
            var puffs = Particles(root, "Chalk", _m.Chalk, 0.5f);
            Burst(puffs, count);
            var main = puffs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.42f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.05f;
            Sphere(puffs, 0.06f);
            Drag(puffs, 0.3f);
            RandomFrame(puffs, 2, 2);
            SizeOverLife(puffs, Curve((0f, 0.5f), (0.3f, 1f), (1f, 1.2f)));
            Fade(puffs, 0.45f);
        }

        private static void ChalkSpecks(GameObject root, int count, ParticleSystemScalingMode scaling)
        {
            var specks = Particles(root, "Chalk specks", _m.Chalk, 0.5f, scaling: scaling);
            Burst(specks, count);
            var main = specks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.gravityModifier = 0.6f;
            Sphere(specks, 0.1f);
            Drag(specks, 0.2f);
            RandomFrame(specks, 2, 2);
            Fade(specks, 0.5f);
        }

        /// <summary>
        /// A world-space particle ribbon behind a moving limb: particles emitted along the bone's
        /// path for <paramref name="emitFor"/> seconds and drawn only as their connecting trail.
        /// </summary>
        private static void Ribbon(GameObject root, string name, Material material, float width, float emitFor)
        {
            var ribbon = Particles(root, name, material, emitFor);
            var main = ribbon.main;
            main.startLifetime = 0.13f;
            main.startSize = width;
            main.maxParticles = 128;
            var emission = ribbon.emission;
            emission.rateOverTime = 240f;
            var trails = ribbon.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.Ribbon;
            trails.ribbonCount = 1;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = true;
            trails.dieWithParticles = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, Curve((0f, 1f), (1f, 0.35f)));
            var renderer = ribbon.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.None;
            renderer.trailMaterial = material;
            Fade(ribbon, 0.35f);
        }

        // ── Assets ───────────────────────────────────────────────────────────────

        private static Materials BuildMaterials()
        {
            var niku = new MaskField(NikuMaskPath);
            return new Materials
            {
                Glow = _kit.ParticleMaterial("FightGuyGlowAdd", _kit.BakeTexture("FightGuyGlow", 128, 128,
                    (u, v) => Alpha(Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f))), new Color(1.3f, 1.3f, 1.3f), additive: true),
                Flash = Mat("FightGuyFlash", 128, 128, FlashPixel),
                KiBall = Mat("FightGuyKiBall", 128, 128, KiBallPixel),
                Niku = Mat("FightGuyNiku", 256, 256, (u, v) => ToonShapes.Bands(niku.Sample(u, v) * 0.5f, 3.5f, 4f, GoldRim, Gold, GoldPale)),
                Stamp = Mat("FightGuyNikuStamp", 256, 256, (u, v) => StampPixel(u, v, niku)),
                Impact = Mat("FightGuyImpact", 256, 256, (u, v) => StarBurst(u, v, 9, 0.42f, 11, KiRim, KiBlue, Color.white)),
                ImpactGold = Mat("FightGuyImpactGold", 256, 256, (u, v) => StarBurst(u, v, 11, 0.4f, 21, GoldRim, Gold, GoldPale)),
                Pow = Mat("FightGuyPow", 256, 256, PowPixel),
                Ring = Mat("FightGuyRing", 256, 256, (u, v) => ToonShapes.Bands((Mathf.Abs(Radius(u, v) - 0.8f) - 0.07f) * 128f, 2f, 3f, KiRim, KiBlue, Color.white)),
                RingGold = Mat("FightGuyRingGold", 256, 256, (u, v) => ToonShapes.Bands((Mathf.Abs(Radius(u, v) - 0.8f) - 0.07f) * 128f, 2f, 3f, GoldRim, Gold, GoldPale)),
                Swoosh = Mat("FightGuySwoosh", 256, 64, (u, v) => SwooshPixel(u, v, Color.white)),
                SwooshGold = Mat("FightGuySwooshGold", 256, 64, (u, v) => SwooshPixel(u, v, GoldPale, GoldRim, Gold)),
                Ribbon = Mat("FightGuyRibbon", 128, 64, (u, v) => RibbonPixel(u, v, KiRim, KiBlue, Color.white)),
                RibbonGold = Mat("FightGuyRibbonGold", 128, 64, (u, v) => RibbonPixel(u, v, GoldRim, Gold, GoldPale)),
                Chalk = Mat("FightGuyChalk", 256, 256, ChalkPixel),
                Foam = Mat("FightGuyFoamFinger", 256, 256, FoamPixel),
                Fist = Mat("FightGuyFist", 128, 128, FistPixel),
                Star = Mat("FightGuyStar", 128, 128, (u, v) => ToonShapes.Bands(Polygon(P(u, v), Star5) * 64f, 2.2f, 4f, GoldRim, Gold, GoldPale)),
                Spotlight = _kit.ParticleMaterial("FightGuySpotlight", _kit.BakeTexture("FightGuySpotlight", 128, 256, SpotlightPixel),
                    new Color(1.1f, 1.04f, 0.86f, 0.55f)),
                Seal = Mat("FightGuySeal", 256, 256, (u, v) => SealPixel(u, v, niku)),
                Helix = HelixMesh("FightGuyHelix"),
            };
        }

        private static Material Mat(string name, int width, int height, System.Func<float, float, Color> pixel)
            => _kit.ParticleMaterial(name, _kit.BakeTexture(name, width, height, pixel));

        private static Vector2 P(float u, float v) => new(u * 2f - 1f, v * 2f - 1f);

        private static readonly Vector2[] Star5 = Enumerable.Range(0, 10).Select(k =>
        {
            float r = k % 2 == 0 ? 0.95f : 0.42f, a = Mathf.PI / 2f + k * Mathf.PI / 5f;
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }).ToArray();

        private static readonly Dictionary<int, Vector2[]> Outlines = new();

        private static Color StarBurst(float u, float v, int spikes, float inner, int seed, Color rim, Color accent, Color core)
        {
            if (!Outlines.TryGetValue(seed, out var outline))
                Outlines[seed] = outline = StarOutline(spikes, inner, seed);
            return ToonShapes.Bands(Polygon(P(u, v), outline) * 128f, 3f, 9f, rim, accent, core);
        }

        private static readonly Vector2[] PowOutline = Enumerable.Range(0, 96).Select(k =>
        {
            float t = k / 96f * Mathf.PI * 2f, r = 0.8f * (1f + 0.14f * Mathf.Cos(8f * t));
            return new Vector2(Mathf.Cos(t) * r, Mathf.Sin(t) * r);
        }).ToArray();

        private static Color PowPixel(float u, float v)
            => ToonShapes.Bands(Polygon(P(u, v), PowOutline) * 128f, 3f, 12f, KiRim, KiBlue, Color.white);

        /// <summary>A camera flash: long white cross spikes with warm edges and a white centre.</summary>
        private static Color FlashPixel(float u, float v)
        {
            var p = P(u, v);
            var q = Rotate(p, 45f);
            float d = Mathf.Min(Mathf.Min(Rhombus(p, new Vector2(0.98f, 0.08f)), Rhombus(p, new Vector2(0.08f, 0.72f))),
                Mathf.Min(Mathf.Min(Rhombus(q, new Vector2(0.34f, 0.06f)), Rhombus(q, new Vector2(0.06f, 0.34f))), p.magnitude - 0.16f));
            return ToonShapes.Bands(d * 64f, 1.2f, 1.8f, WarmEdge, WarmPale, Color.white);
        }

        /// <summary>A toon ki sphere: navy rim, blue band, pale inside and a white-hot centre.</summary>
        private static Color KiBallPixel(float u, float v)
        {
            float r = Radius(u, v);
            Color color = ToonShapes.Bands((r - 0.94f) * 64f, 2.5f, 7f, KiRim, KiBlue, KiPale);
            float hot = Mathf.Clamp01((0.5f - r) / 0.18f) * 0.95f;
            return new Color(Mathf.Lerp(color.r, 1f, hot), Mathf.Lerp(color.g, 1f, hot), Mathf.Lerp(color.b, 1f, hot), color.a);
        }

        /// <summary>The red ink 肉 stamp: rounded-square border and glyph, with uneven ink.</summary>
        private static Color StampPixel(float u, float v, MaskField niku)
        {
            var p = P(u, v);
            float border = Mathf.Abs(Box(p, new Vector2(0.66f, 0.66f)) - 0.2f) - 0.075f;
            const float glyphScale = 0.66f;
            float glyph = niku.Sample(0.5f + (u - 0.5f) / glyphScale, 0.5f + (v - 0.5f) / glyphScale) * 0.5f * glyphScale;
            float ink = Mathf.Max(Mathf.Clamp01(0.5f - border * 128f), Mathf.Clamp01(0.5f - glyph));
            float noise = ValueNoise(u, v, 14f, 3) * 0.6f + ValueNoise(u, v, 41f, 7) * 0.4f;
            ink *= Mathf.Clamp((noise - 0.22f) * 5f, 0.5f, 1f);
            float edge = Mathf.Clamp01(0.5f - Mathf.Min(border * 128f, glyph) - 1.5f);
            Color color = Color.Lerp(InkDark, InkRed, edge);
            color.a = ink * 0.95f;
            return color;
        }

        /// <summary>Swoosh lens along U: dark rim, blue body and a white (or gold) core.</summary>
        private static Color SwooshPixel(float u, float v, Color core, Color? rim = null, Color? accent = null)
        {
            var p = new Vector2(u * 2f - 1f, (v * 2f - 1f) * 0.25f);
            return ToonShapes.Bands(Lens(p, 0.95f, 0.17f) * 128f, 1.6f, 3.2f, rim ?? KiRim, accent ?? KiBlue, core);
        }

        /// <summary>A trail and helix band: banded across V, fading toward the U=1 end.</summary>
        private static Color RibbonPixel(float u, float v, Color rim, Color accent, Color core)
        {
            float across = Mathf.Abs(v * 2f - 1f);
            Color color = ToonShapes.Bands((across - 0.9f) * 32f, 2f, 6f, rim, accent, core);
            color.a *= Mathf.Min(Mathf.Clamp01(u / 0.06f), Mathf.Clamp01((1f - u) / 0.45f));
            return color;
        }

        private static readonly Vector3[][] ChalkCells =
        {
            new[] { new Vector3(-0.3f, -0.1f, 0.42f), new Vector3(0.3f, -0.05f, 0.38f), new Vector3(0f, 0.25f, 0.45f) },
            new[] { new Vector3(-0.25f, 0f, 0.5f), new Vector3(0.35f, 0.1f, 0.35f), new Vector3(0.05f, -0.3f, 0.3f) },
            new[] { new Vector3(0f, 0f, 0.55f), new Vector3(-0.45f, -0.2f, 0.28f), new Vector3(0.45f, -0.15f, 0.3f) },
            new[] { new Vector3(-0.2f, 0.2f, 0.4f), new Vector3(0.25f, 0.25f, 0.36f), new Vector3(0f, -0.25f, 0.42f) },
        };

        /// <summary>Four chalk-puff clusters (2×2 sheet): grey rim, cream body, white top.</summary>
        private static Color ChalkPixel(float u, float v)
        {
            int cell = (u < 0.5f ? 0 : 1) + (v < 0.5f ? 2 : 0);
            var p = new Vector2((u % 0.5f) * 4f - 1f, (v % 0.5f) * 4f - 1f);
            float d = float.MaxValue;
            foreach (var c in ChalkCells[cell])
                d = Mathf.Min(d, (p - new Vector2(c.x, c.y)).magnitude - c.z);
            return ToonShapes.Bands(d * 64f, 2.5f, 4f, ChalkRim, Chalk, Color.white);
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        private static float RoundBox(Vector2 p, Vector2 centre, Vector2 half, float r) => Box(p - centre, half - new Vector2(r, r)) - r;

        private static Color Over(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0f) return new Color(over.r, over.g, over.b, 0f);
            return new Color((over.r * over.a + under.r * under.a * (1f - over.a)) / a,
                (over.g * over.a + under.g * under.a * (1f - over.a)) / a,
                (over.b * over.a + under.b * under.a * (1f - over.a)) / a, a);
        }

        /// <summary>A foam "#1" finger: yellow foam hand, blue cuff, "#1" in blue with a white outline.</summary>
        private static Color FoamPixel(float u, float v)
        {
            var p = P(u, v);
            float hand = Mathf.Min(Mathf.Min(RoundBox(p, new Vector2(0.02f, -0.14f), new Vector2(0.36f, 0.33f), 0.13f),
                    Capsule(p, new Vector2(-0.04f, 0.1f), new Vector2(-0.08f, 0.82f), 0.135f)),
                Mathf.Min(Capsule(p, new Vector2(-0.3f, -0.04f), new Vector2(-0.5f, 0.2f), 0.1f),
                    Mathf.Min((p - new Vector2(0.32f, 0.08f)).magnitude - 0.1f, (p - new Vector2(0.35f, -0.1f)).magnitude - 0.1f)));
            Color color = ToonShapes.Bands(hand * 128f, 3.5f, 99f, FoamRim, Foam, Foam);
            float shine = Capsule(p, new Vector2(-0.13f, 0.22f), new Vector2(-0.15f, 0.7f), 0.025f);
            if (color.a > 0f) color = Color.Lerp(color, new Color(1f, 0.93f, 0.62f, color.a), Mathf.Clamp01(0.5f - shine * 128f) * 0.9f);
            color = Over(color, ToonShapes.Bands(RoundBox(p, new Vector2(0.02f, -0.56f), new Vector2(0.4f, 0.11f), 0.04f) * 128f,
                3f, 99f, ClothRim, Cloth, Cloth));
            float text = Mathf.Min(
                Mathf.Min(Box(p - new Vector2(-0.2f, -0.13f), new Vector2(0.026f, 0.15f)), Box(p - new Vector2(-0.09f, -0.13f), new Vector2(0.026f, 0.15f))),
                Mathf.Min(Box(p - new Vector2(-0.145f, -0.06f), new Vector2(0.12f, 0.024f)), Box(p - new Vector2(-0.145f, -0.2f), new Vector2(0.12f, 0.024f))));
            text = Mathf.Min(text, Mathf.Min(Box(p - new Vector2(0.13f, -0.13f), new Vector2(0.036f, 0.16f)),
                Mathf.Min(Capsule(p, new Vector2(0.12f, 0.02f), new Vector2(0.05f, -0.03f), 0.03f),
                    Box(p - new Vector2(0.13f, -0.28f), new Vector2(0.08f, 0.025f)))));
            return Over(color, ToonShapes.Bands(text * 128f, 2.5f, 99f, Color.white, Cloth, Cloth));
        }

        /// <summary>A ghost fist pointing along +U: wrist, fist, four knuckles and a thumb, with creases.</summary>
        private static Color FistPixel(float u, float v)
        {
            var p = P(u, v);
            float d = Mathf.Min(Mathf.Min(Capsule(p, new Vector2(-0.95f, 0.02f), new Vector2(-0.45f, 0f), 0.26f),
                    RoundBox(p, new Vector2(0.08f, 0f), new Vector2(0.42f, 0.4f), 0.18f)),
                Capsule(p, new Vector2(-0.12f, -0.33f), new Vector2(0.32f, -0.27f), 0.12f));
            for (int k = 0; k < 4; k++)
                d = Mathf.Min(d, (p - new Vector2(0.47f, -0.3f + k * 0.2f)).magnitude - 0.13f);
            Color color = ToonShapes.Bands(d * 64f, 2.2f, 5f, KiRim, KiBlue, KiPale);
            foreach (float y in new[] { -0.2f, 0f, 0.2f })
                if (Mathf.Abs(p.y - y) < 0.022f && p.x > 0.2f && p.x < 0.45f && color.a > 0.5f)
                    color = Color.Lerp(color, new Color(KiRim.r, KiRim.g, KiRim.b, color.a), 0.8f);
            return color;
        }

        /// <summary>
        /// A soft spotlight beam: narrow toward its source at the top, widest and brightest around
        /// the fighter in its lower half, with a soft bottom edge.
        /// </summary>
        private static Color SpotlightPixel(float u, float v)
        {
            float half = Mathf.Lerp(0.95f, 0.22f, v), edge = Mathf.Abs(u * 2f - 1f) / half;
            float a = (1f - Step(0.6f, 1f, edge)) * Step(0f, 0.14f, v) * (1f - 0.65f * Step(0.45f, 1f, v));
            return Alpha(a);
        }

        /// <summary>A gold champion seal: plate, engraved inner ring and a dark red 肉.</summary>
        private static Color SealPixel(float u, float v, MaskField niku)
        {
            var p = P(u, v);
            float plate = (new Vector2(p.x / 0.92f, p.y / 0.76f).magnitude - 1f) * 0.76f;
            Color color = ToonShapes.Bands(plate * 128f, 3.5f, 99f, GoldRim, Gold, Gold);
            float engraved = Mathf.Abs((new Vector2(p.x / 0.76f, p.y / 0.6f).magnitude - 1f) * 0.6f) - 0.02f;
            if (color.a > 0f) color = Color.Lerp(color, new Color(GoldRim.r, GoldRim.g, GoldRim.b, color.a), Mathf.Clamp01(0.5f - engraved * 128f) * 0.7f);
            const float glyphScale = 0.6f;
            float glyph = niku.Sample(0.5f + (u - 0.5f) / glyphScale, 0.5f + (v - 0.5f) / glyphScale) * 0.5f * glyphScale;
            return Over(color, ToonShapes.Bands(glyph, 2f, 99f, GoldPale, InkDark, InkDark));
        }

        /// <summary>
        /// A vertical ribbon winding two turns up a slightly narrowing cylinder, from its pivot
        /// (height 0) to height 1. U runs along the ribbon and V across it.
        /// </summary>
        private static Mesh HelixMesh(string name)
        {
            const int segments = 96;
            const float turns = 2f, width = 0.075f;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments, angle = t * turns * Mathf.PI * 2f, radius = Mathf.Lerp(0.5f, 0.34f, t);
                var centre = new Vector3(Mathf.Cos(angle) * radius, t, Mathf.Sin(angle) * radius);
                vertices.Add(centre - Vector3.up * width * 0.5f);
                vertices.Add(centre + Vector3.up * width * 0.5f);
                uvs.Add(new Vector2(t, 0f));
                uvs.Add(new Vector2(t, 1f));
                if (i == 0) continue;
                int a = (i - 1) * 2;
                triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }
    }
}
#endif
