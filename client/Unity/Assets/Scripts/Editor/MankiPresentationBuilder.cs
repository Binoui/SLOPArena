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
    /// Rebuilds Manki's package-owned combat presentation from code (docs/characters/manki.md):
    /// "Controlled Chaos". Toon blasts with chunky smoke that clears fast, gadget junk (bolts,
    /// nuts, springs, gears) out of every boom, and his winking, tongue-out grin rising in the
    /// smoke of his two big booms. The gags are his gadgets backfiring on him: a coughing
    /// jetpack, a backblast that singes his fur, a rattling spray can. Workflow and readability
    /// rules are in skills/sloparena-character-vfx-pass; plumbing is in <see cref="PresentationBuilderKit"/>.
    /// </summary>
    public static class MankiPresentationBuilder
    {
        private const string BombModelPath = "Assets/Art/Characters/manki/Models/bomb.fbx";
        // The bomb model's import needs 80x, turned upright; the fuse tip sits here (measured).
        private const float BombModelScale = 80f;
        private static readonly Vector3 FuseTip = new(0.204f, 0.4f, 0.01f);
        // The bazooka's barrel runs along the muzzle cue's +Y; its back end is this far behind.
        private const float BarrelLength = 1.62f;
        // From the spray can's nozzle (the charge cue) back to the middle of the can, hand +Z.
        private const float CanCentre = 0.36f;
        // Blast radii of the cooked capabilities, for the floor rings (metres).
        private const float BlastRadius = 3f, IgnitionRadius = 1.5f;

        private static readonly Color Soot = new(0.11f, 0.075f, 0.063f);
        private static readonly Color Ember = new(0.76f, 0.21f, 0.106f);
        private static readonly Color Blast = new(1f, 0.54f, 0.11f);
        private static readonly Color Fuse = new(1f, 0.82f, 0.25f);
        private static readonly Color Cream = new(1f, 0.965f, 0.84f);
        private static readonly Color SmokeRim = new(0.12f, 0.094f, 0.078f);
        private static readonly Color Smoke = new(0.416f, 0.38f, 0.353f);
        private static readonly Color SmokeLight = new(0.66f, 0.624f, 0.588f);
        private static readonly Color SootMid = new(0.23f, 0.19f, 0.165f);
        private static readonly Color SootLight = new(0.353f, 0.306f, 0.275f);
        private static readonly Color SteelRim = new(0.086f, 0.094f, 0.11f);
        private static readonly Color Steel = new(0.608f, 0.616f, 0.62f);
        private static readonly Color SteelLight = new(0.914f, 0.93f, 0.94f);
        private static readonly Color MaskRed = new(0.9f, 0.2f, 0.165f);
        private static readonly Color MaskDark = new(0.43f, 0.06f, 0.047f);
        private static readonly Color Tongue = new(0.85f, 0.325f, 0.31f);
        private static readonly Color MistRim = new(0.49f, 0.54f, 0.58f);
        private static readonly Color Mist = new(0.91f, 0.933f, 0.95f);

        // Junk sheet cells (2x2): bolt, nut, spring, gear.
        private const int Bolt = 0, Nut = 1, Spring = 2, Gear = 3;

        private static PresentationBuilderKit _kit;
        private static Materials _m;

        private sealed class Materials
        {
            public Material Glow, SoftGlow, Fireball, Flash, Pow, Puff, Soot, Mist, Junk, Ring, Scorch, Spark, Flame,
                FlameJet, FlameJetHot, Grin, Ribbon, RibbonHeavy, Rattle, Rocket;
            public Mesh RocketMesh, LighterMesh;
        }

        [CliCommand("sloparena.presentation.manki.build", "Rebuild Manki's package presentation prefabs, textures, meshes, materials and bindings in Edit Mode. Saving the catalog triggers the usual package cook.", MainThreadRequired = true, Tags = new[] { "presentation", "character" })]
        public static object Build()
        {
            using var kit = new PresentationBuilderKit("manki", audioFolder: "Manki");
            _kit = kit;
            _m = BuildMaterials();
            var built = new List<(string id, GameObject prefab)>
            {
                ("presentation.manki.normal.strike", Swing("MankiNormalStrike", 0.15f, 0.1f, heavy: false, puffs: 2)),
                ("presentation.manki.normal.sweep", Swing("MankiNormalSweep", 0.22f, 0.12f, heavy: false, puffs: 3)),
                ("presentation.manki.normal.heavy", Swing("MankiHeavySwing", 0.32f, 0.14f, heavy: true, puffs: 4)),
                ("presentation.manki.normal.hit", NormalHit(heavy: false)),
                ("presentation.manki.normal.heavy-hit", NormalHit(heavy: true)),
                ("presentation.manki.blast.hit", BlastHit()),
                ("presentation.manki.round-bomb.explosion", Boom("MankiRoundBombExplosion", big: false)),
                ("presentation.manki.jetpack-boost.ignition", Ignition()),
                ("presentation.manki.jetpack-boost.exhaust", Exhaust()),
                ("presentation.manki.jetpack-boost.trail", ExhaustTrail()),
                ("presentation.manki.jetpack-boost.cough", Cough()),
                ("presentation.manki.bazooka.ready", BazookaReady()),
                ("presentation.manki.bazooka.muzzle", Muzzle()),
                ("presentation.manki.bazooka.singe", Singe()),
                ("presentation.manki.bazooka.explosion", Boom("MankiBazookaExplosion", big: true)),
                ("presentation.manki.aerosol-inferno.charge", AerosolCharge()),
                ("presentation.manki.aerosol-inferno.lighter", Lighter()),
                ("presentation.manki.aerosol-inferno.start", Inferno()),
                ("presentation.manki.aerosol-inferno.hit", InfernoHit()),
            };
            var bomb = LitBomb();
            var rocket = Rocket();
            // The bomb root is unscaled now (the model carries its import scale), so bind at 1x.
            kit.BindProjectile(CharacterClass.Manki, AbilitySlots.A, bomb);
            kit.BindProjectile(CharacterClass.Manki, AbilitySlots.R, rocket);
            return kit.Finish(built, new { bomb = AssetDatabase.GetAssetPath(bomb), rocket = AssetDatabase.GetAssetPath(rocket) });
        }

        [MenuItem("Tools/SLOPArena/Presentation/Rebuild Manki Presentation")]
        private static void Menu() => Debug.Log("[MankiPresentationBuilder] " + Newtonsoft.Json.JsonConvert.SerializeObject(Build()));

        // ── Normals ──────────────────────────────────────────────────────────────

        /// <summary>An orange toon swoosh behind the striking limb and a puff of smoke off it.</summary>
        private static GameObject Swing(string name, float width, float emitFor, bool heavy, int puffs)
        {
            var root = _kit.NewRoot(name, attackBound: true);
            var frame = Frame(root);
            Ribbon(frame, "Ribbon", heavy ? _m.RibbonHeavy : _m.Ribbon, width, emitFor);
            SmokePuffs(frame, puffs, heavy ? 0.24f : 0.16f, 1f);
            return _kit.Save(root);
        }

        /// <summary>A soot-rimmed orange "pow" with sparks; heavies add a flash behind and a nut flying off.</summary>
        private static GameObject NormalHit(bool heavy)
        {
            var root = _kit.NewRoot(heavy ? "MankiHeavyHit" : "MankiNormalHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, heavy ? 1.05f : 0.85f, 0.12f, Blast, scaled);
            if (heavy) Pop(root, "Flash", _m.Flash, 1f, 0.15f, scaled, spin: true, fudge: 5f);
            Pop(root, "Pow", _m.Pow, heavy ? 0.72f : 0.6f, heavy ? 0.14f : 0.12f, scaled, spin: true);
            Sparks(root, heavy ? 7 : 4, scaled);
            Puffs(root, "Puffs", heavy ? 3 : 2, 0.2f, 0.3f, 1.5f, scaled);
            if (heavy) Junk(root, "Nut", Nut, 1, 3f, 4.5f, 0.17f, 0.8f, scaled, floor: null);
            return _kit.Save(root);
        }

        // ── Blasts ───────────────────────────────────────────────────────────────

        /// <summary>Round Bomb, Jetpack and Bazooka contacts: a pow, a small fireball, puffs and sparks.</summary>
        private static GameObject BlastHit()
        {
            var root = _kit.NewRoot("MankiBlastHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, 1.2f, 0.14f, Blast, scaled);
            Fireballs(root, 1, 0.55f, 0.65f, 0.24f, scaled);
            Pop(root, "Pow", _m.Pow, 0.7f, 0.14f, scaled, spin: true);
            Puffs(root, "Puffs", 3, 0.26f, 0.36f, 2f, scaled);
            Sparks(root, 6, scaled);
            return _kit.Save(root);
        }

        /// <summary>
        /// The Round Bomb and Bazooka explosion: a flash star, a toon fireball, a ring of puffs and
        /// gadget junk that lands, then his grin rises out of the smoke. On the floor, a ring grows to
        /// the real blast radius and a scorch with glowing cracks fades over 2.5 s.
        /// </summary>
        private static GameObject Boom(string name, bool big)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            float b = big ? 1.25f : 1f;
            var floor = Floor(root, 3f, -0.6f);
            FloorRing(floor, BlastRadius * 2f, 0.36f);
            var scorch = Particles(floor, "Scorch", _m.Scorch, 2.4f);
            Burst(scorch, 1);
            var main = scorch.main;
            main.startLifetime = 2.3f;
            main.startSize = 2.6f * b;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            scorch.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            scorch.GetComponent<ParticleSystemRenderer>().sortingFudge = 40f;
            Fade(scorch, 0.55f);

            Pop(root, "Flash", _m.Flash, 3f * b, 0.13f, local, spin: true, fudge: 25f);
            Halo(root, 2.4f * b, 0.12f, Fuse, local);
            Fireballs(root, big ? 4 : 3, 1.5f * b, 1.9f * b, 0.4f, local);
            Puffs(root, "Puffs", big ? 10 : 8, 0.55f * b, 0.85f * b, 4f, local, ring: 0.7f * b, life: 0.85f);
            Junk(root, "Junk", -1, big ? 7 : 5, 5f * b, 8f * b, 0.3f, 1.6f, local, floor.transform);
            Sparks(root, big ? 14 : 10, local, 0.3f, 4f, 8f);
            Grin(root, big ? 2f : 1.65f, 0.22f);
            _kit.Sound(root, big ? "manki_boom_big" : "manki_boom", big ? 0.9f : 0.8f, 1f);
            return _kit.Save(root);
        }

        /// <summary>His winking, tongue-out grin in the smoke: rises out of the blast after the fireball.</summary>
        private static void Grin(GameObject root, float size, float delay)
        {
            var grin = Particles(root, "Grin", _m.Grin, 2f);
            grin.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            Burst(grin, 1);
            var main = grin.main;
            main.startDelay = delay;
            main.startLifetime = 1.2f;
            main.startSize = size;
            main.startSpeed = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            var velocity = grin.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // x, y and z share one curve mode; a zero multiplier keeps the grin from drifting sideways.
            var rise = Curve((0f, 1.2f), (0.35f, 0.5f), (1f, 0.25f));
            velocity.x = new ParticleSystem.MinMaxCurve(0f, rise);
            velocity.y = new ParticleSystem.MinMaxCurve(1f, rise);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, rise);
            SizeOverLife(grin, Curve((0f, 0.35f), (0.12f, 1.08f), (0.25f, 1f), (1f, 1.12f)));
            var color = grin.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (0.08f, 1f), (0.62f, 1f), (1f, 0f) });
            grin.GetComponent<ParticleSystemRenderer>().sortingFudge = -2f;
        }

        // ── Jetpack Boost (E) ────────────────────────────────────────────────────

        /// <summary>
        /// Ignition, centred on Manki: a squashed flash and fireball at his feet, puffs hugging the
        /// ground, a floor ring at the blast radius, and a spring and a bolt popping loose.
        /// </summary>
        private static GameObject Ignition()
        {
            var root = _kit.NewRoot("MankiJetpackIgnition", attackBound: false);
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            var floor = Floor(root, 1.4f, -0.75f);
            FloorRing(floor, IgnitionRadius * 2f, 0.3f);
            var feet = new GameObject("Feet");
            feet.transform.SetParent(root.transform, false);
            feet.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            var flash = Pop(feet, "Flash", _m.Flash, 1f, 0.13f, local, spin: false, fudge: 25f);
            var main = flash.main;
            main.startSize3D = true;
            main.startSizeX = 3f;
            main.startSizeY = 1.6f;
            main.startSizeZ = 1f;
            var fire = Fireballs(feet, 2, 1f, 1.2f, 0.32f, local);
            main = fire.main;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(1.5f, 1.8f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.9f, 1.1f);
            main.startSizeZ = 1f;
            var puffs = Puffs(feet, "Ground puffs", 6, 0.45f, 0.65f, 3.6f, local, life: 0.85f);
            var shape = puffs.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.45f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            Junk(root, "Spring", Spring, 1, 4.5f, 6f, 0.34f, 1.3f, local, floor.transform);
            Junk(root, "Bolt", Bolt, 1, 4.5f, 6f, 0.3f, 1.3f, local, floor.transform);
            Sparks(feet, 6, local, 0.3f, 3f, 6f);
            _kit.Sound(root, "manki_jet_ignite", 0.75f, 1f);
            return _kit.Save(root);
        }

        /// <summary>A flaring toon exhaust under the hips while he rises; it ends with the move at the apex.</summary>
        private static GameObject Exhaust()
        {
            var root = _kit.NewRoot("MankiJetpackExhaust", attackBound: true);
            var frame = Frame(root, upright: true);
            var glow = Particles(frame, "Glow", _m.Glow, 1f, ParticleSystemSimulationSpace.Local);
            glow.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            var main = glow.main;
            main.loop = true;
            main.startLifetime = 0.12f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 0.8f);
            main.startColor = new Color(1f, 0.62f, 0.25f, 0.55f);
            var emission = glow.emission;
            emission.rateOverTime = 20f;
            Behind(glow);

            var flame = Particles(frame, "Flame", _m.Flame, 1f, ParticleSystemSimulationSpace.Local);
            flame.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            main = flame.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.13f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.42f, 0.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(Mathf.PI - 0.25f, Mathf.PI + 0.25f);
            main.maxParticles = 32;
            emission = flame.emission;
            emission.rateOverTime = 40f;
            Sphere(flame, 0.05f);
            var velocity = flame.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            velocity.y = new ParticleSystem.MinMaxCurve(-3f, -1.6f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            RandomFrame(flame, 2, 2);
            SizeOverLife(flame, Curve((0f, 0.55f), (0.3f, 1f), (1f, 0.7f)));
            _kit.Sound(root, "manki_jet_sputter", 0.55f, 1f);
            return _kit.Save(root);
        }

        /// <summary>
        /// The sputtering, dotted puff column the ascent leaves behind. Not attack-bound: it emits
        /// only during the rise and fades on its own after the move ends at the apex.
        /// </summary>
        private static GameObject ExhaustTrail()
        {
            var root = _kit.NewRoot("MankiJetpackTrail", attackBound: false);
            var frame = Frame(root, upright: true);
            var puffs = Particles(frame, "Puffs", _m.Puff, 0.62f);
            puffs.transform.localPosition = new Vector3(0f, -0.38f, 0f);
            var main = puffs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.34f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 64;
            var emission = puffs.emission;
            // Random spacing between puffs is the sputter.
            emission.rateOverDistance = new ParticleSystem.MinMaxCurve(1.2f, 4.2f);
            Sphere(puffs, 0.07f);
            Drift(puffs, 0.15f, -0.1f, 0.2f);
            RandomFrame(puffs, 2, 2);
            SizeOverLife(puffs, Curve((0f, 0.5f), (0.25f, 1f), (1f, 1.25f)));
            Fade(puffs, 0.4f);

            var embers = Particles(frame, "Embers", _m.Spark, 0.62f);
            embers.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            main.gravityModifier = 0.5f;
            emission = embers.emission;
            emission.rateOverTime = 18f;
            var shape = embers.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = 0.05f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            Spin(embers, 300f);
            Fade(embers, 0.5f);
            return _kit.Save(root);
        }

        /// <summary>At the apex the engine coughs: a ring of puffs, a "pff" and a bolt dropping off.</summary>
        private static GameObject Cough()
        {
            var root = _kit.NewRoot("MankiJetpackCough", attackBound: false);
            var ring = Particles(root, "Smoke ring", _m.Puff, 0.2f);
            Burst(ring, 12);
            var main = ring.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.65f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.2f);
            main.startSpeed = 0.9f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = ring.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.22f;
            shape.radiusThickness = 0f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            shape.rotation = new Vector3(90f, 0f, 0f);
            Drag(ring, 0.3f);
            RandomFrame(ring, 2, 2);
            SizeOverLife(ring, Curve((0f, 0.5f), (0.2f, 1f), (1f, 1.15f)));
            Fade(ring, 0.45f);

            var pff = Particles(root, "Pff", _m.Soot, 0.3f);
            Burst(pff, 1);
            main = pff.main;
            main.startLifetime = 0.3f;
            main.startSize = 0.32f;
            RandomFrame(pff, 2, 2);
            SizeOverLife(pff, Curve((0f, 0.4f), (0.2f, 1.1f), (1f, 1f)));
            Fade(pff, 0.4f);
            Junk(root, "Bolt", Bolt, 1, 0.3f, 0.8f, 0.2f, 0.85f, ParticleSystemScalingMode.Local, floor: null, gravity: 1.5f, upward: false);
            Sparks(root, 3, ParticleSystemScalingMode.Local, 0.1f, 1.5f, 3f);
            _kit.Sound(root, "manki_jet_cough", 0.7f, 1f);
            return _kit.Save(root);
        }

        // ── Bazooka (R) ──────────────────────────────────────────────────────────

        /// <summary>Startup tell: a glint at the barrel tip and a clunk-click as he shoulders it.</summary>
        private static GameObject BazookaReady()
        {
            var root = _kit.NewRoot("MankiBazookaReady", attackBound: true);
            var glint = Particles(Frame(root), "Glint", _m.Spark, 0.3f, ParticleSystemSimulationSpace.Local);
            Burst(glint, 1);
            var main = glint.main;
            main.startLifetime = 0.2f;
            main.startSize = 0.42f;
            SizeOverLife(glint, Curve((0f, 0.2f), (0.25f, 1.15f), (1f, 0f)));
            Spin(glint, 220f);
            InFront(glint);
            _kit.Sound(root, "manki_bazooka_ready", 0.6f, 1f);
            return _kit.Save(root);
        }

        /// <summary>
        /// The shot: a flash star and flame out of the muzzle, sparks along the barrel, and a
        /// comically oversized backblast out of the back end, away from Manki.
        /// </summary>
        private static GameObject Muzzle()
        {
            var root = _kit.NewRoot("MankiBazookaMuzzle", attackBound: true);
            var frame = Frame(root);
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            Pop(frame, "Flash", _m.Flash, 1.05f, 0.1f, local, spin: true);
            Halo(frame, 1.2f, 0.1f, Fuse, local);
            Jet(frame, "Flame", _m.FlameJet, Vector3.zero, Vector3.up, 5, 7f, 10f, 0.28f, 0.42f, 0.1f);
            var sparks = Particles(frame, "Sparks", _m.Spark, 0.3f);
            Burst(sparks, 8);
            var main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 10f);
            var shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.05f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Stretch(sparks, 0.03f, 1.5f);
            Fade(sparks, 0.5f);

            var back = new GameObject("Backblast");
            back.transform.SetParent(frame.transform, false);
            back.transform.localPosition = new Vector3(0f, -BarrelLength, 0f);
            Jet(back, "Blast flame", _m.FlameJet, Vector3.zero, Vector3.down, 4, 5f, 7f, 0.32f, 0.45f, 0.1f);
            var puffs = Puffs(back, "Blast puffs", 6, 0.7f, 1.1f, 6f, local, life: 0.4f);
            shape = puffs.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            main = puffs.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 6f);
            puffs.GetComponent<ParticleSystemRenderer>().sortingFudge = 8f;
            _kit.Sound(root, "manki_bazooka_fire", 0.8f, 1f);
            return _kit.Save(root);
        }

        /// <summary>The backblast singes his fur: wisps of smoke and embers curl off his head.</summary>
        private static GameObject Singe()
        {
            var root = _kit.NewRoot("MankiBazookaSinge", attackBound: false);
            var frame = Frame(root, upright: true);
            var wisps = Particles(frame, "Wisps", _m.Puff, 0.45f);
            wisps.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            var main = wisps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = wisps.emission;
            emission.rateOverTime = 14f;
            Sphere(wisps, 0.08f);
            Drift(wisps, 0.15f, 0.7f, 1.1f);
            var noise = wisps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 2f;
            RandomFrame(wisps, 2, 2);
            SizeOverLife(wisps, Curve((0f, 0.6f), (1f, 2f)));
            Fade(wisps, 0.35f);
            var embers = Particles(frame, "Embers", _m.Spark, 0.3f);
            embers.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            Burst(embers, 4);
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            Sphere(embers, 0.08f);
            Fade(embers, 0.5f);
            return _kit.Save(root);
        }

        /// <summary>
        /// The rocket: a red toon rocket that flies point-first (ProjectileVFXManager turns +Y along
        /// the flight), a flame out of the tail and a wobbly smoke trail that lingers after impact.
        /// </summary>
        private static GameObject Rocket()
        {
            var root = _kit.NewRoot("MankiBazookaRocket", attackBound: false);
            var body = Particles(root, "Rocket", _m.Rocket, 1f, ParticleSystemSimulationSpace.Local);
            Burst(body, 1);
            var main = body.main;
            main.startLifetime = 60f;
            main.startSize = 1f;
            var renderer = body.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = _m.RocketMesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            Jet(root, "Exhaust", _m.FlameJetHot, new Vector3(0f, -0.48f, 0f), Vector3.down, 0, 3f, 5f, 0.3f, 0.42f, 0.08f, rate: 40f);

            var trail = new GameObject("Trail");
            trail.transform.SetParent(root.transform, false);
            trail.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            trail.AddComponent<LingerAfterProjectile>().Seconds = 1f;
            var puffs = Particles(trail, "Puffs", _m.Puff, 1f);
            main = puffs.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.32f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 96;
            var emission = puffs.emission;
            emission.rateOverDistance = 9f;
            Sphere(puffs, 0.12f);
            var noise = puffs.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 1.2f;
            noise.scrollSpeed = 0.5f;
            RandomFrame(puffs, 2, 2);
            SizeOverLife(puffs, Curve((0f, 0.5f), (0.2f, 1f), (1f, 1.4f)));
            Fade(puffs, 0.4f);
            var embers = Particles(trail, "Embers", _m.Spark, 1f);
            main = embers.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
            main.maxParticles = 48;
            emission = embers.emission;
            emission.rateOverDistance = 5f;
            Sphere(embers, 0.1f);
            Fade(embers, 0.5f);
            _kit.Sound(root, "manki_rocket_whistle", 0.45f, 1f);
            return _kit.Save(root);
        }

        // ── Round Bomb (A) ───────────────────────────────────────────────────────

        /// <summary>
        /// The lit bomb, held and thrown: the fuse fizzes with sparks, the bomb pulses red, and a
        /// dotted trail of puffs follows the lob and fades after the blast.
        /// </summary>
        private static GameObject LitBomb()
        {
            var root = _kit.NewRoot("MankiLitBomb", attackBound: false);
            var model = _kit.NestModel(root, BombModelPath, "Bomb");
            if (model != null)
            {
                model.transform.localScale = Vector3.one * BombModelScale;
                model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }
            var danger = Particles(root, "Danger", _m.SoftGlow, 1f, ParticleSystemSimulationSpace.Local);
            var main = danger.main;
            main.loop = true;
            main.startLifetime = 0.25f;
            main.startSize = 1.15f;
            main.startColor = new Color(MaskRed.r, MaskRed.g, MaskRed.b, 0.6f);
            var emission = danger.emission;
            emission.rateOverTime = 4f;
            SizeOverLife(danger, Curve((0f, 0.75f), (0.5f, 1f), (1f, 0.8f)));
            var color = danger.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0.2f), (0.4f, 1f), (1f, 0.1f) });
            Behind(danger);

            var fuse = new GameObject("Fuse");
            fuse.transform.SetParent(root.transform, false);
            fuse.transform.localPosition = FuseTip;
            var glow = Particles(fuse, "Fuse glow", _m.Glow, 1f, ParticleSystemSimulationSpace.Local);
            main = glow.main;
            main.loop = true;
            main.startLifetime = 0.16f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startColor = new Color(1f, 0.75f, 0.3f, 0.8f);
            emission = glow.emission;
            emission.rateOverTime = 10f;
            Behind(glow);
            var sparks = Particles(fuse, "Fuse sparks", _m.Spark, 1f);
            main = sparks.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.17f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
            main.gravityModifier = 0.6f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            emission = sparks.emission;
            emission.rateOverTime = 28f;
            Sphere(sparks, 0.02f);
            Spin(sparks, 400f);
            Fade(sparks, 0.5f);
            InFront(sparks);

            var trail = new GameObject("Trail");
            trail.transform.SetParent(root.transform, false);
            trail.AddComponent<LingerAfterProjectile>().Seconds = 0.7f;
            var puffs = Particles(trail, "Puffs", _m.Puff, 1f);
            main = puffs.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.65f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.28f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 48;
            emission = puffs.emission;
            emission.rateOverDistance = 2.2f;
            Sphere(puffs, 0.15f);
            RandomFrame(puffs, 2, 2);
            SizeOverLife(puffs, Curve((0f, 0.6f), (0.3f, 1f), (1f, 1.15f)));
            Fade(puffs, 0.4f);
            _kit.Sound(root, "manki_fuse", 0.4f, 1f);
            return _kit.Save(root);
        }

        // ── Aerosol Inferno (F) ──────────────────────────────────────────────────

        /// <summary>
        /// The charge, at the can's nozzle: the can itself grows, shakes harder and heats red as the
        /// charge builds and throbs when full (<see cref="ChargePropFeedback"/>). It rattles and leaks
        /// mist, and a pilot flame on the nozzle grows one step per charge tier with a spark pop and
        /// a rattle at each of the five. Everything stops on release (<see cref="ChargeTierCues"/>).
        /// </summary>
        private static GameObject AerosolCharge()
        {
            var root = _kit.NewRoot("MankiAerosolCharge", attackBound: true);
            // A silent controller system: stopping it stops every child on release.
            var controller = root.AddComponent<ParticleSystem>();
            controller.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = controller.main;
            main.loop = true;
            main.playOnAwake = true;
            var emission = controller.emission;
            emission.enabled = false;
            var shape = controller.shape;
            shape.enabled = false;
            root.GetComponent<ParticleSystemRenderer>().enabled = false;

            var can = Frame(root);
            var rattle = Particles(can, "Rattle", _m.Rattle, 1f, ParticleSystemSimulationSpace.Local);
            rattle.transform.localPosition = new Vector3(0f, 0f, CanCentre);
            main = rattle.main;
            main.loop = true;
            main.startLifetime = 0.12f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.27f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            emission = rattle.emission;
            emission.rateOverTime = 9f;
            shape = rattle.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            shape.radiusThickness = 0f;
            RandomFrame(rattle, 2, 1);
            var mist = Particles(can, "Mist", _m.Mist, 1f);
            main = mist.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            emission = mist.emission;
            emission.rateOverTime = 7f;
            shape = mist.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.02f;
            shape.rotation = new Vector3(0f, 180f, 0f);
            SizeOverLife(mist, Curve((0f, 0.6f), (1f, 2.2f)));
            Fade(mist, 0.35f);

            var pilotFrame = Frame(root, upright: true);
            var grow = new GameObject("Grow");
            grow.transform.SetParent(pilotFrame.transform, false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var halo = Particles(grow, "Glow", _m.Glow, 1f, ParticleSystemSimulationSpace.Local, scaled);
            main = halo.main;
            main.loop = true;
            main.startLifetime = 0.15f;
            main.startSize = 0.42f;
            main.startColor = new Color(1f, 0.7f, 0.3f, 0.6f);
            emission = halo.emission;
            emission.rateOverTime = 14f;
            halo.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            Behind(halo);
            var pilot = Particles(grow, "Pilot flame", _m.Flame, 1f, ParticleSystemSimulationSpace.Local, scaled);
            pilot.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            main = pilot.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.26f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            emission = pilot.emission;
            emission.rateOverTime = 30f;
            RandomFrame(pilot, 2, 2);
            SizeOverLife(pilot, Curve((0f, 0.6f), (0.35f, 1f), (1f, 0.8f)));

            var tiers = new ParticleSystem[4];
            for (int i = 0; i < 4; i++)
            {
                var pop = Particles(pilotFrame, $"Tier {i + 2}", _m.Spark, 0.4f);
                pop.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                main = pop.main;
                main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.1f + 0.02f * i, 0.18f + 0.03f * i);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f + 0.5f * i);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                Burst(pop, 4 + i);
                Sphere(pop, 0.05f);
                Spin(pop, 300f);
                Fade(pop, 0.5f);
                InFront(pop);
                var psst = Particles(pop.gameObject, "Psst", _m.Mist, 0.4f);
                main = psst.main;
                main.playOnAwake = false;
                main.startLifetime = 0.35f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.22f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
                Burst(psst, 2);
                Sphere(psst, 0.05f);
                SizeOverLife(psst, Curve((0f, 0.5f), (1f, 1.6f)));
                Fade(psst, 0.3f);
                _kit.Sound(pop.gameObject, "manki_can_rattle", 0.5f, 1f + 0.08f * i);
                var sound = pop.GetComponent<AudioSource>();
                if (sound != null) sound.playOnAwake = false;
                tiers[i] = pop;
            }
            var cues = root.AddComponent<ChargeTierCues>();
            cues.Slot = AbilitySlots.F;
            cues.Charging = controller;
            cues.Tier2 = tiers[0];
            cues.Tier3 = tiers[1];
            cues.Tier4 = tiers[2];
            cues.Tier5 = tiers[3];
            cues.Grow = grow.transform;
            cues.GrowPerTier = 0.3f;
            var feedback = root.AddComponent<ChargePropFeedback>();
            feedback.Slot = AbilitySlots.F;
            feedback.Marks = rattle;
            _kit.Sound(root, "manki_can_rattle", 0.35f, 0.9f);
            return _kit.Save(root);
        }

        /// <summary>
        /// The homemade flamethrower's other half: a chunky red lighter, sized like his cartoon spray
        /// can, in his free hand while the stream fires. The fire clip keeps that hand under the can,
        /// so the lighter leans back toward the cap, at an angle measured from the fist, can axis and
        /// cap to clear the can from tick 4 to tick 37 with its flame beside the cap. It shows from
        /// tick 3, sparks fly off the wheel as it flicks on, and as the stream lights at tick 8 its
        /// flame flares toward the cap. The cue ends at tick 39, before the clip swings the can back
        /// through that hand.
        /// </summary>
        private static GameObject Lighter()
        {
            var root = _kit.NewRoot("MankiLighter", attackBound: true);
            var upright = Frame(root, upright: true);
            var aim = new GameObject("Lean");
            aim.transform.SetParent(upright.transform, false);
            aim.transform.localRotation = Quaternion.Euler(-30f, 0f, -10f);
            const float show = 2f / 60f;
            var body = Particles(aim, "Lighter", _m.Rocket, 1f, ParticleSystemSimulationSpace.Local);
            Burst(body, 1);
            var main = body.main;
            main.startDelay = show;
            main.startLifetime = 60f;
            main.startSize = 1f;
            var renderer = body.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = _m.LighterMesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;

            var nozzle = new GameObject("Flame");
            nozzle.transform.SetParent(aim.transform, false);
            nozzle.transform.localPosition = new Vector3(0f, LighterTop + 0.13f, 0f);
            var glow = Particles(nozzle, "Glow", _m.Glow, 1f, ParticleSystemSimulationSpace.Local);
            main = glow.main;
            main.loop = true;
            main.startDelay = show + 0.03f;
            main.startLifetime = 0.15f;
            main.startSize = 0.55f;
            main.startColor = new Color(1f, 0.72f, 0.3f, 0.6f);
            var emission = glow.emission;
            emission.rateOverTime = 14f;
            Behind(glow);
            var flame = Particles(nozzle, "Flame", _m.Flame, 1f, ParticleSystemSimulationSpace.Local);
            main = flame.main;
            main.loop = true;
            main.startDelay = show + 0.03f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.36f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            emission = flame.emission;
            emission.rateOverTime = 28f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
            RandomFrame(flame, 2, 2);
            SizeOverLife(flame, Curve((0f, 0.6f), (0.35f, 1f), (1f, 0.8f)));
            InFront(flame);

            var flick = Particles(aim, "Flick", _m.Spark, 0.3f);
            flick.transform.localPosition = new Vector3(-0.02f, LighterTop - 0.02f, 0f);
            Burst(flick, 6);
            main = flick.main;
            main.startDelay = show;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
            main.gravityModifier = 0.5f;
            Sphere(flick, 0.01f);
            Spin(flick, 400f);
            Fade(flick, 0.5f);
            InFront(flick);

            // Tick 8 (seven ticks after this cue): as the spray lights, the flame flares forward into
            // the stream, away from his face.
            var flareAt = new GameObject("Flare");
            flareAt.transform.SetParent(upright.transform, false);
            flareAt.transform.localPosition = aim.transform.localRotation * nozzle.transform.localPosition;
            var flare = Particles(flareAt, "Flare", _m.Flame, 0.5f);
            main = flare.main;
            main.startDelay = 7f / 60f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.24f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.32f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            Burst(flare, 5);
            var shape = flare.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.03f;
            shape.rotation = new Vector3(-35f, 0f, 0f);
            RandomFrame(flare, 2, 2);
            SizeOverLife(flare, Curve((0f, 0.5f), (0.3f, 1f), (1f, 0.6f)));
            Fade(flare, 0.5f);
            InFront(flare);
            _kit.Sound(root, "manki_lighter", 0.6f, 1f);
            return _kit.Save(root);
        }

        /// <summary>
        /// The fire stream, scaled with the charge tier like the hitbox (0.6x to 1.5x of a 3.2 m by
        /// 1.4 m flame at 1x): toon flame tongues licking forward and up, a hot core, soot rolling
        /// off the top and embers. From tier 4 the nozzle burns white-hot (<see cref="DamageScaleVariant"/>
        /// reads the placement scale).
        /// </summary>
        private static GameObject Inferno()
        {
            var root = _kit.NewRoot("MankiAerosolInferno", attackBound: true);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var soot = Particles(root, "Soot", _m.Soot, 0.7f, ParticleSystemSimulationSpace.Local, scaled);
            var main = soot.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = soot.emission;
            emission.rateOverTime = 16f;
            var shape = soot.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 0.55f, 1.8f);
            shape.scale = new Vector3(0.4f, 0.15f, 2.4f);
            var velocity = soot.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            velocity.y = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            velocity.z = new ParticleSystem.MinMaxCurve(1f, 2f);
            RandomFrame(soot, 2, 2);
            SizeOverLife(soot, Curve((0f, 0.5f), (0.3f, 1f), (1f, 1.2f)));
            Fade(soot, 0.4f);
            soot.GetComponent<ParticleSystemRenderer>().sortingFudge = 6f;

            Tongues(root, "Flames", _m.FlameJet, 70f, 5.5f, 7.5f, 0.38f, 0.5f, 0.65f, 0.95f, 14f, 0.9f, 0f);
            Tongues(root, "Hot flames", _m.FlameJetHot, 40f, 6f, 8f, 0.25f, 0.35f, 0.45f, 0.65f, 9f, 0.6f, -3f);
            var licks = Particles(root, "Licks", _m.Flame, 0.7f, ParticleSystemSimulationSpace.Local, scaled);
            main = licks.main;
            main.startLifetime = 0.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            emission = licks.emission;
            emission.rateOverTime = 14f;
            shape = licks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 0.2f, 2.9f);
            shape.scale = new Vector3(0.6f, 0.8f, 0.5f);
            velocity = licks.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            velocity.z = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            RandomFrame(licks, 2, 2);
            SizeOverLife(licks, Curve((0f, 0.4f), (0.3f, 1f), (1f, 0.5f)));

            var embers = Particles(root, "Embers", _m.Spark, 0.7f, ParticleSystemSimulationSpace.Local, scaled);
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.gravityModifier = -0.2f;
            emission = embers.emission;
            emission.rateOverTime = 30f;
            shape = embers.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.15f;
            Spin(embers, 300f);
            Fade(embers, 0.5f);
            InFront(embers);

            var regular = new GameObject("Regular");
            regular.transform.SetParent(root.transform, false);
            var torch = new GameObject("White-hot");
            torch.transform.SetParent(root.transform, false);
            var core = Particles(torch, "Core", _m.Glow, 0.7f, ParticleSystemSimulationSpace.Local, scaled);
            main = core.main;
            main.startLifetime = 0.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 0.8f);
            main.startColor = new Color(1f, 0.95f, 0.8f, 0.8f);
            emission = core.emission;
            emission.rateOverTime = 40f;
            core.transform.localPosition = new Vector3(0f, 0f, 0.25f);
            Tongues(torch, "Torch", _m.FlameJetHot, 30f, 6f, 8f, 0.12f, 0.18f, 0.5f, 0.7f, 6f, 0.2f, -6f);
            regular.SetActive(false);
            torch.SetActive(false);
            var variant = root.AddComponent<DamageScaleVariant>();
            variant.HeavyFromScale = 1.2f;
            variant.Regular = regular;
            variant.Heavy = torch;
            _kit.Sound(root, "manki_spray_flame", 0.8f, 1f);
            return _kit.Save(root);
        }

        /// <summary>A victim caught in the flame: a soot puff, licks of flame and rising embers.</summary>
        private static GameObject InfernoHit()
        {
            var root = _kit.NewRoot("MankiAerosolHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, 0.9f, 0.12f, Blast, scaled);
            Puffs(root, "Soot", 2, 0.55f, 0.7f, 0.6f, scaled, life: 0.55f, material: _m.Soot);
            var licks = Particles(root, "Licks", _m.Flame, 0.4f, scaling: scaled);
            Burst(licks, 3);
            var main = licks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.32f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            Sphere(licks, 0.2f);
            Drift(licks, 0.2f, 1.5f, 2.5f);
            RandomFrame(licks, 2, 2);
            SizeOverLife(licks, Curve((0f, 0.4f), (0.3f, 1f), (1f, 0.6f)));
            InFront(licks);
            var embers = Particles(root, "Embers", _m.Spark, 0.6f, scaling: scaled);
            Burst(embers, 7);
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.gravityModifier = -0.3f;
            Sphere(embers, 0.25f);
            Fade(embers, 0.5f);
            return _kit.Save(root);
        }

        // ── Shared layers ────────────────────────────────────────────────────────

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
            var floor = new GameObject("Floor");
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, fallbackY, 0f);
            floor.AddComponent<SnapToFloor>().MaxDrop = maxDrop;
            return floor;
        }

        /// <summary>A shock ring on the floor that grows to <paramref name="diameter"/>: the real blast area.</summary>
        private static void FloorRing(GameObject floor, float diameter, float lifetime)
        {
            var ring = Particles(floor, "Ring", _m.Ring, 0.5f);
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = lifetime;
            main.startSize = diameter;
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ring.GetComponent<ParticleSystemRenderer>().maxParticleSize = 10f;
            SizeOverLife(ring, Curve((0f, 0.15f), (0.6f, 0.95f), (1f, 1f)));
            Fade(ring, 0.4f);
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

        /// <summary>A single shape that pops large, settles and fades: impacts and flashes.</summary>
        private static ParticleSystem Pop(GameObject root, string name, Material material, float size, float lifetime,
            ParticleSystemScalingMode scaling, bool spin, float fudge = -10f)
        {
            var pop = Particles(root, name, material, 0.5f, scaling: scaling);
            Burst(pop, 1);
            var main = pop.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            if (spin) main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(pop, Curve((0f, 0.5f), (0.12f, 1.12f), (0.4f, 1f), (1f, 0.85f)));
            Fade(pop, 0.7f);
            pop.GetComponent<ParticleSystemRenderer>().sortingFudge = fudge;
            return pop;
        }

        /// <summary>Toon fireballs from the 2x2 sheet that swell, hold and shrink away.</summary>
        private static ParticleSystem Fireballs(GameObject root, int count, float minSize, float maxSize, float lifetime,
            ParticleSystemScalingMode scaling)
        {
            var fire = Particles(root, "Fireball", _m.Fireball, 0.6f, scaling: scaling);
            Burst(fire, count);
            var main = fire.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Sphere(fire, 0.2f * maxSize);
            RandomFrame(fire, 2, 2);
            SizeOverLife(fire, Curve((0f, 0.45f), (0.08f, 1.1f), (0.3f, 1f), (1f, 0.82f)));
            Fade(fire, 0.55f);
            fire.GetComponent<ParticleSystemRenderer>().sortingFudge = 6f;
            return fire;
        }

        /// <summary>Chunky smoke puffs thrown outward that slow down, swell and clear in about a second.</summary>
        private static ParticleSystem Puffs(GameObject root, string name, int count, float minSize, float maxSize, float speed,
            ParticleSystemScalingMode scaling, float ring = 0.15f, float life = 0.7f, Material material = null)
        {
            var puffs = Particles(root, name, material ?? _m.Puff, life + 0.4f, scaling: scaling);
            Burst(puffs, count);
            var main = puffs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.08f;
            Sphere(puffs, ring);
            Drag(puffs, 0.2f);
            RandomFrame(puffs, 2, 2);
            SizeOverLife(puffs, Curve((0f, 0.4f), (0.2f, 1f), (1f, 1.15f)));
            Fade(puffs, 0.45f);
            puffs.GetComponent<ParticleSystemRenderer>().sortingFudge = 12f;
            return puffs;
        }

        /// <summary>
        /// Gadget junk (bolts, nuts, springs, gears; <paramref name="frame"/> −1 for a random mix)
        /// tumbling out with gravity. With a floor it bounces once and settles there.
        /// </summary>
        private static void Junk(GameObject root, string name, int frame, int count, float minSpeed, float maxSpeed, float size,
            float lifetime, ParticleSystemScalingMode scaling, Transform floor, float gravity = 1.8f, bool upward = true)
        {
            var junk = Particles(root, name, _m.Junk, lifetime + 0.2f, scaling: scaling);
            Burst(junk, count);
            var main = junk.main;
            main.startLifetime = lifetime;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            var shape = junk.shape;
            shape.enabled = true;
            shape.shapeType = upward ? ParticleSystemShapeType.Cone : ParticleSystemShapeType.Sphere;
            shape.angle = 55f;
            shape.radius = 0.15f;
            if (upward) shape.rotation = new Vector3(-90f, 0f, 0f);
            Spin(junk, 520f);
            var sheet = junk.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = frame < 0 ? new ParticleSystem.MinMaxCurve(0f, 0.9999f) : new ParticleSystem.MinMaxCurve(frame / 4f + 0.01f);
            Fade(junk, 0.75f);
            junk.GetComponent<ParticleSystemRenderer>().sortingFudge = -5f;
            if (floor == null) return;
            var collision = junk.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.Planes;
            collision.SetPlane(0, floor);
            collision.bounce = 0.35f;
            collision.dampen = 0.45f;
            collision.radiusScale = 0.4f;
        }

        private static void Sparks(GameObject root, int count, ParticleSystemScalingMode scaling, float radius = 0.1f,
            float minSpeed = 1.5f, float maxSpeed = 3.5f)
        {
            var sparks = Particles(root, "Sparks", _m.Spark, 0.6f, scaling: scaling);
            Burst(sparks, count);
            var main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.8f;
            Sphere(sparks, radius);
            Drag(sparks, 0.25f);
            Spin(sparks, 300f);
            SizeOverLife(sparks, Curve((0f, 1f), (1f, 0.2f)));
            Fade(sparks, 0.6f);
            sparks.GetComponent<ParticleSystemRenderer>().sortingFudge = -8f;
        }

        private static void SmokePuffs(GameObject root, int count, float size, float speed)
        {
            var puffs = Particles(root, "Smoke", _m.Puff, 0.5f);
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

        /// <summary>Stretched flame tongues shot along <paramref name="direction"/> (local), drawn tip-first.</summary>
        private static void Jet(GameObject root, string name, Material material, Vector3 position, Vector3 direction, int burst,
            float minSpeed, float maxSpeed, float minSize, float maxSize, float lifetime, float rate = 0f)
        {
            var jet = Particles(root, name, material, rate > 0f ? 1f : 0.2f, ParticleSystemSimulationSpace.Local);
            jet.transform.localPosition = position;
            var main = jet.main;
            main.loop = rate > 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.75f, lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            if (burst > 0) Burst(jet, burst);
            var emission = jet.emission;
            emission.rateOverTime = rate;
            var shape = jet.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.04f;
            shape.rotation = Quaternion.FromToRotation(Vector3.forward, direction).eulerAngles;
            // The quad trails behind each particle: start ahead and keep it short, so the flame
            // never reaches back over the barrel or Manki.
            shape.position = direction * 0.18f;
            Stretch(jet, 0f, 1.15f);
            SizeOverLife(jet, Curve((0f, 0.35f), (0.3f, 1f), (1f, 0.7f)));
            Fade(jet, 0.6f);
        }

        /// <summary>The F stream's forward-licking flame tongues, emitted from the nozzle edge along +Z.</summary>
        private static void Tongues(GameObject root, string name, Material material, float rate, float minSpeed, float maxSpeed,
            float minLife, float maxLife, float minSize, float maxSize, float angle, float rise, float fudge)
        {
            var tongues = Particles(root, name, material, 0.7f, ParticleSystemSimulationSpace.Local, ParticleSystemScalingMode.Hierarchy);
            var main = tongues.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.maxParticles = 96;
            var emission = tongues.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate, Curve((0f, 1f), (0.85f, 1f), (1f, 0.4f)));
            var shape = tongues.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = 0.18f;
            shape.position = new Vector3(0f, 0f, 0.3f);
            var velocity = tongues.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(rise * 0.6f, rise);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            // Each tongue trails behind its particle; grow it with distance so none reach back past the nozzle.
            Stretch(tongues, 0f, 1.25f);
            SizeOverLife(tongues, Curve((0f, 0.2f), (0.5f, 0.85f), (1f, 1.25f)));
            Fade(tongues, 0.6f);
            tongues.GetComponent<ParticleSystemRenderer>().sortingFudge = fudge;
        }

        /// <summary>A world-space particle ribbon behind a moving limb (see FightGuy's builder).</summary>
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

        private static Materials BuildMaterials() => new()
        {
            Glow = _kit.ParticleMaterial("MankiGlowAdd", _kit.BakeTexture("MankiGlow", 128, 128,
                (u, v) => Alpha(Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f))), new Color(1.3f, 1.3f, 1.3f), additive: true),
            SoftGlow = _kit.ParticleMaterial("MankiSoftGlow", _kit.BakeTexture("MankiSoftGlow", 128, 128,
                (u, v) => Alpha(Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 1.6f) * Step(0f, 0.25f, Radius(u, v)))), Color.white),
            Fireball = Mat("MankiFireball", 512, 512, (u, v) => Sheet(u, v, FireballPixel)),
            Flash = Mat("MankiFlash", 256, 256, (u, v) => FlashPixel(P(u, v))),
            Pow = Mat("MankiPow", 256, 256, (u, v) => PowPixel(P(u, v))),
            Puff = Mat("MankiPuff", 512, 512, (u, v) => Sheet(u, v, (p, cell) => PuffPixel(p, cell, SmokeRim, Smoke, SmokeLight))),
            Soot = Mat("MankiSoot", 512, 512, (u, v) => Sheet(u, v, (p, cell) => PuffPixel(p, cell + 10, Soot, SootMid, SootLight))),
            Mist = Mat("MankiMist", 128, 128, (u, v) => MistPixel(P(u, v))),
            Junk = Mat("MankiJunk", 512, 512, (u, v) => Sheet(u, v, JunkPixel)),
            Ring = Mat("MankiRing", 256, 256, (u, v) => ToonShapes.Bands((Mathf.Abs(Radius(u, v) - 0.86f) - 0.045f) * 128f, 2.5f, 99f, Soot, Cream, Cream)),
            Scorch = Mat("MankiScorch", 256, 256, (u, v) => ScorchPixel(P(u, v))),
            Spark = Mat("MankiSpark", 128, 128, (u, v) => SparkPixel(P(u, v))),
            Flame = Mat("MankiFlame", 512, 512, (u, v) => Sheet(u, v, FlamePixel)),
            FlameJet = Mat("MankiFlameJet", 256, 128, (u, v) => FlameJetPixel(u, v, hot: false)),
            FlameJetHot = Mat("MankiFlameJetHot", 256, 128, (u, v) => FlameJetPixel(u, v, hot: true)),
            Grin = Mat("MankiGrin", 512, 512, (u, v) => GrinPixel(P(u, v))),
            Ribbon = Mat("MankiRibbon", 128, 64, (u, v) => RibbonPixel(u, v, Soot, Blast, Fuse)),
            RibbonHeavy = Mat("MankiRibbonHeavy", 128, 64, (u, v) => RibbonPixel(u, v, Soot, Blast, Cream)),
            Rattle = Mat("MankiRattle", 256, 128, RattlePixel),
            Rocket = RocketMaterial(),
            RocketMesh = RocketMesh("MankiRocketMesh"),
            LighterMesh = LighterMesh("MankiLighterMesh"),
        };

        private static Material Mat(string name, int width, int height, System.Func<float, float, Color> pixel)
            => _kit.ParticleMaterial(name, _kit.BakeTexture(name, width, height, pixel));

        /// <summary>An opaque unlit material for the rocket mesh, so its faces sort correctly.</summary>
        private static Material RocketMaterial()
        {
            var texture = _kit.BakeTexture("MankiRocket", 64, 64, RocketPixel);
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? throw new System.InvalidOperationException("URP Unlit is unavailable.");
            string path = $"{_kit.Root}/MankiRocket.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(shader);
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            // Double-sided: the fins are single quads; depth sorts the rest.
            material.SetFloat("_Cull", 0f);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

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

        private static float Ellipse(Vector2 p, Vector2 c, float a, float b)
            => (new Vector2((p.x - c.x) / a, (p.y - c.y) / b).magnitude - 1f) * Mathf.Min(a, b);

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

        /// <summary>A toon fireball: soot rim, ember and orange outside, a yellow body and a cream-hot core.</summary>
        private static Color FireballPixel(Vector2 p, int cell)
        {
            int seed = 11 + cell * 7;
            const float s = 128f;
            Color color = ToonShapes.Bands(Cloud(p, new Vector2(0f, -0.02f), 0.86f, 9, seed, 0.85f) * s, 4f, 9f, Soot, Ember, Blast);
            color = Over(color, ToonShapes.Bands(Cloud(p, new Vector2(0.02f, 0f), 0.66f, 8, seed + 1, 0.85f) * s, 0f, 9f, Blast, Blast, Fuse));
            return Over(color, Flat(Cloud(p, new Vector2(-0.04f, 0.05f), 0.34f, 6, seed + 2, 0.85f) * s, Cream));
        }

        private static readonly Dictionary<int, Vector2[]> Stars = new();

        private static float Star(Vector2 p, int spikes, float inner, int seed)
        {
            if (!Stars.TryGetValue(seed, out var outline))
                Stars[seed] = outline = StarOutline(spikes, inner, seed);
            return Polygon(p, outline);
        }

        private static Color FlashPixel(Vector2 p)
        {
            Color color = ToonShapes.Bands(Star(p, 13, 0.5f, 31) * 128f, 3f, 12f, Blast, Fuse, Cream);
            return Over(color, Flat(Circle(p, Vector2.zero, 0.3f) * 128f, Color.white));
        }

        private static Color PowPixel(Vector2 p)
        {
            Color color = ToonShapes.Bands(Star(p, 10, 0.52f, 41) * 128f, 4f, 10f, Soot, Blast, Fuse);
            return Over(color, Flat(Circle(p, Vector2.zero, 0.24f) * 128f, Cream));
        }

        /// <summary>A smoke puff with a lighter top-left and halftone dots in its lower-right shade.</summary>
        private static Color PuffPixel(Vector2 p, int cell, Color rim, Color body, Color light)
        {
            int seed = 51 + cell * 5;
            const float s = 128f;
            float puff = Cloud(p, Vector2.zero, 0.84f, 7, seed, 0.8f);
            Color color = ToonShapes.Bands(puff * s, 3.5f, 99f, rim, body, body);
            float lit = Mathf.Max(Cloud(p, new Vector2(-0.14f, 0.17f), 0.6f, 6, seed + 3, 0.8f), puff + 0.07f);
            color = Over(color, Flat(lit * s, light));
            float shade = Mathf.Max(puff + 0.05f, -Cloud(p, new Vector2(-0.18f, 0.22f), 0.8f, 7, seed, 0.8f));
            const float period = 0.14f;
            Vector2 g = new(Mathf.Repeat(p.x, period) - period / 2f, Mathf.Repeat(p.y, period) - period / 2f);
            float dots = Mathf.Max(g.magnitude - period * 0.27f, shade);
            Color dot = rim;
            dot.a = Mathf.Clamp01(0.5f - dots * s) * 0.55f;
            return Over(color, dot);
        }

        private static Color MistPixel(Vector2 p)
            => ToonShapes.Bands(Cloud(p, Vector2.zero, 0.84f, 6, 71, 0.85f) * 64f, 1.8f, 4f, MistRim, Mist, Color.white);

        /// <summary>Gadget junk: a bolt, a nut, a spring and a gear in steel toon bands.</summary>
        private static Color JunkPixel(Vector2 p, int cell)
        {
            const float s = 128f;
            switch (cell)
            {
                case Bolt:
                {
                    var q = Rotate(p, -25f);
                    float head = Polygon(q, Hexagon(new Vector2(-0.5f, 0f), 0.34f));
                    float shaft = Box(q - new Vector2(0.22f, 0f), new Vector2(0.52f, 0.14f));
                    Color color = ToonShapes.Bands(Mathf.Min(head, shaft) * s, 4f, 99f, SteelRim, Steel, Steel);
                    for (int k = 0; k < 4; k++)
                        color = Over(color, Flat(Capsule(q, new Vector2(0.05f + 0.17f * k, -0.13f), new Vector2(0.12f + 0.17f * k, 0.13f), 0.022f, 0.022f) * s, SteelRim));
                    return Over(color, Flat(Capsule(q, new Vector2(-0.62f, 0.12f), new Vector2(-0.42f, 0.2f), 0.035f, 0.035f) * s, SteelLight));
                }
                case Nut:
                {
                    float nut = Mathf.Max(Polygon(p, Hexagon(Vector2.zero, 0.66f)), -Circle(p, Vector2.zero, 0.3f));
                    return ToonShapes.Bands(nut * s, 4f, 14f, SteelRim, Steel, SteelLight);
                }
                case Spring:
                {
                    var points = new Vector2[9];
                    for (int i = 0; i < 9; i++)
                        points[i] = Rotate(new Vector2(-0.78f + 1.56f * i / 8f, i == 0 || i == 8 ? 0f : (i % 2 == 1 ? 0.32f : -0.32f)), 30f);
                    return ToonShapes.Bands(Polyline(p, points, 0.075f, 0.075f) * s, 3.5f, 4f, SteelRim, Steel, SteelLight);
                }
                default:
                {
                    float gear = Mathf.Max(Polygon(p, GearOutline), -Circle(p, Vector2.zero, 0.24f));
                    return ToonShapes.Bands(gear * s, 4f, 14f, SteelRim, Steel, SteelLight);
                }
            }
        }

        private static Vector2[] Hexagon(Vector2 c, float r)
            => Enumerable.Range(0, 6).Select(k => c + new Vector2(Mathf.Cos(k * Mathf.PI / 3f + Mathf.PI / 6f), Mathf.Sin(k * Mathf.PI / 3f + Mathf.PI / 6f)) * r).ToArray();

        private static readonly Vector2[] GearOutline = Enumerable.Range(0, 32).Select(k =>
        {
            float a = k / 32f * Mathf.PI * 2f, r = (k % 4) is 1 or 2 ? 0.8f : 0.6f;
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }).ToArray();

        /// <summary>A scorch: a soft dark star on the floor with glowing ember cracks.</summary>
        private static Color ScorchPixel(Vector2 p)
        {
            float star = Star(p, 14, 0.62f, 61);
            Color color = Soot;
            color.a = (1f - Step(-0.12f, 0.05f, star)) * 0.85f;
            float centre = (p.magnitude - 0.36f);
            color.a = Mathf.Max(color.a, (1f - Step(-0.1f, 0.08f, centre)) * 0.95f);
            var random = new System.Random(5);
            float crack = float.MaxValue;
            for (int k = 0; k < 7; k++)
            {
                float a = (k / 7f + (float)random.NextDouble() * 0.06f) * Mathf.PI * 2f;
                float bend = ((float)random.NextDouble() - 0.5f) * 0.25f;
                float end = Mathf.Lerp(0.55f, 0.82f, (float)random.NextDouble());
                var points = new[] { new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.1f,
                    new Vector2(Mathf.Cos(a + bend), Mathf.Sin(a + bend)) * 0.38f, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * end };
                crack = Mathf.Min(crack, Polyline(p, points, 0.03f, 0.008f));
            }
            return Over(color, ToonShapes.Bands(crack * 128f, 0.8f, 1.2f, Ember, Blast, Fuse));
        }

        private static Color SparkPixel(Vector2 p)
        {
            var q = Rotate(p, 45f);
            float d = Mathf.Min(Mathf.Min(Rhombus(p, new Vector2(0.95f, 0.13f)), Rhombus(p, new Vector2(0.13f, 0.95f))),
                Mathf.Min(Rhombus(q, new Vector2(0.5f, 0.09f)), Rhombus(q, new Vector2(0.09f, 0.5f))));
            d = Mathf.Min(d, p.magnitude - 0.17f);
            return ToonShapes.Bands(d * 64f, 1.6f, 2f, Ember, Fuse, Color.white);
        }

        /// <summary>
        /// A toon flame tongue: base at the bottom, tip at the top, wavy and leaning, with a side
        /// lick, in soot, ember, orange, yellow and a cream core.
        /// </summary>
        private static Color FlamePixel(Vector2 p, int cell)
        {
            var random = new System.Random(81 + cell);
            float lean = ((float)random.NextDouble() - 0.5f) * 0.4f, phase = (float)random.NextDouble() * 6f;
            float outer = TongueDistance(p, new Vector2(0f, -0.88f), new Vector2(lean, 0.92f), 0.46f, phase, 0.2f);
            float lick = TongueDistance(p, new Vector2(cell % 2 == 0 ? 0.24f : -0.24f, -0.3f), new Vector2(cell % 2 == 0 ? 0.62f : -0.62f, 0.38f), 0.16f, phase + 2f, 0.1f);
            outer = Mathf.Min(outer, lick);
            float inner = TongueDistance(p, new Vector2(0f, -0.78f), new Vector2(lean * 0.6f, 0.45f), 0.3f, phase + 1f, 0.15f);
            float core = TongueDistance(p, new Vector2(0f, -0.7f), new Vector2(lean * 0.3f, 0.05f), 0.16f, phase + 2f, 0.1f);
            const float s = 128f;
            Color color = ToonShapes.Bands(outer * s, 4f, 8f, Soot, Ember, Blast);
            color = Over(color, ToonShapes.Bands(inner * s, 0f, 8f, Blast, Blast, Fuse));
            return Over(color, Flat(core * s, Cream));
        }

        /// <summary>Signed distance to a flame tongue from base a to tip b, widest a third of the way.</summary>
        private static float TongueDistance(Vector2 p, Vector2 a, Vector2 b, float width, float phase, float wiggle)
        {
            Vector2 axis = b - a;
            float length = axis.magnitude;
            Vector2 along = axis / length, across = new(-along.y, along.x);
            Vector2 d = p - a;
            float t = Vector2.Dot(d, along) / length, x = Vector2.Dot(d, across);
            if (t < -0.2f || t > 1.1f) return Mathf.Max(-t * length, (t - 1f) * length, Mathf.Abs(x) - width);
            float tc = Mathf.Clamp01(t);
            float profile = tc < 0.33f ? Mathf.Sin(Mathf.PI * Mathf.Min(1f, tc * 1.5f + 0.12f)) : Mathf.Pow((1f - tc) / 0.67f, 0.9f);
            float half = Mathf.Max(0f, profile * width * (1f + wiggle * Mathf.Sin(tc * 11f + phase)));
            float bend = Mathf.Sin(tc * Mathf.PI * 1.2f + phase) * width * 0.3f * tc;
            float side = Mathf.Abs(x - bend) - half;
            float ends = Mathf.Max(-t * length, (t - 1f) * length);
            return Mathf.Max(side, ends) * 0.85f;
        }

        /// <summary>
        /// A flame tongue lying along U for stretched particles. A stretched billboard's head is the
        /// particle and its quad trails behind it, with U = 0 at the head, so the tip sits at U = 0.
        /// </summary>
        private static Color FlameJetPixel(float u, float v, bool hot)
        {
            var p = new Vector2(u * 2f - 1f, (v * 2f - 1f) * 0.5f);
            float outer = TongueDistance(p, new Vector2(0.95f, 0f), new Vector2(-0.97f, 0.04f), 0.42f, 1.3f, 0.18f);
            float inner = TongueDistance(p, new Vector2(0.9f, 0f), new Vector2(-0.45f, 0.02f), 0.26f, 2.1f, 0.12f);
            const float s = 128f;
            if (hot)
            {
                Color warm = ToonShapes.Bands(outer * s, 2.5f, 6f, Blast, Fuse, Fuse);
                return Over(warm, Flat(inner * s, Cream));
            }
            Color color = ToonShapes.Bands(outer * s, 3f, 6f, Soot, Ember, Blast);
            return Over(color, ToonShapes.Bands(inner * s, 0f, 6f, Blast, Blast, Fuse));
        }

        /// <summary>Two wavy shake marks for the rattling can (2x1 sheet, mirrored).</summary>
        private static Color RattlePixel(float u, float v)
        {
            bool right = u >= 0.5f;
            var p = new Vector2((u % 0.5f) * 4f - 1f, v * 2f - 1f);
            if (right) p.x = -p.x;
            float d = float.MaxValue;
            for (int k = 0; k < 2; k++)
            {
                var points = new Vector2[9];
                for (int i = 0; i < 9; i++)
                {
                    float y = -0.75f + 1.5f * i / 8f;
                    points[i] = new Vector2(-0.25f + 0.45f * k + 0.13f * Mathf.Sin(y * Mathf.PI * 1.6f), y * (1f - 0.2f * k));
                }
                d = Mathf.Min(d, Polyline(p, points, 0.07f, 0.07f));
            }
            return ToonShapes.Bands(d * 64f, 2.5f, 99f, Soot, Cream, Cream);
        }

        /// <summary>A trail band across V with a dark rim, fading toward U = 1 (FightGuy's ribbon shape).</summary>
        private static Color RibbonPixel(float u, float v, Color rim, Color accent, Color core)
        {
            float across = Mathf.Abs(v * 2f - 1f);
            Color color = ToonShapes.Bands((across - 0.9f) * 32f, 2f, 6f, rim, accent, core);
            color.a *= Mathf.Min(Mathf.Clamp01(u / 0.06f), Mathf.Clamp01((1f - u) / 0.45f));
            return color;
        }

        /// <summary>
        /// Manki's grin as a smoke cloud: round head, ears and horns, his red mask glowing like
        /// embers, a wide eye and a wink, a toothy grin and the tongue out. Drawn in his own face
        /// coordinates (y down, head radius 1) and fitted into the texture.
        /// </summary>
        private static Color GrinPixel(Vector2 p)
        {
            const float fit = 1f / 1.55f, lift = 0.23f, s = 256f * fit;
            var q = new Vector2(p.x / fit, -p.y / fit - lift);
            float silhouette = Mathf.Min(Mathf.Min(Mathf.Min(Circle(q, Vector2.zero, 1f), Circle(q, new Vector2(-0.7f, 0.4f), 0.5f)),
                    Mathf.Min(Circle(q, new Vector2(0.7f, 0.4f), 0.5f), Circle(q, new Vector2(0f, 0.62f), 0.6f))),
                Mathf.Min(Mathf.Min(Circle(q, new Vector2(-1.12f, -0.06f), 0.34f), Circle(q, new Vector2(1.12f, -0.06f), 0.34f)),
                    Mathf.Min(Mathf.Min(Circle(q, new Vector2(-0.52f, -0.74f), 0.34f), Circle(q, new Vector2(0f, -0.9f), 0.36f)),
                        Mathf.Min(Circle(q, new Vector2(0.52f, -0.74f), 0.34f), Mathf.Min(Circle(q, new Vector2(-0.95f, 0.5f), 0.3f), Circle(q, new Vector2(0.95f, 0.52f), 0.28f))))));
            foreach (float side in new[] { -1f, 1f })
                silhouette = Mathf.Min(silhouette, Polyline(q, new[] { new Vector2(0.5f * side, -0.7f), new Vector2(0.78f * side, -1.12f),
                    new Vector2(0.92f * side, -1.42f), new Vector2(0.82f * side, -1.68f) }, 0.2f, 0.05f));
            Color color = ToonShapes.Bands(silhouette * s, 0.05f * s, 0.14f * s, SmokeRim, Smoke, SmokeLight);

            float shade = Mathf.Max(silhouette + 0.05f, SilhouetteShift(q));
            const float period = 0.11f;
            Vector2 g = new(Mathf.Repeat(q.x, period) - period / 2f, Mathf.Repeat(q.y, period) - period / 2f);
            Color dot = SmokeRim;
            dot.a = Mathf.Clamp01(0.5f - Mathf.Max(g.magnitude - period * 0.27f, shade) * s) * 0.5f;
            color = Over(color, dot);

            float mask = Mathf.Min(Mathf.Min(Circle(q, new Vector2(-0.36f, -0.14f), 0.37f), Circle(q, new Vector2(0.38f, -0.15f), 0.33f)),
                Capsule(q, new Vector2(-0.36f, -0.16f), new Vector2(0.38f, -0.16f), 0.2f, 0.2f));
            color = Over(color, ToonShapes.Bands(mask * s, 0.03f * s, 0.04f * s, Soot, MaskRed, Ember));
            color = Over(color, ToonShapes.Bands(Circle(q, new Vector2(-0.36f, -0.14f), 0.24f) * s, 0.02f * s, 99f, Soot, Cream, Cream));
            color = Over(color, Flat(Circle(q, new Vector2(-0.31f, -0.1f), 0.085f) * s, Soot));
            color = Over(color, Flat(Polyline(q, new[] { new Vector2(0.18f, -0.06f), new Vector2(0.38f, -0.25f), new Vector2(0.58f, -0.06f) }, 0.06f, 0.06f) * s, Soot));
            float mouth = Mathf.Max(Ellipse(q, new Vector2(0f, 0.28f), 0.68f, 0.5f), 0.28f - q.y);
            color = Over(color, Flat(mouth * s, Soot));
            color = Over(color, Flat(Mathf.Max(mouth + 0.045f, q.y - 0.43f) * s, Cream));
            for (int k = -4; k <= 4; k++)
                color = Over(color, Flat(Capsule(q, new Vector2(k * 0.13f, 0.3f), new Vector2(k * 0.13f, 0.44f), 0.014f, 0.014f) * s, Soot));
            float tongue = Mathf.Min(Capsule(q, new Vector2(0.3f, 0.66f), new Vector2(0.33f, 0.98f), 0.15f, 0.15f), Circle(q, new Vector2(0.31f, 0.66f), 0.16f));
            color = Over(color, ToonShapes.Bands(tongue * s, 0.03f * s, 99f, Soot, Tongue, Tongue));
            return Over(color, Flat(Capsule(q, new Vector2(0.31f, 0.7f), new Vector2(0.325f, 0.92f), 0.018f, 0.018f) * s, MaskDark));
        }

        /// <summary>The grin silhouette shifted up and left, whose outside is the lower-right shade.</summary>
        private static float SilhouetteShift(Vector2 q)
        {
            var o = q + new Vector2(0.16f, 0.2f);
            return -Mathf.Min(Mathf.Min(Circle(o, Vector2.zero, 1.08f), Circle(o, new Vector2(0f, 0.62f), 0.68f)),
                Mathf.Min(Circle(o, new Vector2(-0.7f, 0.4f), 0.58f), Circle(o, new Vector2(0.7f, 0.4f), 0.58f)));
        }

        // The lighter's hood top in its mesh (metres above the grip at the origin).
        private const float LighterTop = 0.17f;

        /// <summary>
        /// A chunky disposable lighter along +Y in metres, held at the origin, at the cartoon scale
        /// of his spray can: a red body with chamfered edges, a steel hood and a dark spark wheel.
        /// UVs pick the rocket atlas's colour columns, with V carrying a fixed light for toon shading.
        /// </summary>
        private static Mesh LighterMesh(string name)
        {
            const float halfWidth = 0.05f, halfDepth = 0.028f, chamfer = 0.014f, bottom = -0.15f, shoulder = 0.12f;
            var light = new Vector3(0.45f, 0.35f, 0.82f).normalized;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            Vector3[] Ring(float shrink) => new[]
            {
                new Vector3(halfWidth, 0f, -halfDepth + chamfer), new Vector3(halfWidth, 0f, halfDepth - chamfer),
                new Vector3(halfWidth - chamfer, 0f, halfDepth), new Vector3(-halfWidth + chamfer, 0f, halfDepth),
                new Vector3(-halfWidth, 0f, halfDepth - chamfer), new Vector3(-halfWidth, 0f, -halfDepth + chamfer),
                new Vector3(-halfWidth + chamfer, 0f, -halfDepth), new Vector3(halfWidth - chamfer, 0f, -halfDepth),
            }.Select(v => new Vector3(v.x * shrink, 0f, v.z * shrink)).ToArray();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float column)
            {
                var normal = Vector3.Cross(b - a, d - a).normalized;
                float lit = 0.5f + 0.5f * Mathf.Abs(Vector3.Dot(normal, light));
                int k = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                for (int i = 0; i < 4; i++) uvs.Add(new Vector2(column, lit));
                triangles.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            }
            void Prism(Vector3[] ring, float y0, float y1, float column)
            {
                for (int i = 0; i < ring.Length; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Length];
                    Quad(a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1, column);
                }
                foreach (float y in new[] { y0, y1 })
                {
                    int centre = vertices.Count;
                    vertices.Add(Vector3.up * y);
                    uvs.Add(new Vector2(column, y > 0f ? 0.95f : 0.4f));
                    for (int i = 0; i < ring.Length; i++)
                    {
                        vertices.Add(ring[i] + Vector3.up * y);
                        uvs.Add(new Vector2(column, y > 0f ? 0.95f : 0.4f));
                    }
                    for (int i = 0; i < ring.Length; i++)
                        triangles.AddRange(new[] { centre, centre + 1 + i, centre + 1 + (i + 1) % ring.Length });
                }
            }
            Prism(Ring(1f), bottom, shoulder, 0.125f);
            Prism(Ring(0.9f), shoulder, LighterTop, 0.625f);
            const int sides = 10;
            const float wheelRadius = 0.02f;
            var wheelCentre = new Vector3(-halfWidth * 0.35f, shoulder + 0.026f, 0f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = wheelCentre + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * wheelRadius;
                var p1 = wheelCentre + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * wheelRadius;
                var depth = Vector3.forward * halfDepth * 0.7f;
                Quad(p0 - depth, p1 - depth, p1 + depth, p0 + depth, 0.875f);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        /// <summary>Colour columns for the rocket mesh (body, band, nose, fins), toon-shaded along V.</summary>
        private static Color RocketPixel(float u, float v)
        {
            Color baseColor = u < 0.25f ? MaskRed : u < 0.5f ? Fuse : u < 0.75f ? SteelLight : new Color(0.33f, 0.35f, 0.38f);
            float shade = v < 0.35f ? 0.55f : v < 0.85f ? 1f : 1.25f;
            var c = baseColor * shade;
            c.a = 1f;
            return c;
        }

        /// <summary>
        /// A toon rocket along +Y in metres: four fins at the tail, a red body with a yellow band and
        /// a steel nose cone. UV U picks the colour column; V carries a fixed light for toon shading.
        /// </summary>
        private static Mesh RocketMesh(string name)
        {
            const int sides = 12;
            const float radius = 0.11f, tail = -0.42f, bandLow = 0.02f, bandHigh = 0.1f, noseBase = 0.2f, tip = 0.44f;
            var light = new Vector3(0.45f, 0.35f, 0.82f).normalized;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float Lit(Vector3 normal) => 0.5f + 0.5f * Vector3.Dot(normal.normalized, light);
            void Band(float y0, float y1, float r0, float r1, float column)
            {
                int start = vertices.Count;
                for (int i = 0; i <= sides; i++)
                {
                    float a = i / (float)sides * Mathf.PI * 2f;
                    var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var normal = radial * (y1 - y0) + Vector3.up * (r0 - r1);
                    vertices.Add(radial * r0 + Vector3.up * y0);
                    vertices.Add(radial * r1 + Vector3.up * y1);
                    float lit = Lit(normal);
                    uvs.Add(new Vector2(column, lit));
                    uvs.Add(new Vector2(column, lit));
                    if (i == sides) continue;
                    int k = start + i * 2;
                    triangles.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 });
                }
            }
            Band(tail, bandLow, radius, radius, 0.125f);
            Band(bandLow, bandHigh, radius * 1.03f, radius * 1.03f, 0.375f);
            Band(bandHigh, noseBase, radius, radius, 0.125f);
            Band(noseBase, tip, radius, 0.001f, 0.625f);
            Band(tail - 0.001f, tail, 0.001f, radius, 0.875f);
            for (int f = 0; f < 4; f++)
            {
                float a = f * Mathf.PI / 2f + Mathf.PI / 4f;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                int k = vertices.Count;
                vertices.Add(radial * radius + Vector3.up * (tail + 0.2f));
                vertices.Add(radial * (radius + 0.15f) + Vector3.up * (tail - 0.06f));
                vertices.Add(radial * radius + Vector3.up * (tail + 0.01f));
                for (int i = 0; i < 3; i++) uvs.Add(new Vector2(0.875f, 0.6f));
                triangles.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 1 });
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
