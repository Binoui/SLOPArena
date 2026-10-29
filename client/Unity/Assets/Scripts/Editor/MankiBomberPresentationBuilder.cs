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
            BombMeshPart(root, "Masked shell", BombShellMesh(), _m.BombShell);
            var points = new List<Vector3>();
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f;
                points.Add(new Vector3(.12f * Mathf.Sin(t * Mathf.PI * 1.25f), .33f + .23f * t, 0f));
            }
            BombMeshPart(root, "Curled fuse", BombFuseMesh(points), _m.BombTrim);
            var collar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            collar.name = "Fuse collar";
            collar.transform.SetParent(root.transform, false);
            collar.transform.localPosition = new Vector3(0f, .335f, 0f);
            collar.transform.localScale = new Vector3(.12f, .025f, .12f);
            Object.DestroyImmediate(collar.GetComponent<Collider>());
            collar.GetComponent<MeshRenderer>().sharedMaterial = _m.BombTrim;

            var tip = new GameObject("Fuse tip");
            tip.transform.SetParent(root.transform, false);
            tip.transform.localPosition = points[^1];
            var star = GameObject.CreatePrimitive(PrimitiveType.Quad);
            star.name = "Crisp star";
            star.transform.SetParent(tip.transform, false);
            star.transform.localScale = Vector3.one * .19f;
            Object.DestroyImmediate(star.GetComponent<Collider>());
            star.GetComponent<MeshRenderer>().sharedMaterial = _m.Spark;
            var sparks = Particles(tip, "Crackle", _m.Spark, 1f, scaling: ParticleSystemScalingMode.Hierarchy);
            var main = sparks.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.07f, .14f);
            main.startSize = new ParticleSystem.MinMaxCurve(.025f, .06f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.3f, .8f);
            main.gravityModifier = .25f;
            var emission = sparks.emission;
            emission.rateOverTime = 18f;
            Sphere(sparks, .012f);
            Spin(sparks, 280f);
            Fade(sparks, .45f);
            _kit.Sound(root, "manki_fuse", .3f, 1f);
            return Save(root);
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

        private static Mesh BombFuseMesh(List<Vector3> points)
        {
            const int sides = 6;
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i < points.Count; i++)
            {
                var along = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(0, i - 1)]).normalized;
                var side = Vector3.Cross(along, Vector3.forward);
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    vertices.Add(points[i] + (side * Mathf.Cos(a) + Vector3.forward * Mathf.Sin(a)) * .018f);
                    uv.Add(new Vector2(.75f, .5f));
                    if (i == 0) continue;
                    int k = i * sides + j, n = i * sides + (j + 1) % sides;
                    triangles.AddRange(new[] { k, n, k - sides, n, n - sides, k - sides });
                }
            }
            var mesh = new Mesh { name = "MankiBombFuseMesh" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        private static Color BombPixel(float u, float v)
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
            color = Over(color, ToonShapes.Bands(Ellipse(q, new Vector2(-.34f, -.12f), .16f, .205f) * s,
                2f, 99f, Cream, new Color(.025f, .025f, .03f), Cream));
            float wink = Polyline(q, new[] { new Vector2(.16f, -.04f), new Vector2(.34f, -.2f), new Vector2(.53f, -.08f) }, .045f, .045f);
            color = Over(color, Flat(wink * s, new Color(.025f, .025f, .03f)));
            float mouth = Mathf.Max(Ellipse(q, new Vector2(0f, .25f), .65f, .36f), .25f - q.y);
            color = Over(color, ToonShapes.Bands(mouth * s, 3f, 99f, new Color(.025f, .025f, .03f), Cream, Cream));
            float gap = Mathf.Max(Mathf.Abs(q.x - .13f) - .067f, Mathf.Abs(q.y - .31f) - .13f);
            return Over(color, Flat(Mathf.Max(gap, mouth + .018f) * s, new Color(.025f, .025f, .03f)));
        }

        private static GameObject KamikazeBlast()
        {
            var root = _kit.NewRoot("MankiKamikazeExplosion", attackBound: false);
            const ParticleSystemScalingMode local = ParticleSystemScalingMode.Local;
            var floor = Floor(root, 6f, -.6f);
            // Shared scales self-push explosion presentation by radius / 3.
            FloorRing(floor, 6f, .34f);
            Pop(root, "Contact star", _m.Flash, 3.2f, .1f, local, spin: true);
            Fireballs(root, 4, 1.5f, 2.3f, .27f, local);
            for (int i = 0; i < 3; i++)
            {
                var stalk = new GameObject("Stalk tier " + i);
                stalk.transform.SetParent(root.transform, false);
                stalk.transform.localPosition = Vector3.up * (.5f + i * .85f);
                var fire = Fireballs(stalk, 1, 1.15f + i * .18f, 1.35f + i * .18f, .38f, local);
                var main = fire.main;
                main.startDelay = i * .025f;
                main.startSize3D = true;
                main.startSizeX = 1.3f + i * .35f;
                main.startSizeY = 2f;
                main.startSizeZ = 1f;
                main.startSpeed = 0f;
                main.startRotation = 0f;
                var shape = fire.shape;
                shape.enabled = false;
            }
            var cap = Particles(root, "Grin mushroom cap", _m.Mushroom, .85f);
            cap.transform.localPosition = Vector3.up * 2.9f;
            Burst(cap, 1);
            var capMain = cap.main;
            capMain.startDelay = .055f;
            capMain.startLifetime = .66f;
            capMain.startSize = 6.4f;
            Drift(cap, 0f, 1.8f, 1.8f);
            SizeOverLife(cap, Curve((0f, .42f), (.12f, 1f), (.55f, 1.06f), (1f, 1.13f)));
            Fade(cap, .55f);
            cap.GetComponent<ParticleSystemRenderer>().sortingFudge = -2f;
            Puffs(root, "Base smoke", 7, .4f, .75f, 4f, local, ring: .65f, life: .42f, material: _m.Soot);
            Junk(root, "Workshop junk", -1, 11, 6f, 10f, .4f, .78f, local, floor.transform);
            Sparks(root, 16, local, .5f, 4f, 9f);
            _kit.Sound(root, "manki_boom_big", .9f, 1f);
            return Save(root);
        }

        private static GameObject KamikazeTrail()
        {
            var root = _kit.NewRoot("MankiKamikazeTrail", attackBound: true);
            var frame = Frame(root);
            foreach (var (side, x) in Nozzles)
            {
                var flame = Particles(frame, "Full throttle " + side, _m.CometFlame, 1f, ParticleSystemSimulationSpace.Local);
                flame.transform.localPosition = new Vector3(x, -.22f, 0f);
                var main = flame.main;
                main.loop = true;
                main.startLifetime = .075f;
                main.startSize = .24f;
                main.maxParticles = 12;
                var emission = flame.emission;
                emission.rateOverTime = 55f;
                var velocity = flame.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.x = new ParticleSystem.MinMaxCurve(0f);
                velocity.y = new ParticleSystem.MinMaxCurve(-4.8f);
                velocity.z = new ParticleSystem.MinMaxCurve(0f);
                Stretch(flame, 0f, 1.2f);
                SizeOverLife(flame, Curve((0f, .6f), (.4f, 1f), (1f, .2f)));

                var sparks = Particles(frame, "Comet sparks " + side, _m.Spark, 1f);
                sparks.transform.localPosition = new Vector3(x, -.45f, 0f);
                main = sparks.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.1f, .18f);
                main.startSize = new ParticleSystem.MinMaxCurve(.045f, .095f);
                emission = sparks.emission;
                emission.rateOverDistance = 4f;
                Drift(sparks, .25f, -.4f, .1f);
                Fade(sparks, .35f);

                var soot = Particles(frame, "Short soot " + side, _m.Soot, 1f);
                soot.transform.localPosition = new Vector3(x, -.52f, 0f);
                main = soot.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.1f, .17f);
                main.startSize = new ParticleSystem.MinMaxCurve(.12f, .2f);
                emission = soot.emission;
                emission.rateOverDistance = 2.6f;
                RandomFrame(soot, 2, 2);
                SizeOverLife(soot, Curve((0f, .45f), (.4f, 1f), (1f, .65f)));
                Fade(soot, .3f);
            }
            return Save(root);
        }
    }
}
#endif
