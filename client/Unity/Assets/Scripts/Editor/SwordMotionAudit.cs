using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Entities;
using SlopArena.Client.Tools;

namespace SlopArena.EditorTools;

/// <summary>Samples the exact package clips and weapon attachment used by the pose baker.</summary>
public static class SwordMotionAudit
{
    private const float SampleRate = 60f;

    [MenuItem("Tools/SLOPArena/Diagnostics/Sword Motion Audit/Wibou")]
    private static void AuditWibou() => Run("wibou");

    [MenuItem("Tools/SLOPArena/Diagnostics/Sword Motion Audit/Bonk")]
    private static void AuditBonk() => Run("bonk");

    private static void Run(string packageId)
    {
        var preview = AbilityLabPackagePreviewLoader.Load(packageId);
        if (!preview.IsAvailable)
        {
            Debug.LogError($"[SwordMotionAudit] {packageId} preview unavailable: " +
                string.Join("; ", preview.Diagnostics.Select(x => x.Message)));
            return;
        }

        var catalog = preview.AnimationCatalog;
        var entry = (catalog.WeaponConfig?.Entries ?? Array.Empty<WeaponEntry>()).FirstOrDefault(x => x != null);
        if (entry?.Prefab == null)
            throw new InvalidOperationException($"Package '{packageId}' has no weapon attachment prefab.");

        var sourceRig = preview.Rig;
        var rig = UnityEngine.Object.Instantiate(sourceRig);
        rig.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var bone = FindBone(rig, entry.BoneName);
            if (bone == null) throw new InvalidOperationException($"Weapon bone '{entry.BoneName}' was not found.");
            var points = DeriveWeaponPoints(entry.Prefab);
            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library/SlopArena/SwordMotionAudit"));
            Directory.CreateDirectory(outputDirectory);
            string path = Path.Combine(outputDirectory, packageId + ".csv");

            using var writer = new StreamWriter(path, false);
            writer.WriteLine("package,animation,frame,time_seconds,hilt_x,hilt_y,hilt_z,tip_x,tip_y,tip_z,length,tip_step,blade_angle_step_deg,hand_angle_step_deg");
            foreach (var animation in catalog.Animations.Where(x => x?.Clip != null).OrderBy(x => x.SemanticId, StringComparer.Ordinal))
            {
                Vector3 previousTip = default;
                Vector3 previousAxis = default;
                Quaternion previousHandRotation = Quaternion.identity;
                bool hasPrevious = false;
                int frames = Math.Max(1, animation.FrameCount > 0
                    ? animation.FrameCount
                    : Mathf.CeilToInt(animation.Clip.length * SampleRate));
                for (int frame = 0; frame < frames; frame++)
                {
                    animation.Clip.SampleAnimation(rig, frame / SampleRate);
                    Vector3 bladeOrigin = bone.TransformPoint(entry.PositionOffset);
                    Quaternion bladeRotation = bone.rotation * Quaternion.Euler(entry.RotationOffset);
                    Vector3 hilt = bladeOrigin + bladeRotation * points.hilt - rig.transform.position;
                    Vector3 tip = bladeOrigin + bladeRotation * points.tip - rig.transform.position;
                    Vector3 axis = (tip - hilt).normalized;
                    float tipStep = hasPrevious ? Vector3.Distance(previousTip, tip) : 0f;
                    float bladeAngleStep = hasPrevious && previousAxis.sqrMagnitude > 0.5f
                        ? Vector3.Angle(previousAxis, axis) : 0f;
                    float handAngleStep = hasPrevious
                        ? Quaternion.Angle(previousHandRotation, bone.rotation) : 0f;
                    writer.WriteLine(string.Join(",", packageId, Csv(animation.SemanticId), frame,
                        (frame / SampleRate).ToString("F4", CultureInfo.InvariantCulture),
                        F(hilt.x), F(hilt.y), F(hilt.z), F(tip.x), F(tip.y), F(tip.z),
                        F(Vector3.Distance(hilt, tip)), F(tipStep), F(bladeAngleStep), F(handAngleStep)));
                    previousTip = tip;
                    previousAxis = axis;
                    previousHandRotation = bone.rotation;
                    hasPrevious = true;
                }
            }

            Debug.Log($"[SwordMotionAudit] Wrote {path}. Metrics are 60 Hz, package-rig local, using the same weapon points as the cooker.");
            EditorUtility.RevealInFinder(path);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig);
        }
    }

    private static Transform FindBone(GameObject rig, string name)
    {
        var exact = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
        if (exact != null) return exact;
        if (name == "mixamorig:RightHand")
            return rig.GetComponent<Animator>()?.GetBoneTransform(HumanBodyBones.RightHand);
        return null;
    }

    private static (Vector3 tip, Vector3 hilt) DeriveWeaponPoints(GameObject prefab)
    {
        var vertices = new List<Vector3>();
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            Matrix4x4 toPrefab = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            vertices.AddRange(filter.sharedMesh.vertices.Select(toPrefab.MultiplyPoint3x4));
        }
        if (vertices.Count == 0) return (new Vector3(0f, 0f, 1.5f), Vector3.zero);
        Vector3 min = vertices[0], max = vertices[0];
        foreach (var point in vertices) { min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
        Vector3 extent = max - min;
        Vector3 axis = extent.x >= extent.y && extent.x >= extent.z ? Vector3.right
            : extent.y >= extent.z ? Vector3.up : Vector3.forward;
        Vector3 tip = vertices.OrderByDescending(x => Vector3.Dot(x, axis)).First();
        Vector3 hilt = vertices.OrderBy(x => Vector3.Dot(x, axis)).First();
        return (tip, hilt);
    }

    private static string F(float value) => value.ToString("F5", CultureInfo.InvariantCulture);
    private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
