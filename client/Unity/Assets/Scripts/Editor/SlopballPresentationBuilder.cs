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
using Mood = SlopArena.Client.World.SlopballBall.Mood;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Rebuilds Slopball's feel (docs/design/stages/slopball.md, "Feel pass"): the mascot ball with
    /// the mural's face in five moods, its trail, ring and contact shadow, the sender's POP! puff,
    /// hit bursts with BOP!/WHAP!, cage dust, the SLOP! splat and the red SPLAT! on a miss. Writes
    /// Stages/slopball/Presentation/Ball and Resources/Slopball; tools/sfx/slopball_clips.py makes
    /// the sounds and tools/glyph_mask.py the word masks in Ball/Source. Plumbing:
    /// <see cref="PresentationBuilderKit"/>; runtime: <see cref="SlopballPresentation"/>.
    /// </summary>
    public static class SlopballPresentationBuilder
    {
        private const string Root = "Assets/Stages/slopball/Presentation/Ball";
        private const string SourceRoot = Root + "/Source";
        private const string SetPath = "Assets/Resources/" + SlopballPresentationSet.ResourcesPath + ".asset";
        // The court's ball radius; the runtime scales the prefabs to others.
        private const float BallRadius = .65f;
        private const int FaceSize = 512;
        // The painted ball fills this share of its texture; the margin keeps mip levels clean.
        private const float DiscFill = .96f;
        private const float RimTexels = 9f;
        // The ring texture's circle sits at this share of its quad's half-size.
        private const float RingRadius = .8f;
        private const int WordWidth = 512, WordHeight = 208;
        private const float WordU0 = .03f, WordU1 = .97f, WordV0 = .30f, WordV1 = .68f;
        // Comic words draw this far toward the camera (in units of their Z size), in front of the ball.
        private const float WordForward = 1.6f;

        private static readonly Color Ink = Hex(0x1b1a18);
        private static readonly Color Rim = Hex(0x2a1708);
        private static readonly Color Shade = Hex(0xf08a1c);
        private static readonly Color Ball = Hex(0xffd23f);
        private static readonly Color BallLight = Hex(0xfff3b0);
        private static readonly Color Cream = Hex(0xfff6de);
        private static readonly Color Mouth = Hex(0x3a1210);
        private static readonly Color Tongue = Hex(0xff5a5f);
        private static readonly Color Blush = Hex(0xff8f8f);
        private static readonly Color Coral = Hex(0xff6b4a);
        private static readonly Color CoralDark = Hex(0x9e2a22);
        private static readonly Color CoralLight = Hex(0xffe9a8);
        private static readonly Color Red = Hex(0xe5332a);
        private static readonly Color RedDark = Hex(0x5e0d0b);
        private static readonly Color RedLight = Hex(0xff9a7a);
        private static readonly Color Mustard = Hex(0x7a5a0a);
        private static readonly Color RingYellow = Hex(0xffd84a);
        private static readonly Color SmokeRim = Hex(0x3b3a3c);
        private static readonly Color Smoke = Hex(0xbdb8ae);
        private static readonly Color SmokeLight = Hex(0xf4f1ea);
        private static readonly Color SweatDark = Hex(0x1e3d6b);
        private static readonly Color Sweat = Hex(0x7fd3ff);
        private static readonly Color SweatLight = Hex(0xe6f8ff);

        private static PresentationBuilderKit _kit;
        private static Materials _m;
        private static Mesh _sphere, _quad;

        private sealed class Materials
        {
            public Material[] Faces;
            public Material Flash, Trail, Ring, Shadow, Glow, Star, Impact, Puff, Mote, SplatYellow, SplatRed, DropYellow, DropRed,
                WordSlop, WordWhap, WordBop, WordSplat, WordPop;
        }

        [CliCommand("sloparena.presentation.slopball.build", "Rebuild Slopball's mascot ball, ground cue and serve, hit, bounce, score and miss effects plus its Resources/Slopball set in Edit Mode.", MainThreadRequired = true, Tags = new[] { "presentation", "arcade" })]
        public static object Build()
        {
            using var kit = PresentationBuilderKit.ForFolder(Root, audioFolder: "Slopball");
            _kit = kit;
            _sphere = BallMesh();
            _quad = Quad();
            _m = BuildMaterials();
            var set = AssetDatabase.LoadAssetAtPath<SlopballPresentationSet>(SetPath);
            bool created = set == null;
            if (created) set = ScriptableObject.CreateInstance<SlopballPresentationSet>();
            set.Ball = BallPrefab();
            set.BallRadius = BallRadius;
            set.GroundMarker = GroundMarker();
            set.ServePuff = ServePuff();
            set.HitLight = Hit("SlopballHitLight", heavy: false);
            set.HitHeavy = Hit("SlopballHitHeavy", heavy: true);
            set.Bounce = BounceDust();
            set.Score = Splat("SlopballScore", _m.SplatYellow, _m.DropYellow, _m.WordSlop, 2.3f, ring: true);
            set.Miss = Splat("SlopballMiss", _m.SplatRed, _m.DropRed, _m.WordSplat, 2.1f, ring: false);
            set.SoundFolder = "Audio/SFX/Slopball";
            if (created)
            {
                PresentationBuilderKit.EnsureFolder("Assets/Resources/Slopball");
                AssetDatabase.CreateAsset(set, SetPath);
            }
            else EditorUtility.SetDirty(set);
            return kit.Close(new { set = SetPath });
        }

        [MenuItem("Tools/SLOPArena/Presentation/Rebuild Slopball Presentation")]
        private static void Menu() => Debug.Log("[SlopballPresentationBuilder] " + Newtonsoft.Json.JsonConvert.SerializeObject(Build()));

        // ── The ball ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The mascot ball: a sphere whose front carries the painted face (SlopballBall turns it to the
        /// camera), a slightly larger white shell for the hit flash and a streak trail.
        /// </summary>
        private static GameObject BallPrefab()
        {
            var root = _kit.NewRoot("SlopballBall", attackBound: false);
            var ball = root.AddComponent<SlopballBall>();
            var facing = Child(root, "Facing");
            var squash = Child(facing, "Squash");
            var unsquash = Child(squash, "Unsquash");
            var model = Child(unsquash, "Model");
            AddMesh(model, _sphere, _m.Faces[0]);
            var flash = Child(unsquash, "Flash");
            flash.transform.localScale = Vector3.one * 1.04f;
            AddMesh(flash, _sphere, _m.Flash);
            flash.GetComponent<MeshRenderer>().enabled = false;
            ball.Facing = facing.transform;
            ball.Squash = squash.transform;
            ball.Unsquash = unsquash.transform;
            ball.Model = model.transform;
            ball.ModelRenderer = model.GetComponent<MeshRenderer>();
            ball.FlashRenderer = flash.GetComponent<MeshRenderer>();
            ball.Moods = _m.Faces;
            ball.BuiltRadius = BallRadius;

            var trail = root.AddComponent<TrailRenderer>();
            trail.time = .2f;
            trail.minVertexDistance = .08f;
            trail.widthMultiplier = BallRadius * 1.7f;
            trail.widthCurve = Curve((0f, 1f), (1f, 0f));
            trail.textureMode = LineTextureMode.Stretch;
            trail.alignment = LineAlignment.View;
            trail.numCapVertices = 4;
            trail.sharedMaterial = _m.Trail;
            trail.emitting = false;
            trail.autodestruct = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            ball.Trail = trail;
            return Save(root);
        }

        /// <summary>The ring and soft contact shadow under the ball, lying on the floor.</summary>
        private static GameObject GroundMarker()
        {
            var root = _kit.NewRoot("SlopballGroundMarker", attackBound: false);
            var shadow = Child(root, "Shadow");
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadow.transform.localScale = Vector3.one * 1.4f;
            AddMesh(shadow, _quad, _m.Shadow);
            var ring = Child(root, "Ring");
            ring.transform.localPosition = new Vector3(0f, .006f, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = Vector3.one * (2f * BallRadius / RingRadius);
            AddMesh(ring, _quad, _m.Ring);
            return Save(root);
        }

        // ── Effects ──────────────────────────────────────────────────────────────

        /// <summary>The sender fires: a smoke puff at its exit and a small POP!.</summary>
        private static GameObject ServePuff()
        {
            var root = _kit.NewRoot("SlopballServePuff", attackBound: false);
            var puffs = Particles(root, "Puffs", _m.Puff, .9f);
            Burst(puffs, 7);
            var main = puffs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .6f);
            main.startSize = new ParticleSystem.MinMaxCurve(.45f, .85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Sphere(puffs, .3f);
            Drag(puffs, .12f);
            SizeOverLife(puffs, Curve((0f, .45f), (.2f, 1f), (1f, 1.2f)));
            Fade(puffs, .5f);
            Word(root, "POP!", _m.WordPop, 1.3f, 0f, .55f, .9f, new Vector3(.55f, .5f, 0f));
            return Save(root);
        }

        /// <summary>A hit: a white flash, a coral comic star, an impact ring and BOP! or WHAP!; heavy ones throw sparks.</summary>
        private static GameObject Hit(string name, bool heavy)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            float size = heavy ? 1f : .68f;
            var flash = Particles(root, "Flash", _m.Glow, .3f);
            Burst(flash, 1);
            var main = flash.main;
            main.startLifetime = .12f;
            main.startSize = 2.4f * size;
            main.startColor = new Color(1f, 1f, 1f, .75f);
            SizeOverLife(flash, Curve((0f, .6f), (.3f, 1f), (1f, 1.1f)));
            Fade(flash, .2f);
            Behind(flash);

            var star = Particles(root, "Star", _m.Star, .4f);
            Burst(star, 1);
            main = star.main;
            main.startLifetime = heavy ? .22f : .17f;
            main.startSize = 2f * size;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(star, Curve((0f, .4f), (.18f, 1.15f), (.5f, 1f), (1f, .75f)));
            Fade(star, .5f);
            InFront(star);

            var ring = Particles(root, "Impact ring", _m.Impact, .5f);
            Burst(ring, 1);
            main = ring.main;
            main.startLifetime = .3f;
            main.startSize = 1.7f * size;
            SizeOverLife(ring, Curve((0f, .6f), (1f, 1.8f)));
            Fade(ring, .3f);
            ring.GetComponent<ParticleSystemRenderer>().sortingFudge = -8f;

            if (heavy)
            {
                var sparks = Particles(root, "Sparks", _m.Mote, .5f);
                Burst(sparks, 9);
                main = sparks.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.18f, .3f);
                main.startSize = new ParticleSystem.MinMaxCurve(.12f, .2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 8.5f);
                Sphere(sparks, .2f);
                Stretch(sparks, .05f, 1.6f);
                Fade(sparks, .5f);
            }
            // Beside the ball rather than above it: hits often happen near the top of the screen.
            if (heavy) Word(root, "WHAP!", _m.WordWhap, 1.9f, .02f, .5f, .5f, new Vector3(1f, .15f, 0f));
            else Word(root, "BOP!", _m.WordBop, 1.3f, .02f, .45f, .45f, new Vector3(.8f, .1f, 0f));
            return Save(root);
        }

        /// <summary>A bounce off the chain-link: a small dust ring and a few motes.</summary>
        private static GameObject BounceDust()
        {
            var root = _kit.NewRoot("SlopballBounce", attackBound: false);
            var ring = Particles(root, "Dust ring", _m.Impact, .4f);
            Burst(ring, 1);
            var main = ring.main;
            main.startLifetime = .26f;
            main.startSize = 1.1f;
            main.startColor = new Color(1f, 1f, 1f, .8f);
            SizeOverLife(ring, Curve((0f, .5f), (1f, 1.5f)));
            Fade(ring, .25f);
            var motes = Particles(root, "Motes", _m.Mote, .5f);
            Burst(motes, 6);
            main = motes.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.2f, .35f);
            main.startSize = new ParticleSystem.MinMaxCurve(.1f, .16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
            Sphere(motes, .25f);
            Fade(motes, .5f);
            return Save(root);
        }

        /// <summary>
        /// A ball lands for good: a toon splat on the floor that pops open and fades, splashing
        /// droplets, the word rising above it and, for a score, a ring racing out across the floor.
        /// </summary>
        private static GameObject Splat(string name, Material splat, Material drop, Material word, float wordWidth, bool ring)
        {
            var root = _kit.NewRoot(name, attackBound: false);
            var decal = Particles(root, "Splat", splat, 2.2f);
            decal.transform.localPosition = new Vector3(0f, .03f, 0f);
            Burst(decal, 1);
            var main = decal.main;
            main.startLifetime = 2.1f;
            main.startSize = 3.1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(decal, Curve((0f, .3f), (.04f, 1.1f), (.08f, 1f), (1f, 1f)));
            Fade(decal, .62f);
            decal.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            decal.GetComponent<ParticleSystemRenderer>().sortingFudge = 12f;

            var drops = Particles(root, "Droplets", drop, 1.2f);
            drops.transform.localPosition = new Vector3(0f, .2f, 0f);
            Burst(drops, 12);
            main = drops.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.14f, .26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 6f);
            main.gravityModifier = 1.4f;
            var shape = drops.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = .35f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Fade(drops, .7f);

            if (ring)
            {
                var wave = Particles(root, "Ring", _m.Impact, .7f);
                wave.transform.localPosition = new Vector3(0f, .05f, 0f);
                Burst(wave, 1);
                main = wave.main;
                main.startLifetime = .55f;
                main.startSize = 2.4f;
                SizeOverLife(wave, Curve((0f, .5f), (1f, 2.3f)));
                Fade(wave, .3f);
                wave.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            }
            Word(root, name.EndsWith("Score", StringComparison.Ordinal) ? "SLOP!" : "SPLAT!", word, wordWidth, .03f, .7f, .55f,
                new Vector3(0f, 1.05f, 0f));
            return Save(root);
        }

        /// <summary>
        /// Comic lettering that pops in, rises and fades. Bonk's lettering pattern: a 3D-sized
        /// billboard whose pivot moves toward the camera, so it draws in front of the ball.
        /// </summary>
        private static void Word(GameObject root, string name, Material material, float width, float delay, float life, float rise, Vector3 offset)
        {
            var word = Particles(root, name, material, delay + life + .1f, ParticleSystemSimulationSpace.Local);
            word.transform.localPosition = offset;
            Burst(word, 1);
            var main = word.main;
            main.startDelay = delay;
            main.startLifetime = life;
            main.startSize3D = true;
            main.startSizeX = width;
            main.startSizeY = width * WordHeight / WordWidth;
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(.1f);
            var velocity = word.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(rise, rise);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            SizeOverLife(word, Curve((0f, .2f), (.12f, 1.18f), (.24f, .96f), (.34f, 1f), (1f, 1.03f)));
            Fade(word, .72f);
            var renderer = word.GetComponent<ParticleSystemRenderer>();
            renderer.sortingFudge = -18f;
            renderer.maxParticleSize = 3f;
            renderer.pivot = new Vector3(0f, 0f, WordForward);
        }

        // ── Meshes ───────────────────────────────────────────────────────────────

        /// <summary>
        /// A sphere with its poles on ±Z, so the silhouette from the front is one exact ring. UVs project
        /// the front straight on: the painted disc fills <see cref="DiscFill"/> of the texture, screen
        /// right (the ball's -X when +Z faces the camera) on the right of the image.
        /// </summary>
        private static Mesh BallMesh()
        {
            const int rings = 28, segments = 64;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float theta = r / (float)rings * Mathf.PI;
                for (int s = 0; s <= segments; s++)
                {
                    float phi = s / (float)segments * Mathf.PI * 2f;
                    var p = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta));
                    vertices.Add(p * BallRadius);
                    uvs.Add(new Vector2(.5f - p.x * .5f * DiscFill, .5f + p.y * .5f * DiscFill));
                    colors.Add(Color.white);
                }
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + 1, c = a + segments + 1, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            var mesh = new Mesh { name = "SlopballBallMesh" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        private static Mesh Quad()
        {
            var mesh = new Mesh { name = "SlopballQuad" };
            mesh.SetVertices(new[] { new Vector3(-.5f, -.5f, 0f), new Vector3(.5f, -.5f, 0f), new Vector3(.5f, .5f, 0f), new Vector3(-.5f, .5f, 0f) });
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
            var faces = new Material[5];
            foreach (Mood mood in Enum.GetValues(typeof(Mood)))
                faces[(int)mood] = Unlit($"SlopballFace{mood}", _kit.BakeTexture($"SlopballFace{mood}", FaceSize, FaceSize, (u, v) => FacePixel(u, v, mood)));
            var glow = _kit.BakeTexture("SlopballGlow", 128, 128, (u, v) => Alpha(Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f)));
            var ring = Mat("SlopballRing", 256, 256, (u, v) => Bands((Mathf.Abs(P(u, v).magnitude - RingRadius) - .075f) * 128f, 3f, 99f, Ink, RingYellow, RingYellow));
            // Over the contact shadow, which shares its spot on the floor.
            ring.SetFloat("_QueueOffset", 11f);
            ring.renderQueue = (int)RenderQueue.Transparent + 11;
            EditorUtility.SetDirty(ring);
            var words = new Dictionary<string, Material>();
            foreach (var (file, extrude, dark) in new[]
                     {
                         ("Slop", Coral, CoralDark), ("Whap", Coral, CoralDark), ("Bop", Coral, CoralDark),
                         ("Pop", Hex(0xf6b52a), Hex(0x8a5a10)), ("Splat", Red, RedDark),
                     })
            {
                var mask = new MaskField($"{SourceRoot}/{file}.png");
                words[file] = Mat($"SlopballWord{file}", WordWidth, WordHeight, (u, v) => WordPixel(u, v, mask, extrude, dark));
            }
            return new Materials
            {
                Faces = faces,
                Flash = _kit.ParticleMaterial("SlopballFlash", _kit.BakeTexture("SlopballWhite", 8, 8, (_, _) => Color.white), Color.white),
                Trail = Mat("SlopballTrail", 256, 64, TrailPixel),
                Ring = ring,
                Shadow = _kit.ParticleMaterial("SlopballShadow", _kit.BakeTexture("SlopballShadow", 128, 128,
                    (u, v) => new Color(.03f, .07f, .09f, .9f * Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 1.6f))), Color.white),
                Glow = _kit.ParticleMaterial("SlopballGlowAlpha", glow),
                Star = Mat("SlopballStar", 256, 256, (u, v) => Bands(Polygon(P(u, v), StarPoints) * 128f, 4f, 14f, Ink, Coral, CoralLight)),
                Impact = Mat("SlopballImpact", 256, 256, (u, v) => Bands((Mathf.Abs(P(u, v).magnitude - .8f) - .05f) * 128f, 2.5f, 4.5f, Ink, Cream, Color.white)),
                Puff = Mat("SlopballPuff", 256, 256, (u, v) => PuffPixel(P(u, v))),
                Mote = Mat("SlopballMote", 64, 64, (u, v) => Bands(Rhombus(P(u, v), new Vector2(.55f, .92f)) * 32f, 2f, 4f, Ink, Ball, Cream)),
                SplatYellow = Mat("SlopballSplatYellow", 512, 512, (u, v) => SplatPixel(P(u, v), 3, Mustard, Shade, Ball)),
                SplatRed = Mat("SlopballSplatRed", 512, 512, (u, v) => SplatPixel(P(u, v), 6, RedDark, Hex(0xa81d17), Red)),
                DropYellow = Mat("SlopballDropYellow", 64, 64, (u, v) => Bands(P(u, v).magnitude * 32f - 26f, 3f, 99f, Mustard, Ball, Ball)),
                DropRed = Mat("SlopballDropRed", 64, 64, (u, v) => Bands(P(u, v).magnitude * 32f - 26f, 3f, 99f, RedDark, Red, Red)),
                WordSlop = words["Slop"], WordWhap = words["Whap"], WordBop = words["Bop"], WordSplat = words["Splat"], WordPop = words["Pop"],
            };
        }

        private static readonly Vector2[] StarPoints = StarOutline(12, .5f, 17);

        /// <summary>
        /// One mood of the mascot ball, painted flat as seen from the front: a dark rim, an orange
        /// shaded crescent, the yellow lit body and a highlight, then the mural's face. Units are
        /// the ball's radius (+Y up); widths become texels through <c>px</c>.
        /// </summary>
        private static Color FacePixel(float u, float v, Mood mood)
        {
            var b = new Vector2((u * 2f - 1f) / DiscFill, (v * 2f - 1f) / DiscFill);
            float px = FaceSize * .5f * DiscFill;
            float edge = (b.magnitude - 1f) * px;
            Color c = Over(Rim, Bands(edge, RimTexels, 0f, Rim, Shade, Shade));
            float lit = Mathf.Max(Circle(b, Vector2.zero, .93f), Circle(b, new Vector2(-.176f, .198f), .86f));
            c = Fill(c, lit * px, Ball);
            c = Fill(c, Ellipse(b, new Vector2(-.32f, .36f), new Vector2(.24f, .15f)) * px, BallLight, .85f);
            c = Face(c, b, px, mood);
            c.a = 1f;
            return c;
        }

        /// <summary>The mural mascot's face (tools: the 2026-10-18 concept board), 22% larger than the ball's units.</summary>
        private static Color Face(Color c, Vector2 b, float px, Mood mood)
        {
            const float F = 1.22f, ex = .27f, ey = .14f;
            float lw = .045f * F;
            Vector2 At(float x, float y) => new(x * F, y * F);
            if (mood is Mood.Grin or Mood.Laugh)
                for (int side = -1; side <= 1; side += 2)
                    c = Fill(c, Ellipse(b, At(side * .46f, -.10f), At(.12f, .07f)) * px, Blush, .75f);
            switch (mood)
            {
                case Mood.Grin:
                {
                    c = Fill(c, Polyline(b, Arc(At(-ex, .11f), At(.10f, .08f), 20f, 160f, 10), lw) * px, Ink);
                    c = Fill(c, Ellipse(b, At(ex, ey), At(.075f, .11f)) * px, Ink);
                    c = Fill(c, Circle(b, At(ex - .025f, ey + .04f), .025f * F) * px, Color.white);
                    float mouth = Mathf.Max(Ellipse(b, At(0f, -.16f), At(.33f, .25f)), b.y + .16f * F);
                    c = Banded(c, mouth * px, lw * .6f * px, 0f, Ink, Mouth, Mouth);
                    c = Fill(c, Mathf.Max(Ellipse(b, At(.04f, -.37f), At(.17f, .10f)), mouth + lw * .5f) * px, Tongue);
                    break;
                }
                case Mood.Laugh:
                {
                    for (int side = -1; side <= 1; side += 2)
                        c = Fill(c, Polyline(b, Arc(At(side * ex, .10f), At(.10f, .09f), 20f, 160f, 10), lw) * px, Ink);
                    float mouth = Mathf.Max(Ellipse(b, At(0f, -.13f), At(.40f, .36f)), b.y + .11f * F);
                    c = Banded(c, mouth * px, lw * .6f * px, 0f, Ink, Mouth, Mouth);
                    c = Fill(c, Mathf.Max(Ellipse(b, At(0f, -.42f), At(.22f, .13f)), mouth + lw * .5f) * px, Tongue);
                    break;
                }
                case Mood.Wince:
                {
                    for (int side = -1; side <= 1; side += 2)
                        c = Fill(c, Polyline(b, new[] { At(side * .36f, .21f), At(side * .21f, ey), At(side * .36f, .07f) }, lw) * px, Ink);
                    var zig = new Vector2[7];
                    for (int i = 0; i < zig.Length; i++) zig[i] = At(-.24f + i * .08f, -(.2f + (i % 2 == 1 ? .05f : -.02f)));
                    c = Fill(c, Polyline(b, zig, lw * .5f) * px, Ink);
                    break;
                }
                case Mood.Yikes:
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        c = Banded(c, Circle(b, At(side * ex, ey), .12f * F) * px, lw * .6f * px, 0f, Ink, Color.white, Color.white);
                        c = Fill(c, Circle(b, At(side * ex + .01f, ey - .03f), .045f * F) * px, Ink);
                    }
                    c = Banded(c, Ellipse(b, At(0f, -.26f), At(.09f, .12f)) * px, lw * .6f * px, 0f, Ink, Mouth, Mouth);
                    var drop = At(.62f, .38f);
                    float sweat = Mathf.Min(Circle(b, drop + At(0f, -.06f), .07f * F),
                        Polygon(b, new[] { drop + At(-.06f, -.04f), drop + At(0f, .12f), drop + At(.06f, -.04f) }));
                    c = Banded(c, sweat * px, lw * .4f * px, .02f * F * px, SweatDark, Sweat, SweatLight);
                    break;
                }
                case Mood.Ko:
                {
                    const float d = .08f;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var eye = At(side * ex, ey);
                        float cross = Mathf.Min(Segment(b, eye + At(-d, -d), eye + At(d, d), lw * .5f),
                            Segment(b, eye + At(-d, d), eye + At(d, -d), lw * .5f));
                        c = Fill(c, cross * px, Ink);
                    }
                    var wave = new Vector2[9];
                    for (int i = 0; i < wave.Length; i++) wave[i] = At(-.22f + i * .055f, -(.24f + Mathf.Sin(i * 1.4f) * .035f));
                    c = Fill(c, Polyline(b, wave, lw * .55f) * px, Ink);
                    c = Banded(c, Ellipse(b, At(.1f, -.34f), At(.08f, .1f)) * px, lw * .4f * px, 0f, Ink, Tongue, Tongue);
                    break;
                }
            }
            return c;
        }

        /// <summary>The streak: a light core and a dark rim across it, fading out toward the tail (u = 1).</summary>
        private static Color TrailPixel(float u, float v)
        {
            float across = Mathf.Abs(v - .5f) * 2f;
            var color = Bands((across - .82f) * 32f, 3f, 8f, new Color(.16f, .07f, .04f), new Color(.82f, .82f, .82f), Color.white);
            color.a *= Mathf.Pow(1f - u, 1.3f);
            return color;
        }

        /// <summary>The serve's smoke: a cauliflower puff with a lighter top-left.</summary>
        private static Color PuffPixel(Vector2 p)
        {
            float puff = Cloud(p, Vector2.zero, .84f, 7, 51);
            Color color = Bands(puff * 128f, 3.5f, 99f, SmokeRim, Smoke, Smoke);
            float lit = Mathf.Max(Cloud(p, new Vector2(-.14f, .17f), .6f, 6, 54), puff + .07f);
            return Over(color, Flat(lit * 128f, SmokeLight));
        }

        /// <summary>A paint splat seen from above: a blobby body, tapered streaks and loose droplets.</summary>
        private static Color SplatPixel(Vector2 p, int seed, Color dark, Color mid, Color light)
        {
            var random = new System.Random(seed);
            float d = Circle(p, Vector2.zero, .5f);
            for (int i = 0; i < 11; i++)
            {
                float angle = (i + (float)random.NextDouble() * .6f - .3f) / 11f * Mathf.PI * 2f;
                float distance = Mathf.Lerp(.32f, .52f, (float)random.NextDouble());
                float radius = Mathf.Lerp(.13f, .23f, (float)random.NextDouble());
                d = Mathf.Min(d, Circle(p, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance, radius));
            }
            for (int i = 0; i < 6; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var tip = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Lerp(.78f, .9f, (float)random.NextDouble());
                d = Mathf.Min(d, Tapered(p, Vector2.zero, tip, .1f, .035f));
            }
            for (int i = 0; i < 9; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var centre = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Lerp(.7f, .92f, (float)random.NextDouble());
                d = Mathf.Min(d, Circle(p, centre, Mathf.Lerp(.035f, .06f, (float)random.NextDouble())));
            }
            Color color = Bands(d * 256f, 5f, 14f, dark, mid, light);
            // A wet highlight on the body.
            float shine = Mathf.Max(Ellipse(p, new Vector2(-.16f, .18f), new Vector2(.16f, .07f)), d + .06f);
            return Over(color, Flat(shine * 256f, new Color(1f, 1f, 1f, .8f)));
        }

        /// <summary>Comic lettering from a glyph mask: cream faces, an extrusion that darkens with depth and a dark outline.</summary>
        private static Color WordPixel(float u, float v, MaskField mask, Color extrude, Color extrudeDark)
        {
            float mu = Mathf.Lerp(WordU0, WordU1, u), mv = Mathf.Lerp(WordV0, WordV1, v);
            float texels = WordWidth / ((WordU1 - WordU0) * mask.Width);
            const int steps = 12;
            var shift = new Vector2(.012f, -.02f);
            float face = mask.Sample(mu, mv) * texels;
            float extrusion = float.MaxValue, depth = 1f;
            for (int k = 1; k <= steps; k++)
            {
                float t = k / (float)steps;
                float d = mask.Sample(mu - shift.x * t, mv - shift.y * t) * texels;
                if (d < 0f && depth >= 1f) depth = t;
                extrusion = Mathf.Min(extrusion, d);
            }
            Color color = Flat(Mathf.Min(face, extrusion) - 5f, Ink);
            color = Over(color, Flat(extrusion, Color.Lerp(extrude, extrudeDark, depth)));
            color = Over(color, Flat(face - 2.2f, Ink));
            return Over(color, Flat(face, Cream));
        }

        // ── Shapes ───────────────────────────────────────────────────────────────

        private static float Circle(Vector2 p, Vector2 centre, float radius) => (p - centre).magnitude - radius;

        private static float Ellipse(Vector2 p, Vector2 centre, Vector2 radii)
        {
            var q = p - centre;
            return (new Vector2(q.x / radii.x, q.y / radii.y).magnitude - 1f) * Mathf.Min(radii.x, radii.y);
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float radius) => Tapered(p, a, b, radius, radius);

        private static float Tapered(Vector2 p, Vector2 a, Vector2 b, float ra, float rb)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - Mathf.Lerp(ra, rb, h);
        }

        private static float Polyline(Vector2 p, Vector2[] points, float radius)
        {
            float d = float.MaxValue;
            for (int i = 0; i + 1 < points.Length; i++) d = Mathf.Min(d, Segment(p, points[i], points[i + 1], radius));
            return d;
        }

        /// <summary>Points on an elliptical arc from <paramref name="from"/> to <paramref name="to"/> degrees (counter-clockwise, +Y up).</summary>
        private static Vector2[] Arc(Vector2 centre, Vector2 radii, float from, float to, int count)
        {
            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.Lerp(from, to, i / (float)(count - 1)) * Mathf.Deg2Rad;
                points[i] = centre + new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y);
            }
            return points;
        }

        /// <summary>A cauliflower puff: a body plus bumps around it.</summary>
        private static float Cloud(Vector2 p, Vector2 centre, float radius, int bumps, int seed)
        {
            var random = new System.Random(seed);
            float d = Circle(p, centre, radius * .62f);
            for (int i = 0; i < bumps; i++)
            {
                float angle = (i + (float)random.NextDouble() * .5f - .25f) / bumps * Mathf.PI * 2f;
                float distance = radius * Mathf.Lerp(.5f, .62f, (float)random.NextDouble());
                d = Mathf.Min(d, Circle(p, centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance,
                    radius * Mathf.Lerp(.3f, .44f, (float)random.NextDouble())));
            }
            return d;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static Material Mat(string name, int width, int height, Func<float, float, Color> pixel)
            => _kit.ParticleMaterial(name, _kit.BakeTexture(name, width, height, pixel));

        /// <summary>An opaque URP Unlit material: the ball's toon shading is painted into its faces.</summary>
        private static Material Unlit(string name, Texture texture)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? throw new InvalidOperationException("URP Unlit is unavailable.");
            string path = $"{_kit.Root}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(shader);
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        private static Color Fill(Color under, float distance, Color color, float alpha = 1f)
        {
            color.a = Mathf.Clamp01(.5f - distance) * alpha;
            return Over(under, color);
        }

        private static Color Banded(Color under, float distance, float rim, float accent, Color rimColor, Color accentColor, Color coreColor)
            => Over(under, Bands(distance, rim, accent, rimColor, accentColor, coreColor));

        private static Color Flat(float distance, Color color)
        {
            color.a *= Mathf.Clamp01(.5f - distance);
            return color;
        }

        private static Color Over(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0f) return new Color(over.r, over.g, over.b, 0f);
            return new Color((over.r * over.a + under.r * under.a * (1f - over.a)) / a,
                (over.g * over.a + under.g * under.a * (1f - over.a)) / a,
                (over.b * over.a + under.b * under.a * (1f - over.a)) / a, a);
        }

        private static Color Hex(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        private static Vector2 P(float u, float v) => new(u * 2f - 1f, v * 2f - 1f);

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
