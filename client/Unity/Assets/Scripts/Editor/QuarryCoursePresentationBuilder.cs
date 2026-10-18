#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
    /// Rebuilds Kaboom Quarry's course feel (Manki's course, docs/design/stages/manki_quarry.design.md):
    /// nervous red TNT crates with a lit fuse for targets, which blow apart into planks, sticks and
    /// Manki's own fireballs, smoke and grin; hazard-tape checkpoint frames; red flares over
    /// missed crates; the detonator's orange beam and a giant comic KABOOM at the finish. The
    /// break reuses Manki's explosion materials (CharacterPackages/manki/Presentation), so the
    /// course and his kit blow things up the same way. Writes
    /// Stages/manki_quarry/Presentation/Course and Resources/Courses/manki_quarry;
    /// tools/sfx/quarry_course_clips.py makes the sounds (Audio/SFX/CourseQuarry).
    /// </summary>
    public static class QuarryCoursePresentationBuilder
    {
        private const string Root = "Assets/Stages/manki_quarry/Presentation/Course";
        private const string SetPath = "Assets/Resources/" + CoursePresentationSet.ResourcesFolder + "/manki_quarry.asset";
        private const string MankiFx = "Assets/CharacterPackages/manki/Presentation/";
        private const string SoundFolder = "Audio/SFX/CourseQuarry";
        // The authored target radius; CharacterCoursePresentation scales the prefabs to others.
        private const float TargetRadius = .65f;
        // The crate nearly fills the hit sphere; three sticks of dynamite poke out of its lid.
        private const float Half = .4f, StickRadius = .065f, StickLength = .5f;

        // Manki Demolition's colours (tools/blender/manki_quarry/palette.py).
        private static readonly Color Ink = Hex(0x141212);
        private static readonly Color RedDark = Hex(0x6e120d);
        private static readonly Color Red = Hex(0xd6261a);
        private static readonly Color RedLight = Hex(0xff6448);
        private static readonly Color Hazard = Hex(0xfac71a);
        private static readonly Color HazardLight = Hex(0xfff0a0);
        private static readonly Color HazardDeep = Hex(0xb07a06);
        private static readonly Color Orange = Hex(0xeb6b1a);
        private static readonly Color OrangeLight = Hex(0xffb36b);
        private static readonly Color Cream = Hex(0xfff2cf);
        private static readonly Color Label = Hex(0xf5e08a);
        private static readonly Color Fuse = Hex(0x4a3a2c);

        private static PresentationBuilderKit _kit;
        private static Materials _m;
        private static Mesh _quad, _beamQuad;

        private sealed class Materials
        {
            public Material Crate, Outline, Halo, Pool, Debris, Confetti, RingHazard, RingRed, MoteHazard, MoteRed, MoteOrange,
                FrameHazard, FrameRed, BeamOrange, BeamRed;
            // Manki's own (CharacterPackages/manki/Presentation).
            public Material Flash, Pow, Fireball, Puff, Spark, Grin, MankiRing, MankiGlow;
        }

        [CliCommand("sloparena.presentation.course.quarry.build", "Rebuild Kaboom Quarry's crate targets, break, checkpoint, flare and finish prefabs and its Resources/Courses set in Edit Mode.", MainThreadRequired = true, Tags = new[] { "presentation", "arcade" })]
        public static object Build()
        {
            using var kit = PresentationBuilderKit.ForFolder(Root, audioFolder: "CourseQuarry");
            _kit = kit;
            _m = BuildMaterials();
            _quad = Quad("QuarryQuad", -.5f);
            _beamQuad = Quad("QuarryBeamQuad", 0f);
            var set = AssetDatabase.LoadAssetAtPath<CoursePresentationSet>(SetPath);
            bool created = set == null;
            if (created) set = ScriptableObject.CreateInstance<CoursePresentationSet>();
            set.Target = Crate();
            set.TargetRadius = TargetRadius;
            set.Break = CrateBreak();
            set.Marker = CheckpointMarker();
            set.MissedBeacon = Beacon("QuarryMissedFlare", _m.BeamRed, new Vector2(.9f, 16f), _m.RingRed, 1.7f, floorRing: false,
                _m.MoteRed, 5f, .2f, new Vector2(1.4f, 2.6f), 2f);
            set.FinishBeacon = Beacon("QuarryDetonatorBeam", _m.BeamOrange, new Vector2(2.4f, 42f), _m.RingHazard, 5f, floorRing: true,
                _m.MoteOrange, 12f, .8f, new Vector2(2f, 4.5f), 3f);
            set.FinishBurst = FinishBurst();
            set.SoundFolder = SoundFolder;
            set.NoteSemitones = new[] { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21 };
            if (created)
            {
                PresentationBuilderKit.EnsureFolder("Assets/Resources/" + CoursePresentationSet.ResourcesFolder);
                AssetDatabase.CreateAsset(set, SetPath);
            }
            else EditorUtility.SetDirty(set);
            return kit.Close(new { set = SetPath });
        }

        [MenuItem("Tools/SLOPArena/Presentation/Rebuild Kaboom Quarry Course Presentation")]
        private static void Menu() => Debug.Log("[QuarryCoursePresentationBuilder] " + Newtonsoft.Json.JsonConvert.SerializeObject(Build()));

        // ── Targets ──────────────────────────────────────────────────────────────

        /// <summary>
        /// A red TNT crate that cannot keep still: hazard-yellow label, three sticks of dynamite
        /// in the lid, the fuse sparking and smoking, a dark outline, an orange glow behind it and
        /// a pool of light on the rock below.
        /// </summary>
        private static GameObject Crate()
        {
            var root = _kit.NewRoot("QuarryCrate", attackBound: false);
            var lantern = root.AddComponent<CourseLantern>();
            lantern.BobHeight = .035f;
            lantern.BobHz = 1.9f;
            lantern.SwayDegrees = 6f;
            var model = Child(root, "Model");
            lantern.Model = model.transform;
            AddMesh(Child(model, "Crate"), CrateMesh(), _m.Crate);
            AddMesh(Child(model, "Outline"), OutlineMesh(), _m.Outline);
            var tip = Child(model, "Fuse tip");
            tip.transform.localPosition = FuseTip;
            FuseSparks(tip);

            var halo = Child(root, "Halo");
            halo.transform.localScale = Vector3.one * 2.6f;
            AddMesh(halo, _quad, _m.Halo);
            lantern.Halo = halo.transform;
            var pool = Child(root, "Light pool");
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pool.transform.localScale = Vector3.one * 2f;
            var snap = pool.AddComponent<SnapToFloor>();
            snap.MaxDrop = 3f;
            snap.Lift = .04f;
            AddMesh(pool, _quad, _m.Pool);
            return Save(root);
        }

        private static readonly Vector3 FuseTip = new(.12f, Half + StickLength + .2f, -.02f);

        /// <summary>The lit fuse: sparks spitting out, a flickering glow and a thread of smoke.</summary>
        private static void FuseSparks(GameObject tip)
        {
            var sparks = Particles(tip, "Sparks", _m.Spark, 1f);
            var main = sparks.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.18f, .32f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.07f, .14f);
            main.gravityModifier = .5f;
            main.maxParticles = 32;
            var emission = sparks.emission;
            emission.rateOverTime = 22f;
            Sphere(sparks, .02f);
            Fade(sparks, .5f);
            InFront(sparks);

            var glow = Particles(tip, "Glow", _m.MankiGlow, 1f);
            main = glow.main;
            main.loop = true;
            main.startLifetime = .12f;
            main.startSize = new ParticleSystem.MinMaxCurve(.3f, .5f);
            main.startColor = new Color(Orange.r, Orange.g, Orange.b, .8f);
            main.maxParticles = 4;
            emission = glow.emission;
            emission.rateOverTime = 14f;
            Fade(glow, .3f);

            var smoke = Particles(tip, "Smoke", _m.Puff, 1f);
            main = smoke.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.7f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(.14f, .24f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 12;
            emission = smoke.emission;
            emission.rateOverTime = 5f;
            Drift(smoke, .08f, .5f, .9f);
            RandomFrame(smoke, 2, 2);
            SizeOverLife(smoke, Curve((0f, .5f), (1f, 1.6f)));
            Fade(smoke, .35f);
            Behind(smoke);
        }

        /// <summary>
        /// The break: Manki's flash and comic POW, two fireballs, the crate's planks and sticks
        /// flying, smoke puffs, sparks and, rising out of it, his winking grin.
        /// </summary>
        private static GameObject CrateBreak()
        {
            var root = _kit.NewRoot("QuarryCrateBreak", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var flash = Particles(root, "Flash", _m.Flash, .3f, scaling: scaled);
            Burst(flash, 1);
            var main = flash.main;
            main.startLifetime = .14f;
            main.startSize = 2.8f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(flash, Curve((0f, .6f), (.3f, 1f), (1f, 1.1f)));
            Fade(flash, .3f);
            Behind(flash);

            var pow = Particles(root, "Pow", _m.Pow, .4f, scaling: scaled);
            Burst(pow, 1);
            main = pow.main;
            main.startLifetime = .26f;
            main.startSize = 2f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.3f, .3f);
            SizeOverLife(pow, Curve((0f, .4f), (.2f, 1.15f), (.5f, 1f), (1f, .8f)));
            Fade(pow, .6f);
            InFront(pow);

            var fireballs = Particles(root, "Fireballs", _m.Fireball, .8f, scaling: scaled);
            Burst(fireballs, 2);
            main = fireballs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .5f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.1f, 1.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Sphere(fireballs, .2f);
            RandomFrame(fireballs, 2, 2);
            SizeOverLife(fireballs, Curve((0f, .5f), (.2f, 1.1f), (1f, .8f)));
            Fade(fireballs, .45f);

            var debris = Particles(root, "Planks", _m.Debris, 1.8f, scaling: scaled);
            Burst(debris, 14);
            main = debris.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(.2f, .36f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 7.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1.3f;
            Sphere(debris, .3f);
            RandomFrame(debris, 2, 2);
            Spin(debris, 600f);
            Fade(debris, .75f);
            debris.GetComponent<ParticleSystemRenderer>().sortingFudge = -6f;

            var puffs = Particles(root, "Puffs", _m.Puff, 1.4f, scaling: scaled);
            Burst(puffs, 7);
            main = puffs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(.6f, .95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Sphere(puffs, .3f);
            Drag(puffs, .12f);
            RandomFrame(puffs, 2, 2);
            SizeOverLife(puffs, Curve((0f, .5f), (.3f, 1f), (1f, 1.25f)));
            Fade(puffs, .5f);
            Behind(puffs);

            var sparks = Particles(root, "Sparks", _m.Spark, .6f, scaling: scaled);
            Burst(sparks, 12);
            main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .45f);
            main.startSize = new ParticleSystem.MinMaxCurve(.08f, .15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.gravityModifier = .8f;
            Sphere(sparks, .2f);
            Fade(sparks, .5f);
            InFront(sparks);

            var ring = Particles(root, "Ring", _m.MankiRing, .5f, scaling: scaled);
            Burst(ring, 1);
            main = ring.main;
            main.startLifetime = .4f;
            main.startSize = 1.6f;
            SizeOverLife(ring, Curve((0f, .6f), (.3f, 1.2f), (1f, 1.7f)));
            Fade(ring, .3f);

            Grin(root, 1.45f, .2f, scaled);
            return Save(root);
        }

        /// <summary>Manki's winking grin rising out of the smoke (the same sprite as his bombs).</summary>
        private static void Grin(GameObject root, float size, float delay, ParticleSystemScalingMode scaling)
        {
            var grin = Particles(root, "Grin", _m.Grin, 2f, scaling: scaling);
            grin.transform.localPosition = new Vector3(0f, .4f, 0f);
            Burst(grin, 1);
            var main = grin.main;
            main.startDelay = delay;
            main.startLifetime = 1.1f;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.12f, .12f);
            var velocity = grin.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // x, y and z share one curve mode; a zero multiplier keeps the grin from drifting sideways.
            var rise = Curve((0f, 1.2f), (.35f, .5f), (1f, .25f));
            velocity.x = new ParticleSystem.MinMaxCurve(0f, rise);
            velocity.y = new ParticleSystem.MinMaxCurve(1f, rise);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, rise);
            SizeOverLife(grin, Curve((0f, .35f), (.12f, 1.08f), (.25f, 1f), (1f, 1.12f)));
            var color = grin.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (.08f, 1f), (.62f, 1f), (1f, 0f) });
            grin.GetComponent<ParticleSystemRenderer>().sortingFudge = -2f;
        }

        // ── Checkpoints, flares and the finish ───────────────────────────────────

        /// <summary>A floor frame sized at runtime: hazard tape when crossing it will save, red when it cannot.</summary>
        private static GameObject CheckpointMarker()
        {
            var root = _kit.NewRoot("QuarryCheckpointMarker", attackBound: false);
            var marker = root.AddComponent<CourseCheckpointMarker>();
            // Above the painted hazard bands and drop-edge stripes, so the lit frame covers them.
            var frame = Child(root, "Frame");
            frame.transform.localPosition = new Vector3(0f, .1f, 0f);
            marker.Frame = frame.AddComponent<MeshFilter>();
            marker.FrameRenderer = frame.AddComponent<MeshRenderer>();
            Quiet(marker.FrameRenderer);
            marker.FrameRenderer.sharedMaterial = _m.FrameHazard;
            marker.FrameRenderer.enabled = false;
            marker.ArmedMaterial = _m.FrameHazard;
            marker.LockedMaterial = _m.FrameRed;
            marker.Width = .7f;
            marker.ArmedMotes = Motes(root, "Armed motes", _m.MoteHazard, 12f);
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

        /// <summary>A flare of light that faces the camera, with a ripple ring and rising sparks.</summary>
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
            Spin(motes, 200f);
            Fade(motes, .6f);
            return Save(root);
        }

        /// <summary>
        /// Pushing the big red thing: the biggest KABOOM yet. Manki's flash, a giant POW, a ring
        /// of fireballs, smoke, planks and hazard-tape confetti, floor rings and a huge grin.
        /// </summary>
        private static GameObject FinishBurst()
        {
            var root = _kit.NewRoot("QuarryFinishBurst", attackBound: false);
            var flash = Particles(root, "Flash", _m.Flash, .4f);
            flash.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Burst(flash, 1);
            var main = flash.main;
            main.startLifetime = .2f;
            main.startSize = 6f;
            SizeOverLife(flash, Curve((0f, .5f), (.3f, 1f), (1f, 1.1f)));
            Fade(flash, .3f);
            Behind(flash);

            var pow = Particles(root, "Pow", _m.Pow, .6f);
            pow.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            Burst(pow, 1);
            main = pow.main;
            main.startDelay = .05f;
            main.startLifetime = .45f;
            main.startSize = 4.5f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.2f, .2f);
            SizeOverLife(pow, Curve((0f, .3f), (.15f, 1.15f), (.5f, 1f), (1f, .85f)));
            Fade(pow, .6f);
            InFront(pow);

            var fireballs = Particles(root, "Fireballs", _m.Fireball, 1f);
            fireballs.transform.localPosition = new Vector3(0f, 1f, 0f);
            Burst(fireballs, 6);
            main = fireballs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .7f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Sphere(fireballs, .6f);
            RandomFrame(fireballs, 2, 2);
            SizeOverLife(fireballs, Curve((0f, .4f), (.2f, 1.1f), (1f, .8f)));
            Fade(fireballs, .45f);

            var puffs = Particles(root, "Puffs", _m.Puff, 2.4f);
            puffs.transform.localPosition = new Vector3(0f, .8f, 0f);
            Burst(puffs, 14);
            main = puffs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 1.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Sphere(puffs, .6f);
            Drag(puffs, .1f);
            RandomFrame(puffs, 2, 2);
            SizeOverLife(puffs, Curve((0f, .4f), (.3f, 1f), (1f, 1.3f)));
            Fade(puffs, .55f);
            Behind(puffs);

            foreach (var (label, material, count, sizes) in new[]
                     {
                         ("Planks", _m.Debris, 26, new Vector2(.24f, .4f)),
                         ("Hazard tape", _m.Confetti, 70, new Vector2(.2f, .34f)),
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
                main.gravityModifier = material == _m.Debris ? 1.1f : .45f;
                main.maxParticles = 96;
                var shape = bits.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 42f;
                shape.radius = .4f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                RandomFrame(bits, 2, 2);
                Spin(bits, 500f);
                Fade(bits, .8f);
            }

            var floor = Floor(root, 3f, -.85f);
            FloorRing(floor, "Blast ring", _m.MankiRing, 7f, 0f, .8f);
            FloorRing(floor, "Hazard ring", _m.RingHazard, 4.4f, .1f, .8f);

            Grin(root, 3.4f, .3f, ParticleSystemScalingMode.Local);
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

        // ── Crate geometry ───────────────────────────────────────────────────────

        // Crate atlas cells (u0, v0, u1, v1): the front with its label, the lid, the sides, and a
        // strip for the sticks (red body, label band, dark ends and fuse).
        private static readonly Rect FrontCell = new(0f, .5f, .5f, .5f);
        private static readonly Rect LidCell = new(.5f, .5f, .5f, .5f);
        private static readonly Rect SideCell = new(0f, 0f, .5f, .5f);
        private const float StickU = .62f, LabelU = .78f, DarkU = .93f;

        /// <summary>
        /// The crate box (front and back carry the label; CourseLantern turns +Z to the camera,
        /// so the label always faces it) and three sticks leaning out of the lid.
        /// </summary>
        private static Mesh CrateMesh()
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            void Face(Vector3 center, Vector3 right, Vector3 up, Rect cell)
            {
                int i = vertices.Count;
                vertices.Add(center - right - up); vertices.Add(center + right - up);
                vertices.Add(center + right + up); vertices.Add(center - right + up);
                uvs.Add(new Vector2(cell.xMin, cell.yMin)); uvs.Add(new Vector2(cell.xMax, cell.yMin));
                uvs.Add(new Vector2(cell.xMax, cell.yMax)); uvs.Add(new Vector2(cell.xMin, cell.yMax));
                // Clockwise seen from outside: Unity's front faces.
                triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            float h = Half;
            Face(new Vector3(0f, 0f, h), new Vector3(-h, 0f, 0f), new Vector3(0f, h, 0f), FrontCell);   // +Z, toward the camera
            Face(new Vector3(0f, 0f, -h), new Vector3(h, 0f, 0f), new Vector3(0f, h, 0f), FrontCell);   // -Z
            Face(new Vector3(h, 0f, 0f), new Vector3(0f, 0f, h), new Vector3(0f, h, 0f), SideCell);     // +X
            Face(new Vector3(-h, 0f, 0f), new Vector3(0f, 0f, -h), new Vector3(0f, h, 0f), SideCell);   // -X
            Face(new Vector3(0f, h, 0f), new Vector3(h, 0f, 0f), new Vector3(0f, 0f, h), LidCell);      // +Y
            Face(new Vector3(0f, -h, 0f), new Vector3(h, 0f, 0f), new Vector3(0f, 0f, -h), SideCell);   // -Y

            void Cylinder(Vector3 a, Vector3 b, float radius, float uBody, float uBand, int segments)
            {
                Vector3 axis = (b - a).normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
                Vector3 other = Vector3.Cross(axis, side);
                // Repeated stops with a new u make hard seams round the label band.
                float[] stops = { 0f, .38f, .38f, .62f, .62f, 1f };
                float[] us = { uBody, uBody, uBand, uBand, uBody, uBody };
                int first = vertices.Count;
                for (int s = 0; s < stops.Length; s++)
                {
                    Vector3 c = Vector3.Lerp(a, b, stops[s]);
                    for (int k = 0; k <= segments; k++)
                    {
                        float angle = k / (float)segments * Mathf.PI * 2f;
                        vertices.Add(c + (side * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * radius);
                        uvs.Add(new Vector2(us[s], .25f));
                    }
                }
                int columns = segments + 1;
                for (int s = 0; s < stops.Length - 1; s++)
                {
                    if (stops[s + 1] - stops[s] < 1e-4f) continue;
                    for (int k = 0; k < segments; k++)
                    {
                        int p = first + s * columns + k, q = p + 1, r = p + columns, t = r + 1;
                        triangles.AddRange(new[] { p, q, t, p, t, r });
                    }
                }
                int cap = vertices.Count;
                vertices.Add(b);
                uvs.Add(new Vector2(DarkU, .25f));
                int ring = first + (stops.Length - 1) * columns;
                for (int k = 0; k < segments; k++) triangles.AddRange(new[] { cap, ring + k, ring + k + 1 });
            }
            var sticks = new[] { (new Vector3(-.13f, h - .1f, .02f), new Vector3(-.2f, h + StickLength, .05f)),
                                 (new Vector3(.12f, h - .1f, -.02f), new Vector3(.12f, h + StickLength + .05f, -.02f)),
                                 (new Vector3(.02f, h - .1f, -.15f), new Vector3(.08f, h + StickLength - .06f, -.24f)) };
            foreach (var (a, b) in sticks) Cylinder(a, b, StickRadius, StickU, LabelU, 10);
            // The fuse, out of the tallest stick and curling over.
            Cylinder(new Vector3(.12f, h + StickLength + .04f, -.02f), FuseTip, .016f, DarkU, DarkU, 5);

            var mesh = new Mesh { name = "QuarryCrateMesh" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        /// <summary>An inverted hull a little larger than the box: the crate's dark outline.</summary>
        private static Mesh OutlineMesh()
        {
            float h = Half + .035f;
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++) corners[i] = new Vector3((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h);
            // Each face is listed clockwise from outside and wound backwards, so only the hull's
            // far side draws round the crate's silhouette: an outline.
            int[][] faces =
            {
                new[] { 4, 5, 7, 6 }, new[] { 1, 0, 2, 3 }, new[] { 5, 1, 3, 7 }, new[] { 0, 4, 6, 2 }, new[] { 2, 6, 7, 3 }, new[] { 0, 1, 5, 4 },
            };
            var triangles = new List<int>();
            foreach (var f in faces) triangles.AddRange(new[] { f[0], f[2], f[1], f[0], f[3], f[2] });
            var mesh = new Mesh { name = "QuarryCrateOutlineMesh" };
            mesh.SetVertices(corners);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
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
            var glow = _kit.BakeTexture("QuarryGlow", 128, 128, (u, v) => Alpha(Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f)));
            return new Materials
            {
                Crate = Unlit("QuarryCrate", _kit.BakeTexture("QuarryCrateAtlas", 512, 512, CratePixel), new Color(1.04f, 1.04f, 1.04f)),
                Outline = Unlit("QuarryCrateOutline", null, Ink),
                Halo = _kit.ParticleMaterial("QuarryCrateHalo", glow, new Color(Orange.r * 1.2f, Orange.g * 1.2f, Orange.b * 1.2f, .5f)),
                Pool = _kit.ParticleMaterial("QuarryCratePool", glow, new Color(Orange.r, Orange.g, Orange.b, .3f)),
                Debris = Mat("QuarryDebris", 256, 256, (u, v) => Sheet(u, v, DebrisPixel)),
                Confetti = Mat("QuarryConfetti", 256, 256, (u, v) => Sheet(u, v, ConfettiPixel)),
                RingHazard = Mat("QuarryRingHazard", 256, 256, (u, v) => RingPixel(P(u, v), Ink, Hazard, HazardLight)),
                RingRed = Mat("QuarryRingRed", 256, 256, (u, v) => RingPixel(P(u, v), Ink, Red, RedLight)),
                MoteHazard = Mat("QuarryMoteHazard", 64, 64, (u, v) => MotePixel(P(u, v), Ink, Hazard, HazardLight)),
                MoteRed = Mat("QuarryMoteRed", 64, 64, (u, v) => MotePixel(P(u, v), RedDark, Red, RedLight)),
                MoteOrange = Mat("QuarryMoteOrange", 64, 64, (u, v) => MotePixel(P(u, v), RedDark, Orange, OrangeLight)),
                FrameHazard = Mat("QuarryFrameHazard", 8, 64, (_, v) => FramePixel(v, Ink, Hazard, HazardLight)),
                FrameRed = Mat("QuarryFrameRed", 8, 64, (_, v) => FramePixel(v, Ink, Red, RedLight)),
                BeamOrange = Mat("QuarryBeamOrange", 64, 256, (u, v) => BeamPixel(u, v, RedDark, Orange, Cream)),
                BeamRed = Mat("QuarryBeamRed", 64, 256, (u, v) => BeamPixel(u, v, RedDark, Red, Cream)),
                Flash = Manki("MankiFlash"),
                Pow = Manki("MankiPow"),
                Fireball = Manki("MankiFireball"),
                Puff = Manki("MankiPuff"),
                Spark = Manki("MankiSpark"),
                Grin = Manki("MankiGrin"),
                MankiRing = Manki("MankiRing"),
                MankiGlow = Manki("MankiGlowAdd"),
            };
        }

        private static Material Manki(string name)
            => AssetDatabase.LoadAssetAtPath<Material>(MankiFx + name + ".mat")
               ?? throw new InvalidOperationException($"Manki's {name}.mat is missing; run sloparena.presentation.manki.build first.");

        /// <summary>
        /// The crate atlas. Front: red planks in a dark frame, the yellow TNT label and a hazard
        /// band along the foot. Lid: lighter planks. Sides: darker planks. Bottom right: the
        /// sticks' red, label cream and dark ends. Unlit, so the faces carry their own shade.
        /// </summary>
        private static Color CratePixel(float u, float v)
        {
            if (u >= .5f && v < .5f)
                return u < .7f ? Red : u < .86f ? Label : Fuse;
            bool front = u < .5f && v >= .5f, lid = u >= .5f && v >= .5f;
            var p = new Vector2((u % .5f) * 4f - 1f, (v % .5f) * 4f - 1f);
            float texel = 1f / 128f;
            float edge = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
            float shade = lid ? 1.12f : front ? 1f : .8f;
            // Planks: horizontal boards with dark seams and a little grain.
            float board = Mathf.Abs(Mathf.Repeat(p.y * 2f + .5f, 1f) - .5f);
            float grain = ValueNoise(u * 3f, v * 40f, 6f, lid ? 3 : 1);
            Color wood = Color.Lerp(Red, RedLight, .12f * grain) * shade;
            wood = Color.Lerp(wood, RedDark, Step(.44f, .48f, board) * .8f);
            // Frame round the face, inked at the very edge.
            wood = Color.Lerp(wood, RedDark * (shade * 1.05f), Step(.8f, .82f, edge));
            wood = Color.Lerp(wood, Ink, Step(.95f, .97f, edge));
            wood.a = 1f;
            if (!front) return wood;
            // Hazard band along the foot, inside the frame.
            if (p.y < -.6f && p.y > -.8f && Mathf.Abs(p.x) < .8f)
            {
                bool stripe = Mathf.Repeat((p.x + p.y) * 3.2f, 1f) < .5f;
                wood = stripe ? Hazard : Ink;
            }
            // The label: a rounded yellow card with an ink border and TNT in block capitals.
            float card = Box(p - new Vector2(0f, .08f), new Vector2(.6f, .34f)) - .06f;
            Color label = Color.Lerp(Label, HazardDeep, .25f * Step(-.02f, .1f, p.y - .3f));
            label = Color.Lerp(label, Ink, Step(-.05f, -.035f, card));
            float letters = Mathf.Min(Mathf.Min(T(p - new Vector2(-.36f, .08f)), N(p - new Vector2(0f, .08f))), T(p - new Vector2(.36f, .08f)));
            label = Color.Lerp(label, Ink, Mathf.Clamp01(.5f - letters / (texel * 2f)));
            label.a = 1f;
            return Color.Lerp(wood, label, Mathf.Clamp01(.5f - card / (texel * 2f)));
        }

        private static float T(Vector2 p) => Mathf.Min(Box(p - new Vector2(0f, .17f), new Vector2(.14f, .045f)), Box(p - new Vector2(0f, -.02f), new Vector2(.045f, .2f)));

        private static float N(Vector2 p)
        {
            float stems = Mathf.Min(Box(p - new Vector2(-.11f, 0f), new Vector2(.045f, .22f)), Box(p - new Vector2(.11f, 0f), new Vector2(.045f, .22f)));
            return Mathf.Min(stems, Segment(p, new Vector2(-.11f, .2f), new Vector2(.11f, -.2f)) - .05f);
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        /// <summary>Flying bits: two red planks, a stick of dynamite and a scrap of the yellow label.</summary>
        private static Color DebrisPixel(Vector2 p, int cell)
        {
            switch (cell)
            {
                case 0: return Bands(Box(Rotate(p, 12f), new Vector2(.82f, .22f)) * 64f, 2f, 5f, Ink, RedDark, Red);
                case 1: return Bands(Box(Rotate(p, -20f), new Vector2(.7f, .26f)) * 64f, 2f, 4f, Ink, Red, RedLight);
                case 2:
                {
                    float d = Box(Rotate(p, 35f), new Vector2(.72f, .2f));
                    var color = Bands(d * 64f, 2f, 4f, Ink, Red, RedLight);
                    if (d < 0f && Mathf.Abs(Rotate(p, 35f).x) < .2f) color = new Color(Label.r, Label.g, Label.b, color.a);
                    return color;
                }
                default: return Bands(Polygon(p, Scrap) * 64f, 2f, 4f, Ink, Hazard, HazardLight);
            }
        }

        private static readonly Vector2[] Scrap = { new(-.7f, -.5f), new(.5f, -.7f), new(.75f, .3f), new(.1f, .7f), new(-.6f, .4f) };

        /// <summary>Hazard-tape strips (yellow and black), a red strip and an orange one.</summary>
        private static Color ConfettiPixel(Vector2 p, int cell)
        {
            var q = Rotate(p, cell * 23f - 30f);
            float d = Box(q, new Vector2(.8f, .26f)) * 64f;
            if (cell >= 2) return Bands(d, 1.5f, 3f, Ink, cell == 2 ? Red : Orange, cell == 2 ? RedLight : OrangeLight);
            var color = Bands(d, 1.5f, 99f, Ink, Hazard, Hazard);
            if (d < -1.5f && Mathf.Repeat((q.x + q.y) * 2.4f + cell * .5f, 1f) < .5f) color = new Color(Ink.r, Ink.g, Ink.b, color.a);
            return color;
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

        /// <summary>A flare of light: a pale core, the accent, then a soft edge; it fades toward the top.</summary>
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

        /// <summary>An opaque URP Unlit material: the crate keeps its painted shade under any stage light.</summary>
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
