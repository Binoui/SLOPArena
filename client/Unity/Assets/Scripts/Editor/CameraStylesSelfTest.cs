using System;
using System.Reflection;
using SlopArena.Client.Camera;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
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
            var host = EditorUtility.CreateGameObjectWithHideFlags("Camera framing test", HideFlags.HideAndDontSave,
                typeof(UnityEngine.Camera));
            var camera = host.GetComponent<UnityEngine.Camera>();
            camera.enabled = false;
            camera.fieldOfView = 40f;
            int projections = 0;
            try
            {
                VerifyDevicePreferences();
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

                float Assisted(float yaw, Vector3 offset) => (float)assist.Invoke(null,
                    new object[] { yaw, offset, 8f, 3f, 120f, 1f / 60f });
                Require(Assisted(37f, new Vector3(0.1f, 0f, -0.1f)) == 37f,
                    "Overlapping fighters must not flip the camera.");
                Require(Mathf.Abs(Mathf.DeltaAngle(0f, Assisted(0f, Vector3.right))) <= 2.001f,
                    "Automatic turning exceeded its speed limit.");
                Require(Assisted(179f, Quaternion.Euler(0f, -165f, 0f) * Vector3.forward) > 179f,
                    "Automatic turning did not take the short route across the yaw wrap.");
                SlopArenaSettingsCommandsSelfTest.Run();
                DefenseInputEdgesSelfTest.Run();
                Debug.Log($"[SLOPArena] Camera styles passed: {projections} safe-frame projections, Steady defaults, legacy migration, independent toggles, persistence, overlap, turn speed, yaw wrap, settings and defense validation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void VerifyDevicePreferences()
        {
            const string key = "sloparena.client-settings.v1";
            bool hadSave = PlayerPrefs.HasKey(key);
            string saved = PlayerPrefs.GetString(key);
            var host = new GameObject("Camera preference test") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);
            var settings = host.AddComponent<ClientSettingsService>();
            var payload = typeof(ClientSettingsService).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic);
            var load = typeof(ClientSettingsService).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic);
            void Load(string json)
            {
                payload.SetValue(settings, Activator.CreateInstance(payload.FieldType, true));
                PlayerPrefs.SetString(key, json);
                load.Invoke(settings, null);
            }
            try
            {
                Load("{\"version\":1}");
                Require(settings.ResolveCameraStyle(false) == CameraStyle.Steady &&
                    settings.ResolveCameraStyle(true) == CameraStyle.Steady,
                    "Fresh preferences did not default both devices to Steady.");
                // Saves from before ADR-0043 (no cameraRevision) move once to Steady, whatever they held.
                foreach (int legacy in new[] { 0, 1, 2, 3, 4, 5, 6, 99 })
                {
                    Load("{\"version\":1,\"cameraStyle\":" + legacy + ",\"controllerCameraStyle\":" + legacy + "}");
                    Require(settings.ResolveCameraStyle(false) == CameraStyle.Steady &&
                        settings.ResolveCameraStyle(true) == CameraStyle.Steady,
                        $"Legacy camera style {legacy} was not moved to Steady.");
                }
                Load("{\"version\":1,\"cameraRevision\":1,\"cameraStyle\":2,\"controllerCameraStyle\":0}");
                Require(settings.ResolveCameraStyle(false) == CameraStyle.Arena &&
                    settings.ResolveCameraStyle(true) == CameraStyle.Manual,
                    "A choice saved after the Steady migration was overwritten.");
                Load("{\"version\":1,\"cameraRevision\":1,\"cameraStyle\":5,\"controllerCameraStyle\":99,\"controllerAutoCameraStyle\":0}");
                Require(settings.ResolveCameraStyle(false) == CameraStyle.Steady &&
                    settings.ResolveCameraStyle(true) == CameraStyle.Steady &&
                    settings.ToggleCameraStyle(true) == CameraStyle.Manual &&
                    settings.ToggleCameraStyle(true) == CameraStyle.Steady,
                    "Removed or invalid saved camera styles were admitted.");
                Load("{\"version\":1,\"cameraRevision\":1,\"controllerCameraStyle\":-1,\"cameraStyle\":2}");
                Require(settings.ResolveCameraStyle(true) == CameraStyle.Arena,
                    "An unset controller style did not match the mouse style.");
                settings.SetCameraStyle(CameraStyle.Arena, true);
                Require(settings.ToggleCameraStyle(true) == CameraStyle.Manual &&
                    settings.ResolveCameraStyle(false) == CameraStyle.Arena,
                    "Controller toggle changed the mouse style.");
                Load(PlayerPrefs.GetString(key));
                Require(settings.ToggleCameraStyle(true) == CameraStyle.Arena,
                    "Controller toggle did not restore its remembered style after reload.");
                Require(settings.CameraTurnSpeed == 20f && settings.HoldRunDirection,
                    "Steady turn speed or hold-run default changed.");
                var toggle = HumanInputActions.Get("CameraToggle");
                var target = HumanInputActions.Get("ToggleLock");
                Require(toggle.bindings[HumanInputActions.BindingIndex("CameraToggle", HumanInputActions.KeyboardMouseGroup)].path == "<Keyboard>/y",
                    "Camera toggle lost the LoL keyboard default.");
                int hold = HumanInputActions.BindingIndex("CameraToggle", HumanInputActions.GamepadGroup);
                int tap = HumanInputActions.BindingIndex("ToggleLock", HumanInputActions.GamepadGroup);
                Require(toggle.bindings[hold].path == "<Gamepad>/rightStickPress" &&
                    toggle.bindings[hold].interactions == "hold(duration=0.45)" &&
                    target.bindings[tap].path == "<Gamepad>/rightStickPress" &&
                    target.bindings[tap].interactions == "tap(duration=0.45)",
                    "R3 tap and hold no longer share an explicit 0.45 second boundary.");
            }
            finally
            {
                if (hadSave) PlayerPrefs.SetString(key, saved);
                else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
