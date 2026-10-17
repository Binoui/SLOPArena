#if UNITY_EDITOR
using System.Collections.Generic;
using SlopArena.Client.Animation;
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
    /// Rebuilds Bonk's package-owned combat presentation from code (docs/characters/bonk.md):
    /// "BONK!". His name is the sound his hits make, so his big hits print a comic BONK! in
    /// bandage cream on bucket red. A thick red smear follows the plank, contacts are blunt
    /// "thunks" with ringing arcs instead of sharp sparks, and dust, cracks and rubble give him
    /// weight. The gags are on him: he snorts like a bull before an armoured charge, his own
    /// bucket rings after a headbutt, and a hit his armour shrugs off gets a little "?". Workflow
    /// and readability rules are in skills/sloparena-character-vfx-pass; plumbing is in
    /// <see cref="PresentationBuilderKit"/>.
    /// </summary>
    public static class BonkPresentationBuilder
    {
        private const string LettersMaskPath = "Assets/CharacterPackages/bonk/Presentation/Source/Bonk.png";
        private const string QuestionMaskPath = "Assets/CharacterPackages/bonk/Presentation/Source/Question.png";
        // The bucket's eye slit in the head cue frame (metres; its axes match the fighter's at
        // rest), measured from the helmet vertices: the face recesses there 0.05–0.08 m up.
        private static readonly Vector3 Slit = new(0f, 0.065f, 0.25f);
        private const float BucketTop = 0.29f;
        // Lifetime of each smear sample behind the plank (the weapon config's HitboxMotionTime).
        private const float SmearSeconds = 0.12f;
        // Blade Storm's whirl: orbit radius (about the plank's reach) and spin in radians per second.
        private const float WhirlRadius = 1.7f, WhirlSpin = 15f;
        // How far the BONK! sits in front of its contact, toward the camera (metres).
        private const float LetteringForward = 1f;

        private static readonly Color Rim = new(0.165f, 0.086f, 0.071f);
        private static readonly Color Red = new(0.886f, 0.204f, 0.239f);
        private static readonly Color RedDark = new(0.56f, 0.1f, 0.13f);
        private static readonly Color RedLight = new(1f, 0.42f, 0.38f);
        private static readonly Color Cream = new(1f, 0.945f, 0.84f);
        private static readonly Color Hot = new(1f, 0.55f, 0.3f);
        private static readonly Color DustRim = new(0.29f, 0.192f, 0.141f);
        private static readonly Color Dust = new(0.788f, 0.643f, 0.486f);
        private static readonly Color DustLight = new(0.925f, 0.847f, 0.714f);
        private static readonly Color Stone = new(0.663f, 0.545f, 0.416f);
        private static readonly Color SteelRim = new(0.086f, 0.094f, 0.11f);
        private static readonly Color Steel = new(0.655f, 0.678f, 0.698f);
        private static readonly Color SteelLight = new(0.933f, 0.949f, 0.957f);
        private static readonly Color SteamRim = new(0.365f, 0.408f, 0.447f);
        private static readonly Color Steam = new(0.957f, 0.965f, 0.973f);

        private static PresentationBuilderKit _kit;
        private static Materials _m;

        private sealed class Materials
        {
            public Material Glow, Thunk, Rings, Letters, Balloon, Question, Dust, Puff, Rock, Chip, Cracks, Eye, Line,
                Glint, Ribbon, SmearBand;
        }

        [CliCommand("sloparena.presentation.bonk.build", "Rebuild Bonk's package presentation prefabs, textures, materials, sword-trail style and bindings in Edit Mode. Saving the catalog triggers the usual package cook.", MainThreadRequired = true, Tags = new[] { "presentation", "character" })]
        public static object Build()
        {
            using var kit = new PresentationBuilderKit("bonk", audioFolder: "Bonk");
            _kit = kit;
            _m = BuildMaterials();
            var built = new List<(string id, GameObject prefab)>
            {
                ("presentation.bonk.normal.hit", NormalHit()),
                ("presentation.bonk.heavy.hit", BigHit("BonkHeavyHit", thunk: 0.85f, letters: 0.72f, lift: 0.35f, sound: "bonk_bonk", volume: 0.7f, pitch: 1.05f)),
                ("presentation.bonk.bash.hit", BashHit()),
                ("presentation.bonk.slam.hit", BigHit("BonkSlamHit", thunk: 0.9f, letters: 1f, lift: 0.45f, sound: "bonk_bonk", volume: 0.8f, pitch: 0.92f)),
                ("presentation.bonk.slash.hit", SlashHit()),
                ("presentation.bonk.storm.hit", StormHit()),
                ("presentation.bonk.normal.glint", Glint()),
                ("presentation.bonk.normal.plant", FootPlant()),
                ("presentation.bonk.slash.cut", Swing("BonkRoughCut", "bonk_swing", 0.55f)),
                ("presentation.bonk.slash.heavy", Swing("BonkHeavyCut", "bonk_swing_heavy", 0.7f)),
                ("presentation.bonk.bash.snort", Snort()),
                ("presentation.bonk.bash.charge", ChargeDust()),
                ("presentation.bonk.armor", Armor()),
                ("presentation.bonk.slam.launch", SlamLaunch("BonkSlamLaunch", ground: true)),
                ("presentation.bonk.slam.air-launch", SlamLaunch("BonkSlamAirLaunch", ground: false)),
                ("presentation.bonk.slam.landing", SlamLanding()),
                ("presentation.bonk.storm.wind", Whirl("BonkBladeStormWind")),
                ("presentation.bonk.storm.air-wind", Whirl("BonkAirStormWind")),
                ("presentation.bonk.storm.dust", StormDust()),
            };
            var trail = SmearTrail();
            kit.BindWeaponTrail(trail);
            SetSmearLifetime();
            return kit.Finish(built, new { trail = AssetDatabase.GetAssetPath(trail) });
        }

        [MenuItem("Tools/SLOPArena/Presentation/Rebuild Bonk Presentation")]
        private static void Menu() => Debug.Log("[BonkPresentationBuilder] " + Newtonsoft.Json.JsonConvert.SerializeObject(Build()));

        // ── Hits ─────────────────────────────────────────────────────────────────

        /// <summary>Normals 1–3: a blunt red thunk with ringing arcs, steel chips and a puff of dust.</summary>
        private static GameObject NormalHit()
        {
            var root = _kit.NewRoot("BonkNormalHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, 1f, 0.12f, Red, scaled);
            Thunk(root, 0.8f, 0.16f, scaled);
            Rings(root, 1.2f, 0.22f, 0.02f, scaled);
            Chips(root, 3, scaled);
            DustPuffs(root, "Dust", 2, 0.22f, 0.3f, 1.2f, scaled);
            _kit.Sound(root, "bonk_thock", 0.55f, 1f);
            return Save(root);
        }

        /// <summary>
        /// A big hit: a bigger thunk and the BONK! balloon. Contacts scale with damage
        /// (sqrt(damage/6), 0.6–1.6), so <paramref name="letters"/> is the width at 6 damage.
        /// </summary>
        private static GameObject BigHit(string name, float thunk, float letters, float lift, string sound, float volume, float pitch)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, thunk * 1.45f, 0.14f, Red, scaled);
            Thunk(root, thunk, 0.18f, scaled);
            Rings(root, thunk * 1.45f, 0.24f, 0.02f, scaled);
            Chips(root, 4, scaled);
            DustPuffs(root, "Dust", 3, 0.24f, 0.34f, 1.4f, scaled);
            Lettering(root, letters, lift, 0.03f, scaled);
            _kit.Sound(root, sound, volume, pitch);
            return Save(root);
        }

        /// <summary>
        /// Shoulder Bash: the big BONK! at the contact, and his own bucket ringing twice on his
        /// head (<see cref="SnapToOwnerBone"/> moves that part from the contact onto him).
        /// </summary>
        private static GameObject BashHit()
        {
            var root = _kit.NewRoot("BonkBashImpact", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, 1.4f, 0.14f, Red, scaled);
            Thunk(root, 1f, 0.18f, scaled);
            Chips(root, 5, scaled);
            DustPuffs(root, "Dust", 3, 0.26f, 0.36f, 1.5f, scaled);
            Lettering(root, 1.4f, 0.55f, 0.03f, scaled);
            _kit.Sound(root, "bonk_bonk", 0.8f, 1f);

            var bucket = new GameObject("Bucket ring");
            bucket.transform.SetParent(root.transform, false);
            var snap = bucket.AddComponent<SnapToOwnerBone>();
            snap.BoneId = "bone.head";
            snap.Offset = new Vector3(0f, 0.15f, 0f);
            Rings(bucket, 0.95f, 0.26f, 0.04f, scaled, ParticleSystemSimulationSpace.Local);
            Rings(bucket, 1.15f, 0.28f, 0.2f, scaled, ParticleSystemSimulationSpace.Local, "Rings again");
            _kit.Sound(bucket, "bonk_doong", 0.55f, 1f);
            return Save(root);
        }

        /// <summary>Charging Double Slash: slash 1 (4 damage) a small thunk, slash 2 (8) a bigger one.</summary>
        private static GameObject SlashHit()
        {
            var root = _kit.NewRoot("BonkSlashHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var regular = Child(root, "First slash");
            Thunk(regular, 0.8f, 0.14f, scaled);
            Rings(regular, 1.1f, 0.2f, 0.02f, scaled);
            Chips(regular, 2, scaled);
            _kit.Sound(regular, "bonk_thock", 0.5f, 1.1f);
            var heavy = Child(root, "Second slash");
            Halo(heavy, 1.2f, 0.12f, Red, scaled);
            Thunk(heavy, 0.95f, 0.17f, scaled);
            Rings(heavy, 1.35f, 0.24f, 0.02f, scaled);
            Chips(heavy, 4, scaled);
            DustPuffs(heavy, "Dust", 2, 0.24f, 0.32f, 1.3f, scaled);
            _kit.Sound(heavy, "bonk_thock", 0.65f, 0.88f);
            return Variant(root, regular, heavy, 1f);
        }

        /// <summary>Blade Storm: spin hits (2.5 damage) are small thunks; the finisher (12) is the biggest BONK!.</summary>
        private static GameObject StormHit()
        {
            var root = _kit.NewRoot("BonkStormHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var regular = Child(root, "Spin");
            Thunk(regular, 0.7f, 0.12f, scaled);
            Chips(regular, 2, scaled);
            _kit.Sound(regular, "bonk_thock", 0.4f, 1.2f);
            var heavy = Child(root, "Finisher");
            Halo(heavy, 1.4f, 0.16f, Red, scaled);
            Thunk(heavy, 1f, 0.2f, scaled);
            Rings(heavy, 1.45f, 0.26f, 0.02f, scaled);
            Chips(heavy, 6, scaled);
            DustPuffs(heavy, "Dust", 4, 0.28f, 0.4f, 1.8f, scaled);
            Lettering(heavy, 1.3f, 0.5f, 0.03f, scaled);
            _kit.Sound(heavy, "bonk_bonk", 0.85f, 0.95f);
            var ring = Child(heavy, "Ring");
            _kit.Sound(ring, "bonk_doong", 0.4f, 1.1f);
            return Variant(root, regular, heavy, 1.2f);
        }

        private static GameObject Variant(GameObject root, GameObject regular, GameObject heavy, float heavyFromScale)
        {
            regular.SetActive(false);
            heavy.SetActive(false);
            var variant = root.AddComponent<DamageScaleVariant>();
            variant.HeavyFromScale = heavyFromScale;
            variant.Regular = regular;
            variant.Heavy = heavy;
            return Save(root);
        }

        // ── Swings ───────────────────────────────────────────────────────────────

        /// <summary>A chunky cream-and-red twinkle on the plank's end before a big swing lands.</summary>
        private static GameObject Glint()
        {
            var root = _kit.NewRoot("BonkBladeGlint", attackBound: true);
            var frame = Frame(root);
            var glint = Particles(frame, "Glint", _m.Glint, 0.3f, ParticleSystemSimulationSpace.Local);
            Burst(glint, 1);
            var main = glint.main;
            main.startLifetime = 0.16f;
            main.startSize = 0.42f;
            SizeOverLife(glint, Curve((0f, 0.2f), (0.3f, 1.15f), (1f, 0f)));
            Spin(glint, 200f);
            InFront(glint);
            Halo(frame, 0.45f, 0.14f, Red, ParticleSystemScalingMode.Local);
            return Save(root);
        }

        /// <summary>
        /// Swing sounds on the hilt (a low "whoomp"). The thick red smear itself is the sword-trail
        /// style (<see cref="SmearTrail"/>), drawn along the whole plank.
        /// </summary>
        private static GameObject Swing(string name, string sound, float volume)
        {
            var root = _kit.NewRoot(name, attackBound: true);
            _kit.Sound(root, sound, volume, 1f);
            return Save(root);
        }

        /// <summary>A planted foot kicks a ring of dust along the floor.</summary>
        private static GameObject FootPlant()
        {
            var root = _kit.NewRoot("BonkFootPlant", attackBound: true);
            var floor = Floor(root, 0.6f, -0.05f);
            var dust = DustPuffs(floor, "Dust", 4, 0.3f, 0.42f, 2.2f, ParticleSystemScalingMode.Local, life: 0.45f);
            FlatRing(dust, 0.15f);
            _kit.Sound(root, "bonk_scuff", 0.45f, 1f);
            return Save(root);
        }

        /// <summary>
        /// The plank's sword-trail style: a thick bucket-red smear with a cream streak near the
        /// leading edge and a dark rim at the end of the plank (SwordSweepTrail reads the root's
        /// material, tint and colour over lifetime).
        /// </summary>
        private static GameObject SmearTrail()
        {
            var root = _kit.NewRoot("BonkSmearTrail", attackBound: false);
            var ribbon = root.AddComponent<ParticleSystem>();
            ribbon.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ribbon.main;
            main.duration = 0.3f;
            main.loop = false;
            main.playOnAwake = false;
            main.startColor = Color.white;
            var emission = ribbon.emission;
            emission.enabled = false;
            var shape = ribbon.shape;
            shape.enabled = false;
            var color = ribbon.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 1f), (0.45f, 0.92f), (1f, 0f) });
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _m.SmearBand;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return Save(root);
        }

        private static void SetSmearLifetime()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>($"{_kit.PackageRoot}/CharacterAssetCatalog.asset");
            var weapon = catalog != null ? catalog.WeaponConfig : null;
            if (weapon?.Entries == null) return;
            foreach (var entry in weapon.Entries)
                if (entry?.TrailStylePrefab != null)
                    entry.HitboxMotionTime = SmearSeconds;
            EditorUtility.SetDirty(weapon);
        }

        // ── Shoulder Bash and Charging Double Slash (armour) ─────────────────────

        /// <summary>
        /// The tell before an armoured charge: he snorts like a bull, two puffs out of the
        /// bucket's eye slit ("hmph-hmph"), and his eyes flash in the slit.
        /// </summary>
        private static GameObject Snort()
        {
            var root = _kit.NewRoot("BonkSnort", attackBound: true);
            var frame = Frame(root);
            Eyes(frame, 0.3f, loop: false);
            foreach (float side in new[] { -1f, 1f })
            {
                var jet = Particles(frame, side < 0f ? "Snort left" : "Snort right", _m.Puff, 0.6f);
                jet.transform.localPosition = Slit + new Vector3(0.05f * side, -0.01f, 0.03f);
                jet.transform.localRotation = Quaternion.LookRotation(new Vector3(0.25f * side, -0.45f, 1f).normalized);
                var main = jet.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.45f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.24f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var emission = jet.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3), new ParticleSystem.Burst(0.15f, 3) });
                var shape = jet.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 14f;
                shape.radius = 0.015f;
                Drag(jet, 0.28f);
                RandomFrame(jet, 2, 2);
                SizeOverLife(jet, Curve((0f, 0.5f), (0.25f, 1f), (1f, 1.6f)));
                Fade(jet, 0.4f);
                InFront(jet);
            }
            _kit.Sound(root, "bonk_snort", 0.6f, 1f);
            return Save(root);
        }

        /// <summary>
        /// The armour window: a pulsing bucket-red rim on Bonk and his eyes lit in the slit, for
        /// exactly the authored window (<see cref="ArmorGlowPresentation"/>). When the armour
        /// shrugs off a hit, a steel tink and a little "?" pop over his bucket. He didn't notice.
        /// </summary>
        private static GameObject Armor()
        {
            var root = _kit.NewRoot("BonkArmor", attackBound: true);
            var glow = root.AddComponent<ArmorGlowPresentation>();
            glow.RimColor = new Color(1.5f, 0.32f, 0.26f, 1f);
            Eyes(Frame(root), 3f, loop: true);

            var upright = Frame(root, upright: true);
            var absorb = Child(upright, "Absorb");
            absorb.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            var question = Particles(absorb, "Question", _m.Question, 1f, ParticleSystemSimulationSpace.Local);
            question.transform.localPosition = new Vector3(0f, BucketTop + 0.3f, 0f);
            Burst(question, 1);
            var main = question.main;
            main.startDelay = 0.08f;
            main.startLifetime = 0.85f;
            main.startSize = 0.42f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0.14f);
            var velocity = question.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.25f, 0.25f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            SizeOverLife(question, Curve((0f, 0.2f), (0.12f, 1.25f), (0.22f, 0.92f), (0.32f, 1f), (1f, 1f)));
            Fade(question, 0.75f);
            question.GetComponent<ParticleSystemRenderer>().sortingFudge = -18f;
            var tink = Particles(absorb, "Tink", _m.Glint, 0.3f, ParticleSystemSimulationSpace.Local);
            tink.transform.localPosition = new Vector3(0f, 0.1f, 0.15f);
            Burst(tink, 1);
            main = tink.main;
            main.startLifetime = 0.14f;
            main.startSize = 0.36f;
            main.startColor = new Color(0.85f, 0.88f, 0.92f, 1f);
            SizeOverLife(tink, Curve((0f, 0.3f), (0.3f, 1.1f), (1f, 0f)));
            InFront(tink);
            var chips = Chips(absorb, 3, ParticleSystemScalingMode.Local);
            chips.transform.localPosition = new Vector3(0f, 0.1f, 0.15f);
            _kit.Sound(absorb, "bonk_tink", 0.6f, 1f);
            absorb.SetActive(false);
            glow.Absorb = absorb;
            glow.AbsorbSeconds = 1.1f;
            return Save(root);
        }

        /// <summary>The charge: a stomp, then dust kicked up behind his feet and speed lines streaming back.</summary>
        private static GameObject ChargeDust()
        {
            var root = _kit.NewRoot("BonkChargeDust", attackBound: true);
            var floor = Floor(root, 0.8f, -0.05f);
            var stomp = DustPuffs(floor, "Stomp", 4, 0.35f, 0.5f, 2.6f, ParticleSystemScalingMode.Local, life: 0.5f);
            FlatRing(stomp, 0.2f);
            var trail = Particles(floor, "Kicked dust", _m.Dust, 0.42f);
            var main = trail.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.48f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 48;
            var emission = trail.emission;
            emission.rateOverDistance = 4.5f;
            Sphere(trail, 0.2f);
            Drift(trail, 0.4f, 0.3f, 0.9f);
            Drag(trail, 0.3f);
            RandomFrame(trail, 2, 2);
            SizeOverLife(trail, Curve((0f, 0.5f), (0.3f, 1f), (1f, 1.2f)));
            Fade(trail, 0.45f);
            trail.GetComponent<ParticleSystemRenderer>().sortingFudge = 12f;

            var back = Frame(root, upright: true);
            var lines = Particles(back, "Speed lines", _m.Line, 0.4f);
            lines.transform.localPosition = new Vector3(0f, 1f, -1f);
            main = lines.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            main.startSpeed = 0f;
            main.maxParticles = 32;
            emission = lines.emission;
            emission.rateOverTime = 18f;
            var shape = lines.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1f, 1.1f, 0.2f);
            var velocity = lines.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(-9f, -6f);
            Stretch(lines, 0.06f, 1.6f);
            Fade(lines, 0.5f);
            _kit.Sound(root, "bonk_stomp", 0.6f, 1f);
            return Save(root);
        }

        // ── Jump Slam (E) ────────────────────────────────────────────────────────

        /// <summary>Takeoff: a ring of dust thrown along the floor (ground only), rocks and upward speed lines.</summary>
        private static GameObject SlamLaunch(string name, bool ground)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            GameObject feet;
            if (ground)
            {
                feet = Floor(root, 1.6f, -1f);
                var ring = DustPuffs(feet, "Dust ring", 9, 0.45f, 0.65f, 4.5f, local, life: 0.7f);
                FlatRing(ring, 0.45f);
                Rocks(feet, 3, 3.5f, 6f, feet.transform);
            }
            else
            {
                feet = Child(root, "Feet");
                feet.transform.localPosition = new Vector3(0f, -0.9f, 0f);
                var ring = Puffs(feet, "Puff ring", 7, 0.3f, 0.45f, 3f, local, life: 0.55f);
                FlatRing(ring, 0.3f);
            }
            var lines = Particles(feet, "Up lines", _m.Line, 0.3f);
            Burst(lines, 6);
            var main = lines.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.24f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 13f);
            var shape = lines.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.5f;
            shape.radiusThickness = 0.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            shape.alignToDirection = false;
            Stretch(lines, 0.05f, 2.2f);
            Fade(lines, 0.5f);
            _kit.Sound(root, "bonk_hup", ground ? 0.7f : 0.55f, ground ? 1f : 1.1f);
            return Save(root);
        }

        /// <summary>
        /// Landing, wherever he lands: a ring of dust, rubble that bounces once and floor cracks
        /// that fade over 2.5 s (the landing event's lifetime). A hit adds the BONK! (slam.hit).
        /// </summary>
        private static GameObject SlamLanding()
        {
            var root = _kit.NewRoot("BonkSlamLanding", attackBound: false);
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            var floor = Floor(root, 2f, -1f);
            var cracks = Particles(floor, "Cracks", _m.Cracks, 2.4f);
            Burst(cracks, 1);
            var main = cracks.main;
            main.startLifetime = 2.35f;
            main.startSize = 3.8f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var renderer = cracks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.maxParticleSize = 10f;
            renderer.sortingFudge = 40f;
            SizeOverLife(cracks, Curve((0f, 0.55f), (0.04f, 1f), (1f, 1f)));
            Fade(cracks, 0.62f);

            var ring = DustPuffs(floor, "Dust ring", 12, 0.5f, 0.75f, 5.5f, local, life: 0.8f);
            FlatRing(ring, 0.6f);
            var cloud = DustPuffs(floor, "Dust cloud", 6, 0.8f, 1.1f, 1.6f, local, life: 1.1f);
            FlatRing(cloud, 0.4f);
            Rocks(floor, 6, 4f, 7f, floor.transform);
            _kit.Sound(root, "bonk_thoom", 0.85f, 1f);
            return Save(root);
        }

        // ── Blade Storm (F) ──────────────────────────────────────────────────────

        /// <summary>
        /// The spin: three red smears whirling around him at chest height, in a slightly tilted
        /// circle. Each is the camera-facing ribbon trail of a particle orbiting his body, so it
        /// reads from any angle (flat discs collapse into lines at the gameplay camera's height).
        /// </summary>
        private static GameObject Whirl(string name)
        {
            var root = _kit.NewRoot(name, attackBound: true);
            var orbit = Child(Frame(root, upright: true), "Orbit");
            orbit.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            orbit.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            var whirl = Particles(orbit, "Smears", _m.Ribbon, 0.8f, ParticleSystemSimulationSpace.Local);
            Burst(whirl, 3);
            var main = whirl.main;
            main.startLifetime = 0.78f;
            main.startSize = 0.42f;
            main.startSpeed = 0f;
            var shape = whirl.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = WhirlRadius;
            shape.radiusThickness = 0f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var velocity = whirl.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            // Radians per second about the orbit's up axis: about 2.5 turns a second, like the spin.
            // Every axis shares one curve mode, or Unity ignores the orbit.
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(-WhirlSpin, -WhirlSpin);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            var trails = whirl.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.16f);
            trails.minVertexDistance = 0.05f;
            trails.worldSpace = false;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = true;
            trails.dieWithParticles = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, Curve((0f, 1f), (1f, 0.15f)));
            var renderer = whirl.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.None;
            renderer.trailMaterial = _m.Ribbon;
            var color = whirl.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) },
                new[] { (0f, 0f), (0.06f, 1f), (0.85f, 0.9f), (1f, 0f) });
            _kit.Sound(root, "bonk_storm", 0.7f, 1f);
            return Save(root);
        }

        /// <summary>A dust tornado around his feet while he spins on the ground.</summary>
        private static GameObject StormDust()
        {
            var root = _kit.NewRoot("BonkBladeStormDust", attackBound: true);
            var floor = Floor(root, 0.8f, -0.05f);
            var dust = Particles(floor, "Tornado", _m.Dust, 0.75f, ParticleSystemSimulationSpace.Local);
            var main = dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 64;
            var emission = dust.emission;
            emission.rateOverTime = 26f;
            var shape = dust.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.95f;
            shape.radiusThickness = 0.2f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var velocity = dust.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(-5f, -4f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            RandomFrame(dust, 2, 2);
            SizeOverLife(dust, Curve((0f, 0.5f), (0.3f, 1f), (1f, 1.15f)));
            Fade(dust, 0.45f);
            dust.GetComponent<ParticleSystemRenderer>().sortingFudge = 12f;
            return Save(root);
        }

        // ── Shared layers ────────────────────────────────────────────────────────

        /// <summary>
        /// Saves with unique object names. When Unity overwrites a prefab it keeps an object's
        /// fileID by matching names, so a repeated name (Spin/Thunk and Finisher/Thunk) gets a new
        /// fileID on every rebuild and the prefab never rebuilds byte-identically.
        /// </summary>
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

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        /// <summary>The content child of a bone-attached cue; it cancels the bone's import scale.</summary>
        private static GameObject Frame(GameObject root, bool upright = false)
        {
            var frame = new GameObject(upright ? "Upright frame" : "Frame");
            frame.transform.SetParent(root.transform, false);
            var component = frame.AddComponent<PresentationFrame>();
            component.UnitScale = true;
            component.Upright = upright;
            return frame;
        }

        /// <summary>A child moved onto the stage floor on spawn (<see cref="SnapToFloor"/>), or hidden.</summary>
        private static GameObject Floor(GameObject root, float maxDrop, float fallbackY)
        {
            var floor = Child(root, "Floor");
            floor.transform.localPosition = new Vector3(0f, fallbackY, 0f);
            floor.AddComponent<SnapToFloor>().MaxDrop = maxDrop;
            return floor;
        }

        private static ParticleSystem Halo(GameObject root, float size, float lifetime, Color color, ParticleSystemScalingMode scaling)
        {
            var halo = Particles(root, "Halo", _m.Glow, 0.3f, scaling: scaling);
            Burst(halo, 1);
            var main = halo.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startColor = new Color(color.r, color.g, color.b, 0.5f);
            SizeOverLife(halo, Curve((0f, 0.4f), (0.2f, 1f), (1f, 0.2f)));
            Fade(halo, 0.2f);
            Behind(halo);
            return halo;
        }

        /// <summary>The blunt red "thunk" that pops and settles at a contact.</summary>
        private static ParticleSystem Thunk(GameObject root, float size, float lifetime, ParticleSystemScalingMode scaling)
        {
            var thunk = Particles(root, "Thunk", _m.Thunk, 0.5f, scaling: scaling);
            Burst(thunk, 1);
            var main = thunk.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(thunk, Curve((0f, 0.5f), (0.12f, 1.12f), (0.4f, 1f), (1f, 0.85f)));
            Fade(thunk, 0.7f);
            thunk.GetComponent<ParticleSystemRenderer>().sortingFudge = -10f;
            return thunk;
        }

        /// <summary>The ringing arcs "((( )))" that spread around a contact or his bucket.</summary>
        private static ParticleSystem Rings(GameObject root, float size, float lifetime, float delay, ParticleSystemScalingMode scaling,
            ParticleSystemSimulationSpace space = ParticleSystemSimulationSpace.World, string name = "Rings")
        {
            var rings = Particles(root, name, _m.Rings, delay + lifetime + 0.05f, space, scaling);
            Burst(rings, 1);
            var main = rings.main;
            main.startDelay = delay;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            SizeOverLife(rings, Curve((0f, 0.7f), (0.3f, 1f), (1f, 1.25f)));
            Fade(rings, 0.5f);
            rings.GetComponent<ParticleSystemRenderer>().sortingFudge = -11f;
            return rings;
        }

        private static ParticleSystem Chips(GameObject root, int count, ParticleSystemScalingMode scaling)
        {
            var chips = Particles(root, "Steel chips", _m.Chip, 0.6f, scaling: scaling);
            Burst(chips, count);
            var main = chips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1.2f;
            Sphere(chips, 0.1f);
            Spin(chips, 600f);
            Fade(chips, 0.6f);
            chips.GetComponent<ParticleSystemRenderer>().sortingFudge = -6f;
            return chips;
        }

        /// <summary>Tan dust puffs thrown outward that slow down, swell and clear.</summary>
        private static ParticleSystem DustPuffs(GameObject root, string name, int count, float minSize, float maxSize, float speed,
            ParticleSystemScalingMode scaling, float life = 0.6f)
            => Puffs(root, name, count, minSize, maxSize, speed, scaling, life, _m.Dust);

        private static ParticleSystem Puffs(GameObject root, string name, int count, float minSize, float maxSize, float speed,
            ParticleSystemScalingMode scaling, float life = 0.6f, Material material = null)
        {
            var puffs = Particles(root, name, material ?? _m.Puff, life + 0.4f, scaling: scaling);
            Burst(puffs, count);
            var main = puffs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.05f;
            Sphere(puffs, 0.15f);
            Drag(puffs, 0.22f);
            RandomFrame(puffs, 2, 2);
            SizeOverLife(puffs, Curve((0f, 0.4f), (0.2f, 1f), (1f, 1.15f)));
            Fade(puffs, 0.45f);
            puffs.GetComponent<ParticleSystemRenderer>().sortingFudge = 12f;
            return puffs;
        }

        /// <summary>Emit along a flat horizontal circle (puffs hugging the floor) instead of a sphere.</summary>
        private static void FlatRing(ParticleSystem system, float radius)
        {
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            shape.rotation = new Vector3(90f, 0f, 0f);
        }

        /// <summary>Rubble thrown up from the floor; it bounces once on the floor plane and settles.</summary>
        private static void Rocks(GameObject root, int count, float minSpeed, float maxSpeed, Transform floor)
        {
            var rocks = Particles(root, "Rubble", _m.Rock, 1.8f);
            Burst(rocks, count);
            var main = rocks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 1.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1.7f;
            var shape = rocks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = 0.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Spin(rocks, 420f);
            RandomFrame(rocks, 2, 2);
            Fade(rocks, 0.75f);
            rocks.GetComponent<ParticleSystemRenderer>().sortingFudge = -4f;
            var collision = rocks.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.Planes;
            collision.SetPlane(0, floor);
            collision.bounce = 0.3f;
            collision.dampen = 0.5f;
            collision.radiusScale = 0.4f;
        }

        /// <summary>His eyes lit in the bucket's slit: two short red-hot dashes with a soft glow.</summary>
        private static void Eyes(GameObject frame, float lifetime, bool loop)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                var eye = Particles(frame, side < 0f ? "Eye left" : "Eye right", _m.Eye, lifetime, ParticleSystemSimulationSpace.Local);
                eye.transform.localPosition = Slit + new Vector3(0.065f * side, 0f, 0.03f);
                Burst(eye, 1);
                var main = eye.main;
                main.loop = loop;
                main.startLifetime = lifetime;
                main.startSize3D = true;
                main.startSizeX = 0.11f;
                main.startSizeY = 0.06f;
                main.startSizeZ = 1f;
                if (!loop)
                {
                    SizeOverLife(eye, Curve((0f, 0.3f), (0.15f, 1.2f), (0.3f, 1f), (1f, 0.8f)));
                    Fade(eye, 0.6f);
                }
                InFront(eye);
                var glow = Particles(eye.gameObject, "Glow", _m.Glow, lifetime, ParticleSystemSimulationSpace.Local);
                Burst(glow, 1);
                main = glow.main;
                main.loop = loop;
                main.startLifetime = lifetime;
                main.startSize = 0.34f;
                main.startColor = new Color(RedLight.r, RedLight.g, RedLight.b, 0.75f);
                if (!loop) Fade(glow, 0.5f);
                Behind(glow);
            }
        }

        /// <summary>
        /// The comic BONK!: a jagged red balloon pops, then the cream letters bounce in on top of it
        /// and rise a little. <paramref name="width"/> is the letters' width in metres at contact scale 1.
        /// </summary>
        private static void Lettering(GameObject root, float width, float lift, float delay, ParticleSystemScalingMode scaling)
        {
            var holder = Child(root, "BONK!");
            holder.transform.localPosition = new Vector3(0f, lift, 0f);
            var balloon = Particles(holder, "Balloon", _m.Balloon, 1f, ParticleSystemSimulationSpace.Local, scaling);
            Burst(balloon, 1);
            var main = balloon.main;
            main.startDelay = delay;
            main.startLifetime = 0.56f;
            main.startSize3D = true;
            main.startSizeX = width * 1.16f;
            main.startSizeY = width * 0.64f;
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0.12f);
            SizeOverLife(balloon, Curve((0f, 0.3f), (0.1f, 1.12f), (0.22f, 1f), (1f, 1.04f)));
            Fade(balloon, 0.78f);
            balloon.GetComponent<ParticleSystemRenderer>().sortingFudge = -14f;

            var letters = Particles(holder, "Letters", _m.Letters, 1f, ParticleSystemSimulationSpace.Local, scaling);
            Burst(letters, 1);
            main = letters.main;
            main.startDelay = delay + 0.02f;
            main.startLifetime = 0.56f;
            main.startSize3D = true;
            main.startSizeX = width;
            main.startSizeY = width * LettersHeight / LettersWidth;
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0.14f);
            var velocity = letters.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.3f, 0.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            SizeOverLife(letters, Curve((0f, 0.2f), (0.12f, 1.18f), (0.24f, 0.96f), (0.34f, 1f), (1f, 1.03f)));
            Fade(letters, 0.8f);
            letters.GetComponent<ParticleSystemRenderer>().sortingFudge = -18f;
            // A positive pivot Z moves a billboard toward the camera (in units of its Z size, 1 m
            // here), so the lettering draws in front of both fighters wherever the contact is.
            foreach (var system in new[] { balloon, letters })
            {
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                renderer.maxParticleSize = 3f;
                renderer.pivot = new Vector3(0f, 0f, LetteringForward);
            }
        }

        // ── Assets ───────────────────────────────────────────────────────────────

        private const int LettersWidth = 512, LettersHeight = 192;

        private static Materials BuildMaterials()
        {
            var letters = new MaskField(LettersMaskPath);
            var question = new MaskField(QuestionMaskPath);
            return new Materials
            {
                Glow = _kit.ParticleMaterial("BonkGlowAdd", _kit.BakeTexture("BonkGlow", 128, 128,
                    (u, v) => Alpha(Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f))), new Color(1.3f, 1.3f, 1.3f), additive: true),
                Thunk = Mat("BonkThunk", 256, 256, (u, v) => ThunkPixel(P(u, v))),
                Rings = Mat("BonkRings", 256, 256, (u, v) => RingsPixel(P(u, v))),
                Letters = Mat("BonkLetters", LettersWidth, LettersHeight, (u, v) => LettersPixel(u, v, letters)),
                Balloon = Mat("BonkBalloon", 256, 256, (u, v) => BalloonPixel(P(u, v))),
                Question = Mat("BonkQuestion", 128, 128, (u, v) => QuestionPixel(u, v, question)),
                Dust = Mat("BonkDust", 512, 512, (u, v) => Sheet(u, v, (p, cell) => PuffPixel(p, cell, DustRim, Dust, DustLight, 128f))),
                Puff = Mat("BonkPuff", 256, 256, (u, v) => Sheet(u, v, (p, cell) => PuffPixel(p, cell + 10, SteamRim, Steam, Color.white, 64f))),
                Rock = Mat("BonkRock", 256, 256, (u, v) => Sheet(u, v, RockPixel)),
                Chip = Mat("BonkChip", 64, 64, (u, v) => ChipPixel(P(u, v))),
                Cracks = Mat("BonkCracks", 512, 512, (u, v) => CracksPixel(P(u, v))),
                Eye = Mat("BonkEye", 64, 32, EyePixel),
                Line = Mat("BonkLine", 128, 32, LinePixel),
                Glint = Mat("BonkGlint", 128, 128, (u, v) => GlintPixel(P(u, v))),
                Ribbon = Mat("BonkRibbon", 128, 64, RibbonPixel),
                SmearBand = Mat("BonkSmearBand", 64, 128, SmearBandPixel),
            };
        }

        private static Material Mat(string name, int width, int height, System.Func<float, float, Color> pixel)
            => _kit.ParticleMaterial(name, _kit.BakeTexture(name, width, height, pixel));

        private static Vector2 P(float u, float v) => new(u * 2f - 1f, v * 2f - 1f);

        /// <summary>A 2x2 sheet: each cell gets its own −1..1 coordinates and its index (0 top-left).</summary>
        private static Color Sheet(float u, float v, System.Func<Vector2, int, Color> cell)
        {
            int cx = u < 0.5f ? 0 : 1, cy = v < 0.5f ? 1 : 0;
            var p = new Vector2((u % 0.5f) * 4f - 1f, (v % 0.5f) * 4f - 1f);
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

        private static Color Flat(float distance, Color color)
        {
            color.a = Mathf.Clamp01(0.5f - distance);
            return color;
        }

        private static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float ra, float rb)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - Mathf.Lerp(ra, rb, h);
        }

        private static float Polyline(Vector2 p, Vector2[] points, float ra, float rb)
        {
            float d = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                float t0 = i / (float)(points.Length - 1), t1 = (i + 1) / (float)(points.Length - 1);
                d = Mathf.Min(d, Capsule(p, points[i], points[i + 1], Mathf.Lerp(ra, rb, t0), Mathf.Lerp(ra, rb, t1)));
            }
            return d;
        }

        private static readonly Dictionary<int, Vector3[]> Clouds = new();

        /// <summary>A cauliflower puff: a body plus seeded bumps (flatten below 1 squashes the bottom).</summary>
        private static float Cloud(Vector2 p, Vector2 centre, float radius, int bumps, int seed, float flatten)
        {
            int key = seed * 64 + bumps;
            if (!Clouds.TryGetValue(key, out var circles))
            {
                var random = new System.Random(seed);
                circles = new Vector3[bumps + 1];
                circles[0] = new Vector3(0f, 0f, 0.62f);
                for (int i = 0; i < bumps; i++)
                {
                    float a = (i + (float)random.NextDouble() * 0.5f - 0.25f) / bumps * Mathf.PI * 2f;
                    float r = Mathf.Lerp(0.3f, 0.44f, (float)random.NextDouble());
                    float dist = Mathf.Lerp(0.5f, 0.62f, (float)random.NextDouble());
                    circles[i + 1] = new Vector3(Mathf.Cos(a) * dist, Mathf.Sin(a) * dist, r);
                }
                Clouds[key] = circles;
            }
            float d = float.MaxValue;
            foreach (var c in circles)
            {
                float y = c.y < 0f ? c.y * flatten : c.y;
                d = Mathf.Min(d, (p - centre - new Vector2(c.x, y) * radius).magnitude - c.z * radius);
            }
            return d;
        }

        private static readonly Dictionary<int, Vector2[]> Stars = new();

        /// <summary>A star with rounded tips and valleys: <paramref name="round"/> is the corner radius.</summary>
        private static float BluntStar(Vector2 p, int spikes, float inner, int seed, float round)
        {
            int key = seed * 100 + spikes;
            if (!Stars.TryGetValue(key, out var outline))
            {
                outline = StarOutline(spikes, inner, seed);
                for (int i = 0; i < outline.Length; i++) outline[i] *= 1f - round;
                Stars[key] = outline;
            }
            return Polygon(p, outline) - round;
        }

        /// <summary>The thunk: a blunt nine-point star, rust rim, bucket red and a cream-white core.</summary>
        private static Color ThunkPixel(Vector2 p)
        {
            Color color = ToonShapes.Bands(BluntStar(p, 9, 0.6f, 23, 0.08f) * 128f, 4.5f, 34f, Rim, Red, Cream);
            return Over(color, Flat(Circle(p, Vector2.zero, 0.17f) * 128f, Color.white));
        }

        /// <summary>Three ringing arcs on each side, "((( )))", in cream with a rust rim.</summary>
        private static Color RingsPixel(Vector2 p)
        {
            float d = float.MaxValue;
            float[] radii = { 0.55f, 0.72f, 0.89f }, spans = { 30f, 26f, 22f }, widths = { 0.035f, 0.03f, 0.026f };
            float angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            foreach (float side in new[] { 0f, 180f })
            {
                for (int k = 0; k < 3; k++)
                {
                    float delta = Mathf.Abs(Mathf.DeltaAngle(angle, side));
                    float arc;
                    if (delta <= spans[k]) arc = Mathf.Abs(p.magnitude - radii[k]) - widths[k];
                    else
                    {
                        float end = (side + Mathf.Sign(Mathf.DeltaAngle(side, angle)) * spans[k]) * Mathf.Deg2Rad;
                        arc = (p - new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * radii[k]).magnitude - widths[k];
                    }
                    d = Mathf.Min(d, arc);
                }
            }
            return ToonShapes.Bands(d * 128f, 3f, 99f, Rim, Cream, Cream);
        }

        /// <summary>The jagged comic balloon behind the BONK!: bucket red with a rust rim.</summary>
        private static Color BalloonPixel(Vector2 p)
        {
            float d = BluntStar(p, 16, 0.76f, 37, 0.02f);
            Color color = ToonShapes.Bands(d * 128f, 4f, 99f, Rim, Red, Red);
            return Over(color, Flat((d + 0.11f) * 128f, new Color(RedLight.r, RedLight.g, RedLight.b, 0.35f)));
        }

        // The BONK! mask spans its centre band; the letters' extrusion runs down and to the right.
        private const float LettersU0 = 0.04f, LettersU1 = 0.96f, LettersV0 = 0.33f, LettersV1 = 0.6675f;

        private static Color LettersPixel(float u, float v, MaskField mask)
        {
            float texels = LettersWidth / ((LettersU1 - LettersU0) * mask.Width);
            return ComicGlyph(mask, Mathf.Lerp(LettersU0, LettersU1, u), Mathf.Lerp(LettersV0, LettersV1, v), texels,
                new Vector2(0.0135f, -0.021f), 4.5f, 2.2f);
        }

        private static Color QuestionPixel(float u, float v, MaskField mask)
            => ComicGlyph(mask, u, v, 128f / mask.Width, new Vector2(0.035f, -0.055f), 3f, 1.6f);

        /// <summary>
        /// Comic lettering from a glyph mask: cream faces with a rust line, a bucket-red extrusion
        /// that darkens with depth, and a rust outline around both. Widths are in texels.
        /// </summary>
        private static Color ComicGlyph(MaskField mask, float mu, float mv, float texelsPerPixel, Vector2 extrude,
            float outline, float faceLine)
        {
            const int steps = 12;
            float face = mask.Sample(mu, mv) * texelsPerPixel;
            float extrusion = float.MaxValue, depth = 1f;
            for (int k = 1; k <= steps; k++)
            {
                float t = k / (float)steps;
                float d = mask.Sample(mu - extrude.x * t, mv - extrude.y * t) * texelsPerPixel;
                if (d < 0f && depth >= 1f) depth = t;
                extrusion = Mathf.Min(extrusion, d);
            }
            Color color = Flat(Mathf.Min(face, extrusion) - outline, Rim);
            color = Over(color, Flat(extrusion, Color.Lerp(Red, RedDark, depth)));
            color = Over(color, Flat(face - faceLine, Rim));
            return Over(color, Flat(face, Cream));
        }

        /// <summary>A puff with a lighter top-left (dust in tan, the snort in steam white).</summary>
        private static Color PuffPixel(Vector2 p, int cell, Color rim, Color body, Color light, float s)
        {
            int seed = 51 + cell * 5;
            float puff = Cloud(p, Vector2.zero, 0.84f, 7, seed, 0.8f);
            Color color = ToonShapes.Bands(puff * s, s / 36f, 99f, rim, body, body);
            float lit = Mathf.Max(Cloud(p, new Vector2(-0.14f, 0.17f), 0.6f, 6, seed + 3, 0.8f), puff + 0.07f);
            return Over(color, Flat(lit * s, light));
        }

        private static readonly Dictionary<int, Vector2[]> RockOutlines = new();

        /// <summary>An angular chunk of rubble with a lit facet.</summary>
        private static Color RockPixel(Vector2 p, int cell)
        {
            if (!RockOutlines.TryGetValue(cell, out var outline))
            {
                var random = new System.Random(71 + cell);
                int n = 5 + cell % 2;
                outline = new Vector2[n];
                for (int k = 0; k < n; k++)
                {
                    float a = (k + (float)random.NextDouble() * 0.5f - 0.25f) / n * Mathf.PI * 2f;
                    float r = Mathf.Lerp(0.55f, 0.85f, (float)random.NextDouble());
                    outline[k] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.85f);
                }
                RockOutlines[cell] = outline;
            }
            const float s = 64f;
            float rock = Polygon(p, outline);
            Color color = ToonShapes.Bands(rock * s, 2.5f, 99f, DustRim, Stone, Stone);
            float facet = Mathf.Max(rock + 0.12f, -(p.y - p.x * 0.6f - 0.05f));
            return Over(color, Flat(facet * s, DustLight));
        }

        private static Color ChipPixel(Vector2 p)
        {
            var outline = new[] { new Vector2(0.9f, 0.05f), new Vector2(-0.55f, 0.55f), new Vector2(-0.35f, -0.6f) };
            return ToonShapes.Bands(Polygon(p, outline) * 32f, 1.5f, 3f, SteelRim, Steel, SteelLight);
        }

        /// <summary>
        /// Floor cracks around a small dent, drawn flat on the floor: jagged dark lines with a pale
        /// lip beside each, so they read on dark floors too. Fades out toward the edge.
        /// </summary>
        private static Color CracksPixel(Vector2 p)
        {
            const float s = 256f;
            Color color = Rim;
            color.a = (1f - Step(-0.05f, 0.08f, p.magnitude - 0.16f)) * 0.6f;
            var random = new System.Random(19);
            float crack = float.MaxValue, lip = float.MaxValue;
            const int count = 10;
            for (int k = 0; k < count; k++)
            {
                float a = (k + (float)random.NextDouble() * 0.5f - 0.25f) / count * Mathf.PI * 2f;
                float length = Mathf.Lerp(0.55f, 0.93f, (float)random.NextDouble());
                var points = new Vector2[6];
                for (int i = 0; i < 6; i++)
                {
                    float t = Mathf.Lerp(0.08f, length, i / 5f);
                    float jitter = i is > 0 and < 5 ? ((float)random.NextDouble() - 0.5f) * 0.22f : 0f;
                    points[i] = new Vector2(Mathf.Cos(a + jitter), Mathf.Sin(a + jitter)) * t;
                }
                crack = Mathf.Min(crack, Polyline(p, points, 0.06f, 0.018f));
                var shifted = new Vector2[points.Length];
                for (int i = 0; i < points.Length; i++) shifted[i] = points[i] + new Vector2(0.03f, -0.035f);
                lip = Mathf.Min(lip, Polyline(p, shifted, 0.045f, 0.012f));
                if (random.NextDouble() < 0.6)
                {
                    int j = 2 + random.Next(2);
                    float b = a + (random.NextDouble() < 0.5 ? -1f : 1f) * Mathf.Lerp(0.35f, 0.6f, (float)random.NextDouble());
                    var branch = new[] { points[j], points[j] + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * Mathf.Lerp(0.12f, 0.22f, (float)random.NextDouble()) };
                    crack = Mathf.Min(crack, Polyline(p, branch, 0.04f, 0.012f));
                }
            }
            color = Over(color, Flat(lip * s, new Color(DustLight.r, DustLight.g, DustLight.b, 0.85f)));
            color = Over(color, Flat(crack * s, Rim));
            color.a *= 1f - Step(0.78f, 1f, p.magnitude);
            return color;
        }

        private static Color EyePixel(float u, float v)
        {
            var p = new Vector2(u * 2f - 1f, (v * 2f - 1f) * 0.5f);
            float d = Capsule(p, new Vector2(-0.6f, 0f), new Vector2(0.6f, 0f), 0.32f, 0.32f);
            return ToonShapes.Bands(d * 32f, 1.2f, 3f, RedDark, Hot, Cream);
        }

        /// <summary>A speed line: a cream lens with a rust rim, long along U for stretched particles.</summary>
        private static Color LinePixel(float u, float v)
        {
            var p = new Vector2(u * 2f - 1f, (v * 2f - 1f) * 0.25f);
            return ToonShapes.Bands(Lens(p, 0.95f, 0.2f) * 64f, 1.5f, 99f, Rim, Cream, Cream);
        }

        private static Color GlintPixel(Vector2 p)
        {
            var q = Rotate(p, 45f);
            float d = Mathf.Min(Mathf.Min(Rhombus(p, new Vector2(0.95f, 0.16f)), Rhombus(p, new Vector2(0.16f, 0.95f))),
                Mathf.Min(Rhombus(q, new Vector2(0.48f, 0.1f)), Rhombus(q, new Vector2(0.1f, 0.48f))));
            Color color = ToonShapes.Bands(d * 64f, 2f, 3.5f, Rim, Red, Cream);
            return Over(color, Flat(Circle(p, Vector2.zero, 0.13f) * 64f, Color.white));
        }

        /// <summary>
        /// A ribbon band across V for particle trails (FightGuy's ribbon shape): rust rims, bucket
        /// red and a cream core line, fading from the head at U = 0 toward the tail.
        /// </summary>
        private static Color RibbonPixel(float u, float v)
        {
            float across = Mathf.Abs(v * 2f - 1f);
            Color color = ToonShapes.Bands((across - 0.9f) * 32f, 2.5f, 7f, Rim, Red, Cream);
            color.a *= Mathf.Min(Mathf.Clamp01(u / 0.06f), Mathf.Clamp01((1f - u) / 0.5f));
            return color;
        }

        /// <summary>
        /// The plank smear for SwordSweepTrail, which maps U along the sweep and V 0–0.5 from the
        /// inner edge to the end of the plank (the top half is unused). Only the outer third of the
        /// plank leaves a band, so a big overhead swing never hides him: see-through bucket red
        /// with a solid cream streak and a rust rim at the end.
        /// </summary>
        private static Color SmearBandPixel(float u, float v)
        {
            if (v >= 0.5f) return new Color(Red.r, Red.g, Red.b, 0f);
            float across = v * 2f, texel = across * 64f;
            Color color = Red;
            float streak = 1f - Step(0f, 1.5f, Mathf.Abs(texel - 52f) - 2f);
            color = Color.Lerp(color, Cream, streak);
            float rim = Mathf.Clamp01(texel - 59f);
            color = Color.Lerp(color, Rim, rim);
            color.a = Mathf.Max(Mathf.Max(Step(0.62f, 0.76f, across) * 0.62f, streak * 0.95f), rim);
            color.a *= Mathf.Min(Mathf.Clamp01(u / 0.05f), Mathf.Clamp01((1f - u) / 0.05f));
            return color;
        }
    }
}
#endif
