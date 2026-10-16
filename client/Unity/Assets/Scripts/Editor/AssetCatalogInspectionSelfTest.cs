using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class AssetCatalogInspectionSelfTest
{
    public static void Run()
    {
        string projectRoot = UnityCharacterAssetCooker.ProjectRoot();
        string assetFolder = "Assets/AssetCatalogInspectionSelfTest";
        string cacheFolder = Path.Combine(projectRoot, "..", "..", ".asset-catalog-cache", "self-test");
        string scaledPath = assetFolder + "/ScaledCube.prefab";
        string disabledPath = assetFolder + "/DisabledCube.prefab";
        string hugePath = assetFolder + "/HugeCube.prefab";
        string scaledWorkset = Path.Combine(cacheFolder, "scaled-workset.json");
        string disabledWorkset = Path.Combine(cacheFolder, "disabled-workset.json");
        string hugeWorkset = Path.Combine(cacheFolder, "huge-workset.json");
        string scaledOutput = Path.Combine(cacheFolder, "scaled-inspection.json");
        string disabledOutput = Path.Combine(cacheFolder, "disabled-inspection.json");
        string hugeOutput = Path.Combine(cacheFolder, "huge-inspection.json");
        if (!AssetDatabase.IsValidFolder(assetFolder))
            AssetDatabase.CreateFolder("Assets", "AssetCatalogInspectionSelfTest");
        try
        {
            CreateCubePrefab(scaledPath, "ScaledCube", new Vector3(2f, 3f, 4f), true, true);
            CreateCubePrefab(disabledPath, "DisabledCube", Vector3.one, false, false);
            CreateCubePrefab(hugePath, "HugeCube", new Vector3(1000f, 1000f, 1000f), true, false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(scaledPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(disabledPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(hugePath, ImportAssetOptions.ForceUpdate);

            string scaledGuid = AssetDatabase.AssetPathToGUID(scaledPath);
            string disabledGuid = AssetDatabase.AssetPathToGUID(disabledPath);
            string hugeGuid = AssetDatabase.AssetPathToGUID(hugePath);
            WriteWorkset(scaledWorkset, "ScaledCube", scaledPath, scaledGuid);
            WriteWorkset(disabledWorkset, "DisabledCube", disabledPath, disabledGuid);
            WriteWorkset(hugeWorkset, "HugeCube", hugePath, hugeGuid);

            var service = new AssetCatalogInspectionService(projectRoot);
            AssetCatalogInspectionResult scaled = service.Inspect(scaledWorkset, scaledOutput, false);
            Require(scaled.Success && scaled.Items.Count == 1, "One-item technical inspection failed.");
            AssetCatalogInspectionItem first = scaled.Items[0];
            Require(first.SelectionStatus == "selected" && first.TechnicalValidation.Status == "pass", "Selection or technical validation failed.");
            Require(first.VisualEvidence.Status == "unavailable" && first.VisualEvidence.Diagnostics.Any(x => x.Code == "THUMBNAIL_NOT_REQUESTED"), "Missing not-requested visual status.");
            Require(first.Metrics.RendererCount == 1, "Renderer measurement failed.");
            Require(first.Metrics.ColliderCount == 1 && first.Metrics.EnabledColliderCount == 1, "Collider measurement failed.");
            Require(first.Metrics.UniqueMaterialCount == 1 && first.Metrics.MaterialSlotCount == 1, "Material measurement failed.");
            Require(first.Metrics.HighestDetailTriangleCount == 12, "LOD0 triangle measurement failed.");
            Require(first.Metrics.LodGroupCount == 1 && first.Metrics.LodLevels.Count == 2, "LOD measurement failed.");
            Require(first.Metrics.BoundsDimensions.X > 0f && first.Metrics.BoundsDimensions.Y > 0f && first.Metrics.BoundsDimensions.Z > 0f, "Bounds measurement failed.");

            AssetCatalogInspectionResult disabled = service.Inspect(disabledWorkset, disabledOutput, true);
            Require(disabled.Success && disabled.Items[0].TechnicalValidation.Status == "pass", "Disabled-renderer prefab was not technically valid.");
            Require(disabled.Items[0].VisualEvidence.Status == "unavailable" &&
                (disabled.Items[0].VisualEvidence.Diagnostics.Any(x => x.Code == "THUMBNAIL_BLANK") ||
                 disabled.Items[0].VisualEvidence.Diagnostics.Any(x => x.Code == "THUMBNAIL_GPU_UNAVAILABLE")),
                "Disabled-renderer blank preview was not classified as unavailable.");

            AssetCatalogInspectionResult huge = service.Inspect(hugeWorkset, hugeOutput, true);
            Require(huge.Success && huge.Items[0].TechnicalValidation.Status == "pass", "Huge prefab technical validation failed.");
            Require(File.Exists(Path.Combine(Path.GetDirectoryName(hugeOutput), "report.html")), "Inspection report was not created.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Require(huge.Items[0].VisualEvidence.Status == "pass", "Huge prefab did not produce a framed visual preview.");
                Require(huge.ContactSheetCells.Count == 1 && huge.ContactSheetCells[0].Role == "self-test" &&
                    huge.ContactSheetCells[0].IdentityLabel.StartsWith("HugeCube · ", StringComparison.Ordinal), "Contact-sheet cell labels are incomplete.");
            }

            AssetCatalogInspectionCompactResult compact = (AssetCatalogInspectionCompactResult)
                SlopArenaCharacterCommands.Inspect(scaledWorkset, scaledOutput, false, true);
            Require(compact.InspectionPath == scaled.RelativePath(scaledOutput, projectRoot) && compact.ReportPath == null && compact.ContactSheetPath == null,
                "Compact inspection paths were incorrect.");
            UnityEngine.Debug.Log("[SLOPArena] Asset catalog inspection self-test passed.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(assetFolder);
            AssetDatabase.Refresh();
            if (Directory.Exists(cacheFolder)) Directory.Delete(cacheFolder, true);
        }
    }

    private static void CreateCubePrefab(string path, string name, Vector3 scale, bool rendererEnabled, bool withLod)
    {
        var root = new GameObject(name);
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Mesh";
        cube.transform.SetParent(root.transform, false);
        cube.transform.localScale = scale;
        Renderer renderer = cube.GetComponent<Renderer>();
        renderer.enabled = rendererEnabled;
        if (withLod)
        {
            var lodGroup = root.AddComponent<LODGroup>();
            lodGroup.SetLODs(new[] { new LOD(1f, new[] { renderer }), new LOD(0.5f, new Renderer[0]) });
        }
        PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void WriteWorkset(string path, string name, string prefabPath, string guid)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var workset = new AssetCatalogWorkset
        {
            SchemaVersion = 1,
            Concept = "inspection self-test",
            Shortlist = new List<AssetCatalogWorksetItem>
            {
                new AssetCatalogWorksetItem
                {
                    SourcePack = "self-test",
                    Id = guid,
                    Name = name,
                    Path = prefabPath,
                    Role = "self-test",
                    SelectionStatus = "selected",
                    Reasons = new List<string>()
                }
            }
        };
        File.WriteAllText(path, JsonConvert.SerializeObject(workset, Formatting.Indented));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string RelativePath(this AssetCatalogInspectionResult result, string path, string projectRoot)
    {
        return Path.GetRelativePath(Directory.GetParent(projectRoot).Parent.FullName, path).Replace('\\', '/');
    }
}
