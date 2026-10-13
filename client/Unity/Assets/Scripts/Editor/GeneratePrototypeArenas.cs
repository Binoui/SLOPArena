using System;
using System.Collections.Generic;
using System.Reflection;
using SlopArena.Client.World;

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// THROWAWAY LOOK-DEV GENERATOR. Builds five playable arena prefabs and bakes their
/// authoritative collision data. Delete this tool after the layouts are accepted.
/// </summary>
public static class GeneratePrototypeArenas
{
    private const string MaterialRoot = "Assets/Art/Stages/GeneratedPrototypes";
    private const string PrefabRoot = "Assets/Resources/Stages";

    private readonly struct Box
    {
        public readonly string Name;
        public readonly Vector3 Center;
        public readonly Vector3 Size;

        public Box(string name, Vector3 center, Vector3 size)
        {
            Name = name;
            Center = center;
            Size = size;
        }
    }

    private readonly struct Spawn
    {
        public readonly Vector3 Position;
        public readonly float Yaw;

        public Spawn(Vector3 position, float yaw)
        {
            Position = position;
            Yaw = yaw;
        }
    }

    [MenuItem("Tools/SlopArena/Prototypes/Generate Arenas")]
    public static void GenerateAll()
    {
        EnsureFolder("Assets/Art/Stages", "GeneratedPrototypes");

        GenerateSlopCourt();
        GenerateSplashDeck();
        GenerateAfterHours();
        GenerateRecCenterRoof();
        GeneratePicnicPanic();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PrototypeArenas] Generated and baked five match arenas.");
    }

    private static void GenerateSlopCourt()
    {
        const string key = "slop_court";
        var boxes = new[]
        {
            new Box("Main Court", new Vector3(0f, -0.25f, 0f), new Vector3(26f, 0.5f, 22f)),
            new Box("West Wing", new Vector3(-15f, -0.25f, 0f), new Vector3(4f, 0.5f, 14f)),
            new Box("East Wing", new Vector3(15f, -0.25f, 0f), new Vector3(4f, 0.5f, 14f)),
        };
        var spawns = CardinalSpawns(6f, 0.85f);

        var teal = Mat(key, "Court", new Color(0.24f, 0.43f, 0.44f), 0.22f);
        var yellow = Mat(key, "Trim", new Color(0.78f, 0.62f, 0.26f), 0.16f);
        var coral = Mat(key, "Coral", new Color(0.62f, 0.31f, 0.28f), 0.12f);
        var cream = Mat(key, "Cream", new Color(0.82f, 0.78f, 0.66f), 0.10f);
        var navy = Mat(key, "Navy", new Color(0.10f, 0.20f, 0.24f), 0.25f);

        var root = NewRoot("Slop Court");
        root.AddComponent<ArenaAtmosphere>().Configure(
            new Color(0.50f, 0.76f, 0.94f),
            new Color(0.66f, 0.78f, 0.88f),
            new Color(0.26f, 0.34f, 0.40f),
            1.05f);
        AddCollisionVisuals(root, boxes, teal);
        AddChamferedCourtTrim(root, yellow);

        for (int x = -8; x <= 8; x += 4)
            Cube(root, $"Lane Mark {x}", new Vector3(x, 0.04f, 0f), new Vector3(0.16f, 0.05f, 19f), cream);
        Cube(root, "Center Mark", new Vector3(0f, 0.055f, 0f), new Vector3(6f, 0.045f, 0.18f), coral);

        Pavilion(root, new Vector3(-20f, 0f, 13f), navy, cream, coral);
        Pavilion(root, new Vector3(20f, 0f, 13f), navy, cream, coral);
        Scoreboard(root, new Vector3(0f, 5.5f, 17f), navy, coral);
        Banners(root, 18.5f, coral, cream);
        AddSpawnMarkers(root, spawns);

        SaveAndBake(key, "Slop Court", "#35a4a8", root, boxes, spawns, -12f);
    }

    private static void GenerateSplashDeck()
    {
        const string key = "splash_deck";
        var boxes = new[]
        {
            new Box("Pool Deck", new Vector3(0f, -0.25f, 0f), new Vector3(30f, 0.5f, 22f)),
            new Box("West Diving Board", new Vector3(-10.5f, 0.75f, 0f), new Vector3(6f, 1.5f, 5f)),
            new Box("East Diving Board", new Vector3(10.5f, 0.75f, 0f), new Vector3(6f, 1.5f, 5f)),
        };
        var spawns = new[]
        {
            new Spawn(new Vector3(-6f, 0.85f, -5f), 45f),
            new Spawn(new Vector3(6f, 0.85f, 5f), 225f),
            new Spawn(new Vector3(-6f, 0.85f, 5f), 135f),
            new Spawn(new Vector3(6f, 0.85f, -5f), 315f),
        };

        var deck = Mat(key, "Deck", new Color(0.72f, 0.65f, 0.53f), 0.18f);
        var aqua = Mat(key, "Pool", new Color(0.18f, 0.55f, 0.62f), 0.30f, new Color(0.005f, 0.045f, 0.055f));
        var coral = Mat(key, "Coral", new Color(0.72f, 0.38f, 0.34f), 0.12f);
        var yellow = Mat(key, "Yellow", new Color(0.76f, 0.66f, 0.31f), 0.10f);
        var cream = Mat(key, "Cream", new Color(0.82f, 0.80f, 0.72f), 0.08f);
        var blue = Mat(key, "Blue", new Color(0.27f, 0.42f, 0.52f), 0.20f);

        var root = NewRoot("Splash Deck");
        root.AddComponent<ArenaAtmosphere>().Configure(
            new Color(0.43f, 0.79f, 0.96f),
            new Color(0.72f, 0.86f, 0.94f),
            new Color(0.34f, 0.43f, 0.47f),
            1.15f);
        AddCollisionVisuals(root, boxes, deck, new Dictionary<string, Material>
        {
            ["West Diving Board"] = coral,
            ["East Diving Board"] = coral,
        });
        AddRectangleTrim(root, 30f, 22f, yellow);

        Cube(root, "North Pool", new Vector3(0f, -0.45f, 16f), new Vector3(30f, 0.18f, 7f), aqua);
        Cube(root, "South Pool", new Vector3(0f, -0.45f, -16f), new Vector3(30f, 0.18f, 7f), aqua);
        Cube(root, "North Pool Rim", new Vector3(0f, -0.18f, 12.4f), new Vector3(30f, 0.45f, 0.35f), cream);
        Cube(root, "South Pool Rim", new Vector3(0f, -0.18f, -12.4f), new Vector3(30f, 0.45f, 0.35f), cream);

        Umbrella(root, new Vector3(-19f, 0f, 14f), coral, cream);
        Umbrella(root, new Vector3(19f, 0f, -14f), yellow, blue);
        Umbrella(root, new Vector3(19f, 0f, 14f), blue, cream);
        DeckChair(root, new Vector3(-18f, 0f, -14f), coral, cream, 18f);
        DeckChair(root, new Vector3(18f, 0f, 14f), yellow, cream, -20f);
        FloatStack(root, new Vector3(-7f, -0.1f, 16f), coral, yellow, blue);
        FloatStack(root, new Vector3(9f, -0.1f, -16f), blue, coral, yellow);
        AddSpawnMarkers(root, spawns);

        SaveAndBake(key, "Splash Deck", "#36bfd1", root, boxes, spawns, -12f);
    }

    private static void GenerateAfterHours()
    {
        const string key = "after_hours";
        var boxes = new[]
        {
            new Box("East West Floor", new Vector3(0f, -0.25f, 0f), new Vector3(32f, 0.5f, 14f)),
            new Box("North South Floor", new Vector3(0f, -0.25f, 0f), new Vector3(14f, 0.5f, 26f)),
            new Box("West Stage", new Vector3(-10.5f, 0.9f, 0f), new Vector3(6f, 1.8f, 6f)),
            new Box("East Stage", new Vector3(10.5f, 0.9f, 0f), new Vector3(6f, 1.8f, 6f)),
        };
        var spawns = new[]
        {
            new Spawn(new Vector3(0f, 0.85f, -5f), 0f),
            new Spawn(new Vector3(0f, 0.85f, 5f), 180f),
            new Spawn(new Vector3(-5f, 0.85f, 0f), 90f),
            new Spawn(new Vector3(5f, 0.85f, 0f), 270f),
        };

        var floor = Mat(key, "Floor", new Color(0.12f, 0.17f, 0.25f), 0.28f);
        var magenta = Mat(key, "Magenta", new Color(0.58f, 0.20f, 0.36f), 0.15f, new Color(0.10f, 0.005f, 0.025f));
        var cyan = Mat(key, "Cyan", new Color(0.22f, 0.55f, 0.57f), 0.14f, new Color(0.005f, 0.09f, 0.10f));
        var orange = Mat(key, "Orange", new Color(0.72f, 0.40f, 0.20f), 0.12f, new Color(0.12f, 0.025f, 0.005f));
        var dark = Mat(key, "Skyline", new Color(0.035f, 0.04f, 0.08f), 0.30f);
        var stall = Mat(key, "Stall", new Color(0.18f, 0.11f, 0.23f), 0.20f);

        var root = NewRoot("After Hours");
        root.AddComponent<ArenaAtmosphere>().Configure(
            new Color(0.055f, 0.035f, 0.14f),
            new Color(0.12f, 0.10f, 0.25f),
            new Color(0.025f, 0.02f, 0.07f),
            0.68f,
            true,
            new Color(0.055f, 0.035f, 0.14f),
            30f,
            85f);
        AddCollisionVisuals(root, boxes, floor, new Dictionary<string, Material>
        {
            ["West Stage"] = cyan,
            ["East Stage"] = orange,
        });
        AddCrossTrim(root, magenta);
        FestivalStall(root, new Vector3(-19f, 0f, 12f), stall, cyan);
        FestivalStall(root, new Vector3(19f, 0f, 12f), stall, orange);
        FestivalStall(root, new Vector3(-19f, 0f, -12f), stall, orange);
        FestivalStall(root, new Vector3(19f, 0f, -12f), stall, cyan);
        Skyline(root, dark, cyan, orange);
        LightStrings(root, cyan, orange);
        AddSpawnMarkers(root, spawns);
        AddPointLight(root, "Warm Festival Light", new Vector3(-8f, 7f, -5f), new Color(1f, 0.35f, 0.12f), 4.5f, 16f);
        AddPointLight(root, "Cool Festival Light", new Vector3(8f, 7f, 5f), new Color(0.10f, 0.65f, 1f), 3.5f, 16f);

        SaveAndBake(key, "After Hours", "#cf2877", root, boxes, spawns, -12f);
    }

    private static void GenerateRecCenterRoof()
    {
        const string key = "rec_center_roof";
        var boxes = new[]
        {
            new Box("Main Roof", new Vector3(0f, -0.25f, 0f), new Vector3(22f, 0.5f, 18f)),
            new Box("West Lane", new Vector3(-12.5f, -0.25f, 0f), new Vector3(3f, 0.5f, 12f)),
            new Box("East Lane", new Vector3(12.5f, -0.25f, 0f), new Vector3(3f, 0.5f, 12f)),
            new Box("Center Strip", new Vector3(0f, 0.55f, 0f), new Vector3(14f, 1.1f, 4f)),
        };
        var spawns = new[]
        {
            new Spawn(new Vector3(-7f, 0.85f, -5f), 55f),
            new Spawn(new Vector3(7f, 0.85f, 5f), 235f),
            new Spawn(new Vector3(-7f, 0.85f, 5f), 125f),
            new Spawn(new Vector3(7f, 0.85f, -5f), 305f),
        };

        var concrete = Mat(key, "Concrete", new Color(0.52f, 0.53f, 0.49f), 0.18f);
        var sage = Mat(key, "Sage", new Color(0.35f, 0.48f, 0.42f), 0.16f);
        var brick = Mat(key, "Brick", new Color(0.53f, 0.31f, 0.27f), 0.14f);
        var cream = Mat(key, "Cream", new Color(0.79f, 0.76f, 0.65f), 0.10f);
        var dark = Mat(key, "Structure", new Color(0.16f, 0.22f, 0.23f), 0.24f);

        var root = NewRoot("Rec Center Roof");
        root.AddComponent<ArenaAtmosphere>().Configure(
            new Color(0.54f, 0.70f, 0.78f),
            new Color(0.62f, 0.70f, 0.72f),
            new Color(0.27f, 0.30f, 0.29f),
            0.95f);
        AddCollisionVisuals(root, boxes, concrete, new Dictionary<string, Material>
        {
            ["Center Strip"] = sage,
        });
        AddRectangleTrim(root, 22f, 18f, cream);
        Cube(root, "West Lane Edge", new Vector3(-14.1f, 0.03f, 0f), new Vector3(0.35f, 0.5f, 12f), cream);
        Cube(root, "East Lane Edge", new Vector3(14.1f, 0.03f, 0f), new Vector3(0.35f, 0.5f, 12f), cream);
        Cube(root, "Center Line", new Vector3(0f, 0.04f, 0f), new Vector3(0.16f, 0.05f, 16f), brick);
        Cube(root, "Center Strip Mark", new Vector3(0f, 1.13f, 0f), new Vector3(11f, 0.04f, 0.15f), cream);

        for (int side = -1; side <= 1; side += 2)
        {
            Cylinder(root, "Water Tank", new Vector3(side * 18f, 2.5f, 11f), new Vector3(2.6f, 2.5f, 2.6f), dark);
            Cube(root, "Vent", new Vector3(side * 18f, 1f, -10f), new Vector3(3.2f, 2f, 2.6f), sage);
            Cube(root, "Vent Cap", new Vector3(side * 18f, 2.1f, -10f), new Vector3(3.6f, 0.25f, 3f), cream);
        }
        Cube(root, "Laundry Rail", new Vector3(0f, 4.5f, 14f), new Vector3(24f, 0.12f, 0.12f), dark);
        for (int i = -4; i <= 4; i++)
            Cube(root, "Laundry Flag", new Vector3(i * 2.5f, 3.8f, 14f), new Vector3(1.5f, 1.2f, 0.08f), i % 2 == 0 ? brick : cream);
        AddSpawnMarkers(root, spawns);

        SaveAndBake(key, "Rec Center Roof", "#687a70", root, boxes, spawns, -12f);
    }

    private static void GeneratePicnicPanic()
    {
        const string key = "picnic_panic";
        var boxes = new[]
        {
            new Box("Picnic Lawn", new Vector3(0f, -0.25f, 0f), new Vector3(22f, 0.5f, 24f)),
            new Box("West Lawn", new Vector3(-13f, -0.25f, 0f), new Vector3(4f, 0.5f, 16f)),
            new Box("East Lawn", new Vector3(13f, -0.25f, 0f), new Vector3(4f, 0.5f, 16f)),
            new Box("Center Blanket", new Vector3(0f, 0.5f, 0f), new Vector3(8f, 1f, 6f)),
            new Box("Northwest Table", new Vector3(-8f, 0.9f, 6f), new Vector3(5f, 1.8f, 5f)),
            new Box("Southeast Table", new Vector3(8f, 0.9f, -6f), new Vector3(5f, 1.8f, 5f)),
        };
        var spawns = new[]
        {
            new Spawn(new Vector3(-6f, 0.85f, -5f), 45f),
            new Spawn(new Vector3(6f, 0.85f, 5f), 225f),
            new Spawn(new Vector3(-6f, 0.85f, 5f), 135f),
            new Spawn(new Vector3(6f, 0.85f, -5f), 315f),
        };

        var grass = Mat(key, "Grass", new Color(0.38f, 0.50f, 0.34f), 0.12f);
        var canvas = Mat(key, "Canvas", new Color(0.80f, 0.74f, 0.59f), 0.08f);
        var orange = Mat(key, "Orange", new Color(0.68f, 0.40f, 0.24f), 0.10f);
        var wood = Mat(key, "Wood", new Color(0.38f, 0.25f, 0.18f), 0.16f);
        var leaf = Mat(key, "Leaves", new Color(0.25f, 0.39f, 0.24f), 0.08f);
        var red = Mat(key, "Faded Red", new Color(0.58f, 0.29f, 0.27f), 0.10f);

        var root = NewRoot("Picnic Panic");
        root.AddComponent<ArenaAtmosphere>().Configure(
            new Color(0.58f, 0.74f, 0.82f),
            new Color(0.68f, 0.76f, 0.75f),
            new Color(0.28f, 0.35f, 0.27f),
            1f);
        AddCollisionVisuals(root, boxes, grass, new Dictionary<string, Material>
        {
            ["Center Blanket"] = canvas,
            ["Northwest Table"] = orange,
            ["Southeast Table"] = orange,
        });
        Cube(root, "North Edge", new Vector3(0f, 0.03f, 12.1f), new Vector3(22f, 0.5f, 0.35f), canvas);
        Cube(root, "South Edge", new Vector3(0f, 0.03f, -12.1f), new Vector3(22f, 0.5f, 0.35f), canvas);
        Cube(root, "West Edge", new Vector3(-15.1f, 0.03f, 0f), new Vector3(0.35f, 0.5f, 16f), canvas);
        Cube(root, "East Edge", new Vector3(15.1f, 0.03f, 0f), new Vector3(0.35f, 0.5f, 16f), canvas);

        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
        {
            var center = new Vector3(x * 20f, 0f, z * 14f);
            Cylinder(root, "Tree Trunk", center + Vector3.up * 2.4f, new Vector3(0.75f, 2.4f, 0.75f), wood);
            Sphere(root, "Tree Crown", center + Vector3.up * 6f, new Vector3(5f, 3.8f, 5f), leaf);
        }
        Pavilion(root, new Vector3(-21f, 0f, 0f), wood, canvas, red);
        Pavilion(root, new Vector3(21f, 0f, 0f), wood, canvas, orange);
        for (int i = -5; i <= 5; i++)
            Cube(root, "Bunting", new Vector3(i * 3f, 5f - Mathf.Abs(i) * 0.12f, 16f), new Vector3(1.2f, 0.8f, 0.08f), i % 2 == 0 ? orange : red);
        AddSpawnMarkers(root, spawns);

        SaveAndBake(key, "Picnic Panic", "#657f57", root, boxes, spawns, -12f);
    }


    private static GameObject NewRoot(string name)
    {
        var root = new GameObject(name);
        root.transform.position = Vector3.zero;
        return root;
    }

    private static void AddCollisionVisuals(GameObject root, Box[] boxes, Material fallback, Dictionary<string, Material> overrides = null)
    {
        var parent = Child(root, "Playable Geometry");
        foreach (var box in boxes)
        {
            var material = overrides != null && overrides.TryGetValue(box.Name, out var specific) ? specific : fallback;
            Cube(parent, box.Name, box.Center, box.Size, material);
        }
    }

    private static void AddChamferedCourtTrim(GameObject root, Material material)
    {
        Cube(root, "North Trim", new Vector3(0f, 0.03f, 11.1f), new Vector3(26f, 0.5f, 0.35f), material);
        Cube(root, "South Trim", new Vector3(0f, 0.03f, -11.1f), new Vector3(26f, 0.5f, 0.35f), material);
        Cube(root, "West Outer Trim", new Vector3(-17.1f, 0.03f, 0f), new Vector3(0.35f, 0.5f, 14f), material);
        Cube(root, "East Outer Trim", new Vector3(17.1f, 0.03f, 0f), new Vector3(0.35f, 0.5f, 14f), material);
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
            Cube(root, "Corner Trim", new Vector3(x * 14.95f, 0.03f, z * 9.05f), new Vector3(5.7f, 0.5f, 0.35f), material, new Vector3(0f, x * z * 45f, 0f));
    }

    private static void AddRectangleTrim(GameObject root, float width, float depth, Material material)
    {
        Cube(root, "North Trim", new Vector3(0f, 0.03f, depth * 0.5f), new Vector3(width + 0.5f, 0.5f, 0.35f), material);
        Cube(root, "South Trim", new Vector3(0f, 0.03f, -depth * 0.5f), new Vector3(width + 0.5f, 0.5f, 0.35f), material);
        Cube(root, "East Trim", new Vector3(width * 0.5f, 0.03f, 0f), new Vector3(0.35f, 0.5f, depth), material);
        Cube(root, "West Trim", new Vector3(-width * 0.5f, 0.03f, 0f), new Vector3(0.35f, 0.5f, depth), material);
    }

    private static void AddCrossTrim(GameObject root, Material material)
    {
        Cube(root, "North End", new Vector3(0f, 0.03f, 13.1f), new Vector3(14f, 0.55f, 0.4f), material);
        Cube(root, "South End", new Vector3(0f, 0.03f, -13.1f), new Vector3(14f, 0.55f, 0.4f), material);
        Cube(root, "East End", new Vector3(16.1f, 0.03f, 0f), new Vector3(0.4f, 0.55f, 14f), material);
        Cube(root, "West End", new Vector3(-16.1f, 0.03f, 0f), new Vector3(0.4f, 0.55f, 14f), material);
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
        {
            Cube(root, "Cross X Edge", new Vector3(x * 11.5f, 0.03f, z * 7.1f), new Vector3(9f, 0.55f, 0.4f), material);
            Cube(root, "Cross Z Edge", new Vector3(x * 7.1f, 0.03f, z * 10f), new Vector3(0.4f, 0.55f, 6f), material);
        }
    }

    private static void Pavilion(GameObject root, Vector3 center, Material structure, Material canopy, Material accent)
    {
        Cube(root, "Pavilion Base", center + Vector3.up * 0.55f, new Vector3(8f, 1.1f, 6f), structure);
        Cube(root, "Pavilion Canopy", center + Vector3.up * 5f, new Vector3(9f, 0.45f, 7f), canopy, new Vector3(0f, 0f, center.x > 0f ? -4f : 4f));
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
            Cube(root, "Pavilion Post", center + new Vector3(x * 3.2f, 2.8f, z * 2.2f), new Vector3(0.25f, 4.7f, 0.25f), accent);
    }

    private static void Scoreboard(GameObject root, Vector3 center, Material frame, Material face)
    {
        Cube(root, "Scoreboard Post", center + Vector3.down * 2.5f, new Vector3(0.35f, 5f, 0.35f), frame);
        Cube(root, "Scoreboard Frame", center, new Vector3(9f, 4f, 0.6f), frame);
        Cube(root, "Scoreboard Face", center + Vector3.back * 0.34f, new Vector3(7.7f, 2.7f, 0.08f), face);
    }

    private static void Banners(GameObject root, float radius, Material first, Material second)
    {
        for (int i = 0; i < 10; i++)
        {
            float angle = i * Mathf.PI * 2f / 10f;
            var position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * 13.5f);
            Cube(root, "Banner Pole", position + Vector3.up * 2f, new Vector3(0.12f, 4f, 0.12f), second);
            Cube(root, "Banner", position + new Vector3(0f, 3.2f, 0f), new Vector3(1.2f, 1.1f, 0.1f), i % 2 == 0 ? first : second, new Vector3(0f, -angle * Mathf.Rad2Deg, 0f));
        }
    }

    private static void Umbrella(GameObject root, Vector3 center, Material canopy, Material pole)
    {
        Cylinder(root, "Umbrella Pole", center + Vector3.up * 2.2f, new Vector3(0.12f, 2.2f, 0.12f), pole);
        Sphere(root, "Umbrella Canopy", center + Vector3.up * 4.3f, new Vector3(4.2f, 0.65f, 4.2f), canopy);
    }

    private static void DeckChair(GameObject root, Vector3 center, Material fabric, Material frame, float yaw)
    {
        Cube(root, "Chair Seat", center + Vector3.up * 0.7f, new Vector3(2.4f, 0.25f, 3.2f), fabric, new Vector3(0f, yaw, -7f));
        Cube(root, "Chair Back", center + new Vector3(0f, 2f, 1.3f), new Vector3(2.4f, 3f, 0.25f), fabric, new Vector3(-18f, yaw, 0f));
        Cube(root, "Chair Frame", center + Vector3.up * 0.25f, new Vector3(2.7f, 0.2f, 3.5f), frame, new Vector3(0f, yaw, 0f));
    }

    private static void FloatStack(GameObject root, Vector3 center, Material first, Material second, Material third)
    {
        Cylinder(root, "Float A", center + Vector3.up * 0.15f, new Vector3(2.1f, 0.18f, 2.1f), first);
        Cylinder(root, "Float B", center + new Vector3(1.4f, 0.22f, 0.6f), new Vector3(1.6f, 0.15f, 1.6f), second, new Vector3(12f, 0f, 18f));
        Sphere(root, "Beach Ball", center + new Vector3(-1.6f, 0.9f, -0.5f), Vector3.one * 1.4f, third);
    }

    private static void FestivalStall(GameObject root, Vector3 center, Material body, Material accent)
    {
        Cube(root, "Stall Counter", center + Vector3.up * 1.1f, new Vector3(6f, 2.2f, 4f), body);
        Cube(root, "Stall Roof", center + Vector3.up * 4.2f, new Vector3(7f, 0.5f, 5f), accent, new Vector3(0f, 0f, center.x > 0f ? 4f : -4f));
        Cube(root, "Stall Sign", center + new Vector3(0f, 5.3f, -2.5f), new Vector3(4f, 1f, 0.2f), accent);
    }

    private static void Skyline(GameObject root, Material building, Material firstLight, Material secondLight)
    {
        for (int i = -5; i <= 5; i++)
        {
            float height = 6f + Mathf.Abs((i * 7 + 3) % 9);
            var center = new Vector3(i * 7f, height * 0.5f - 2f, 32f);
            Cube(root, "Skyline Building", center, new Vector3(5.5f, height, 5f), building);
            Cube(root, "Skyline Window", center + new Vector3(0f, 0f, -2.55f), new Vector3(0.5f, 0.5f, 0.08f), i % 2 == 0 ? firstLight : secondLight);
        }
    }

    private static void LightStrings(GameObject root, Material first, Material second)
    {
        foreach (int z in new[] { -1, 1 })
        {
            for (int i = -6; i <= 6; i++)
            {
                float sag = Mathf.Abs(i) * 0.10f;
                Sphere(root, "Festival Bulb", new Vector3(i * 3f, 5.2f - sag, z * 16f), Vector3.one * 0.35f, i % 2 == 0 ? first : second);
            }
        }
    }

    private static void AddPointLight(GameObject root, string name, Vector3 position, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
    }

    private static Spawn[] CardinalSpawns(float radius, float y)
    {
        return new[]
        {
            new Spawn(new Vector3(-radius, y, 0f), 90f),
            new Spawn(new Vector3(radius, y, 0f), 270f),
            new Spawn(new Vector3(0f, y, radius), 180f),
            new Spawn(new Vector3(0f, y, -radius), 0f),
        };
    }

    private static void AddSpawnMarkers(GameObject root, Spawn[] spawns)
    {
        var parent = Child(root, "Spawn Points");
        for (int i = 0; i < spawns.Length; i++)
        {
            var marker = new GameObject($"spawn_p{i + 1}");
            marker.tag = "SpawnPoint";
            marker.transform.SetParent(parent.transform, false);
            marker.transform.localPosition = spawns[i].Position;
            marker.transform.localEulerAngles = new Vector3(0f, spawns[i].Yaw, 0f);
        }
    }

    private static void SaveAndBake(string key, string displayName, string previewColor, GameObject visualRoot, Box[] boxes, Spawn[] spawns, float killHeight)
    {
        string prefabPath = $"{PrefabRoot}/{key}.prefab";
        PrefabUtility.SaveAsPrefabAsset(visualRoot, prefabPath);
        UnityEngine.Object.DestroyImmediate(visualRoot);

        var collisionRoot = new GameObject($"{displayName} Bake Source");
        foreach (var box in boxes)
            RawCube(collisionRoot, box.Name, box.Center, box.Size);
        AddSpawnMarkers(collisionRoot, spawns);

        var baker = ScriptableObject.CreateInstance<SlopArenaArenaBaker>();
        SetField(baker, "_arenaRoot", collisionRoot);
        SetField(baker, "_arenaName", key);
        SetField(baker, "_displayName", displayName);
        SetField(baker, "_previewColor", previewColor);
        SetField(baker, "_killHeight", killHeight);
        SetField(baker, "_autoBounds", true);
        SetField(baker, "_autoBlastLines", true);
        SetField(baker, "_outputDir", "../../data/arenas");

        SlopArenaArenaBaker.SuppressDialogs = true;
        typeof(SlopArenaArenaBaker).GetMethod("BakeArena", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(baker, null);
        SlopArenaArenaBaker.SuppressDialogs = false;

        UnityEngine.Object.DestroyImmediate(collisionRoot);
        UnityEngine.Object.DestroyImmediate(baker);
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static Material Mat(string arena, string name, Color color, float smoothness, Color? emission = null)
    {
        string folder = $"{MaterialRoot}/{arena}";
        EnsureFolder(MaterialRoot, arena);
        string path = $"{folder}/{name.Replace(' ', '_')}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            AssetDatabase.DeleteAsset(path);

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"{arena}_{name}" };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static GameObject Child(GameObject root, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        return child;
    }

    private static GameObject Cube(GameObject root, string name, Vector3 position, Vector3 scale, Material material, Vector3? rotation = null)
    {
        return Primitive(root, PrimitiveType.Cube, name, position, scale, material, rotation ?? Vector3.zero);
    }

    private static GameObject Sphere(GameObject root, string name, Vector3 position, Vector3 scale, Material material)
    {
        return Primitive(root, PrimitiveType.Sphere, name, position, scale, material, Vector3.zero);
    }

    private static GameObject Cylinder(GameObject root, string name, Vector3 position, Vector3 scale, Material material, Vector3? rotation = null)
    {
        return Primitive(root, PrimitiveType.Cylinder, name, position, scale, material, rotation ?? Vector3.zero);
    }

    private static GameObject Primitive(GameObject root, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, Vector3 rotation)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        go.transform.localEulerAngles = rotation;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        var collider = go.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.DestroyImmediate(collider);
        return go;
    }

    private static GameObject RawCube(GameObject root, string name, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        return go;
    }
}
