using System;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools
{
    public static class SlopArenaSettingsCommandsSelfTest
    {
        [MenuItem("Tools/SLOPArena/Tests/Supported Settings Command Validation")]
        public static void Run()
        {
            RequireValid("{\"uiScale\":80,\"targetOpacity\":20,\"screenShake\":0,\"showOverheadDamage\":false,\"reducedFlashing\":true}");
            RequireValid("{\"uiScale\":140,\"targetOpacity\":100,\"screenShake\":100}");
            RequireValid("{\"recordSessionLog\":true}");
            RequireInvalid(null);
            RequireInvalid("{}");
            RequireInvalid("[]");
            RequireInvalid("{\"uiScale\":79}");
            RequireInvalid("{\"uiScale\":81}");
            RequireInvalid("{\"uiScale\":139}");
            RequireInvalid("{\"uiScale\":80.5}");
            RequireInvalid("{\"targetOpacity\":19}");
            RequireInvalid("{\"screenShake\":101}");
            RequireInvalid("{\"screenShake\":NaN}");
            RequireInvalid("{\"showOverheadDamage\":1}");
            RequireInvalid("{\"reducedFlashing\":\"true\"}");
            RequireInvalid("{\"recordSessionLog\":1}");
            RequireInvalid("{\"uiScale\":100,\"unknown\":true}");
            RequireInvalid("{\"uiScale\":100,\"uiScale\":110}");
            RequireInvalid("{\"uiScale\":100} trailing");
            Debug.Log("[SLOPArena] Supported settings command validation self-test passed.");
        }

        private static void RequireValid(string json)
        {
            if (!SlopArenaSettingsCommands.TryParsePatch(json, out _, out string error))
                throw new InvalidOperationException("Expected valid settings patch to parse: " + error);
        }

        private static void RequireInvalid(string json)
        {
            if (SlopArenaSettingsCommands.TryParsePatch(json, out _, out _))
                throw new InvalidOperationException("Invalid settings patch was accepted: " + json);
        }
    }
}
