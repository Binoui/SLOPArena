using System;
using System.Collections.Generic;
using System.Reflection;
using SlopArena.Client.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace SlopArena.EditorTools
{
    public static class ControllerRumbleSelfTest
    {
        [MenuItem("Tools/SLOPArena/Tests/Controller Rumble Cleanup")]
        public static void Run()
        {
            var previousPad = Gamepad.current;
            var scene = Application.isPlaying ? default : EditorSceneManager.NewPreviewScene();
            var host = new GameObject("Controller rumble regression") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false); // Avoid creating the preferences singleton or changing saved settings.
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(host, scene);
            var owner = host.AddComponent<ClientSettingsService>();
            InputSystem.RegisterLayout<RecordingRumbleGamepad>();
            var pad = InputSystem.AddDevice<RecordingRumbleGamepad>();
            try
            {
                // A driver may reject a command without throwing: SetMotorSpeeds returns void.
                BeginPulse(owner, pad);
                pad.IgnoredStops = 1;
                StopAcrossUpdates(owner);
                Require(!pad.IsRumbling, "A missed stop command left controller rumble running indefinitely.");
                int stoppedCommands = pad.StopCommands;
                StopAcrossUpdates(owner);
                Require(pad.StopCommands == stoppedCommands, "Idle frames kept sending rumble commands after cleanup finished.");

                BeginPulse(owner, pad);
                InputSystem.RemoveDevice(pad);
                StopAcrossUpdates(owner);
                Require(!pad.IsRumbling, "Removing a controller discarded its pending rumble stop.");
                InputSystem.AddDevice(pad);

                BeginPulse(owner, pad);
                Invoke(owner, "OnDisable");
                Require(!pad.IsRumbling, "Disabling the settings owner left its controller rumbling.");

                BeginPulse(owner, pad);
                Invoke(owner, "OnApplicationPause", true);
                Require(!pad.IsRumbling, "Application suspension left its controller rumbling.");

                BeginPulse(owner, pad);
                Invoke(owner, "OnApplicationFocus", false);
                Require(!pad.IsRumbling, "Losing application focus left its controller rumbling.");

                Debug.Log("[ControllerRumbleSelfTest] Passed missed stop, removed controller, disable, suspension and focus cleanup.");
            }
            finally
            {
                pad.SetMotorSpeeds(0f, 0f);
                if (pad.added) InputSystem.RemoveDevice(pad);
                InputSystem.RemoveLayout(nameof(RecordingRumbleGamepad));
                if (previousPad != null && previousPad.added) previousPad.MakeCurrent();
                UnityEngine.Object.DestroyImmediate(host);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void BeginPulse(ClientSettingsService owner, RecordingRumbleGamepad pad)
        {
            // Seed only the result of an accepted hit; the real Update/cancellation paths do the stopping.
            // PlayConfirmedImpact targets every connected pad, so do not invoke it in an isolated Editor test.
            Invoke(owner, "StopRumble");
            Invoke(owner, "StopRumble");
            Invoke(owner, "StopRumble");
            pad.IgnoredStops = 0;
            pad.SetMotorSpeeds(0.18f, 0.32f);
            var pads = (List<Gamepad>)Field("_rumblingPads").GetValue(owner);
            pads.Add(pad);
            Field("_rumbleUntil").SetValue(owner, -1f);
            Field("_lastUiSceneHandle").SetValue(owner, SceneManager.GetActiveScene().handle);
        }

        private static void StopAcrossUpdates(ClientSettingsService owner)
        {
            for (int frame = 0; frame < 3; frame++) Invoke(owner, "Update");
        }

        private static FieldInfo Field(string name) => typeof(ClientSettingsService)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing settings field: " + name);

        private static void Invoke(ClientSettingsService owner, string name, params object[] arguments)
        {
            var method = typeof(ClientSettingsService).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new InvalidOperationException("Missing rumble lifecycle handler: " + name);
            method.Invoke(owner, arguments);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    [InputControlLayout(stateType = typeof(GamepadState))]
    public sealed class RecordingRumbleGamepad : Gamepad
    {
        public int IgnoredStops;
        public int StopCommands;
        public bool IsRumbling { get; private set; }

        public override void SetMotorSpeeds(float lowFrequency, float highFrequency)
        {
            bool stopping = lowFrequency == 0f && highFrequency == 0f;
            if (stopping) StopCommands++;
            if (stopping && IgnoredStops > 0) { IgnoredStops--; return; }
            IsRumbling = !stopping;
        }
    }
}
