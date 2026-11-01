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
    /// bucket rings after a headbutt, and a hit his armour shrugs off gets a little "?".
    /// Sir Bonk (2026-10-21) keeps all of that and looks homemade, like the pictures in his book:
    /// his plank throws wood splinters, Leap of Faith gets a storybook holy light while the plank is
    /// stuck and a POP! when it comes out, and Blade Storm's smears are windmill sails, with a tiny
    /// windmill over his bucket afterwards. The Joust (R) does its own sound effects: his hobby horse
    /// comes and goes in a puff of straw, he neighs, a pennant streams from his lance, coconut hooves go
    /// clip-clop, his heels gouge the deck in the skid, and off a ledge he looks down and gulps.
    /// Workflow and readability rules are in skills/sloparena-character-vfx-pass; plumbing is in
    /// <see cref="PresentationBuilderKit"/>.
    /// </summary>
    public static class BonkPresentationBuilder
    {
        private const string LettersMaskPath = "Assets/CharacterPackages/bonk/Presentation/Source/Bonk.png";
        private const string QuestionMaskPath = "Assets/CharacterPackages/bonk/Presentation/Source/Question.png";
        private const string PopMaskPath = "Assets/CharacterPackages/bonk/Presentation/Source/Pop.png";
        private const string NeighMaskPath = "Assets/CharacterPackages/bonk/Presentation/Source/Neigh.png";
        private const string ClipMaskPath = "Assets/CharacterPackages/bonk/Presentation/Source/Clip.png";
        private const string ClopMaskPath = "Assets/CharacterPackages/bonk/Presentation/Source/Clop.png";
        private const string GulpMaskPath = "Assets/CharacterPackages/bonk/Presentation/Source/Gulp.png";
        // The bucket's eye slit in the head cue frame (metres; its axes match the fighter's at
        // rest), measured from the helmet vertices: the face recesses there 0.05–0.08 m up.
        private static readonly Vector3 Slit = new(0f, 0.065f, 0.25f);
        private const float BucketTop = 0.29f;
        // Lifetime of each smear sample behind the plank (the weapon config's HitboxMotionTime).
        private const float SmearSeconds = 0.12f;
        // Blade Storm's whirl: orbit radius (about the plank's reach) and spin in radians per second.
        // Blade Storm's wind stripes on the plank tip: count, spacing (m), width (m) and trail length (s, about a
        // quarter turn of the tip at the spin's speed).
        private const int StripeCount = 3;
        private const float StripeGap = 0.14f, StripeWidth = 0.09f, StripeSeconds = 0.12f;
        // Leap of Faith's landing clip (bonk_sir_e1_land), in attack ticks after touchdown: the
        // holy light comes once he is up in the handstand, after the impact's BONK!, and is gone
        // when the haul starts; the plank leaves the floor at the pop. The plank's hole lies about
        // 1.3 m in front of where he lands.
        private const int HolyStartTick = 6, HolyTicks = 40, PopTick = 53;
        private const float PlankHole = 1.3f, BeamHeight = 7f, RaysHeight = 3.4f;
        // How far the BONK! sits in front of its contact, toward the camera (metres).
        private const float LetteringForward = 1f;
        // The Joust (slop.ability.steered-charge.v1). Both R stages list their clips as [mount, gallop, hit,
        // look-down, stop], so the gallop loop shows as ComboStage 1; it loops every 24 ticks and a hoof lands
        // on its ticks 1 and 13 (bonk_sir_r_gallop, measured on the rig: left heel down on 1, right on 13). The
        // skid lasts 18 ticks with both heels planted, the left one in front; the look-down gulps 6 ticks in.
        // The horse and pennant cues outlive the longest Joust (about 160 ticks).
        private const int GallopStage = 1, GallopLoopTicks = 24, LookDownGulpTick = 6;
        private static readonly int[] HoofTicks = { 1, 13 };
        private const float SkidSeconds = 0.42f, PennantLength = 0.62f, PennantWidth = 0.27f;
        // Q, the Pot-Lid Throw: the flying lid is his shoulder lid (the WeaponAttach prop, so it keeps its size), spinning
        // flat about its face about three turns a second.
        private const string PotLidPrefabPath = "Assets/Art/Characters/bonk/Prefabs/sir_bonk_pot_lid.prefab";
        private const float LidSpinDegreesPerSecond = 1080f;
        // Its hit word (the owner picked the wobbly lettering, with a tink for the sound) and its wake, which trails about a
        // metre behind it at 15 m/s.
        private const string DooongMaskPath = "Assets/CharacterPackages/bonk/Presentation/Source/Dooong.png";
        private const float LidWakeSeconds = 0.075f;

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
        // Sir Bonk: his plank's wood, and his blue paint (bucket blue, crown navy) for lettering.
        private static readonly Color WoodRim = new(0.227f, 0.141f, 0.075f);
        private static readonly Color Wood = new(0.788f, 0.553f, 0.329f);
        private static readonly Color WoodLight = new(0.953f, 0.784f, 0.561f);
        private static readonly Color PaintRim = new(0.055f, 0.122f, 0.188f);
        private static readonly Color Paint = new(0.184f, 0.471f, 0.682f);
        private static readonly Color PaintDark = new(0.122f, 0.271f, 0.4f);
        // The Joust: a darker wood for lettering extrusions, the horse's straw, the gouge in the deck and a sweat drop.
        private static readonly Color WoodDark = new(0.471f, 0.29f, 0.149f);
        private static readonly Color Straw = new(0.91f, 0.765f, 0.416f);
        private static readonly Color StrawLight = new(1f, 0.925f, 0.639f);
        private static readonly Color GougeDark = new(0.165f, 0.098f, 0.055f);
        private static readonly Color Sweat = new(0.62f, 0.82f, 0.96f);

        private static PresentationBuilderKit _kit;
        private static Materials _m;

        private sealed class Materials
        {
            public Material Glow, Thunk, Rings, Letters, Balloon, Question, Dust, Puff, Rock, Chip, Cracks, Eye, Line,
                Glint, Ribbon, SmearBand, Splinter, Gleam, Beam, Rays, Pop, Pinwheel,
                Neigh, Clip, Clop, Gulp, QuestionPaint, Straw, Disc, Sweat, Pennant, Dooong, SpinRings, Wake;
            // Width over height of the lettering textures (each is baked around its own ink).
            public float NeighAspect, ClipAspect, ClopAspect, GulpAspect, DooongAspect;
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
                ("presentation.bonk.storm.dizzy", StormDizzy()),
                ("presentation.bonk.joust.horse", JoustHorse()),
                ("presentation.bonk.joust.pennant", JoustPennant()),
                ("presentation.bonk.joust.neigh", JoustNeigh()),
                ("presentation.bonk.joust.gallop", JoustGallop()),
                ("presentation.bonk.joust.skid", JoustSkid()),
                ("presentation.bonk.joust.lookdown", JoustLookDown()),
                ("presentation.bonk.joust.challenge", JoustNeigh()),
                ("presentation.bonk.joust.scoop", JoustScoop()),
                ("presentation.bonk.joust.bonk", BigHit("BonkJoustBonk", thunk: 0.9f, letters: 1f, lift: 0.45f, sound: "bonk_bonk", volume: 0.8f, pitch: 1f)),
                ("presentation.bonk.faith.halo", FaithHalo()),
                ("presentation.bonk.faith.lock", Glint()),
                ("presentation.bonk.challenge.marker", Glint()),
                ("presentation.bonk.lid.throw", Swing("BonkLidThrow", "bonk_lid_fwip", 0.6f)),
                ("presentation.bonk.lid.hit", LidHit()),
                ("presentation.bonk.lid.catch", LidCatch()),
                ("presentation.bonk.lid.selfhit", LidSelfHit()),
            };
            var trail = SmearTrail();
            var lid = PotLidProjectile();
            kit.BindWeaponTrail(trail);
            kit.BindProjectile("ground.A", lid, ProjectileOrientation.PreservePrefab, "");
            kit.BindProjectile("air.A", lid, ProjectileOrientation.PreservePrefab, "");
            SetSmearLifetime();
            return kit.Finish(built, new { trail = AssetDatabase.GetAssetPath(trail), lid = AssetDatabase.GetAssetPath(lid) });
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
            var chips = Chips(absorb, 3, ParticleSystemScalingMode.Local, wood: false);
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
        /// Sir Bonk's leap of faith is answered while the plank is stuck (<see cref="HolyLight"/>),
        /// and the plank comes out with a POP! (<see cref="PlankPop"/>). Both run on his attack
        /// clock and clear if he is knocked out of the landing.
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
            HolyLight(root);
            PlankPop(root);
            return Save(root);
        }

        /// <summary>
        /// The sword in the stone: a shaft of light from the sky onto him and the stuck plank, rays
        /// fanning from its foot and a few gleams, all flat storybook cream (alpha-blended, no glow),
        /// with a choir "aah". Drawn a metre behind him so he stays in front of the light.
        /// </summary>
        private static void HolyLight(GameObject root)
        {
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            var holy = Child(root, "Holy");
            var light = Child(holy, "Light");
            light.transform.localPosition = new Vector3(0f, 0f, PlankHole * 0.75f);
            var floor = Floor(light, 2f, -1f);

            var beam = Particles(floor, "Beam", _m.Beam, 0.7f, ParticleSystemSimulationSpace.Local, local);
            beam.transform.localPosition = new Vector3(0f, BeamHeight * 0.5f, 0f);
            Burst(beam, 1);
            var main = beam.main;
            main.startLifetime = (HolyTicks - HolyStartTick) / 60f;
            main.startSize3D = true;
            main.startSizeX = 1.6f;
            main.startSizeY = BeamHeight;
            main.startSizeZ = 1f;
            SizeOverLife(beam, Curve((0f, 0.2f), (0.12f, 1f), (1f, 1f)), Curve((0f, 1f), (1f, 1f)));
            Fade(beam, 0.5f);
            Backdrop(beam);

            var rays = Particles(floor, "Rays", _m.Rays, 0.9f, ParticleSystemSimulationSpace.Local, local);
            rays.transform.localPosition = new Vector3(0f, RaysHeight * 0.5f, 0f);
            Burst(rays, 1);
            main = rays.main;
            main.startLifetime = (HolyTicks - HolyStartTick) / 60f;
            main.startSize3D = true;
            main.startSizeX = RaysHeight * 2f;
            main.startSizeY = RaysHeight;
            main.startSizeZ = 1f;
            SizeOverLife(rays, Curve((0f, 0.45f), (0.14f, 1f), (1f, 1.04f)));
            Fade(rays, 0.66f);
            Backdrop(rays);

            var gleams = Particles(floor, "Gleams", _m.Gleam, 0.6f, scaling: local);
            gleams.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            main = gleams.main;
            main.duration = 0.5f;
            main.startLifetime = 0.26f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var emission = gleams.emission;
            emission.rateOverTime = 10f;
            Sphere(gleams, 0.9f);
            SizeOverLife(gleams, Curve((0f, 0f), (0.3f, 1.15f), (1f, 0f)));
            InFront(gleams);
            _kit.Sound(light, "bonk_choir", 0.5f, 1f);

            var window = holy.AddComponent<AttackTickWindow>();
            window.StartTick = HolyStartTick;
            window.CancelableUntilTick = HolyTicks;
            window.Content = light;
            light.SetActive(false);
        }

        /// <summary>The plank comes out of the floor: a cork POP! in his paint, a puff and splinters.</summary>
        private static void PlankPop(GameObject root)
        {
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            var pop = Child(root, "Pop");
            var hole = Child(pop, "Hole");
            hole.transform.localPosition = new Vector3(0f, 0f, PlankHole);
            var floor = Floor(hole, 2f, -1f);
            var puff = DustPuffs(floor, "Puff", 5, 0.22f, 0.36f, 2.2f, local, life: 0.5f);
            FlatRing(puff, 0.12f);
            Chips(floor, 4, local);

            var word = Particles(floor, "POP!", _m.Pop, 0.9f, ParticleSystemSimulationSpace.Local, local);
            word.transform.localPosition = new Vector3(0f, 1f, 0f);
            Burst(word, 1);
            var main = word.main;
            main.startLifetime = 0.6f;
            main.startSize3D = true;
            main.startSizeX = 0.85f;
            main.startSizeY = 0.85f * PopHeight / PopWidth;
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0.12f);
            var velocity = word.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.4f, 0.4f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            SizeOverLife(word, Curve((0f, 0.2f), (0.14f, 1.2f), (0.26f, 0.95f), (0.36f, 1f), (1f, 1.02f)));
            Fade(word, 0.75f);
            var renderer = word.GetComponent<ParticleSystemRenderer>();
            renderer.sortingFudge = -18f;
            renderer.pivot = new Vector3(0f, 0f, LetteringForward);
            _kit.Sound(hole, "bonk_pop", 0.75f, 1f);

            var window = pop.AddComponent<AttackTickWindow>();
            window.StartTick = PopTick;
            window.CancelableUntilTick = PopTick;
            window.Content = hole;
            hole.SetActive(false);
        }

        /// <summary>
        /// A tall camera-facing backdrop that stays upright and draws a metre behind the fighter, so
        /// the fighter reads in front of it (a billboard through his centre would cover his far half).
        /// </summary>
        private static void Backdrop(ParticleSystem system)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            renderer.maxParticleSize = 20f;
            renderer.pivot = new Vector3(0f, 0f, -1f);
            renderer.sortingFudge = 30f;
        }

        // ── Blade Storm (F) ──────────────────────────────────────────────────────

        /// <summary>
        /// Blade Storm's wind stripes (owner's pick B, 2026-10-22, replacing the windmill sails that hid him): three thin
        /// cream streaks riding the plank's tip, stacked a little apart, each a short world-space trail that draws the
        /// plank's own arc and fades within about a quarter turn. The cue rides <c>_weapon_tip</c> for the spin's hit
        /// windows. The whum stays; the owner dropped the wooden creak ("croaking wood", 2026-10-22).
        /// </summary>
        private static GameObject Whirl(string name)
        {
            var root = _kit.NewRoot(name, attackBound: true);
            var frame = Frame(root);
            for (int k = 0; k < StripeCount; k++)
            {
                var stripe = Child(frame, $"Stripe {k + 1}");
                stripe.transform.localPosition = new Vector3(0f, (k - (StripeCount - 1) * 0.5f) * StripeGap, 0f);
                var trail = stripe.AddComponent<TrailRenderer>();
                trail.time = StripeSeconds * (1f - 0.15f * k);
                trail.minVertexDistance = 0.05f;
                trail.widthMultiplier = StripeWidth * (1f - 0.2f * k);
                trail.widthCurve = Curve((0f, 1f), (1f, 0f));
                trail.colorGradient = GradientOf(new[] { (0f, Color.white), (1f, Color.white) },
                    new[] { (0f, 0.95f), (0.5f, 0.7f), (1f, 0f) }).gradient;
                trail.textureMode = LineTextureMode.Stretch;
                trail.alignment = LineAlignment.View;
                trail.numCapVertices = 2;
                trail.sharedMaterial = _m.Wake;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
            }
            _kit.Sound(root, "bonk_storm", 0.7f, 1f);
            return Save(root);
        }

        /// <summary>
        /// After the storm, until he can act: a tiny windmill spinning over his bucket and slowing
        /// down, his version of dizzy stars, and so the opening to punish.
        /// </summary>
        private static GameObject StormDizzy()
        {
            var root = _kit.NewRoot("BonkStormDizzy", attackBound: true);
            var frame = Frame(root, upright: true);
            var mill = Particles(frame, "Windmill", _m.Pinwheel, 0.75f, ParticleSystemSimulationSpace.Local);
            mill.transform.localPosition = new Vector3(0f, BucketTop + 0.32f, 0f);
            Burst(mill, 1);
            var main = mill.main;
            main.startLifetime = 0.7f;
            main.startSize = 0.5f;
            var spin = mill.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(Mathf.Deg2Rad * 900f, Curve((0f, 1f), (1f, 0.25f)));
            SizeOverLife(mill, Curve((0f, 0.3f), (0.12f, 1.1f), (0.22f, 1f), (0.85f, 1f), (1f, 0.4f)));
            Fade(mill, 0.85f);
            InFront(mill);
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
            // A low ring kicked up round his feet (owner's pick B): wide and flat, so it never hides him.
            shape.radius = 1.15f;
            shape.radiusThickness = 0.15f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var velocity = dust.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
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

        // ── The Joust (R) ────────────────────────────────────────────────────────

        /// <summary>
        /// His hobby horse, on his hips from the press: it comes in a puff of straw and leaves in another
        /// when the move ends, however it ends (<see cref="AttackEndCue"/>), so it never just pops in or
        /// out. Not attack-bound, so the cue outlives the move for the second puff.
        /// </summary>
        private static GameObject JoustHorse()
        {
            var root = _kit.NewRoot("BonkJoustHorse", attackBound: false);
            var frame = Frame(root, upright: true);
            // The horse's stick runs from the straw tail behind him up to the head in front of his belly
            // (0.94 m ahead of the hips): the puff sits over the stick and the neck.
            var horse = new Vector3(0f, 0.18f, 0.45f);
            StrawPoof(frame, "Poof in", horse, 1f, sound: true);
            var away = StrawPoof(frame, "Poof out", horse, 1.1f, sound: true);
            away.SetActive(false);
            var end = root.AddComponent<AttackEndCue>();
            end.Content = away;
            end.ContentSeconds = 1.2f;
            return Save(root);
        }

        /// <summary>
        /// The knight's pennant tied under his lance tip: cream cloth with his blue crown that hangs while
        /// he rears, streams back as he gallops and swings over when he stops dead
        /// (<see cref="PennantPresentation"/>). It comes and goes with the horse, in a smaller puff.
        /// </summary>
        private static GameObject JoustPennant()
        {
            var root = _kit.NewRoot("BonkJoustPennant", attackBound: false);
            var pennant = Child(root, "Pennant");
            pennant.AddComponent<MeshFilter>();
            var renderer = pennant.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _m.Pennant;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var cloth = pennant.AddComponent<PennantPresentation>();
            cloth.Length = PennantLength;
            cloth.Width = PennantWidth;
            // Heavy enough to hang soon after a swing, with quadratic drag so it streams at a gallop and swings over
            // the tip when he stops dead (simulated offline: a 12 m/s gallop, a dead stop, then the lance raised).
            cloth.Gravity = 18f;
            cloth.AirDrag = 3.5f;
            cloth.Damping = 2f;
            var frame = Frame(root);
            StrawPoof(frame, "Puff in", Vector3.zero, 0.45f, sound: false);
            var away = StrawPoof(frame, "Puff out", Vector3.zero, 0.45f, sound: false);
            away.SetActive(false);
            var end = root.AddComponent<AttackEndCue>();
            end.Content = away;
            end.ContentSeconds = 0.9f;
            end.HideOnEnd = new[] { pennant };
            return Save(root);
        }

        /// <summary>
        /// The rear, his warning: he does the horse's neigh himself, so NEIGH! pops over his bucket in his
        /// homemade lettering (cream faces, a wood extrusion: red stays for hits), with sound lines from
        /// the eye slit.
        /// </summary>
        private static GameObject JoustNeigh()
        {
            var root = _kit.NewRoot("BonkJoustNeigh", attackBound: true);
            // Half a metre toward the camera only: the match camera looks down on him, so lettering drawn further
            // forward rises on screen away from his bucket.
            var word = Word(Frame(root, upright: true), "NEIGH!", _m.Neigh, 1.15f, _m.NeighAspect, 0.6f, 0.3f, forward: 0.5f);
            word.transform.localPosition = new Vector3(0f, BucketTop + 0.15f, 0.15f);
            SoundLines(Frame(root));
            _kit.Sound(root, "bonk_neigh", 0.75f, 1f);
            return Save(root);
        }

        /// <summary>Three short sound lines thrown from the bucket's eye slit, twice, as he neighs.</summary>
        private static void SoundLines(GameObject frame)
        {
            var lines = Particles(frame, "Sound lines", _m.Line, 0.5f, ParticleSystemSimulationSpace.Local);
            lines.transform.localPosition = Slit + new Vector3(0f, 0.04f, 0.06f);
            var emission = lines.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3), new ParticleSystem.Burst(0.16f, 3) });
            var main = lines.main;
            main.startLifetime = 0.16f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.07f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.6f, 3.4f);
            var shape = lines.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = 0.04f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            shape.rotation = new Vector3(-35f, 0f, 0f);
            Stretch(lines, 0.04f, 2.2f);
            Fade(lines, 0.5f);
            InFront(lines);
        }

        /// <summary>
        /// The gallop, on the charge's launch cue: a ring of dust where he sets off, then at every hoof a
        /// small homemade "clip!" or "clop!" beside the landing foot with a coconut knock and, on the
        /// ground, a kick of dust (<see cref="LoopFootfalls"/>). On thin air the knocks stay, the dust doesn't.
        /// </summary>
        private static GameObject JoustGallop()
        {
            var root = _kit.NewRoot("BonkJoustGallop", attackBound: false);
            var floor = Floor(root, 0.6f, -0.05f);
            var start = DustPuffs(floor, "Set off", 6, 0.3f, 0.45f, 2.6f, ParticleSystemScalingMode.Local, life: 0.5f);
            FlatRing(start, 0.25f);
            var clip = Hoof(root, "Clip", "clip!", _m.Clip, _m.ClipAspect, "bonk_clip", 0.55f, 0.12f);
            var clop = Hoof(root, "Clop", "clop!", _m.Clop, _m.ClopAspect, "bonk_clop", 0.47f, -0.12f);
            var kick = Child(root, "Kick");
            var dust = DustPuffs(Floor(kick, 0.6f, -0.05f), "Kicked dust", 3, 0.26f, 0.4f, 2.2f,
                ParticleSystemScalingMode.Local, life: 0.45f);
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = 0.12f;
            shape.rotation = new Vector3(-30f, 180f, 0f);
            kick.SetActive(false);
            var footfalls = root.AddComponent<LoopFootfalls>();
            footfalls.ComboStage = GallopStage;
            footfalls.LoopTicks = GallopLoopTicks;
            footfalls.FootfallTicks = HoofTicks;
            footfalls.Steps = new[] { clip, clop };
            footfalls.Ground = kick;
            footfalls.SideOffset = 0.2f;
            return Save(root);
        }

        /// <summary>One hoof's word and coconut knock: an inactive template the footfalls copy beside the foot.</summary>
        private static GameObject Hoof(GameObject root, string name, string text, Material material, float aspect,
            string sound, float volume, float tilt)
        {
            var hoof = Child(root, name);
            var word = Word(hoof, text, material, 0.42f, aspect, 0.45f, 0.5f, forward: 1f, tilt: tilt);
            word.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            _kit.Sound(hoof, sound, volume, 1f);
            hoof.SetActive(false);
            return hoof;
        }

        /// <summary>
        /// A miss or a shield: the dramatic stop. His heels gouge two grooves into the deck as he skids
        /// (<see cref="FollowOwnerBone"/> drags each gouge along a heel), throwing wood chips and a wave
        /// of dust ahead, and he squeals his own brakes, "eeeeerk!".
        /// </summary>
        private static GameObject JoustSkid()
        {
            var root = _kit.NewRoot("BonkJoustSkid", attackBound: false);
            var floor = Floor(root, 0.6f, -0.05f);
            Heel(floor, "Left heel", "bone.left-foot", wave: 1f);
            Heel(floor, "Right heel", "bone.right-foot", wave: 0.4f);
            var dig = DustPuffs(floor, "Dig in", 4, 0.28f, 0.42f, 2f, ParticleSystemScalingMode.Local, life: 0.5f);
            FlatRing(dig, 0.2f);
            _kit.Sound(root, "bonk_squeal", 0.7f, 1f);
            return Save(root);
        }

        /// <summary>
        /// One heel's gouge, laid flat on the floor along the heel's path: dark dots over a wider pale lip
        /// (so it reads on dark decks too), chips flicked up and dust piling ahead as he slows
        /// (<paramref name="wave"/> scales the dust: the front heel pushes the most).
        /// </summary>
        private static void Heel(GameObject floor, string name, string boneId, float wave)
        {
            var heel = Child(floor, name);
            var follow = heel.AddComponent<FollowOwnerBone>();
            follow.BoneId = boneId;
            follow.Seconds = SkidSeconds;
            GougeDots(heel, "Lip", 0.22f, new Color(WoodLight.r, WoodLight.g, WoodLight.b, 0.8f), 46f);
            GougeDots(heel, "Gouge", 0.14f, GougeDark, 44f);

            var chips = Particles(heel, "Chips", _m.Splinter, SkidSeconds);
            var main = chips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.48f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1.2f;
            var emission = chips.emission;
            emission.rateOverDistance = 4f;
            var shape = chips.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.05f;
            shape.rotation = new Vector3(-45f, 0f, 0f);
            Spin(chips, 600f);
            Fade(chips, 0.6f);
            chips.GetComponent<ParticleSystemRenderer>().sortingFudge = -6f;

            var dust = Particles(heel, "Dust wave", _m.Dust, SkidSeconds);
            dust.transform.localPosition = new Vector3(0f, 0.05f, 0.22f);
            main = dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.56f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.05f;
            emission = dust.emission;
            // Most of the dust comes as he slows, so it piles up ahead of the stopped heel.
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(22f * wave, Curve((0f, 0.3f), (0.5f, 1f), (1f, 1f)));
            shape = dust.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = 0.08f;
            shape.rotation = new Vector3(-25f, 0f, 0f);
            Drag(dust, 0.25f);
            RandomFrame(dust, 2, 2);
            SizeOverLife(dust, Curve((0f, 0.4f), (0.25f, 1f), (1f, 1.15f)));
            Fade(dust, 0.45f);
            dust.GetComponent<ParticleSystemRenderer>().sortingFudge = 12f;
        }

        /// <summary>Flat tinted dots laid by distance: one layer of a gouge (the later the fudge, the lower it draws).</summary>
        private static void GougeDots(GameObject heel, string name, float size, Color color, float fudge)
        {
            var dots = Particles(heel, name, _m.Disc, SkidSeconds);
            var main = dots.main;
            main.startLifetime = 1.6f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.9f, size);
            main.startColor = color;
            main.maxParticles = 160;
            var emission = dots.emission;
            emission.rateOverDistance = 36f;
            var renderer = dots.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.sortingFudge = fudge;
            Fade(dots, 0.7f);
        }

        /// <summary>
        /// Off a ledge, the look-down: a "?" in his blue paint pops over his bucket, then a sweat drop and
        /// a small "gulp" with the sound, before he drops and the horse goes in its puff. The "?" stays in
        /// the air where he was as he falls away from it.
        /// </summary>
        private static GameObject JoustLookDown()
        {
            var root = _kit.NewRoot("BonkJoustLookDown", attackBound: false);
            var head = Child(root, "Head");
            head.AddComponent<SnapToOwnerBone>().BoneId = "bone.head";
            var question = Particles(head, "Question", _m.QuestionPaint, 1f, ParticleSystemSimulationSpace.Local);
            question.transform.localPosition = new Vector3(0f, BucketTop + 0.42f, 0f);
            Burst(question, 1);
            var main = question.main;
            main.startLifetime = 0.8f;
            main.startSize = 0.44f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f);
            SizeOverLife(question, Curve((0f, 0.2f), (0.12f, 1.25f), (0.22f, 0.92f), (0.32f, 1f), (1f, 1f)));
            Fade(question, 0.75f);
            question.GetComponent<ParticleSystemRenderer>().sortingFudge = -18f;

            var window = Child(head, "Gulp window");
            var gulp = Child(window, "Gulp");
            var word = Word(gulp, "gulp", _m.Gulp, 0.42f, _m.GulpAspect, 0.5f, 0.12f, forward: 0.6f, tilt: -0.08f);
            word.transform.localPosition = new Vector3(0.36f, 0.12f, 0.05f);
            var drop = Particles(gulp, "Sweat", _m.Sweat, 0.8f, ParticleSystemSimulationSpace.Local);
            drop.transform.localPosition = new Vector3(0.2f, BucketTop + 0.05f, 0.1f);
            Burst(drop, 1);
            main = drop.main;
            main.startLifetime = 0.55f;
            main.startSize = 0.15f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0.25f);
            var velocity = drop.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0.05f, 0.05f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.35f, -0.35f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            SizeOverLife(drop, Curve((0f, 0.3f), (0.15f, 1.1f), (0.3f, 1f), (1f, 0.9f)));
            Fade(drop, 0.7f);
            InFront(drop);
            _kit.Sound(gulp, "bonk_gulp", 0.8f, 1f);
            var beat = window.AddComponent<AttackTickWindow>();
            beat.StartTick = LookDownGulpTick;
            beat.CancelableUntilTick = LookDownGulpTick;
            beat.Content = gulp;
            gulp.SetActive(false);
            return Save(root);
        }

        /// <summary>A puff of straw dust with stalks flying out: the hobby horse arriving or leaving.</summary>
        private static GameObject StrawPoof(GameObject parent, string name, Vector3 position, float size, bool sound)
        {
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            var poof = Child(parent, name);
            poof.transform.localPosition = position;
            // Full size at once, so the horse never shows popping in or out, and drawn about half a metre
            // toward the camera so the horse's head and his arm don't hide it.
            var dust = DustPuffs(poof, "Dust", 5, 0.46f * size, 0.62f * size, 1.4f * size, local, life: 0.55f);
            var puff = Puffs(poof, "Puff", 3, 0.4f * size, 0.54f * size, 1.1f * size, local, life: 0.5f);
            foreach (var cloud in new[] { dust, puff })
            {
                Sphere(cloud, 0.3f * size);
                SizeOverLife(cloud, Curve((0f, 0.8f), (0.15f, 1.1f), (1f, 1.2f)));
                cloud.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, 0f, 1.1f);
            }
            var stalks = Particles(poof, "Straw", _m.Straw, 0.8f, scaling: local);
            Burst(stalks, 12);
            var main = stalks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.46f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f * size, 0.08f * size);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f * size, 5.5f * size);
            main.gravityModifier = 0.6f;
            Sphere(stalks, 0.12f * size);
            Drag(stalks, 0.08f);
            Stretch(stalks, 0.03f, 3.5f);
            Fade(stalks, 0.6f);
            stalks.GetComponent<ParticleSystemRenderer>().sortingFudge = -8f;
            if (sound) _kit.Sound(poof, "bonk_poof", 0.6f, 1f);
            return poof;
        }

        /// <summary>
        /// A homemade word in his lettering that pops in, rises and fades. It draws
        /// <paramref name="forward"/> metres toward the camera, in front of the fighters.
        /// </summary>
        private static ParticleSystem Word(GameObject parent, string name, Material material, float width, float aspect,
            float lifetime, float rise, float forward = LetteringForward, float tilt = 0.12f)
        {
            var word = Particles(parent, name, material, lifetime + 0.05f);
            Burst(word, 1);
            var main = word.main;
            main.startLifetime = lifetime;
            main.startSize3D = true;
            main.startSizeX = width;
            main.startSizeY = width / aspect;
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(tilt);
            var velocity = word.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(rise, rise);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            SizeOverLife(word, Curve((0f, 0.2f), (0.12f, 1.18f), (0.24f, 0.96f), (0.34f, 1f), (1f, 1.03f)));
            Fade(word, 0.75f);
            var renderer = word.GetComponent<ParticleSystemRenderer>();
            renderer.sortingFudge = -18f;
            renderer.pivot = new Vector3(0f, 0f, forward);
            return word;
        }

        // ── Q: the Pot-Lid Throw ─────────────────────────────────────────────────

        /// <summary>
        /// The flying pot lid, face up and spinning flat as it skims out and back. ProjectileVFXManager places the root on
        /// the projectile and leaves its rotation alone (it is not point-first), so the lid stays level on both legs.
        /// </summary>
        private static GameObject JoustScoop()
        {
            var root = _kit.NewRoot("BonkJoustScoop", attackBound: false);
            var dust = DustPuffs(root, "Scoop dust", 5, 0.35f, 0.45f, 2.2f, ParticleSystemScalingMode.Local, life: 0.45f);
            FlatRing(dust, 0.2f);
            Chips(root, 4, ParticleSystemScalingMode.Hierarchy);
            _kit.Sound(root, "bonk_thock", 0.65f, 0.9f);
            return Save(root);
        }

        private static GameObject FaithHalo()
        {
            var root = _kit.NewRoot("BonkFaithHalo", attackBound: true);
            root.AddComponent<ArmorGlowPresentation>().RimColor = new Color(1.4f, 1.05f, 0.45f, 1f);
            return Save(root);
        }

        private static GameObject PotLidProjectile()
        {
            var root = _kit.NewRoot("BonkPotLidProjectile", attackBound: false);
            var spin = Child(root, "Spin");
            spin.AddComponent<PropSpin>().DegreesPerSecond = LidSpinDegreesPerSecond;
            var (across, thick) = LidSize(_kit.NestModel(spin, PotLidPrefabPath, "Lid"));

            // A cream wake a little thicker than the lid seen edge-on, as the camera sees it, thinning to nothing behind it,
            // so it reads on dark stages and from behind and draws the curve home. As wide as the lid's face, a camera-facing
            // trail swallowed the lid in a white sheet (2026-10-22). It outlives a caught lid by a moment.
            var wake = Child(root, "Wake");
            wake.AddComponent<LingerAfterProjectile>().Seconds = 0.25f;
            var trail = wake.AddComponent<TrailRenderer>();
            trail.time = LidWakeSeconds;
            trail.minVertexDistance = 0.04f;
            trail.widthMultiplier = Mathf.Max(0.08f, thick * 1.4f);
            trail.widthCurve = Curve((0f, 1f), (1f, 0f));
            trail.colorGradient = GradientOf(new[] { (0f, Color.white), (1f, Color.white) },
                new[] { (0f, 0.95f), (0.5f, 0.75f), (1f, 0f) }).gradient;
            trail.textureMode = LineTextureMode.Stretch;
            trail.alignment = LineAlignment.View;
            trail.numCapVertices = 2;
            trail.sharedMaterial = _m.Wake;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;

            // Flat spin rings round it, turning with it: from the match camera they are thin cream ellipses.
            var rings = Particles(root, "Spin rings", _m.SpinRings, 99f, ParticleSystemSimulationSpace.Local);
            Burst(rings, 1);
            var main = rings.main;
            main.startLifetime = 99f;
            main.startSize = across * 1.7f;
            Spin(rings, -LidSpinDegreesPerSecond);
            rings.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            _kit.Sound(root, "bonk_lid_whirr", 0.2f, 1f);
            var whirr = root.GetComponent<AudioSource>();
            if (whirr != null) whirr.loop = true;
            root.transform.localScale *= 5f / 3f;
            return Save(root);
        }

        /// <summary>
        /// The lid's width across its face and its thickness, from the nested model's vertices in the lid's own frame (+Y
        /// its face); 0.37 m by 0.09 m when the local model is missing. Renderer bounds are world-aligned boxes round the
        /// mesh, which sits tilted inside the prefab, so they measured it 0.5 m thick.
        /// </summary>
        private static (float across, float thick) LidSize(GameObject lid)
        {
            var filters = lid != null ? lid.GetComponentsInChildren<MeshFilter>() : System.Array.Empty<MeshFilter>();
            var bounds = new Bounds();
            bool any = false;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    var point = lid.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (any) bounds.Encapsulate(point);
                    else bounds = new Bounds(point, Vector3.zero);
                    any = true;
                }
            }
            return any ? (Mathf.Max(bounds.size.x, bounds.size.z), bounds.size.y) : (0.37f, 0.09f);
        }

        /// <summary>
        /// The lid's hit, out and back (both legs use the slot's hit): a small red thunk, the lid ringing twice, tin chips
        /// and a wobbly DOOOONG rising away. The sound is a bright tink, which the owner preferred to a DOOONG.
        /// </summary>
        private static GameObject LidHit()
        {
            var root = _kit.NewRoot("BonkLidHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, 0.9f, 0.12f, Red, scaled);
            Thunk(root, 0.6f, 0.14f, scaled);
            Rings(root, 1.1f, 0.24f, 0.02f, scaled);
            Rings(root, 1.35f, 0.26f, 0.16f, scaled, name: "Rings again");
            Chips(root, 4, scaled, wood: false);
            var word = Word(root, "Dooong", _m.Dooong, 1.05f, _m.DooongAspect, 0.7f, 0.55f);
            word.transform.localPosition = new Vector3(0.12f, 0.42f, 0f);
            var main = word.main;
            main.scalingMode = scaled;
            var wobble = word.rotationOverLifetime;
            wobble.enabled = true;
            wobble.z = new ParticleSystem.MinMaxCurve(3f, Curve((0f, 1f), (0.2f, -1f), (0.4f, 0.8f), (0.6f, -0.6f), (0.8f, 0.4f), (1f, 0f)));
            _kit.Sound(root, "bonk_lid_tink", 0.8f, 1f);
            return Save(root);
        }

        /// <summary>
        /// The catch: the lid is back on his left shoulder with a cream gleam and a tin clank. The event fires where the
        /// lid reached him (his chest), so the gleam moves onto the shoulder.
        /// </summary>
        private static GameObject LidCatch()
        {
            var root = _kit.NewRoot("BonkLidCatch", attackBound: false);
            var shoulder = Child(root, "Shoulder");
            var snap = shoulder.AddComponent<SnapToOwnerBone>();
            snap.BoneId = "mixamorig:LeftArm";
            snap.Offset = new Vector3(0f, 0.03f, 0f);
            var gleam = Particles(shoulder, "Gleam", _m.Gleam, 0.4f, ParticleSystemSimulationSpace.Local);
            Burst(gleam, 1);
            var main = gleam.main;
            main.startLifetime = 0.28f;
            main.startSize = 0.72f;
            SizeOverLife(gleam, Curve((0f, 0.2f), (0.25f, 1.15f), (1f, 0f)));
            Spin(gleam, 220f);
            InFront(gleam);
            // Drawn toward the camera, so the lid and the plank over his shoulder don't hide it.
            gleam.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0f, 0f, 0.8f);
            _kit.Sound(root, "bonk_lid_clank", 0.75f, 1f);
            return Save(root);
        }

        /// <summary>
        /// The lid comes home while he is busy and bonks his own bucket. The slot's hit (the tink and the DOOOONG) plays at
        /// the contact; this rings his bucket twice and pops his blue-paint "?" over it, then his own puzzled "hm?".
        /// </summary>
        private static GameObject LidSelfHit()
        {
            var root = _kit.NewRoot("BonkLidSelfHit", attackBound: false);
            var head = Child(root, "Head");
            var snap = head.AddComponent<SnapToOwnerBone>();
            snap.BoneId = "bone.head";
            snap.Offset = new Vector3(0f, 0.15f, 0f);
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            Rings(head, 0.95f, 0.26f, 0.04f, local, ParticleSystemSimulationSpace.Local);
            Rings(head, 1.15f, 0.28f, 0.2f, local, ParticleSystemSimulationSpace.Local, "Rings again");
            var question = Particles(head, "Question", _m.QuestionPaint, 1f, ParticleSystemSimulationSpace.Local);
            question.transform.localPosition = new Vector3(0f, BucketTop + 0.27f, 0f);
            Burst(question, 1);
            var main = question.main;
            main.startDelay = 0.08f;
            main.startLifetime = 0.8f;
            main.startSize = 0.44f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f);
            SizeOverLife(question, Curve((0f, 0.2f), (0.12f, 1.25f), (0.22f, 0.92f), (0.32f, 1f), (1f, 1f)));
            Fade(question, 0.75f);
            question.GetComponent<ParticleSystemRenderer>().sortingFudge = -18f;
            _kit.Sound(head, "bonk_hm", 0.85f, 1f);
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

        /// <summary>Chips thrown off a contact: wood splinters from his plank, or steel off his armour.</summary>
        private static ParticleSystem Chips(GameObject root, int count, ParticleSystemScalingMode scaling, bool wood = true)
        {
            var chips = Particles(root, wood ? "Splinters" : "Steel chips", wood ? _m.Splinter : _m.Chip, 0.6f, scaling: scaling);
            Burst(chips, count);
            var main = chips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
            main.startSize = wood ? new ParticleSystem.MinMaxCurve(0.1f, 0.18f) : new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
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
        private const int PopWidth = 384, PopHeight = 160;

        private static Materials BuildMaterials()
        {
            var letters = new MaskField(LettersMaskPath);
            var question = new MaskField(QuestionMaskPath);
            var pop = new MaskField(PopMaskPath);
            var neigh = WordMaterial("BonkNeigh", NeighMaskPath, 512, 4.5f, 2.2f);
            var clip = WordMaterial("BonkClip", ClipMaskPath, 256, 3.5f, 1.6f);
            var clop = WordMaterial("BonkClop", ClopMaskPath, 256, 3.5f, 1.6f);
            var gulp = WordMaterial("BonkGulp", GulpMaskPath, 256, 3.5f, 1.6f);
            var dooong = WordMaterial("BonkDooong", DooongMaskPath, 512, 4.5f, 2.2f, Rim, Red, RedDark);
            return new Materials
            {
                Neigh = neigh.material, NeighAspect = neigh.aspect,
                Clip = clip.material, ClipAspect = clip.aspect,
                Clop = clop.material, ClopAspect = clop.aspect,
                Gulp = gulp.material, GulpAspect = gulp.aspect,
                Dooong = dooong.material, DooongAspect = dooong.aspect,
                SpinRings = Mat("BonkLidSpinRings", 256, 256, (u, v) => SpinRingsPixel(P(u, v))),
                Wake = Mat("BonkLidWake", 64, 32, WakePixel),
                QuestionPaint = Mat("BonkQuestionPaint", 128, 128, (u, v) => ComicGlyph(question, u, v, 128f / question.Width,
                    new Vector2(0.035f, -0.055f), 3f, 1.6f, PaintRim, Paint, PaintDark)),
                Straw = Mat("BonkStraw", 64, 16, StrawPixel),
                Disc = Mat("BonkDisc", 64, 64, (u, v) => DiscPixel(P(u, v))),
                Sweat = Mat("BonkSweat", 64, 64, (u, v) => SweatPixel(P(u, v))),
                Pennant = Mat("BonkPennant", 256, 112, PennantPixel),
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
                Splinter = Mat("BonkSplinter", 64, 64, (u, v) => SplinterPixel(P(u, v))),
                Gleam = Mat("BonkGleam", 128, 128, (u, v) => GleamPixel(P(u, v))),
                Beam = Mat("BonkHolyBeam", 64, 256, BeamPixel),
                Rays = Mat("BonkHolyRays", 512, 256, RaysPixel),
                Pop = Mat("BonkPop", PopWidth, PopHeight, (u, v) => PopPixel(u, v, pop)),
                Pinwheel = Mat("BonkPinwheel", 128, 128, (u, v) => PinwheelPixel(P(u, v))),
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
            => ComicGlyph(mask, mu, mv, texelsPerPixel, extrude, outline, faceLine, Rim, Red, RedDark);

        /// <summary>The same lettering with its own rim and extrusion colours (his blue paint for POP!).</summary>
        private static Color ComicGlyph(MaskField mask, float mu, float mv, float texelsPerPixel, Vector2 extrude,
            float outline, float faceLine, Color rim, Color extrusionColor, Color extrusionDark)
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
            Color color = Flat(Mathf.Min(face, extrusion) - outline, rim);
            color = Over(color, Flat(extrusion, Color.Lerp(extrusionColor, extrusionDark, depth)));
            color = Over(color, Flat(face - faceLine, rim));
            return Over(color, Flat(face, Cream));
        }

        // The POP! mask spans its centre band, like the BONK! one.
        private const float PopU0 = 0.04f, PopU1 = 0.96f, PopV0 = 0.3f, PopV1 = 0.7f;

        /// <summary>POP! in his paint: cream faces, a blue extrusion and a navy rim.</summary>
        private static Color PopPixel(float u, float v, MaskField mask)
        {
            float texels = PopWidth / ((PopU1 - PopU0) * mask.Width);
            return ComicGlyph(mask, Mathf.Lerp(PopU0, PopU1, u), Mathf.Lerp(PopV0, PopV1, v), texels,
                new Vector2(0.0135f, -0.021f), 4f, 2f, PaintRim, Paint, PaintDark);
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

        /// <summary>The lid's spin rings, flat round it: two broken cream rings with a dust rim, the outer one thinner.</summary>
        private static Color SpinRingsPixel(Vector2 p)
        {
            float d = float.MaxValue, angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            foreach (var (radius, width) in new[] { (0.7f, 0.055f), (0.92f, 0.04f) })
                foreach (var (a0, a1) in new[] { (200f, 340f), (25f, 150f) })
                    d = Mathf.Min(d, ArcDistance(p, angle, radius, width, a0, a1));
            return ToonShapes.Bands(d * 128f, 2.5f, 99f, DustRim, Cream, Cream);
        }

        /// <summary>Distance to a round-ended arc of a circle, from <paramref name="a0"/> to <paramref name="a1"/> degrees.</summary>
        private static float ArcDistance(Vector2 p, float angle, float radius, float width, float a0, float a1)
        {
            float mid = (a0 + a1) * 0.5f, half = (a1 - a0) * 0.5f, delta = Mathf.DeltaAngle(mid, angle);
            if (Mathf.Abs(delta) <= half) return Mathf.Abs(p.magnitude - radius) - width;
            float end = (mid + Mathf.Sign(delta) * half) * Mathf.Deg2Rad;
            return (p - new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * radius).magnitude - width;
        }

        /// <summary>The lid's wake across V: a dust rim, a cream body and a white core line; the trail's gradient fades it.</summary>
        private static Color WakePixel(float u, float v)
            => ToonShapes.Bands((Mathf.Abs(v - 0.5f) - 0.47f) * 32f, 2.5f, 8f, DustRim, Cream, Color.white);

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

        // ── Sir Bonk ─────────────────────────────────────────────────────────────

        /// <summary>A wood splinter off the plank: a long sliver with a lit edge.</summary>
        private static Color SplinterPixel(Vector2 p)
        {
            var outline = new[] { new Vector2(0.95f, 0.02f), new Vector2(-0.2f, 0.17f), new Vector2(-0.9f, -0.02f), new Vector2(-0.15f, -0.13f) };
            return ToonShapes.Bands(Polygon(p, outline) * 32f, 1.5f, 2.5f, WoodRim, Wood, WoodLight);
        }


        /// <summary>A knightly gleam: a four-point star in cream with a white core and a dust rim.</summary>
        private static Color GleamPixel(Vector2 p)
        {
            var q = Rotate(p, 45f);
            float d = Mathf.Min(Mathf.Min(Rhombus(p, new Vector2(0.95f, 0.15f)), Rhombus(p, new Vector2(0.15f, 0.95f))),
                Mathf.Min(Rhombus(q, new Vector2(0.45f, 0.09f)), Rhombus(q, new Vector2(0.09f, 0.45f))));
            Color color = ToonShapes.Bands(d * 64f, 2f, 3.5f, DustRim, Cream, Color.white);
            return Over(color, Flat(Circle(p, Vector2.zero, 0.12f) * 64f, Color.white));
        }

        /// <summary>
        /// The shaft of light, U across and V up: flat translucent cream with a paler core, crisp
        /// sides, fading in above the floor and out toward the sky. No glow: it is painted.
        /// </summary>
        private static Color BeamPixel(float u, float v)
        {
            float across = Mathf.Abs(u * 2f - 1f);
            float side = Mathf.Clamp01((0.86f - across) * 32f);
            float core = Mathf.Clamp01((0.3f - across) * 32f);
            Color color = Color.Lerp(Cream, Color.white, core);
            color.a = (0.34f + 0.16f * core) * side * Step(0f, 0.05f, v) * (1f - Step(0.7f, 1f, v));
            return color;
        }

        /// <summary>
        /// Storybook rays fanning up from the bottom centre: flat cream wedges of alternating widths,
        /// gone near their origin (behind him) and fading toward the edge of the quad.
        /// </summary>
        private static Color RaysPixel(float u, float v)
        {
            var p = new Vector2((u - 0.5f) * 2f, v);
            float r = p.magnitude, angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            const int count = 15;
            float best = float.MaxValue;
            for (int k = 0; k < count; k++)
            {
                float centre = Mathf.Lerp(8f, 172f, k / (float)(count - 1));
                float half = k % 2 == 0 ? 2.6f : 1.5f;
                best = Mathf.Min(best, (Mathf.Abs(angle - centre) - half) * Mathf.Deg2Rad * r * 256f);
            }
            Color color = Cream;
            color.a = Mathf.Clamp01(0.5f - best) * 0.5f * Step(0.08f, 0.2f, r) * (1f - Step(0.7f, 1f, r));
            return color;
        }

        /// <summary>
        /// A word in his lettering, baked around its own ink: cream faces with a wood extrusion down and to
        /// the right (in proportion to the letters' height, as BONK!'s) and a dark wood outline. Returns the
        /// material and the texture's width over height for the particle's size.
        /// </summary>
        private static (Material material, float aspect) WordMaterial(string name, string maskPath, int width,
            float outline, float faceLine) => WordMaterial(name, maskPath, width, outline, faceLine, WoodRim, Wood, WoodDark);

        /// <summary>The same word with its own rim and extrusion colours: the lid's hit word in his hit red.</summary>
        private static (Material material, float aspect) WordMaterial(string name, string maskPath, int width,
            float outline, float faceLine, Color rim, Color extrusionColor, Color extrusionDark)
        {
            var mask = new MaskField(maskPath);
            float u0 = 1f, u1 = 0f, v0 = 1f, v1 = 0f;
            const int grid = 256;
            for (int y = 0; y < grid; y++)
                for (int x = 0; x < grid; x++)
                {
                    float u = (x + 0.5f) / grid, v = (y + 0.5f) / grid;
                    if (mask.Sample(u, v) >= 0f) continue;
                    u0 = Mathf.Min(u0, u);
                    u1 = Mathf.Max(u1, u);
                    v0 = Mathf.Min(v0, v);
                    v1 = Mathf.Max(v1, v);
                }
            float ink = v1 - v0;
            var extrude = new Vector2(0.055f * ink, -0.085f * ink);
            const float pad = 0.025f;
            float bandU0 = Mathf.Max(0f, u0 - pad), bandU1 = Mathf.Min(1f, u1 + pad + extrude.x);
            float bandV0 = Mathf.Max(0f, v0 - pad + extrude.y), bandV1 = Mathf.Min(1f, v1 + pad);
            int height = Mathf.Max(8, Mathf.RoundToInt(width * (bandV1 - bandV0) / (bandU1 - bandU0)));
            float texels = width / ((bandU1 - bandU0) * mask.Width);
            var material = Mat(name, width, height, (u, v) => ComicGlyph(mask, Mathf.Lerp(bandU0, bandU1, u),
                Mathf.Lerp(bandV0, bandV1, v), texels, extrude, outline, faceLine, rim, extrusionColor, extrusionDark));
            return (material, width / (float)height);
        }

        /// <summary>A straw stalk for stretched particles, long along U: straw yellow with a wood rim and a lit core.</summary>
        private static Color StrawPixel(float u, float v)
        {
            var p = new Vector2(u * 2f - 1f, (v * 2f - 1f) * 0.25f);
            float d = Capsule(p, new Vector2(-0.86f, 0f), new Vector2(0.86f, 0.03f), 0.13f, 0.08f);
            return ToonShapes.Bands(d * 32f, 1.2f, 1.4f, WoodRim, Straw, StrawLight);
        }

        /// <summary>A plain disc tinted by particle colour: the dots of a gouge and of its pale lip.</summary>
        private static Color DiscPixel(Vector2 p) => Alpha(0.5f - (p.magnitude - 0.9f) * 32f);

        /// <summary>A sweat drop, tip up: pale water blue with a navy rim and a white glint.</summary>
        private static Color SweatPixel(Vector2 p)
        {
            float drop = Mathf.Min(Circle(p, new Vector2(0f, -0.25f), 0.5f),
                Polygon(p, new[] { new Vector2(-0.4f, -0.1f), new Vector2(0.4f, -0.1f), new Vector2(0f, 0.9f) }));
            Color color = ToonShapes.Bands(drop * 32f, 2.5f, 99f, PaintRim, Sweat, Sweat);
            return Over(color, Flat(Circle(p, new Vector2(-0.16f, -0.18f), 0.11f) * 32f, Color.white));
        }

        /// <summary>
        /// The pennant, U from the hoist to the fly end and V across (V = 1 up): cream cloth tapering to a
        /// point, a wood rim, a wooden hem where it is tied, and his crown in blue paint near the hoist.
        /// </summary>
        private static Color PennantPixel(float u, float v)
        {
            const float length = 256f, height = 112f, hem = 8f;
            var p = new Vector2(u * length, (v - 0.5f) * height);
            float half = (height * 0.5f - 3f) * Mathf.Pow(Mathf.Clamp01(1f - p.x / (length - 4f)), 0.85f);
            float cloth = Mathf.Max(Mathf.Abs(p.y) - half, Mathf.Max(2f - p.x, p.x - (length - 4f)));
            Color color = ToonShapes.Bands(cloth, 2.6f, 99f, WoodRim, Cream, Cream);
            color = Over(color, ToonShapes.Bands(Mathf.Max(cloth, p.x - hem), 2f, 99f, WoodRim, Wood, Wood));
            return Over(color, CrownPixel(p - new Vector2(64f, 0f)));
        }

        /// <summary>His crown in blue paint, in texels about its centre: three points with balls on a band.</summary>
        private static Color CrownPixel(Vector2 q)
        {
            float d = Box(q - new Vector2(0f, -12f), new Vector2(24f, 6f));
            d = Mathf.Min(d, Polygon(q, new[] { new Vector2(-24f, -8f), new Vector2(-8f, -8f), new Vector2(-21f, 13f) }));
            d = Mathf.Min(d, Polygon(q, new[] { new Vector2(-9f, -8f), new Vector2(9f, -8f), new Vector2(0f, 17f) }));
            d = Mathf.Min(d, Polygon(q, new[] { new Vector2(8f, -8f), new Vector2(24f, -8f), new Vector2(21f, 13f) }));
            d = Mathf.Min(d, Mathf.Min(Circle(q, new Vector2(-21f, 14f), 3.5f),
                Mathf.Min(Circle(q, new Vector2(0f, 18f), 4f), Circle(q, new Vector2(21f, 14f), 3.5f))));
            return ToonShapes.Bands(d, 2f, 99f, PaintRim, Paint, Paint);
        }

        /// <summary>A tiny windmill: four lattice sails in cream canvas on wooden spars around a hub.</summary>
        private static Color PinwheelPixel(Vector2 p)
        {
            float sail = float.MaxValue, spar = float.MaxValue, bars = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                float a = (k * 90f + 20f) * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var n = new Vector2(-d.y, d.x);
                sail = Mathf.Min(sail, Polygon(p, new[] { d * 0.14f, d * 0.94f, d * 0.94f + n * 0.4f, d * 0.14f + n * 0.22f }));
                spar = Mathf.Min(spar, Capsule(p, Vector2.zero, d * 0.97f, 0.04f, 0.032f));
                foreach (float t in new[] { 0.42f, 0.68f })
                    bars = Mathf.Min(bars, Capsule(p, d * t, d * t + n * (0.22f + 0.2f * t), 0.016f, 0.016f));
            }
            Color color = ToonShapes.Bands(sail * 64f, 2f, 99f, WoodRim, Cream, Cream);
            Color lattice = Flat(bars * 64f, Wood);
            lattice.a *= Mathf.Clamp01(0.5f - sail * 64f - 2f);
            color = Over(color, lattice);
            color = Over(color, ToonShapes.Bands(spar * 64f, 1.4f, 99f, WoodRim, Wood, Wood));
            return Over(color, ToonShapes.Bands(Circle(p, Vector2.zero, 0.12f) * 64f, 1.5f, 3f, WoodRim, Wood, WoodLight));
        }
    }
}
#endif
