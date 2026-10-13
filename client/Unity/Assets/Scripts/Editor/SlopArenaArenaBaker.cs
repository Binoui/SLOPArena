using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;

/// <summary>
/// Bakes a Unity arena scene (prefab or GameObject hierarchy) into a .arena binary file.
/// 
/// Extracts:
///   - Collision triangle mesh from MeshFilters
///   - Heightmap rasterized from collision triangles
///   - SpawnPoint[] from tagged "SpawnPoint" GameObjects
///   - KillHeight from manual field
/// 
/// Usage: Tools → SlopArena → Legacy → Bake Arena...
/// Select the root GameObject containing all arena meshes, set name/spawns, bake.
/// </summary>
public class SlopArenaArenaBaker : EditorWindow
{
    /// <summary>When true, BakeArena skips modal dialogs (used by automated/CI bakes).</summary>
    public static bool SuppressDialogs { get; set; }

    private GameObject _arenaRoot;
    private string _arenaName = "my_arena";
    private string _displayName = "My Arena";
    private string _previewColor = "#1a1a1a";
    private float _killHeight = -10f;
    private bool _autoBlastLines = true;
    private float _killTop;
    private float _killMinX;
    private float _killMaxX;
    private float _killMinZ;
    private float _killMaxZ;
    private float _minX = -25f;
    private float _maxX = 25f;
    private float _minZ = -25f;
    private float _maxZ = 25f;
    private string _outputDir = "../../data/arenas";
    private bool _autoBounds = true;
    [MenuItem("Tools/SlopArena/Legacy/Bake Arena...")]
    public static void ShowWindow()
    {
        GetWindow<SlopArenaArenaBaker>("Bake Arena");
    }

    private void OnGUI()
    {
        _arenaRoot = (GameObject)EditorGUILayout.ObjectField("Arena Root", _arenaRoot, typeof(GameObject), true);

        EditorGUILayout.Space();
        _arenaName = EditorGUILayout.TextField("Arena Key (file name)", _arenaName);
        _displayName = EditorGUILayout.TextField("Display Name", _displayName);
        _previewColor = EditorGUILayout.TextField("Preview Color", _previewColor);

        EditorGUILayout.Space();
        _killHeight = EditorGUILayout.FloatField("Kill Height (Y below = death)", _killHeight);

        EditorGUILayout.Space();
        _autoBlastLines = EditorGUILayout.Toggle("Auto Side/Top Blast Lines", _autoBlastLines);
        if (!_autoBlastLines)
        {
            EditorGUI.indentLevel++;
            _killTop = EditorGUILayout.FloatField("Kill Top (Y above = death)", _killTop);
            _killMinX = EditorGUILayout.FloatField("Kill Min X", _killMinX);
            _killMaxX = EditorGUILayout.FloatField("Kill Max X", _killMaxX);
            _killMinZ = EditorGUILayout.FloatField("Kill Min Z", _killMinZ);
            _killMaxZ = EditorGUILayout.FloatField("Kill Max Z", _killMaxZ);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        _autoBounds = EditorGUILayout.Toggle("Auto Bounds from Mesh", _autoBounds);
        if (!_autoBounds)
        {
            EditorGUI.indentLevel++;
            _minX = EditorGUILayout.FloatField("Min X", _minX);
            _maxX = EditorGUILayout.FloatField("Max X", _maxX);
            _minZ = EditorGUILayout.FloatField("Min Z", _minZ);
            _maxZ = EditorGUILayout.FloatField("Max Z", _maxZ);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        _outputDir = EditorGUILayout.TextField("Output (rel to project root)", _outputDir);

        EditorGUILayout.HelpBox(
            "Tag GameObjects as 'SpawnPoint' to auto-detect spawns.\n" +
            "All MeshFilters under Arena Root become collision triangles.\n" +
            "A heightmap is auto-generated from collision geometry.",
            MessageType.Info);

        GUI.enabled = _arenaRoot != null && !string.IsNullOrEmpty(_arenaName);
        if (GUILayout.Button("Bake Arena"))
        {
            BakeArena();
        }
        GUI.enabled = true;
    }

    private void BakeArena()
    {
        if (_arenaRoot == null)
        {
            if (!SuppressDialogs) EditorUtility.DisplayDialog("Error", "Select an Arena Root GameObject.", "OK");
            return;
        }

        // 1. Collect mesh triangles (world space)
        var tris = new List<CollisionTriangle>();
        var meshFilters = _arenaRoot.GetComponentsInChildren<MeshFilter>(true);

        // First pass: compute bounds if auto
        float autoMinX = float.MaxValue, autoMaxX = float.MinValue;
        float autoMinZ = float.MaxValue, autoMaxZ = float.MinValue;

        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh == null) continue;
            var mesh = mf.sharedMesh;
            var verts = mesh.vertices;
            var t = mf.transform;

            for (int i = 0; i < mesh.triangles.Length; i += 3)
            {
                Vector3 a = t.TransformPoint(verts[mesh.triangles[i]]);
                Vector3 b = t.TransformPoint(verts[mesh.triangles[i + 1]]);
                Vector3 c = t.TransformPoint(verts[mesh.triangles[i + 2]]);

                tris.Add(new CollisionTriangle
                {
                    AX = a.x, AY = a.y, AZ = a.z,
                    BX = b.x, BY = b.y, BZ = b.z,
                    CX = c.x, CY = c.y, CZ = c.z,
                });

                if (_autoBounds)
                {
                    if (a.x < autoMinX) autoMinX = a.x;
                    if (a.x > autoMaxX) autoMaxX = a.x;
                    if (b.x < autoMinX) autoMinX = b.x;
                    if (b.x > autoMaxX) autoMaxX = b.x;
                    if (c.x < autoMinX) autoMinX = c.x;
                    if (c.x > autoMaxX) autoMaxX = c.x;
                    if (a.z < autoMinZ) autoMinZ = a.z;
                    if (a.z > autoMaxZ) autoMaxZ = a.z;
                    if (b.z < autoMinZ) autoMinZ = b.z;
                    if (b.z > autoMaxZ) autoMaxZ = b.z;
                    if (c.z < autoMinZ) autoMinZ = c.z;
                    if (c.z > autoMaxZ) autoMaxZ = c.z;
                }
            }
        }
        // Include Terrain bounds in auto-bounds calculation
        if (_autoBounds)
        {
            var terrainsForBounds = _arenaRoot.GetComponentsInChildren<Terrain>(true);
            foreach (var terrain in terrainsForBounds)
            {
                var tData = terrain.terrainData;
                if (tData == null) continue;
                Transform tTrans = terrain.transform;
                float w = tData.size.x;
                float d = tData.size.z;
                float tMinX = tTrans.position.x;
                float tMaxX = tMinX + w;
                float tMinZ = tTrans.position.z;
                float tMaxZ = tMinZ + d;
                if (tMinX < autoMinX) autoMinX = tMinX;
                if (tMaxX > autoMaxX) autoMaxX = tMaxX;
                if (tMinZ < autoMinZ) autoMinZ = tMinZ;
                if (tMaxZ > autoMaxZ) autoMaxZ = tMaxZ;
            }
        }

            // Also sample Terrain components for collision data
            var terrains = _arenaRoot.GetComponentsInChildren<Terrain>(true);
            foreach (var terrain in terrains)
            {
                var tData = terrain.terrainData;
                if (tData == null) continue;
                Transform tTrans = terrain.transform;
                int res = tData.heightmapResolution;
                float w = tData.size.x;
                float d = tData.size.z;
                float h = tData.size.y;
                Vector3 tPos = tTrans.position;

                for (int iz = 0; iz < res - 1; iz++)
                {
                    for (int ix = 0; ix < res - 1; ix++)
                    {
                        float y00 = tPos.y + h * (tData.GetHeight(ix, iz) / 65535f);
                        float y10 = tPos.y + h * (tData.GetHeight(ix + 1, iz) / 65535f);
                        float y01 = tPos.y + h * (tData.GetHeight(ix, iz + 1) / 65535f);
                        float y11 = tPos.y + h * (tData.GetHeight(ix + 1, iz + 1) / 65535f);

                        float wx0 = tPos.x + (float)ix / (res - 1) * w;
                        float wx1 = tPos.x + (float)(ix + 1) / (res - 1) * w;
                        float wz0 = tPos.z + (float)iz / (res - 1) * d;
                        float wz1 = tPos.z + (float)(iz + 1) / (res - 1) * d;

                        var t1 = new CollisionTriangle
                        {
                            AX = wx0, AY = y00, AZ = wz0,
                            BX = wx1, BY = y10, BZ = wz0,
                            CX = wx0, CY = y01, CZ = wz1,
                        };
                        tris.Add(t1);

                        var t2 = new CollisionTriangle
                        {
                            AX = wx1, AY = y10, AZ = wz0,
                            BX = wx1, BY = y11, BZ = wz1,
                            CX = wx0, CY = y01, CZ = wz1,
                        };
                        tris.Add(t2);
                    }
                }
            }

        if (_autoBounds)
        {
            _minX = autoMinX;
            _maxX = autoMaxX;
            _minZ = autoMinZ;
            _maxZ = autoMaxZ;
        }

        // 2. Generate heightmap from collision triangles
        const float cellSize = 0.5f;
        int gridW = Mathf.CeilToInt((_maxX - _minX) / cellSize) + 1;
        int gridH = Mathf.CeilToInt((_maxZ - _minZ) / cellSize) + 1;
        float[] heightData = new float[gridW * gridH];
        for (int i = 0; i < heightData.Length; i++) heightData[i] = float.MinValue;

        foreach (var tri in tris)
        {
            float tMinX = Mathf.Min(tri.AX, Mathf.Min(tri.BX, tri.CX));
            float tMaxX = Mathf.Max(tri.AX, Mathf.Max(tri.BX, tri.CX));
            float tMinZ = Mathf.Min(tri.AZ, Mathf.Min(tri.BZ, tri.CZ));
            float tMaxZ = Mathf.Max(tri.AZ, Mathf.Max(tri.BZ, tri.CZ));

            int cellX0 = Mathf.Max(0, Mathf.FloorToInt((tMinX - _minX) / cellSize));
            int cellX1 = Mathf.Min(gridW - 1, Mathf.FloorToInt((tMaxX - _minX) / cellSize));
            int cellZ0 = Mathf.Max(0, Mathf.FloorToInt((tMinZ - _minZ) / cellSize));
            int cellZ1 = Mathf.Min(gridH - 1, Mathf.FloorToInt((tMaxZ - _minZ) / cellSize));

            Vector3 v0 = new Vector3(tri.AX, tri.AY, tri.AZ);
            Vector3 v1 = new Vector3(tri.BX, tri.BY, tri.BZ);
            Vector3 v2 = new Vector3(tri.CX, tri.CY, tri.CZ);
            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            if (Mathf.Abs(normal.y) < 0.001f) continue;

            for (int cz = cellZ0; cz <= cellZ1; cz++)
            {
                for (int cx = cellX0; cx <= cellX1; cx++)
                {
                    float worldX = _minX + (cx + 0.5f) * cellSize;
                    float worldZ = _minZ + (cz + 0.5f) * cellSize;

                    float t = Vector3.Dot(normal, v0 - new Vector3(worldX, 1000f, worldZ)) / (-normal.y);
                    if (t < 0f || t > 2000f) continue;

                    float hitY = 1000f - t;
                    Vector3 hitPoint = new Vector3(worldX, hitY, worldZ);

                    Vector3 v0p = hitPoint - v0;
                    Vector3 v01 = v1 - v0;
                    Vector3 v02 = v2 - v0;
                    float d00 = Vector3.Dot(v01, v01);
                    float d01 = Vector3.Dot(v01, v02);
                    float d11 = Vector3.Dot(v02, v02);
                    float d20 = Vector3.Dot(v0p, v01);
                    float d21 = Vector3.Dot(v0p, v02);
                    float denom = d00 * d11 - d01 * d01;
                    if (Mathf.Abs(denom) < 0.0001f) continue;
                    float v = (d11 * d20 - d01 * d21) / denom;
                    float w = (d00 * d21 - d01 * d20) / denom;
                    if (v < 0f || w < 0f || v + w > 1f) continue;

                    int idx = cz * gridW + cx;
                    if (hitY > heightData[idx])
                        heightData[idx] = hitY;
                }
            }
        }

        var heightmap = new ArenaHeightmap
        {
            Width = gridW,
            Height = gridH,
            CellSize = cellSize,
            OriginX = _minX,
            OriginZ = _minZ,
            Data = heightData,
        };

        Debug.Log($"[ArenaBaker] Heightmap: {gridW}x{gridH} cells, {cellSize}m cell size");


        // 3. Collect spawn points from tagged GameObjects
        var spawns = new List<SpawnPoint>();
        var allChildren = _arenaRoot.GetComponentsInChildren<Transform>(true);
        foreach (var child in allChildren)
        {
            if (!child.CompareTag("SpawnPoint")) continue;
            Vector3 pos = child.position;
            float yaw = child.eulerAngles.y * Mathf.Deg2Rad;

            spawns.Add(new SpawnPoint
            {
                X = pos.x,
                Y = pos.y,
                Z = pos.z,
                Yaw = yaw,
            });
        }

        // If no spawn points found, use the center of bounds
        if (spawns.Count == 0)
        {
            float cx = (_minX + _maxX) * 0.5f;
            float cz = (_minZ + _maxZ) * 0.5f;
            float defaultY = 1f;
            spawns.Add(new SpawnPoint { X = cx, Y = defaultY, Z = cz, Yaw = 0f });
            spawns.Add(new SpawnPoint { X = cx, Y = defaultY, Z = cz, Yaw = Mathf.PI });
            Debug.LogWarning("No SpawnPoint-tagged objects found. Using default center spawns.");
        }

        // 4. Build ArenaDefinition
        // Blast lines: auto mode derives from the heightmap/bounds with the shared
        // margins (same numbers the server uses for 0 = auto at resolve time);
        // manual mode writes exactly what was entered (0 = auto at resolve time).
        float hmMax = heightData.Length > 0 ? heightData.Max() : _killHeight + 20f;
        if (hmMax <= float.MinValue / 2f || hmMax < _killHeight) hmMax = _killHeight + 20f;
        var arena = new ArenaDefinition
        {
            Name = _arenaName,
            DisplayName = _displayName,
            PreviewColor = _previewColor,
            KillHeight = _killHeight,
            KillTop = _autoBlastLines ? hmMax + ArenaCollision.TopBlastMargin : _killTop,
            KillMinX = _autoBlastLines ? _minX - ArenaCollision.SideBlastMargin : _killMinX,
            KillMaxX = _autoBlastLines ? _maxX + ArenaCollision.SideBlastMargin : _killMaxX,
            KillMinZ = _autoBlastLines ? _minZ - ArenaCollision.SideBlastMargin : _killMinZ,
            KillMaxZ = _autoBlastLines ? _maxZ + ArenaCollision.SideBlastMargin : _killMaxZ,
            Heightmap = heightmap,
            MinX = _minX,
            MaxX = _maxX,
            MinZ = _minZ,
            MaxZ = _maxZ,
            SpawnPoints = spawns.ToArray(),
            CollisionTriangles = tris.ToArray(),
        };

        // Build spatial grid at bake time (good for validation)
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);

        // 5. Write .arena file
        string outputPath = Path.Combine(_outputDir, _arenaName + ".arena");
        string fullPath = Path.GetFullPath(outputPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            ArenaBinaryFormat.SaveToFile(fullPath, arena);
            Debug.Log($"[ArenaBaker] Baked: {_arenaName}.arena");
            Debug.Log($"  Triangles: {tris.Count}  Spawns: {spawns.Count}  Heightmap: {gridW}x{gridH}");
            Debug.Log($"  Bounds: X[{_minX:F1}, {_maxX:F1}]  Z[{_minZ:F1}, {_maxZ:F1}]");
            Debug.Log($"  Output: {fullPath}");
            if (!SuppressDialogs)
                EditorUtility.DisplayDialog("Success",
                    $"Baked {_arenaName}.arena\n" +
                    $"{tris.Count} triangles, {spawns.Count} spawns\n" +
                    $"→ {fullPath}", "OK");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ArenaBaker] Failed to bake: {ex.Message}");
            if (!SuppressDialogs) EditorUtility.DisplayDialog("Error", $"Bake failed: {ex.Message}", "OK");
        }

        AssetDatabase.Refresh();
    }
}
