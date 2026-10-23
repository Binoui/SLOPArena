using System;
using System.Linq;
using System.Net;
using System.Reflection;
using SlopArena.Client.Input;
using SlopArena.Client.Network;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class UnityAuditInputSelfTest
    {
        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Input")]
        public static void Run()
        {
            RunChatGate();
            RunDevelopmentAddress();
        }

        public static void RunChatGate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run this check while the Editor is stopped.");
            var fields = typeof(ChatInputGate).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(bool) || field.FieldType == typeof(int)).ToArray();
            var saved = fields.Select(field => field.GetValue(null)).ToArray();
            var devices = InputSystem.devices.Where(device => device.enabled).ToArray();
            var previousKeyboard = Keyboard.current;
            var previousMouse = Mouse.current;
            var previousPad = Gamepad.current;
            bool previouslyGamepad = HumanInputActions.LastUsedGamepad;
            var previousSettings = InputSystem.settings;
            var testSettings = Object.Instantiate(previousSettings);
            Keyboard keyboard = null;
            Mouse mouse = null;
            try
            {
                foreach (var device in devices) InputSystem.DisableDevice(device);
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                InputSystem.settings = testSettings;
                testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
                foreach (var field in fields)
                    field.SetValue(null, field.FieldType == typeof(bool) ? (object)false : -1);

                ChatInputGate.Begin();
                ChatInputGate.End();
                SetFrame("_suppressThroughFrame");
                Require(ChatInputGate.SuppressGameplay && ChatInputGate.SuppressShortcuts,
                    "Reading a getter consumed the chat release latch.");
                Require((bool)Field("_releaseRequired").GetValue(null), "The chat getter changed its latch.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                InputSystem.Update();
                ChatInputGate.Tick();
                Require(ChatInputGate.SuppressGameplay, "A held typing key escaped the chat boundary.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                ChatInputGate.Tick();
                Require(!(bool)Field("_releaseRequired").GetValue(null), "Tick failed to release the idle chat latch.");

                ChatInputGate.BeginExternalOverlay();
                ChatInputGate.EndExternalOverlay();
                SetFrame("_externalSuppressThroughFrame");
                Require(ChatInputGate.ExternalOverlaySuppressed && ChatInputGate.ExternalOverlaySuppressed,
                    "Reading the external-overlay getter consumed its latch.");
                ChatInputGate.Tick();
                Require(!ChatInputGate.ExternalOverlaySuppressed, "Tick failed to release the external overlay.");
                Debug.Log("[UnityAuditInputSelfTest] Pure getters, held-key suppression and explicit release ticks passed.");
            }
            finally
            {
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]);
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                if (mouse != null) InputSystem.RemoveDevice(mouse);
                testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", false);
                InputSystem.settings = previousSettings;
                Object.DestroyImmediate(testSettings);
                foreach (var device in devices) if (device.added) InputSystem.EnableDevice(device);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousPad != null && previousPad.added) previousPad.MakeCurrent();
                typeof(HumanInputActions).GetProperty(nameof(HumanInputActions.LastUsedGamepad))
                    .SetValue(null, previouslyGamepad);
            }
        }

        public static void RunDevelopmentAddress()
        {
            var resolve = typeof(NetworkClient).GetMethod("ResolveDevelopmentEndpoint", BindingFlags.Static | BindingFlags.NonPublic);
            var literal = (IPEndPoint)resolve.Invoke(null, new object[] { "127.0.0.1", 9876 });
            var named = (IPEndPoint)resolve.Invoke(null, new object[] { "localhost", 9876 });
            Require(literal.Address.Equals(IPAddress.Loopback) && named.Address.Equals(IPAddress.Loopback) && named.Port == 9876,
                "Development DNS and numeric addresses did not resolve to the same IPv4 endpoint.");
            foreach (var invalid in new[] { ("", 9876), ("::1", 9876), ("127.0.0.1", 65536) })
            {
                bool rejected = false;
                try { resolve.Invoke(null, new object[] { invalid.Item1, invalid.Item2 }); }
                catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { rejected = true; }
                Require(rejected, "Unsupported development endpoint was admitted.");
            }
            Debug.Log("[UnityAuditInputSelfTest] Development IPv4/DNS resolution and invalid endpoint rejection passed.");
        }

        private static FieldInfo Field(string name)
            => typeof(ChatInputGate).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);

        private static void SetFrame(string name) => Field(name).SetValue(null, -1);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
