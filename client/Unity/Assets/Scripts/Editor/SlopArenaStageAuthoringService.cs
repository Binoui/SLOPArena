using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class SlopArenaStageDiagnostic
{
    [JsonProperty("code")] public string Code { get; set; }
    [JsonProperty("message")] public string Message { get; set; }
}

public sealed class SlopArenaStageBakeResult
{
    [JsonProperty("success")] public bool Success { get; set; }
    [JsonProperty("stage")] public string Stage { get; set; }
    [JsonProperty("sourceScene")] public string SourceScene { get; set; }
    [JsonProperty("arenaPath")] public string ArenaPath { get; set; }
    [JsonProperty("triangleCount")] public int TriangleCount { get; set; }
    [JsonProperty("spawnCount")] public int SpawnCount { get; set; }
    [JsonProperty("heightmapWidth")] public int HeightmapWidth { get; set; }
    [JsonProperty("heightmapHeight")] public int HeightmapHeight { get; set; }
    [JsonProperty("diagnostics")] public List<SlopArenaStageDiagnostic> Diagnostics { get; set; } = new List<SlopArenaStageDiagnostic>();
}

public sealed class SlopArenaStageInspectionResult
{
    [JsonProperty("success")] public bool Success { get; set; }
    [JsonProperty("stage")] public string Stage { get; set; }
    [JsonProperty("sourceScene")] public string SourceScene { get; set; }
    [JsonProperty("arenaPath")] public string ArenaPath { get; set; }
    [JsonProperty("prefabPath")] public string PrefabPath { get; set; }
    [JsonProperty("outputPath")] public string OutputPath { get; set; }
    [JsonProperty("sourceArenaHash")] public string SourceArenaHash { get; set; }
    [JsonProperty("bakedArenaHash")] public string BakedArenaHash { get; set; }
    [JsonProperty("collisionTriangleCount")] public int CollisionTriangleCount { get; set; }
    [JsonProperty("spawnCount")] public int SpawnCount { get; set; }
    [JsonProperty("cosmeticMetrics")] public SlopArenaStageCosmeticMetrics CosmeticMetrics { get; set; }
    [JsonProperty("captures")] public List<string> Captures { get; set; } = new List<string>();
    [JsonProperty("diagnostics")] public List<SlopArenaStageDiagnostic> Diagnostics { get; set; } = new List<SlopArenaStageDiagnostic>();
}

public sealed class SlopArenaStageDesignCaptureResult
{
    [JsonProperty("success")] public bool Success { get; set; }
    [JsonProperty("stage")] public string Stage { get; set; }
    [JsonProperty("prefabPath")] public string PrefabPath { get; set; }
    [JsonProperty("outputDirectory")] public string OutputDirectory { get; set; }
    [JsonProperty("killPlanes")] public SlopArenaStageKillPlanes KillPlanes { get; set; }
    [JsonProperty("captures")] public List<string> Captures { get; set; } = new List<string>();
    [JsonProperty("diagnostics")] public List<SlopArenaStageDiagnostic> Diagnostics { get; set; } = new List<SlopArenaStageDiagnostic>();
}

public sealed class SlopArenaStageKillPlanes
{
    [JsonProperty("minX")] public float MinX { get; set; }
    [JsonProperty("maxX")] public float MaxX { get; set; }
    [JsonProperty("minZ")] public float MinZ { get; set; }
    [JsonProperty("maxZ")] public float MaxZ { get; set; }
    [JsonProperty("killHeight")] public float KillHeight { get; set; }
    [JsonProperty("killTop")] public float KillTop { get; set; }
}

public sealed class SlopArenaStageCosmeticMetrics
{
    [JsonProperty("rendererCount")] public int RendererCount { get; set; }
    [JsonProperty("triangleCount")] public int TriangleCount { get; set; }
    [JsonProperty("colliderCount")] public int ColliderCount { get; set; }
    [JsonProperty("materialSlotCount")] public int MaterialSlotCount { get; set; }
    [JsonProperty("uniqueMaterialCount")] public int UniqueMaterialCount { get; set; }
    [JsonProperty("supportedShaders")] public List<string> SupportedShaders { get; set; } = new List<string>();
    [JsonProperty("unsupportedShaders")] public List<string> UnsupportedShaders { get; set; } = new List<string>();
    [JsonProperty("missingShaderCount")] public int MissingShaderCount { get; set; }
    [JsonProperty("missingMeshReferenceCount")] public int MissingMeshReferenceCount { get; set; }
    [JsonProperty("missingMaterialReferenceCount")] public int MissingMaterialReferenceCount { get; set; }
    [JsonProperty("localLightCount")] public int LocalLightCount { get; set; }
    [JsonProperty("boundsCenter")] public SlopArenaStageVector3 BoundsCenter { get; set; }
    [JsonProperty("boundsDimensions")] public SlopArenaStageVector3 BoundsDimensions { get; set; }
}

public sealed class SlopArenaStageVector3
{
    [JsonProperty("x")] public float X { get; set; }
    [JsonProperty("y")] public float Y { get; set; }
    [JsonProperty("z")] public float Z { get; set; }

    public SlopArenaStageVector3() { }
    public SlopArenaStageVector3(Vector3 value) { X = value.x; Y = value.y; Z = value.z; }
}

public sealed class SlopArenaStageAuthoringService
{
    private const float HeightmapCellSize = 0.5f;
    private const float SpawnBodyHalfHeight = 0.85f;
    private const float SpawnGroundTolerance = 0.11f;
    private const int CaptureWidth = 512;
    private const int CaptureHeight = 384;
    private static readonly Color CaptureBackground = new Color(0.035f, 0.055f, 0.09f, 1f);
    private static readonly Color CollisionOverlay = new Color(1f, 0.78f, 0.1f, 0.24f);
    private static readonly Color SpawnOverlay = new Color(0.2f, 1f, 0.35f, 1f);

    private readonly string _projectRoot;
    private readonly string _repositoryRoot;
    private readonly string _stageCacheRoot;

    private sealed class StagePaths
    {
        public string Key;
        public string SourceAssetPath;
        public string SourceFullPath;
        public string ArenaFullPath;
        public string PrefabAssetPath;
        public string PrefabFullPath;
    }

    private sealed class StageBuild
    {
        public ArenaDefinition Arena;
        public bool HasArena;
        public readonly List<SlopArenaStageDiagnostic> Diagnostics = new List<SlopArenaStageDiagnostic>();
    }

    private enum CaptureView
    {
        Top,
        Front,
        Back,
        Left,
        Right,
        Isometric
    }

    public SlopArenaStageAuthoringService(string projectRoot)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        DirectoryInfo repository = Directory.GetParent(_projectRoot)?.Parent;
        _repositoryRoot = repository?.FullName ?? _projectRoot;
        _stageCacheRoot = Path.Combine(_repositoryRoot, ".stage-authoring-cache");
    }

    public SlopArenaStageBakeResult Bake(string stage)
    {
        var result = new SlopArenaStageBakeResult { Stage = stage };
        if (!TryBuildPaths(stage, result.Diagnostics, out StagePaths paths)) return result;

        result.SourceScene = paths.SourceAssetPath;
        result.ArenaPath = RelativeToRepository(paths.ArenaFullPath);
        if (!File.Exists(paths.SourceFullPath))
        {
            Add(result.Diagnostics, "SOURCE_SCENE_MISSING", paths.SourceAssetPath);
            return result;
        }

        Scene scene = default;
        try
        {
            scene = EditorSceneManager.OpenScene(paths.SourceAssetPath, OpenSceneMode.Additive);
            StageBuild build = BuildFromScene(scene, paths.Key);
            result.Diagnostics.AddRange(build.Diagnostics);
            if (!build.HasArena) return result;

            ArenaBinaryFormat.SaveToFile(paths.ArenaFullPath, build.Arena);
            result.Success = true;
            result.TriangleCount = build.Arena.CollisionTriangles?.Length ?? 0;
            result.SpawnCount = build.Arena.SpawnPoints?.Length ?? 0;
            result.HeightmapWidth = build.Arena.Heightmap.Width;
            result.HeightmapHeight = build.Arena.Heightmap.Height;
            AssetDatabase.Refresh();
            return result;
        }
        catch (Exception ex)
        {
            Add(result.Diagnostics, "BAKE_EXCEPTION", ex.Message);
            return result;
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    public SlopArenaStageInspectionResult Inspect(string stage, string output)
    {
        var result = new SlopArenaStageInspectionResult { Stage = stage };
        if (!TryBuildPaths(stage, result.Diagnostics, out StagePaths paths)) return result;
        if (!TryResolveOutput(output, result.Diagnostics, out string outputPath)) return result;

        result.SourceScene = paths.SourceAssetPath;
        result.ArenaPath = RelativeToRepository(paths.ArenaFullPath);
        result.PrefabPath = paths.PrefabAssetPath;
        result.OutputPath = RelativeToRepository(outputPath);
        string outputDirectory = Path.GetDirectoryName(outputPath);
        Directory.CreateDirectory(outputDirectory);
        CleanCaptureArtifacts(outputDirectory);

        ArenaDefinition? expectedArena = null;
        Scene scene = default;
        try
        {
            if (!File.Exists(paths.SourceFullPath))
                Add(result.Diagnostics, "SOURCE_SCENE_MISSING", paths.SourceAssetPath);
            else
            {
                scene = EditorSceneManager.OpenScene(paths.SourceAssetPath, OpenSceneMode.Additive);
                StageBuild build = BuildFromScene(scene, paths.Key);
                result.Diagnostics.AddRange(build.Diagnostics);
                if (build.HasArena) expectedArena = build.Arena;
            }
        }
        catch (Exception ex)
        {
            Add(result.Diagnostics, "SOURCE_INSPECTION_EXCEPTION", ex.Message);
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }

        ArenaDefinition? bakedArena = null;
        if (!File.Exists(paths.ArenaFullPath))
            Add(result.Diagnostics, "ARENA_MISSING", result.ArenaPath);
        else
        {
            bakedArena = ArenaBinaryFormat.LoadFromFile(paths.ArenaFullPath);
            if (!bakedArena.HasValue)
                Add(result.Diagnostics, "ARENA_UNREADABLE", result.ArenaPath);
        }

        if (expectedArena.HasValue)
        {
            result.SourceArenaHash = HashArena(expectedArena.Value);
            if (bakedArena.HasValue)
            {
                result.BakedArenaHash = HashFile(paths.ArenaFullPath);
                if (!ByteArraysEqual(ArenaBinaryFormat.Serialize(expectedArena.Value), File.ReadAllBytes(paths.ArenaFullPath)))
                    Add(result.Diagnostics, "ARENA_STALE_OR_MISMATCHED", "Baked arena does not match the current collision source.");
            }
        }

        if (bakedArena.HasValue)
        {
            ArenaDefinition arena = bakedArena.Value;
            result.CollisionTriangleCount = arena.CollisionTriangles?.Length ?? 0;
            result.SpawnCount = arena.SpawnPoints?.Length ?? 0;
            if (result.CollisionTriangleCount == 0) Add(result.Diagnostics, "ARENA_NO_COLLISION_TRIANGLES", result.ArenaPath);
            if (arena.Purpose == ArenaPurpose.Arcade && (arena.SpawnPoints == null || arena.SpawnPoints.Length != 1))
                Add(result.Diagnostics, "ARCADE_SPAWN_COUNT", "Arcade stages require one valid player start.");
            if (arena.Purpose == ArenaPurpose.PvP && (arena.SpawnPoints == null || arena.SpawnPoints.Length < 4))
                Add(result.Diagnostics, "ARENA_FEWER_THAN_FOUR_SPAWNS", "Selectable stages require four valid spawn points.");
        }

        InspectPrefab(paths, result, outputDirectory);
        if (result.CosmeticMetrics != null && result.Diagnostics.All(x => x.Code != "COSMETIC_COLLIDER"))
            RenderCaptures(paths, bakedArena, result, outputDirectory);

        result.Success = result.Diagnostics.Count == 0;
        File.WriteAllText(outputPath, JsonConvert.SerializeObject(result, Formatting.Indented));
        AssetDatabase.Refresh();
        return result;
    }

    private const int DesignCaptureWidth = 1280;
    private const int DesignCaptureHeight = 720;
    private static readonly Color DesignCaptureBackground = new Color(0.56f, 0.65f, 0.78f, 1f);
    private static readonly Color KillPlaneOverlay = new Color(1f, 0.25f, 0.2f, 0.16f);

    public SlopArenaStageDesignCaptureResult DesignCapture(string stage, string outputDirectory)
    {
        var result = new SlopArenaStageDesignCaptureResult { Stage = stage };
        if (!TryBuildPaths(stage, result.Diagnostics, out StagePaths paths)) return result;
        result.PrefabPath = paths.PrefabAssetPath;
        if (!File.Exists(paths.PrefabFullPath))
        {
            Add(result.Diagnostics, "PREFAB_MISSING", paths.PrefabAssetPath);
            return result;
        }
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Add(result.Diagnostics, "CAPTURE_GPU_UNAVAILABLE", "Design captures require a GPU-backed Unity Editor.");
            return result;
        }

        if (string.IsNullOrEmpty(outputDirectory))
            outputDirectory = Path.Combine(_stageCacheRoot, paths.Key, "design");
        string outDir = Path.GetFullPath(Path.IsPathRooted(outputDirectory)
            ? outputDirectory
            : Path.Combine(_repositoryRoot, outputDirectory));
        Directory.CreateDirectory(outDir);
        result.OutputDirectory = RelativeToRepository(outDir);

        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(paths.PrefabAssetPath);
        if (prefabAsset == null)
        {
            Add(result.Diagnostics, "PREFAB_UNLOADABLE", paths.PrefabAssetPath);
            return result;
        }
        GameObject stageObject = null;
        var cleanup = new List<UnityEngine.Object>();
        PreviewRenderUtility utility = null;
        try
        {
            stageObject = UnityEngine.Object.Instantiate(prefabAsset);
            stageObject.hideFlags = HideFlags.HideAndDontSave;
            Bounds prefabBounds = PrefabRenderBounds(stageObject);
            float killMinX = prefabBounds.min.x - ArenaCollision.SideBlastMargin;
            float killMaxX = prefabBounds.max.x + ArenaCollision.SideBlastMargin;
            float killMinZ = prefabBounds.min.z - ArenaCollision.SideBlastMargin;
            float killMaxZ = prefabBounds.max.z + ArenaCollision.SideBlastMargin;
            float killHeight = prefabBounds.min.y - 10f;
            float killTop = prefabBounds.max.y + ArenaCollision.TopBlastMargin;
            if (File.Exists(paths.ArenaFullPath))
            {
                ArenaDefinition arena = ArenaBinaryFormat.LoadFromFile(paths.ArenaFullPath).GetValueOrDefault();
                if (arena.MinX != 0f || arena.MaxX != 0f)
                {
                    ArenaCollision.BlastLines lines = ArenaCollision.ResolveBlastLines(in arena);
                    killMinX = lines.KillMinX;
                    killMaxX = lines.KillMaxX;
                    killMinZ = lines.KillMinZ;
                    killMaxZ = lines.KillMaxZ;
                    killHeight = lines.KillHeight;
                    killTop = lines.KillTop;
                }
            }
            else
            {
                Add(result.Diagnostics, "ARENA_MISSING", RelativeToRepository(paths.ArenaFullPath));
            }
            result.KillPlanes = new SlopArenaStageKillPlanes
            {
                MinX = killMinX, MaxX = killMaxX, MinZ = killMinZ, MaxZ = killMaxZ,
                KillHeight = killHeight,
                KillTop = killTop,
            };

            utility = new PreviewRenderUtility(true);
            utility.AddSingleGO(stageObject);
            var wallRoot = new GameObject("DesignOverlays") { hideFlags = HideFlags.HideAndDontSave };
            utility.AddSingleGO(wallRoot);
            Camera camera = utility.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = DesignCaptureBackground;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 2000f;

            RenderDesignTop(utility, outDir, result, "design-top.png", new Vector3(0f, prefabBounds.center.y, (killMinZ + killMaxZ) * 0.5f), killMinX, killMaxX, killMinZ, killMaxZ);

            const float wallHeight = 0.6f; // low curb marking the death line, not a veil
            float wallY = result.KillPlanes.KillHeight + wallHeight * 0.5f;
            CreateDesignWall(wallRoot, cleanup, new Vector3(killMinX, wallY, (killMinZ + killMaxZ) * 0.5f), new Vector3(0.15f, wallHeight, killMaxZ - killMinZ));
            CreateDesignWall(wallRoot, cleanup, new Vector3(killMaxX, wallY, (killMinZ + killMaxZ) * 0.5f), new Vector3(0.15f, wallHeight, killMaxZ - killMinZ));
            CreateDesignWall(wallRoot, cleanup, new Vector3((killMinX + killMaxX) * 0.5f, wallY, killMinZ), new Vector3(killMaxX - killMinX, wallHeight, 0.15f));
            CreateDesignWall(wallRoot, cleanup, new Vector3((killMinX + killMaxX) * 0.5f, wallY, killMaxZ), new Vector3(killMaxX - killMinX, wallHeight, 0.15f));

            float coreMinX = killMinX + ArenaCollision.SideBlastMargin;
            float coreMaxX = killMaxX - ArenaCollision.SideBlastMargin;
            float coreMinZ = killMinZ + ArenaCollision.SideBlastMargin;
            float coreMaxZ = killMaxZ - ArenaCollision.SideBlastMargin;
            Vector3 coreCenter = new Vector3((coreMinX + coreMaxX) * 0.5f, prefabBounds.center.y, (coreMinZ + coreMaxZ) * 0.5f);
            float coreRadius = 0.5f * Mathf.Max(coreMaxX - coreMinX, coreMaxZ - coreMinZ);

            RenderDesignYaw(utility, outDir, result, "design-north.png", coreCenter, coreRadius, 0f);
            RenderDesignYaw(utility, outDir, result, "design-east.png", coreCenter, coreRadius, 90f);
            RenderDesignYaw(utility, outDir, result, "design-south.png", coreCenter, coreRadius, 180f);
            RenderDesignYaw(utility, outDir, result, "design-west.png", coreCenter, coreRadius, 270f);

            result.Success = result.Diagnostics.Count == 0;
            AssetDatabase.Refresh();
            return result;
        }
        catch (Exception ex)
        {
            Add(result.Diagnostics, "DESIGN_CAPTURE_EXCEPTION", ex.Message);
            return result;
        }
        finally
        {
            if (utility != null) utility.Cleanup();
            foreach (UnityEngine.Object obj in cleanup) UnityEngine.Object.DestroyImmediate(obj);
            if (stageObject != null) UnityEngine.Object.DestroyImmediate(stageObject);
        }
    }

    private static Bounds PrefabRenderBounds(GameObject stage)
    {
        // Kill planes and framing derive from the FIGHTING SHELL only (children named
        // "Shell*"), never from cosmetic background bounds — background is allowed to
        // extend far beyond the fight area in every direction.
        Renderer[] renderers = stage.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (Renderer renderer in renderers)
        {
            if (!renderer.gameObject.name.StartsWith("Shell")) continue;
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!any)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    private static void CreateDesignWall(GameObject parent, List<UnityEngine.Object> cleanup, Vector3 center, Vector3 size)
    {
        Material material = CreatePreviewMaterial(KillPlaneOverlay);
        cleanup.Add(material);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(wall.GetComponent<Collider>());
        wall.hideFlags = HideFlags.HideAndDontSave;
        wall.transform.SetParent(parent.transform, false);
        wall.GetComponent<MeshRenderer>().sharedMaterial = material;
        wall.transform.localPosition = center;
        wall.transform.localScale = size;
        cleanup.Add(wall);
    }

    private void RenderDesignYaw(PreviewRenderUtility utility, string outDir, SlopArenaStageDesignCaptureResult result, string fileName, Vector3 center, float coreRadius, float yawDeg)
    {
        Camera camera = utility.camera;
        const float elevationDeg = 20f;
        float elevation = elevationDeg * Mathf.Deg2Rad;
        // Stay INSIDE the kill corridor so the near ring never sits between lens and stage.
        float distance = Mathf.Min(coreRadius * 0.95f, 26f);
        camera.fieldOfView = 50f;
        float yaw = yawDeg * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        Vector3 lookTarget = new Vector3(center.x, 2.5f, center.z);
        camera.transform.position = lookTarget - direction * (distance * Mathf.Cos(elevation)) + Vector3.up * (distance * Mathf.Sin(elevation));
        camera.transform.rotation = Quaternion.LookRotation((lookTarget - camera.transform.position).normalized, Vector3.up);
        string path = Path.Combine(outDir, fileName);
        WriteDesignShot(utility, path);
        result.Captures.Add(RelativeToRepository(path));
    }

    private void RenderDesignTop(PreviewRenderUtility utility, string outDir, SlopArenaStageDesignCaptureResult result, string fileName, Vector3 center, float killMinX, float killMaxX, float killMinZ, float killMaxZ)
    {
        Camera camera = utility.camera;
        camera.orthographic = true;
        camera.aspect = DesignCaptureWidth / (float)DesignCaptureHeight;
        camera.transform.position = center + Vector3.up * 150f;
        camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        float halfWidth = Mathf.Max(killMaxX - center.x, center.x - killMinX) * 1.12f;
        float halfDepth = Mathf.Max(killMaxZ - center.z, center.z - killMinZ) * 1.12f;
        camera.orthographicSize = Mathf.Max(6f, Mathf.Max(halfDepth, halfWidth / camera.aspect));
        string path = Path.Combine(outDir, fileName);
        WriteDesignShot(utility, path);
        result.Captures.Add(RelativeToRepository(path));
    }

    private static void WriteDesignShot(PreviewRenderUtility utility, string path)
    {
        utility.BeginPreview(new Rect(0, 0, DesignCaptureWidth, DesignCaptureHeight), GUIStyle.none);
        utility.camera.Render();
        Texture preview = utility.EndPreview();
        var image = new Texture2D(DesignCaptureWidth, DesignCaptureHeight, TextureFormat.RGBA32, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            if (preview is RenderTexture renderTexture)
            {
                RenderTexture.active = renderTexture;
                image.ReadPixels(new Rect(0, 0, DesignCaptureWidth, DesignCaptureHeight), 0, 0);
                image.Apply();
            }
            else if (preview is Texture2D texture)
            {
                var temp = RenderTexture.GetTemporary(DesignCaptureWidth, DesignCaptureHeight, 0);
                Graphics.Blit(texture, temp);
                RenderTexture.active = temp;
                image.ReadPixels(new Rect(0, 0, DesignCaptureWidth, DesignCaptureHeight), 0, 0);
                image.Apply();
                RenderTexture.ReleaseTemporary(temp);
            }
            else throw new InvalidOperationException("PreviewRenderUtility returned an unsupported texture.");
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    private void InspectPrefab(StagePaths paths, SlopArenaStageInspectionResult result, string outputDirectory)
    {
        if (!File.Exists(paths.PrefabFullPath))
        {
            Add(result.Diagnostics, "PREFAB_MISSING", paths.PrefabAssetPath);
            return;
        }

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(paths.PrefabAssetPath);
        if (asset == null)
        {
            Add(result.Diagnostics, "PREFAB_UNREADABLE", paths.PrefabAssetPath);
            return;
        }

        GameObject contents = null;
        try
        {
            contents = PrefabUtility.LoadPrefabContents(paths.PrefabAssetPath);
            if (!IsIdentity(contents.transform))
                Add(result.Diagnostics, "PREFAB_ROOT_TRANSFORM_MISMATCH", "Cosmetic root must have position zero, identity rotation, and unit scale.");

            var metrics = new SlopArenaStageCosmeticMetrics();
            Renderer[] renderers = contents.GetComponentsInChildren<Renderer>(true);
            Collider[] colliders = contents.GetComponentsInChildren<Collider>(true);
            metrics.RendererCount = renderers.Length;
            metrics.ColliderCount = colliders.Length;
            metrics.LocalLightCount = contents.GetComponentsInChildren<Light>(true).Length;
            if (metrics.LocalLightCount == 0) Add(result.Diagnostics, "LOCAL_LIGHTING_MISSING", "Cosmetic stage prefab must own local lighting.");
            if (colliders.Length > 0) Add(result.Diagnostics, "COSMETIC_COLLIDER", $"Prefab contains {colliders.Length} Collider component(s).");

            var materials = new HashSet<Material>();
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (!IsFinite(renderer.bounds)) Add(result.Diagnostics, "INVALID_RENDER_BOUNDS", renderer.name);
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
                metrics.TriangleCount += TriangleCount(renderer);
                Material[] slots = renderer.sharedMaterials ?? Array.Empty<Material>();
                metrics.MaterialSlotCount += slots.Length;
                foreach (Material material in slots)
                {
                    if (material == null)
                    {
                        metrics.MissingMaterialReferenceCount++;
                        continue;
                    }
                    materials.Add(material);
                    Shader shader = material.shader;
                    if (shader == null) metrics.MissingShaderCount++;
                    else if (shader.isSupported) AddUnique(metrics.SupportedShaders, shader.name);
                    else AddUnique(metrics.UnsupportedShaders, shader.name);
                }
            }
            metrics.UniqueMaterialCount = materials.Count;
            foreach (MeshFilter filter in contents.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null) metrics.MissingMeshReferenceCount++;
            foreach (SkinnedMeshRenderer renderer in contents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh == null) metrics.MissingMeshReferenceCount++;
            if (metrics.RendererCount == 0) Add(result.Diagnostics, "NO_RENDERERS", "Cosmetic prefab contains no renderers.");
            if (metrics.MissingMeshReferenceCount > 0) Add(result.Diagnostics, "MISSING_MESH_REFERENCE", metrics.MissingMeshReferenceCount.ToString());
            if (metrics.MissingMaterialReferenceCount > 0) Add(result.Diagnostics, "MISSING_MATERIAL_REFERENCE", metrics.MissingMaterialReferenceCount.ToString());
            if (metrics.MissingShaderCount > 0 || metrics.UnsupportedShaders.Count > 0)
                Add(result.Diagnostics, "UNSUPPORTED_SHADER", "Cosmetic prefab contains missing or unsupported shaders.");
            foreach (MonoBehaviour behaviour in contents.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                string typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
                if (!typeName.EndsWith("ArenaAtmosphere", StringComparison.Ordinal))
                    Add(result.Diagnostics, "COSMETIC_GAMEPLAY_COMPONENT", typeName);
            }
            if (hasBounds)
            {
                metrics.BoundsCenter = new SlopArenaStageVector3(contents.transform.InverseTransformPoint(bounds.center));
                metrics.BoundsDimensions = new SlopArenaStageVector3(bounds.size);
            }
            result.CosmeticMetrics = metrics;
        }
        catch (Exception ex)
        {
            Add(result.Diagnostics, "PREFAB_INSPECTION_EXCEPTION", ex.Message);
        }
        finally
        {
            if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private void RenderCaptures(StagePaths paths, ArenaDefinition? bakedArena, SlopArenaStageInspectionResult result, string outputDirectory)
    {
        if (!bakedArena.HasValue || bakedArena.Value.CollisionTriangles == null) return;
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Add(result.Diagnostics, "CAPTURE_GPU_UNAVAILABLE", "Six stage captures require a GPU-backed Unity Editor.");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths.PrefabAssetPath);
        if (prefab == null) return;
        GameObject stage = null;
        GameObject overlay = null;
        Mesh overlayMesh = null;
        Material lineMaterial = null;
        Material markerMaterial = null;
        var captureMaterials = new List<Material>();
        PreviewRenderUtility utility = null;
        try
        {
            stage = UnityEngine.Object.Instantiate(prefab);
            stage.hideFlags = HideFlags.HideAndDontSave;
            ApplyCaptureMaterials(stage, captureMaterials);
            overlay = new GameObject("AuthoritativeOverlay") { hideFlags = HideFlags.HideAndDontSave };
            overlayMesh = BuildOverlayMesh(bakedArena.Value.CollisionTriangles);
            var shell = new GameObject("CollisionShell") { hideFlags = HideFlags.HideAndDontSave };
            shell.transform.SetParent(overlay.transform, false);
            var shellFilter = shell.AddComponent<MeshFilter>();
            shellFilter.sharedMesh = overlayMesh;
            var shellRenderer = shell.AddComponent<MeshRenderer>();
            lineMaterial = CreatePreviewMaterial(CollisionOverlay);
            shellRenderer.sharedMaterial = lineMaterial;
            foreach (SpawnPoint spawn in bakedArena.Value.SpawnPoints ?? Array.Empty<SpawnPoint>())
            {
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "SpawnOverlay";
                marker.hideFlags = HideFlags.HideAndDontSave;
                marker.transform.SetParent(overlay.transform, false);
                marker.transform.position = new Vector3(spawn.X, spawn.Y, spawn.Z);
                marker.transform.localScale = Vector3.one * 0.55f;
                UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
                if (markerMaterial == null) markerMaterial = CreatePreviewMaterial(SpawnOverlay);
                marker.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
            }
            utility = new PreviewRenderUtility(true);
            utility.AddSingleGO(stage);
            utility.AddSingleGO(overlay);
            Bounds framing = FramingBounds(stage, bakedArena.Value);
            foreach (CaptureView view in Enum.GetValues(typeof(CaptureView)))
            {
                string name = ViewName(view) + ".png";
                string fullPath = Path.Combine(outputDirectory, name);
                RenderCapture(utility, framing, view, fullPath);
                result.Captures.Add(RelativeToRepository(fullPath));
            }
        }
        catch (Exception ex)
        {
            Add(result.Diagnostics, "CAPTURE_EXCEPTION", ex.Message);
        }
        finally
        {
            if (utility != null) utility.Cleanup();
            DestroyPreviewObject(stage);
            DestroyPreviewObject(overlay);
            DestroyPreviewObject(overlayMesh);
            DestroyPreviewObject(lineMaterial);
            DestroyPreviewObject(markerMaterial);
            foreach (Material material in captureMaterials) DestroyPreviewObject(material);
        }
    }

    private static void RenderCapture(PreviewRenderUtility utility, Bounds framing, CaptureView view, string path)
    {
        Camera camera = utility.camera;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = CaptureBackground;
        camera.orthographic = true;
        camera.aspect = CaptureWidth / (float)CaptureHeight;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 500f;
        Vector3 direction = ViewDirection(view);
        Vector3 up = view == CaptureView.Top ? Vector3.forward : Vector3.up;
        camera.transform.position = framing.center + direction * 100f;
        camera.transform.rotation = Quaternion.LookRotation(-direction, up);
        float horizontal = framing.size.x / camera.aspect;
        float vertical = framing.size.y;
        if (view == CaptureView.Top || view == CaptureView.Front || view == CaptureView.Back)
        {
            horizontal = view == CaptureView.Top ? framing.size.x : framing.size.x;
            vertical = view == CaptureView.Top ? framing.size.z : framing.size.y;
        }
        else if (view == CaptureView.Left || view == CaptureView.Right)
        {
            horizontal = framing.size.z;
            vertical = framing.size.y;
        }
        else
        {
            horizontal = Mathf.Max(framing.size.x, framing.size.z) * 1.25f;
            vertical = framing.size.y * 1.25f;
        }
        camera.orthographicSize = Mathf.Max(1f, Mathf.Max(vertical, horizontal / camera.aspect) * 0.58f);
        utility.BeginPreview(new Rect(0, 0, CaptureWidth, CaptureHeight), GUIStyle.none);
        camera.Render();
        Texture preview = utility.EndPreview();
        var image = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            if (preview is RenderTexture renderTexture)
            {
                RenderTexture.active = renderTexture;
                image.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                image.Apply();
            }
            else if (preview is Texture2D texture)
            {
                image.SetPixels(texture.GetPixels());
                image.Apply();
            }
            else throw new InvalidOperationException("PreviewRenderUtility returned an unsupported texture.");
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    private static Bounds FramingBounds(GameObject stage, ArenaDefinition arena)
    {
        Renderer[] renderers = stage.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        foreach (CollisionTriangle triangle in arena.CollisionTriangles ?? Array.Empty<CollisionTriangle>())
        {
            bounds.Encapsulate(new Vector3(triangle.AX, triangle.AY, triangle.AZ));
            bounds.Encapsulate(new Vector3(triangle.BX, triangle.BY, triangle.BZ));
            bounds.Encapsulate(new Vector3(triangle.CX, triangle.CY, triangle.CZ));
        }
        foreach (SpawnPoint spawn in arena.SpawnPoints ?? Array.Empty<SpawnPoint>())
            bounds.Encapsulate(new Vector3(spawn.X, spawn.Y, spawn.Z));
        bounds.Expand(2f);
        return bounds;
    }

    private static Vector3 ViewDirection(CaptureView view)
    {
        switch (view)
        {
            case CaptureView.Top: return Vector3.up;
            case CaptureView.Front: return Vector3.back;
            case CaptureView.Back: return Vector3.forward;
            case CaptureView.Left: return Vector3.left;
            case CaptureView.Right: return Vector3.right;
            default: return new Vector3(1f, 0.8f, 1f).normalized;
        }
    }

    private static string ViewName(CaptureView view) => view.ToString().ToLowerInvariant();

    private static Mesh BuildOverlayMesh(CollisionTriangle[] triangles)
    {
        var vertices = new Vector3[triangles.Length * 3];
        var indices = new int[vertices.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            CollisionTriangle triangle = triangles[i];
            int offset = i * 3;
            vertices[offset] = new Vector3(triangle.AX, triangle.AY + 0.015f, triangle.AZ);
            vertices[offset + 1] = new Vector3(triangle.BX, triangle.BY + 0.015f, triangle.BZ);
            vertices[offset + 2] = new Vector3(triangle.CX, triangle.CY + 0.015f, triangle.CZ);
            indices[offset] = offset;
            indices[offset + 1] = offset + 1;
            indices[offset + 2] = offset + 2;
        }
        var mesh = new Mesh { name = "AuthoritativeCollisionOverlay" };
        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Material CreatePreviewMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        if (shader == null) throw new InvalidOperationException("No preview shader is available.");
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (color.a < 0.999f)
        {
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        return material;
    }
    private static void ApplyCaptureMaterials(GameObject stage, List<Material> created)
    {
        foreach (Renderer renderer in stage.GetComponentsInChildren<Renderer>(true))
        {
            Material[] source = renderer.sharedMaterials ?? Array.Empty<Material>();
            if (source.Length == 0) continue;
            var replacements = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Material original = source[i];
                Color color = new Color(0.46f, 0.5f, 0.56f, 1f);
                if (original != null)
                {
                    if (original.HasProperty("_BaseColor")) color = original.GetColor("_BaseColor");
                    else if (original.HasProperty("_Color")) color = original.GetColor("_Color");
                    color.a = 1f;
                }
                replacements[i] = CreatePreviewMaterial(color);
                created.Add(replacements[i]);
            }
            renderer.sharedMaterials = replacements;
        }
    }

    private StageBuild BuildFromScene(Scene scene, string stage)
    {
        var build = new StageBuild();
        GameObject[] roots = scene.GetRootGameObjects();
        if (roots.Length != 1) Add(build.Diagnostics, "SOURCE_ROOT_COUNT", "Authoring scene must contain exactly one root.");
        GameObject root = roots.FirstOrDefault(x => x.name == "Stage_" + stage);
        if (root == null)
        {
            Add(build.Diagnostics, "SOURCE_ROOT_MISSING", "Expected root Stage_" + stage + ".");
            return build;
        }
        if (!IsIdentity(root.transform)) Add(build.Diagnostics, "SOURCE_ROOT_TRANSFORM_MISMATCH", "Source root must have position zero, identity rotation, and unit scale.");

        Transform geometry = root.transform.Find("GameplayGeometry");
        Transform spawns = root.transform.Find("SpawnPoints");
        Transform aids = root.transform.Find("AuthoringAids");
        var arcadeSource = aids != null ? aids.GetComponent<SlopArena.Client.World.ArcadeStageAuthoring>() : null;
        ArcadeStageDefinition arcade = null;
        if (arcadeSource != null)
        {
            try { arcade = arcadeSource.Compile(); }
            catch (ArgumentException ex) { Add(build.Diagnostics, "ARCADE_PROFILE_INVALID", ex.Message); }
        }
        Transform killPlaneMarker = aids != null ? aids.Find("KillPlane") : null;
        if (geometry == null) Add(build.Diagnostics, "GAMEPLAY_GEOMETRY_MISSING", "Missing GameplayGeometry child.");
        if (spawns == null) Add(build.Diagnostics, "SPAWN_CONTAINER_MISSING", "Missing SpawnPoints child.");
        if (aids == null) Add(build.Diagnostics, "AUTHORING_AIDS_MISSING", "Missing AuthoringAids child.");
        if (geometry == null || spawns == null || aids == null) return build;
        if (killPlaneMarker != null
            && (killPlaneMarker.childCount != 0 || killPlaneMarker.GetComponents<Component>().Length != 1))
            Add(build.Diagnostics, "KILL_PLANE_MARKER_NOT_EMPTY", "AuthoringAids/KillPlane must be an empty transform.");

        string[] childNames = root.transform.Cast<Transform>().Select(x => x.name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        string[] expectedChildren = { "AuthoringAids", "GameplayGeometry", "SpawnPoints" };
        if (!childNames.SequenceEqual(expectedChildren, StringComparer.Ordinal))
            Add(build.Diagnostics, "SOURCE_HIERARCHY_MISMATCH", "Stage root must contain only GameplayGeometry, SpawnPoints, and AuthoringAids.");

        if (root.GetComponentsInChildren<Terrain>(true).Length > 0)
            Add(build.Diagnostics, "TERRAIN_UNSUPPORTED", "Terrain is not valid authoritative stage geometry.");
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component is Transform || component is MeshFilter || component is MeshRenderer) continue;
            if (component == arcadeSource) continue;
            Add(build.Diagnostics, "SOURCE_COMPONENT_UNSUPPORTED", component.GetType().Name + " on " + component.gameObject.name);
        }

        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        var triangles = new List<CollisionTriangle>();
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        float minY = float.MaxValue;
        foreach (MeshFilter filter in meshFilters)
        {
            if (!IsDescendantOf(filter.transform, geometry))
            {
                Add(build.Diagnostics, "MESH_FILTER_OUTSIDE_GAMEPLAY_GEOMETRY", filter.name);
                continue;
            }
            if (!filter.gameObject.isStatic) Add(build.Diagnostics, "GAMEPLAY_GEOMETRY_NOT_STATIC", filter.name);
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                Add(build.Diagnostics, "SOURCE_MESH_MISSING", filter.name);
                continue;
            }
            Vector3[] vertices;
            int[] indices;
            try { vertices = mesh.vertices; indices = mesh.triangles; }
            catch (Exception ex)
            {
                Add(build.Diagnostics, "SOURCE_MESH_UNREADABLE", filter.name + ": " + ex.Message);
                continue;
            }
            if (indices.Length == 0 || indices.Length % 3 != 0)
            {
                Add(build.Diagnostics, "SOURCE_MESH_EMPTY", filter.name);
                continue;
            }
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = filter.transform.TransformPoint(vertices[indices[i]]);
                Vector3 b = filter.transform.TransformPoint(vertices[indices[i + 1]]);
                Vector3 c = filter.transform.TransformPoint(vertices[indices[i + 2]]);
                if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
                {
                    Add(build.Diagnostics, "SOURCE_VERTEX_NONFINITE", filter.name);
                    continue;
                }
                triangles.Add(new CollisionTriangle
                {
                    AX = a.x, AY = a.y, AZ = a.z,
                    BX = b.x, BY = b.y, BZ = b.z,
                    CX = c.x, CY = c.y, CZ = c.z
                });
                foreach (Vector3 point in new[] { a, b, c })
                {
                    minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x);
                    minY = Mathf.Min(minY, point.y);
                    minZ = Mathf.Min(minZ, point.z); maxZ = Mathf.Max(maxZ, point.z);
                }
            }
        }
        if (triangles.Count == 0) Add(build.Diagnostics, "SOURCE_NO_COLLISION_TRIANGLES", "GameplayGeometry must contain readable mesh triangles.");

        var spawnTransforms = spawns.Cast<Transform>().ToArray();
        int expectedSpawnCount = arcadeSource != null ? 1 : 4;
        if (spawnTransforms.Length != expectedSpawnCount)
            Add(build.Diagnostics, "SPAWN_COUNT", arcadeSource != null
                ? "Arcade SpawnPoints must contain exactly Spawn_01."
                : "SpawnPoints must contain exactly Spawn_01 through Spawn_04.");
        var spawnPoints = new List<SpawnPoint>();
        for (int i = 0; i < expectedSpawnCount; i++)
        {
            string expectedName = "Spawn_" + (i + 1).ToString("00");
            Transform marker = spawnTransforms.FirstOrDefault(x => x.name == expectedName);
            if (marker == null)
            {
                Add(build.Diagnostics, "SPAWN_ORDER_OR_NAME", "Missing " + expectedName + ".");
                continue;
            }
            if (!marker.CompareTag("SpawnPoint")) Add(build.Diagnostics, "SPAWN_TAG_MISSING", expectedName);
            if (marker.childCount != 0 || marker.GetComponents<Component>().Length != 1)
                Add(build.Diagnostics, "SPAWN_MARKER_NOT_EMPTY", expectedName);
            float surfaceY;
            if (!TrySurfaceY(triangles, marker.position.x, marker.position.z, out surfaceY, arcadeSource != null) ||
                Mathf.Abs(marker.position.y - (surfaceY + SpawnBodyHalfHeight)) > SpawnGroundTolerance)
                Add(build.Diagnostics, "SPAWN_NOT_GROUNDED", expectedName);
            spawnPoints.Add(new SpawnPoint
            {
                X = marker.position.x,
                Y = marker.position.y,
                Z = marker.position.z,
                Yaw = marker.eulerAngles.y * Mathf.Deg2Rad
            });
        }

        if (build.Diagnostics.Count > 0 || triangles.Count == 0 || spawnPoints.Count != expectedSpawnCount) return build;
        float killHeight = minY - 10f;
        if (killPlaneMarker != null)
        {
            if (!IsFinite(killPlaneMarker.position))
                Add(build.Diagnostics, "KILL_PLANE_NONFINITE", "AuthoringAids/KillPlane must have a finite position.");
            else if (Mathf.Abs(killPlaneMarker.position.y - minY) > 0.001f)
                Add(build.Diagnostics, "KILL_PLANE_GEOMETRY_MISMATCH",
                    "AuthoringAids/KillPlane Y must match the lowest gameplay geometry vertex.");
            killHeight = killPlaneMarker.position.y;
        }
        if (build.Diagnostics.Count > 0) return build;
        int gridWidth = Mathf.CeilToInt((maxX - minX) / HeightmapCellSize) + 1;
        int gridHeight = Mathf.CeilToInt((maxZ - minZ) / HeightmapCellSize) + 1;
        var heightData = Enumerable.Repeat(float.MinValue, gridWidth * gridHeight).ToArray();
        foreach (CollisionTriangle triangle in triangles)
        {
            // The net is ball collision, never a player landing platform or projectile ground.
            if (arcade?.Slopball is SlopballDefinition rules
                && Mathf.Abs(triangle.AZ - rules.NetZ) <= rules.NetHalfThickness + .001f
                && Mathf.Abs(triangle.BZ - rules.NetZ) <= rules.NetHalfThickness + .001f
                && Mathf.Abs(triangle.CZ - rules.NetZ) <= rules.NetHalfThickness + .001f) continue;
            RasterizeTriangle(triangle, minX, minZ, gridWidth, gridHeight, heightData, arcadeSource != null);
        }
        float highestSurface = heightData.Where(x => x > float.MinValue / 2f).DefaultIfEmpty(killHeight + 10f).Max();
        var arena = new ArenaDefinition
        {
            Name = stage,
            Arcade = arcade,
            DisplayName = DisplayName(stage),
            PreviewColor = "#24374d",
            KillHeight = killHeight,
            KillTop = highestSurface + ArenaCollision.TopBlastMargin,
            KillMinX = minX - ArenaCollision.SideBlastMargin,
            KillMaxX = maxX + ArenaCollision.SideBlastMargin,
            KillMinZ = minZ - ArenaCollision.SideBlastMargin,
            KillMaxZ = maxZ + ArenaCollision.SideBlastMargin,
            MinX = minX,
            MaxX = maxX,
            MinZ = minZ,
            MaxZ = maxZ,
            SpawnPoints = spawnPoints.ToArray(),
            CollisionTriangles = triangles.ToArray(),
            Heightmap = new ArenaHeightmap
            {
                Width = gridWidth,
                Height = gridHeight,
                CellSize = HeightmapCellSize,
                OriginX = minX,
                OriginZ = minZ,
                Data = heightData
            }
        };
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        try { arcade?.Slopball?.ValidateCourt(in arena); }
        catch (ArgumentException exception)
        { Add(build.Diagnostics, "SLOPBALL_PROFILE_INVALID", exception.Message); return build; }
        build.Arena = arena;
        build.HasArena = true;
        return build;
    }

    private static void RasterizeTriangle(CollisionTriangle triangle, float minX, float minZ, int gridWidth, int gridHeight, float[] heightData,
        bool upwardOnly = false)
    {
        float tMinX = Mathf.Min(triangle.AX, Mathf.Min(triangle.BX, triangle.CX));
        float tMaxX = Mathf.Max(triangle.AX, Mathf.Max(triangle.BX, triangle.CX));
        float tMinZ = Mathf.Min(triangle.AZ, Mathf.Min(triangle.BZ, triangle.CZ));
        float tMaxZ = Mathf.Max(triangle.AZ, Mathf.Max(triangle.BZ, triangle.CZ));
        int cellX0 = Mathf.Max(0, Mathf.FloorToInt((tMinX - minX) / HeightmapCellSize));
        int cellX1 = Mathf.Min(gridWidth - 1, Mathf.FloorToInt((tMaxX - minX) / HeightmapCellSize));
        int cellZ0 = Mathf.Max(0, Mathf.FloorToInt((tMinZ - minZ) / HeightmapCellSize));
        int cellZ1 = Mathf.Min(gridHeight - 1, Mathf.FloorToInt((tMaxZ - minZ) / HeightmapCellSize));
        Vector3 v0 = new Vector3(triangle.AX, triangle.AY, triangle.AZ);
        Vector3 v1 = new Vector3(triangle.BX, triangle.BY, triangle.BZ);
        Vector3 v2 = new Vector3(triangle.CX, triangle.CY, triangle.CZ);
        Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
        if (Mathf.Abs(normal.y) < 0.001f) return;
        if (upwardOnly && normal.y <= 0f) return;
        for (int z = cellZ0; z <= cellZ1; z++)
        for (int x = cellX0; x <= cellX1; x++)
        {
            float worldX = minX + (x + 0.5f) * HeightmapCellSize;
            float worldZ = minZ + (z + 0.5f) * HeightmapCellSize;
            float t = Vector3.Dot(normal, v0 - new Vector3(worldX, 1000f, worldZ)) / (-normal.y);
            if (t < 0f || t > 2000f) continue;
            float y = 1000f - t;
            if (!PointInTriangleXZ(v0, v1, v2, worldX, worldZ)) continue;
            int index = z * gridWidth + x;
            if (y > heightData[index]) heightData[index] = y;
        }
    }

    private static bool TrySurfaceY(List<CollisionTriangle> triangles, float x, float z, out float surfaceY,
        bool upwardOnly = false)
    {
        surfaceY = float.MinValue;
        bool found = false;
        foreach (CollisionTriangle triangle in triangles)
        {
            Vector3 a = new Vector3(triangle.AX, triangle.AY, triangle.AZ);
            Vector3 b = new Vector3(triangle.BX, triangle.BY, triangle.BZ);
            Vector3 c = new Vector3(triangle.CX, triangle.CY, triangle.CZ);
            if (upwardOnly && Vector3.Cross(b - a, c - a).y <= 0f) continue;
            if (!PointInTriangleXZ(a, b, c, x, z)) continue;
            float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < 0.000001f) continue;
            float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
            float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
            float w = 1f - u - v;
            float y = u * a.y + v * b.y + w * c.y;
            if (!found || y > surfaceY) { surfaceY = y; found = true; }
        }
        return found;
    }

    private static bool PointInTriangleXZ(Vector3 a, Vector3 b, Vector3 c, float x, float z)
    {
        float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
        if (Mathf.Abs(denominator) < 0.000001f) return false;
        float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
        float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
        return u >= -0.0001f && v >= -0.0001f && u + v <= 1.0001f;
    }

    private bool TryBuildPaths(string stage, List<SlopArenaStageDiagnostic> diagnostics, out StagePaths paths)
    {
        paths = null;
        if (string.IsNullOrEmpty(stage) || !IsValidKey(stage))
        {
            Add(diagnostics, "INVALID_STAGE_KEY", "Stage key must be lowercase snake_case.");
            return false;
        }
        string sourceAssetPath = "Assets/Stages/" + stage + "/" + stage + ".unity";
        string prefabAssetPath = "Assets/Resources/Stages/" + stage + ".prefab";
        paths = new StagePaths
        {
            Key = stage,
            SourceAssetPath = sourceAssetPath,
            SourceFullPath = Path.Combine(_projectRoot, sourceAssetPath.Replace('/', Path.DirectorySeparatorChar)),
            ArenaFullPath = Path.Combine(_repositoryRoot, "data", "arenas", stage + ".arena"),
            PrefabAssetPath = prefabAssetPath,
            PrefabFullPath = Path.Combine(_projectRoot, prefabAssetPath.Replace('/', Path.DirectorySeparatorChar))
        };
        return true;
    }

    private bool TryResolveOutput(string output, List<SlopArenaStageDiagnostic> diagnostics, out string outputPath)
    {
        outputPath = null;
        if (string.IsNullOrEmpty(output))
        {
            Add(diagnostics, "INVALID_OUTPUT_PATH", "Inspection output is required.");
            return false;
        }
        string full = Path.GetFullPath(Path.IsPathRooted(output) ? output : Path.Combine(_repositoryRoot, output));
        if (!IsUnder(full, _stageCacheRoot) || !full.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            Add(diagnostics, "INVALID_OUTPUT_PATH", "Inspection output must be a .json file under .stage-authoring-cache.");
            return false;
        }
        outputPath = full;
        return true;
    }

    private static void CleanCaptureArtifacts(string directory)
    {
        foreach (string name in new[] { "top.png", "front.png", "back.png", "left.png", "right.png", "isometric.png" })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string DisplayName(string stage)
    {
        return string.Join(" ", stage.Split('_').Select(x => x.Length == 0 ? x : char.ToUpperInvariant(x[0]) + x.Substring(1)));
    }

    private static bool IsValidKey(string value)
    {
        if (value.Length == 0 || !char.IsLetter(value[0]) || value.Any(x => char.IsUpper(x) || !(char.IsLetterOrDigit(x) || x == '_'))) return false;
        return value[value.Length - 1] != '_' && !value.Contains("__", StringComparison.Ordinal);
    }

    private static bool IsDescendantOf(Transform child, Transform ancestor)
    {
        for (Transform current = child; current != null; current = current.parent)
            if (current == ancestor) return true;
        return false;
    }

    private static bool IsIdentity(Transform transform)
    {
        return Vector3.Distance(transform.localPosition, Vector3.zero) < 0.0001f &&
            Quaternion.Angle(transform.localRotation, Quaternion.identity) < 0.0001f &&
            Vector3.Distance(transform.localScale, Vector3.one) < 0.0001f;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }

    private static bool IsFinite(Bounds bounds) => IsFinite(bounds.center) && IsFinite(bounds.size);

    private static int TriangleCount(Renderer renderer)
    {
        Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
            : renderer.TryGetComponent<MeshFilter>(out var filter) && filter != null ? filter.sharedMesh : null;
        return mesh == null ? 0 : mesh.triangles.Length / 3;
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.Ordinal)) values.Add(value);
    }

    private static void Add(List<SlopArenaStageDiagnostic> diagnostics, string code, string message)
    {
        if (!diagnostics.Any(x => x.Code == code && x.Message == message)) diagnostics.Add(new SlopArenaStageDiagnostic { Code = code, Message = message });
    }

    private string HashArena(ArenaDefinition arena) => HashBytes(ArenaBinaryFormat.Serialize(arena));
    private static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));
    private static string HashBytes(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static bool ByteArraysEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private string RelativeToRepository(string path) => Path.GetRelativePath(_repositoryRoot, path).Replace('\\', '/');
    private static bool IsUnder(string path, string root)
    {
        string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void DestroyPreviewObject(UnityEngine.Object value)
    {
        if (value != null) UnityEngine.Object.DestroyImmediate(value);
    }
}
