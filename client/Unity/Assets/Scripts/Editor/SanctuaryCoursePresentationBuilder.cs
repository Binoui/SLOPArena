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
    /// Rebuilds the FightGuy Sanctuary course's feel (docs/design/arcade-phase-4.md, "Feel pass"):
    /// gold paper lanterns for targets that burst into paper shreds and a rising flame, jade and
    /// red checkpoint frames, red pillars over missed lanterns, the gold shrine pillar and
    /// prayer-flag confetti. The course speaks in the sanctuary's gold, red and jade, not
    /// FightGuy's blue ki, so other fighters could run it later. Writes
    /// Stages/fightguy_sanctuary/Presentation/Course and Resources/Courses/fightguy_sanctuary;
    /// tools/sfx/course_clips.py makes the sounds. Plumbing: <see cref="PresentationBuilderKit"/>.
    /// </summary>
    public static class SanctuaryCoursePresentationBuilder
    {
        private const string Root = "Assets/Stages/fightguy_sanctuary/Presentation/Course";
        private const string SetPath = "Assets/Resources/" + CoursePresentationSet.ResourcesFolder + "/fightguy_sanctuary.asset";
        // The authored target radius; CharacterCoursePresentation scales the prefabs to others.
        private const float TargetRadius = .65f;
        // The paper body nearly fills the hit sphere: an oblate lantern open at both ends.
        private const float BodyRadius = .6f, BodyHalfHeight = .5f, Opening = .21f, SealRadius = .13f;
        private const int LanternWidth = 512, LanternHeight = 256;
        // Paper occupies u 0.003–0.747 of the lantern atlas; the lacquer column sits at u 0.875.
        private const float PaperStart = .003f, PaperSpan = .744f, LacquerU = .875f;
        private static readonly float Phi0 = Mathf.Asin(Opening / BodyRadius);
        private static readonly float TexelMetres = Mathf.PI * 2f * BodyRadius / (LanternWidth * PaperSpan);
        private static float _bodyArc;

        private static readonly Color Rim = Hex(0x2b120c);
        private static readonly Color GoldDeep = Hex(0xc8811a);
        private static readonly Color Gold = Hex(0xf6b52a);
        private static readonly Color GoldLight = Hex(0xffe7a3);
        private static readonly Color Glow = Hex(0xffd56b);
        private static readonly Color RedDark = Hex(0x8e1d17);
        private static readonly Color Red = Hex(0xd23a2c);
        private static readonly Color RedLight = Hex(0xff7a5c);
        private static readonly Color Cream = Hex(0xfff6de);
        private static readonly Color JadeDark = Hex(0x0c4d44);
        private static readonly Color Jade = Hex(0x2fc1aa);
        private static readonly Color JadeLight = Hex(0xc8fff1);
        private static readonly Color Flame = Hex(0xff8a1c);
        private static readonly Color FlameCore = Hex(0xfff1b0);
        // Prayer flags: blue, white, red, green and yellow, plus gold.
        private static readonly Color[] FlagColors =
            { Hex(0x2f6fd6), Hex(0xf4f4f0), Hex(0xd63a2f), Hex(0x2f9e4f), Hex(0xf2c230), Hex(0xf6b52a), Hex(0x2f6fd6), Hex(0xd63a2f) };

        private static PresentationBuilderKit _kit;
        private static Materials _m;
        private static Mesh _quad, _beamQuad;

        private sealed class Materials
        {
            public Material Lantern, Outline, Halo, Pool, Glow, Burst, RingGold, RingRed, Shreds, Flame, MoteJade, MoteRed,
                MoteGold, FrameJade, FrameRed, BeamGold, BeamRed, Flags;
        }

        private readonly struct ProfilePoint
        {
            public readonly float R, Y, V;
            public ProfilePoint(float r, float y, float v) { R = r; Y = y; V = v; }
        }

        [CliCommand("sloparena.presentation.course.sanctuary.build", "Rebuild the FightGuy Sanctuary course's lantern targets, break, checkpoint, beacon and finish prefabs and its Resources/Courses set in Edit Mode.", MainThreadRequired = true, Tags = new[] { "presentation", "arcade" })]
        public static object Build()
        {
            using var kit = PresentationBuilderKit.ForFolder(Root, audioFolder: "Course");
            _kit = kit;
            _bodyArc = BodyArc();
            _m = BuildMaterials();
            _quad = Quad("SanctuaryQuad", -.5f);
            _beamQuad = Quad("SanctuaryBeamQuad", 0f);
            var lantern = Lantern();
            var set = AssetDatabase.LoadAssetAtPath<CoursePresentationSet>(SetPath);
            bool created = set == null;
            if (created) set = ScriptableObject.CreateInstance<CoursePresentationSet>();
            set.Target = lantern;
            set.TargetRadius = TargetRadius;
            set.Break = LanternBreak();
            set.Marker = CheckpointMarker();
            set.MissedBeacon = Beacon("SanctuaryMissedBeacon", _m.BeamRed, new Vector2(.9f, 16f), _m.RingRed, 1.7f, floorRing: false,
                _m.MoteRed, 4f, .2f, new Vector2(1.2f, 2.2f), 2.2f);
            set.FinishBeacon = Beacon("SanctuaryShrinePillar", _m.BeamGold, new Vector2(2.2f, 42f), _m.RingGold, 5f, floorRing: true,
                _m.MoteGold, 10f, .7f, new Vector2(2f, 4f), 3f);
            set.FinishBurst = FinishBurst();
            set.SoundFolder = "Audio/SFX/Course";
            if (created)
            {
                PresentationBuilderKit.EnsureFolder("Assets/Resources/" + CoursePresentationSet.ResourcesFolder);
                AssetDatabase.CreateAsset(set, SetPath);
            }
            else EditorUtility.SetDirty(set);
            return kit.Close(new { set = SetPath });
        }

        [MenuItem("Tools/SLOPArena/Presentation/Rebuild Sanctuary Course Presentation")]
        private static void Menu() => Debug.Log("[SanctuaryCoursePresentationBuilder] " + Newtonsoft.Json.JsonConvert.SerializeObject(Build()));

        // ── Targets ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The gold paper lantern: ribbed paper lit from inside with a red seal on the front, red
        /// lacquer caps with gold trim and a tassel, a dark outline, a warm halo and a pool of light
        /// on the floor below it.
        /// </summary>
        private static GameObject Lantern()
        {
            var root = _kit.NewRoot("SanctuaryLantern", attackBound: false);
            var lantern = root.AddComponent<CourseLantern>();
            var (body, outline) = LanternMeshes();
            var model = Child(root, "Model");
            lantern.Model = model.transform;
            AddMesh(Child(model, "Paper"), body, _m.Lantern);
            AddMesh(Child(model, "Outline"), outline, _m.Outline);
            var halo = Child(root, "Halo");
            halo.transform.localScale = Vector3.one * 2.7f;
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

        /// <summary>
        /// The break: a cream flash, a gold comic burst and ring, sixteen paper shreds that flutter
        /// down and the candle's little flame floating up.
        /// </summary>
        private static GameObject LanternBreak()
        {
            var root = _kit.NewRoot("SanctuaryLanternBreak", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var flash = Particles(root, "Flash", _m.Glow, .3f, scaling: scaled);
            Burst(flash, 1);
            var main = flash.main;
            main.startLifetime = .16f;
            main.startSize = 2.4f;
            main.startColor = new Color(Cream.r, Cream.g, Cream.b, .7f);
            SizeOverLife(flash, Curve((0f, .6f), (.25f, 1f), (1f, 1.1f)));
            Fade(flash, .2f);
            Behind(flash);

            var star = Particles(root, "Burst", _m.Burst, .4f, scaling: scaled);
            Burst(star, 1);
            main = star.main;
            main.startLifetime = .2f;
            main.startSize = 1.5f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(star, Curve((0f, .45f), (.18f, 1.12f), (.5f, 1f), (1f, .7f)));
            Fade(star, .55f);
            InFront(star);

            var ring = Particles(root, "Ring", _m.RingGold, .8f, scaling: scaled);
            Burst(ring, 1);
            main = ring.main;
            main.startLifetime = .55f;
            main.startSize = 1.5f;
            SizeOverLife(ring, Curve((0f, .7f), (.25f, 1.2f), (1f, 1.75f)));
            Fade(ring, .35f);
            ring.GetComponent<ParticleSystemRenderer>().sortingFudge = -8f;

            var shreds = Particles(root, "Paper shreds", _m.Shreds, 1.8f, scaling: scaled);
            Burst(shreds, 18);
            main = shreds.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.18f, .3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = .5f;
            Sphere(shreds, .3f);
            Limit(shreds, 1.2f, .09f);
            Flutter(shreds, .7f, .9f);
            RandomFrame(shreds, 2, 2);
            Tumble(shreds, 9f);
            Fade(shreds, .7f);
            shreds.GetComponent<ParticleSystemRenderer>().sortingFudge = -6f;

            foreach (var (name, material, size, color, life) in new[]
                     {
                         ("Flame glow", _m.Glow, 1.1f, new Color(Flame.r, Flame.g, Flame.b, .4f), .95f),
                         ("Flame", _m.Flame, .5f, Color.white, .95f),
                     })
            {
                var flame = Particles(root, name, material, 1.2f, scaling: scaled);
                Burst(flame, 1);
                main = flame.main;
                main.startDelay = .05f;
                main.startLifetime = life;
                main.startSize = size;
                main.startColor = color;
                Rise(flame, .75f);
                SizeOverLife(flame, Curve((0f, .5f), (.15f, 1f), (.7f, .8f), (1f, .2f)));
                Fade(flame, .6f);
                if (material == _m.Glow) Behind(flame);
                else InFront(flame);
            }
            return Save(root);
        }

        // ── Checkpoints, pillars and the finish ──────────────────────────────────

        /// <summary>A floor frame sized at runtime: jade when crossing it will save, red when it cannot.</summary>
        private static GameObject CheckpointMarker()
        {
            var root = _kit.NewRoot("SanctuaryCheckpointMarker", attackBound: false);
            var marker = root.AddComponent<CourseCheckpointMarker>();
            // Above the stage's own checkpoint bars (8 cm tall), so the lit frame covers them.
            var frame = Child(root, "Frame");
            frame.transform.localPosition = new Vector3(0f, .1f, 0f);
            marker.Frame = frame.AddComponent<MeshFilter>();
            marker.FrameRenderer = frame.AddComponent<MeshRenderer>();
            Quiet(marker.FrameRenderer);
            marker.FrameRenderer.sharedMaterial = _m.FrameJade;
            marker.FrameRenderer.enabled = false;
            marker.ArmedMaterial = _m.FrameJade;
            marker.LockedMaterial = _m.FrameRed;
            marker.Width = .7f;
            marker.ArmedMotes = Motes(root, "Armed motes", _m.MoteJade, 12f);
            marker.LockedMotes = Motes(root, "Locked motes", _m.MoteRed, 7f);
            return Save(root);
        }

        private static ParticleSystem Motes(GameObject root, string name, Material material, float rate)
        {
            var motes = Particles(root, name, material, 2f);
            var main = motes.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .2f);
            main.maxParticles = 48;
            var emission = motes.emission;
            emission.rateOverTime = rate;
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.BoxEdge;
            shape.scale = new Vector3(6f, .02f, 2.8f);
            Drift(motes, .08f, .7f, 1.4f);
            Spin(motes, 120f);
            SizeOverLife(motes, Curve((0f, .4f), (.2f, 1f), (1f, .5f)));
            Fade(motes, .55f);
            return motes;
        }

        /// <summary>A light pillar that faces the camera, with a ripple ring and rising motes.</summary>
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
            main.startLifetime = 1.1f;
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
            Drift(motes, .05f, moteSpeed.x, moteSpeed.y);
            Spin(motes, 90f);
            Fade(motes, .6f);
            return Save(root);
        }

        /// <summary>Crossing the finish: prayer-flag confetti, gold rings on the floor and glints.</summary>
        private static GameObject FinishBurst()
        {
            var root = _kit.NewRoot("SanctuaryFinishBurst", attackBound: false);
            var flags = Particles(root, "Prayer flags", _m.Flags, 4f);
            flags.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            Burst(flags, 80);
            var main = flags.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.8f, 3.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.24f, .38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5.5f, 9.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = .45f;
            main.maxParticles = 96;
            var shape = flags.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = .35f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Limit(flags, 1.7f, .07f);
            Flutter(flags, .9f, .7f);
            RandomFrame(flags, 4, 2);
            Tumble(flags, 6f);
            Fade(flags, .8f);

            var floor = Floor(root, 3f, -.85f);
            FloorRing(floor, "Gold ring", 6.5f, 0f, .9f);
            FloorRing(floor, "Inner ring", 4.2f, .12f, .8f);

            var glints = Particles(root, "Glints", _m.MoteGold, 1.6f);
            glints.transform.localPosition = new Vector3(0f, .6f, 0f);
            Burst(glints, 16);
            main = glints.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.7f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.gravityModifier = .3f;
            Sphere(glints, .4f);
            Spin(glints, 200f);
            Fade(glints, .5f);
            return Save(root);
        }

        private static void FloorRing(GameObject parent, string name, float size, float delay, float life)
        {
            var ring = Particles(parent, name, _m.RingGold, delay + life + .1f);
            Burst(ring, 1);
            var main = ring.main;
            main.startDelay = delay;
            main.startLifetime = life;
            main.startSize = size;
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeOverLife(ring, Curve((0f, .25f), (.3f, .8f), (1f, 1f)));
            Fade(ring, .3f);
        }

        // ── Lantern geometry ─────────────────────────────────────────────────────

        /// <summary>The lantern and its outline hull, both lathed from the same profiles.</summary>
        private static (Mesh body, Mesh outline) LanternMeshes()
        {
            Func<float, float> paper = t => PaperStart + t * PaperSpan, lacquer = _ => LacquerU;
            var parts = new (ProfilePoint[] profile, Func<float, float> u, int segments, float outline)[]
            {
                (BodyProfile(), paper, 40, .02f),
                (new ProfilePoint[]
                {
                    new(0f, -.6f, .1f), new(.255f, -.6f, .1f), new(.255f, -.6f, .92f), new(.28f, -.575f, .92f),
                    new(.28f, -.575f, .25f), new(.28f, -.5f, .6f), new(.28f, -.465f, .8f), new(.28f, -.43f, .8f),
                }, lacquer, 40, .018f),
                (new ProfilePoint[]
                {
                    new(.28f, .43f, .3f), new(.28f, .5f, .5f), new(.28f, .555f, .78f), new(.28f, .575f, .78f),
                    new(.28f, .575f, .92f), new(.255f, .6f, .92f), new(.255f, .6f, .76f), new(0f, .6f, .76f),
                }, lacquer, 40, .018f),
                (new ProfilePoint[] { new(.06f, .6f, .92f), new(.06f, .65f, .92f), new(0f, .66f, .92f) }, lacquer, 12, .01f),
                (new ProfilePoint[]
                {
                    new(0f, -.95f, .3f), new(.065f, -.95f, .3f), new(.055f, -.88f, .45f), new(.032f, -.74f, .55f),
                    new(.045f, -.72f, .65f), new(.045f, -.69f, .65f), new(.016f, -.675f, .6f), new(.016f, -.6f, .6f),
                }, lacquer, 12, .01f),
            };
            Mesh Build(string name, bool hull)
            {
                var vertices = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                foreach (var (profile, u, segments, outline) in parts)
                    Lathe(vertices, uvs, triangles, profile, u, segments, hull ? outline : 0f, hull);
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return _kit.SaveMesh(mesh);
            }
            return (Build("SanctuaryLanternMesh", false), Build("SanctuaryLanternOutlineMesh", true));
        }

        /// <summary>The paper: an oblate ellipse from the bottom opening to the top one; v follows the arc.</summary>
        private static ProfilePoint[] BodyProfile()
        {
            const int steps = 18;
            var points = new ProfilePoint[steps + 1];
            for (int k = 0; k <= steps; k++)
            {
                float phi = Phi0 + k / (float)steps * (Mathf.PI - 2f * Phi0);
                points[k] = new ProfilePoint(BodyRadius * Mathf.Sin(phi), -BodyHalfHeight * Mathf.Cos(phi), k / (float)steps);
            }
            return points;
        }

        private static float BodyArc()
        {
            const int steps = 200;
            float length = 0f;
            for (int k = 0; k < steps; k++)
            {
                float a = Phi0 + k / (float)steps * (Mathf.PI - 2f * Phi0), b = Phi0 + (k + 1) / (float)steps * (Mathf.PI - 2f * Phi0);
                length += Vector2.Distance(new Vector2(BodyRadius * Mathf.Sin(a), BodyHalfHeight * Mathf.Cos(a)),
                    new Vector2(BodyRadius * Mathf.Sin(b), BodyHalfHeight * Mathf.Cos(b)));
            }
            return length;
        }

        /// <summary>
        /// Revolves a bottom-to-top (radius, height, v) profile around +Y; u = 0.5 faces +Z, which
        /// CourseLantern turns toward the camera. A positive <paramref name="offset"/> pushes the
        /// surface out along mitred profile normals; <paramref name="inward"/> flips the faces for
        /// an outline hull. Repeated points with a new v make hard texture seams.
        /// </summary>
        private static void Lathe(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, IReadOnlyList<ProfilePoint> profile,
            Func<float, float> u, int segments, float offset, bool inward)
        {
            int first = vertices.Count, columns = segments + 1;
            for (int j = 0; j < profile.Count; j++)
            {
                Vector2 normal = offset > 0f ? ProfileNormal(profile, j) : Vector2.zero;
                float r = Mathf.Max(0f, profile[j].R + normal.x * offset), y = profile[j].Y + normal.y * offset;
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments, theta = t * Mathf.PI * 2f;
                    vertices.Add(new Vector3(-r * Mathf.Sin(theta), y, -r * Mathf.Cos(theta)));
                    uvs.Add(new Vector2(u(t), profile[j].V));
                }
            }
            for (int j = 0; j < profile.Count - 1; j++)
            for (int i = 0; i < segments; i++)
            {
                int a = first + j * columns + i, b = a + 1, c = a + columns, d = c + 1;
                if (inward) triangles.AddRange(new[] { a, c, d, b, a, d });
                else triangles.AddRange(new[] { d, c, a, d, a, b });
            }
        }

        /// <summary>The mitred outward normal at a profile point, skipping zero-length seam segments.</summary>
        private static Vector2 ProfileNormal(IReadOnlyList<ProfilePoint> profile, int index)
        {
            Vector2 before = SegmentNormal(profile, index, -1), after = SegmentNormal(profile, index, 1);
            Vector2 sum = before + after;
            if (sum.sqrMagnitude < 1e-8f) sum = before.sqrMagnitude > 0f ? before : after;
            if (sum.sqrMagnitude < 1e-8f) return Vector2.zero;
            Vector2 normal = sum.normalized;
            float cosine = Vector2.Dot(normal, before.sqrMagnitude > 0f ? before : after);
            return normal / Mathf.Max(.55f, cosine);
        }

        private static Vector2 SegmentNormal(IReadOnlyList<ProfilePoint> profile, int index, int step)
        {
            for (int k = index; k + step >= 0 && k + step < profile.Count; k += step)
            {
                var a = profile[Math.Min(k, k + step)];
                var b = profile[Math.Max(k, k + step)];
                var direction = new Vector2(b.R - a.R, b.Y - a.Y);
                if (direction.sqrMagnitude > 1e-10f) return new Vector2(direction.y, -direction.x).normalized;
            }
            return Vector2.zero;
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
            var glow = _kit.BakeTexture("SanctuaryGlow", 128, 128, (u, v) => Alpha(Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f)));
            return new Materials
            {
                Lantern = Unlit("SanctuaryLanternPaper", _kit.BakeTexture("SanctuaryLantern", LanternWidth, LanternHeight, LanternPixel),
                    new Color(1.06f, 1.06f, 1.06f)),
                Outline = Unlit("SanctuaryLanternOutline", null, Rim),
                Halo = _kit.ParticleMaterial("SanctuaryLanternHalo", glow, new Color(Glow.r * 1.1f, Glow.g * 1.1f, Glow.b * 1.1f, .5f)),
                Pool = _kit.ParticleMaterial("SanctuaryLanternPool", glow, new Color(Gold.r, Gold.g, Gold.b, .32f)),
                Glow = _kit.ParticleMaterial("SanctuaryGlowAlpha", glow),
                Burst = Mat("SanctuaryBurst", 256, 256, (u, v) => Bands(Polygon(P(u, v), StarPoints) * 128f, 3f, 12f, Rim, Gold, Cream)),
                RingGold = Mat("SanctuaryRingGold", 256, 256, (u, v) => RingPixel(P(u, v), Rim, Gold, GoldLight)),
                RingRed = Mat("SanctuaryRingRed", 256, 256, (u, v) => RingPixel(P(u, v), Rim, Red, RedLight)),
                Shreds = Mat("SanctuaryShreds", 256, 256, (u, v) => Sheet(u, v, ShredPixel)),
                Flame = Mat("SanctuaryFlame", 128, 128, (u, v) => Bands(Capsule(P(u, v), new Vector2(0f, -.38f), new Vector2(0f, .82f), .46f, .02f) * 64f,
                    2.5f, 9f, Rim, Flame, FlameCore)),
                MoteJade = Mat("SanctuaryMoteJade", 64, 64, (u, v) => MotePixel(P(u, v), JadeDark, Jade, JadeLight)),
                MoteRed = Mat("SanctuaryMoteRed", 64, 64, (u, v) => MotePixel(P(u, v), RedDark, Red, RedLight)),
                MoteGold = Mat("SanctuaryMoteGold", 64, 64, (u, v) => MotePixel(P(u, v), GoldDeep, Gold, Cream)),
                FrameJade = Mat("SanctuaryFrameJade", 8, 64, (_, v) => FramePixel(v, JadeDark, Jade, JadeLight)),
                FrameRed = Mat("SanctuaryFrameRed", 8, 64, (_, v) => FramePixel(v, RedDark, Red, RedLight)),
                BeamGold = Mat("SanctuaryBeamGold", 64, 256, (u, v) => BeamPixel(u, v, GoldDeep, Gold, Cream)),
                BeamRed = Mat("SanctuaryBeamRed", 64, 256, (u, v) => BeamPixel(u, v, RedDark, Red, Cream)),
                Flags = Mat("SanctuaryFlags", 256, 128, FlagsPixel),
            };
        }

        private static readonly Vector2[] StarPoints = StarOutline(10, .48f, 11);

        private static readonly Vector2[][] ShredShapes =
        {
            new Vector2[] { new(-.75f, -.4f), new(.7f, -.62f), new(.62f, .5f), new(-.58f, .42f) },
            new Vector2[] { new(-.6f, -.7f), new(.55f, -.45f), new(.72f, .66f), new(-.7f, .38f) },
            new Vector2[] { new(-.8f, -.25f), new(.66f, -.5f), new(.8f, .3f), new(-.5f, .55f) },
            new Vector2[] { new(-.45f, -.72f), new(.62f, -.6f), new(.4f, .7f), new(-.66f, .5f) },
        };

        /// <summary>
        /// The lantern atlas. Paper, lit from inside: a pale core facing the camera, gold, then
        /// deep gold toward the silhouette, sixteen ribs (none down the front), darker rims at the
        /// openings and a round red seal on the front. The lacquer column holds the caps' bands.
        /// </summary>
        private static Color LanternPixel(float u, float v)
        {
            if (u >= .75f) return v < .2f ? RedDark : v < .7f ? Red : v < .84f ? RedLight : Gold;
            float around = Mathf.Clamp01((u - PaperStart) / PaperSpan);
            float angle = (around - .5f) * Mathf.PI * 2f;
            float latitude = Mathf.Sin(Phi0 + v * (Mathf.PI - 2f * Phi0));
            float shade = Mathf.Cos(angle) * latitude;
            Color paper = Color.Lerp(GoldDeep, Gold, Step(.17f, .23f, shade));
            paper = Color.Lerp(paper, GoldLight, Step(.6f, .66f, shade));
            float rib = Mathf.Abs(around * 16f - Mathf.Floor(around * 16f) - .5f) * (LanternWidth * PaperSpan / 16f);
            paper = Color.Lerp(paper, GoldDeep, .75f * Mathf.Clamp01(1.3f - rib));
            paper = Color.Lerp(paper, GoldDeep, Mathf.Clamp01(7f - Mathf.Min(v, 1f - v) * LanternHeight));
            float sx = angle * BodyRadius * latitude, sy = (v - .5f) * _bodyArc;
            float seal = (Mathf.Sqrt(sx * sx + sy * sy) - SealRadius) / TexelMetres;
            return Over(paper, Bands(seal, 2.5f, 4f, RedDark, Red, RedLight));
        }

        private static Color RingPixel(Vector2 p, Color dark, Color accent, Color light)
            => Bands((Mathf.Abs(p.magnitude - .8f) - .085f) * 128f, 2.5f, 4.5f, dark, accent, light);

        private static Color ShredPixel(Vector2 p, int cell)
        {
            float d = Polygon(p, ShredShapes[cell]) * 64f;
            var color = cell switch
            {
                1 => Bands(d, 2f, 5f, Rim, Red, RedLight),
                3 => Bands(d, 2f, 5f, Rim, Cream, Color.white),
                _ => Bands(d, 2f, 5f, Rim, Gold, GoldLight),
            };
            if (cell == 2 && d < -2f) color = Color.Lerp(color, GoldDeep, .8f * Mathf.Clamp01(1.5f - Mathf.Abs(p.x - .1f) * 64f));
            return color;
        }

        private static Color MotePixel(Vector2 p, Color dark, Color accent, Color light)
            => Bands(Rhombus(p, new Vector2(.55f, .92f)) * 32f, 2f, 4f, dark, accent, light);

        /// <summary>
        /// Across the frame band: a solid accent line with a thin dark edge and a narrow pale
        /// centre (a pale line vanishes on the sanctuary's light stone), and a glow either side.
        /// </summary>
        private static Color FramePixel(float v, Color dark, Color accent, Color light)
        {
            float x = Mathf.Abs(v - .5f);
            var line = Bands((x - .2f) * 64f, 1.5f, 8f, dark, accent, light);
            float glow = x > .2f ? .65f * Mathf.Pow(Mathf.Clamp01(1f - (x - .2f) / .3f), 1.6f) : 0f;
            return Over(new Color(accent.r, accent.g, accent.b, glow), line);
        }

        /// <summary>A pillar of light: a pale core, the accent, then a soft edge; it fades toward the top.</summary>
        private static Color BeamPixel(float u, float v, Color edge, Color accent, Color core)
        {
            float x = Mathf.Abs(u - .5f) * 2f;
            Color color = Color.Lerp(accent, core, Step(.2f, .14f, x));
            color = Color.Lerp(color, edge, Step(.44f, .5f, x));
            float alpha = x < .2f ? .9f : x < .46f ? .7f : .55f * Mathf.Pow(Mathf.Clamp01(1f - (x - .46f) / .54f), 1.6f);
            color.a = alpha * Mathf.Pow(1f - v, .9f) * Step(0f, .03f, v);
            return color;
        }

        /// <summary>A 4×2 sheet of prayer flags with a slight wave and a printed square.</summary>
        private static Color FlagsPixel(float u, float v)
        {
            int cx = Mathf.Min(3, (int)(u * 4f)), cy = v < .5f ? 1 : 0, cell = cy * 4 + cx;
            var p = new Vector2((u * 4f - cx) * 2f - 1f, (v % .5f) * 4f - 1f);
            var color = FlagColors[cell];
            p.y += .07f * Mathf.Sin(p.x * 3.2f + cell);
            var flag = Bands(Box(p, new Vector2(.78f, .5f)) * 32f, 2f, 3f, Rim, color, Color.Lerp(color, Color.white, .35f));
            float print = Box(p, new Vector2(.22f, .16f)) * 32f;
            if (print < 0f) flag = Color.Lerp(flag, Color.Lerp(color, Rim, .45f), .55f * Mathf.Clamp01(-print));
            return flag;
        }

        private static Material Mat(string name, int width, int height, Func<float, float, Color> pixel)
            => _kit.ParticleMaterial(name, _kit.BakeTexture(name, width, height, pixel));

        /// <summary>An opaque URP Unlit material: the lantern glows from inside, so scene light does not shade it.</summary>
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

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float ra, float rb)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - Mathf.Lerp(ra, rb, h);
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

        /// <summary>Straight up at a constant speed (all three axes in one curve mode).</summary>
        private static void Rise(ParticleSystem system, float speed)
        {
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(speed);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);
        }

        /// <summary>Paper drag: anything faster than <paramref name="speed"/> slows by <paramref name="dampen"/> per step.</summary>
        private static void Limit(ParticleSystem system, float speed, float dampen)
        {
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = speed;
            limit.dampen = dampen;
        }

        /// <summary>Turbulence so paper drifts and flutters instead of falling straight.</summary>
        private static void Flutter(ParticleSystem system, float strength, float frequency)
        {
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = .4f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        /// <summary>Random spin in both directions, up to <paramref name="radiansPerSecond"/>.</summary>
        private static void Tumble(ParticleSystem system, float radiansPerSecond)
        {
            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-radiansPerSecond, radiansPerSecond);
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
