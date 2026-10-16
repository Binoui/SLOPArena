using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

public class SetupArenaLighting : EditorWindow
{
    [MenuItem("Tools/SLOPArena/Prototypes/Setup Arena Lighting")]
    public static void Execute()
    {
        // Find and destroy existing directional lights
        var oldLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var l in oldLights)
        {
            if (l.type == LightType.Directional)
            {
                Undo.DestroyObjectImmediate(l.gameObject);
            }
        }

        // 1. KEY LIGHT — warm, soft shadows, 45° above, 30° off-center
        GameObject keyGO = new GameObject("Key Light");
        keyGO.transform.rotation = Quaternion.Euler(45, -30, 0);
        Light key = keyGO.AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1.0f, 0.95f, 0.85f);
        key.intensity = 1.3f;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.8f;
        Undo.RegisterCreatedObjectUndo(keyGO, "Create Key Light");

        // 2. FILL LIGHT — cool, opposite side, no shadows
        GameObject fillGO = new GameObject("Fill Light");
        fillGO.transform.rotation = Quaternion.Euler(35, 150, 0);
        Light fill = fillGO.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.65f, 0.75f, 1.0f);
        fill.intensity = 0.4f;
        fill.shadows = LightShadows.None;
        Undo.RegisterCreatedObjectUndo(fillGO, "Create Fill Light");

        // 3. RIM LIGHT — cool, behind, no shadows — character separation
        GameObject rimGO = new GameObject("Rim Light");
        rimGO.transform.rotation = Quaternion.Euler(20, 180, 0);
        Light rim = rimGO.AddComponent<Light>();
        rim.type = LightType.Directional;
        rim.color = new Color(0.55f, 0.70f, 1.0f);
        rim.intensity = 0.3f;
        rim.shadows = LightShadows.None;
        Undo.RegisterCreatedObjectUndo(rimGO, "Create Rim Light");

        // 4. Ensure URP Global Volume with ACES tonemapping
        var existingVolume = FindFirstObjectByType<Volume>();
        if (existingVolume == null)
        {
            GameObject volumeGO = new GameObject("PostProcessing Volume");
            var volume = volumeGO.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;

            var tonemapping = profile.Add<Tonemapping>(overrides: false);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            var colorAdj = profile.Add<ColorAdjustments>(overrides: false);
            colorAdj.contrast.overrideState = true;
            colorAdj.contrast.value = 10f;
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = 5f;

            Undo.RegisterCreatedObjectUndo(volumeGO, "Create PP Volume");
        }

        // 5. Fix camera clear color to match background
        var cams = FindObjectsByType<UnityEngine.Camera>(FindObjectsSortMode.None);
        if (cams.Length > 0)
        {
            Undo.RecordObject(cams[0], "Fix Camera Clear");
            cams[0].clearFlags = CameraClearFlags.Color;
            cams[0].backgroundColor = new Color(0.15f, 0.20f, 0.35f, 1.0f);
        }

        Debug.Log("[Arena Lighting] Done: Key(1.3 warm) + Fill(0.4 cool) + Rim(0.3 cool) + ACES tonemapping");
    }
}
