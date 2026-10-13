using System;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools
{
    public static class SlopArenaLabViewOptionsSelfTest
    {
        [MenuItem("Tools/SlopArena/Tests/Lab View Options")]
        public static void Run()
        {
            Vector3 facing = Quaternion.Euler(0f, 37f, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, facing);
            string[] views = { "front", "back", "left", "right", "top", "bottom" };
            Vector3[] forwards = { -facing, facing, right, -right, Vector3.down, Vector3.up };
            for (int i = 0; i < views.Length; i++)
            {
                var options = Parse(views[i], null, null, null, null);
                Quaternion rotation = SlopArenaAbilityLabCommands.ResolveViewRotation(options, Quaternion.identity, facing);
                Check(Vector3.Dot(rotation * Vector3.forward, forwards[i]) > 0.99999f,
                    "View '" + views[i] + "' did not point along its actor-relative direction.");
                if (views[i] == "top" || views[i] == "bottom")
                    Check(Vector3.Dot(rotation * Vector3.up, facing) > 0.99999f,
                        "Vertical views must retain a stable actor-relative up direction.");
            }

            var custom = Parse(null, "-45", "20", null, null);
            Quaternion expected = Quaternion.Euler(20f, -45f, 0f);
            Quaternion actual = SlopArenaAbilityLabCommands.ResolveViewRotation(custom, Quaternion.identity, facing);
            Check(Quaternion.Angle(expected, actual) < 0.001f, "Absolute camera angles were not applied.");
            var yawOnly = Parse(null, "90", null, null, null);
            actual = SlopArenaAbilityLabCommands.ResolveViewRotation(yawOnly, Quaternion.Euler(25f, 10f, 0f), facing);
            Check(Quaternion.Angle(Quaternion.Euler(25f, 90f, 0f), actual) < 0.001f,
                "An omitted custom pitch must preserve the inherited pitch.");

            var none = Parse(null, null, null, "none", null);
            Check(none.OverlayMask == 0 && none.Dummy == false,
                "No-overlays capture must suppress the coupled dummy hurtbox overlay.");
            var selected = Parse(null, null, null, "hitboxes,bones", "off");
            Check(selected.OverlayMask == 5 && selected.Dummy == false,
                "Selected overlays must not enable unrequested layers.");
            Reject("front", "0", null, null, null);
            Reject("diagonal", null, null, null, null);
            Reject(null, "NaN", null, null, null);
            Reject(null, "Infinity", null, null, null);
            Reject(null, "181", null, null, null);
            Reject(null, null, "-91", null, null);
            Reject(null, null, null, "hitboxes,hitboxes", null);
            Reject(null, null, null, "unknown", null);
            Reject(null, null, null, "none", "on");
            Reject(null, null, null, null, "maybe");
            Debug.Log("Lab view options regression passed: actor-relative directions, vertical up, absolute angles, overlay selection and rejected inputs.");
        }

        private static SlopArenaAbilityLabCommands.CommandViewOptions Parse(
            string view, string yaw, string pitch, string overlays, string dummy)
        {
            if (!SlopArenaAbilityLabCommands.TryParseViewOptions(view, yaw, pitch, overlays, dummy, out var options, out string error))
                throw new InvalidOperationException(error);
            return options;
        }

        private static void Reject(string view, string yaw, string pitch, string overlays, string dummy)
        {
            if (SlopArenaAbilityLabCommands.TryParseViewOptions(view, yaw, pitch, overlays, dummy, out _, out _))
                throw new InvalidOperationException("Invalid view/overlay options were accepted.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
