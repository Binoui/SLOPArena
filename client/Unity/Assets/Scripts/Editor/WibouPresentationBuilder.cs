#if UNITY_EDITOR
using SlopArena.Client.Animation;
using System.Collections.Generic;
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
    /// Rebuilds Wibou's package-owned combat presentation from code (docs/characters/wibou.md):
    /// the mall-ninja "Matrix" look of green pixels, pixel-font code rain, anime glints, crescent
    /// cuts and hit bursts, plus the pixel sword-trail style and the flying-kunai visual. The
    /// workflow and readability rules are in skills/sloparena-character-vfx-pass; generic asset,
    /// prefab and binding plumbing is in <see cref="PresentationBuilderKit"/>. Tune the look here
    /// and rerun the command.
    /// </summary>
    public static class WibouPresentationBuilder
    {
        // Local-only art; the projectile prefab nests it. The old config drew it at 40×, unadorned.
        private const string KunaiModelPath = "Assets/Art/Characters/wibou/Models/kunai.fbx";
        private const float KunaiModelScale = 40f * 1.4f;
        // kunai.fbx stores the kunai diagonally. Measured in its mesh space: the ring-to-tip axis
        // and the ring's face normal. The prefab turns the tip onto the root's +Y, which
        // ProjectileVFXManager aligns with the flight, and shows the ring's face to the side.
        private static readonly Vector3 KunaiTipAxis = new(-0.311f, -0.836f, -0.453f);
        private static readonly Vector3 KunaiRingNormal = new(0.945f, -0.221f, -0.241f);
        // Half the scaled kunai's length (0.66 m), for placing trails at the ring end.
        private const float KunaiHalfLength = 0.33f;

        // Approved accent #91FF69 between a dark green rim and a white core.
        private static readonly Color Rim = new(0.03f, 0.19f, 0.07f);
        private static readonly Color Green = new(0.57f, 1f, 0.41f);
        private static readonly Color Pale = new(0.84f, 1f, 0.76f);

        private static PresentationBuilderKit _kit;
        private static Materials _materials;
        private static Vector2[] _burstOutline;

        private sealed class Materials
        {
            public Material Glow, Glint, Pixel, Glyphs, Slash, Ring, Burst, Edge, TrailBand;
            public Mesh Crescent;
        }

        [CliCommand("sloparena.presentation.wibou.build", "Rebuild Wibou's package presentation prefabs, textures, meshes, materials and catalog bindings in Edit Mode. Saving the catalog triggers the usual package cook.", MainThreadRequired = true, Tags = new[] { "presentation", "character" })]
        public static object Build()
        {
            using var kit = new PresentationBuilderKit("wibou", audioFolder: "Wibou");
            _kit = kit;
            _materials = BuildMaterials();
            var built = new List<(string id, GameObject prefab)>
            {
                ("presentation.wibou.blade.glint", BladeGlint()),
                ("presentation.wibou.blade.hit", BladeHit(heavy: false)),
                ("presentation.wibou.blade.heavy-hit", BladeHit(heavy: true)),
                ("presentation.wibou.kunai.release", KunaiRelease()),
                ("presentation.wibou.kunai.hit", KunaiHit()),
                ("presentation.wibou.code-rain", CodeRain()),
                ("presentation.wibou.rising.crescent", RisingCrescent()),
                ("presentation.wibou.skyfall.draw", SkyfallDraw()),
                ("presentation.wibou.skyfall.launch", SkyfallLaunch()),
                ("presentation.wibou.skyfall.spike", SkyfallSpike()),
                ("presentation.wibou.dash.charge", DashCharge()),
                ("presentation.wibou.dash.cut", DashCut()),
            };
            var trail = PixelTrail();
            var kunai = KunaiProjectile();
            kit.BindWeaponTrail(trail);
            kit.BindProjectile("ground.A", kunai, ProjectileOrientation.FlightPositiveY, "");
            kit.BindProjectile("air.A", kunai, ProjectileOrientation.FlightPositiveY, "");
            return kit.Finish(built, new
            {
                trail = AssetDatabase.GetAssetPath(trail),
                kunai = AssetDatabase.GetAssetPath(kunai),
            });
        }

        [MenuItem("Tools/SLOPArena/Presentation/Rebuild Wibou Presentation")]
        private static void Menu() => Debug.Log("[WibouPresentationBuilder] " + Newtonsoft.Json.JsonConvert.SerializeObject(Build()));

        // ── Prefabs ──────────────────────────────────────────────────────────────

        /// <summary>A star flash on the blade tip shortly before a swing becomes active.</summary>
        private static GameObject BladeGlint()
        {
            var root = _kit.NewRoot("WibouBladeGlint", attackBound: true);
            Halo(root, 0.75f, 0.14f, ParticleSystemSimulationSpace.Local);
            Star(root, 0.8f, 0.16f, 120f);
            var sparkles = Particles(root, "Sparkles", _materials.Pixel, 0.3f);
            Burst(sparkles, 5);
            var main = sparkles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.34f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.gravityModifier = 0.3f;
            Sphere(sparkles, 0.04f);
            PixelFrames(sparkles);
            SizeOverLife(sparkles, Steps(1f, 1f, 0.7f, 0.7f, 0.4f, 0f));
            return _kit.Save(root);
        }

        /// <summary>
        /// Accepted sword hit: a comic impact star, slash line(s) across the contact and pixel shards
        /// thrown along the attacker's facing. Hierarchy scaling keeps the contact's damage scale.
        /// </summary>
        private static GameObject BladeHit(bool heavy)
        {
            var root = _kit.NewRoot(heavy ? "WibouBladeHeavyHit" : "WibouBladeHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, heavy ? 1.5f : 1.1f, heavy ? 0.12f : 0.1f, scaling: scaled);

            var burst = Particles(root, "Burst", _materials.Burst, 0.3f, scaling: scaled);
            Burst(burst, 1);
            var main = burst.main;
            main.startLifetime = heavy ? 0.17f : 0.13f;
            main.startSize = heavy ? 1.2f : 0.85f;
            InFront(burst);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(burst, Curve((0f, 0.6f), (0.12f, 1.1f), (0.5f, 1f), (1f, 0.75f)));
            Fade(burst, 0.75f);

            float[] angles = heavy ? new[] { 32f, -38f } : new[] { 34f };
            for (int i = 0; i < angles.Length; i++)
            {
                var slash = Particles(root, angles.Length > 1 ? $"Slash {i + 1}" : "Slash", _materials.Slash, 0.3f, scaling: scaled);
                Burst(slash, 1);
                main = slash.main;
                main.startDelay = i * 0.03f;
                main.startLifetime = heavy ? 0.2f : 0.16f;
                main.startSize3D = true;
                main.startSizeX = heavy ? 1.8f : 1.3f;
                main.startSizeY = heavy ? 0.26f : 0.2f;
                main.startSizeZ = 1f;
                main.startRotation = angles[i] * Mathf.Deg2Rad;
                SizeOverLife(slash, Curve((0f, 0.4f), (0.2f, 1.05f), (1f, 1.1f)), Curve((0f, 1f), (0.4f, 1f), (1f, 0.2f)));
                Fade(slash, 0.7f);
                InFront(slash);
            }

            if (heavy)
            {
                var ring = Particles(root, "Ring", _materials.Ring, 0.4f, scaling: scaled);
                Burst(ring, 1);
                main = ring.main;
                main.startLifetime = 0.22f;
                main.startSize = 1.3f;
                SizeOverLife(ring, Curve((0f, 0.35f), (1f, 1.25f)));
                Fade(ring, 0.4f);
                InFront(ring);
            }

            InFront(Shards(root, heavy ? 22 : 12, heavy ? 0.55f : 0.45f, heavy ? 0.14f : 0.12f, heavy ? 8f : 6f,
                heavy ? 65f : 55f, scaled));
            return _kit.Save(root);
        }

        /// <summary>Kunai release: a hand-height glint, three streaks along the throw and a light "shing".</summary>
        private static GameObject KunaiRelease()
        {
            var root = _kit.NewRoot("WibouKunaiRelease", attackBound: false);
            Halo(root, 0.6f, 0.12f);
            Star(root, 0.55f, 0.16f, 200f);
            var streaks = Particles(root, "Streaks", _materials.Slash, 0.2f);
            Burst(streaks, 3);
            var main = streaks.main;
            main.startLifetime = 0.14f;
            main.startSize = 0.12f;
            main.startSpeed = 16f;
            var shape = streaks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15f;
            shape.radius = 0.01f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Loop;
            Stretch(streaks, 0.04f, 2f);
            Fade(streaks, 0.5f);
            _kit.Sound(root, "wibou_draw_02", 0.32f, 1.18f);
            return _kit.Save(root);
        }

        /// <summary>Kunai hit: a small impact star, a few shards and a wooden thunk.</summary>
        private static GameObject KunaiHit()
        {
            var root = _kit.NewRoot("WibouKunaiHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            Halo(root, 0.9f, 0.09f, scaling: scaled);
            var burst = Particles(root, "Burst", _materials.Burst, 0.2f, scaling: scaled);
            Burst(burst, 1);
            var main = burst.main;
            main.startLifetime = 0.12f;
            main.startSize = 0.85f;
            InFront(burst);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(burst, Curve((0f, 0.6f), (0.15f, 1.1f), (1f, 0.8f)));
            Fade(burst, 0.7f);
            InFront(Shards(root, 10, 0.38f, 0.11f, 4f, 70f, scaled));
            _kit.Sound(root, "wibou_kunai_thunk", 0.5f, 1f);
            return _kit.Save(root);
        }

        /// <summary>
        /// Matrix-style code rain: pixel glyphs that flicker at 15 fps and fall from a moving body
        /// part, with loose pixels. World space, so a rising Wibou leaves a falling stream.
        /// </summary>
        private static GameObject CodeRain()
        {
            var root = _kit.NewRoot("WibouCodeRain", attackBound: true);
            // Emits for 0.75 s; the cue outlives it so the last glyphs finish falling.
            var glyphs = Particles(root, "Glyphs", _materials.Glyphs, 0.75f);
            var main = glyphs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(Green, Pale);
            main.maxParticles = 120;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            var emission = glyphs.emission;
            emission.rateOverTime = 40f;
            emission.rateOverDistance = 6f;
            Sphere(glyphs, 0.4f);
            Fall(glyphs, 2.5f, 4f);
            GlyphFrames(glyphs);
            Fade(glyphs, 0.6f);
            var pixels = Particles(root, "Pixels", _materials.Pixel, 0.75f);
            main = pixels.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.maxParticles = 60;
            emission = pixels.emission;
            emission.rateOverTime = 14f;
            Sphere(pixels, 0.35f);
            Fall(pixels, 1.5f, 3f);
            PixelFrames(pixels);
            SizeOverLife(pixels, Steps(1f, 1f, 0.6f, 0.6f, 0.3f, 0f));
            return _kit.Save(root);
        }

        /// <summary>Rising Slash: a vertical crescent in front of Wibou, upward streaks and a slice.</summary>
        private static GameObject RisingCrescent()
        {
            var root = _kit.NewRoot("WibouRisingCrescent", attackBound: false);
            // The mesh apex turns forward and its span stands up in the forward/up plane.
            var crescent = Crescent(root, "Crescent", 0.26f, 1.4f, Quaternion.Euler(0f, 90f, 90f));
            crescent.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            var streaks = Particles(root, "Streaks", _materials.Slash, 0.3f);
            Burst(streaks, 8);
            var main = streaks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.22f);
            main.startSize = 0.1f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 11f);
            var shape = streaks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.35f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Stretch(streaks, 0.05f, 2f);
            Fade(streaks, 0.5f);
            _kit.Sound(root, "wibou_slice_01", 0.55f, 1.12f);
            return _kit.Save(root);
        }

        /// <summary>
        /// Skyfall startup: a curtain of code rain falling around Wibou, a pixel ring bursting from
        /// the feet, a big glint and the blade-draw sting.
        /// </summary>
        private static GameObject SkyfallDraw()
        {
            var root = _kit.NewRoot("WibouSkyfallDraw", attackBound: false);
            var curtain = Particles(root, "Code curtain", _materials.Glyphs, 0.3f);
            var main = curtain.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.26f);
            main.startColor = new ParticleSystem.MinMaxGradient(Green, Pale);
            main.maxParticles = 80;
            var emission = curtain.emission;
            emission.rateOverTime = 120f;
            var shape = curtain.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.85f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            curtain.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            Fall(curtain, 3f, 4.2f);
            GlyphFrames(curtain);
            Fade(curtain, 0.6f);
            var ring = Particles(root, "Pixel ring", _materials.Pixel, 0.3f);
            Burst(ring, 22);
            main = ring.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            shape = ring.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.3f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            ring.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            PixelFrames(ring);
            SizeOverLife(ring, Steps(1f, 1f, 0.7f, 0.7f, 0.4f, 0f));
            var glint = Star(root, 0.9f, 0.24f, 160f);
            glint.transform.localPosition = new Vector3(0f, 1.2f, 0.15f);
            var halo = Halo(root, 0.9f, 0.18f);
            halo.transform.localPosition = glint.transform.localPosition;
            _kit.Sound(root, "wibou_draw_01", 0.7f, 0.92f);
            return _kit.Save(root);
        }

        /// <summary>Skyfall launch: a vertical blade column, upward speed lines and shards.</summary>
        private static GameObject SkyfallLaunch()
        {
            var root = _kit.NewRoot("WibouSkyfallLaunch", attackBound: false);
            var column = Particles(root, "Column", _materials.Slash, 0.3f);
            Burst(column, 2);
            var main = column.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.24f);
            // The slash runs along the quad's X; a quarter turn stands it upright.
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(2.8f, 3.4f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.3f, 0.4f);
            main.startSizeZ = 1f;
            main.startRotation = Mathf.PI * 0.5f;
            var shape = column.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;
            column.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            SizeOverLife(column, Curve((0f, 0.5f), (0.25f, 1f), (1f, 1.05f)), Curve((0f, 1f), (0.5f, 1f), (1f, 0.15f)));
            Fade(column, 0.6f);
            var lines = Particles(root, "Speed lines", _materials.Slash, 0.3f);
            Burst(lines, 14);
            main = lines.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.28f);
            main.startSize = 0.09f;
            main.startSpeed = 0f;
            shape = lines.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var velocity = lines.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(14f, 20f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Stretch(lines, 0.06f, 2f);
            Fade(lines, 0.5f);
            Shards(root, 18, 0.55f, 0.13f, 7f, 30f, ParticleSystemScalingMode.Local, upward: true);
            _kit.Sound(root, "wibou_slice_02", 0.65f, 1.05f);
            return _kit.Save(root);
        }

        /// <summary>Skyfall spike: a big crescent cutting forward and down, downward lines and a heavy slice.</summary>
        private static GameObject SkyfallSpike()
        {
            var root = _kit.NewRoot("WibouSkyfallSpike", attackBound: false);
            // As the Rising Slash crescent, then tipped 35° so the apex cuts forward and down.
            Crescent(root, "Crescent", 0.28f, 1.8f, Quaternion.AngleAxis(35f, Vector3.right) * Quaternion.Euler(0f, 90f, 90f));
            var lines = Particles(root, "Down lines", _materials.Slash, 0.3f);
            Burst(lines, 12);
            var main = lines.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
            main.startSize = 0.09f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(12f, 18f);
            var shape = lines.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.5f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            Stretch(lines, 0.06f, 2f);
            Fade(lines, 0.5f);
            Shards(root, 20, 0.5f, 0.15f, 7.5f, 80f, ParticleSystemScalingMode.Local);
            _kit.Sound(root, "wibou_slice_01", 0.8f, 0.86f);
            return _kit.Save(root);
        }

        /// <summary>Dash Slash charge: a hilt glint, pixels rising off the blade and a quick draw.</summary>
        private static GameObject DashCharge()
        {
            var root = _kit.NewRoot("WibouDashCharge", attackBound: true);
            Halo(root, 0.6f, 0.16f, ParticleSystemSimulationSpace.Local);
            Star(root, 0.6f, 0.2f, 220f);
            var embers = Particles(root, "Embers", _materials.Pixel, 1f);
            var main = embers.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.gravityModifier = -0.7f;
            main.maxParticles = 60;
            var emission = embers.emission;
            emission.rateOverTime = 18f;
            Sphere(embers, 0.12f);
            PixelFrames(embers);
            SizeOverLife(embers, Steps(1f, 1f, 0.6f, 0.6f, 0.3f, 0f));
            _kit.Sound(root, "wibou_draw_03", 0.42f, 1.22f);
            return _kit.Save(root);
        }

        /// <summary>
        /// Dash Slash release: thin streaks behind Wibou during the dash, then sharp diagonal cuts
        /// left along the path pop a beat after Wibou passes each one (the delayed iaido cut),
        /// synced to a click-then-slice sound.
        /// </summary>
        private static GameObject DashCut()
        {
            var root = _kit.NewRoot("WibouDashCut", attackBound: true);
            // A pixel "blink" where Wibou vanishes.
            var blink = Particles(root, "Blink", _materials.Pixel, 0.3f);
            Burst(blink, 18);
            var main = blink.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.6f);
            Sphere(blink, 0.45f);
            PixelFrames(blink);
            SizeOverLife(blink, Steps(1f, 1f, 0.7f, 0.7f, 0.4f, 0f));

            var streaks = Particles(root, "Streaks", _materials.Slash, 1f);
            main = streaks.main;
            main.loop = true;
            main.startLifetime = 0.16f;
            main.startSize = 0.07f;
            main.maxParticles = 60;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            var emission = streaks.emission;
            emission.rateOverDistance = 4f;
            Sphere(streaks, 0.4f);
            var inherit = streaks.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = 0.1f;
            Stretch(streaks, 0f, 14f);
            Fade(streaks, 0.3f);

            // Hidden for the first 0.17 s of each cut's life, then a hard pop that thins away.
            var cuts = Particles(root, "Delayed cuts", _materials.Slash, 1f);
            main = cuts.main;
            main.loop = true;
            main.startLifetime = 0.5f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(1.2f, 1.6f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.16f, 0.22f);
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(20f * Mathf.Deg2Rad, 160f * Mathf.Deg2Rad);
            main.maxParticles = 24;
            emission = cuts.emission;
            emission.rateOverDistance = 1.5f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
            Sphere(cuts, 0.35f);
            var color = cuts.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) },
                new[] { (0f, 0f), (0.33f, 0f), (0.34f, 1f), (0.75f, 1f), (1f, 0f) });
            SizeOverLife(cuts, Curve((0f, 0.3f), (0.33f, 0.3f), (0.4f, 1.1f), (1f, 1.15f)),
                Curve((0f, 1f), (0.4f, 1f), (1f, 0.15f)));
            InFront(cuts);
            _kit.Sound(root, "wibou_iaido_late", 0.75f, 1f);
            return _kit.Save(root);
        }

        /// <summary>
        /// Sword-trail style for <c>SwordSweepTrail</c> (bound in wibou_weapon.asset): green and
        /// white pixel squares shed along the blade over a light, translucent sweep with a crisp
        /// edge. The trail reads the root's material, tint and fade, emits "Dots" five per blade
        /// pose and "Lines" at their rate, and advances both on its own clock, so Hitstop freezes
        /// them with the blade.
        /// </summary>
        private static GameObject PixelTrail()
        {
            var root = _kit.NewRoot("WibouPixelTrail", attackBound: false);
            var ribbon = root.AddComponent<ParticleSystem>();
            ribbon.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ribbon.main;
            main.duration = 0.24f;
            main.loop = false;
            main.playOnAwake = false;
            main.startColor = Color.white;
            var emission = ribbon.emission;
            emission.enabled = false;
            var shape = ribbon.shape;
            shape.enabled = false;
            var color = ribbon.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 1f), (0.35f, 0.85f), (1f, 0f) });
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _materials.TrailBand;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var dots = Particles(root, "Dots", _materials.Pixel, 1f);
            main = dots.main;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.42f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.maxParticles = 256;
            // A fast random hop that brakes within a few frames scatters the five fixed rows
            // into a pixel cloud over the sweep.
            Drift(dots, 1.4f, -1f, 1.4f);
            Drag(dots, 0.35f);
            PixelFrames(dots);
            SizeOverLife(dots, Steps(1f, 1f, 0.7f, 0.7f, 0.4f, 0f));

            // Emitted along the blade at this rate, drifting back against the swing at startSpeed.
            var lines = Particles(root, "Lines", _materials.Pixel, 1f);
            main = lines.main;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.startSpeed = 0.5f;
            main.maxParticles = 400;
            emission = lines.emission;
            emission.rateOverTime = 600f;
            Drift(lines, 0.9f, -0.7f, 0.9f);
            Drag(lines, 0.35f);
            PixelFrames(lines);
            SizeOverLife(lines, Steps(1f, 1f, 0.6f, 0.6f, 0.3f, 0f));
            return _kit.Save(root);
        }

        /// <summary>
        /// Flying kunai: the mall-ninja model enlarged for readability and turned point-first, a
        /// white-to-green streak and shed pixels from the ring end, a green halo and a blade shine.
        /// ProjectileVFXManager turns the root's +Y along the flight.
        /// </summary>
        private static GameObject KunaiProjectile()
        {
            var root = _kit.NewRoot("WibouKunaiProjectile", attackBound: false);
            var instance = _kit.NestModel(root, KunaiModelPath, "Kunai");
            if (instance != null)
            {
                var tip = KunaiTipAxis.normalized;
                var side = Vector3.ProjectOnPlane(KunaiRingNormal, tip).normalized;
                instance.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(side, tip), tip));
                instance.transform.localScale = Vector3.one * KunaiModelScale;
            }

            var halo = Particles(root, "Halo", _materials.Glow, 1f, ParticleSystemSimulationSpace.Local);
            Burst(halo, 1);
            var main = halo.main;
            main.startLifetime = 10f;
            main.startSize = 0.5f;
            main.startColor = new Color(1f, 1f, 1f, 0.6f);
            Behind(halo);

            var streak = new GameObject("Streak").AddComponent<TrailRenderer>();
            streak.transform.SetParent(root.transform, false);
            streak.transform.localPosition = new Vector3(0f, -KunaiHalfLength, 0f);
            streak.time = 0.22f;
            streak.minVertexDistance = 0.04f;
            streak.widthMultiplier = 0.17f;
            streak.widthCurve = Curve((0f, 1f), (1f, 0f));
            streak.colorGradient = GradientOf(new[] { (0f, Color.white), (0.4f, Green), (1f, Green) },
                new[] { (0f, 1f), (0.6f, 0.8f), (1f, 0f) }).gradient;
            streak.textureMode = LineTextureMode.Stretch;
            streak.alignment = LineAlignment.View;
            streak.sharedMaterial = _materials.Slash;
            streak.shadowCastingMode = ShadowCastingMode.Off;
            streak.receiveShadows = false;

            var pixels = Particles(root, "Pixels", _materials.Pixel, 1f);
            main = pixels.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.maxParticles = 96;
            pixels.transform.localPosition = new Vector3(0f, -KunaiHalfLength, 0f);
            var emission = pixels.emission;
            emission.rateOverDistance = 16f;
            Drift(pixels, 0.3f, -0.6f, 0.2f);
            PixelFrames(pixels);
            SizeOverLife(pixels, Steps(1f, 1f, 0.6f, 0.6f, 0.3f, 0f));

            // A cringe anime "shine" that pops on the blade about four times a second.
            var shine = Particles(root, "Shine", _materials.Glint, 1f, ParticleSystemSimulationSpace.Local);
            shine.transform.localPosition = new Vector3(0f, KunaiHalfLength * 0.45f, 0f);
            main = shine.main;
            main.loop = true;
            main.startLifetime = 0.16f;
            main.startSize = 0.42f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            emission = shine.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 4, 0.25f) });
            SizeOverLife(shine, Curve((0f, 0.3f), (0.2f, 1.1f), (1f, 0f)));
            Spin(shine, 300f);
            return _kit.Save(root);
        }

        // ── Shared layers ────────────────────────────────────────────────────────

        /// <summary>The only additive layer: a soft green halo drawn behind a glint or hit.</summary>
        private static ParticleSystem Halo(GameObject root, float size, float lifetime,
            ParticleSystemSimulationSpace space = ParticleSystemSimulationSpace.World,
            ParticleSystemScalingMode scaling = ParticleSystemScalingMode.Local)
        {
            var halo = Particles(root, "Halo", _materials.Glow, 0.3f, space, scaling);
            Burst(halo, 1);
            var main = halo.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startColor = new Color(1f, 1f, 1f, 0.55f);
            SizeOverLife(halo, Curve((0f, 0.4f), (0.2f, 1f), (1f, 0.2f)));
            Fade(halo, 0.2f);
            Behind(halo);
            return halo;
        }

        /// <summary>The four-point blade star: pops large, holds, then shrinks away while spinning.</summary>
        private static ParticleSystem Star(GameObject root, float size, float lifetime, float spin)
        {
            var star = Particles(root, "Star", _materials.Glint, 0.3f, ParticleSystemSimulationSpace.Local);
            Burst(star, 1);
            var main = star.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            SizeOverLife(star, Curve((0f, 0.25f), (0.12f, 1.15f), (0.25f, 1f), (0.65f, 0.8f), (1f, 0f)));
            Spin(star, spin);
            return star;
        }

        /// <summary>Rimmed pixel shards thrown in a cone along local forward (or up), falling under gravity.</summary>
        private static ParticleSystem Shards(GameObject root, int count, float maxLifetime, float maxSize, float maxSpeed,
            float coneAngle, ParticleSystemScalingMode scaling, bool upward = false)
        {
            var shards = Particles(root, "Shards", _materials.Pixel, 0.6f, scaling: scaling);
            Burst(shards, count);
            var main = shards.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, maxLifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(maxSize * 0.5f, maxSize);
            main.startSpeed = new ParticleSystem.MinMaxCurve(maxSpeed * 0.45f, maxSpeed);
            main.gravityModifier = upward ? 0.4f : 0.8f;
            var shape = shards.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.05f;
            if (upward) shape.rotation = new Vector3(-90f, 0f, 0f);
            Drag(shards, 0.25f);
            PixelFrames(shards);
            SizeOverLife(shards, Steps(1f, 1f, 0.7f, 0.7f, 0.4f, 0f));
            return shards;
        }

        // ── Assets ───────────────────────────────────────────────────────────────

        private static Materials BuildMaterials() => new()
        {
            Glow = _kit.ParticleMaterial("WibouGlowAdd", _kit.BakeTexture("WibouGlow", 128, 128,
                (u, v) => Alpha(Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f))), new Color(0.9f, 1.6f, 0.7f), additive: true),
            Glint = _kit.ParticleMaterial("WibouGlint", _kit.BakeTexture("WibouGlint", 128, 128, GlintPixel)),
            Pixel = _kit.ParticleMaterial("WibouPixel", _kit.BakeTexture("WibouPixel", 64, 32, PixelPixel)),
            Glyphs = _kit.ParticleMaterial("WibouGlyphs", _kit.BakeTexture("WibouGlyphs", 256, 256, GlyphPixel)),
            TrailBand = _kit.ParticleMaterial("WibouTrailBand", _kit.BakeTexture("WibouTrailBand", 64, 128, TrailBandPixel)),
            Slash = _kit.ParticleMaterial("WibouSlash", _kit.BakeTexture("WibouSlash", 256, 64, SlashPixel)),
            Ring = _kit.ParticleMaterial("WibouRing", _kit.BakeTexture("WibouRing", 256, 256,
                (u, v) => Bands((Mathf.Abs(Radius(u, v) - 0.8f) - 0.08f) * 128f, 2f, 3f, Green, Pale))),
            Burst = _kit.ParticleMaterial("WibouBurst", _kit.BakeTexture("WibouBurst", 256, 256, BurstPixel)),
            Edge = _kit.ParticleMaterial("WibouCrescentEdge", _kit.BakeTexture("WibouCrescentEdge", 64, 64, EdgePixel)),
            Crescent = CrescentMesh("WibouCrescent"),
        };

        /// <summary>Toon bands with Wibou's dark green rim.</summary>
        private static Color Bands(float distance, float rimWidth, float accentWidth, Color accent, Color core)
            => ToonShapes.Bands(distance, rimWidth, accentWidth, Rim, accent, core);

        /// <summary>A four-point star with short diagonal rays and a round white centre.</summary>
        private static Color GlintPixel(float u, float v)
        {
            var p = new Vector2(u * 2f - 1f, v * 2f - 1f);
            var q = Rotate(p, 45f);
            float d = Mathf.Min(
                Mathf.Min(Rhombus(p, new Vector2(0.95f, 0.15f)), Rhombus(p, new Vector2(0.15f, 0.95f))),
                Mathf.Min(Mathf.Min(Rhombus(q, new Vector2(0.48f, 0.1f)), Rhombus(q, new Vector2(0.1f, 0.48f))),
                    p.magnitude - 0.17f));
            return Bands(d * 64f, 1.6f, 2.6f, Green, Color.white);
        }

        /// <summary>Two square frames (2×1): a green pixel and a white one with a green band, both rimmed.</summary>
        private static Color PixelPixel(float u, float v)
        {
            var p = new Vector2((u % 0.5f) * 4f - 1f, v * 2f - 1f);
            float d = Box(p, new Vector2(0.78f, 0.78f)) * 16f;
            return u >= 0.5f ? Bands(d, 2.5f, 2.5f, Green, Color.white) : Bands(d, 2.5f, 99f, Green, Green);
        }

        // 5×7 pixel glyphs after the film's mirrored katakana and digits, one per sheet cell.
        private static readonly string[][] GlyphBitmaps =
        {
            new[] { "#####", "....#", "...#.", "..##.", "..#..", ".#...", "#...." },
            new[] { "....#", "...#.", "..##.", ".#.#.", "#..#.", "...#.", "...#." },
            new[] { "..#..", "#####", "#...#", "....#", "...#.", "..#..", "##..." },
            new[] { ".....", "#####", "..#..", "..#..", "..#..", "..#..", "#####" },
            new[] { "...#.", "#####", "..##.", ".#.#.", "#..#.", "...#.", "..##." },
            new[] { "..#..", "#####", "..#.#", "..#.#", ".#..#", ".#..#", "#..##" },
            new[] { "..#..", "#####", "..#..", "#####", "..#..", "..#..", "..#.." },
            new[] { ".#...", ".####", "#...#", "....#", "...#.", "..#..", "##..." },
            new[] { ".#...", ".####", "#..#.", "...#.", "...#.", "..#..", ".#..." },
            new[] { ".....", "#####", "....#", "....#", "....#", "....#", "#####" },
            new[] { ".#.#.", "#####", ".#.#.", ".#.#.", "...#.", "..#..", ".#..." },
            new[] { "#....", ".#..#", "#...#", ".#..#", "...#.", "..#..", "##..." },
            new[] { "#####", "....#", "...#.", "..#..", ".#.#.", "#...#", "....." },
            new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            new[] { "#...#", "#...#", ".#..#", "....#", "...#.", "..#..", "##..." },
        };

        /// <summary>
        /// A 4×4 sheet of rimmed pixel glyphs on 64-texel cells. Bodies are white so particle
        /// colour tints them, from accent green to a pale "leading" glyph.
        /// </summary>
        private static Color GlyphPixel(float u, float v)
        {
            int column = Mathf.Min(3, (int)(u * 4f)), rowFromTop = 3 - Mathf.Min(3, (int)(v * 4f));
            string[] glyph = GlyphBitmaps[rowFromTop * 4 + column];
            float x = u * 256f - column * 64f, y = v * 256f - (3 - rowFromTop) * 64f;
            float distance = 99f;
            for (int gy = 0; gy < 7; gy++)
            for (int gx = 0; gx < 5; gx++)
            {
                if (glyph[gy][gx] != '#') continue;
                var center = new Vector2(12f + gx * 8f + 4f, 4f + (6 - gy) * 8f + 4f);
                distance = Mathf.Min(distance, Box(new Vector2(x, y) - center, new Vector2(4f, 4f)));
            }
            return Bands(distance, 1.5f, 99f, Color.white, Color.white);
        }

        /// <summary>
        /// Sword-ribbon band. SwordSweepTrail maps U along the sweep and V 0–0.5 from the inner
        /// edge to the blade tip; the top half is unused. A light translucent green rises toward a
        /// pale cutting edge with a thin dark rim, so the pixels carry the look.
        /// </summary>
        private static Color TrailBandPixel(float u, float v)
        {
            if (v >= 0.5f) return new Color(Rim.r, Rim.g, Rim.b, 0f);
            float across = v * 2f, texel = across * 64f;
            // Mathf.SmoothStep eases from 0.15 to 0.86 here (not a 0–1 step); this is the approved look.
            float body = Mathf.Pow(Mathf.SmoothStep(0.15f, 0.86f, across), 1.5f) * 0.38f;
            float edge = Mathf.Clamp01(texel - 55f), rim = Mathf.Clamp01(texel - 61f);
            Color color = Color.Lerp(Green, Pale, edge);
            color = Color.Lerp(color, Rim, rim);
            color.a = Mathf.Lerp(body, 0.95f, edge);
            return color;
        }

        /// <summary>A pointed lens: white blade, green edges, dark rim. Stretched particles run along U.</summary>
        private static Color SlashPixel(float u, float v)
        {
            var p = new Vector2(u * 2f - 1f, (v * 2f - 1f) * 0.25f);
            return Bands(Lens(p, 0.95f, 0.17f) * 128f, 1.6f, 3.2f, Green, Color.white);
        }

        /// <summary>A comic impact star with uneven spikes: white centre, green spikes, dark rim.</summary>
        private static Color BurstPixel(float u, float v)
        {
            _burstOutline ??= StarOutline(spikes: 9, inner: 0.36f, seed: 7);
            var p = new Vector2(u * 2f - 1f, v * 2f - 1f);
            return Bands(Polygon(p, _burstOutline) * 128f, 3f, 7f, Green, Color.white);
        }

        /// <summary>Across a crescent (V from inner to outer edge): dark rim, green body, white cutting edge, dark rim.</summary>
        private static Color EdgePixel(float u, float v)
        {
            float texel = v * 64f;
            Color color = Color.Lerp(Rim, Green, Mathf.Clamp01(texel - 6f));
            color = Color.Lerp(color, Color.white, Mathf.Clamp01(texel - 40f));
            return Color.Lerp(color, Rim, Mathf.Clamp01(texel - 57f));
        }

        /// <summary>
        /// A 120° crescent in local XY, thick at the middle and sharp at both ends, centred near its
        /// apex. V runs from the inner (0) to the outer cutting edge (1) for the edge texture.
        /// </summary>
        private static Mesh CrescentMesh(string name)
        {
            const int segments = 28;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = Mathf.Lerp(30f, 150f, t) * Mathf.Deg2Rad;
                // Sin(π) is slightly negative in float; clamp before the fractional power.
                float thickness = 0.28f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), 0.75f);
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices.Add(direction - Vector3.up * 0.9f);
                vertices.Add(direction * (1f - thickness) - Vector3.up * 0.9f);
                uvs.Add(new Vector2(t, 1f));
                uvs.Add(new Vector2(t, 0f));
                if (i == 0) continue;
                int a = (i - 1) * 2, b = a + 1, c = a + 2, d = a + 3;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        // ── Wibou helpers ────────────────────────────────────────────────────────

        /// <summary>Crescent mesh particle aligned to the prefab's local space; it grows into the cut, then thins.</summary>
        private static ParticleSystem Crescent(GameObject root, string name, float lifetime, float size, Quaternion rotation)
        {
            var system = Particles(root, name, _materials.Edge, 0.4f, ParticleSystemSimulationSpace.Local);
            system.transform.localRotation = rotation;
            Burst(system, 1);
            var main = system.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = _materials.Crescent;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            SizeOverLife(system, Curve((0f, 0.7f), (0.2f, 1.05f), (1f, 1.12f)));
            Fade(system, 0.6f);
            return system;
        }

        /// <summary>Flickers through the 4×4 glyph sheet at 15 fps from a random glyph.</summary>
        private static void GlyphFrames(ParticleSystem system) => FlickerFrames(system, 4, 4, 15f);

        /// <summary>Gives each particle the green or the white pixel frame at random.</summary>
        private static void PixelFrames(ParticleSystem system) => RandomFrame(system, 2, 1);
    }
}
#endif
