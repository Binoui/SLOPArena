#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.TextCore.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    /// <summary>Original cosmetic finish for the approved Slopball club. Never edits the arena source.</summary>
    public static class SlopballClubPresentation
    {
        private const string PrefabPath = "Assets/Resources/Stages/slopball.prefab";
        private const string Prefix = "Club ";

        [CliCommand("sloparena.arcade.slopball.finish", "Apply the approved underground club finish to the existing cosmetic prefab, without rebaking gameplay.",
            MainThreadRequired = true, Tags = new[] { "arcade", "authoring/stage" })]
        public static object Finish()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Finish Slopball in Edit Mode.");
            var assets = new Assets();
            var concrete = assets.Material("Concrete", new(.49f, .465f, .39f), .08f, assets.Grain("Concrete Grain", .13f));
            var padding = assets.Material("Teal Padding", new(.10f, .28f, .29f), .22f);
            var frame = assets.Material("Steel", new(.16f, .23f, .22f), .3f);
            var roof = assets.Material("Ceiling", new(.25f, .27f, .24f), .1f, assets.Grain("Concrete Grain", .13f));
            var player = assets.Material("Receive Floor", new(.17f, .36f, .37f), .2f, assets.Grain("Floor Grain", .035f));
            var sender = assets.Material("Sender Floor", new(.46f, .27f, .215f), .2f, assets.Grain("Floor Grain", .035f));
            var paint = assets.Material("Court Paint", new(.66f, .66f, .56f), .1f, unlit: true);
            var net = assets.Material("Net Cord", new(.21f, .25f, .23f), .1f, unlit: true);
            var tape = assets.Material("Net Tape", new(.88f, .81f, .60f), .1f, unlit: true);
            var amber = assets.Material("Amber Trim", new(.88f, .35f, .18f), .1f, unlit: true);
            var lamp = assets.Material("Warm Diffuser", new(.95f, .80f, .52f), .1f, unlit: true);
            var sign = assets.Material("Sign Back", new(.055f, .105f, .105f), .1f, unlit: true);
            var coralPadding = assets.Material("Coral Padding", new(.50f, .22f, .16f), .22f);
            var mintPadding = assets.Material("Mint Padding", new(.24f, .42f, .36f), .22f);
            var mascotMint = assets.Material("Mascot Mint", new(.30f, .55f, .43f), .1f, unlit: true);
            var mascotInk = assets.Material("Mascot Ink", new(.035f, .12f, .125f), .1f, unlit: true);
            var font = assets.Font();
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Remove(root.transform.Find("ClubBlockout"));
                Remove(root.transform.Find("Club Finish"));
                var finish = Child(root.transform, "Club Finish");
                BindSurface(root.transform.Find("CourtShell"), concrete, assets);
                BindSurface(root.transform.Find("PlayerHalf"), player, assets);
                BindSurface(root.transform.Find("SenderHalf"), sender, assets);
                foreach (var line in root.GetComponentsInChildren<LineRenderer>())
                {
                    line.sharedMaterial = line.name.StartsWith("Net strand") || line.name == "Net row" ? net
                        : line.name.StartsWith("Net") ? tape : paint;
                    if (line.name.StartsWith("Net strand") || line.name == "Net row") line.startWidth = line.endWidth = .012f;
                    if (line.name == "Net top") line.startWidth = line.endWidth = .065f;
                    line.shadowCastingMode = ShadowCastingMode.Off;
                }
                var pads = new Geometry(); var coralPads = new Geometry(); var mintPads = new Geometry(); var steel = new Geometry(); var accents = new Geometry(); var lamps = new Geometry();
                for (int side = 0; side < 4; side++)
                {
                    var rotation = Quaternion.Euler(0, side * 90, 0);
                    for (int bay = 0; bay < 10; bay++)
                    {
                        float x = -18 + 4 * bay;
                        int pattern = (bay + side * 2) % 6;
                        var bayGeometry = pattern == 1 ? coralPads : pattern == 4 ? mintPads : pads;
                        bayGeometry.BevelPanel(new(x, 1.48f, 20.12f), new(3.84f, 2.78f, .32f), rotation);
                        // Broad wall fields remain quiet; shallow strips clarify the established plane.
                        steel.Box(new(x, 3.03f, 20.1f), new(3.84f, .12f, .26f), rotation);
                    }
                    for (int pier = 1; pier < 4; pier++)
                        steel.Box(new(-20 + 10 * pier, 11.6f, 20.14f), new(.24f, 16.8f, .32f), rotation);
                    accents.Box(new(0, 3.14f, 20.12f), new(39.8f, .045f, .36f), rotation);
                    steel.Box(new(0, 13.8f, 20.12f), new(39.8f, .24f, .32f), rotation);
                    for (int fixture = 0; fixture < 4; fixture++)
                    {
                        float x = -15 + fixture * 10;
                        steel.Box(new(x, 4.35f, 20.10f), new(2.8f, .22f, .30f), rotation);
                        lamps.Box(new(x, 4.35f, 19.965f), new(2.55f, .085f, .07f), rotation);
                    }
                }
                var ceiling = new Geometry();
                ceiling.Quad(new(-20, 19.998f, -20), new(20, 19.998f, -20), new(20, 19.998f, 20), new(-20, 19.998f, 20), 5);
                foreach (float x in new[] { -16f, -8f, 0f, 8f, 16f })
                    steel.Box(new(x, 20.24f, 0), new(.42f, .55f, 39.8f), Quaternion.identity);
                // Different soffit heights prevent coplanar intersections at roof-grid joins.
                foreach (float z in new[] { -16f, -8f, 0f, 8f, 16f })
                    steel.Box(new(0, 20.25f, z), new(39.8f, .55f, .32f), Quaternion.identity);
                foreach (float x in new[] { -12f, 12f }) foreach (float z in new[] { -8f, 8f })
                {
                    steel.Box(new(x, 20.10f, z), new(4.3f, .28f, .4f), Quaternion.identity);
                    lamps.Box(new(x, 19.985f, z), new(3.9f, .06f, .22f), Quaternion.identity);
                }
                pads.Save(finish, "Padded Wall Bays", padding, assets);
                coralPads.Save(finish, "Coral Wall Bays", coralPadding, assets);
                mintPads.Save(finish, "Mint Wall Bays", mintPadding, assets);
                steel.Save(finish, "Wall Frames and Roof Grid", frame, assets);
                accents.Save(finish, "Perimeter Accent", amber, assets);
                lamps.Save(finish, "Warm Fixtures", lamp, assets);
                ceiling.Save(finish, "Ceiling Surface", roof, assets);
                var markings = new Geometry();
                markings.FloorRectangle(-18.7f, 18.7f, -18.7f, 18.7f, .085f);
                markings.FloorRectangle(-12, 12, -16, 16, .065f);
                markings.FloorLine(new(-12, .025f, -7), new(12, .025f, -7), .065f);
                markings.FloorLine(new(-12, .025f, 7), new(12, .025f, 7), .065f);
                markings.Save(finish, "Court Markings", paint, assets);
                var board = new Geometry();
                board.Box(new(0, 2.2f, 20.13f), new(17.6f, 2.7f, .45f), Quaternion.identity);
                board.Save(finish, "Club Sign", sign, assets);
                var border = new Geometry();
                border.Box(new(0, 3.50f, 19.93f), new(17.5f, .045f, .08f), Quaternion.identity);
                border.Box(new(0, .90f, 19.93f), new(17.5f, .045f, .08f), Quaternion.identity);
                border.Save(finish, "Sign Border", amber, assets);
                Lettering(finish, "Club Name", "SLOPBALL", new(0, 2.45f, 19.89f), Quaternion.identity, 1.35f, new(.88f, .84f, .69f), font, assets);
                Lettering(finish, "Club Subtitle", "HIT BALL   FEEL GOOD", new(0, 1.22f, 19.89f), Quaternion.identity, .27f, new(.66f, .69f, .59f), font, assets);
                Lettering(finish, "Rear Motto", "KEEP IT MOVING", new(0, 1.7f, -19.94f), Quaternion.Euler(0, 180, 0), .45f, new(.34f, .48f, .45f), font, assets);
                foreach (int side in new[] { -1, 1 })
                    Lettering(finish, "Bay Number " + side, side < 0 ? "01" : "02", new(side * 19.94f, 1.65f, -10),
                        Quaternion.Euler(0, side < 0 ? -90 : 90, 0), .65f, new(.43f, .54f, .49f), font, assets);
                PlayfulPaint(finish, tape, mascotMint, mascotInk, amber, font, assets);
                DressSender(root.transform.Find("Ball Sender"), frame, padding, amber, lamp, assets);
                var localLight = root.transform.Find("LocalLighting").GetComponent<Light>();
                localLight.intensity = .55f; localLight.color = new(1f, .91f, .77f); localLight.shadows = LightShadows.None;
                foreach (float x in new[] { -12f, 12f })
                {
                    var light = Child(finish, "Warm Court Fill").gameObject.AddComponent<Light>();
                    light.transform.localPosition = new(x, 10, 0); light.type = LightType.Point;
                    light.color = new(1f, .78f, .52f); light.intensity = 28; light.range = 36; light.shadows = LightShadows.None;
                }
                if (root.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Club presentation cannot contain Colliders.");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return new { success = true, prefab = PrefabPath, cosmeticOnly = true };
        }

        private static void PlayfulPaint(Transform parent, Material cream, Material mint, Material ink,
            Material coral, FontAsset font, Assets assets)
        {
            var outline = new Geometry(); var fill = new Geometry(); var expression = new Geometry(); var accents = new Geometry();
            var center = new Vector3(-7.1f, 2.23f, 19.89f);
            outline.Oval(center, new(.98f, .90f), Quaternion.identity, wobble: true);
            // Separate the thin paint layers so overlapping triangles cannot fight for depth.
            fill.Oval(center + Vector3.back * .005f, new(.88f, .80f), Quaternion.identity, wobble: true);
            // A lopsided painted ball with a wink; its flat mint silhouette differs from the live ball.
            center += Vector3.back * .01f;
            expression.Stroke2D(center, new[] { new Vector2(-.40f,.15f), new(-.27f,.23f), new(-.13f,.15f) }, .07f, Quaternion.identity);
            expression.Oval(center + new Vector3(.24f,.20f,0), new(.055f,.115f), Quaternion.identity);
            expression.Stroke2D(center, new[] { new Vector2(-.30f,-.20f), new(-.17f,-.31f), new(0,-.35f), new(.18f,-.30f), new(.31f,-.18f) }, .065f, Quaternion.identity);
            expression.Stroke2D(center, new[] { new Vector2(-.65f,.53f), new(-.28f,.57f), new(.07f,.64f), new(.34f,.70f) }, .045f, Quaternion.identity);
            accents.Oval(center + new Vector3(-.50f,-.15f,-.005f), new(.11f,.06f), Quaternion.identity);
            accents.Oval(center + new Vector3(.48f,-.14f,-.005f), new(.11f,.06f), Quaternion.identity);
            accents.Star(new(7.15f,2.27f,19.87f), .79f, Quaternion.Euler(0,0,-9));
            accents.Stroke2D(new(0,0,19.87f), new[] { new Vector2(-4.8f,1.64f), new(-1.5f,1.59f), new(2.8f,1.67f), new(4.8f,1.73f) }, .065f, Quaternion.identity);
            for (int side = 0; side < 4; side++)
            {
                var rotation = Quaternion.Euler(0,side*90,0);
                foreach (float x in new[] { -14f,14f })
                {
                    var origin = rotation * new Vector3(x,1.45f,19.90f);
                    accents.Stroke2D(origin, new[] { new Vector2(-1.15f,-.28f), new(-.65f,.35f), new(-.1f,-.28f), new(.45f,.20f), new(1f,-.28f) }, .11f, rotation);
                }
            }
            outline.Save(parent,"Mascot Outline",cream,assets);
            fill.Save(parent,"Smiling Ball Mascot",mint,assets);
            expression.Save(parent,"Mascot Expression",ink,assets);
            accents.Save(parent,"Playful Painted Accents",coral,assets);
            Lettering(parent,"Club Cheer","GO",new(7.15f,2.27f,19.84f),Quaternion.Euler(0,0,-9),.42f,new(.88f,.84f,.69f),font,assets);
        }

        private static void BindSurface(Transform target, Material material, Assets assets)
        {
            var filter = target.GetComponent<MeshFilter>();
            var mesh = Object.Instantiate(filter.sharedMesh);
            var vertices = mesh.vertices; var normals = mesh.normals; var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                uv[i] = Mathf.Abs(normals[i].y) > .5f ? new(vertices[i].x / 5, vertices[i].z / 5)
                    : Mathf.Abs(normals[i].x) > .5f ? new(vertices[i].z / 5, vertices[i].y / 5) : new(vertices[i].x / 5, vertices[i].y / 5);
            mesh.uv = uv; filter.sharedMesh = assets.Store(target.name + " Surface", mesh);
            target.GetComponent<MeshRenderer>().sharedMaterial = material;
            target.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void DressSender(Transform sender, Material steel, Material padding, Material trim, Material diffuser, Assets assets)
        {
            var old = sender.GetComponent<MeshRenderer>(); if (old != null) old.enabled = false;
            Remove(sender.Find("Club Sender"));
            var root = Child(sender, "Club Sender");
            // The accepted parent retains its position/scale; compensate only the cosmetic child.
            root.localScale = new(1 / sender.localScale.x, 1 / sender.localScale.y, 1 / sender.localScale.z);
            var chassis = new Geometry();
            chassis.Box(new(0, -.30f, 0), new(.8f, 1.55f, .7f), Quaternion.identity);
            chassis.Box(new(0, -1.02f, 0), new(.95f, .16f, .85f), Quaternion.identity);
            chassis.Box(new(0, .61f, .14f), new(.28f, .45f, .28f), Quaternion.identity);
            chassis.Box(new(0, .86f, .05f), new(.72f, .3f, .65f), Quaternion.Euler(-22, 0, 0));
            chassis.Save(root, "Sender Chassis", steel, assets);
            var body = new Geometry(); body.BevelPanel(new(0, -.25f, -.32f), new(.67f, .9f, .08f), Quaternion.identity);
            body.Save(root, "Sender Teal Face", padding, assets);
            var label = new Geometry(); label.Box(new(0, .18f, -.378f), new(.42f, .055f, .03f), Quaternion.identity);
            label.Save(root, "Sender Stripe", trim, assets);
            var exit = new Geometry(); exit.Box(new(0, 1.05f, -.16f), new(.48f, .07f, .28f), Quaternion.Euler(-22, 0, 0));
            exit.Save(root, "Sender Exit", diffuser, assets);
            var eyes = new Geometry();
            eyes.Oval(new(-.16f,-.14f,-.39f), new(.055f,.08f), Quaternion.identity);
            eyes.Oval(new(.16f,-.14f,-.39f), new(.055f,.08f), Quaternion.identity);
            eyes.Save(root,"Sender Eyes",diffuser,assets);
            var smile = new Geometry();
            smile.Stroke2D(new(0,0,-.39f), new[] { new Vector2(-.15f,-.34f), new(-.08f,-.40f), new(.07f,-.40f), new(.16f,-.33f) }, .035f, Quaternion.identity);
            smile.Save(root,"Sender Smile",trim,assets);
        }

        private static void Lettering(Transform parent, string name, string text, Vector3 position, Quaternion rotation,
            float height, Color color, FontAsset font, Assets assets)
        {
            // Reuse the project's existing TextCore atlas; persist plain glyph meshes.
            // No text renderer, dynamic font atlas or gameplay component is needed at runtime.
            float width = 0, minY = float.MaxValue, maxY = float.MinValue;
            foreach (char c in text)
            {
                if (!font.characterLookupTable.TryGetValue(c, out var character))
                    throw new InvalidOperationException("Club font is missing character " + c);
                var m = character.glyph.metrics; width += m.horizontalAdvance;
                if (c == ' ') continue;
                minY = Mathf.Min(minY, m.horizontalBearingY - m.height); maxY = Mathf.Max(maxY, m.horizontalBearingY);
            }
            float scale = height / (maxY - minY), cursor = -width / 2, centerY = (minY + maxY) / 2;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
            var atlas = font.atlasTextures[0]; float padding = font.atlasPadding;
            foreach (char c in text)
            {
                var glyph = font.characterLookupTable[c].glyph; var m = glyph.metrics; var r = glyph.glyphRect;
                if (c != ' ')
                {
                    float left = (cursor + m.horizontalBearingX - padding) * scale;
                    float right = (cursor + m.horizontalBearingX + m.width + padding) * scale;
                    float top = (m.horizontalBearingY + padding - centerY) * scale;
                    float bottom = (m.horizontalBearingY - m.height - padding - centerY) * scale;
                    int start = vertices.Count;
                    vertices.AddRange(new[] { new Vector3(left,bottom,0), new(left,top,0), new(right,top,0), new(right,bottom,0) });
                    uv.AddRange(new[] { new Vector2((r.x-padding)/atlas.width,(r.y-padding)/atlas.height),
                        new((r.x-padding)/atlas.width,(r.y+r.height+padding)/atlas.height),
                        new((r.x+r.width+padding)/atlas.width,(r.y+r.height+padding)/atlas.height),
                        new((r.x+r.width+padding)/atlas.width,(r.y-padding)/atlas.height) });
                    indices.AddRange(new[] { start,start+1,start+2,start,start+2,start+3 });
                }
                cursor += m.horizontalAdvance;
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(indices,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var material = assets.Material(name + " Ink", color, .1f, assets.FontMask(font), unlit: true);
            material.SetFloat("_AlphaClip",1); material.SetFloat("_Cutoff",.5f); material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType","TransparentCutout"); material.renderQueue = (int)RenderQueue.AlphaTest;
            var child = Child(parent,name); child.gameObject.AddComponent<MeshFilter>().sharedMesh = assets.Store(name + " Lettering",mesh);
            var renderer = child.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            child.localPosition = position; child.localRotation = rotation;
        }

        private static Transform Child(Transform parent, string name)
        { var child = new GameObject(name); child.transform.SetParent(parent, false); return child.transform; }
        private static void Remove(Transform item) { if (item != null) Object.DestroyImmediate(item.gameObject); }

        private sealed class Assets
        {
            private readonly Dictionary<string, Object> _objects = AssetDatabase.LoadAllAssetsAtPath(PrefabPath)
                .Where(x => x.name.StartsWith(Prefix)).GroupBy(x => x.name).ToDictionary(x => x.Key, x => x.First());
            public T Store<T>(string name, T value) where T : Object
            {
                string key = Prefix + name;
                if (_objects.TryGetValue(key, out var existing) && existing is T asset)
                {
                    if (asset is Mesh mesh && value is Mesh proposed)
                    {
                        mesh.Clear(); mesh.vertices = proposed.vertices; mesh.triangles = proposed.triangles;
                        mesh.normals = proposed.normals; mesh.uv = proposed.uv; mesh.uv2 = proposed.uv2;
                        mesh.colors32 = proposed.colors32; mesh.tangents = proposed.tangents; mesh.bounds = proposed.bounds;
                    }
                    else EditorUtility.CopySerialized(value, asset);
                    asset.name = key; // CopySerialized also copies the temporary material's name.
                    Object.DestroyImmediate(value); EditorUtility.SetDirty(asset); return asset;
                }
                value.name = key; AssetDatabase.AddObjectToAsset(value, PrefabPath); _objects[key] = value; return value;
            }
            public Texture2D Grain(string name, float amplitude)
            {
                if (_objects.TryGetValue(Prefix + name, out var existing)) return (Texture2D)existing;
                const int size = 128; var pixels = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    uint hash = unchecked((uint)(x * 73856093 ^ y * 19349663)); hash ^= hash >> 13; hash *= 1274126177;
                    float noise = (hash & 65535) / 65535f - .5f;
                    float grain = .86f + amplitude * (noise + .5f * (Mathf.PerlinNoise(x / 17f, y / 17f) - .5f));
                    pixels[y * size + x] = new(grain, grain, grain, 1);
                }
                var texture = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
                texture.SetPixels(pixels); texture.Apply(); return Store(name, texture);
            }
            public Material Material(string name, Color color, float smoothness, Texture2D texture = null, bool unlit = false)
            {
                var material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", smoothness);
                if (texture != null) material.SetTexture("_BaseMap", texture);
                return Store(name, material);
            }
            public Texture2D FontMask(FontAsset font)
            {
                if (_objects.TryGetValue(Prefix + "Lettering Mask", out var existing)) return (Texture2D)existing;
                var atlas = font.atlasTextures[0]; var pixels = atlas.GetPixels32();
                // TextCore atlases store distance in alpha with black RGB. A normal
                // URP material needs white RGB so the chosen ink color survives.
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255,255,255,pixels[i].a);
                var mask = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear };
                mask.SetPixels32(pixels); mask.Apply(); return Store("Lettering Mask",mask);
            }
            public FontAsset Font()
                => AssetDatabase.LoadAssetAtPath<FontAsset>("Assets/Fonts/ArchivoBlack.asset")
                    ?? throw new InvalidOperationException("The club needs the existing ArchivoBlack font asset.");
        }

        private sealed class Geometry
        {
            private readonly List<Vector3> _vertices = new(); private readonly List<int> _indices = new();
            private readonly List<Vector2> _uv = new();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float tiles = 1)
            {
                int start = _vertices.Count; _vertices.AddRange(new[] { a, b, c, d });
                _indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                _uv.AddRange(new[] { Vector2.zero, new Vector2(0, tiles), new Vector2(tiles, tiles), new Vector2(tiles, 0) });
            }
            private void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int start = _vertices.Count; _vertices.AddRange(new[] {a,b,c}); _indices.AddRange(new[] {start,start+1,start+2});
                _uv.AddRange(new[] {Vector2.zero,Vector2.zero,Vector2.zero});
            }
            public void Oval(Vector3 center, Vector2 radius, Quaternion rotation, bool wobble = false)
            {
                Vector3 Point(int index)
                {
                    float angle = index * Mathf.PI * 2 / 32;
                    float shape = wobble ? 1 + .045f * Mathf.Sin(angle * 3) + .025f * Mathf.Cos(angle * 5) : 1;
                    return center + rotation * new Vector3(Mathf.Cos(angle)*radius.x*shape,Mathf.Sin(angle)*radius.y*shape,0);
                }
                for (int i=0;i<32;i++) Triangle(center,Point(i+1),Point(i));
            }
            public void Star(Vector3 center, float radius, Quaternion rotation)
            {
                Vector3 Point(int index)
                {
                    float angle = index * Mathf.PI / 8; float r = (index%2==0 ? radius : radius*.52f);
                    return center + rotation * new Vector3(Mathf.Cos(angle)*r,Mathf.Sin(angle)*r,0);
                }
                for(int i=0;i<16;i++) Triangle(center,Point(i+1),Point(i));
            }
            public void Stroke2D(Vector3 center, IReadOnlyList<Vector2> points, float width, Quaternion rotation)
            {
                for(int i=0;i<points.Count-1;i++)
                {
                    Vector2 delta=(points[i+1]-points[i]).normalized; Vector2 side=new(-delta.y*width/2,delta.x*width/2);
                    Vector3 P(Vector2 point) => center + rotation * new Vector3(point.x,point.y,0);
                    Quad(P(points[i]+side),P(points[i+1]+side),P(points[i+1]-side),P(points[i]-side));
                }
            }
            public void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                Vector3 Point(float x, float y, float z) => rotation * (center + Vector3.Scale(new(x, y, z), size));
                Quad(Point(-.5f,-.5f,-.5f),Point(-.5f,.5f,-.5f),Point(.5f,.5f,-.5f),Point(.5f,-.5f,-.5f));
                Quad(Point(.5f,-.5f,.5f),Point(.5f,.5f,.5f),Point(-.5f,.5f,.5f),Point(-.5f,-.5f,.5f));
                Quad(Point(-.5f,-.5f,.5f),Point(-.5f,.5f,.5f),Point(-.5f,.5f,-.5f),Point(-.5f,-.5f,-.5f));
                Quad(Point(.5f,-.5f,-.5f),Point(.5f,.5f,-.5f),Point(.5f,.5f,.5f),Point(.5f,-.5f,.5f));
                Quad(Point(-.5f,.5f,-.5f),Point(-.5f,.5f,.5f),Point(.5f,.5f,.5f),Point(.5f,.5f,-.5f));
                Quad(Point(-.5f,-.5f,.5f),Point(-.5f,-.5f,-.5f),Point(.5f,-.5f,-.5f),Point(.5f,-.5f,.5f));
            }
            public void BevelPanel(Vector3 center, Vector3 size, Quaternion rotation)
            {
                float x = size.x / 2, y = size.y / 2, back = size.z / 2, front = -size.z / 2;
                Vector3 P(float a, float b, float c) => rotation * (center + new Vector3(a,b,c));
                const float bevel = .055f;
                Quad(P(-x+bevel,-y+bevel,front),P(-x+bevel,y-bevel,front),P(x-bevel,y-bevel,front),P(x-bevel,-y+bevel,front));
                Quad(P(-x,-y,back),P(-x,y,back),P(-x+bevel,y-bevel,front),P(-x+bevel,-y+bevel,front));
                Quad(P(x-bevel,-y+bevel,front),P(x-bevel,y-bevel,front),P(x,y,back),P(x,-y,back));
                Quad(P(-x+bevel,y-bevel,front),P(-x,y,back),P(x,y,back),P(x-bevel,y-bevel,front));
                Quad(P(-x,-y,back),P(-x+bevel,-y+bevel,front),P(x-bevel,-y+bevel,front),P(x,-y,back));
            }
            public void FloorLine(Vector3 a, Vector3 b, float width)
            { var side = Vector3.Cross((b-a).normalized,Vector3.up)*width/2; Quad(a+side,b+side,b-side,a-side); }
            public void FloorRectangle(float minX,float maxX,float minZ,float maxZ,float width)
            {
                FloorLine(new(minX,.025f,minZ),new(minX,.025f,maxZ),width); FloorLine(new(minX,.025f,maxZ),new(maxX,.025f,maxZ),width);
                FloorLine(new(maxX,.025f,maxZ),new(maxX,.025f,minZ),width); FloorLine(new(maxX,.025f,minZ),new(minX,.025f,minZ),width);
            }
            public void Save(Transform parent, string name, Material material, Assets assets)
            {
                var mesh = new Mesh(); mesh.SetVertices(_vertices); mesh.SetTriangles(_indices,0); mesh.SetUVs(0,_uv); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var child = Child(parent,name); child.gameObject.AddComponent<MeshFilter>().sharedMesh = assets.Store(name + " Mesh",mesh);
                var renderer = child.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }
}
#endif
