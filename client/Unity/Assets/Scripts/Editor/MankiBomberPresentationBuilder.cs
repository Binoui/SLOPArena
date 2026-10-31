#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static SlopArena.EditorTools.PresentationFx;
using static SlopArena.EditorTools.ToonShapes;

namespace SlopArena.EditorTools
{
    public static partial class MankiPresentationBuilder
    {
        private static GameObject Save(GameObject root)
        {
            // Unity matches names when preserving file IDs across prefab overwrites.
            var seen = new Dictionary<string, int>();
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name;
                seen.TryGetValue(name, out int count);
                seen[name] = count + 1;
                if (count > 0) child.name = $"{name} {count + 1}";
            }
            return _kit.Save(root);
        }

        private static Color CometPixel(float u, float v)
        {
            var p = new Vector2(u * 2f - 1f, v * 1f - .5f);
            float core = TongueDistance(p, new Vector2(.87f, 0f), new Vector2(-.36f, .02f), .17f, 2.1f, .1f);
            return Over(FlameJetPixel(u, v, hot: false), Flat(core * 128f, Cream));
        }

        private static GameObject LitBomb()
        {
            var root = _kit.NewRoot("MankiLitBomb", attackBound: false);
            var shell = BombShellMesh();
            for (int phase = 0; phase < 3; phase++)
            {
                var state = new GameObject(phase == 0 ? "Unarmed" : phase == 1 ? "Armed" : "Triggered");
                state.transform.SetParent(root.transform, false);
                BombMeshPart(state, "Masked shell", shell, phase == 0 ? _m.BombShell : phase == 1 ? _m.BombArmed : _m.BombTriggered);
                var points = new List<Vector3>();
                for (int i = 0; i <= 12; i++)
                {
                    float t = i / 12f;
                    float x = phase == 1 ? .035f * Mathf.Sin(t * Mathf.PI * 3f) : .08f * Mathf.Sin(t * Mathf.PI * 1.25f);
                    points.Add(new Vector3(x, .34f + (phase == 2 ? .11f : .23f) * t, 0f));
                }
                BombMeshPart(state, phase == 2 ? "Hot fuse" : "Fuse", TubeMesh(points, "MankiBombFuse" + phase, phase == 0 ? .018f : .022f),
                    phase == 0 ? _m.SolidInk : _m.SolidEmber);
                if (phase > 0)
                {
                    var core = new List<Vector3>();
                    foreach (var point in points) core.Add(point + Vector3.back * .018f);
                    BombMeshPart(state, "Pale fuse core", TubeMesh(core, "MankiBombFuseCore" + phase, .009f), _m.SolidCream);
                    if (phase == 2)
                    {
                        var sparks = Particles(state, "Warning sparks", _m.Spark, 1f, scaling: ParticleSystemScalingMode.Hierarchy);
                        sparks.transform.localPosition = points[^1];
                        var main = sparks.main;
                        main.loop = true; main.startLifetime = .09f; main.startSize = .045f; main.startSpeed = .65f; main.maxParticles = 4;
                        var emission = sparks.emission; emission.rateOverTime = 18f;
                        Sphere(sparks, .01f); Fade(sparks, .35f);
                    }
                }
                state.SetActive(phase == 0);
            }
            var zone = new GameObject("Ember stitch");
            zone.transform.SetParent(root.transform, false);
            BombMeshPart(zone, "Scorched seam and raised tabs", EmberStitchMesh(), _m.FloorStitch);
            zone.SetActive(false);
            return Save(root);
        }

        private static Material SolidMaterial(string name, Color color)
        {
            var material = _kit.ParticleMaterial(name, _kit.BakeTexture(name, 8, 8, (u, v) => color), Color.white);
            material.shader = Shader.Find("SLOPArena/Manki Toon Volume");
            material.SetFloat("_VertexPalette", name is "MankiSootVolume" or "MankiEmberStitch" ? 1f : 0f);
            material.SetFloat("_SootBands", name == "MankiSootVolume" ? 1f : 0f);
            material.SetFloat("_ToonBands", name is "MankiSolidEmber" or "MankiSolidTongue" or "MankiTorchVolume" ? 1f : 0f);
            return material;
        }

        private static void BombMeshPart(GameObject root, string name, Mesh mesh, Material material)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(root.transform, false);
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material BombMaterial(string name, int width, int height, System.Func<float, float, Color> pixel)
        {
            var texture = _kit.BakeTexture(name, width, height, pixel);
            string path = $"{_kit.Root}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (created) material = new Material(shader);
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Cull", 0f);
            if (created) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh BombShellMesh()
        {
            const int columns = 24, rows = 12;
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            Vector3 Point(float u, float v)
            {
                float a = (u - .5f) * Mathf.PI * 2f, b = (v - .5f) * Mathf.PI;
                return new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Sin(b), -Mathf.Cos(a) * Mathf.Cos(b)) * .35f;
            }
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    float u = x / (float)columns, v = y / (float)rows;
                    var corners = new[] { new Vector2(u, v), new Vector2(u + 1f / columns, v),
                        new Vector2(u + 1f / columns, v + 1f / rows), new Vector2(u, v + 1f / rows) };
                    int k = vertices.Count;
                    foreach (var p in corners) { vertices.Add(Point(p.x, p.y)); uv.Add(p); }
                    triangles.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
                }
            var mesh = new Mesh { name = "MankiBombShellMesh" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        private static Mesh TubeMesh(List<Vector3> points, string name, float radius)
        {
            const int sides = 8;
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i < points.Count; i++)
            {
                var along = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(0, i - 1)]).normalized;
                var side = Vector3.Cross(along, Mathf.Abs(Vector3.Dot(along, Vector3.forward)) > .9f ? Vector3.up : Vector3.forward).normalized;
                var other = Vector3.Cross(along, side).normalized;
                for (int j = 0; j < sides; j++)
                {
                    float angle = j * Mathf.PI * 2f / sides;
                    vertices.Add(points[i] + (side * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * radius);
                    uv.Add(new Vector2(.5f, .5f));
                    if (i == 0) continue;
                    int k = i * sides + j, n = i * sides + (j + 1) % sides;
                    triangles.AddRange(new[] { k, n, k - sides, n, n - sides, k - sides });
                }
            }
            int start = vertices.Count;
            vertices.Add(points[0]); uv.Add(Vector2.one * .5f);
            vertices.Add(points[^1]); uv.Add(Vector2.one * .5f);
            for (int j = 0; j < sides; j++)
            {
                triangles.AddRange(new[] { start, (j + 1) % sides, j,
                    start + 1, (points.Count - 1) * sides + j, (points.Count - 1) * sides + (j + 1) % sides });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        private static Mesh EmberStitchMesh()
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                int first = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                colors.AddRange(new[] { color, color, color, color });
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
            Vector3 Point(int i, float width, float height)
            {
                float angle = i * Mathf.PI * 2f / 144f;
                float radius = 1f + .006f * Mathf.Sin(i * 2.17f) + width;
                return new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
            }
            for (int band = 0; band < 3; band++)
            {
                float width = band == 0 ? .009f : band == 1 ? .0055f : .0016f;
                Color color = band == 0 ? new Color(.07f, .045f, .035f, .8f)
                    : band == 1 ? new Color(.95f, .32f, .07f, .78f) : new Color(1f, .78f, .28f, .78f);
                for (int i = 0; i < 144; i++)
                {
                    if (band > 0 && i % 13 > 9) continue;
                    Quad(Point(i, -width, band * .0007f), Point(i, width, band * .0007f),
                        Point(i + 1, width, band * .0007f), Point(i + 1, -width, band * .0007f), color);
                }
            }
            for (int i = 0; i < 9; i++)
            {
                float angle = (.06f + i / 9f) * Mathf.PI * 2f;
                var centre = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var side = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * .014f;
                var radial = centre * .012f;
                var tip = centre + Vector3.up * (.03f + i % 3 * .005f);
                foreach (float sign in new[] { -1f, 1f })
                    Quad(centre - side + radial * sign, centre + side + radial * sign, tip + radial * sign, tip + radial * sign, SmokeRim);
                Quad(centre - side + radial, centre - side - radial, tip - radial, tip + radial, SootMid);
                Quad(centre + side - radial, centre + side + radial, tip + radial, tip - radial, Soot);
                Quad(centre - side * .35f - radial * 1.1f, centre + side * .35f - radial * 1.1f,
                    centre + Vector3.up * .024f - radial * 1.1f, centre + Vector3.up * .024f - radial * 1.1f, Ember);
            }
            var mesh = new Mesh { name = "MankiEmberStitchMesh" };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); mesh.RecalculateNormals();
            return _kit.SaveMesh(mesh);
        }

        private static Color BombPixel(float u, float v, int phase)
        {
            float a = (u - .5f) * Mathf.PI * 2f, b = (v - .5f) * Mathf.PI;
            var p = new Vector2(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Sin(b));
            float facing = Mathf.Cos(a) * Mathf.Cos(b);
            float light = Mathf.Round((p.y * .45f - p.x * .3f + facing * .35f) * 3f) / 3f;
            Color color = light < -.1f ? new Color(.055f, .06f, .067f) : light < .5f
                ? new Color(.105f, .115f, .125f) : new Color(.19f, .20f, .21f);
            if (facing < .25f) return color;
            const float s = 220f;
            var q = new Vector2(p.x, -p.y);
            float mask = Mathf.Min(Ellipse(q, new Vector2(-.34f, -.12f), .42f, .29f),
                Ellipse(q, new Vector2(.34f, -.12f), .42f, .29f));
            mask = Mathf.Min(mask, Capsule(q, new Vector2(-.35f, -.12f), new Vector2(.35f, -.12f), .21f, .21f));
            color = Over(color, ToonShapes.Bands(mask * s, 3f, 99f, new Color(.04f, .04f, .045f),
                new Color(229f / 255f, 57f / 255f, 53f / 255f), Cream));
            var dark = new Color(.025f, .025f, .03f);
            for (int eye = -1; eye <= 1; eye += 2)
            {
                bool wink = phase == 1 && eye == 1;
                float lens = wink ? Polyline(q, new[] { new Vector2(.17f, -.06f), new Vector2(.34f, -.2f), new Vector2(.52f, -.08f) }, .045f, .045f)
                    : Ellipse(q, new Vector2(eye * .34f, -.12f), phase == 2 ? .2f : .17f, phase == 2 ? .235f : .205f);
                color = Over(color, ToonShapes.Bands(lens * s, 2f, 99f, dark, phase == 0 || wink ? dark : Cream, Cream));
                if (phase > 0 && !wink)
                    color = Over(color, Flat(Ellipse(q, new Vector2(eye * .34f + .025f, -.1f), phase == 2 ? .085f : .065f, .125f) * s, dark));
            }
            float mouth = Mathf.Max(Ellipse(q, new Vector2(0f, .25f), .65f, .36f), .25f - q.y);
            color = Over(color, ToonShapes.Bands(mouth * s, 3f, 99f, new Color(.025f, .025f, .03f), Cream, Cream));
            float gap = Mathf.Max(Mathf.Abs(q.x - .13f) - .067f, Mathf.Abs(q.y - .31f) - .13f);
            color = Over(color, Flat(Mathf.Max(gap, mouth + .018f) * s, new Color(.025f, .025f, .03f)));
            if (phase == 2)
            {
                float tongue = Ellipse(q, new Vector2(.3f, .51f), .17f, .21f);
                color = Over(color, ToonShapes.Bands(tongue * s, 2f, 99f, Soot, Tongue, Tongue));
                color = Over(color, Flat(Capsule(q, new Vector2(.3f, .39f), new Vector2(.33f, .52f), .012f, .008f) * s, MaskDark));
            }
            return color;
        }

        private static Mesh SootClodMesh()
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(primitive);
            mesh.name = "MankiSootClodMesh";
            var vertices = mesh.vertices;
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var lobes = new (Vector3 centre, float radius)[]
            {
                (new Vector3(.16f, .09f, .02f), .30f),
                (new Vector3(-.15f, .10f, .02f), .29f),
                (new Vector3(.015f, -.13f, .13f), .30f),
                (new Vector3(.02f, .14f, -.14f), .29f),
                (new Vector3(-.06f, .015f, -.19f), .28f),
            };
            for (int i = 0; i < vertices.Length; i++)
            {
                var point = vertices[i];
                var normal = point.normalized;
                float a = Mathf.Atan2(normal.x, -normal.z);
                float radius = .32f;
                var centre = Vector3.zero;
                // Every lobe contains the origin, so its outer ray intersection defines a closed puff.
                foreach (var lobe in lobes)
                {
                    float along = Vector3.Dot(normal, lobe.centre);
                    float surface = along + Mathf.Sqrt(Mathf.Max(0f,
                        lobe.radius * lobe.radius - lobe.centre.sqrMagnitude + along * along));
                    float blend = Mathf.Clamp01(.5f + .5f * (radius - surface) / .055f);
                    radius = Mathf.Lerp(surface, radius, blend) + .055f * blend * (1f - blend);
                    centre = Vector3.Lerp(lobe.centre, centre, blend);
                }
                vertices[i] = normal * radius;
                normals[i] = (vertices[i] - centre).normalized;
                uv[i] = new Vector2(a / (Mathf.PI * 2f) + .5f, Mathf.Asin(normal.y) / Mathf.PI + .5f);
            }
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv;
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        private static GameObject KamikazeBlast()
        {
            var root = _kit.NewRoot("MankiKamikazeExplosion", attackBound: false);
            var clodMesh = SootClodMesh();
            var clods = new List<Transform>();
            _m.SootVolume.SetFloat("_VolumeDepthWrite", 0f);
            _m.SootVolume.SetFloat("_VertexPalette", 0f);
            _m.SootVolume.SetFloat("_VolumeCull", 2f);
            // Self-push events scale this construction by the saved radius divided by three.
            for (int i = 0; i < 6; i++)
            {
                var clod = new GameObject("Burning scatter puff " + i);
                clod.transform.SetParent(root.transform, false);
                float angle = (.06f + i / 6f) * Mathf.PI * 2f;
                clod.transform.localPosition = new Vector3(Mathf.Cos(angle) * .55f,
                    .16f + i % 3 * .19f, Mathf.Sin(angle) * .55f);
                clod.transform.localScale = new Vector3(.44f + i % 3 * .07f, .44f + i % 2 * .11f, .46f + i % 3 * .065f);
                clod.transform.localRotation = Quaternion.Euler(i * 11f, i * 57f, -16f + i * 7f);
                BombMeshPart(clod, "Banded burning soot", clodMesh, _m.SootVolume);
                clods.Add(clod.transform);
            }
            var face = new GameObject("Rising masked grin puff");
            face.transform.SetParent(root.transform, false);
            face.transform.localPosition = new Vector3(.12f, 1.12f, 0f);
            face.transform.localScale = new Vector3(.60f, .64f, .54f);
            var faceTexture = _kit.BakeTexture("MankiScatterFace", 512, 256, (u, v) =>
            {
                float a = (u - .5f) * Mathf.PI * 2f, b = (v - .5f) * Mathf.PI;
                float x = Mathf.Sin(a) * Mathf.Cos(b), y = Mathf.Sin(b), facing = Mathf.Cos(a) * Mathf.Cos(b);
                Color soot = y > .3f ? new Color(.25f, .22f, .21f, .88f) : new Color(.14f, .12f, .12f, .9f);
                if (facing < .22f) return soot;
                return Over(soot, GrinPixel(new Vector2(x * .75f, y * .75f - .21f)));
            });
            var faceMaterial = _kit.ParticleMaterial("MankiScatterFace", faceTexture, Color.white);
            faceMaterial.shader = Shader.Find("SLOPArena/Manki Toon Volume");
            faceMaterial.SetFloat("_VolumeDepthWrite", 0f);
            faceMaterial.SetFloat("_VolumeCull", 2f);
            BombMeshPart(face, "Soft masked wink and tongue", clodMesh, faceMaterial);
            var scatter = root.AddComponent<SlopArena.Client.World.MankiGrinScatter>();
            scatter.Clods = clods.ToArray(); scatter.Face = face.transform;

            var core = Particles(root, "Brief hot contact", _m.Flash, .1f);
            Burst(core, 1);
            var main = core.main;
            main.startLifetime = .0833f; main.startSize = .85f; main.startSpeed = 0f;
            SizeOverLife(core, Curve((0f, .65f), (.3f, 1f), (1f, .1f)));
            Fade(core, .25f);
            for (int i = 0; i < 7; i++)
            {
                float angle = (.02f + i / 7f) * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), .08f + i % 3 * .12f, Mathf.Sin(angle)).normalized;
                Jet(root, "Outward fire tongue " + i, _m.CometFlame, direction * .45f,
                    direction, 1, 13f, 17f, .4f, .65f, .10f);
            }
            Sparks(root, 8, ParticleSystemScalingMode.Local, .1f, 5f, 9f);
            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>())
            {
                main = particles.main;
                main.startLifetime = Mathf.Min(main.startLifetime.constantMax, .4f);
            }
            foreach (string old in new[] { "MankiScatterWink.asset", "MankiScatterGrin.asset" })
                AssetDatabase.DeleteAsset($"{_kit.Root}/{old}");
            _kit.Sound(root, "manki_boom_big", .75f, 1.1f);
            return Save(root);
        }

        private static GameObject KamikazeStartup()
        {
            var root = _kit.NewRoot("MankiKamikazeStartup", attackBound: true);
            var frame = Frame(root, upright: true);
            var cough = new GameObject("Early cough");
            cough.transform.SetParent(frame.transform, false);
            cough.transform.localPosition = new Vector3(0f, .6f, -.1f);
            Puffs(cough, "Cough clods", 2, .08f, .13f, .4f, ParticleSystemScalingMode.Local, life: .14f, material: _m.Soot);
            var marks = new GameObject("Pack announcement");
            marks.transform.SetParent(frame.transform, false);
            marks.transform.localPosition = new Vector3(0f, .85f, 0f);
            for (int i = -1; i <= 1; i++)
            {
                var points = new List<Vector3> { new(i * .1f, 0f, 0f), new(i * .2f, .24f - Mathf.Abs(i) * .07f, 0f) };
                BombMeshPart(marks, "Announcement ember", TubeMesh(points, "MankiStartupEmber" + i, .032f), _m.SolidEmber);
            }
            _kit.Sound(root, "manki_jet_cough", .4f, 1.1f);
            return Save(root);
        }

        private static Mesh TorchMesh()
        {
            const int sides = 12;
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            var radii = new[] { .045f, .105f, .12f, .085f, .05f, 0f };
            for (int ring = 0; ring < radii.Length; ring++)
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                float radius = radii[ring] * (1f + .08f * Mathf.Sin(i * 2.4f + ring));
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, -ring * .12f,
                    Mathf.Sin(angle) * radius + ring * ring * .0015f));
                uv.Add(Vector2.one * .5f);
                if (ring == 0) continue;
                int k = ring * sides + i, next = ring * sides + (i + 1) % sides;
                triangles.AddRange(new[] { k, next, k - sides, next, next - sides, k - sides });
            }
            int cap = vertices.Count; vertices.Add(Vector3.zero); uv.Add(Vector2.one * .5f);
            for (int i = 0; i < sides; i++) triangles.AddRange(new[] { cap, i, (i + 1) % sides });
            var mesh = new Mesh { name = "MankiTorchMesh" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        private static GameObject KamikazeTrail()
        {
            var root = _kit.NewRoot("MankiKamikazeTrail", attackBound: true);
            var frame = Frame(root);
            var torchMesh = TorchMesh();
            foreach (var (side, x) in Nozzles)
            {
                var torch = new GameObject("Twin blowtorch " + side);
                torch.transform.SetParent(frame.transform, false);
                torch.transform.localPosition = new Vector3(x, -.026f, -.008f);
                BombMeshPart(torch, "Closed banded flame", torchMesh, _m.TorchVolume);
                var main = default(ParticleSystem.MainModule);
                var emission = default(ParticleSystem.EmissionModule);
                var ignition = Particles(frame, "Launch ignition " + side, _m.Flash, .3f, ParticleSystemSimulationSpace.Local);
                ignition.transform.localPosition = new Vector3(x, -.026f, -.008f);
                Burst(ignition, 1); main = ignition.main; main.startLifetime = .1f; main.startSize = .4f;
                Fade(ignition, .2f);
                var sparks = Particles(frame, "Hot chips " + side, _m.Spark, 1f);
                sparks.transform.localPosition = new Vector3(x, -.16f, -.008f);
                main = sparks.main;
                main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(.1f, .18f);
                main.startSize = new ParticleSystem.MinMaxCurve(.035f, .065f); main.maxParticles = 4;
                emission = sparks.emission; emission.rateOverTime = 14f;
                Drift(sparks, .18f, -.8f, .1f); Fade(sparks, .35f);
            }
            _kit.Sound(root, "manki_jet_ignite", .45f, 1.1f);
            return Save(root);
        }
    }
}
#endif
