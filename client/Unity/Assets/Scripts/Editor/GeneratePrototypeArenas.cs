using System;
using System.Collections.Generic;
using System.Reflection;
using SlopArena.Client.World;

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// THROWAWAY LOOK-DEV GENERATOR. Builds the remaining box-prototype arena prefabs and bakes
/// their collision data. It skips any stage with an authoring scene under Assets/Stages:
/// that scene is the gameplay authority (sloparena.stage.bake) and its art has its own source.
/// Delete this tool after the layouts are accepted.
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

    [MenuItem("Tools/SLOPArena/Prototypes/Generate Arenas")]
    public static void GenerateAll()
    {
        EnsureFolder("Assets/Art/Stages", "GeneratedPrototypes");

        GenerateRecCenterRoof();
        GeneratePicnicPanic();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PrototypeArenas] Generated and baked the prototype arenas without authoring scenes.");
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

    private static void AddRectangleTrim(GameObject root, float width, float depth, Material material)
    {
        Cube(root, "North Trim", new Vector3(0f, 0.03f, depth * 0.5f), new Vector3(width + 0.5f, 0.5f, 0.35f), material);
        Cube(root, "South Trim", new Vector3(0f, 0.03f, -depth * 0.5f), new Vector3(width + 0.5f, 0.5f, 0.35f), material);
        Cube(root, "East Trim", new Vector3(width * 0.5f, 0.03f, 0f), new Vector3(0.35f, 0.5f, depth), material);
        Cube(root, "West Trim", new Vector3(-width * 0.5f, 0.03f, 0f), new Vector3(0.35f, 0.5f, depth), material);
    }

    private static void Pavilion(GameObject root, Vector3 center, Material structure, Material canopy, Material accent)
    {
        Cube(root, "Pavilion Base", center + Vector3.up * 0.55f, new Vector3(8f, 1.1f, 6f), structure);
        Cube(root, "Pavilion Canopy", center + Vector3.up * 5f, new Vector3(9f, 0.45f, 7f), canopy, new Vector3(0f, 0f, center.x > 0f ? -4f : 4f));
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
            Cube(root, "Pavilion Post", center + new Vector3(x * 3.2f, 2.8f, z * 2.2f), new Vector3(0.25f, 4.7f, 0.25f), accent);
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
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>($"Assets/Stages/{key}/{key}.unity") != null)
        {
            UnityEngine.Object.DestroyImmediate(visualRoot);
            Debug.LogWarning($"[PrototypeArenas] Skipped {key}: its authoring scene is the gameplay authority; bake it with sloparena.stage.bake.");
            return;
        }
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
