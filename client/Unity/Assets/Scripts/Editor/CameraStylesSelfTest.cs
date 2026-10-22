using System;
using System.Reflection;
using SlopArena.Client.Camera;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools
{
    public static class CameraStylesSelfTest
    {
        [MenuItem("Tools/SLOPArena/Tests/Camera Styles")]
        public static void Run()
        {
            var fit = typeof(CameraMount).GetMethod("DistanceToFrame", BindingFlags.Static | BindingFlags.NonPublic);
            var assist = typeof(CameraMount).GetMethod("GetAssistedYaw", BindingFlags.Static | BindingFlags.NonPublic);
            var fitLookAt = typeof(CameraMount).GetMethod("DistanceToFrameWithLookAt", BindingFlags.Static | BindingFlags.NonPublic);
            var host = EditorUtility.CreateGameObjectWithHideFlags("Camera framing test", HideFlags.HideAndDontSave,
                typeof(UnityEngine.Camera));
            var camera = host.GetComponent<UnityEngine.Camera>();
            camera.enabled = false;
            camera.fieldOfView = 40f;
            int projections = 0;
            try
            {
                foreach (float aspect in new[] { 16f / 10f, 16f / 9f, 1f })
                foreach (float yaw in new[] { 0f, 87f, 179f, -135f })
                foreach (float pitch in new[] { 0f, 20f, 35f, 45f })
                {
                    var bounds = new Bounds(new Vector3(3f, 7f, -2f), new Vector3(20f, 16f, 12f));
                    var anchor = bounds.center + new Vector3(-2f, 1f, 3f);
                    var rotation = Quaternion.Euler(pitch, yaw, 0f);
                    float distance = (float)fit.Invoke(null, new object[] { bounds, anchor, rotation, 40f, aspect });
                    camera.aspect = aspect;
                    camera.transform.SetPositionAndRotation(anchor - rotation * Vector3.forward * distance, rotation);
                    foreach (float x in new[] { bounds.min.x, bounds.max.x })
                    foreach (float y in new[] { bounds.min.y, bounds.max.y })
                    foreach (float z in new[] { bounds.min.z, bounds.max.z })
                    {
                        Vector3 viewport = camera.WorldToViewportPoint(new Vector3(x, y, z));
                        Require(viewport.z > 0f && viewport.x >= 0.0999f && viewport.x <= 0.9001f
                            && viewport.y >= 0.0999f && viewport.y <= 0.9001f,
                            $"Fighter bounds escaped the safe frame: {viewport}, aspect {aspect}, yaw {yaw}, pitch {pitch}.");
                        projections++;
                    }
                }

                foreach (float aspect in new[] { 16f / 10f, 16f / 9f, 1f })
                foreach (float yaw in new[] { 0f, 87f, 179f, -135f })
                foreach (float pitch in new[] { 5f, 8f, 20f })
                foreach (float height in new[] { 0f, 6f, 16f, -6f })
                foreach (bool shoulder in new[] { false, true })
                {
                    Vector3 player = new Vector3(2f, 10f, 3f);
                    Vector3 opponent = player + new Vector3(5f, height, 8f);
                    var bounds = new Bounds(player, Vector3.zero);
                    bounds.Encapsulate(opponent);
                    bounds.Expand(new Vector3(3f, 4f, 3f));
                    var rotation = Quaternion.Euler(pitch, yaw, 0f);
                    Vector3 anchor = shoulder ? player + rotation * Vector3.right * 1.1f : Vector3.Lerp(player, opponent, 0.5f);
                    anchor.y = player.y + (shoulder ? 1.5f : 0.75f);
                    Vector3 look = anchor + Vector3.up * height * (shoulder ? 0.65f : 0.5f);
                    float distance = (float)fitLookAt.Invoke(null,
                        new object[] { bounds, anchor, look, rotation, 40f, aspect, 9f, 120f });
                    Vector3 eye = anchor - rotation * Vector3.forward * distance;
                    Require(eye.y >= player.y + 0.7f, "Looking at an airborne opponent put the camera below the player's floor.");
                    camera.aspect = aspect;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                    foreach (float x in new[] { bounds.min.x, bounds.max.x })
                    foreach (float y in new[] { bounds.min.y, bounds.max.y })
                    foreach (float z in new[] { bounds.min.z, bounds.max.z })
                    {
                        Vector3 viewport = camera.WorldToViewportPoint(new Vector3(x, y, z));
                        Require(viewport.z > 0f && viewport.x >= 0.099f && viewport.x <= 0.901f
                            && viewport.y >= 0.099f && viewport.y <= 0.901f,
                            $"Vertical/shoulder focus lost a fighter: {viewport}, height {height}, yaw {yaw}, pitch {pitch}.");
                        projections++;
                    }
                }

                float Assisted(float yaw, Vector3 offset) => (float)assist.Invoke(null,
                    new object[] { yaw, offset, 8f, 3f, 120f, 1f / 60f });
                Require(Assisted(37f, new Vector3(0.1f, 0f, -0.1f)) == 37f,
                    "Overlapping fighters must not flip the camera.");
                Require(Mathf.Abs(Mathf.DeltaAngle(0f, Assisted(0f, Vector3.right))) <= 2.001f,
                    "Automatic turning exceeded its speed limit.");
                Require(Assisted(179f, Quaternion.Euler(0f, -165f, 0f) * Vector3.forward) > 179f,
                    "Automatic turning did not take the short route across the yaw wrap.");
                SlopArenaSettingsCommandsSelfTest.Run();
                Debug.Log($"[SLOPArena] Camera styles passed: {projections} safe-frame projections, overlap, turn speed, yaw wrap and settings validation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
