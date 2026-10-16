#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Client.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>Original, presentation-only art sample fitted to the accepted sanctuary shell.</summary>
    public static class SlopArenaSanctuaryArtCommands
    {
        private const string PrefabPath = "Assets/Resources/Stages/fightguy_sanctuary.prefab";
        private const string ArtPath = "Assets/Stages/fightguy_sanctuary/Presentation";
        private const string SampleRoot = "Cloudtop First Crossing";
        private const int Stone = 0, StoneLight = 1, StoneDark = 2, Grout = 3, Jade = 4,
            Wood = 5, WoodLight = 6, WoodDark = 7, Terra = 8, Iron = 9, Gold = 10, Cloud = 11, CloudShade = 12;

        [MenuItem("Tools/SLOPArena/Arcade/Phase 5/Apply Cloudtop First Crossing")]
        private static void ApplyMenu() => Apply();

        [CliCommand("sloparena.arcade.course.art-sample", "Apply the approved Cloudtop first-crossing presentation to the existing sanctuary prefab; preserve baked gameplay.",
            MainThreadRequired = true, Tags = new[] { "arcade", "authoring/stage" })]
        public static object Apply()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Apply sanctuary art in stopped Edit Mode with the current scene saved.");
            if (!File.Exists(PrefabPath)) throw new InvalidOperationException("Prepare the course before applying its art sample.");
            // Both external meshes must have passed the stage's catalog workset inspection.
            var peaks = new[] { "03d04f6f87e6d49409b84319f58f54a5", "83d9f920ea9269346ae62b253a4ff3cc" }
                .Select(id => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(id))).ToArray();
            if (peaks.Any(x => x == null)) throw new InvalidOperationException("Materialize the verified FantasyForest mountain workset first.");
            Directory.CreateDirectory(ArtPath); AssetDatabase.Refresh();
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                foreach (string name in new[] { "Training Courtyard", "Broken Bridge Takeoff", "Broken Bridge Landing",
                    "Bridge Detour Pole 1", "Bridge Detour Pole 2", "Bridge Detour Pole 3", "Sanctuary Foundation", "Sanctuary Hall", "Sanctuary Roof", "Sanctuary Crown" }) Find(root, name);
                var materials = MakeMaterials();
                var previous = root.transform.Find(SampleRoot);
                if (previous != null) Object.DestroyImmediate(previous.gameObject);
                var sample = new GameObject(SampleRoot); sample.transform.SetParent(root.transform, false);
                var details = new Geometry();
                foreach (string name in new[] { "Broken Bridge Takeoff", "Broken Bridge Landing" })
                    StoneDeck(Find(root, name), details, materials);
                MatchCourtyardJoin(Find(root, "Training Courtyard"), Find(root, "Broken Bridge Takeoff"));
                for (int i = 1; i <= 3; i++) TimberPole(Find(root, "Bridge Detour Pole " + i), details, materials);
                Pavilion(root, details, materials);
                AddMesh(sample.transform, "Original Stone Timber and Pavilion", SaveMesh("CloudtopDetails", details.Mesh()), materials);

                var distant = new GameObject("Distant Mountains and Pines"); distant.transform.SetParent(sample.transform, false);
                var trees = new Geometry();
                var placements = new[]
                {
                    (new Vector3(-145, -65, 105), new Vector3(.36f, 1.4f, .34f)),
                    (new Vector3(-82, -55, 160), new Vector3(.30f, 1.7f, .32f)),
                    (new Vector3(20, -85, 200), new Vector3(.44f, 2.2f, .38f)),
                    (new Vector3(155, -60, 135), new Vector3(.42f, 1.9f, .42f)),
                    (new Vector3(175, -65, -70), new Vector3(.40f, 1.7f, .36f)),
                    (new Vector3(70, -80, -190), new Vector3(.50f, 2.4f, .40f)),
                    (new Vector3(-130, -85, -150), new Vector3(.48f, 1.7f, .40f)),
                };
                var rock = Material("CloudtopDistantRock", new(.43f, .50f, .54f));
                int treeCount = 0;
                for (int i = 0; i < placements.Length; i++)
                {
                    var peak = (GameObject)PrefabUtility.InstantiatePrefab(peaks[i % peaks.Length], distant.transform);
                    peak.name = "Distant Peak " + (i + 1); peak.transform.localPosition = placements[i].Item1;
                    peak.transform.localRotation = Quaternion.Euler(0, i * 37, 0); peak.transform.localScale = placements[i].Item2;
                    foreach (var collider in peak.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                    foreach (var script in peak.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
                    foreach (var renderer in peak.GetComponentsInChildren<MeshRenderer>(true))
                        renderer.sharedMaterials = Enumerable.Repeat(rock, renderer.sharedMaterials.Length).ToArray();
                    for (int j = 0; j < 3; j++)
                    {
                        Vector3 point = placements[i].Item1 + new Vector3((j - 1) * 11, 0, (j % 2 == 0 ? 1 : -1) * 7);
                        float lowest = float.PositiveInfinity; bool supported = true;
                        foreach (var offset in new[] { Vector2.zero, new Vector2(.35f, 0), new Vector2(-.35f, 0), new Vector2(0, .35f), new Vector2(0, -.35f) })
                        {
                            if (!SupportHeight(peak, point.x + offset.x, point.z + offset.y, out float y)) { supported = false; break; }
                            lowest = Mathf.Min(lowest, y);
                        }
                        if (!supported || lowest < -20) continue;
                        point.y = lowest - .15f;
                        Pine(trees, point, 7 + (i + j) % 4, i * 51 + j * 27); treeCount++;
                    }
                }
                AddMesh(distant.transform, "Original Ridge Pines", SaveMesh("CloudtopPines", trees.Mesh()), materials);
                var clouds = new Geometry();
                for (int x = -3; x <= 3; x++)
                    for (int z = -3; z <= 3; z++)
                    {
                        float y = -44 - (x * x + z * z) % 3 * 2;
                        clouds.Ellipsoid(new(x * 95 + (z % 2) * 22, y, z * 95), new(80 + (x + 4) % 3 * 8, 13, 87), (x + z) % 3 == 0 ? CloudShade : Cloud);
                    }
                var cloudSea = AddMesh(sample.transform, "Cloud Sea Below Falls", SaveMesh("CloudtopClouds", clouds.Mesh()), materials);
                cloudSea.shadowCastingMode = ShadowCastingMode.Off; cloudSea.receiveShadows = false;
                var atmosphere = root.GetComponent<ArenaAtmosphere>() ?? root.AddComponent<ArenaAtmosphere>();
                atmosphere.Configure(new(.61f, .75f, .87f), new(.55f, .64f, .73f), new(.22f, .20f, .18f), .75f,
                    true, new(.65f, .76f, .85f), 90, 390);
                var localLight = root.transform.Find("LocalLighting")?.GetComponent<Light>();
                if (localLight != null) { localLight.intensity = .20f; localLight.color = new(1, .93f, .80f); }
                AssetDatabase.SaveAssets(); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return new { success = true, prefab = PrefabPath, sample = SampleRoot, peakCount = placements.Length, treeCount,
                    originalDetailTriangles = details.TriangleCount, cloudTriangles = clouds.TriangleCount,
                    gameplay = "unchanged; presentation only" };
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static GameObject Find(GameObject root, string name)
        { var t = root.transform.Find(name); return t != null ? t.gameObject : throw new InvalidOperationException("Missing approved surface: " + name); }

        private static Material[] MakeMaterials()
        {
            var colors = new[] { new Color(.50f,.45f,.37f), new Color(.52f,.47f,.39f), new Color(.48f,.43f,.35f),
                new Color(.36f,.34f,.29f), new Color(.18f,.31f,.27f), new Color(.36f,.22f,.12f),
                new Color(.44f,.29f,.16f), new Color(.24f,.15f,.09f), new Color(.48f,.19f,.11f),
                new Color(.16f,.19f,.19f), new Color(.75f,.51f,.17f), new Color(.57f,.61f,.64f), new Color(.54f,.58f,.62f) };
            var names = new[] { "Stone", "StoneLight", "StoneWorn", "Grout", "Jade", "Cedar", "CedarLight", "CedarDark", "Terracotta", "Iron", "Brass", "Cloud", "CloudShade" };
            var materials = names.Select((name, i) => Material("Cloudtop" + name, colors[i], i == Iron ? .35f : 0)).ToArray();
            foreach (int i in new[] { Cloud, CloudShade })
            { materials[i].SetFloat("_ReceiveShadows", 0); materials[i].EnableKeyword("_RECEIVE_SHADOWS_OFF"); }
            return materials;
        }

        private static Material Material(string name, Color color, float metallic = 0)
        {
            string path = ArtPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .12f); material.SetFloat("_Metallic", metallic);
            return material;
        }

        private static Mesh SaveMesh(string name, Mesh mesh)
        {
            mesh.name = name; string path = ArtPath + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); return existing;
        }

        private static MeshRenderer AddMesh(Transform parent, string name, Mesh mesh, Material[] materials)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = materials; return renderer; }

        private static void RemoveTop(GameObject host, Material material)
        {
            var filter = host.GetComponent<MeshFilter>();
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ArtPath + "/CloudtopSlabShell.asset");
            if (mesh == null)
            {
                mesh = Object.Instantiate(filter.sharedMesh); var normals = mesh.normals; var indices = mesh.triangles;
                var kept = new List<int>();
                for (int i = 0; i < indices.Length; i += 3)
                    if (normals[indices[i]].y < .9f) { kept.Add(indices[i]); kept.Add(indices[i + 1]); kept.Add(indices[i + 2]); }
                mesh.triangles = kept.ToArray(); mesh = SaveMesh("CloudtopSlabShell", mesh);
            }
            filter.sharedMesh = mesh; host.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void StoneDeck(GameObject host, Geometry g, Material[] materials)
        {
            Vector3 p = host.transform.localPosition, size = host.transform.localScale; float top = p.y + size.y / 2;
            RemoveTop(host, materials[Grout]);
            g.Top(new(p.x, top - .035f, p.z), size.x, size.z, Grout);
            int nx = Mathf.CeilToInt((size.x - .48f) / 2.3f), nz = Mathf.CeilToInt((size.z - .48f) / 1.3f);
            float dx = (size.x - .48f) / nx, dz = (size.z - .48f) / nz;
            float left = p.x - size.x / 2 + .24f, right = p.x + size.x / 2 - .24f;
            for (int z = 0; z < nz; z++) for (int x = 0; x <= nx; x++)
            {
                float start = Mathf.Max(left, left + dx * (x - (z % 2) * .5f));
                float end = Mathf.Min(right, left + dx * (x + 1 - (z % 2) * .5f));
                if (end - start < .05f) continue;
                g.Box(new((start + end) / 2, top - .015f, p.z - size.z / 2 + .24f + dz * (z + .5f)),
                    new(end - start - .018f, .03f, dz - .018f), (x * 17 + z * 11) % 3);
            }
            Rim(g, new(p.x, top - .02f, p.z), size.x, size.z, .24f, .04f, StoneDark);
            Rim(g, new(p.x, top - .20f, p.z), size.x + .006f, size.z + .006f, .04f, .18f, Jade);
        }

        private static void MatchCourtyardJoin(GameObject courtyard, GameObject takeoff)
        {
            // The accepted cubes overlap at the same top height. Keep the courtyard's
            // sides and pose; trim only the hidden top patch beneath the new paving.
            var shell = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Mesh>(ArtPath + "/CloudtopSlabShell.asset"));
            var vertices = shell.vertices.ToList(); var triangles = shell.triangles.ToList();
            var p = courtyard.transform.localPosition; var s = courtyard.transform.localScale;
            var t = takeoff.transform.localPosition; var size = takeoff.transform.localScale;
            float left = Mathf.Clamp((t.x - size.x / 2 - p.x) / s.x, -.5f, .5f);
            float right = Mathf.Clamp((t.x + size.x / 2 - p.x) / s.x, -.5f, .5f);
            float back = Mathf.Clamp((t.z - size.z / 2 - p.z) / s.z, -.5f, .5f);
            float front = Mathf.Clamp((t.z + size.z / 2 - p.z) / s.z, -.5f, .5f);
            foreach (var rect in new[] { new Rect(-.5f, -.5f, left + .5f, 1), new Rect(right, -.5f, .5f - right, 1),
                new Rect(left, -.5f, right - left, back + .5f), new Rect(left, front, right - left, .5f - front) })
            {
                if (rect.width < .00001f || rect.height < .00001f) continue;
                int n = vertices.Count;
                vertices.AddRange(new[] { new Vector3(rect.xMin, .5f, rect.yMin), new Vector3(rect.xMin, .5f, rect.yMax),
                    new Vector3(rect.xMax, .5f, rect.yMax), new Vector3(rect.xMax, .5f, rect.yMin) });
                triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
            }
            shell.Clear(); shell.SetVertices(vertices); shell.SetTriangles(triangles, 0); shell.RecalculateNormals(); shell.RecalculateBounds();
            courtyard.GetComponent<MeshFilter>().sharedMesh = SaveMesh("CloudtopCourtyardJoin", shell);
        }

        private static void TimberPole(GameObject host, Geometry g, Material[] materials)
        {
            var p = host.transform.localPosition; var size = host.transform.localScale; float top = p.y + size.y / 2;
            RemoveTop(host, materials[WoodDark]);
            g.Top(new(p.x, top - .025f, p.z), size.x, size.z, WoodDark);
            int count = 5; float width = size.x / count;
            for (int i = 0; i < count; i++) g.Box(new(p.x - size.x / 2 + width * (i + .5f), top - .0125f, p.z), new(width - .018f, .025f, size.z), i % 2 == 0 ? Wood : WoodLight);
            foreach (var normal in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
            {
                var u = new Vector3(normal.z, 0, -normal.x); float faceWidth = normal.x != 0 ? size.z : size.x;
                var center = p + normal * ((normal.x != 0 ? size.x : size.z) / 2 + .003f);
                for (int i = 0; i < 6; i++) g.Wall(center + u * (-faceWidth / 2 + faceWidth / 6 * (i + .5f)), u, faceWidth / 6 - .018f, size.y, i % 3 == 0 ? WoodLight : Wood);
            }
            foreach (float y in new[] { top - .45f, top - 1.85f })
            {
                Rim(g, new(p.x, y, p.z), size.x + .04f, size.z + .04f, .045f, .22f, Iron);
                foreach (float x in new[] { -.32f, .32f })
                    g.Box(new(p.x + size.x * x, y, p.z - size.z / 2 - .032f), new(.09f, .09f, .04f), Gold);
            }
        }

        private static void Pavilion(GameObject root, Geometry g, Material[] materials)
        {
            var foundation = Find(root, "Sanctuary Foundation"); foundation.GetComponent<MeshRenderer>().sharedMaterial = materials[StoneDark];
            var hall = Find(root, "Sanctuary Hall"); hall.GetComponent<MeshRenderer>().sharedMaterial = materials[Stone];
            foreach (var normal in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
            {
                var u = new Vector3(normal.z, 0, -normal.x); var center = hall.transform.localPosition + normal * 5.012f;
                g.Wall(center, u, 2.5f, 4.6f, Terra);
                foreach (float offset in new[] { -4.78f, -1.4f, 1.4f, 4.78f })
                    g.Box(center + u * offset, new(.22f, 7, .22f), WoodDark);
                g.Wall(center + Vector3.up * 2.65f, u, 9.8f, .30f, Wood);
                g.Wall(center - Vector3.up * 2.65f, u, 9.8f, .28f, WoodDark);
                for (int i = -2; i <= 2; i++) g.Wall(center + u * (i * .47f) + normal * .008f, u, .025f, 4.5f, WoodDark);
                g.Ring(center + normal * .018f, u, .55f, .045f, Gold);
                var f = foundation.transform.localPosition + normal * 8.006f;
                for (int row = 0; row < 8; row++)
                    for (int column = 0; column < 11; column++)
                    {
                        float x = -8 + (column + .5f) * 16 / 11;
                        g.Wall(f + Vector3.up * (-3.5f + row) + u * x, u, 16f / 11 - .022f, .974f, (row + column * 3) % 3);
                    }
            }
            foreach (string name in new[] { "Sanctuary Roof", "Sanctuary Crown" })
            {
                var roof = Find(root, name); roof.GetComponent<MeshRenderer>().sharedMaterial = materials[Terra];
                var p = roof.transform.localPosition; var size = roof.transform.localScale; float top = p.y + size.y / 2;
                Rim(g, new(p.x, top - .18f, p.z), size.x + .008f, size.z + .008f, .07f, .22f, Jade);
            }
        }

        private static void Rim(Geometry g, Vector3 p, float width, float depth, float border, float height, int material)
        {
            foreach (float x in new[] { -(width - border) / 2, (width - border) / 2 }) g.Box(p + new Vector3(x, 0, 0), new(border, height, depth), material);
            foreach (float z in new[] { -(depth - border) / 2, (depth - border) / 2 }) g.Box(p + new Vector3(0, 0, z), new(width - 2 * border, height, border), material);
        }

        private static void Pine(Geometry g, Vector3 p, float height, float yaw)
        {
            var lean = Quaternion.Euler(0, yaw, 0) * new Vector3(height * .14f, 0, 0);
            g.Branch(p, p + Vector3.up * height * .88f + lean, height * .035f, WoodDark);
            for (int i = 0; i < 3; i++)
            {
                var canopy = p + Vector3.up * height * (.55f + i * .19f) + lean * (i + 1) / 3;
                g.Branch(p + Vector3.up * height * (.4f + i * .18f), canopy, height * .018f, Wood);
                g.Ellipsoid(canopy, new(height * (.34f - i * .07f), height * .09f, height * (.25f - i * .045f)), Jade);
            }
        }

        private static bool SupportHeight(GameObject root, float x, float z, out float height)
        {
            height = float.NegativeInfinity;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh; if (mesh == null) continue;
                var v = mesh.vertices; var t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    var a = filter.transform.TransformPoint(v[t[i]]); var b = filter.transform.TransformPoint(v[t[i + 1]]); var c = filter.transform.TransformPoint(v[t[i + 2]]);
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) < .00001f) continue;
                    float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
                    float w = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
                    if (u >= -.0001f && w >= -.0001f && u + w <= 1.0001f) height = Mathf.Max(height, u * a.y + w * b.y + (1 - u - w) * c.y);
                }
            }
            return !float.IsNegativeInfinity(height);
        }

        private sealed class Geometry
        {
            private readonly List<Vector3> _vertices = new();
            private readonly List<int>[] _triangles = Enumerable.Range(0, 13).Select(_ => new List<int>()).ToArray();
            public int TriangleCount => _triangles.Sum(x => x.Count) / 3;
            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int material)
            {
                int n = _vertices.Count; _vertices.AddRange(new[] { a, b, c, d });
                _triangles[material].AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
            }
            public void Top(Vector3 p, float width, float depth, int material)
            { Vector3 x = Vector3.right * width / 2, z = Vector3.forward * depth / 2; Quad(p - x - z, p - x + z, p + x + z, p + x - z, material); }
            public void Wall(Vector3 p, Vector3 tangent, float width, float height, int material)
            { var x = tangent * width / 2; var y = Vector3.up * height / 2; Quad(p - x - y, p + x - y, p + x + y, p - x + y, material); }
            public void Box(Vector3 p, Vector3 size, int material)
            {
                var x = Vector3.right * size.x / 2; var y = Vector3.up * size.y / 2; var z = Vector3.forward * size.z / 2;
                Top(p + y, size.x, size.z, material);
                Quad(p - x - y - z, p + x - y - z, p + x - y + z, p - x - y + z, material);
                Quad(p - x - y + z, p + x - y + z, p + x + y + z, p - x + y + z, material);
                Quad(p + x - y - z, p - x - y - z, p - x + y - z, p + x + y - z, material);
                Quad(p + x - y + z, p + x - y - z, p + x + y - z, p + x + y + z, material);
                Quad(p - x - y - z, p - x - y + z, p - x + y + z, p - x + y - z, material);
            }
            public void Ring(Vector3 p, Vector3 tangent, float radius, float thickness, int material)
            {
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8, b = (i + 1) * Mathf.PI / 8;
                    var va = tangent * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a); var vb = tangent * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    Quad(p + va * (radius - thickness), p + va * radius, p + vb * radius, p + vb * (radius - thickness), material);
                }
            }
            public void Branch(Vector3 start, Vector3 end, float radius, int material)
            {
                var axis = (end - start).normalized; var x = Vector3.Cross(axis, Vector3.forward).normalized;
                if (x.sqrMagnitude < .1f) x = Vector3.right; var z = Vector3.Cross(axis, x).normalized;
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3, b = (i + 1) * Mathf.PI / 3;
                    var va = (x * Mathf.Cos(a) + z * Mathf.Sin(a)) * radius; var vb = (x * Mathf.Cos(b) + z * Mathf.Sin(b)) * radius;
                    Quad(start + va, start + vb, end + vb * .45f, end + va * .45f, material);
                }
            }
            public void Ellipsoid(Vector3 p, Vector3 radius, int material)
            {
                foreach (int y in new[] { -1, 1 }) for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI / 2, b = (i + 1) * Mathf.PI / 2;
                    var v0 = Vector3.up * y; var v1 = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var v2 = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
                    if (y > 0) SphereTriangle(v0, v2, v1, 2, p, radius, material); else SphereTriangle(v0, v1, v2, 2, p, radius, material);
                }
            }
            private void SphereTriangle(Vector3 a, Vector3 b, Vector3 c, int depth, Vector3 p, Vector3 radius, int material)
            {
                if (depth > 0)
                {
                    var ab = (a + b).normalized; var bc = (b + c).normalized; var ca = (c + a).normalized;
                    SphereTriangle(a, ab, ca, depth - 1, p, radius, material); SphereTriangle(ab, b, bc, depth - 1, p, radius, material);
                    SphereTriangle(ca, bc, c, depth - 1, p, radius, material); SphereTriangle(ab, bc, ca, depth - 1, p, radius, material); return;
                }
                int n = _vertices.Count; _vertices.Add(p + Vector3.Scale(a, radius)); _vertices.Add(p + Vector3.Scale(b, radius)); _vertices.Add(p + Vector3.Scale(c, radius));
                _triangles[material].AddRange(new[] { n, n + 1, n + 2 });
            }
            public Mesh Mesh()
            {
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32, subMeshCount = _triangles.Length };
                mesh.SetVertices(_vertices); for (int i = 0; i < _triangles.Length; i++) mesh.SetTriangles(_triangles[i], i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
    }
}
#endif
